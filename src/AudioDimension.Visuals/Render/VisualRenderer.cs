using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Visualizers;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Serilog;

namespace AudioDimension.Visuals.Render;

/// <summary>Lets visualizer fullscreen passes set their own uniforms.</summary>
public sealed class ShaderBinder
{
    internal GlShader Shader { get; }
    internal ShaderBinder(GlShader s) => Shader = s;
    public void Set(string n, float v) => Shader.Set(n, v);
    public void Set(string n, int v) => Shader.Set(n, v);
    public void Set(string n, Vector2 v) => Shader.Set(n, v);
    public void Set(string n, Vector3 v) => Shader.Set(n, v);
    public void Set(string n, Vector4 v) => Shader.Set(n, v);
}

public sealed class GpuInfo
{
    public string Renderer { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Version { get; init; } = "";
    public int VramMb { get; init; }      // 0 = unknown
    public int MaxPointSize { get; init; }
    public bool IsIntegrated => Renderer.Contains("Intel", StringComparison.OrdinalIgnoreCase) && !Renderer.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                                || Renderer.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) || Renderer.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);

    public static GpuInfo Query()
    {
        int vram = 0;
        try
        {
            string ext = "";
            int n = GL.GetInteger(GetPName.NumExtensions);
            for (int i = 0; i < n; i++) ext += GL.GetString(StringNameIndexed.Extensions, i) + " ";
            if (ext.Contains("GL_NVX_gpu_memory_info")) { GL.GetInteger((GetPName)0x9048, out int kb); vram = kb / 1024; }
            else if (ext.Contains("GL_ATI_meminfo")) { var v = new int[4]; GL.GetInteger((GetPName)0x87FB, v); vram = v[0] / 1024; }
            GL.GetError();
        }
        catch (Exception ex) { Log.Debug(ex, "VRAM query failed"); }
        var sizes = new float[2];
        GL.GetFloat(GetPName.PointSizeRange, sizes);
        return new GpuInfo
        {
            Renderer = GL.GetString(StringName.Renderer) ?? "", Vendor = GL.GetString(StringName.Vendor) ?? "",
            Version = GL.GetString(StringName.Version) ?? "", VramMb = vram, MaxPointSize = (int)sizes[1]
        };
    }

    public QualityLevel RecommendedQuality()
    {
        if (IsIntegrated) return QualityLevel.Low;
        if (VramMb > 0 && VramMb <= 2048) return QualityLevel.Medium;
        if (VramMb > 0 && VramMb <= 4096) return QualityLevel.High;
        return VramMb == 0 ? QualityLevel.High : QualityLevel.Ultra;
    }

    /// <summary>Returns a user-facing warning if the chosen quality/resolution is likely too heavy, else null.</summary>
    public string? Warn(QualitySettings q, int outW, int outH)
    {
        var rec = RecommendedQuality();
        if (q.Level > rec && q.Level != QualityLevel.Custom)
            return $"{q.Level} quality may be too demanding for {Renderer}. Recommended: {rec}.";
        long pixels = (long)outW * outH;
        if (pixels > 2560L * 1440 && (VramMb is > 0 and <= 2048 || IsIntegrated))
            return "This output resolution is high for the detected graphics hardware. Consider lowering quality or resolution.";
        return null;
    }
}

/// <summary>
/// Renders any <see cref="Visualizer"/> into a target framebuffer: background, HDR scene, post-processing.
/// Works against whatever GL context is current (WPF control or offscreen), so preview, screenshots and export share it.
/// </summary>
public sealed class VisualRenderer : IDisposable
{
    private sealed class Batch
    {
        public int Vao, Vbo;
    }

