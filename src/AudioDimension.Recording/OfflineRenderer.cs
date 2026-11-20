using System.Collections.Concurrent;
using System.Diagnostics;
using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Visualizers;
using NAudio.Wave;
using Serilog;

namespace AudioDimension.Recording;

public interface IDiskSpace { long FreeBytes(string path); }

public sealed class SystemDiskSpace : IDiskSpace
{
    public long FreeBytes(string path)
    {
        try { var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path)); return new DriveInfo(root!).AvailableFreeSpace; }
        catch (Exception) { return long.MaxValue; }
    }
}

/// <summary>Everything an offline render needs, captured up front so later edits in the UI cannot change a running export.</summary>
public sealed class ExportJob
{
    public required ExportOptions Options { get; init; }
    public required VisualConfig Config { get; init; }
    public required string VisualizerId { get; init; }
    public required AudioParams AudioParams { get; init; }
    public required string AudioPath { get; init; }
}

/// <summary>
/// Deterministic offline pipeline: audio file -> analysis at exactly 1/fps steps -> OpenGL render -> raw frames piped to FFmpeg, which also
/// muxes the audio. Because every frame is computed from the audio timeline (not from wall-clock time) it does not matter whether rendering
/// runs faster or slower than real time, and text animations land on exact frame times.
/// </summary>
public static class OfflineRenderer
{
    public static IDiskSpace Disk { get; set; } = new SystemDiskSpace();

