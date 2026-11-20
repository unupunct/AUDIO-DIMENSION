using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AudioDimension.Audio.Analysis;
using AudioDimension.Audio.Capture;
using AudioDimension.Audio.Devices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioDimension.App.ViewModels;

public sealed partial class BandToggle : ObservableObject
{
    private readonly AudioParams _p; private readonly int _i;
    public BandToggle(AudioParams p, int i) { _p = p; _i = i; }
    public string Name => FrequencyBands.Names[_i];
    public string Range => $"{FrequencyBands.Edges[_i]:0}-{FrequencyBands.Edges[_i + 1]:0} Hz";
    public void Refresh() => OnPropertyChanged(nameof(IsEnabled));
    public bool IsEnabled { get => _p.BandEnabled[_i]; set { _p.BandEnabled[_i] = value; OnPropertyChanged(); } }
}

public sealed partial class AudioViewModel : PageViewModel
{
    public override string Title => "Audio Settings";
    public override string Glyph => "";

    private readonly AudioEngine _engine = App.Audio;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private IReadOnlyList<AudioDeviceInfo> _inputs = Array.Empty<AudioDeviceInfo>();
    private IReadOnlyList<AudioDeviceInfo> _outputs = Array.Empty<AudioDeviceInfo>();
    private bool _loading;

    public AudioParams Params => _engine.Params;
    public string[] Sources { get; } = { "Microphone", "System audio (loopback)" };
    public BandToggle[] Bands { get; }
    public string[] BandNames => FrequencyBands.Names;

    [ObservableProperty] private string _selectedSource = "Microphone";
    [ObservableProperty] private IReadOnlyList<AudioDeviceInfo> _devices = Array.Empty<AudioDeviceInfo>();
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    [ObservableProperty] private IReadOnlyList<AudioDeviceInfo> _monitorDevices = Array.Empty<AudioDeviceInfo>();
    [ObservableProperty] private AudioDeviceInfo? _selectedMonitorDevice;
    [ObservableProperty] private bool _monitorEnabled;

    [ObservableProperty] private string _deviceName = "-";
    [ObservableProperty] private string _deviceStatus = "Not capturing";
    [ObservableProperty] private string _deviceFormat = "-";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _monitorMessage = "";
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _showPrivacyButton;
    [ObservableProperty] private string _startStopText = "Start capture";

    [ObservableProperty] private double _level;
    [ObservableProperty] private double _peak;
    [ObservableProperty] private string _rmsText = "-- dB";
    [ObservableProperty] private string _peakText = "-- dB";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _beat;
    [ObservableProperty] private float[] _bandValues = new float[FrequencyBands.Count];
    [ObservableProperty] private float[] _spectrum = new float[AudioAnalyzer.SpectrumBins];

    public AudioViewModel()
    {
        Bands = Enumerable.Range(0, FrequencyBands.Count).Select(i => new BandToggle(Params, i)).ToArray();
        var s = App.Settings.Current;
        Params.Sensitivity = s.Sensitivity;
        Params.Smoothing = s.AudioSmoothing;
        RefreshDevices();
        _engine.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(SyncState);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        SyncState();
    }

