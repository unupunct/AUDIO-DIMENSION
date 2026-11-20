using System.Text.Json;
using AudioDimension.Audio.Analysis;
using AudioDimension.Core.Paths;
using AudioDimension.Presets;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Text;
using AudioDimension.Visuals.Visualizers;
using Xunit;

namespace AudioDimension.Tests;

public class TextAnimationTests
{
    private static TextLayer WithAnim(TextAnimType type, double dur = 1, double delay = 0.5, Easing e = Easing.Linear, bool loop = false, AnimDirection dir = AnimDirection.Forward)
    {
        var l = new TextLayer();
        l.Animations.Add(new TextAnimation { Type = type, Duration = dur, Delay = delay, Easing = e, Loop = loop, Direction = dir });
        return l;
    }

    private static bool Differs(TextState a, TextState b) =>
        Math.Abs(a.Alpha - b.Alpha) > 1e-3 || Math.Abs(a.OffsetX - b.OffsetX) > 1e-3 || Math.Abs(a.OffsetY - b.OffsetY) > 1e-3 || Math.Abs(a.Scale - b.Scale) > 1e-3
        || Math.Abs(a.Rotation - b.Rotation) > 1e-3 || Math.Abs(a.Blur - b.Blur) > 1e-3 || Math.Abs(a.Reveal - b.Reveal) > 1e-3 || Math.Abs(a.LetterReveal - b.LetterReveal) > 1e-3
        || a.Glitch > 1e-3 || a.RgbSplit > 1e-3 || a.Sweep >= 0 || a.WaveAmp > 1e-3;

    [Fact]
    public void AllTwentyRequestedAnimationTypesExist() => Assert.Equal(20, Enum.GetValues<TextAnimType>().Length);

    [Theory]
    [MemberData(nameof(Types))]
    public void EveryAnimationTypeChangesTheLayerOverTime(TextAnimType type)
    {
        var l = WithAnim(type, 1, 0.5, Easing.Linear, loop: TextAnimator.IsContinuous(type));
        var rest = TextState.Rest;
        bool changed = false;
        for (double t = 0; t < 6; t += 0.04)
        {
            var s = TextAnimator.Evaluate(l, t);
            Assert.InRange(s.Alpha, 0f, 1f);
            Assert.True(float.IsFinite(s.OffsetX) && float.IsFinite(s.OffsetY) && float.IsFinite(s.Scale) && float.IsFinite(s.Rotation));
            if (Differs(s, rest)) changed = true;
        }
        Assert.True(changed, $"{type} never altered the layer");
    }

    public static IEnumerable<object[]> Types() => Enum.GetValues<TextAnimType>().Select(t => new object[] { t });

    [Fact]
    public void FadeInRunsFromDelayForDuration()
    {
        var l = WithAnim(TextAnimType.FadeIn, 2, 1);
        Assert.Equal(0f, TextAnimator.Evaluate(l, 0).Alpha, 3);
        Assert.Equal(0f, TextAnimator.Evaluate(l, 1).Alpha, 3);
        Assert.Equal(0.5f, TextAnimator.Evaluate(l, 2).Alpha, 3);
        Assert.Equal(1f, TextAnimator.Evaluate(l, 3).Alpha, 3);
        Assert.Equal(1f, TextAnimator.Evaluate(l, 30).Alpha, 3);
    }

    [Fact]
    public void FadeOutIsOpaqueUntilItsDelayThenVanishes()
    {
        var l = WithAnim(TextAnimType.FadeOut, 1, 4);
        Assert.Equal(1f, TextAnimator.Evaluate(l, 3.9).Alpha, 3);
        Assert.Equal(0.5f, TextAnimator.Evaluate(l, 4.5).Alpha, 3);
        Assert.Equal(0f, TextAnimator.Evaluate(l, 6).Alpha, 3);
    }

