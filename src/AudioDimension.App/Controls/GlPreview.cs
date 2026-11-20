using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioDimension.App.ViewModels;
using AudioDimension.Audio.Analysis;
using AudioDimension.Audio.Capture;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Text;
using AudioDimension.Visuals.Visualizers;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Wpf;
using Serilog;

namespace AudioDimension.App.Controls;

/// <summary>
/// Live preview: an OpenGL surface (own context + renderer) with mouse camera control, HUD and guide overlays.
/// DataContext must be a <see cref="StudioViewModel"/>. The main preview also reports FPS and adapts its resolution.
/// </summary>
public sealed class GlPreview : Grid
{
    public bool IsMainPreview { get; set; } = true;
    /// <summary>When true, mouse input selects and drags text layers instead of orbiting the camera.</summary>
    public bool TextEdit { get; set; }
    private readonly TextEditOverlay _edit = new() { IsHitTestVisible = false };
    private TextLayer? _dragLayer, _watched;
    private Point _grab;

    private readonly GLWpfControl _gl = new();
    private readonly TextBlock _hud = new()
    {
        Foreground = Brushes.White, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11,
        Margin = new Thickness(10, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
        IsHitTestVisible = false
    };
    private readonly GuidesOverlay _guides = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private VisualRenderer? _renderer;
    private Visualizer? _viz;
    private string _vizId = "";
    private int _resetSeen = -1;
    private AnalysisFrame _held = AnalysisFrame.Silent;
    private bool _wasPaused;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastRender;
    private double _fpsAcc; private int _fpsFrames; private double _adaptive = 1.0; private double _slowAcc;
    private Point _drag; private bool _dragging;
    private string? _startError;

    private StudioViewModel? Vm => DataContext as StudioViewModel;

    public GlPreview()
    {
        Children.Add(_gl); Children.Add(_guides); Children.Add(_edit); Children.Add(_hud);
        StartGl();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { if (Vm is not null) Vm.PropertyChanged -= OnVmChanged; };
        MouseLeftButtonDown += (_, e) =>
        {
            if (TextEdit && Vm is not null) { BeginTextDrag(e.GetPosition(this)); return; }
            if (e.ClickCount == 2 && IsMainPreview) { Vm?.ResetCameraCommand.Execute(null); return; }
            _dragging = true; _drag = e.GetPosition(this); CaptureMouse();
        };
        MouseLeftButtonUp += (_, _) => { _dragging = false; _dragLayer = null; _edit.SetGuides(null, null); ReleaseMouseCapture(); };
        MouseMove += OnMove;
        MouseWheel += OnWheel;
    }

    private void StartGl()
    {
        try
        {
            _gl.Render += OnGlRender;
            // Must be started before the control is loaded, otherwise it never subscribes to composition rendering.
            _gl.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3, RenderContinuously = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Graphics device unavailable");
            _startError = "The graphics device is unavailable: " + ex.Message;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (_startError is not null) Vm.RenderError = _startError;
        Vm.PropertyChanged -= OnVmChanged;
        Vm.PropertyChanged += OnVmChanged;
        SyncOverlays();
        WatchSelection();
    }

    private void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SyncOverlays();
        if (e.PropertyName == nameof(StudioViewModel.SelectedTextLayer)) WatchSelection();
    }

    private void WatchSelection()
    {
        if (_watched is not null) _watched.PropertyChanged -= OnLayerChanged;
        _watched = TextEdit ? Vm?.SelectedTextLayer : null;
        if (_watched is not null) _watched.PropertyChanged += OnLayerChanged;
        _edit.Layer = _watched; _edit.InvalidateVisual();
    }

    private void OnLayerChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => _edit.InvalidateVisual();

    private void BeginTextDrag(Point p)
    {
        var vm = Vm!; double w = ActualWidth, h = ActualHeight;
        TextLayer? hit = null;
        for (int i = vm.Config.TextLayers.Count - 1; i >= 0; i--)
        {
            var l = vm.Config.TextLayers[i];
            if (!l.Visible) continue;
            if (TextRenderer.Bounds(l, (int)w, (int)h).Contains((float)p.X, (float)p.Y)) { hit = l; break; }
        }
        vm.SelectedTextLayer = hit;
        if (hit is null) return;
        var b = TextRenderer.Bounds(hit, (int)w, (int)h);
        _grab = new Point(p.X - b.CenterX, p.Y - b.CenterY);
        _dragLayer = hit; CaptureMouse();
    }

    /// <summary>Moves the dragged layer, snapping its centre/edges to the frame centre, thirds and safe margins and showing guide lines.</summary>
    private void DragText(Point p)
    {
        var l = _dragLayer!; int w = (int)ActualWidth, h = (int)ActualHeight;
        var b = TextRenderer.Bounds(l, w, h);
        double cx = p.X - _grab.X, cy = p.Y - _grab.Y;
        double? gx = null, gy = null;
        if (Vm!.SnapText)
        {
            var sn = TextSnapping.Snap(cx, cy, b.Width, b.Height, w, h);
            cx = sn.Cx; cy = sn.Cy; gx = sn.GuideX is null ? null : sn.GuideX * w; gy = sn.GuideY is null ? null : sn.GuideY * h;
        }
        float shift = TextRenderer.AnchorShift(l, w, h);
        l.X = Math.Round(Math.Clamp((cx - shift) / w, -0.5, 1.5), 4); l.Y = Math.Round(Math.Clamp(cy / h, -0.5, 1.5), 4);
        _edit.SetGuides(gx is null ? null : gx / w, gy is null ? null : gy / h);
    }

    private void SyncOverlays()
    {
        if (Vm is null) return;
        _guides.Visibility = Vm.ShowGrid ? Visibility.Visible : Visibility.Collapsed;
        _hud.Visibility = Vm.ShowHud ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragLayer is not null && e.LeftButton == MouseButtonState.Pressed) { DragText(e.GetPosition(this)); return; }
        if (!_dragging || Vm is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this); var d = p - _drag; _drag = p;
        var cam = Vm.Config.Camera;
        if (Vm.Is3D)
        {
            cam.Yaw -= d.X * 0.4;
            cam.Pitch = Math.Clamp(cam.Pitch + d.Y * 0.3, -85, 85);
            Vm.RefreshInspector();
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm is null) return;
        if (TextEdit && Keyboard.Modifiers == ModifierKeys.Control && Vm.SelectedTextLayer is { } sel)
        {
            sel.Scale = Math.Clamp(sel.Scale * Math.Pow(1.0012, e.Delta), 0.1, 6); return;      // Ctrl+wheel resizes the selected text
        }
        var cam = Vm.Config.Camera;
        double k = Math.Pow(0.9992, e.Delta);
        if (Vm.Is3D) cam.Distance = Math.Clamp(cam.Distance * k, 1.0, 60);
        else cam.Zoom2D = Math.Clamp(cam.Zoom2D / k, 0.4, 4);
        Vm.RefreshInspector();
    }

