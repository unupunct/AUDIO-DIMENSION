using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AudioDimension.Audio.Capture;
using AudioDimension.Recording;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.Wave;
using Serilog;

namespace AudioDimension.App.ViewModels;

public sealed record FormatChoice(VideoFormat Format, string Name) { public override string ToString() => Name; }
public sealed record SizeChoice(string Name, int W, int H) { public override string ToString() => Name; }

public sealed partial class RecordingViewModel : PageViewModel
{
    public override string Title => "Recording & Export";
    public override string Glyph => "";

    private readonly StudioViewModel _studio;
    private readonly RecordingSession _session;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _cts;
    private bool _startedCapture;     // Record switched the audio source on, so it also switches it off again

    public FormatChoice[] Formats { get; } =
    {
        new(VideoFormat.Mp4H264, "MP4 (H.264)"), new(VideoFormat.Mp4Hevc, "MP4 (H.265/HEVC)"), new(VideoFormat.WebM, "WebM (VP9)")
    };
    public SizeChoice[] Sizes { get; } = ExportOptions.StandardSizes.Select(s => new SizeChoice(s.Name, s.W, s.H)).Append(new SizeChoice("Custom", 0, 0)).ToArray();
    public int[] FrameRates { get; } = ExportOptions.StandardFps;
    public EncoderPreference[] Encoders { get; } = Enum.GetValues<EncoderPreference>();
    public ObservableCollection<ExportHistoryEntry> History { get; } = new();

    [ObservableProperty] private FormatChoice _selectedFormat;
    [ObservableProperty] private SizeChoice _selectedSize;
    [ObservableProperty] private int _customWidth = 1920;
    [ObservableProperty] private int _customHeight = 1080;
    [ObservableProperty] private int _selectedFps = 30;
    [ObservableProperty] private int _quality = 75;
    [ObservableProperty] private EncoderPreference _encoder = EncoderPreference.Auto;
    [ObservableProperty] private bool _includeAudio = true;
    [ObservableProperty] private bool _autoRender = true;
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _filePrefix = "AudioDimension";

    [ObservableProperty] private string _stateText = "Ready";
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _sizeEstimate = "";
    [ObservableProperty] private string _recordLabel = "Record";
    [ObservableProperty] private string _pauseLabel = "Pause";
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private bool _hasCapture;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _ffmpegStatus = "Checking FFmpeg...";
    [ObservableProperty] private string _encoderStatus = "";
    [ObservableProperty] private string _summary = "";

    public string? LastOutput { get; private set; }
    public double CaptureSeconds => _session.State is RecordingState.Recording or RecordingState.Paused ? _session.ElapsedSeconds : _session.CapturedSeconds;

    public RecordingViewModel(StudioViewModel studio, RecordingSession session)
    {
        _studio = studio; _session = session;
        var s = App.Settings.Current;
        _selectedFormat = Formats.FirstOrDefault(f => f.Name == s.DefaultFormat) ?? Formats[0];
        _selectedSize = Sizes.FirstOrDefault(z => $"{z.W}x{z.H}" == s.DefaultResolution) ?? Sizes[1];
        _selectedFps = FrameRates.Contains(s.DefaultFrameRate) ? s.DefaultFrameRate : 30;
        _quality = Math.Clamp(s.VideoQuality, 1, 100);
        _encoder = Enum.TryParse<EncoderPreference>(s.EncoderPreference, out var ep) ? ep : EncoderPreference.Auto;
        _outputFolder = string.IsNullOrWhiteSpace(s.ExportFolder) ? App.Paths.Exports : s.ExportFolder;
        _session.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(SyncState);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        RefreshHistory(); SyncState(); UpdateSummary();
        _ = ProbeAsync();
    }

    public RecordingSession Session => _session;

