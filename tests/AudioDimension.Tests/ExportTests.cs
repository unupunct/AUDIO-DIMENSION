using System.Globalization;
using System.Text.RegularExpressions;
using AudioDimension.Audio.Analysis;
using AudioDimension.Audio.Capture;
using AudioDimension.Audio.Devices;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using AudioDimension.Recording;
using AudioDimension.Visuals.Config;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace AudioDimension.Tests;

/// <summary>Helpers shared by the export tests: synthetic WAVs and ffmpeg based inspection of the produced video.</summary>
internal static class Media
{
    public static string Ffmpeg => FfmpegLocator.Require().Path;

    /// <summary>WAV made of (seconds, amplitude) segments; non-zero segments are a 440 Hz tone plus a 60 Hz bass.</summary>
    public static string MakeWav(string path, params (double seconds, double amp)[] segments)
    {
        const int sr = 44100;
        using var w = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sr, 2));
        long pos = 0;
        foreach (var (sec, amp) in segments)
        {
            int n = (int)(sec * sr); var buf = new float[n * 2];
            for (int i = 0; i < n; i++, pos++)
            {
                float v = amp == 0 ? 0f : (float)(amp * (0.6 * Math.Sin(2 * Math.PI * 440 * pos / sr) + 0.5 * Math.Sin(2 * Math.PI * 60 * pos / sr)));
                buf[i * 2] = v; buf[i * 2 + 1] = v;
            }
            w.WriteSamples(buf, 0, buf.Length);
        }
        return path;
    }

    public static string Info(string file)
    {
        var r = ProcessRunner.Run(Ffmpeg, new[] { "-hide_banner", "-i", file }, 20000);
        return r.Stderr;       // ffmpeg prints stream info to stderr and exits 1 because there is no output file
    }

    public static bool DecodesCleanly(string file)
    {
        var r = ProcessRunner.Run(Ffmpeg, new[] { "-v", "error", "-i", file, "-f", "null", "-" }, 60000);
        return r.ExitCode == 0 && string.IsNullOrWhiteSpace(r.Stderr);
    }

    public static double Duration(string file)
    {
        var m = Regex.Match(Info(file), @"Duration: (\d+):(\d+):(\d+\.\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : -1;
    }

    /// <summary>Mean luminance of every video frame at 32x18 (so brightness over time can be compared with the audio).</summary>
    public static List<double> FrameBrightness(string file)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-v", "error", "-i", file, "-vf", "scale=32:18,format=gray", "-f", "rawvideo", "-pix_fmt", "gray", "-" }) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        using var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
        var all = ms.ToArray(); var list = new List<double>();
        for (int o = 0; o + 576 <= all.Length; o += 576) { double s = 0; for (int i = 0; i < 576; i++) s += all[o + i]; list.Add(s / 576); }
        return list;
    }

    /// <summary>Decoded mono audio at 8 kHz from the finished video file.</summary>
    public static float[] DecodeAudio(string file)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-v", "error", "-i", file, "-vn", "-ac", "1", "-ar", "8000", "-f", "f32le", "-" }) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        using var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); p.WaitForExit();
        var bytes = ms.ToArray(); var f = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, f, 0, f.Length * 4); return f;
    }

    public static ExportJob Job(string wav, ExportOptions o, string viz = "classic-spectrum", VisualConfig? cfg = null) => new()
    {
        Options = o, Config = cfg ?? new VisualConfig(), VisualizerId = viz, AudioParams = new AudioParams { AutoGain = false }, AudioPath = wav
    };

    public static ExportOptions Opt(string output, int w = 320, int h = 180, int fps = 30, VideoFormat f = VideoFormat.Mp4H264, bool audio = true) => new()
    {
        Format = f, Width = w, Height = h, Fps = fps, Quality = 60, Encoder = EncoderPreference.Software, IncludeAudio = audio, OutputPath = output
    };

    public static ExportResult RenderSync(ExportJob job, IProgress<ExportProgress>? p = null, CancellationToken ct = default) =>
        Task.Run(() => OfflineRenderer.Render(job, p, ct)).GetAwaiter().GetResult();
}

