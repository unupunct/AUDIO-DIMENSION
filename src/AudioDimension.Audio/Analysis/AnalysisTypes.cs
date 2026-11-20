namespace AudioDimension.Audio.Analysis;

/// <summary>User-adjustable analysis parameters. Read by the analysis thread; plain fields are fine for single-value updates.</summary>
public sealed class AudioParams
{
    public double InputGain { get; set; } = 1.0;        // linear multiplier
    public double Sensitivity { get; set; } = 1.0;
    public double NoiseGateDb { get; set; } = -80;      // below this input level, output is silenced
    public double Smoothing { get; set; } = 0.6;        // 0 = instant, 0.95 = very slow release
    public double BassSensitivity { get; set; } = 1.0;
    public double MidSensitivity { get; set; } = 1.0;
    public double TrebleSensitivity { get; set; } = 1.0;
    public double FrequencyTilt { get; set; }           // -1..+1, +/-3 dB per octave around 1 kHz
    public bool AutoGain { get; set; } = true;
    public bool PeakDetection { get; set; } = true;
    /// <summary>Which of the six frequency bands feed the output; disabled bands read zero.</summary>
    public bool[] BandEnabled { get; } = { true, true, true, true, true, true };

    /// <summary>Independent copy, so an offline render keeps the exact analysis settings that were active when it started.</summary>
    public AudioParams Clone()
    {
        var c = (AudioParams)MemberwiseClone();
        for (int i = 0; i < BandEnabled.Length; i++) c.BandEnabled[i] = BandEnabled[i];
        return c;
    }
}

public static class FrequencyBands
{
    public const int Count = 6;
    public static readonly string[] Names = { "Sub-bass", "Bass", "Low-mid", "Mid", "High-mid", "Treble" };
    /// <summary>Band edges in Hz; band i spans Edges[i]..Edges[i+1].</summary>
    public static readonly double[] Edges = { 20, 60, 250, 500, 2000, 4000, 16000 };
}

/// <summary>Immutable snapshot of one analysis tick, safe to read from the render thread.</summary>
public sealed class AnalysisFrame
{
    public static readonly AnalysisFrame Silent = new(0, 48000, -120, 0, 0, 0, false, false, 0,
        new float[FrequencyBands.Count], new float[AudioAnalyzer.SpectrumBins], new float[AudioAnalyzer.WaveformPoints]);

    public AnalysisFrame(double audioTime, int sampleRate, double rmsDb, float level, float peak, float peakHold,
        bool isActive, bool isBeat, float beatStrength, float[] bands, float[] spectrum, float[] waveform)
    {
        AudioTimeSeconds = audioTime; SampleRate = sampleRate; RmsDb = rmsDb; Level = level; Peak = peak;
        PeakHold = peakHold; IsActive = isActive; IsBeat = isBeat; BeatStrength = beatStrength;
        Bands = bands; Spectrum = spectrum; Waveform = waveform;
    }

    /// <summary>Position on the audio clock (total captured samples / sample rate).</summary>
    public double AudioTimeSeconds { get; }
    public int SampleRate { get; }
    public double RmsDb { get; }
    /// <summary>0..1 meter value (-60 dB..0 dB).</summary>
    public float Level { get; }
    /// <summary>Instantaneous linear peak, 0..1+.</summary>
    public float Peak { get; }
    public float PeakHold { get; }
    public bool IsActive { get; }
    public bool IsBeat { get; }
    public float BeatStrength { get; }
    /// <summary>6 smoothed band energies, 0..1 (sub-bass .. treble).</summary>
    public float[] Bands { get; }
    /// <summary>Log-spaced smoothed spectrum, 0..1.</summary>
    public float[] Spectrum { get; }
    /// <summary>Recent waveform, -1..1.</summary>
    public float[] Waveform { get; }

    public float Bass => Math.Max(Bands[0], Bands[1]);
    public float Mid => Math.Max(Bands[2], Bands[3]);
    public float Treble => Math.Max(Bands[4], Bands[5]);
}