    public AudioDimension.Projects.ProjectRecording ToProject()
    {
        var (w, h) = Resolution;
        return new AudioDimension.Projects.ProjectRecording
        {
            Format = SelectedFormat.Format.ToString(), Width = w, Height = h, Fps = SelectedFps, Quality = Quality, Encoder = Encoder.ToString(),
            IncludeAudio = IncludeAudio, AutoRender = AutoRender, FilePrefix = FilePrefix
        };
    }

    public void ApplyProject(AudioDimension.Projects.ProjectRecording r)
    {
        SelectedFormat = Formats.FirstOrDefault(f => f.Format.ToString() == r.Format) ?? Formats[0];
        var std = Sizes.FirstOrDefault(z => z.W == r.Width && z.H == r.Height);
        if (std is null) { CustomWidth = r.Width; CustomHeight = r.Height; }
        SelectedSize = std ?? Sizes[^1];
        SelectedFps = FrameRates.Contains(r.Fps) ? r.Fps : 30;
        Quality = r.Quality; Encoder = Enum.TryParse<EncoderPreference>(r.Encoder, out var e) ? e : EncoderPreference.Auto;
        IncludeAudio = r.IncludeAudio; AutoRender = r.AutoRender; FilePrefix = r.FilePrefix;
    }

    private async Task ProbeAsync()
    {
        try
        {
            var (status, enc) = await Task.Run(() =>
            {
                var ff = FfmpegLocator.Require();
                var caps = EncoderCapabilities.Probe(ff.Path);
                var hw = new[] { "h264_nvenc", "h264_qsv", "h264_amf" }.Where(caps.Working.Contains).Select(x => x.Split('_')[1].ToUpperInvariant()).ToList();
                return ($"FFmpeg {ff.Version} ready", "Hardware encoders: " + (hw.Count == 0 ? "none detected (software encoding will be used)" : string.Join(", ", hw)));
            });
            FfmpegStatus = status; EncoderStatus = enc; ErrorText = "";
        }
        catch (ExportException ex) { FfmpegStatus = "FFmpeg unavailable"; ErrorText = ex.Message; }
        catch (Exception ex) { Log.Error(ex, "FFmpeg probe failed"); FfmpegStatus = "FFmpeg unavailable"; ErrorText = "FFmpeg could not be checked: " + ex.Message; }
    }

    partial void OnSelectedFormatChanged(FormatChoice value) => UpdateSummary();
    partial void OnSelectedSizeChanged(SizeChoice value) => UpdateSummary();
    partial void OnCustomWidthChanged(int value) => UpdateSummary();
    partial void OnCustomHeightChanged(int value) => UpdateSummary();
    partial void OnSelectedFpsChanged(int value) => UpdateSummary();
    partial void OnQualityChanged(int value) => UpdateSummary();
    partial void OnIncludeAudioChanged(bool value) => UpdateSummary();

    public bool IsCustomSize => SelectedSize.W == 0;
    public bool CanRecord => !IsExporting;
    partial void OnIsExportingChanged(bool value) => OnPropertyChanged(nameof(CanRecord));

    private (int w, int h) Resolution => SelectedSize.W == 0 ? (CustomWidth, CustomHeight) : (SelectedSize.W, SelectedSize.H);