    private readonly Canvas _canvas = new();
    private readonly Camera _camera = new();
    private readonly FrameInput _frame = new();
    private readonly PostProcessor _post = new();
    private readonly TextOverlay _text = new();
    private ImageBackground? _image;
    private readonly GlShader _solid, _points, _screen, _background;
    private readonly Dictionary<string, GlShader?> _passShaders = new();
    private readonly Batch _bSolid = new(), _bGlow = new(), _bPoints = new(), _bScreen = new();
    private readonly int _emptyVao;
    private readonly float[] _bandsTmp = new float[6];
    private readonly Vector4[] _palTmp = new Vector4[5];
    private double _animTime;

    public GpuInfo Gpu { get; }
    public string? LastError { get; private set; }
    public Camera Camera => _camera;
    public int LastVertexCount { get; private set; }
    public double AnimTime => _animTime;
    /// <summary>Timeline position (seconds) used to evaluate text animations. Preview advances it; export sets it from the frame index.</summary>
    public double TextTime { get; set; }
    /// <summary>Extra internal-resolution multiplier (adaptive preview resolution); 1 = use quality setting as-is.</summary>
    public double ScaleMultiplier { get; set; } = 1.0;

    public VisualRenderer()
    {
        Gpu = GpuInfo.Query();
        Log.Information("GL {Version} on {Renderer} ({Vendor}), VRAM {Vram} MB", Gpu.Version, Gpu.Renderer, Gpu.Vendor, Gpu.VramMb);
        _solid = new GlShader("solid", Shaders.SolidVert, Shaders.SolidFrag);
        _points = new GlShader("points", Shaders.PointsVert, Shaders.PointsFrag);
        _screen = new GlShader("screen", Shaders.ScreenVert, Shaders.ScreenFrag);
        _background = new GlShader("background", Shaders.FullscreenVert, Shaders.Background);
        _emptyVao = GL.GenVertexArray();
        SetupBatch(_bSolid, (3, 0), (3, 3), (4, 6));
        SetupBatch(_bGlow, (3, 0), (3, 3), (4, 6));
        SetupBatch(_bPoints, (3, 0), (4, 3), (1, 7));
        SetupBatch(_bScreen, (2, 0), (4, 2));
    }

    private static void SetupBatch(Batch b, params (int size, int offset)[] attrs)
    {
        b.Vao = GL.GenVertexArray(); b.Vbo = GL.GenBuffer();
        GL.BindVertexArray(b.Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, b.Vbo);
        int stride = 0; foreach (var a in attrs) stride += a.size;
        for (int i = 0; i < attrs.Length; i++)
        {
            GL.EnableVertexAttribArray(i);
            GL.VertexAttribPointer(i, attrs[i].size, VertexAttribPointerType.Float, false, stride * 4, attrs[i].offset * 4);
        }
        GL.BindVertexArray(0);
    }

    public void ResetTime() => _animTime = 0;