public sealed class ExportFilesFixture : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ad-export-" + Guid.NewGuid().ToString("N"));
    public ExportFilesFixture() => Directory.CreateDirectory(Dir);
    public string P(string name) => Path.Combine(Dir, name);
    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
}

public class ExportPureTests
{
    [Fact]
    public void BundledFfmpegIsFoundAndRuns()
    {
        var ff = FfmpegLocator.Require();
        Assert.True(File.Exists(ff.Path)); Assert.StartsWith("n9", ff.Version);
    }

    [Fact]
    public void EncoderProbeSeesTheSoftwareEncodersWeShip()
    {
        var caps = EncoderCapabilities.Probe(FfmpegLocator.Require().Path);
        foreach (var e in new[] { "libx264", "libx265", "libvpx-vp9", "aac", "libopus" }) Assert.Contains(e, caps.Working);
    }

    [Fact]
    public void ArgumentsSelectTheRightCodecForEachFormat()
    {
        var caps = new EncoderCapabilities();
        string Enc(VideoFormat f, EncoderPreference p) => FfmpegArgs.ResolveEncoder(f, p, caps);
        Assert.Equal("libx264", Enc(VideoFormat.Mp4H264, EncoderPreference.Auto));          // no working hardware encoders -> software fallback
        Assert.Equal("libx265", Enc(VideoFormat.Mp4Hevc, EncoderPreference.Nvenc));
        Assert.Equal("libvpx-vp9", Enc(VideoFormat.WebM, EncoderPreference.Auto));
        var hw = new EncoderCapabilities { Working = new HashSet<string> { "h264_nvenc", "hevc_nvenc", "h264_amf" } };
        Assert.Equal("h264_nvenc", FfmpegArgs.ResolveEncoder(VideoFormat.Mp4H264, EncoderPreference.Auto, hw));
        Assert.Equal("hevc_nvenc", FfmpegArgs.ResolveEncoder(VideoFormat.Mp4Hevc, EncoderPreference.Nvenc, hw));
        Assert.Equal("h264_amf", FfmpegArgs.ResolveEncoder(VideoFormat.Mp4H264, EncoderPreference.Amf, hw));
        Assert.Equal("libx264", FfmpegArgs.ResolveEncoder(VideoFormat.Mp4H264, EncoderPreference.QuickSync, hw));   // not available -> fallback

        var o = new ExportOptions { Width = 1921, Height = 1081, Fps = 50, OutputPath = "out.mp4" };
        var a = FfmpegArgs.Build(o, "libx264", "a.wav");
        Assert.Contains("1920x1080", a); Assert.Contains("libx264", a); Assert.Contains("aac", a); Assert.Contains("+faststart", a); Assert.Contains("-shortest", a);
        Assert.Equal("out.mp4", a[^1]);
        var w = FfmpegArgs.Build(new ExportOptions { Format = VideoFormat.WebM, OutputPath = "o.webm" }, "libvpx-vp9", "a.wav");
        Assert.Contains("libopus", w); Assert.DoesNotContain("+faststart", w);
        var h = FfmpegArgs.Build(new ExportOptions { Format = VideoFormat.Mp4Hevc, OutputPath = "o.mp4" }, "libx265", "a.wav");
        Assert.Contains("hvc1", h);
        var silent = FfmpegArgs.Build(new ExportOptions { IncludeAudio = false, OutputPath = "o.mp4" }, "libx264", "a.wav");
        Assert.Contains("-ac", a);
        Assert.Contains("-an", silent); Assert.DoesNotContain("a.wav", silent);
    }