    private ExportOptions BuildOptions(string? name = null)
    {
        var (w, h) = Resolution;
        var o = new ExportOptions { Format = SelectedFormat.Format, Width = w, Height = h, Fps = SelectedFps, Quality = Quality, Encoder = Encoder, IncludeAudio = IncludeAudio };
        string stamp = name ?? $"{(string.IsNullOrWhiteSpace(FilePrefix) ? "AudioDimension" : FilePrefix.Trim())}_{DateTime.Now:yyyyMMdd_HHmmss}";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) stamp = stamp.Replace(c, '_');
        o.OutputPath = System.IO.Path.Combine(string.IsNullOrWhiteSpace(OutputFolder) ? App.Paths.Exports : OutputFolder, stamp + o.Extension);
        return o;
    }

    private void UpdateSummary()
    {
        OnPropertyChanged(nameof(IsCustomSize));
        var (w, h) = Resolution;
        Summary = $"{SelectedFormat.Name}  -  {w & ~1} x {h & ~1}  -  {SelectedFps} FPS  -  quality {Quality}" + (IncludeAudio ? "  -  with audio" : "  -  no audio");
        UpdateEstimate();
    }

    private void UpdateEstimate()
    {
        double secs = CaptureSeconds;
        var o = BuildOptions();
        SizeEstimate = secs > 0 ? $"Estimated video size: about {FormatBytes(o.EstimateBytes(secs))}  (recording file: {FormatBytes(_session.CapturedBytes)})" : $"Estimated video size: about {FormatBytes(o.EstimateBytes(60))} per minute";
    }

    private static string FormatBytes(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.00} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} MB" : $"{b / 1024.0:0} KB";
    private static string Clock(double s) { var t = TimeSpan.FromSeconds(Math.Max(0, s)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"); }

    private void Tick()
    {
        if (_session.State is RecordingState.Recording or RecordingState.Paused) { ElapsedText = Clock(_session.ElapsedSeconds); UpdateEstimate(); }
    }

    private void SyncState()
    {
        var st = _session.State;
        IsRecording = st is RecordingState.Recording or RecordingState.Paused;
        IsPaused = st == RecordingState.Paused;
        IsExporting = st == RecordingState.Rendering;
        HasCapture = _session.HasCapture && st == RecordingState.Idle;
        RecordLabel = IsRecording ? "Stop" : "Record";
        PauseLabel = IsPaused ? "Resume" : "Pause";
        StateText = st switch { RecordingState.Recording => "RECORDING", RecordingState.Paused => "PAUSED", RecordingState.Rendering => "Rendering video", _ => HasCapture ? "Recording saved - ready to render" : "Ready" };
        if (!IsRecording) ElapsedText = Clock(_session.CapturedSeconds);
        if (_session.Message is { Length: > 0 } m && st == RecordingState.Idle) ResultText = m;
        UpdateEstimate();
        _studio.NotifyRecordingChanged();
    }

    // ---- recording ---------------------------------------------------------------------------
    [RelayCommand]
    public void ToggleRecord()
    {
        if (IsExporting) return;
        if (IsRecording) { StopRecording(); return; }
        ErrorText = ""; ResultText = "";
        if (App.Audio.State != EngineState.Capturing)
        {
            _startedCapture = true;
            _studio.PlayCommand.Execute(null);                 // Record is an explicit user action, so it may start the audio source they selected
            if (App.Audio.State != EngineState.Capturing) { _startedCapture = false; ErrorText = App.Audio.Message ?? "Audio capture could not start. Pick a device on the Audio page."; return; }
        }
        if (!_session.Start(out var err)) { ErrorText = err; ReleaseCapture(); return; }
        _studio.RestartTimelineCommand.Execute(null);          // text animations begin at the start of the take
    }

    [RelayCommand]
    private void TogglePause() { if (_session.State == RecordingState.Recording) _session.Pause(); else if (_session.State == RecordingState.Paused) _session.Resume(); }

    private void ReleaseCapture() { if (_startedCapture) { _startedCapture = false; App.Audio.Stop(); } }

    private void StopRecording()
    {
        var path = _session.Stop();
        ReleaseCapture();                       // no background capture once the take is over
        if (path is null) { ErrorText = _session.Message ?? "Nothing was recorded."; return; }
        if (AutoRender) _ = RenderCapturedAsync(); else ResultText = $"Recording saved ({Clock(_session.CapturedSeconds)}). Press Render video when you are ready.";
    }

    [RelayCommand] private void Stop() { if (IsRecording) StopRecording(); }

    // ---- exporting -----------------------------------------------------------------------------
    [RelayCommand]
    private Task RenderCapturedAsync() => RenderAsync(_session.CapturedAudioPath);

    private async Task RenderAsync(string? audioPath)
    {
        if (audioPath is null || !File.Exists(audioPath)) { ErrorText = "There is no recording to render yet."; return; }
        if (IsExporting) return;
        ErrorText = ""; ResultText = ""; Progress = 0; ProgressText = "Preparing...";
        var o = BuildOptions();
        var job = new ExportJob
        {
            Options = o, Config = _studio.Config.Clone(), VisualizerId = _studio.SelectedVisualizer.Id,
            AudioParams = App.Audio.Params.Clone(), AudioPath = audioPath
        };
        _cts = new CancellationTokenSource();
        var progress = new Progress<ExportProgress>(p =>
        {
            Progress = p.Fraction;
            ProgressText = p.Stage == "Finishing" ? "Finalising the video file..." :
                $"Rendering frame {p.Frame:N0} of {p.TotalFrames:N0}   -   {p.RenderFps:0.#} fps" + (p.Remaining is { } r ? $"   -   about {Clock(r.TotalSeconds)} left" : "");
        });
        try
        {
            var res = await _session.RenderAsync(job, progress, _cts.Token);
            LastOutput = res.Path; Progress = 1;
            ProgressText = "Done";
            ResultText = $"Saved {System.IO.Path.GetFileName(res.Path)} - {res.Frames:N0} frames, {FormatBytes(res.SizeBytes)}, rendered in {Clock(res.Elapsed.TotalSeconds)} using {res.EncoderName}.";
            OnPropertyChanged(nameof(HasOutput));
        }
        catch (OperationCanceledException) { ProgressText = ""; Progress = 0; ResultText = "Export cancelled. No partial file was kept."; }
        catch (ExportException ex) { ProgressText = ""; ErrorText = ex.Message; Log.Warning(ex, "Export failed"); }
        catch (Exception ex) { ProgressText = ""; ErrorText = "The export failed unexpectedly: " + ex.Message; Log.Error(ex, "Export crashed"); }
        finally { _cts?.Dispose(); _cts = null; RefreshHistory(); SyncState(); }
    }

    [RelayCommand] private void CancelExport() => _cts?.Cancel();

    [RelayCommand]
    private async Task RenderFromFileAsync()
    {
        if (IsRecording || IsExporting) return;
        var dlg = new OpenFileDialog { Title = "Choose an audio file to turn into a video", Filter = "Audio files (*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma)|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma|All files (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var r = new AudioFileReader(dlg.FileName);
            _session.UseExternalAudio(dlg.FileName, r.TotalTime.TotalSeconds);
        }
        catch (Exception ex) { ErrorText = "That audio file could not be read: " + ex.Message; return; }
        await RenderAsync(dlg.FileName);
    }

    [RelayCommand]
    private void DiscardCapture() { _session.DiscardCapture(); ResultText = "Recording discarded."; SyncState(); }

    public bool HasOutput => LastOutput is not null && File.Exists(LastOutput);

    [RelayCommand]
    private void OpenVideo() { if (HasOutput) Process.Start(new ProcessStartInfo(LastOutput!) { UseShellExecute = true }); }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (HasOutput) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LastOutput}\"") { UseShellExecute = true });
        else OpenFolder();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        Process.Start(new ProcessStartInfo(OutputFolder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose the folder for exported videos", InitialDirectory = OutputFolder };
        if (dlg.ShowDialog() != true) return;
        OutputFolder = dlg.FolderName;
        var s = App.Settings.Current; s.ExportFolder = OutputFolder; App.Settings.Save(s);
    }

    [RelayCommand]
    private void OpenHistoryItem(ExportHistoryEntry? e)
    {
        if (e is not null && File.Exists(e.FilePath)) Process.Start(new ProcessStartInfo(e.FilePath) { UseShellExecute = true });
    }

    private void RefreshHistory()
    {
        try
        {
            History.Clear();
            foreach (var h in ExportHistory.Recent(App.Db, 20)) History.Add(h);
        }
        catch (Exception ex) { Log.Warning(ex, "Could not load export history"); }
    }

    public void Shutdown() { _cts?.Cancel(); _timer.Stop(); }
}
