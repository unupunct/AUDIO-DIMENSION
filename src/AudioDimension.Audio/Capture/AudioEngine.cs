using System.Diagnostics;
using AudioDimension.Audio.Analysis;
using AudioDimension.Audio.Devices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace AudioDimension.Audio.Capture;

public enum EngineState { Stopped, Capturing, Reconnecting, Error }

/// <summary>
/// Owns WASAPI capture (microphone or loopback), optional monitoring, and a dedicated analysis thread.
/// Capture runs only between Start() and Stop(); nothing is captured otherwise.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly object _ringLock = new();
    private readonly float[] _ring = new float[1 << 17];
    private int _ringHead, _ringCount;
    private readonly ManualResetEventSlim _dataSignal = new(false);

    private WasapiCapture? _capture;
    private WasapiOut? _monitorOut;
    private BufferedWaveProvider? _monitorBuffer;
    private Thread? _analysisThread;
    private volatile bool _running;
    private Timer? _retryTimer;
    private AudioAnalyzer _analyzer;
    private AnalysisFrame _latest = AnalysisFrame.Silent;
    private long _lastDataTicks;

    private AudioSource _source;
    private string _deviceId = "";
    private string? _monitorDeviceId;
    private int _bufferMs = 20;

    public AudioParams Params { get; } = new();
    public EngineState State { get; private set; } = EngineState.Stopped;
    public string? Message { get; private set; }
    public string? MonitorMessage { get; private set; }
    public AudioDeviceInfo? ActiveDevice { get; private set; }
    /// <summary>Wave format of the open capture stream (used by the recorder to write a matching WAV).</summary>
    public WaveFormat? ActiveFormat { get; private set; }
    public int SampleRate => _analyzer.SampleRate;
    public AudioSource Source => _source;
    public AnalysisFrame Latest => Volatile.Read(ref _latest);

    public event Action<EngineState, string?>? StateChanged;
    /// <summary>Raw captured bytes in the device format (for the recording engine).</summary>
    public event Action<byte[], int, WaveFormat>? RawDataAvailable;

    public AudioEngine() { _analyzer = new AudioAnalyzer(48000, Params); }

    public void Start(AudioSource source, string deviceId, string? monitorDeviceId = null, int bufferMs = 20)
    {
        lock (_gate)
        {
            StopInternal(EngineState.Stopped, null);
            _source = source; _deviceId = deviceId; _monitorDeviceId = monitorDeviceId; _bufferMs = Math.Clamp(bufferMs, 5, 200);
            if (source == AudioSource.Microphone && AudioErrors.CheckMicrophonePermission() == MicrophonePermission.Denied)
            {
                SetState(EngineState.Error, "Windows microphone access is turned off. Enable 'Let desktop apps access your microphone' in Windows privacy settings.");
                return;
            }
            TryOpen();
        }
    }

    public void Stop()
    {
        lock (_gate) StopInternal(EngineState.Stopped, null);
    }

    private bool TryOpen()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            bool mic = _source == AudioSource.Microphone;
            var flow = mic ? DataFlow.Capture : DataFlow.Render;
            MMDevice device;
            try
            {
                device = string.IsNullOrEmpty(_deviceId) ? en.GetDefaultAudioEndpoint(flow, Role.Multimedia) : en.GetDevice(_deviceId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Device lookup failed");
                SetState(EngineState.Error, mic ? "No microphone was found. Connect one and press Start again."
                                                  : "No audio output device was found for system audio capture.");
                return false;
            }
            using (device)
            {
                if (device.State != DeviceState.Active)
                {
                    SetState(EngineState.Error, $"'{device.FriendlyName}' is not available ({AudioDeviceService.DescribeState(device.State)}).");
                    return false;
                }
                _capture = mic ? new WasapiCapture(device, true, _bufferMs) : new WasapiLoopbackCapture(device);
                var fmt = _capture.WaveFormat;
                ActiveFormat = fmt;
                ActiveDevice = new AudioDeviceInfo(device.ID, device.FriendlyName, mic, "Ready", true, fmt.SampleRate, fmt.Channels, false);
                _analyzer = new AudioAnalyzer(fmt.SampleRate, Params);
                lock (_ringLock) { _ringHead = 0; _ringCount = 0; }
                _capture.DataAvailable += OnData;
                _capture.RecordingStopped += OnRecordingStopped;
                StartMonitor(en, fmt, mic);
                _lastDataTicks = Stopwatch.GetTimestamp();
                _running = true;
                _analysisThread = new Thread(AnalysisLoop) { IsBackground = true, Name = "AudioAnalysis", Priority = ThreadPriority.AboveNormal };
                _analysisThread.Start();
                _capture.StartRecording();
            }
            Log.Information("Capture started: {Src} on {Dev} ({Rate} Hz, {Ch} ch)", _source, ActiveDevice!.Name, ActiveDevice.SampleRate, ActiveDevice.Channels);
            SetState(EngineState.Capturing, null);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Capture initialisation failed");
            var msg = AudioErrors.Describe(ex, _source == AudioSource.Microphone);
            StopInternal(EngineState.Error, msg);
            return false;
        }
    }

    private void StartMonitor(MMDeviceEnumerator en, WaveFormat fmt, bool mic)
    {
        MonitorMessage = null;
        if (string.IsNullOrEmpty(_monitorDeviceId)) return;
        if (!mic) { MonitorMessage = "Monitoring is only available for microphone input (system audio already plays on its device)."; return; }
        try
        {
            using var md = en.GetDevice(_monitorDeviceId);
            _monitorBuffer = new BufferedWaveProvider(fmt) { DiscardOnBufferOverflow = true };
            _monitorOut = new WasapiOut(md, AudioClientShareMode.Shared, true, 60);
            _monitorOut.Init(_monitorBuffer);
            _monitorOut.Play();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Monitoring could not start");
            MonitorMessage = "Monitoring could not start: " + ex.Message;
            DisposeMonitor();
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _capture is null) return;
        var fmt = _capture.WaveFormat;
        Interlocked.Exchange(ref _lastDataTicks, Stopwatch.GetTimestamp());
        try { _monitorBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded); } catch (Exception) { }
        RawDataAvailable?.Invoke(e.Buffer, e.BytesRecorded, fmt);
        WriteRing(MonoConverter.ToMono(e.Buffer, e.BytesRecorded, fmt));
        _dataSignal.Set();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;
        Log.Warning(e.Exception, "Capture stopped unexpectedly");
        lock (_gate)
        {
            if (!_running || !ReferenceEquals(sender, _capture)) return;
            var text = AudioErrors.Describe(e.Exception, _source == AudioSource.Microphone);
            StopInternal(EngineState.Reconnecting, text + " Trying to reconnect...");
            _retryTimer = new Timer(_ => Retry(), null, 2000, 2000);
        }
    }

    private void Retry()
    {
        lock (_gate)
        {
            if (State != EngineState.Reconnecting) return;
            var t = _retryTimer; _retryTimer = null; t?.Dispose();
            if (TryOpen()) return;
            // TryOpen set Error; keep trying so unplug/replug recovers automatically
            State = EngineState.Reconnecting;
            _retryTimer = new Timer(_ => Retry(), null, 2000, Timeout.Infinite);
        }
    }

    private void WriteRing(float[] samples)
    {
        lock (_ringLock)
        {
            foreach (var s in samples)
            {
                _ring[(_ringHead + _ringCount) & (_ring.Length - 1)] = s;
                if (_ringCount < _ring.Length) _ringCount++; else _ringHead = (_ringHead + 1) & (_ring.Length - 1);
            }
        }
    }

    private int ReadRing(float[] dest)
    {
        lock (_ringLock)
        {
            int n = Math.Min(_ringCount, dest.Length);
            for (int i = 0; i < n; i++) dest[i] = _ring[(_ringHead + i) & (_ring.Length - 1)];
            _ringHead = (_ringHead + n) & (_ring.Length - 1);
            _ringCount -= n;
            return n;
        }
    }

    private void AnalysisLoop()
    {
        var analyzer = _analyzer;
        var scratch = new float[8192];
        long last = Stopwatch.GetTimestamp();
        while (_running)
        {
            _dataSignal.Wait(16);
            _dataSignal.Reset();
            long now = Stopwatch.GetTimestamp();
            double dt = (now - last) / (double)Stopwatch.Frequency;
            if (dt < 0.008) { Thread.Sleep(2); continue; }
            last = now;

            int n = ReadRing(scratch);
            if (n > 0) analyzer.Push(scratch.AsSpan(0, n));
            else if ((now - Interlocked.Read(ref _lastDataTicks)) / (double)Stopwatch.Frequency > 0.06)
                analyzer.PushSilence((int)(dt * analyzer.SampleRate)); // loopback delivers nothing while silent
            try { Volatile.Write(ref _latest, analyzer.Analyze(dt)); }
            catch (Exception ex) { Log.Error(ex, "Analysis error"); }
        }
    }

    private void StopInternal(EngineState newState, string? message)
    {
        _running = false;
        var rt = _retryTimer; _retryTimer = null; rt?.Dispose();
        _dataSignal.Set();
        var cap = _capture; _capture = null;
        if (cap is not null)
        {
            cap.DataAvailable -= OnData; cap.RecordingStopped -= OnRecordingStopped;
            try { cap.StopRecording(); } catch (Exception) { }
            try { cap.Dispose(); } catch (Exception) { }
        }
        DisposeMonitor();
        var t = _analysisThread; _analysisThread = null;
        if (t is not null && t != Thread.CurrentThread) t.Join(500);
        Volatile.Write(ref _latest, AnalysisFrame.Silent);
        if (newState == EngineState.Stopped) { ActiveDevice = null; ActiveFormat = null; }
        SetState(newState, message);
    }

    private void DisposeMonitor()
    {
        try { _monitorOut?.Stop(); _monitorOut?.Dispose(); } catch (Exception) { }
        _monitorOut = null; _monitorBuffer = null;
    }

    private void SetState(EngineState s, string? msg)
    {
        State = s; Message = msg;
        StateChanged?.Invoke(s, msg);
    }

    public void Dispose()
    {
        Stop();
        _dataSignal.Dispose();
    }
}
