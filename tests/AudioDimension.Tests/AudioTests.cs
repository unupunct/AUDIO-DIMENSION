using AudioDimension.Audio.Analysis;
using AudioDimension.Audio.Capture;
using AudioDimension.Audio.Devices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using Xunit;

namespace AudioDimension.Tests;

public class FftTests
{
    private static float[] Sine(double freq, int rate, int n, double amp = 1.0)
    {
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / rate));
        return s;
    }

    [Fact]
    public void SinePeaksAtExpectedBinWithUnitAmplitude()
    {
        const int rate = 48000, size = 2048;
        double freq = 64 * (double)rate / size; // bin-centred
        var fft = new FftProcessor(size);
        var mags = new float[size / 2];
        fft.ComputeMagnitudes(Sine(freq, rate, size), mags);
        int peak = Array.IndexOf(mags, mags.Max());
        Assert.Equal(64, peak);
        Assert.InRange(mags[peak], 0.95f, 1.05f);
    }

    [Fact]
    public void RejectsNonPowerOfTwo() => Assert.Throws<ArgumentException>(() => new FftProcessor(1000));

    [Fact]
    public void HalfAmplitudeReadsHalf()
    {
        const int rate = 48000, size = 2048;
        var fft = new FftProcessor(size);
        var mags = new float[size / 2];
        fft.ComputeMagnitudes(Sine(32 * (double)rate / size, rate, size, 0.5), mags);
        Assert.InRange(mags.Max(), 0.47f, 0.53f);
    }
}

public class AnalyzerTests
{
    private const int Rate = 48000;