    [Fact]
    public void QualityMapsMonotonicallyAndEstimatesScale()
    {
        Assert.True(FfmpegArgs.CrfFor(100) < FfmpegArgs.CrfFor(50)); Assert.True(FfmpegArgs.CrfFor(50) < FfmpegArgs.CrfFor(1));
        var lo = new ExportOptions { Quality = 20 }; var hi = new ExportOptions { Quality = 95 };
        Assert.True(hi.EstimateBytes(60) > lo.EstimateBytes(60) * 2);
        var big = new ExportOptions { Width = 3840, Height = 2160 }; var small = new ExportOptions { Width = 1280, Height = 720 };
        Assert.True(big.EstimateBytes(60) > small.EstimateBytes(60) * 5);
        Assert.True(new ExportOptions { Format = VideoFormat.Mp4Hevc }.EstimateBytes(60) < new ExportOptions { Format = VideoFormat.Mp4H264 }.EstimateBytes(60));
        Assert.Equal(0, new ExportOptions().EstimateBytes(0));
    }

    [Fact]
    public void OptionsValidationCatchesBadInput()
    {
        Assert.NotEmpty(new ExportOptions { OutputPath = "" }.Validate());
        Assert.NotEmpty(new ExportOptions { OutputPath = "x", Fps = 0 }.Validate());
        Assert.NotEmpty(new ExportOptions { OutputPath = "x", Width = 10 }.Validate());
        Assert.NotEmpty(new ExportOptions { OutputPath = "x", Quality = 500 }.Validate());
        Assert.Empty(new ExportOptions { OutputPath = "x" }.Validate());
        foreach (var fps in ExportOptions.StandardFps) Assert.Empty(new ExportOptions { OutputPath = "x", Fps = fps }.Validate());
        Assert.Equal(new[] { 24, 25, 30, 50, 60 }, ExportOptions.StandardFps);
        Assert.Equal(new[] { 1280, 1920, 2560, 3840 }, ExportOptions.StandardSizes.Select(s => s.W));
    }
}

public sealed class WavWriterTests : IClassFixture<ExportFilesFixture>
{
    private readonly ExportFilesFixture _fx;
    public WavWriterTests(ExportFilesFixture fx) => _fx = fx;

    private static byte[] Chunk(int frames, int channels = 2) => new byte[frames * channels * 4];

    [Fact]
    public void FillsSilenceWhereTheDeviceDeliversNothing()
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        using var w = new TimelineWavWriter(_fx.P("gap.wav"), fmt);
        w.Write(Chunk(4800), 4800 * 8);                 // 0.1 s of audio
        Thread.Sleep(600);                              // loopback went quiet: nothing arrives
        w.Write(Chunk(4800), 4800 * 8);
        double secs = w.Finish();
        Assert.InRange(secs, 0.55, 0.8);                // timeline length (~0.6 s elapsed), not just the 0.2 s that was delivered
        Assert.InRange(new FileInfo(_fx.P("gap.wav")).Length, secs * 48000 * 8 * 0.97, secs * 48000 * 8 * 1.03 + 100);
    }

    [Fact]
    public void PausedTimeIsCutOut()
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        using var w = new TimelineWavWriter(_fx.P("pause.wav"), fmt);
        for (int i = 0; i < 5; i++) { w.Write(Chunk(4800), 4800 * 8); Thread.Sleep(100); }
        w.Pause(); Assert.True(w.IsPaused);
        double before = w.DurationSeconds;
        Thread.Sleep(700); w.Write(Chunk(4800), 4800 * 8);     // ignored while paused
        Assert.Equal(before, w.DurationSeconds, 3);
        w.Resume();
        for (int i = 0; i < 5; i++) { Thread.Sleep(100); w.Write(Chunk(4800), 4800 * 8); }
        double total = w.Finish();
        Assert.InRange(total, 0.8, 1.45);                       // ~1 s of recorded time; the 0.7 s pause is not in the file
    }

    [Fact]
    public void WritesAValidWavThatCanBeReadBack()
    {
        var fmt = new WaveFormat(44100, 16, 1);
        using (var w = new TimelineWavWriter(_fx.P("pcm.wav"), fmt)) { w.Write(new byte[44100 * 2 / 2], 44100); Thread.Sleep(50); w.Finish(); }
        using var r = new AudioFileReader(_fx.P("pcm.wav"));
        Assert.Equal(44100, r.WaveFormat.SampleRate); Assert.True(r.TotalTime.TotalSeconds > 0.4);
    }

    [Fact]
    public void ExtensibleDeviceFormatsAreNormalised()
    {
        var ext = new WaveFormatExtensible(48000, 32, 2);   // PCM 32-bit extensible
        var n = TimelineWavWriter.Normalise(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Assert.Equal(WaveFormatEncoding.IeeeFloat, n.Encoding);
        Assert.NotNull(TimelineWavWriter.Normalise(ext));
    }
}