    /// <summary>Runs on the calling thread (it creates its own GL context there); call it from a worker thread.</summary>
    public static ExportResult Render(ExportJob job, IProgress<ExportProgress>? progress, CancellationToken ct)
    {
        var o = job.Options;
        var problems = o.Validate();
        if (problems.Count > 0) throw new ExportException(string.Join(" ", problems));
        var ff = FfmpegLocator.Require();
        var caps = EncoderCapabilities.Probe(ff.Path);
        string encoder = FfmpegArgs.ResolveEncoder(o.Format, o.Encoder, caps);

        if (!File.Exists(job.AudioPath)) throw new ExportException("The recorded audio could not be found, so the video cannot be rendered.");
        using var reader = OpenAudio(job.AudioPath);
        double duration = reader.TotalTime.TotalSeconds;
        if (duration < 0.2) throw new ExportException("The recording is too short to export (under 0.2 seconds).");
        long totalFrames = (long)Math.Ceiling(duration * o.Fps);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(o.OutputPath));
        try { Directory.CreateDirectory(dir!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new ExportException($"The output folder '{dir}' cannot be used: {ex.Message}", ex); }
        long need = (long)(o.EstimateBytes(duration) * 1.3) + 50_000_000;
        long free = Disk.FreeBytes(o.OutputPath);
        if (free < need)
            throw new ExportException($"Not enough disk space for this export. About {need / 1048576} MB is needed but only {free / 1048576} MB is free on that drive.");

        var args = FfmpegArgs.Build(o, encoder, o.IncludeAudio ? job.AudioPath : null);
        var psi = new ProcessStartInfo(ff.Path) { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Log.Information("Export starting: {Fmt} {W}x{H}@{Fps} via {Enc}, {Frames} frames", o.Format, o.EvenWidth, o.EvenHeight, o.Fps, encoder, totalFrames);

        var sw = Stopwatch.StartNew();
        Process? proc = null;
        var stderr = new System.Text.StringBuilder();
        bool finished = false;
        BlockingCollection<byte[]>? full = null;
        try
        {
            proc = Process.Start(psi) ?? throw new ExportException("FFmpeg could not be started.");
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) { stderr.AppendLine(e.Data); } };
            proc.BeginErrorReadLine();
            proc.StandardOutput.BaseStream.CopyToAsync(Stream.Null);

            using var ctx = new OffscreenContext();
            using var renderer = new VisualRenderer();
            int w = o.EvenWidth, h = o.EvenHeight;
            using var target = new OffscreenTarget(w, h);

            var cfg = job.Config.Clone();
            cfg.Quality.RenderScale = Math.Max(1.0, cfg.Quality.RenderScale);        // exports always render at full resolution
            var viz = VisualizerRegistry.Create(job.VisualizerId);
            viz.Reset(); renderer.ResetTime(); renderer.ScaleMultiplier = 1.0;
            var analyzer = new AudioAnalyzer(reader.WaveFormat.SampleRate, job.AudioParams.Clone());
            int ch = reader.WaveFormat.Channels; double framesPerVideoFrame = (double)reader.WaveFormat.SampleRate / o.Fps; double carry = 0;
            var samples = new float[(int)Math.Ceiling(framesPerVideoFrame) * ch + ch];
            var mono = new float[(int)Math.Ceiling(framesPerVideoFrame) + 1];
            double dt = 1.0 / o.Fps;

            // overlap GPU readback with the pipe write: a small ring of buffers handed to a writer thread
            var free2 = new BlockingCollection<byte[]>(); full = new BlockingCollection<byte[]>(3);
            for (int i = 0; i < 4; i++) free2.Add(new byte[w * h * 4]);
            Exception? writeError = null;
            var writer = new Thread(() =>
            {
                try
                {
                    var stdin = proc.StandardInput.BaseStream;
                    foreach (var buf in full.GetConsumingEnumerable()) { stdin.Write(buf, 0, buf.Length); free2.Add(buf); }
                    stdin.Flush(); stdin.Close();
                }
                catch (Exception ex) { writeError = ex; try { while (full.TryTake(out var b, 0)) free2.Add(b); } catch { } }
            }) { IsBackground = true, Name = "ExportPipe" };
            writer.Start();

            long lastReport = 0;
            for (long k = 0; k < totalFrames; k++)
            {
                ct.ThrowIfCancellationRequested();
                if (writeError is not null || proc.HasExited) break;

                carry += framesPerVideoFrame; int want = (int)carry; carry -= want;
                int got = ((ISampleProvider)reader).Read(samples.AsSpan(0, Math.Min(want * ch, samples.Length))) / ch;
                for (int i = 0; i < got; i++) { float s = 0; for (int c = 0; c < ch; c++) s += samples[i * ch + c]; mono[i] = s / ch; }
                for (int i = got; i < want; i++) mono[i] = 0;                            // past the end of the audio: silence
                analyzer.Push(mono.AsSpan(0, want));
                var frame = analyzer.Analyze(dt);

                renderer.TextTime = k * dt;
                renderer.Render(viz, cfg, frame, dt, target.Fbo, w, h);
                if (renderer.LastError is { } err) throw new ExportException("Rendering failed: " + err);

                byte[] buf;
                while (!free2.TryTake(out buf!, 100)) { ct.ThrowIfCancellationRequested(); if (writeError is not null || proc.HasExited) break; }
                if (buf is null) break;
                target.ReadPixelsInto(buf);
                while (!full.TryAdd(buf, 100)) { ct.ThrowIfCancellationRequested(); if (writeError is not null || proc.HasExited) break; }

                if (k + 1 - lastReport >= 5 || k + 1 == totalFrames)
                {
                    lastReport = k + 1;
                    double rate = (k + 1) / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    var remaining = k + 1 >= 10 ? TimeSpan.FromSeconds((totalFrames - k - 1) / Math.Max(rate, 0.001)) : (TimeSpan?)null;
                    progress?.Report(new ExportProgress("Rendering", k + 1, totalFrames, sw.Elapsed, remaining, rate));
                }
            }
            full.CompleteAdding();
            writer.Join();
            progress?.Report(new ExportProgress("Finishing", totalFrames, totalFrames, sw.Elapsed, TimeSpan.Zero, 0));
            if (!proc.WaitForExit(120000)) throw new ExportException("The encoder did not finish in time.");
            proc.WaitForExit();                       // flush async stderr
            string errText; lock (stderr) errText = stderr.ToString().Trim();
            if (proc.ExitCode != 0 || (writeError is not null && proc.ExitCode != 0))
            {
                Log.Error("FFmpeg failed (exit {Code}): {Err}", proc.ExitCode, errText);
                throw new ExportException("The video encoder failed" + (errText.Length > 0 ? ": " + Tail(errText) : "."), writeError);
            }
            if (writeError is not null && errText.Length > 0) throw new ExportException("Writing to the encoder failed: " + Tail(errText), writeError);
            var fi = new FileInfo(o.OutputPath);
            if (!fi.Exists || fi.Length == 0) throw new ExportException("The encoder reported success but produced no file.");
            finished = true;
            Log.Information("Export finished: {Path} {Size} bytes in {Sec:0.0}s", o.OutputPath, fi.Length, sw.Elapsed.TotalSeconds);
            return new ExportResult(o.OutputPath, totalFrames, duration, fi.Length, sw.Elapsed, encoder);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Export cancelled");
            throw;
        }
        catch (ExportException) { throw; }
        catch (IOException ex) { throw new ExportException("A file operation failed during export: " + ex.Message, ex); }
        finally
        {
            try { full?.CompleteAdding(); } catch (ObjectDisposedException) { }     // lets the pipe-writer thread exit on cancel/error
            if (proc is not null)
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch (InvalidOperationException) { }
                proc.Dispose();
            }
            if (!finished) TryDelete(o.OutputPath);          // never leave a half-written video behind
        }
    }

    private static AudioFileReader OpenAudio(string path)
    {
        try { return new AudioFileReader(path); }
        catch (Exception ex) { throw new ExportException("The audio file could not be read: " + ex.Message, ex); }
    }

    private static string Tail(string s) { var lines = s.Split('\n'); return string.Join(" ", lines.Skip(Math.Max(0, lines.Length - 4))).Trim(); }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