    [Theory]
    [InlineData(TextAnimType.SlideLeft)] [InlineData(TextAnimType.SlideRight)] [InlineData(TextAnimType.SlideUp)] [InlineData(TextAnimType.SlideDown)]
    public void SlidesStartOffsetInTheRightDirectionAndLandAtZero(TextAnimType type)
    {
        var l = WithAnim(type, 1, 0);
        var start = TextAnimator.Evaluate(l, 0); var end = TextAnimator.Evaluate(l, 1.5);
        Assert.Equal(0f, end.OffsetX, 3); Assert.Equal(0f, end.OffsetY, 3);
        switch (type)
        {
            case TextAnimType.SlideLeft: Assert.True(start.OffsetX > 0); break;      // enters from the right, travelling left
            case TextAnimType.SlideRight: Assert.True(start.OffsetX < 0); break;
            case TextAnimType.SlideUp: Assert.True(start.OffsetY > 0); break;
            case TextAnimType.SlideDown: Assert.True(start.OffsetY < 0); break;
        }
    }

    [Fact]
    public void ZoomInGrowsAndZoomOutShrinksToNormal()
    {
        var zi = WithAnim(TextAnimType.ZoomIn, 1, 0); var zo = WithAnim(TextAnimType.ZoomOut, 1, 0);
        Assert.True(TextAnimator.Evaluate(zi, 0).Scale < 0.5f); Assert.Equal(1f, TextAnimator.Evaluate(zi, 2).Scale, 3);
        Assert.True(TextAnimator.Evaluate(zo, 0).Scale > 1.5f); Assert.Equal(1f, TextAnimator.Evaluate(zo, 2).Scale, 3);
    }

    [Fact]
    public void TypewriterAndLetterRevealProgressMonotonically()
    {
        foreach (var type in new[] { TextAnimType.Typewriter, TextAnimType.LetterReveal })
        {
            var l = WithAnim(type, 2, 0.5);
            float last = -1;
            for (double t = 0; t <= 3; t += 0.1)
            {
                var s = TextAnimator.Evaluate(l, t); float v = type == TextAnimType.Typewriter ? s.Reveal : s.LetterReveal;
                Assert.True(v >= last - 1e-5f); last = v;
            }
            Assert.Equal(1f, last, 3);
            Assert.Equal(0f, type == TextAnimType.Typewriter ? TextAnimator.Evaluate(l, 0).Reveal : TextAnimator.Evaluate(l, 0).LetterReveal, 3);
        }
    }

    [Fact]
    public void BlurRevealStartsBlurredAndEndsSharp()
    {
        var l = WithAnim(TextAnimType.BlurReveal, 1, 0);
        Assert.True(TextAnimator.Evaluate(l, 0).Blur > 10); Assert.Equal(0f, TextAnimator.Evaluate(l, 2).Blur, 3);
    }

    [Theory]
    [InlineData(TextAnimType.Pulse)] [InlineData(TextAnimType.Bounce)] [InlineData(TextAnimType.Floating)] [InlineData(TextAnimType.WaveMotion)] [InlineData(TextAnimType.RgbSplit)]
    public void LoopingEffectsRepeatExactlyEveryDuration(TextAnimType type)
    {
        var l = WithAnim(type, 1.5, 0.25, loop: true);
        foreach (double t in new[] { 0.6, 1.1, 2.3 })
        {
            var a = TextAnimator.Evaluate(l, t); var b = TextAnimator.Evaluate(l, t + 1.5);
            Assert.Equal(a.Scale, b.Scale, 3); Assert.Equal(a.OffsetY, b.OffsetY, 2); Assert.Equal(a.RgbSplit, b.RgbSplit, 3); Assert.Equal(a.WavePhase % MathF.Tau, b.WavePhase % MathF.Tau, 2);
        }
    }

    [Fact]
    public void RotationCompletesOneTurnOrSpinsForever()
    {
        Assert.Equal(360f, TextAnimator.Evaluate(WithAnim(TextAnimType.Rotation, 2, 0), 5).Rotation, 2);
        Assert.Equal(720f, TextAnimator.Evaluate(WithAnim(TextAnimType.Rotation, 2, 0, loop: true), 4).Rotation, 2);
    }

    [Fact]
    public void NonLoopingContinuousEffectsStopAfterTheirWindow()
    {
        var l = WithAnim(TextAnimType.Pulse, 1, 0.5, loop: false);
        Assert.Equal(1f, TextAnimator.Evaluate(l, 0.2).Scale, 3);
        Assert.Equal(1f, TextAnimator.Evaluate(l, 2.0).Scale, 3);
    }

