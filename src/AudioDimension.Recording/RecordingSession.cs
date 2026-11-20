using AudioDimension.Audio.Capture;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using NAudio.Wave;
using Serilog;

namespace AudioDimension.Recording;

public enum RecordingState { Idle, Recording, Paused, Rendering }

public sealed record ExportHistoryEntry(long Id, string FilePath, string Format, int Width, int Height, int Fps, double DurationSeconds, long SizeBytes, string Status, DateTime CreatedUtc);

public static class ExportHistory
{
    public static void Add(Database db, string path, string format, int w, int h, int fps, double seconds, long size, string status)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO export_history(file_path,format,width,height,fps,duration_seconds,size_bytes,status,created_utc) VALUES($p,$f,$w,$h,$r,$d,$s,$st,$t)";
        cmd.Parameters.AddWithValue("$p", path); cmd.Parameters.AddWithValue("$f", format); cmd.Parameters.AddWithValue("$w", w); cmd.Parameters.AddWithValue("$h", h);
        cmd.Parameters.AddWithValue("$r", fps); cmd.Parameters.AddWithValue("$d", seconds); cmd.Parameters.AddWithValue("$s", size);
        cmd.Parameters.AddWithValue("$st", status); cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public static IReadOnlyList<ExportHistoryEntry> Recent(Database db, int count = 25)
    {
        var list = new List<ExportHistoryEntry>();
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,file_path,format,width,height,fps,duration_seconds,size_bytes,status,created_utc FROM export_history ORDER BY id DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", count);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ExportHistoryEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetDouble(6), r.GetInt64(7), r.GetString(8),
                DateTime.TryParse(r.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.UtcNow));
        return list;
    }
}