[Collection("gl")]
public sealed class VideoExportTests : IClassFixture<ExportFilesFixture>
{
    private readonly ExportFilesFixture _fx;
    public VideoExportTests(ExportFilesFixture fx) => _fx = fx;

    [Fact]
    public void ExportsAPlayableH264Mp4WithAudio()
    {
        var wav = Media.MakeWav(_fx.P("a1.wav"), (0.5, 0), (1.0, 0.5), (0.5, 0));
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("h264.mp4"))));
        Assert.True(File.Exists(res.Path)); Assert.True(res.SizeBytes > 5000);
        var info = Media.Info(res.Path);
        Assert.Matches(@"Video: h264", info); Assert.Matches(@"320x180", info); Assert.Matches(@"Audio: aac", info);
        Assert.InRange(Media.Duration(res.Path), 1.9, 2.15);
        Assert.True(Media.DecodesCleanly(res.Path), "the produced MP4 must decode without errors");
        Assert.Equal(60, res.Frames);
    }

    [Theory]
    [InlineData(VideoFormat.Mp4Hevc, "Video: hevc", "Audio: aac", "h265")]
    [InlineData(VideoFormat.WebM, "Video: vp9", "Audio: opus", "webm")]
    public void ExportsHevcAndWebM(VideoFormat fmt, string video, string audio, string tag)
    {
        var wav = Media.MakeWav(_fx.P($"a-{tag}.wav"), (0.3, 0), (0.6, 0.5));
        var o = Media.Opt(_fx.P($"out-{tag}" + (fmt == VideoFormat.WebM ? ".webm" : ".mp4")), f: fmt);
        var res = Media.RenderSync(Media.Job(wav, o));
        var info = Media.Info(res.Path);
        Assert.Contains(video, info); Assert.Contains(audio, info);
        Assert.True(Media.DecodesCleanly(res.Path));
    }

    [Theory]
    [InlineData(24)] [InlineData(25)] [InlineData(30)] [InlineData(50)] [InlineData(60)]
    public void SupportsAllRequestedFrameRates(int fps)
    {
        var wav = Media.MakeWav(_fx.P($"fps{fps}.wav"), (1.0, 0.4));
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P($"fps{fps}.mp4"), 256, 144, fps)));
        Assert.Equal(fps, res.Frames);
        var m = Regex.Match(Media.Info(res.Path), @"(\d+(?:\.\d+)?) fps");
        Assert.True(m.Success, "fps should be reported");
        Assert.Equal(fps, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 0.5);
        Assert.InRange(Media.FrameBrightness(res.Path).Count, fps - 1, fps + 1);
    }

    [Theory]
    [InlineData(1280, 720)] [InlineData(854, 480)] [InlineData(3840, 2160)] [InlineData(1000, 1000)]
    public void SupportsStandardAndCustomResolutions(int w, int h)
    {
        var wav = Media.MakeWav(_fx.P($"res{w}.wav"), (0.4, 0.5));
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P($"res{w}x{h}.mp4"), w, h, 24)));
        Assert.Contains($"{w}x{h}", Media.Info(res.Path));
        Assert.True(Media.DecodesCleanly(res.Path));
    }

    [Fact]
    public void OddResolutionsAreRoundedDownToEven()
    {
        var wav = Media.MakeWav(_fx.P("odd.wav"), (0.4, 0.5));
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("odd.mp4"), 641, 361)));
        Assert.Contains("640x360", Media.Info(res.Path));
    }

    [Fact]
    public void CanExcludeAudioFromTheVideo()
    {
        var wav = Media.MakeWav(_fx.P("noaudio.wav"), (0.6, 0.5));
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("silent.mp4"), audio: false)));
        Assert.DoesNotContain("Audio:", Media.Info(res.Path)); Assert.Contains("Video:", Media.Info(res.Path));
    }

    [Fact]
    public void VideoAndAudioAreSynchronised()
    {
        // 1.0 s silence, 1.0 s loud, 1.0 s silence. The onset must show up in the picture and in the sound at the same time.
        var wav = Media.MakeWav(_fx.P("sync.wav"), (1.0, 0), (1.0, 0.7), (1.0, 0));
        var o = Media.Opt(_fx.P("sync.mp4"), 320, 180, 30);
        var res = Media.RenderSync(Media.Job(wav, o, "classic-spectrum"));

        var b = Media.FrameBrightness(res.Path);
        double baseline = b.Take(20).Average();
        int onsetFrame = b.FindIndex(v => v > baseline + 3);
        Assert.True(onsetFrame > 0, $"video never reacted (baseline {baseline:0.0}, max {b.Max():0.0})");
        double videoOnset = onsetFrame / 30.0;

        var a = Media.DecodeAudio(res.Path);
        int onsetSample = Array.FindIndex(a, v => Math.Abs(v) > 0.05f);
        Assert.True(onsetSample > 0, "decoded audio is silent");
        double audioOnset = onsetSample / 8000.0;

        Assert.InRange(audioOnset, 0.9, 1.15);
        Assert.InRange(videoOnset, 0.9, 1.25);
        Assert.True(Math.Abs(videoOnset - audioOnset) < 0.12, $"A/V offset too large: video {videoOnset:0.000}s vs audio {audioOnset:0.000}s");

        // the soundtrack itself is silent again after the burst, so the muxed audio really ends where the source did
        int lastLoud = Array.FindLastIndex(a, v => Math.Abs(v) > 0.05f);
        Assert.InRange(lastLoud / 8000.0, 1.9, 2.2);
    }

    [Fact]
    public void TextAnimationsLandOnExactFrameTimes()
    {
        var wav = Media.MakeWav(_fx.P("txt.wav"), (3.0, 0));
        var cfg = new VisualConfig(); cfg.Background.Mode = BackgroundMode.Solid; cfg.Background.Color1 = "#000000"; cfg.Effects.Bloom = false; cfg.Effects.Vignette = false;
        var layer = new AudioDimension.Visuals.Text.TextLayer { Text = "SYNC", FontSize = 520, FontWeight = 800, StartTime = 1.5, Y = 0.5 };
        cfg.TextLayers.Add(layer);
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("txt.mp4"), 320, 180, 30, audio: false), "neon-equalizer", cfg));
        var b = Media.FrameBrightness(res.Path);
        double baseline = b.Take(10).Average();
        int first = b.FindIndex(v => v > baseline + 3);
        Assert.InRange(first, 44, 47);     // text appears at t = 1.5 s -> frame 45
    }

    [Fact]
    public void ReportsMonotonicProgressWithEta()
    {
        var wav = Media.MakeWav(_fx.P("prog.wav"), (2.0, 0.4));
        var events = new List<ExportProgress>();
        var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("prog.mp4"), 320, 180, 30)), new Progress<ExportProgress>(events.Add));
        Thread.Sleep(200);
        var list = events.ToList();
        Assert.NotEmpty(list);
        var frames = list.Where(e => e.Stage == "Rendering").Select(e => e.Frame).ToList();
        Assert.Equal(frames.OrderBy(x => x), frames);
        Assert.Equal(res.Frames, frames.Max());
        Assert.Contains(list, e => e.Remaining is not null); Assert.Contains(list, e => e.RenderFps > 0);
        Assert.Equal(1.0, list.Last().Fraction, 3);
    }

    [Fact]
    public void CancellingStopsFfmpegAndRemovesTheHalfWrittenFile()
    {
        var wav = Media.MakeWav(_fx.P("long.wav"), (40.0, 0.4));            // 1200 frames - far longer than we allow it to run
        var cts = new CancellationTokenSource();
        var output = _fx.P("cancelled.mp4");
        int reported = 0;
        var progress = new Progress<ExportProgress>(p => { if (p.Frame > 15 && Interlocked.Increment(ref reported) == 1) cts.Cancel(); });
        var task = Task.Run(() => OfflineRenderer.Render(Media.Job(wav, Media.Opt(output, 640, 360, 30)), progress, cts.Token));
        var ex = Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        Assert.NotNull(ex);
        Thread.Sleep(500);
        Assert.False(File.Exists(output), "a cancelled export must not leave a partial video");
        Assert.Empty(System.Diagnostics.Process.GetProcessesByName("ffmpeg").Where(p => { try { return p.StartTime > DateTime.Now.AddSeconds(-20) && !p.HasExited; } catch { return false; } }));
    }

    [Fact]
    public void MissingFfmpegGivesAReadableErrorNotACrash()
    {
        var wav = Media.MakeWav(_fx.P("noff.wav"), (0.5, 0.5));
        FfmpegLocator.FinderOverride = () => null;
        try
        {
            var ex = Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("noff.mp4")))));
            Assert.Contains("FFmpeg was not found", ex.Message);
        }
        finally { FfmpegLocator.FinderOverride = null; }
    }

    [Fact]
    public void InsufficientDiskSpaceIsDetectedBeforeStarting()
    {
        var wav = Media.MakeWav(_fx.P("disk.wav"), (0.5, 0.5));
        var old = OfflineRenderer.Disk; OfflineRenderer.Disk = new FakeDisk(5_000_000);
        try
        {
            var ex = Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("disk.mp4")))));
            Assert.Contains("disk space", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(_fx.P("disk.mp4")));
        }
        finally { OfflineRenderer.Disk = old; }
    }

    private sealed class FakeDisk : IDiskSpace { private readonly long _n; public FakeDisk(long n) => _n = n; public long FreeBytes(string path) => _n; }

    [Fact]
    public void EncoderFailuresSurfaceTheEncoderMessage()
    {
        var wav = Media.MakeWav(_fx.P("fail.wav"), (0.5, 0.5));
        var dirAsFile = _fx.P("a-folder"); Directory.CreateDirectory(dirAsFile);
        var ex = Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(wav, Media.Opt(dirAsFile))));      // output path is a directory
        Assert.Contains("encoder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnusableOutputFolderIsReported()
    {
        var wav = Media.MakeWav(_fx.P("badfolder.wav"), (0.5, 0.5));
        var bad = Path.Combine(_fx.P("file.txt"), "sub", "out.mp4"); File.WriteAllText(_fx.P("file.txt"), "x");
        var ex = Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(wav, Media.Opt(bad))));
        Assert.Contains("output folder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrokenOrTinyAudioIsRejectedClearly()
    {
        File.WriteAllText(_fx.P("broken.wav"), "this is not audio");
        Assert.Contains("audio", Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(_fx.P("broken.wav"), Media.Opt(_fx.P("b.mp4"))))).Message, StringComparison.OrdinalIgnoreCase);
        var tiny = Media.MakeWav(_fx.P("tiny.wav"), (0.05, 0.5));
        Assert.Contains("too short", Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(tiny, Media.Opt(_fx.P("t.mp4"))))).Message);
        Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(_fx.P("missing.wav"), Media.Opt(_fx.P("m.mp4")))));
        Assert.Throws<ExportException>(() => Media.RenderSync(Media.Job(Media.MakeWav(_fx.P("ok.wav"), (0.5, 0.5)), Media.Opt(""))));
    }

    [Fact]
    public void ExportsAreDeterministic()
    {
        var wav = Media.MakeWav(_fx.P("det.wav"), (0.4, 0), (0.6, 0.6));
        var r1 = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("det1.mp4"), 256, 144, 25, audio: false), "particle-explosion"));
        var r2 = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P("det2.mp4"), 256, 144, 25, audio: false), "particle-explosion"));
        Assert.Equal(Media.FrameBrightness(r1.Path), Media.FrameBrightness(r2.Path));
    }

    [Fact]
    public void WorksWithPresetsAndEveryKindOfVisualizer()
    {
        var wav = Media.MakeWav(_fx.P("viz.wav"), (0.4, 0.5));
        foreach (var id in new[] { "plasma", "bars-3d", "particle-galaxy", "fractal" })
        {
            var res = Media.RenderSync(Media.Job(wav, Media.Opt(_fx.P($"v-{id}.mp4"), 320, 180, 24, audio: false), id));
            Assert.True(Media.DecodesCleanly(res.Path), id);
        }
    }
}