    private static AnalysisFrame Feed(AudioAnalyzer a, double freq, double amp, double seconds, long startSample)
    {
        const double dt = 1.0 / 60;
        AnalysisFrame f = AnalysisFrame.Silent;
        int per = (int)(Rate * dt);
        long pos = startSample;
        var buf = new float[per];
        for (int t = 0; t < (int)(seconds / dt); t++)
        {
            for (int i = 0; i < per; i++, pos++) buf[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * pos / Rate));
            a.Push(buf);
            f = a.Analyze(dt);
        }
        return f;
    }

    [Theory]
    [InlineData(100, 1)]    // bass
    [InlineData(1000, 3)]   // mid
    [InlineData(8000, 5)]   // treble
    [InlineData(40, 0)]     // sub-bass
    public void ToneLandsInExpectedBand(double freq, int expectedBand)
    {
        var a = new AudioAnalyzer(Rate);
        a.Params.AutoGain = false;
        var f = Feed(a, freq, 0.5, 1.0, 0);
        int max = Array.IndexOf(f.Bands, f.Bands.Max());
        Assert.Equal(expectedBand, max);
        Assert.True(f.Bands[expectedBand] > 0.5f);
        Assert.True(f.IsActive);
    }

    [Fact]
    public void SilenceProducesNoActivity()
    {
        var a = new AudioAnalyzer(Rate);
        var f = Feed(a, 440, 0.0, 0.5, 0);
        Assert.False(f.IsActive);
        Assert.All(f.Bands, b => Assert.Equal(0f, b));
        Assert.All(f.Spectrum, b => Assert.Equal(0f, b));
    }

    [Fact]
    public void NoiseGateSilencesQuietInput()
    {
        var a = new AudioAnalyzer(Rate);
        a.Params.NoiseGateDb = -20;
        a.Params.AutoGain = false;
        var f = Feed(a, 440, 0.01, 0.5, 0); // about -43 dB
        Assert.False(f.IsActive);
        Assert.All(f.Bands, b => Assert.True(b < 0.01f));
    }

    [Fact]
    public void SensitivityRaisesResponse()
    {
        var lo = new AudioAnalyzer(Rate); lo.Params.AutoGain = false; lo.Params.Sensitivity = 0.5;
        var hi = new AudioAnalyzer(Rate); hi.Params.AutoGain = false; hi.Params.Sensitivity = 2.0;
        Assert.True(Feed(hi, 200, 0.05, 0.5, 0).Bands[1] > Feed(lo, 200, 0.05, 0.5, 0).Bands[1]);
    }

    [Fact]
    public void BassSensitivityOnlyAffectsBass()
    {
        var a = new AudioAnalyzer(Rate); a.Params.AutoGain = false;
        var b = new AudioAnalyzer(Rate); b.Params.AutoGain = false; b.Params.BassSensitivity = 0.2;
        Assert.True(Feed(a, 100, 0.2, 0.5, 0).Bands[1] > Feed(b, 100, 0.2, 0.5, 0).Bands[1]);
        Assert.Equal(Feed(a, 8000, 0.2, 0.5, 0).Bands[5], Feed(b, 8000, 0.2, 0.5, 0).Bands[5], 2);
    }

    [Fact]
    public void AutoGainBoostsQuietSignal()
    {
        var off = new AudioAnalyzer(Rate); off.Params.AutoGain = false;
        var on = new AudioAnalyzer(Rate); on.Params.AutoGain = true;
        var fo = Feed(off, 440, 0.01, 6, 0);
        var fn = Feed(on, 440, 0.01, 6, 0);
        Assert.True(fn.Bands.Max() > fo.Bands.Max());
    }

    [Fact]
    public void SmoothingSlowsRelease()
    {
        float Decay(double smoothing)
        {
            var a = new AudioAnalyzer(Rate); a.Params.AutoGain = false; a.Params.Smoothing = smoothing;
            Feed(a, 100, 0.5, 0.5, 0);
            return Feed(a, 100, 0.0, 0.1, 0).Bands[1];
        }
        Assert.True(Decay(0.9) > Decay(0.0));
    }

    [Fact]
    public void BeatDetectedOnBassPulses()
    {
        var a = new AudioAnalyzer(Rate); a.Params.AutoGain = false;
        const double dt = 1.0 / 60; int per = (int)(Rate * dt); long pos = 0; int beats = 0;
        var buf = new float[per];
        for (int t = 0; t < 60 * 4; t++)
        {
            bool loud = (t % 30) < 5;      // 2 pulses per second
            for (int i = 0; i < per; i++, pos++) buf[i] = loud ? (float)(0.7 * Math.Sin(2 * Math.PI * 60 * pos / Rate)) : 0f;
            a.Push(buf);
            if (a.Analyze(dt).IsBeat) beats++;
        }
        Assert.InRange(beats, 5, 9);
    }

    [Fact]
    public void SpectrumPeakFollowsFrequency()
    {
        var a = new AudioAnalyzer(Rate); a.Params.AutoGain = false;
        int PeakBin(double f) { var fr = Feed(a, f, 0.5, 0.6, 0); return Array.IndexOf(fr.Spectrum, fr.Spectrum.Max()); }
        int low = PeakBin(200), high = PeakBin(5000);
        Assert.True(high > low + 15);
    }

    [Fact]
    public void AudioClockAdvancesWithSamples()
    {
        var a = new AudioAnalyzer(Rate);
        a.Push(new float[Rate / 2]);
        Assert.Equal(0.5, a.AudioTime, 3);
    }
}

public class ConversionTests
{
    [Fact]
    public void Pcm16StereoMixesToMono()
    {
        var fmt = new WaveFormat(48000, 16, 2);
        var bytes = new byte[8];
        BitConverter.GetBytes((short)16384).CopyTo(bytes, 0);   // L = 0.5
        BitConverter.GetBytes((short)-16384).CopyTo(bytes, 2);  // R = -0.5
        BitConverter.GetBytes((short)16384).CopyTo(bytes, 4);
        BitConverter.GetBytes((short)16384).CopyTo(bytes, 6);
        var mono = MonoConverter.ToMono(bytes, 8, fmt);
        Assert.Equal(2, mono.Length);
        Assert.Equal(0f, mono[0], 3);
        Assert.Equal(0.5f, mono[1], 3);
    }