    partial void OnSelectedSourceChanged(string value) => RefreshDeviceList();

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (_loading || value is null) return;
        var s = App.Settings.Current;
        if (SelectedSource == Sources[0]) s.DefaultInputDeviceId = value.Id; else s.DefaultSystemDeviceId = value.Id;
        App.Settings.Save(s);
    }

    /// <summary>Selects the source/device stored in a project. Does not start capture and does not change the saved default devices.
    /// Returns a note when the stored device is not present on this PC.</summary>
    public string? ApplyProjectAudio(string source, string? deviceId, string? deviceName)
    {
        RefreshDevices();
        _loading = true;
        try
        {
            SelectedSource = source == "SystemAudio" ? Sources[1] : Sources[0];
            _loading = true;                                   // SelectedSource's change handler resets the flag
            var pick = Devices.FirstOrDefault(d => d.IsUsable && deviceId is not null && d.Id == deviceId)
                       ?? Devices.FirstOrDefault(d => d.IsUsable && deviceName is not null && d.Name == deviceName);
            if (pick is not null) { SelectedDevice = pick; return null; }
            return deviceName is null && deviceId is null ? null : $"The audio device '{deviceName ?? deviceId}' from this project is not available on this PC, so the default device was selected.";
        }
        finally { _loading = false; }
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        _inputs = AudioDeviceService.Enumerate(true);
        _outputs = AudioDeviceService.Enumerate(false);
        MonitorDevices = _outputs.Where(d => d.IsUsable).ToList();
        SelectedMonitorDevice ??= MonitorDevices.FirstOrDefault(d => d.IsDefault) ?? MonitorDevices.FirstOrDefault();
        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        _loading = true;
        bool mic = SelectedSource == Sources[0];
        var list = mic ? _inputs : _outputs;
        Devices = list;
        var saved = mic ? App.Settings.Current.DefaultInputDeviceId : App.Settings.Current.DefaultSystemDeviceId;
        SelectedDevice = list.FirstOrDefault(d => d.Id == saved && d.IsUsable)
                         ?? list.FirstOrDefault(d => d.IsDefault && d.IsUsable) ?? list.FirstOrDefault(d => d.IsUsable);
        _loading = false;
        if (!IsCapturing)
        {
            ErrorMessage = list.Any(d => d.IsUsable) ? "" :
                mic ? "No microphone detected. Connect a microphone, then press Refresh."
                    : "No audio output device detected for system audio capture.";
        }
    }

    [RelayCommand]
    private void StartStop()
    {
        if (_engine.State is EngineState.Capturing or EngineState.Reconnecting) { _engine.Stop(); return; }
        if (SelectedDevice is null || !SelectedDevice.IsUsable)
        {
            ErrorMessage = SelectedSource == Sources[0] ? "No usable microphone is selected." : "No usable output device is selected.";
            return;
        }
        var s = App.Settings.Current;
        string? monitor = MonitorEnabled ? SelectedMonitorDevice?.Id : null;
        _engine.Start(SelectedSource == Sources[0] ? AudioSource.Microphone : AudioSource.SystemAudio,
            SelectedDevice.Id, monitor, s.BufferMilliseconds);
    }

    [RelayCommand]
    private void OpenPrivacySettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });

    /// <summary>Re-reads engine parameters after a preset changed them from outside the sliders.</summary>
    public void RefreshParams()
    {
        OnPropertyChanged(nameof(Params));
        foreach (var b in Bands) b.Refresh();
    }

    [RelayCommand]
    private void ResetParams()
    {
        var d = new AudioParams();
        Params.InputGain = d.InputGain; Params.Sensitivity = 1; Params.NoiseGateDb = d.NoiseGateDb; Params.Smoothing = d.Smoothing;
        Params.BassSensitivity = 1; Params.MidSensitivity = 1; Params.TrebleSensitivity = 1; Params.FrequencyTilt = 0;
        Params.AutoGain = true; Params.PeakDetection = true;
        foreach (var b in Bands) b.IsEnabled = true;
        OnPropertyChanged(nameof(Params));
    }

    private void SyncState()
    {
        var st = _engine.State;
        IsCapturing = st is EngineState.Capturing or EngineState.Reconnecting;
        StartStopText = IsCapturing ? "Stop capture" : "Start capture";
        ErrorMessage = _engine.Message ?? "";
        MonitorMessage = _engine.MonitorMessage ?? "";
        ShowPrivacyButton = st == EngineState.Error && SelectedSource == Sources[0] && ErrorMessage.Contains("privacy", StringComparison.OrdinalIgnoreCase);
        var dev = _engine.ActiveDevice ?? SelectedDevice;
        DeviceName = dev?.Name ?? "-";
        DeviceStatus = st switch
        {
            EngineState.Capturing => "Capturing",
            EngineState.Reconnecting => "Disconnected - reconnecting",
            EngineState.Error => "Error",
            _ => dev is null ? "No device" : dev.Status
        };
        DeviceFormat = dev is { SampleRate: > 0 } ? $"{dev.SampleRate} Hz, {dev.Channels} ch" : "-";
        if (!IsCapturing) ErrorMessage = _engine.Message ?? ErrorMessage;
    }

    private void Tick()
    {
        if (!IsCapturing)
        {
            if (Level != 0 || Bands.Length > 0 && BandValues.Any(v => v != 0))
            {
                Level = 0; Peak = 0; IsActive = false; Beat = false; RmsText = "-- dB"; PeakText = "-- dB";
                BandValues = new float[FrequencyBands.Count]; Spectrum = new float[AudioAnalyzer.SpectrumBins];
            }
            return;
        }
        var f = _engine.Latest;
        Level = f.Level;
        Peak = ToMeter(f.PeakHold);
        RmsText = f.RmsDb > -100 ? $"{f.RmsDb:0.0} dB" : "-inf dB";
        PeakText = f.PeakHold > 1e-5 ? $"{20 * Math.Log10(f.PeakHold):0.0} dB" : "-inf dB";
        IsActive = f.IsActive;
        if (f.IsBeat) Beat = true; else if (Beat && f.BeatStrength == 0) Beat = false;
        BandValues = f.Bands;
        Spectrum = f.Spectrum;
    }

    private static double ToMeter(float lin) => lin <= 1e-5f ? 0 : Math.Clamp((20 * Math.Log10(lin) + 60) / 60, 0, 1);
}