[Collection("gl")]
public sealed class RecordingSessionTests : IClassFixture<ExportFilesFixture>, IDisposable
{
    private readonly ExportFilesFixture _fx;
    private readonly AppPaths _paths;
    private readonly Database _db;
    public RecordingSessionTests(ExportFilesFixture fx)
    {
        _fx = fx; _paths = new AppPaths(_fx.P("data-" + Guid.NewGuid().ToString("N")[..6])); _paths.EnsureCreated();
        _db = new Database(_paths.DatabaseFile); _db.Migrate();
    }
    public void Dispose() { }

    [Fact]
    public void RefusesToRecordUntilAudioCaptureIsRunning()
    {
        using var engine = new AudioEngine();
        using var s = new RecordingSession(engine, _paths, _db);
        Assert.False(s.Start(out var err));
        Assert.Contains("Start audio capture first", err);
        Assert.Equal(RecordingState.Idle, s.State);
    }

    [Fact]
    public void LowDiskSpaceBlocksRecording()
    {
        if (AudioDeviceService.Enumerate(false).All(d => !d.IsUsable)) return;
        using var engine = new AudioEngine(); engine.Start(AudioSource.SystemAudio, "");
        using var s = new RecordingSession(engine, _paths, _db) { Disk = new FakeDisk2(100_000) };
        Assert.False(s.Start(out var err)); Assert.Contains("free disk space", err);
    }

