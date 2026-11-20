using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Visualizers;
using Xunit;

namespace AudioDimension.Tests;

[Collection("gl")]
public class VisualizerTests
{
    private readonly GlFixture _gl;
    public VisualizerTests(GlFixture gl) => _gl = gl;

    public static IEnumerable<object[]> AllIds() => VisualizerRegistry.Ids.Select(i => new object[] { i });

    private static byte[] RunFrames(VisualRenderer r, Visualizer v, VisualConfig cfg, IReadOnlyList<AnalysisFrame> frames, OffscreenTarget t)
    {
        v.Reset(); r.ResetTime();
        foreach (var f in frames) r.Render(v, cfg, f, 1.0 / 60, t.Fbo, t.Width, t.Height);
        return t.ReadPixels();
    }

    private static long Lit(byte[] px) { long n = 0; for (int i = 0; i < px.Length; i += 4) if (px[i] + px[i + 1] + px[i + 2] > 45) n++; return n; }
    private static long Diff(byte[] a, byte[] b) { long d = 0; for (int i = 0; i < a.Length; i += 4) d += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]); return d; }

    [Fact]
    public void RegistryHasAtLeastTwentyVisualizers() => Assert.True(VisualizerRegistry.Ids.Count >= 20);

    [Theory]
    [MemberData(nameof(AllIds))]
    public void VisualizerRendersAndRespondsToAudio(string id)
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(480, 270);
        var viz = VisualizerRegistry.Create(id);
        var cfg = new VisualConfig(); cfg.Camera.Reset(viz.DefaultCamera);
        var music = TestAudio.MusicLike(1.6);
        var silence = music.Select(_ => AnalysisFrame.Silent).ToList();

        var quiet = RunFrames(r, viz, cfg, silence, tgt);
        var loud = RunFrames(r, viz, cfg, music, tgt);
        Assert.Null(r.LastError);
        Assert.True(Lit(loud) > 300, $"{id}: loud frame is nearly blank ({Lit(loud)} lit px)");
        Assert.True(Diff(quiet, loud) > 40000, $"{id}: output barely changed with audio (diff {Diff(quiet, loud)})");

        var dir = Environment.GetEnvironmentVariable("AD_DUMP_DIR");
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); PngWriter.Save(Path.Combine(dir, id + ".png"), loud, tgt.Width, tgt.Height); }
    }

    [Fact]
    public void PresetKnobsChangeTheImage()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(320, 180);
        var viz = new ClassicSpectrum(); var music = TestAudio.MusicLike(0.5);
        var a = RunFrames(r, viz, new VisualConfig(), music, tgt);
        var cfg = new VisualConfig(); cfg.Palette.Colors = new[] { "#FF0000", "#FFAA00" }; cfg.Params.Segments = 16;
        var b = RunFrames(r, viz, cfg, music, tgt);
        Assert.True(Diff(a, b) > 100000);
    }

    [Fact]
    public void EveryEffectCompilesAndRenders()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(320, 180);
        var cfg = new VisualConfig();
        var fx = cfg.Effects;
        fx.ChromaticAberration = fx.RgbSeparation = fx.FilmGrain = fx.Scanlines = fx.ColorGrading = fx.Distortion = fx.Blur = fx.Noise = fx.MotionBlur = fx.LightRays = true;
        var px = RunFrames(r, new CircularSpectrum(), cfg, TestAudio.MusicLike(0.5), tgt);
        Assert.Null(r.LastError);
        Assert.True(Lit(px) > 300);
        cfg.Background.Mode = BackgroundMode.Transparent;
        var px2 = RunFrames(r, new CircularSpectrum(), cfg, TestAudio.MusicLike(0.3), tgt);
        bool anyTransparent = false; for (int i = 3; i < px2.Length; i += 4) if (px2[i] < 128) { anyTransparent = true; break; }
        Assert.True(anyTransparent, "transparent background should leave transparent pixels");
    }

    [Theory]
    [InlineData(BackgroundMode.Solid)] [InlineData(BackgroundMode.Gradient)] [InlineData(BackgroundMode.AnimatedGradient)]
    [InlineData(BackgroundMode.Starfield)] [InlineData(BackgroundMode.ParticleField)]
    public void BackgroundModesRender(BackgroundMode mode)
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(256, 144);
        var cfg = new VisualConfig(); cfg.Background.Mode = mode; cfg.Background.Color1 = "#203050"; cfg.Background.Color2 = "#5030A0";
        var px = RunFrames(r, new Oscilloscope(), cfg, TestAudio.MusicLike(0.3), tgt);
        Assert.Null(r.LastError);
        Assert.True(Lit(px) > 500, $"{mode} should produce visible pixels");
    }

    [Theory]
    [InlineData(QualityLevel.Low)] [InlineData(QualityLevel.Medium)] [InlineData(QualityLevel.High)] [InlineData(QualityLevel.Ultra)]
    public void QualityLevelsRender(QualityLevel q)
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(640, 360);
        var cfg = new VisualConfig { Quality = QualitySettings.For(q) };
        var px = RunFrames(r, new ParticleGalaxy(), cfg, TestAudio.MusicLike(0.4), tgt);
        Assert.Null(r.LastError);
        Assert.True(Lit(px) > 300);
    }

    [Fact]
    public void ParticleBudgetFollowsQualityAndDensity()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(320, 180);
        var music = TestAudio.MusicLike(0.4);
        var lo = new VisualConfig { Quality = QualitySettings.For(QualityLevel.Low) };
        RunFrames(r, new ParticleGalaxy(), lo, music, tgt); int lowVerts = r.LastVertexCount;
        var hi = new VisualConfig { Quality = QualitySettings.For(QualityLevel.Ultra) };
        RunFrames(r, new ParticleGalaxy(), hi, music, tgt); int highVerts = r.LastVertexCount;
        Assert.True(highVerts > lowVerts * 2, $"low {lowVerts}, ultra {highVerts}");
    }

    [Fact]
    public void BrokenFragmentShaderIsReportedNotFatal()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(128, 72);
        r.Render(new BrokenShaderVisualizer(), new VisualConfig(), AnalysisFrame.Silent, 0.016, tgt.Fbo, tgt.Width, tgt.Height);
        Assert.NotNull(r.LastError);
        Assert.Contains("failed to compile", r.LastError);
    }

    [Fact]
    public void ExportStyleRenderingIsDeterministic()
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(240, 135);
        var music = TestAudio.MusicLike(0.8);
        var viz = new ParticleExplosion();
        var a = RunFrames(r, viz, new VisualConfig(), music, tgt);
        var b = RunFrames(r, viz, new VisualConfig(), music, tgt);
        Assert.Equal(0, Diff(a, b));
    }

    private sealed class BrokenShaderVisualizer : Visualizer
    {
        public override string Id => "broken"; public override string Name => "Broken"; public override string Description => "";
        public override void Draw(Canvas c, FrameInput f) => c.Fullscreen("broken", "void main(){ this is not glsl }", _ => { });
    }
}
