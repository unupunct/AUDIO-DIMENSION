using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Render;

internal sealed class RenderTarget : IDisposable
{
    public int Fbo, Tex, Depth, W, H;

    public RenderTarget(int w, int h, bool depth)
    {
        W = Math.Max(2, w); H = Math.Max(2, h);
        Tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, Tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, W, H, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        Fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Tex, 0);
        if (depth)
        {
            Depth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, Depth);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, W, H);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, Depth);
        }
        var st = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (st != FramebufferErrorCode.FramebufferComplete) throw new InvalidOperationException("Render target incomplete: " + st);
    }

    public void Dispose()
    {
        if (Fbo != 0) GL.DeleteFramebuffer(Fbo);
        if (Tex != 0) GL.DeleteTexture(Tex);
        if (Depth != 0) GL.DeleteRenderbuffer(Depth);
        Fbo = Tex = Depth = 0;
    }
}

/// <summary>HDR scene target, bloom/blur chains, motion-blur history and the final composite pass.</summary>
internal sealed class PostProcessor : IDisposable
{
    private readonly GlShader _bright = new("bright", Shaders.FullscreenVert, Shaders.Bright);
    private readonly GlShader _blur = new("blur", Shaders.FullscreenVert, Shaders.Blur);
    private readonly GlShader _accum = new("accum", Shaders.FullscreenVert, Shaders.Accumulate);
    private readonly GlShader _comp = new("composite", Shaders.FullscreenVert, Shaders.Composite);
    private readonly int _vao = GL.GenVertexArray();

    public RenderTarget? Scene;
    private RenderTarget? _h1, _h2, _q1, _q2, _b1, _b2, _histA, _histB;
    private bool _histValid;
    private float _time;

    public void EnsureSize(int w, int h)
    {
        if (Scene is not null && Scene.W == w && Scene.H == h) return;
        DisposeTargets();
        Scene = new RenderTarget(w, h, true);
        _h1 = new RenderTarget(w / 2, h / 2, false); _h2 = new RenderTarget(w / 2, h / 2, false);
        _q1 = new RenderTarget(w / 4, h / 4, false); _q2 = new RenderTarget(w / 4, h / 4, false);
        _histValid = false;
    }