    private sealed class FakeDisk2 : IDiskSpace { private readonly long _n; public FakeDisk2(long n) => _n = n; public long FreeBytes(string p) => _n; }

    [Fact]
    public void RecordsSystemAudioThenRendersASyncedVideoAndLogsHistory()
    {
        if (AudioDeviceService.Enumerate(false).All(d => !d.IsUsable)) return;       // no audio output on this machine
        using var engine = new AudioEngine(); engine.Params.AutoGain = false;
        engine.Start(AudioSource.SystemAudio, "");
        Assert.Equal(EngineState.Capturing, engine.State);
        using var s = new RecordingSession(engine, _paths, _db);
        var changes = new List<RecordingState>(); s.StateChanged += () => changes.Add(s.State);

        Assert.True(s.Start(out var err), err);
        Assert.Equal(RecordingState.Recording, s.State);
        Thread.Sleep(600);                                       // silence first
        var gen = new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.15, Type = SignalGeneratorType.Sin };
        using (var outp = new WasapiOut())
        {
            outp.Init(gen.Take(TimeSpan.FromSeconds(1.2))); outp.Play();
            Thread.Sleep(700);
            s.Pause(); Assert.Equal(RecordingState.Paused, s.State);
            Thread.Sleep(400); s.Resume(); Assert.Equal(RecordingState.Recording, s.State);
            Thread.Sleep(700);
            outp.Stop();
        }
        var wavPath = s.Stop();
        Assert.NotNull(wavPath); Assert.Equal(RecordingState.Idle, s.State); Assert.True(s.HasCapture);
        Assert.InRange(s.CapturedSeconds, 1.5, 2.4);               // ~2.0 s recorded, the 0.4 s pause removed
        using (var r = new AudioFileReader(wavPath!))
        {
            Assert.InRange(r.TotalTime.TotalSeconds, s.CapturedSeconds - 0.05, s.CapturedSeconds + 0.05);
            int chs = r.WaveFormat.Channels; var buf = new float[(int)(r.WaveFormat.SampleRate * r.TotalTime.TotalSeconds) * chs];
            int n = ((ISampleProvider)r).Read(buf.AsSpan()); double peak = buf.Take(n).Max(Math.Abs);
            Assert.True(peak > 0.02, $"the played tone should be in the recording (peak {peak:0.000})");
        }