    /// <summary>Renders one frame into <paramref name="targetFbo"/> (0 = default framebuffer) at w x h.</summary>
    public void Render(Visualizer viz, VisualConfig cfg, AnalysisFrame audio, double dt, int targetFbo, int w, int h)
    {
        if (w < 8 || h < 8) return;
        LastError = null;
        var q = cfg.Quality;
        int sw = Math.Max(16, (int)(w * Math.Clamp(q.RenderScale * ScaleMultiplier, 0.2, 2.0))) & ~3;
        int sh = Math.Max(16, (int)(h * Math.Clamp(q.RenderScale * ScaleMultiplier, 0.2, 2.0))) & ~3;
        _post.EnsureSize(sw, sh);

        double speed = Math.Max(0, cfg.Params.Speed);
        dt = Math.Clamp(dt, 0, 0.1);
        _animTime += dt * speed;
        float aspect = (float)w / h;

        _frame.Audio = audio; _frame.Config = cfg;
        _frame.Time = (float)_animTime; _frame.Dt = (float)(dt * speed); _frame.Aspect = aspect;
        _frame.ParticleBudget = Math.Max(100, (int)(q.MaxParticles * Math.Clamp(cfg.Params.ParticleDensity, 0.05, 2.0)));

        _camera.Update(cfg.Camera, aspect, _animTime, _frame.Bass, viz.Is3D);
        _canvas.Clear();
        _canvas.ViewProj = _camera.ViewProj; _canvas.Width = sw; _canvas.Height = sh; _canvas.CurveDetail = q.CurveDetail;
        try { viz.Draw(_canvas, _frame); }
        catch (Exception ex) { LastError = $"Visualizer '{viz.Name}' failed: {ex.Message}"; Log.Error(ex, "Visualizer {Id} threw", viz.Id); _canvas.Clear(); }

        var scene = _post.Scene!;
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, scene.Fbo);
        GL.Viewport(0, 0, sw, sh);
        bool transparent = cfg.Background.Mode == BackgroundMode.Transparent;
        GL.ClearColor(0, 0, 0, transparent ? 0f : 1f);
        GL.DepthMask(true);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend);

        if (!transparent) DrawBackground(cfg, audio);
        DrawFullscreenPasses(audio, cfg);
        DrawGeometry(viz, cfg);

        _post.Run(cfg.Effects, q, transparent, (float)_animTime, targetFbo, w, h);
        try { _text.Draw(cfg.TextLayers, TextTime, w, h, targetFbo); }
        catch (Exception ex) { LastError = "Text overlay failed: " + ex.Message; Log.Error(ex, "Text overlay failed"); }
        var err = GL.GetError();
        if (err != ErrorCode.NoError) { LastError = "OpenGL error: " + err; Log.Warning("GL error {Err} after rendering {Viz}", err, viz.Id); }
    }

    private void DrawBackground(VisualConfig cfg, AnalysisFrame a)
    {
        var bg = cfg.Background;
        if (bg.Mode == BackgroundMode.Image)
        {
            _image ??= new ImageBackground();
            if (_image.Prepare(bg.ImagePath))
            {
                float zoom = 1.04f + (float)bg.AudioReactive * 0.05f * a.Level + 0.02f * MathF.Sin((float)_animTime * (float)Math.Max(0.05, bg.AnimationSpeed));
                _image.Draw(_frame.Aspect, zoom, (float)bg.ImageDim);
                return;
            }
            LastError = _image.Error; Log.Warning("Image background unavailable: {Err}", _image.Error);
            bg = new BackgroundSettings();         // fall back to the default gradient so the picture is never blank
        }
        _background.Use();
        _background.Set("uMode", (int)bg.Mode);
        var c1 = ColorUtil.TryParse(bg.Color1, new Vector4(0.04f, 0.05f, 0.07f, 1)); var c2 = ColorUtil.TryParse(bg.Color2, new Vector4(0.1f, 0.06f, 0.2f, 1));
        _background.Set("uC1", new Vector3(c1.X, c1.Y, c1.Z)); _background.Set("uC2", new Vector3(c2.X, c2.Y, c2.Z));
        _background.Set("uTime", (float)(_animTime * bg.AnimationSpeed * 3));
        _background.Set("uAspect", _frame.Aspect);
        _background.Set("uAudio", new Vector4(a.Bass, a.Mid, a.Treble, a.Level));
        _background.Set("uReact", (float)bg.AudioReactive);
        GL.BindVertexArray(_emptyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    private void DrawFullscreenPasses(AnalysisFrame a, VisualConfig cfg)
    {
        foreach (var (key, frag, bind) in _canvas.FullscreenPasses)
        {
            if (!_passShaders.TryGetValue(key, out var sh))
            {
                try { sh = new GlShader(key, Shaders.FullscreenVert, Shaders.FullscreenFragHeader + frag); }
                catch (ShaderCompileException ex)
                {
                    sh = null; LastError = ex.Message; Log.Error(ex, "Visualizer shader failed");
                }
                _passShaders[key] = sh;
            }
            if (sh is null) continue;
            sh.Use();
            sh.Set("uTime", (float)_animTime); sh.Set("uAspect", _frame.Aspect);
            sh.Set("uAudio", new Vector4(_frame.Bass, _frame.Mid, _frame.Treble, _frame.Level));
            for (int i = 0; i < 6; i++) _bandsTmp[i] = _frame.Band(i);
            sh.Set("uBands", _bandsTmp);
            for (int i = 0; i < 5; i++) _palTmp[i] = cfg.Palette.Evaluate(i / 4f);
            sh.SetVec4Array("uPal", _palTmp);
            sh.Set("uGain", (float)cfg.Params.Glow);
            bind(new ShaderBinder(sh));
            GL.BindVertexArray(_emptyVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    private static void Upload(Batch b, FloatList l)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, b.Vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(l.N * sizeof(float)), l.A, BufferUsageHint.StreamDraw);
    }

    private void DrawGeometry(Visualizer viz, VisualConfig cfg)
    {
        float gain = (float)cfg.Params.Glow;
        var vp = _canvas.ViewProj;
        int verts = 0;

        if (_canvas.Solid.N > 0)
        {
            GL.Enable(EnableCap.DepthTest); GL.DepthMask(true); GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _solid.Use(); _solid.Set("uVP", ref vp); _solid.Set("uLit", 1f); _solid.Set("uGain", gain);
            _solid.Set("uLightDir", Vector3.Normalize(new Vector3(0.4f, 0.9f, 0.6f)));
            _solid.Set("uFog", viz.Is3D ? 0.018f : 0f);
            GL.BindVertexArray(_bSolid.Vao); Upload(_bSolid, _canvas.Solid);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _canvas.Solid.N / 10);
            verts += _canvas.Solid.N / 10;
        }
        if (_canvas.Glow.N > 0)
        {
            GL.Enable(EnableCap.DepthTest); GL.DepthMask(false);
            GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            _solid.Use(); _solid.Set("uVP", ref vp); _solid.Set("uLit", 0f); _solid.Set("uGain", gain); _solid.Set("uFog", 0f);
            GL.BindVertexArray(_bGlow.Vao); Upload(_bGlow, _canvas.Glow);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _canvas.Glow.N / 10);
            verts += _canvas.Glow.N / 10;
        }
        if (_canvas.Points.N > 0)
        {
            GL.Enable(EnableCap.DepthTest); GL.DepthMask(false); GL.Enable(EnableCap.ProgramPointSize);
            GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            _points.Use(); _points.Set("uVP", ref vp);
            _points.Set("uPixelScale", _canvas.PixelScale); _points.Set("uRef", _camera.IsPerspective ? Math.Max(_camera.Distance, 6f) : 1f);
            _points.Set("uGain", gain);
            GL.BindVertexArray(_bPoints.Vao); Upload(_bPoints, _canvas.Points);
            GL.DrawArrays(PrimitiveType.Points, 0, _canvas.Points.N / 8);
            verts += _canvas.Points.N / 8;
        }
        if (_canvas.Screen.N > 0)
        {
            GL.Disable(EnableCap.DepthTest); GL.DepthMask(false);
            GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            _screen.Use(); _screen.Set("uGain", gain);
            GL.BindVertexArray(_bScreen.Vao); Upload(_bScreen, _canvas.Screen);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _canvas.Screen.N / 6);
            verts += _canvas.Screen.N / 6;
        }
        GL.DepthMask(true); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.DepthTest);
        GL.BindVertexArray(0);
        LastVertexCount = verts;
    }

    public void Dispose()
    {
        _post.Dispose(); _text.Dispose(); _image?.Dispose();
        foreach (var s in new[] { _solid, _points, _screen, _background }) s.Dispose();
        foreach (var s in _passShaders.Values) s?.Dispose();
        foreach (var b in new[] { _bSolid, _bGlow, _bPoints, _bScreen })
        { GL.DeleteVertexArray(b.Vao); GL.DeleteBuffer(b.Vbo); }
        GL.DeleteVertexArray(_emptyVao);
    }
}