    private void OnGlRender(TimeSpan delta)
    {
        var vm = Vm; if (vm is null) return;
        int w = _gl.FrameBufferWidth, h = _gl.FrameBufferHeight;
        if (w <= 0 || h <= 0)
        {
            var dpi = VisualTreeHelper.GetDpi(_gl);
            w = (int)Math.Round(_gl.ActualWidth * dpi.DpiScaleX); h = (int)Math.Round(_gl.ActualHeight * dpi.DpiScaleY);
        }
        if (w < 16 || h < 16) return;

        if (IsMainPreview && vm.IsDetached) { GL.ClearColor(0, 0, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit); _hud.Text = "Preview is shown in a separate window"; return; }

        // frame pacing for target FPS below display rate
        double now = _clock.Elapsed.TotalSeconds, target = Math.Max(15, App.Settings.Current.TargetFrameRate);
        if (now - _lastRender < 1.0 / target - 0.004) return;
        double dtReal = Math.Min(now - _lastRender, 0.1); _lastRender = now;

        try
        {
            _renderer ??= new VisualRenderer();
            if (vm.Gpu is null) { vm.Gpu = _renderer.Gpu; vm.GpuText = _renderer.Gpu.Renderer + (_renderer.Gpu.VramMb > 0 ? $"  {_renderer.Gpu.VramMb} MB" : ""); vm.UpdateWarning(w, h); }
            if (_viz is null || _vizId != vm.SelectedVisualizer.Id) { _viz = VisualizerRegistry.Create(vm.SelectedVisualizer.Id); _vizId = vm.SelectedVisualizer.Id; _renderer.ResetTime(); }
            if (_resetSeen != vm.ResetToken) { _viz.Reset(); _renderer.ResetTime(); _resetSeen = vm.ResetToken; }

            var engine = App.Audio;
            bool live = engine.State == EngineState.Capturing;
            if (vm.IsPaused && !_wasPaused) _held = live ? engine.Latest : AnalysisFrame.Silent;
            _wasPaused = vm.IsPaused;
            var audio = vm.IsPaused ? _held : live ? engine.Latest : AnalysisFrame.Silent;
            double dt = vm.IsPaused ? 0 : dtReal;

            vm.TickTimeline(); _renderer.TextTime = vm.TextTime;
            _renderer.ScaleMultiplier = vm.PreviewScale * (vm.AdaptiveResolution && IsMainPreview ? _adaptive : 1.0);
            _renderer.Render(_viz, vm.Config, audio, dt, _gl.Framebuffer, w, h);

            var shot = IsMainPreview ? vm.TakePendingScreenshot() : null;
            if (shot is not null) SaveScreenshot(vm, shot, audio, w, h);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _gl.Framebuffer);

            if (_renderer.LastError is { } err && vm.RenderError != err) vm.RenderError = err;
            UpdateStats(vm, dtReal, w, h, live);
        }
        catch (ShaderCompileException ex)
        {
            Log.Error(ex, "Shader compilation failed");
            vm.RenderError = "A graphics shader failed to compile: " + ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Render failure");
            vm.RenderError = "Rendering failed: " + ex.Message;
        }
    }

    private void UpdateStats(StudioViewModel vm, double dt, int w, int h, bool live)
    {
        _fpsAcc += dt; _fpsFrames++;
        if (_fpsAcc < 0.5) return;
        double fps = _fpsFrames / _fpsAcc;
        double target = Math.Max(15, App.Settings.Current.TargetFrameRate);
        // adaptive preview resolution: shrink when well below target, recover when comfortably above
        if (IsMainPreview && vm.AdaptiveResolution)
        {
            if (fps < target * 0.75) { _slowAcc += _fpsAcc; if (_slowAcc > 1.0) { _adaptive = Math.Max(0.5, _adaptive - 0.1); _slowAcc = 0; } }
            else { _slowAcc = 0; if (fps > target * 0.95) _adaptive = Math.Min(1.0, _adaptive + 0.02); }
        }
        _fpsAcc = 0; _fpsFrames = 0;
        double scale = vm.Config.Quality.RenderScale * vm.PreviewScale * (vm.AdaptiveResolution && IsMainPreview ? _adaptive : 1.0);
        string txt = $"{fps:0} FPS  {w}x{h} @ {scale:P0}  {vm.Config.Quality.Level}  {_renderer?.LastVertexCount ?? 0:N0} verts";
        if (!live) txt += "\nNo audio capture running - press Play";
        _hud.Text = txt;
        if (IsMainPreview) vm.FpsText = $"{fps:0} FPS";
    }

    private void SaveScreenshot(StudioViewModel vm, ScreenshotRequest req, AnalysisFrame audio, int pw, int ph)
    {
        try
        {
            int w = req.Width > 0 ? req.Width : pw, h = req.Height > 0 ? req.Height : ph;
            using var target = new OffscreenTarget(w, h);
            var cfg = vm.Config.Clone();
            if (req.Transparent) cfg.Background.Mode = AudioDimension.Visuals.Config.BackgroundMode.Transparent;
            var old = _renderer!.ScaleMultiplier;
            _renderer.ScaleMultiplier = 1.0;
            _renderer.Render(_viz!, cfg, audio, 0, target.Fbo, w, h);
            _renderer.ScaleMultiplier = old;
            var px = target.ReadPixels();
            if (req.Jpeg) SaveJpeg(req.Path, px, w, h); else PngWriter.Save(req.Path, px, w, h);
            if (req.OnSaved is null) vm.ScreenshotStatus = $"Saved {System.IO.Path.GetFileName(req.Path)} ({w}x{h})";
            req.OnSaved?.Invoke();
            Log.Information("Screenshot saved to {Path}", req.Path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Screenshot failed");
            vm.ScreenshotStatus = "Screenshot failed: " + ex.Message;
        }
    }

    private static void SaveJpeg(string path, byte[] rgba, int w, int h)
    {
        var bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4) { bgra[i] = rgba[i + 2]; bgra[i + 1] = rgba[i + 1]; bgra[i + 2] = rgba[i]; bgra[i + 3] = 255; }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }
}