        var o = Media.Opt(_fx.P("recorded.mp4"), 320, 180, 30);
        var job = Media.Job(wavPath!, o); job.AudioParams.Sensitivity = 1.5;
        var res = s.RenderAsync(job, null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(Media.DecodesCleanly(res.Path)); Assert.Contains("Audio: aac", Media.Info(res.Path)); Assert.Contains("stereo", Media.Info(res.Path));
        Assert.InRange(Media.Duration(res.Path), s.CapturedSeconds - 0.2, s.CapturedSeconds + 0.2);
        var hist = ExportHistory.Recent(_db);
        Assert.Contains(hist, h => h.FilePath == o.OutputPath && h.Status == "Completed" && h.Fps == 30 && h.Width == 320);
        Assert.Contains(RecordingState.Rendering, changes);
        s.DiscardCapture(); Assert.False(File.Exists(wavPath));
    }

    [Fact]
    public void RecordingEndsAndIsKeptIfTheAudioSourceStops()
    {
        if (AudioDeviceService.Enumerate(false).All(d => !d.IsUsable)) return;
        var engine = new AudioEngine(); engine.Start(AudioSource.SystemAudio, "");
        using var s = new RecordingSession(engine, _paths, _db);
        Assert.True(s.Start(out var err), err);
        Thread.Sleep(900);
        engine.Stop();                                           // user turns the source off mid-recording
        Assert.Equal(RecordingState.Idle, s.State);
        Assert.True(s.HasCapture); Assert.Contains("stopped", s.Message ?? "");
        Assert.InRange(s.CapturedSeconds, 0.6, 1.6);
        s.DiscardCapture(); engine.Dispose();
    }

    [Fact]
    public void FailedExportsAreLoggedInHistory()
    {
        using var engine = new AudioEngine(); using var s = new RecordingSession(engine, _paths, _db);
        var wav = Media.MakeWav(_fx.P("hist.wav"), (0.5, 0.5));
        var o = Media.Opt(Path.Combine(_fx.P("hist.wav"), "x", "bad.mp4"));       // parent is a file -> unusable
        Assert.Throws<ExportException>(() => s.RenderAsync(Media.Job(wav, o), null, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains(ExportHistory.Recent(_db), h => h.Status == "Failed");
        Assert.Equal(RecordingState.Idle, s.State);
    }

    [Fact]
    public void ExternalAudioFilesCanBeRendered()
    {
        using var engine = new AudioEngine(); using var s = new RecordingSession(engine, _paths, _db);
        var wav = Media.MakeWav(_fx.P("ext.wav"), (1.0, 0.5));
        s.UseExternalAudio(wav, 1.0);
        Assert.True(s.HasCapture);
        var res = s.RenderAsync(Media.Job(wav, Media.Opt(_fx.P("ext.mp4"))), null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(Media.DecodesCleanly(res.Path));
        s.UseExternalAudio("none", 0);
    }
}