/// <summary>
/// Start/pause/resume/stop recording. Recording captures the selected audio source (microphone or system output) to a WAV on a gap-free
/// timeline; the video is then rendered offline from that audio with the current look, so it is frame-exact and in sync.
/// Capture only happens between Start and Stop, and only while the user-started audio capture is running.
/// </summary>
public sealed class RecordingSession : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly AppPaths _paths;
    private readonly Database _db;
    private readonly object _gate = new();
    private TimelineWavWriter? _wav;
    private WaveFormat? _format;
    private bool _formatWarned;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? CapturedAudioPath { get; private set; }
    public double CapturedSeconds { get; private set; }
    public string? Message { get; private set; }
    public IDiskSpace Disk { get; set; } = new SystemDiskSpace();
    public bool HasCapture => CapturedAudioPath is not null && File.Exists(CapturedAudioPath);
    public double ElapsedSeconds => _wav?.DurationSeconds ?? CapturedSeconds;
    public long CapturedBytes => _wav?.BytesWritten ?? (HasCapture ? new FileInfo(CapturedAudioPath!).Length : 0);
    public event Action? StateChanged;

    public RecordingSession(AudioEngine engine, AppPaths paths, Database db)
    {
        _engine = engine; _paths = paths; _db = db;
        _engine.StateChanged += OnEngineState;
    }

    private void Set(RecordingState s, string? msg = null) { State = s; Message = msg; StateChanged?.Invoke(); }

    /// <summary>Deletes leftover temporary recordings older than <paramref name="days"/> days (e.g. after a crash).</summary>
    public static int CleanOldRecordings(AppPaths paths, int days = 3)
    {
        int n = 0;
        try
        {
            if (!Directory.Exists(paths.Temp)) return 0;
            foreach (var f in Directory.EnumerateFiles(paths.Temp, "recording-*.wav"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-days)) { try { File.Delete(f); n++; } catch (IOException) { } }
        }
        catch (Exception ex) { Log.Debug(ex, "Temp cleanup skipped"); }
        return n;
    }

    public bool Start(out string error)
    {
        lock (_gate)
        {
            error = "";
            if (State != RecordingState.Idle) { error = "A recording or export is already in progress."; return false; }
            if (_engine.State != EngineState.Capturing)
            {
                error = "Start audio capture first (choose a microphone or system audio and press Play). Nothing is recorded until you do.";
                return false;
            }
            Directory.CreateDirectory(_paths.Temp);
            if (Disk.FreeBytes(_paths.Temp) < 1_000_000_000L) { error = "Less than 1 GB of free disk space is available for the recording's temporary audio file."; return false; }
            DiscardCapture();
            var path = Path.Combine(_paths.Temp, $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            try
            {
                var fmt = _engine.ActiveFormat ?? throw new InvalidOperationException("No active audio format.");
                _wav = new TimelineWavWriter(path, fmt);
                _format = TimelineWavWriter.Normalise(fmt); _formatWarned = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Error(ex, "Could not start recording");
                error = "The recording file could not be created: " + ex.Message; return false;
            }
            CapturedAudioPath = path; CapturedSeconds = 0;
            _engine.RawDataAvailable += OnRaw;
            Log.Information("Recording started: {Path}", path);
            Set(RecordingState.Recording);
            return true;
        }
    }

    private void OnRaw(byte[] buffer, int count, WaveFormat format)
    {
        var w = _wav; if (w is null) return;
        var n = TimelineWavWriter.Normalise(format);
        if (_format is not null && (n.SampleRate != _format.SampleRate || n.Channels != _format.Channels || n.Encoding != _format.Encoding || n.BitsPerSample != _format.BitsPerSample))
        {
            // device was swapped during reconnect: keep the timeline intact with silence rather than corrupting the file
            if (!_formatWarned) { _formatWarned = true; Log.Warning("Audio format changed during recording; the rest of this take will be silent."); }
            return;
        }
        w.Write(buffer, count);
    }

    public void Pause() { lock (_gate) { if (State != RecordingState.Recording) return; _wav?.Pause(); Set(RecordingState.Paused); } }
    public void Resume() { lock (_gate) { if (State != RecordingState.Paused) return; _wav?.Resume(); Set(RecordingState.Recording); } }

    /// <summary>Finishes the capture. Returns the WAV path (null if nothing usable was recorded).</summary>
    public string? Stop(string? reason = null)
    {
        lock (_gate)
        {
            if (State is not (RecordingState.Recording or RecordingState.Paused)) return CapturedAudioPath;
            _engine.RawDataAvailable -= OnRaw;
            double secs = 0;
            try { secs = _wav?.Finish() ?? 0; } catch (Exception ex) { Log.Error(ex, "Finalising recording failed"); }
            _wav?.Dispose(); _wav = null;
            CapturedSeconds = secs;
            if (secs < 0.2) { DiscardCapture(); Set(RecordingState.Idle, "The recording was too short and was discarded."); return null; }
            Log.Information("Recording stopped: {Sec:0.0}s", secs);
            Set(RecordingState.Idle, reason);
            return CapturedAudioPath;
        }
    }

    private void OnEngineState(EngineState s, string? msg)
    {
        if (s is EngineState.Stopped or EngineState.Error && State is RecordingState.Recording or RecordingState.Paused)
            Stop("Audio capture stopped, so the recording was ended and kept." + (msg is null ? "" : " " + msg));
    }

    public void DiscardCapture()
    {
        var p = CapturedAudioPath; CapturedAudioPath = null; CapturedSeconds = 0;
        if (p is not null) { try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>Adopts an existing audio file as the source for rendering (used by "Render from an audio file").</summary>
    public void UseExternalAudio(string path, double seconds) { DiscardCapture(); CapturedAudioPath = path; CapturedSeconds = seconds; StateChanged?.Invoke(); }

    public async Task<ExportResult> RenderAsync(ExportJob job, IProgress<ExportProgress>? progress, CancellationToken ct)
    {
        lock (_gate)
        {
            if (State != RecordingState.Idle) throw new ExportException("Stop the recording before exporting.");
            Set(RecordingState.Rendering);
        }
        var o = job.Options;
        try
        {
            var result = await Task.Factory.StartNew(() => OfflineRenderer.Render(job, progress, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            TryHistory(o, result.DurationSeconds, result.SizeBytes, "Completed");
            return result;
        }
        catch (OperationCanceledException) { TryHistory(o, 0, 0, "Cancelled"); throw; }
        catch (Exception) { TryHistory(o, 0, 0, "Failed"); throw; }
        finally { Set(RecordingState.Idle); }
    }

    private void TryHistory(ExportOptions o, double secs, long size, string status)
    {
        try { ExportHistory.Add(_db, o.OutputPath, ExportOptions.FormatName(o.Format), o.EvenWidth, o.EvenHeight, o.Fps, secs, size, status); }
        catch (Exception ex) { Log.Warning(ex, "Could not write export history"); }
    }

    public void Dispose()
    {
        _engine.StateChanged -= OnEngineState;
        if (State is RecordingState.Recording or RecordingState.Paused) Stop();
    }
}