    [Fact]
    public void GlitchAndFlickerAreDeterministic()
    {
        var g = WithAnim(TextAnimType.Glitch, 2, 0, loop: true); var f = WithAnim(TextAnimType.NeonFlicker, 2, 0, loop: true);
        for (double t = 0; t < 4; t += 0.033)
        {
            Assert.Equal(TextAnimator.Evaluate(g, t).Glitch, TextAnimator.Evaluate(g, t).Glitch);
            Assert.Equal(TextAnimator.Evaluate(f, t).Alpha, TextAnimator.Evaluate(f, t).Alpha);
        }
        Assert.Contains(Enumerable.Range(0, 200).Select(i => TextAnimator.Evaluate(g, i * 0.05).Glitch), v => v > 0.3f);
        Assert.Contains(Enumerable.Range(0, 200).Select(i => TextAnimator.Evaluate(f, i * 0.05).Alpha), v => v < 0.7f);
    }

    [Fact]
    public void LayerStartAndEndTimesControlVisibilityAndShiftAnimations()
    {
        var l = WithAnim(TextAnimType.FadeIn, 1, 0); l.StartTime = 2; l.EndTime = 6;
        Assert.False(TextAnimator.Evaluate(l, 1.9).Visible);
        Assert.Equal(0f, TextAnimator.Evaluate(l, 2.0).Alpha, 3);
        Assert.Equal(1f, TextAnimator.Evaluate(l, 3.2).Alpha, 3);
        Assert.False(TextAnimator.Evaluate(l, 6.0).Visible);
        l.EndTime = 0; Assert.True(TextAnimator.Evaluate(l, 500).Visible);
        l.Visible = false; Assert.False(TextAnimator.Evaluate(l, 3).Visible);
    }

    [Fact]
    public void DirectionReverseAndPingPongChangeTheCurve()
    {
        var rev = WithAnim(TextAnimType.FadeIn, 1, 0, dir: AnimDirection.Reverse);
        Assert.Equal(1f, TextAnimator.Evaluate(rev, 0).Alpha, 3); Assert.Equal(0f, TextAnimator.Evaluate(rev, 2).Alpha, 3);
        var pp = WithAnim(TextAnimType.FadeIn, 2, 0, dir: AnimDirection.PingPong);
        Assert.Equal(0f, TextAnimator.Evaluate(pp, 0).Alpha, 3); Assert.Equal(1f, TextAnimator.Evaluate(pp, 1).Alpha, 3); Assert.Equal(0f, TextAnimator.Evaluate(pp, 2).Alpha, 3);
    }

    [Fact]
    public void EasingsHitEndpointsAndBasicShapes()
    {
        foreach (var e in Enum.GetValues<Easing>())
        {
            Assert.Equal(0f, TextAnimator.Ease(e, 0), 3); Assert.Equal(1f, TextAnimator.Ease(e, 1), 3);
        }
        foreach (var e in new[] { Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut })
        {
            float last = -1; for (float p = 0; p <= 1.0001f; p += 0.05f) { var v = TextAnimator.Ease(e, p); Assert.True(v >= last - 1e-5f, e.ToString()); last = v; }
        }
        Assert.True(TextAnimator.Ease(Easing.EaseIn, 0.5f) < 0.5f); Assert.True(TextAnimator.Ease(Easing.EaseOut, 0.5f) > 0.5f);
        Assert.Contains(Enumerable.Range(0, 100).Select(i => TextAnimator.Ease(Easing.Back, i / 100f)), v => v > 1f);   // overshoot
    }

    [Fact]
    public void MultipleAnimationsCombine()
    {
        var l = new TextLayer();
        l.Animations.Add(new TextAnimation { Type = TextAnimType.FadeIn, Duration = 1, Easing = Easing.Linear });
        l.Animations.Add(new TextAnimation { Type = TextAnimType.SlideUp, Duration = 1, Easing = Easing.Linear });
        var s = TextAnimator.Evaluate(l, 0.5);
        Assert.True(s.Alpha < 1 && s.Alpha > 0); Assert.True(s.OffsetY > 0);
    }
}

[Collection("gl")]
public class TextRenderTests
{
    private readonly GlFixture _gl;
    public TextRenderTests(GlFixture gl) => _gl = gl;

