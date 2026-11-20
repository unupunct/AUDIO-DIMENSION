namespace AudioDimension.Audio.Analysis;

/// <summary>
/// Turns mono PCM into stable visual-friendly data: FFT spectrum, six frequency bands, level/peak meters,
/// noise gate, auto gain and beat detection. Single-threaded: call Push/Analyze from one thread only.
/// </summary>
public sealed class AudioAnalyzer
{
    public const int FftSize = 2048;
    public const int SpectrumBins = 64;
    public const int WaveformPoints = 512;
    private const double DbFloor = -70.0;
    private const double SpectrumLowHz = 30, SpectrumHighHz = 16000;

    public AudioParams Params { get; }
    public int SampleRate { get; }

    private readonly FftProcessor _fft = new(FftSize);
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _mags = new float[FftSize / 2];
    private readonly float[] _weights = new float[FftSize / 2];
    private readonly float[] _bandSmooth = new float[FrequencyBands.Count];
    private readonly float[] _specSmooth = new float[SpectrumBins];
    private readonly double[] _specEdges = new double[SpectrumBins + 1];
    private readonly double _binHz;
    private double _cachedTilt = double.NaN, _cachedBass = double.NaN, _cachedMid = double.NaN, _cachedTreble = double.NaN;

    private long _totalSamples;
    private bool _gateOpen;
    private double _agcEnv = 1e-4, _agcGainLog;
    private double _peakHold, _peakHoldAge;
    private double _bassAvg, _sinceBeat = 10;

    public AudioAnalyzer(int sampleRate, AudioParams? parameters = null)
    {
        SampleRate = sampleRate;
        Params = parameters ?? new AudioParams();
        _binHz = (double)sampleRate / FftSize;
        for (int i = 0; i <= SpectrumBins; i++)
            _specEdges[i] = SpectrumLowHz * Math.Pow(Math.Min(SpectrumHighHz, sampleRate / 2.0 - 1) / SpectrumLowHz, (double)i / SpectrumBins);
    }

    public double AudioTime => (double)_totalSamples / SampleRate;

    /// <summary>Appends new mono samples (newest last) to the sliding analysis window.</summary>
    public void Push(ReadOnlySpan<float> mono)
    {
        if (mono.Length == 0) return;
        _totalSamples += mono.Length;
        if (mono.Length >= FftSize)
        {
            mono.Slice(mono.Length - FftSize).CopyTo(_window);
            return;
        }
        Array.Copy(_window, mono.Length, _window, 0, FftSize - mono.Length);
        mono.CopyTo(_window.AsSpan(FftSize - mono.Length));
    }

    /// <summary>Pushes silence (used when a loopback device delivers no packets because nothing is playing).</summary>
    public void PushSilence(int samples)
    {
        if (samples <= 0) return;
        Span<float> z = samples >= FftSize ? new float[FftSize] : stackalloc float[samples];
        Push(z);
    }