    [Fact]
    public void FloatMonoPassesThrough()
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        var bytes = new byte[8];
        BitConverter.GetBytes(0.25f).CopyTo(bytes, 0);
        BitConverter.GetBytes(-0.75f).CopyTo(bytes, 4);
        var mono = MonoConverter.ToMono(bytes, 8, fmt);
        Assert.Equal(new[] { 0.25f, -0.75f }, mono);
    }
}

public class DeviceTests
{
    [Fact]
    public void EnumerationNeverThrowsAndReportsFields()
    {
        var inputs = AudioDeviceService.Enumerate(true);
        var outputs = AudioDeviceService.Enumerate(false);
        Assert.NotNull(inputs); Assert.NotNull(outputs);
        foreach (var d in inputs.Concat(outputs)) { Assert.False(string.IsNullOrEmpty(d.Id)); Assert.False(string.IsNullOrEmpty(d.Name)); }
    }

    [Fact]
    public void MissingDeviceGivesReadableError()
    {
        using var engine = new AudioEngine();
        engine.Start(AudioSource.SystemAudio, "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}");
        Assert.Equal(EngineState.Error, engine.State);
        Assert.False(string.IsNullOrWhiteSpace(engine.Message));
        engine.Stop();
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public void LoopbackCaptureStartsAndStops()
    {
        if (AudioDeviceService.Enumerate(false).All(d => !d.IsUsable)) return; // no output device on this machine
        using var engine = new AudioEngine();
        engine.Start(AudioSource.SystemAudio, "");
        Assert.Equal(EngineState.Capturing, engine.State);
        Assert.True(engine.SampleRate > 0);
        Thread.Sleep(400);
        Assert.NotNull(engine.Latest);   // analysis thread publishes frames even when nothing is playing
        engine.Stop();
        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public void LoopbackHearsPlayedTone()
    {
        // Plays a brief, quiet 440 Hz tone on the default output and verifies loopback analysis sees it.
        if (AudioDeviceService.Enumerate(false).All(d => !d.IsUsable)) return;
        using var engine = new AudioEngine();
        engine.Params.AutoGain = false;
        engine.Start(AudioSource.SystemAudio, "");
        Assert.Equal(EngineState.Capturing, engine.State);
        var gen = new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.08, Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin };
        using (var outp = new WasapiOut())
        {
            outp.Init(gen.Take(TimeSpan.FromSeconds(3)));
            outp.Play();
            AnalysisFrame f = engine.Latest;
            for (int i = 0; i < 40 && !(f.IsActive && (f.Bands[3] > 0.2f || f.Bands[2] > 0.2f)); i++) { Thread.Sleep(50); f = engine.Latest; }
            Assert.True(f.IsActive, $"expected activity, rms={f.RmsDb:0.0} dB");
            Assert.True(f.Bands[3] > 0.2f || f.Bands[2] > 0.2f, "440 Hz should energise low-mid/mid bands");
            outp.Stop();
        }
        engine.Stop();
    }

    [Fact]
    public void MicrophoneCaptureStartsOrReportsReadableError()
    {
        if (AudioDeviceService.Enumerate(true).All(d => !d.IsUsable)) return; // no microphone on this machine
        using var engine = new AudioEngine();
        engine.Start(AudioSource.Microphone, "");
        if (engine.State == EngineState.Capturing) { Thread.Sleep(300); engine.Stop(); }
        else Assert.False(string.IsNullOrWhiteSpace(engine.Message));
    }

    [Fact]
    public void ErrorTranslation()
    {
        var ex = new System.Runtime.InteropServices.COMException("x", AudioErrors.DeviceInvalidated);
        Assert.Contains("disconnected", AudioErrors.Describe(ex, true));
        Assert.Contains("microphone", AudioErrors.Describe(new UnauthorizedAccessException(), true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PermissionCheckDoesNotThrow() => _ = AudioErrors.CheckMicrophonePermission();
}
