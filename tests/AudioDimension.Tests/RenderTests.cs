using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Visualizers;
using Xunit;

namespace AudioDimension.Tests;

/// <summary>Shared hidden GL context; GL calls must stay on one thread, so tests using it run in one collection.</summary>
public sealed class GlFixture : IDisposable
{
    public OffscreenContext? Context { get; }
    public VisualRenderer? Renderer { get; }
    public string? Unavailable { get; }

    public GlFixture()
    {
        try { Context = new OffscreenContext(); Renderer = new VisualRenderer(); }
        catch (Exception ex) { Unavailable = ex.Message; }
    }

    /// <summary>Binds the context to the calling test thread.</summary>
    public bool Begin() { Context?.MakeCurrent(); return Unavailable is null; }

    public void Dispose() { Renderer?.Dispose(); Context?.Dispose(); }
}

[CollectionDefinition("gl", DisableParallelization = true)]
public class GlCollection : ICollectionFixture<GlFixture> { }

/// <summary>Builds analysis frames by running the real analyzer over synthetic tones (test input only).</summary>
public static class TestAudio
{
    public static AnalysisFrame Frame(params (double freq, double amp)[] tones) => Run(tones, 0.6, beatPulse: false);

    /// <summary>Per-frame analysis of a music-like signal: pulsed 60 Hz kick plus steady mid and treble tones.</summary>
    public static List<AnalysisFrame> MusicLike(double seconds)
    {
        const int rate = 48000; const double dt = 1.0 / 60;
        var a = new AudioAnalyzer(rate); a.Params.AutoGain = false;
        int per = (int)(rate * dt); var buf = new float[per]; long pos = 0; var list = new List<AnalysisFrame>();
        for (int t = 0; t < (int)(seconds / dt); t++)
        {
            bool kick = (t % 30) < 6;
            for (int i = 0; i < per; i++, pos++)
            {
                double s = 0.18 * Math.Sin(2 * Math.PI * 880 * pos / rate) + 0.1 * Math.Sin(2 * Math.PI * 5000 * pos / rate) + 0.12 * Math.Sin(2 * Math.PI * 220 * pos / rate);
                if (kick) s += 0.6 * Math.Sin(2 * Math.PI * 60 * pos / rate);
                buf[i] = (float)s;
            }
            a.Push(buf); list.Add(a.Analyze(dt));
        }
        return list;
    }

    public static AnalysisFrame Run((double freq, double amp)[] tones, double seconds, bool beatPulse)
    {
        const int rate = 48000; const double dt = 1.0 / 60;
        var a = new AudioAnalyzer(rate); a.Params.AutoGain = false;
        int per = (int)(rate * dt); var buf = new float[per]; long pos = 0; AnalysisFrame f = AnalysisFrame.Silent;
        for (int t = 0; t < (int)(seconds / dt); t++)
        {
            for (int i = 0; i < per; i++, pos++)
            {
                double s = 0; foreach (var (fr, am) in tones) s += am * Math.Sin(2 * Math.PI * fr * pos / rate);
                buf[i] = (float)s;
            }
            a.Push(buf); f = a.Analyze(dt);
        }
        return f;
    }
}

[Collection("gl")]
public class RenderTests
{
    private readonly GlFixture _gl;
    public RenderTests(GlFixture gl) => _gl = gl;

    private static (int lit, double avg) Stats(byte[] px)
    {
        int lit = 0; double sum = 0;
        for (int i = 0; i < px.Length; i += 4) { int l = px[i] + px[i + 1] + px[i + 2]; sum += l; if (l > 60) lit++; }
        return (lit, sum / (px.Length / 4));
    }

    [Fact]
    public void GlContextAndCoreShadersCompile()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        Assert.False(string.IsNullOrEmpty(_gl.Renderer!.Gpu.Renderer));
    }

    [Fact]
    public void ClassicSpectrumRendersAudioDrivenPixels()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(480, 270);
        var cfg = new VisualConfig();
        var viz = new ClassicSpectrum();
        r.Render(viz, cfg, AnalysisFrame.Silent, 0.016, tgt.Fbo, tgt.Width, tgt.Height);
        var silent = Stats(tgt.ReadPixels());
        r.Render(viz, cfg, TestAudio.Frame((100, 0.5), (3000, 0.4)), 0.016, tgt.Fbo, tgt.Width, tgt.Height);
        var loud = Stats(tgt.ReadPixels());
        Assert.Null(r.LastError);
        Assert.True(loud.lit > silent.lit + 200, $"audio should light more pixels (silent {silent.lit}, loud {loud.lit})");
    }
}