    private void Fullscreen()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private static void Bind(RenderTarget t)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, t.Fbo);
        GL.Viewport(0, 0, t.W, t.H);
    }

    private static void Tex(TextureUnit unit, int tex)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, tex);
    }

    private void Down(RenderTarget src, RenderTarget dst, float threshold)
    {
        Bind(dst);
        _bright.Use();
        Tex(TextureUnit.Texture0, src.Tex);
        _bright.Set("uTex", 0); _bright.Set("uTexel", new Vector2(1f / src.W, 1f / src.H)); _bright.Set("uThreshold", threshold);
        Fullscreen();
    }

    private void BlurPasses(RenderTarget a, RenderTarget b, int iterations)
    {
        _blur.Use();
        _blur.Set("uTex", 0);
        for (int i = 0; i < iterations; i++)
        {
            Bind(b); Tex(TextureUnit.Texture0, a.Tex); _blur.Set("uDir", new Vector2(1f / a.W, 0)); Fullscreen();
            Bind(a); Tex(TextureUnit.Texture0, b.Tex); _blur.Set("uDir", new Vector2(0, 1f / a.H)); Fullscreen();
        }
    }

    public void Run(EffectSettings fx, QualitySettings q, bool transparent, float time, int targetFbo, int outW, int outH)
    {
        _time = time;
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        GL.DepthMask(true);
        bool expensive = q.ExpensiveEffects;
        bool chain = q.BloomPasses > 0 && ((fx.Bloom && fx.BloomAmount > 0) || (expensive && fx.LightRays));
        bool blur = expensive && fx.Blur;
        bool motion = expensive && fx.MotionBlur;
        var scene = Scene!;
        var sceneForComposite = scene;

        if (chain)
        {
            Down(scene, _h1!, (float)fx.BloomThreshold);
            BlurPasses(_h1!, _h2!, q.BloomPasses);
            Down(_h1!, _q1!, 0f);
            BlurPasses(_q1!, _q2!, q.BloomPasses + 1);
        }
        if (blur)
        {
            _b1 ??= new RenderTarget(scene.W / 4, scene.H / 4, false);
            _b2 ??= new RenderTarget(scene.W / 4, scene.H / 4, false);
            Down(scene, _b1, 0f);
            BlurPasses(_b1, _b2, 2);
        }
        if (motion)
        {
            _histA ??= new RenderTarget(scene.W, scene.H, false);
            _histB ??= new RenderTarget(scene.W, scene.H, false);
            Bind(_histB);
            _accum.Use();
            Tex(TextureUnit.Texture0, scene.Tex); Tex(TextureUnit.Texture1, _histA.Tex);
            _accum.Set("uScene", 0); _accum.Set("uHist", 1);
            _accum.Set("uAmt", _histValid ? (float)Math.Clamp(fx.MotionBlurAmount, 0, 0.95) : 0f);
            Fullscreen();
            (_histA, _histB) = (_histB, _histA);
            _histValid = true;
            sceneForComposite = _histA;
        }
        else _histValid = false;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
        GL.Viewport(0, 0, outW, outH);
        _comp.Use();
        Tex(TextureUnit.Texture0, sceneForComposite.Tex);
        Tex(TextureUnit.Texture1, chain ? _h1!.Tex : sceneForComposite.Tex);
        Tex(TextureUnit.Texture2, chain ? _q1!.Tex : sceneForComposite.Tex);
        Tex(TextureUnit.Texture3, blur ? _b1!.Tex : sceneForComposite.Tex);
        _comp.Set("uScene", 0); _comp.Set("uBloomH", 1); _comp.Set("uBloomQ", 2); _comp.Set("uBlurTex", 3);
        _comp.Set("uRes", new Vector2(outW, outH)); _comp.Set("uTime", _time);
        _comp.Set("uTransparent", transparent ? 1 : 0);
        _comp.Set("uHasBloom", chain ? 1 : 0); _comp.Set("uHasBlur", blur ? 1 : 0);
        _comp.Set("uBloom", fx.Bloom && chain ? (float)fx.BloomAmount : 0f);
        _comp.Set("uRays", expensive && fx.LightRays && chain ? (float)fx.LightRaysAmount : 0f);
        _comp.Set("uBlurAmt", blur ? (float)fx.BlurAmount : 0f);
        _comp.Set("uVignette", fx.Vignette ? (float)fx.VignetteAmount : 0f);
        _comp.Set("uChroma", fx.ChromaticAberration ? (float)fx.ChromaticAmount : 0f);
        _comp.Set("uRgbSep", fx.RgbSeparation ? (float)fx.RgbSeparationAmount : 0f);
        _comp.Set("uGrain", fx.FilmGrain ? (float)fx.FilmGrainAmount : 0f);
        _comp.Set("uScan", fx.Scanlines ? (float)fx.ScanlineAmount : 0f);
        _comp.Set("uNoise", fx.Noise ? (float)fx.NoiseAmount : 0f);
        _comp.Set("uDistort", fx.Distortion ? (float)fx.DistortionAmount : 0f);
        _comp.Set("uSat", fx.ColorGrading ? (float)fx.Saturation : 1f);
        _comp.Set("uContrast", fx.ColorGrading ? (float)fx.Contrast : 1f);
        _comp.Set("uBright", fx.ColorGrading ? (float)fx.Brightness : 0f);
        _comp.Set("uHue", fx.ColorGrading ? MathHelper.DegreesToRadians((float)fx.HueShift) : 0f);
        Fullscreen();
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    private void DisposeTargets()
    {
        foreach (var t in new[] { Scene, _h1, _h2, _q1, _q2, _b1, _b2, _histA, _histB }) t?.Dispose();
        Scene = _h1 = _h2 = _q1 = _q2 = _b1 = _b2 = _histA = _histB = null;
    }

    public void Dispose()
    {
        DisposeTargets();
        _bright.Dispose(); _blur.Dispose(); _accum.Dispose(); _comp.Dispose();
        GL.DeleteVertexArray(_vao);
    }
}