    public AnalysisFrame Analyze(double dt)
    {
        dt = Math.Clamp(dt, 0.001, 0.25);
        var p = Params;
        float gain = (float)Math.Max(0, p.InputGain);

        // ---- time-domain metering on newest 1024 samples
        const int meterLen = 1024;
        double sumSq = 0; float peak = 0;
        for (int i = FftSize - meterLen; i < FftSize; i++)
        {
            float v = _window[i] * gain;
            sumSq += v * v;
            float a = MathF.Abs(v);
            if (a > peak) peak = a;
        }
        double rms = Math.Sqrt(sumSq / meterLen);
        double rmsDb = rms > 1e-7 ? 20 * Math.Log10(rms) : -140;

        // ---- noise gate with 3 dB hysteresis
        if (_gateOpen) { if (rmsDb < p.NoiseGateDb - 3) _gateOpen = false; }
        else if (rmsDb > p.NoiseGateDb) _gateOpen = true;
        bool active = _gateOpen && rmsDb > -65;

        // ---- peak hold
        _peakHoldAge += dt;
        if (peak >= _peakHold || _peakHoldAge > 1.2)
        {
            if (peak >= _peakHold) { _peakHold = peak; _peakHoldAge = 0; }
            else _peakHold = Math.Max(peak, _peakHold - dt * 1.5);
        }

        // ---- spectrum
        UpdateWeights();
        _fft.ComputeMagnitudes(_window, _mags);
        double rawPeak = 0;
        for (int k = 1; k < _mags.Length; k++)
        {
            _mags[k] *= gain;
            if (_mags[k] > rawPeak) rawPeak = _mags[k];
        }

        // ---- auto gain (slow, only while signal present)
        double agc = 1;
        if (p.AutoGain)
        {
            if (active)
            {
                _agcEnv = Math.Max(rawPeak, _agcEnv * Math.Exp(-dt / 5.0));
                double target = Math.Log(Math.Clamp(0.2 / Math.Max(_agcEnv, 1e-4), 1, 40));
                double tc = target < _agcGainLog ? 0.15 : 2.0;
                _agcGainLog += (target - _agcGainLog) * (1 - Math.Exp(-dt / tc));
            }
            agc = Math.Exp(_agcGainLog);
        }
        else { _agcGainLog = 0; _agcEnv = 1e-4; }

        float total = (float)(Math.Max(0, p.Sensitivity) * agc);

        Span<float> rawBands = stackalloc float[FrequencyBands.Count];
        Span<float> rawSpec = stackalloc float[SpectrumBins];
        if (_gateOpen)
        {
            for (int b = 0; b < FrequencyBands.Count; b++)
            {
                int lo = Math.Max(1, (int)Math.Ceiling(FrequencyBands.Edges[b] / _binHz));
                int hi = Math.Min(_mags.Length - 1, (int)Math.Floor(FrequencyBands.Edges[b + 1] / _binHz));
                float m = 0;
                for (int k = lo; k <= Math.Max(lo, hi); k++) m = Math.Max(m, _mags[Math.Min(k, _mags.Length - 1)] * _weights[Math.Min(k, _mags.Length - 1)]);
                rawBands[b] = ToUnit(m * total);
            }
            for (int s = 0; s < SpectrumBins; s++)
            {
                double lo = _specEdges[s] / _binHz, hi = _specEdges[s + 1] / _binHz;
                float m;
                if (hi - lo < 1.0)
                {
                    double c = (lo + hi) / 2; int i0 = Math.Clamp((int)c, 1, _mags.Length - 2);
                    float f = (float)(c - i0);
                    m = (_mags[i0] * _weights[i0]) * (1 - f) + (_mags[i0 + 1] * _weights[i0 + 1]) * f;
                }
                else
                {
                    m = 0;
                    int a = Math.Max(1, (int)Math.Ceiling(lo)), z = Math.Min(_mags.Length - 1, (int)Math.Floor(hi));
                    for (int k = a; k <= z; k++) m = Math.Max(m, _mags[k] * _weights[k]);
                }
                rawSpec[s] = ToUnit(m * total);
            }
        }

        // ---- band selection
        for (int b = 0; b < FrequencyBands.Count; b++)
            if (!p.BandEnabled[b]) rawBands[b] = 0;
        for (int s = 0; s < SpectrumBins; s++)
        {
            double fc = Math.Sqrt(_specEdges[s] * _specEdges[s + 1]);
            for (int b = 0; b < FrequencyBands.Count; b++)
                if (fc >= FrequencyBands.Edges[b] && fc < FrequencyBands.Edges[b + 1] && !p.BandEnabled[b]) rawSpec[s] = 0;
        }

        // ---- beat detection on raw bass energy
        float bassRaw = Math.Max(rawBands[0], rawBands[1]);
        _sinceBeat += dt;
        bool beat = false; float beatStrength = 0;
        if (p.PeakDetection)
        {
            _bassAvg += (bassRaw - _bassAvg) * (1 - Math.Exp(-dt / 1.0));
            if (bassRaw > 0.2 && bassRaw > _bassAvg * 1.35 + 0.04 && _sinceBeat > 0.15)
            {
                beat = true; _sinceBeat = 0;
                beatStrength = (float)Math.Clamp(bassRaw / Math.Max(_bassAvg, 0.05) - 1, 0, 3) / 3f;
            }
        }

        // ---- smoothing (attack fast, release scaled by Smoothing, independent of frame rate)
        double s0 = Math.Clamp(p.Smoothing, 0, 0.98);
        float atk = (float)(1 - Math.Exp(-dt / (0.004 + 0.03 * s0)));
        float rel = (float)(1 - Math.Exp(-dt / (0.02 + 0.5 * s0)));
        for (int i = 0; i < FrequencyBands.Count; i++)
            _bandSmooth[i] += (rawBands[i] - _bandSmooth[i]) * (rawBands[i] > _bandSmooth[i] ? atk : rel);
        for (int i = 0; i < SpectrumBins; i++)
            _specSmooth[i] += (rawSpec[i] - _specSmooth[i]) * (rawSpec[i] > _specSmooth[i] ? atk : rel);

        // ---- waveform snapshot (newest samples, decimated)
        var wave = new float[WaveformPoints];
        int stride = FftSize / WaveformPoints;
        for (int i = 0; i < WaveformPoints; i++)
            wave[i] = Math.Clamp(_window[i * stride] * gain, -1f, 1f);

        float level = (float)Math.Clamp((rmsDb + 60) / 60, 0, 1);
        return new AnalysisFrame(AudioTime, SampleRate, rmsDb, level, peak, (float)_peakHold, active, beat, beatStrength,
            (float[])_bandSmooth.Clone(), (float[])_specSmooth.Clone(), wave);
    }

    private static float ToUnit(float linear)
    {
        if (linear <= 1e-6f) return 0;
        float db = 20f * MathF.Log10(linear);
        return Math.Clamp((db - (float)DbFloor) / (float)-DbFloor, 0f, 1f);
    }

    /// <summary>Per-FFT-bin multiplier combining frequency tilt and bass/mid/treble sensitivity.</summary>
    private void UpdateWeights()
    {
        var p = Params;
        if (p.FrequencyTilt == _cachedTilt && p.BassSensitivity == _cachedBass &&
            p.MidSensitivity == _cachedMid && p.TrebleSensitivity == _cachedTreble) return;
        _cachedTilt = p.FrequencyTilt; _cachedBass = p.BassSensitivity; _cachedMid = p.MidSensitivity; _cachedTreble = p.TrebleSensitivity;
        for (int k = 1; k < _weights.Length; k++)
        {
            double f = k * _binHz;
            double octaves = Math.Log2(f / 1000.0);
            double tilt = Math.Pow(10, p.FrequencyTilt * 3.0 * octaves / 20.0);
            // piecewise-linear in log-frequency through 100 Hz (bass), 1 kHz (mid), 6 kHz (treble)
            double lf = Math.Log10(f), l1 = 2, l2 = 3, l3 = Math.Log10(6000);
            double s = lf <= l1 ? p.BassSensitivity
                : lf <= l2 ? Lerp(p.BassSensitivity, p.MidSensitivity, (lf - l1) / (l2 - l1))
                : lf <= l3 ? Lerp(p.MidSensitivity, p.TrebleSensitivity, (lf - l2) / (l3 - l2))
                : p.TrebleSensitivity;
            _weights[k] = (float)(tilt * Math.Max(0, s));
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