    private static VisualConfig Plain(params TextLayer[] layers)
    {
        var cfg = new VisualConfig();
        cfg.Background.Mode = BackgroundMode.Solid; cfg.Background.Color1 = "#000000"; cfg.Background.Color2 = "#000000";
        cfg.Effects.Bloom = false; cfg.Effects.Vignette = false;
        foreach (var l in layers) cfg.TextLayers.Add(l);
        return cfg;
    }

    private byte[] Render(VisualConfig cfg, double textTime = 5, int w = 640, int h = 360)
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(w, h);
        r.TextTime = textTime;
        var viz = new NeonEqualizer();     // draws nothing without audio except dim idle pads; background is black
        r.Render(viz, cfg, AnalysisFrame.Silent, 0.016, tgt.Fbo, w, h);
        Assert.Null(r.LastError);
        return tgt.ReadPixels();
    }

    private static int Bright(byte[] px, int w, int h, int x0 = 0, int x1 = -1, int y0 = 0, int y1 = -1, int thresh = 200)
    {
        if (x1 < 0) x1 = w; if (y1 < 0) y1 = h; int n = 0;
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { int i = (y * w + x) * 4; if (px[i] > thresh && px[i + 1] > thresh && px[i + 2] > thresh) n++; }
        return n;
    }

    private static long Energy(byte[] px) { long s = 0; for (int i = 0; i < px.Length; i += 4) s += px[i] + px[i + 1] + px[i + 2]; return s; }
    private static int Visible(byte[] px, int thresh = 60) { int n = 0; for (int i = 0; i < px.Length; i += 4) if (Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])) > thresh) n++; return n; }

    private static TextLayer Big(string text = "HELLO", double x = 0.5, double y = 0.5) => new() { Text = text, FontSize = 120, FontWeight = 700, X = x, Y = y };

    [Fact]
    public void TextAppearsOverTheVisualization()
    {
        var px = Render(Plain(Big()));
        Assert.True(Bright(px, 640, 360) > 800, "white text should light many pixels");
        Assert.True(Bright(px, 640, 360, 0, 640, 0, 60) < 5, "nothing should light the top strip");
        Assert.Equal(0, Bright(Render(Plain()), 640, 360));
    }

    [Fact]
    public void PositionAndAlignmentPresetsMoveTheText()
    {
        var left = Render(Plain(Big("LEFT", 0.2, 0.2))); var right = Render(Plain(Big("RIGHT", 0.8, 0.8)));
        Assert.True(Bright(left, 640, 360, 0, 320, 0, 180) > 300); Assert.True(Bright(left, 640, 360, 320, 640, 180, 360) < 5);
        Assert.True(Bright(right, 640, 360, 320, 640, 180, 360) > 300); Assert.True(Bright(right, 640, 360, 0, 320, 0, 180) < 5);
        var l1 = Big("WIDE TEXT HERE", 0.5, 0.5); l1.Align = TextAlign.Left; l1.Text = "WIDE TEXT HERE\nI";
        var px = Render(Plain(l1));
        // Left alignment anchors the box's left edge at X, so everything starts at the centre line and the text runs right
        Assert.True(Bright(px, 640, 360, 320, 640, 0, 360) > 300); Assert.True(Bright(px, 640, 360, 0, 300, 0, 360) < 5);
        var r1 = Big("WIDE TEXT HERE"); r1.Align = TextAlign.Right; r1.X = 0.5;
        var pr = Render(Plain(r1));      // Right alignment ends at X, so the text runs left of the centre line
        Assert.True(Bright(pr, 640, 360, 0, 320, 0, 360) > 300); Assert.True(Bright(pr, 640, 360, 340, 640, 0, 360) < 5);
        Assert.Equal(0.5f * 640 + TextRenderer.Bounds(l1, 640, 360).Width / 2, TextRenderer.Bounds(l1, 640, 360).CenterX + TextRenderer.Bounds(l1, 640, 360).Width / 2 * 0 + 0, 1);
    }

    [Fact]
    public void FontSizeWeightLetterSpacingAndScaleChangeTheLayout()
    {
        int w = 640, h = 360;
        var small = Big(); small.FontSize = 40; var large = Big(); large.FontSize = 160;
        Assert.True(Bright(Render(Plain(large)), w, h) > Bright(Render(Plain(small)), w, h) * 4);
        var spaced = Big("ABC"); spaced.LetterSpacing = 80;
        Assert.True(TextRenderer.Bounds(spaced, w, h).Width > TextRenderer.Bounds(Big("ABC"), w, h).Width + 100 * (h / 1080f));
        var scaled = Big("ABC"); scaled.Scale = 2;
        Assert.True(TextRenderer.Bounds(scaled, w, h).Width > TextRenderer.Bounds(Big("ABC"), w, h).Width * 1.8);
        var thin = Big("ABC"); thin.FontWeight = 100; var bold = Big("ABC"); bold.FontWeight = 900;
        Assert.True(Bright(Render(Plain(bold)), w, h) >= Bright(Render(Plain(thin)), w, h));
        var tall = Big("A\nB"); var one = Big("A");
        Assert.True(TextRenderer.Bounds(tall, w, h).Height > TextRenderer.Bounds(one, w, h).Height * 1.7);
        var lineSp = Big("A\nB"); lineSp.LineSpacing = 2;
        Assert.True(TextRenderer.Bounds(lineSp, w, h).Height > TextRenderer.Bounds(tall, w, h).Height * 1.4);
    }

    [Fact]
    public void ColourOpacityAndRotationAreApplied()
    {
        int w = 640, h = 360;
        var red = Big(); red.Color = "#FF0000"; var px = Render(Plain(red));
        int redCount = 0, other = 0; for (int i = 0; i < px.Length; i += 4) { if (px[i] > 200 && px[i + 1] < 60) redCount++; if (px[i + 2] > 120) other++; }
        Assert.True(redCount > 500); Assert.True(other < 20);
        var half = Big(); half.Opacity = 0.4;
        Assert.True(Bright(Render(Plain(half)), w, h) < 5);              // dimmer than the 200 threshold
        var rot = Big("IIII"); rot.Rotation = 90;
        var pr = Render(Plain(rot)); var p0 = Render(Plain(Big("IIII")));
        Assert.True(Bright(pr, w, h, 280, 360, 0, 360) > 0 && Bright(p0, w, h, 0, 640, 150, 210) > Bright(pr, w, h, 0, 640, 150, 210) / 2);
    }

    [Fact]
    public void ShadowOutlineGlowPanelAndBorderAddVisibleStyling()
    {
        int w = 640, h = 360;
        long Energy(byte[] px) { long s = 0; for (int i = 0; i < px.Length; i += 4) s += px[i] + px[i + 1] + px[i + 2]; return s; }
        long plain = Energy(Render(Plain(Big())));
        var sh = Big(); sh.Shadow.Enabled = true; sh.Shadow.Color = "#FFFFFF"; sh.Shadow.Opacity = 1; sh.Shadow.OffsetX = 12; sh.Shadow.OffsetY = 12; sh.Shadow.Blur = 4;
        Assert.True(Energy(Render(Plain(sh))) > plain * 1.05, "shadow");
        var ol = Big(); ol.Outline.Enabled = true; ol.Outline.Color = "#FFFFFF"; ol.Outline.Width = 6;
        Assert.True(Energy(Render(Plain(ol))) > plain * 1.1, "outline");
        var gl = Big(); gl.Glow.Enabled = true; gl.Glow.Color = "#00FFFF"; gl.Glow.Radius = 30; gl.Glow.Intensity = 2;
        Assert.True(Energy(Render(Plain(gl))) > plain * 1.15, "glow");
        var pn = Big(); pn.Panel.Enabled = true; pn.Panel.Color = "#3355FF"; pn.Panel.Opacity = 1;
        Assert.True(Energy(Render(Plain(pn))) > plain * 1.5, "panel");
        var bd = Big(); bd.Border.Enabled = true; bd.Border.Width = 6; bd.Padding = 40;
        Assert.True(Energy(Render(Plain(bd))) > plain * 1.1, "border");
        Assert.True(TextRenderer.Bounds(pn, w, h).Width > TextRenderer.Bounds(Big(), w, h).Width);   // padding applies once a panel is on
    }

    [Fact]
    public void TimelineStartEndHideAndShowTheLayer()
    {
        var l = Big(); l.StartTime = 2; l.EndTime = 4;
        var cfg = Plain(l);
        Assert.Equal(0, Bright(Render(cfg, 1.0), 640, 360));
        Assert.True(Bright(Render(cfg, 3.0), 640, 360) > 800);
        Assert.Equal(0, Bright(Render(cfg, 4.5), 640, 360));
    }

    [Fact]
    public void AnimationsAreVisibleInTheRenderedFrameAndFollowTheTimeline()
    {
        var l = Big("ANIMATE");
        l.Animations.Add(new TextAnimation { Type = TextAnimType.FadeIn, Duration = 2, Delay = 1, Easing = Easing.Linear });
        var cfg = Plain(l);
        long baseline = Energy(Render(Plain()));
        long Lit(double t) => Energy(Render(cfg, t)) - baseline;
        long a = Lit(0.5), b = Lit(2.0), c = Lit(4.0);
        Assert.True(Math.Abs(a) < 2000, $"nothing visible before the delay: {a}"); Assert.True(b > 0 && c > b * 1.5, $"{a} {b} {c}");
        var tw = Big("TYPEWRITER TEXT"); tw.Animations.Add(new TextAnimation { Type = TextAnimType.Typewriter, Duration = 2, Easing = Easing.Linear });
        var cfg2 = Plain(tw);
        Assert.True(Bright(Render(cfg2, 1.0), 640, 360) < Bright(Render(cfg2, 3.0), 640, 360) * 0.75);
        // identical timeline position renders identical frames (export determinism)
        Assert.Equal(0, Render(cfg2, 1.3).Zip(Render(cfg2, 1.3), (x, y) => Math.Abs(x - y)).Sum());
    }

    [Theory]
    [MemberData(nameof(AnimNames))]
    public void EveryAnimationTypeRendersWithoutErrors(TextAnimType type)
    {
        var l = Big("MOTION"); l.Color = "#707070"; l.Animations.Add(new TextAnimation { Type = type, Duration = 1, Loop = true, Easing = Easing.Linear });
        var cfg = Plain(l);
        long Sum(byte[] p) { long s = 0; for (int i = 0; i < p.Length; i++) s += p[i]; return s; }
        var frames = new[] { 0.1, 0.4, 0.7, 1.1, 1.6 }.Select(t => Sum(Render(cfg, t))).ToList();
        Assert.All(frames, f => Assert.True(f >= 0));
        Assert.True(frames.Distinct().Count() > 1 || type is TextAnimType.Rotation, $"{type} produced identical frames");
    }

    public static IEnumerable<object[]> AnimNames() => Enum.GetValues<TextAnimType>().Select(t => new object[] { t });

    [Fact]
    public void TextIsIncludedInConfigClonesUsedForScreenshotsAndExport()
    {
        var cfg = Plain(Big("SAVED"));
        var clone = cfg.Clone();
        Assert.Single(clone.TextLayers); Assert.Equal("SAVED", clone.TextLayers[0].Text);
        Assert.True(Bright(Render(clone), 640, 360) > 800);
    }

    [Fact]
    public void BoundsSupportHitTestingForDragEditing()
    {
        var l = Big("DRAG ME", 0.3, 0.7);
        var b = TextRenderer.Bounds(l, 1000, 500);
        Assert.Equal(300f, b.CenterX, 1); Assert.Equal(350f, b.CenterY, 1);
        Assert.True(b.Contains(300, 350)); Assert.False(b.Contains(900, 50));
    }

    [Fact]
    public void AllInstalledFontsCanBeListed() => Assert.Contains(TextRenderer.InstalledFonts(), f => f.Contains("Segoe", StringComparison.OrdinalIgnoreCase) || f.Contains("Arial", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void EveryTitleTemplateRendersVisibleText(string name)
    {
        var t = TitleTemplates.All.First(x => x.Name == name);
        var cfg = Plain(t.Layers.Select(l => l.Clone()).ToArray());
        var px = Render(cfg, 5.0);
        Assert.True(Visible(px) > 120, $"{name} should be visible after its intro ({Visible(px)} px)");
    }

    [Fact]
    public void DumpTemplateSamplesWhenRequested()
    {
        var dir = Environment.GetEnvironmentVariable("AD_TEXT_DUMP_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Assert.True(_gl.Begin(), _gl.Unavailable);
        Directory.CreateDirectory(dir);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(640, 360);
        var music = TestAudio.MusicLike(1.0);
        foreach (var t in TitleTemplates.All)
        {
            var cfg = new VisualConfig(); cfg.Palette.Colors = new[] { "#7C4DFF", "#22C7E8", "#F472B6", "#22D3A5" };
            foreach (var l in t.Layers) cfg.TextLayers.Add(l.Clone());
            var viz = new CircularSpectrum(); r.ResetTime(); viz.Reset(); r.TextTime = 4.2;
            foreach (var f in music) r.Render(viz, cfg, f, 1.0 / 60, tgt.Fbo, 640, 360);
            PngWriter.Save(Path.Combine(dir, BuiltInPresets.Slug(t.Name) + ".png"), tgt.ReadPixels(), 640, 360);
        }
    }

    public static IEnumerable<object[]> TemplateNames() => TitleTemplates.All.Select(t => new object[] { t.Name });
}

public class TextSnappingTests
{
    [Fact]
    public void SnapsBoxCentreToFrameCentreAndThirds()
    {
        var r = TextSnapping.Snap(965, 540, 200, 80, 1920, 1080);
        Assert.Equal(960.0, r.Cx, 3); Assert.Equal(0.5, r.GuideX!.Value, 3); Assert.Equal(540.0, r.Cy, 3); Assert.Equal(0.5, r.GuideY!.Value, 3);
        var t = TextSnapping.Snap(1275, 355, 200, 80, 1920, 1080);
        Assert.Equal(1280.0, t.Cx, 3); Assert.Equal(1.0 / 3 * 1080, t.Cy, 1);
    }

    [Fact]
    public void SnapsEdgesToTheSafeMargins()
    {
        var r = TextSnapping.Snap(1630, 300, 400, 100, 1920, 1080);          // right edge at 1830 -> margin at 1824
        Assert.Equal(1824.0 - 200, r.Cx, 1);
        var l = TextSnapping.Snap(300, 300, 400, 100, 1920, 1080);           // left edge at 100 -> margin at 96
        Assert.Equal(96.0 + 200, l.Cx, 1);
        Assert.Equal(0.05, l.GuideX!.Value, 3);
    }

    [Fact]
    public void DoesNotSnapWhenFarFromGuides()
    {
        var r = TextSnapping.Snap(700, 200, 200, 80, 1920, 1080);
        Assert.Equal(700.0, r.Cx, 3); Assert.Equal(200.0, r.Cy, 3); Assert.Null(r.GuideX); Assert.Null(r.GuideY);
    }

    [Fact]
    public void LeftAndRightAnchorsKeepTheDraggedBoxUnderTheCursor()
    {
        var l = new TextLayer { Text = "ANCHORED", Align = TextAlign.Left, X = 0.2, Y = 0.5 };
        var b = TextRenderer.Bounds(l, 1000, 500);
        Assert.Equal(200f, b.CenterX - b.Width / 2, 1);                       // left edge sits on X
        l.Align = TextAlign.Right; var rb = TextRenderer.Bounds(l, 1000, 500);
        Assert.Equal(200f, rb.CenterX + rb.Width / 2, 1);                      // right edge sits on X
        Assert.Equal(rb.CenterX - 200f, TextRenderer.AnchorShift(l, 1000, 500), 1);
    }
}

public sealed class TemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ad-tpl-" + Guid.NewGuid().ToString("N"));
    public TemplateTests() { new AppPaths(_root).EnsureCreated(); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private TemplateStore Store() => new(new AppPaths(_root));

    [Fact]
    public void ShipsTheTenRequestedTemplates() =>
        Assert.Equal(new[] { "Cinematic Intro", "Music Artist", "Podcast Title", "Radio Show", "Minimal Title", "Neon Title", "Cyberpunk Title", "Futuristic Title", "Elegant Typography", "Glitch Intro" },
            TitleTemplates.All.Select(t => t.Name));

    [Fact]
    public void BuiltInTemplatesAreValidAndUseAnimations()
    {
        foreach (var t in TitleTemplates.All)
        {
            Assert.True(TextValidator.Validate(t).Count == 0, $"{t.Name}: {string.Join(";", TextValidator.Validate(t))}");
            Assert.NotEmpty(t.Layers); Assert.False(string.IsNullOrWhiteSpace(t.Description));
        }
        Assert.True(TitleTemplates.All.Count(t => t.Layers.Any(l => l.Animations.Count > 0)) >= 9);
    }

    [Fact]
    public void CustomTemplatesRoundTripThroughDisk()
    {
        var s = Store();
        var layers = TitleTemplates.All[5].Layers;
        var saved = s.Save("My Neon", layers, "mine");
        var again = Store().Custom();
        Assert.Single(again);
        Assert.Equal("My Neon", again[0].Name); Assert.True(again[0].IsCustom);
        Assert.Equal(layers.Count, again[0].Layers.Count);
        Assert.Equal(layers[0].Animations.Count, again[0].Layers[0].Animations.Count);
        Assert.Equal(layers[0].Glow.Enabled, again[0].Layers[0].Glow.Enabled);
        Assert.Equal(11, s.All().Count);
        Assert.True(s.Delete("My Neon")); Assert.Empty(Store().Custom());
    }

    [Fact]
    public void CannotShadowABuiltInTemplateName() =>
        Assert.EndsWith("(custom)", Store().Save("Neon Title", TitleTemplates.All[0].Layers).Name);

    [Fact]
    public void ExportImportAndRejection()
    {
        var s = Store(); var file = Path.Combine(_root, "x.adtemplate");
        s.Export(TitleTemplates.All[2], file);
        var imported = s.Import(file, out var errs);
        Assert.NotNull(imported); Assert.Empty(errs);
        File.WriteAllText(file, "{ \"name\": \"bad\", \"layers\": [ { \"fontSize\": 99999 } ] }");
        Assert.Null(s.Import(file, out var bad)); Assert.NotEmpty(bad);
        File.WriteAllText(file, "nonsense"); Assert.Null(s.Import(file, out _));
        Assert.Throws<InvalidDataException>(() => s.Save("Invalid", new[] { new TextLayer { FontSize = 5000 } }));
    }

    [Fact]
    public void ValidatorRejectsHostileValues()
    {
        var bad = new[]
        {
            new TextLayer { Color = "red" }, new TextLayer { X = double.NaN }, new TextLayer { Opacity = 7 },
            new TextLayer { FontFamily = "evil\u0000" }, new TextLayer { Text = new string('x', 5000) },
        };
        foreach (var l in bad) Assert.NotEmpty(TextValidator.Validate(new TitleTemplate { Name = "t", Layers = { l } }));
        Assert.NotEmpty(TextValidator.Validate(new TitleTemplate { Name = "", Layers = { new TextLayer() } }));
        Assert.NotEmpty(TextValidator.Validate(new TitleTemplate { Name = "empty" }));
    }

    [Fact]
    public void LayerKindsCoverEverythingTheSpecAsks()
    {
        foreach (var k in new[] { TextKind.MainTitle, TextKind.Subtitle, TextKind.Artist, TextKind.SongTitle, TextKind.Album, TextKind.Custom, TextKind.Watermark, TextKind.Credits, TextKind.SocialHandle, TextKind.Caption })
        {
            var l = TextLayer.Create(k);
            Assert.Equal(k, l.Kind); Assert.False(string.IsNullOrWhiteSpace(l.Text));
            Assert.Empty(TextValidator.Validate(new TitleTemplate { Name = "k", Layers = { l } }));
        }
        Assert.NotEmpty(TextLayer.Create(TextKind.Caption).Animations);   // animated captions
    }

    [Fact]
    public void LayerCloneIsIndependent()
    {
        var a = TextLayer.Create(TextKind.Caption); var b = a.Clone();
        b.Text = "changed"; b.Animations[0].Duration = 9; b.Panel.Enabled = false;
        Assert.NotEqual("changed", a.Text); Assert.NotEqual(9, a.Animations[0].Duration); Assert.True(a.Panel.Enabled); Assert.NotEqual(a.Id, b.Id);
    }
}
