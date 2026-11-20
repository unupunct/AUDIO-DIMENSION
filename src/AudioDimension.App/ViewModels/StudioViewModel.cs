using System.Diagnostics;
using AudioDimension.App.Services;
using AudioDimension.Audio.Capture;
using AudioDimension.Presets;
using AudioDimension.Visuals.Text;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Visualizers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AudioDimension.App.ViewModels;

public sealed record VisualizerEntry(string Id, string Name, string Description, bool Is3D)
{
    public override string ToString() => Name;
}

public sealed record AspectOption(string Name, double Ratio)
{
    public override string ToString() => Name;
}

public sealed record NamedPalette(string Name, string[] Colors)
{
    public override string ToString() => Name;
}

public sealed record ScreenshotRequest(string Path, int Width, int Height, bool Jpeg, bool Transparent, Action? OnSaved = null);

/// <summary>Wrapper so the inspector gets its own DataTemplate while sharing the studio view model.</summary>
public sealed record StudioInspector(StudioViewModel Studio);

public sealed partial class StudioViewModel : PageViewModel
{
    public override string Title => "Visualizer Studio";
    public override string Glyph => "";
    public override object? InspectorViewModel { get; }

    public AudioViewModel Audio { get; }
    public VisualConfig Config { get; } = new();

    public IReadOnlyList<VisualizerEntry> Visualizers { get; }
    public QualityLevel[] QualityLevels { get; } = { QualityLevel.Low, QualityLevel.Medium, QualityLevel.High, QualityLevel.Ultra, QualityLevel.Custom };
    public BackgroundMode[] BackgroundModes { get; } = Enum.GetValues<BackgroundMode>();
    public AspectOption[] Aspects { get; } =
    {
        new("16:9", 16.0 / 9), new("9:16 (vertical)", 9.0 / 16), new("1:1 (square)", 1), new("4:3", 4.0 / 3), new("21:9 (ultrawide)", 21.0 / 9), new("Fill window", 0)
    };
    public string[] PreviewScales { get; } = { "Auto", "100%", "75%", "50%", "25%" };
    public string[] ScreenshotSizes { get; } = { "Preview size", "1280x720", "1920x1080", "2560x1440", "3840x2160" };
    public string[] ScreenshotFormats { get; } = { "PNG", "JPEG" };
    public IReadOnlyList<MonitorInfo> Monitors { get; } = MonitorService.List();
    public NamedPalette[] Palettes { get; } =
    {
        new("Aurora", new[] { "#22D3A5", "#22C7E8", "#A78BFA", "#F472B6" }),
        new("Sunset", new[] { "#FF5E62", "#FF9966", "#FFD166", "#F72585" }),
        new("Ocean", new[] { "#0077B6", "#00B4D8", "#90E0EF", "#CAF0F8" }),
        new("Fire", new[] { "#7F1D1D", "#EF4444", "#F59E0B", "#FDE68A" }),
        new("Neon", new[] { "#FF00E5", "#00F0FF", "#7CFF00", "#FFF200" }),
        new("Forest", new[] { "#064E3B", "#10B981", "#A3E635", "#ECFCCB" }),
        new("Candy", new[] { "#FF6EC7", "#B388FF", "#64B5F6", "#80FFEA" }),
        new("Monochrome", new[] { "#6B7280", "#9CA3AF", "#E5E7EB", "#FFFFFF" }),
    };

    [ObservableProperty] private VisualizerEntry _selectedVisualizer;
    [ObservableProperty] private QualityLevel _selectedQuality = QualityLevel.High;
    [ObservableProperty] private AspectOption _selectedAspect;
    [ObservableProperty] private string _selectedPreviewScale = "Auto";
    [ObservableProperty] private NamedPalette? _selectedPalette;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private bool _showHud = true;
    [ObservableProperty] private bool _isDetached;
    [ObservableProperty] private bool _adaptiveResolution = true;
    [ObservableProperty] private MonitorInfo? _selectedMonitor;
    [ObservableProperty] private string _fpsText = "";
    [ObservableProperty] private string _gpuText = "";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private string _renderError = "";
    [ObservableProperty] private string _screenshotStatus = "";
    [ObservableProperty] private string _selectedScreenshotSize = "1920x1080";
    [ObservableProperty] private string _selectedScreenshotFormat = "PNG";
    [ObservableProperty] private bool _screenshotTransparent;
    [ObservableProperty] private string _color0 = "#22D3A5";
    [ObservableProperty] private string _color1 = "#22C7E8";
    [ObservableProperty] private string _color2 = "#A78BFA";
    [ObservableProperty] private string _color3 = "#F472B6";
    [ObservableProperty] private bool _is3D;

    /// <summary>Incremented to make every preview window reset its visualizer instance.</summary>
    public int ResetToken { get; private set; }
    public GpuInfo? Gpu { get; set; }
    public double PreviewScale => SelectedPreviewScale switch { "100%" => 1.0, "75%" => 0.75, "50%" => 0.5, "25%" => 0.25, _ => 1.0 };

    // ---- text overlay timeline (shared by preview, screenshots and, later, export) ----
    [ObservableProperty] private TextLayer? _selectedTextLayer;
    [ObservableProperty] private double _textTime;
    [ObservableProperty] private bool _snapText = true;
    [ObservableProperty] private double _textLoopSeconds = 10;     // 0 = do not loop
    [ObservableProperty] private bool _timelinePlaying = true;
    private long _timelineStamp = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>Advances the preview timeline by real elapsed time. Safe to call from several previews per frame.</summary>
    public void TickTimeline()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = Math.Min(0.1, (now - _timelineStamp) / (double)System.Diagnostics.Stopwatch.Frequency);
        _timelineStamp = now;
        if (!TimelinePlaying || IsPaused) return;
        double t = TextTime + dt;
        if (TextLoopSeconds > 0 && t > TextLoopSeconds) t %= TextLoopSeconds;
        TextTime = t;
    }

    [RelayCommand] private void RestartTimeline() { TextTime = 0; TimelinePlaying = true; }

    // ---- recording hook (the Recording & Export page owns the real logic) ----
    [ObservableProperty] private RecordingViewModel? _recorder;
    public string RecordButtonText => Recorder?.IsRecording == true ? "Stop rec" : "Record";
    public bool IsRecordingNow => Recorder?.IsRecording == true;
    public void NotifyRecordingChanged() { OnPropertyChanged(nameof(RecordButtonText)); OnPropertyChanged(nameof(IsRecordingNow)); }
    [RelayCommand] private void Record() => Recorder?.ToggleRecordCommand.Execute(null);

    [ObservableProperty] private PresetDefinition? _currentPreset;
    [ObservableProperty] private string _currentPresetName = "Custom settings";
    [ObservableProperty] private PresetDefinition? _selectedQuickPreset;
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string _newPresetCategory = PresetCategories.Custom;
    [ObservableProperty] private string _presetStatus = "";
    public string[] PresetCategoryChoices { get; } = PresetCategories.BuiltIn.Append(PresetCategories.Custom).ToArray();
    public System.Collections.ObjectModel.ObservableCollection<PresetDefinition> PresetOptions { get; } = new();
    public bool CanUpdatePreset => CurrentPreset is { IsCustom: true };

    private ScreenshotRequest? _pending;
    private bool _loading;
    private readonly bool _previewOnly;

    public StudioViewModel(AudioViewModel audio, bool previewOnly = false)
    {
        Audio = audio;
        _previewOnly = previewOnly;
        InspectorViewModel = new StudioInspector(this);
        Visualizers = VisualizerRegistry.CreateAll().Select(v => new VisualizerEntry(v.Id, v.Name, v.Description, v.Is3D)).OrderBy(v => v.Name).ToList();
        _selectedAspect = Aspects[0];
        _selectedMonitor = Monitors[0];
        var s = App.Settings.Current;
        _loading = true;
        _selectedVisualizer = Visualizers.FirstOrDefault(v => v.Id == s.LastVisualizerId) ?? Visualizers[0];
        _selectedQuality = ParseQuality(s.LightweightMode ? "Low" : s.GraphicsQuality);
        ApplyQuality();
        SelectedPalette = Palettes[0];
        _loading = false;
        ApplyVisualizerDefaults();
        RefreshPresetOptions();
        App.Presets.Changed += RefreshPresetOptions;
        App.Settings.Changed += _ => { _loading = true; SelectedQuality = ParseQuality(App.Settings.Current.LightweightMode ? "Low" : App.Settings.Current.GraphicsQuality); _loading = false; ApplyQuality(); };
    }

    private static QualityLevel ParseQuality(string s) => Enum.TryParse<QualityLevel>(s, out var q) ? q : QualityLevel.High;

    partial void OnSelectedVisualizerChanged(VisualizerEntry value)
    {
        if (_loading) return;
        ApplyVisualizerDefaults();
        CurrentPreset = null; CurrentPresetName = "Custom settings";
        if (_previewOnly) return;
        var s = App.Settings.Current; s.LastVisualizerId = value.Id; App.Settings.Save(s);
    }

    private void ApplyVisualizerDefaults()
    {
        var v = VisualizerRegistry.Create(SelectedVisualizer.Id);
        Config.Camera.Reset(v.DefaultCamera);
        Is3D = v.Is3D;
        ResetToken++;
        RefreshInspector();
    }

    partial void OnSelectedQualityChanged(QualityLevel value) { if (!_loading) ApplyQuality(); }

    private void ApplyQuality()
    {
        var q = QualitySettings.For(SelectedQuality == QualityLevel.Custom ? QualityLevel.High : SelectedQuality);
        q.Level = SelectedQuality;
        var s = App.Settings.Current;
        if (SelectedQuality == QualityLevel.Custom) q.MaxParticles = Math.Clamp(s.ParticleLimit, 500, 200000);
        if (s.LightweightMode) { q.RenderScale = Math.Min(q.RenderScale, 0.6); q.BloomPasses = 0; q.MaxParticles = Math.Min(q.MaxParticles, 4000); q.ExpensiveEffects = false; }
        Config.Quality = q;
        RefreshInspector();
        UpdateWarning(1920, 1080);
    }

    public void UpdateWarning(int w, int h) => WarningText = Gpu?.Warn(Config.Quality, w, h) ?? "";

    partial void OnSelectedPaletteChanged(NamedPalette? value)
    {
        if (value is null || _loading) return;
        Config.Palette.Colors = (string[])value.Colors.Clone();
        Color0 = value.Colors[0]; Color1 = value.Colors[1]; Color2 = value.Colors[2]; Color3 = value.Colors[3];
    }

    partial void OnColor0Changed(string value) => ApplyColors();
    partial void OnColor1Changed(string value) => ApplyColors();
    partial void OnColor2Changed(string value) => ApplyColors();
    partial void OnColor3Changed(string value) => ApplyColors();

    private void ApplyColors()
    {
        if (_loading) return;
        var all = new[] { Color0, Color1, Color2, Color3 };
        try { foreach (var c in all) ColorUtil.Parse(c); }
        catch (Exception) { return; }          // ignore half-typed values
        Config.Palette.Colors = all;
    }

    public void RefreshInspector() => OnPropertyChanged(string.Empty);

    // ---- presets ---------------------------------------------------------------
    partial void OnCurrentPresetChanged(PresetDefinition? value) => OnPropertyChanged(nameof(CanUpdatePreset));

    private void RefreshPresetOptions()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _loading = true;
            PresetOptions.Clear();
            foreach (var p in PresetQuery.Apply(App.Presets.All, new PresetFilter(Sort: PresetSort.Category))) PresetOptions.Add(p);
            SelectedQuickPreset = CurrentPreset is null ? null : PresetOptions.FirstOrDefault(p => p.Id == CurrentPreset.Id);
            _loading = false;
        });
    }

    partial void OnSelectedQuickPresetChanged(PresetDefinition? value)
    {
        if (_loading || value is null) return;
        ApplyPreset(value);
    }

    /// <summary>Loads a preset's visualizer, look and (optionally) audio response into the live studio.</summary>
    public void ApplyPreset(PresetDefinition p, bool applyAudio = true)
    {
        var entry = Visualizers.FirstOrDefault(v => v.Id == p.VisualizerId);
        if (entry is null) { RenderError = $"Preset '{p.Name}' needs a visualizer that is not available."; return; }
        _loading = true;
        SelectedVisualizer = entry;
        p.ApplyTo(Config);
        Is3D = entry.Is3D;
        if (applyAudio && !_previewOnly) { p.Audio.ApplyTo(Audio.Params); Audio.RefreshParams(); }
        var cols = Config.Palette.Colors;
        Color0 = cols[0]; Color1 = cols[Math.Min(1, cols.Length - 1)]; Color2 = cols[Math.Min(2, cols.Length - 1)]; Color3 = cols[Math.Min(3, cols.Length - 1)];
        SelectedPalette = Palettes.FirstOrDefault(x => x.Colors.SequenceEqual(cols));
        CurrentPreset = p; CurrentPresetName = p.Name;
        SelectedQuickPreset = PresetOptions.FirstOrDefault(x => x.Id == p.Id);
        _loading = false;
        ResetToken++;
        RenderError = "";
        RefreshInspector();
        if (!_previewOnly) { var s = App.Settings.Current; s.LastVisualizerId = entry.Id; App.Settings.Save(s); }
    }

    /// <summary>Loads a complete look (used when a project is opened). The configuration objects are adopted as-is, so callers pass copies.</summary>
    public void ApplyLook(string visualizerId, VisualParams p, PaletteSettings pal, CameraSettings cam, BackgroundSettings bg, EffectSettings fx,
        IEnumerable<TextLayer> text, string aspectName, double loopSeconds, string? presetId, string? presetName)
    {
        var entry = Visualizers.FirstOrDefault(v => v.Id == visualizerId) ?? Visualizers[0];
        _loading = true;
        SelectedVisualizer = entry;
        Config.Params = p; Config.Palette = pal; Config.Camera = cam; Config.Background = bg; Config.Effects = fx;
        Config.TextLayers.Clear(); foreach (var l in text) Config.TextLayers.Add(l);
        SelectedTextLayer = null;
        Is3D = entry.Is3D;
        var cols = Config.Palette.Colors;
        Color0 = cols[0]; Color1 = cols[Math.Min(1, cols.Length - 1)]; Color2 = cols[Math.Min(2, cols.Length - 1)]; Color3 = cols[Math.Min(3, cols.Length - 1)];
        SelectedPalette = Palettes.FirstOrDefault(x => x.Colors.SequenceEqual(cols));
        SelectedAspect = Aspects.FirstOrDefault(a => a.Name == aspectName) ?? Aspects[0];
        TextLoopSeconds = loopSeconds; TextTime = 0; TimelinePlaying = true;
        var preset = presetId is null ? null : App.Presets.Find(presetId);
        CurrentPreset = preset; CurrentPresetName = presetName ?? preset?.Name ?? "Project look";
        SelectedQuickPreset = preset is null ? null : PresetOptions.FirstOrDefault(x => x.Id == preset.Id);
        _loading = false;
        ResetToken++; RenderError = "";
        RefreshInspector();
    }

    public const string ImageFilter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp";

    [RelayCommand]
    private void ChooseBackgroundImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose a background picture", Filter = ImageFilter };
        if (dlg.ShowDialog() != true) return;
        try { App.Projects.SetBackgroundImage(dlg.FileName); BackgroundStatus = "Picture added to the project."; }
        catch (AudioDimension.Projects.ProjectException ex) { BackgroundStatus = ex.Message; }
        RefreshInspector();
    }

    [RelayCommand]
    private void RemoveBackgroundImage() { App.Projects.ClearBackgroundImage(); BackgroundStatus = "Picture removed."; RefreshInspector(); }

    [ObservableProperty] private string _backgroundStatus = "";

    private PresetDefinition CaptureCurrent(string name, string category, string description) =>
        PresetDefinition.FromConfig(Config, PresetAudio.From(Audio.Params), SelectedVisualizer.Id, name, category, description);

    private ScreenshotRequest ThumbnailRequest(string id, Action? done) =>
        new(App.Presets.ThumbnailPath(id), 320, 180, false, false, done);

    [RelayCommand]
    private void SavePreset()
    {
        try
        {
            string name = string.IsNullOrWhiteSpace(NewPresetName) ? "My " + SelectedVisualizer.Name : NewPresetName;
            var saved = App.Presets.SaveNew(CaptureCurrent(name, NewPresetCategory, "Saved from Visualizer Studio."));
            System.IO.Directory.CreateDirectory(App.Presets.ThumbnailDirectory);
            _pending = ThumbnailRequest(saved.Id, () => App.Presets.RaiseChanged());
            CurrentPreset = saved; CurrentPresetName = saved.Name; NewPresetName = "";
            SelectedQuickPreset = PresetOptions.FirstOrDefault(x => x.Id == saved.Id);
            PresetStatus = $"Saved preset '{saved.Name}'.";
        }
        catch (Exception ex) { Log.Error(ex, "Saving preset failed"); PresetStatus = "Could not save preset: " + ex.Message; }
    }

    [RelayCommand]
    private void UpdatePreset()
    {
        if (CurrentPreset is not { IsCustom: true } cur) return;
        try
        {
            var upd = CaptureCurrent(cur.Name, cur.Category, cur.Description); upd.Id = cur.Id; upd.CreatedUtc = cur.CreatedUtc;
            App.Presets.Update(upd);
            _pending = ThumbnailRequest(cur.Id, () => App.Presets.RaiseChanged());
            CurrentPreset = App.Presets.Find(cur.Id);
            PresetStatus = $"Updated preset '{cur.Name}'.";
        }
        catch (Exception ex) { Log.Error(ex, "Updating preset failed"); PresetStatus = "Could not update preset: " + ex.Message; }
    }

    [RelayCommand]
    private void RevertToPreset() { if (CurrentPreset is { } p) ApplyPreset(p); }

    // ---- transport ---------------------------------------------------------------
    [RelayCommand]
    private void Play()
    {
        IsPaused = false;
        if (App.Audio.State is EngineState.Capturing or EngineState.Reconnecting) return;
        Audio.StartStopCommand.Execute(null);
    }

    [RelayCommand] private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void Stop()
    {
        IsPaused = false;
        if (App.Audio.State is EngineState.Capturing or EngineState.Reconnecting) Audio.StartStopCommand.Execute(null);
    }

    [RelayCommand]
    private void ResetCamera()
    {
        Config.Camera.Reset(VisualizerRegistry.Create(SelectedVisualizer.Id).DefaultCamera);
        RefreshInspector();
    }

    [RelayCommand] private void ResetVisualization() => ResetToken++;

    [RelayCommand]
    private void ResetAll()
    {
        var d = new VisualConfig();
        Config.Params = d.Params; Config.Effects = d.Effects; Config.Background = d.Background;
        SelectedPalette = Palettes[0];
        ApplyVisualizerDefaults();
    }

    // ---- screenshots ---------------------------------------------------------------
    [RelayCommand]
    private void Screenshot()
    {
        try
        {
            int w = 0, h = 0;
            if (SelectedScreenshotSize != "Preview size")
            {
                var parts = SelectedScreenshotSize.Split('x');
                h = int.Parse(parts[1]); w = int.Parse(parts[0]);
                double ratio = SelectedAspect.Ratio > 0 ? SelectedAspect.Ratio : 16.0 / 9;
                if (ratio < 1) (w, h) = (h, w);             // vertical presets swap the long edge
                if (Math.Abs(ratio - (double)w / h) > 0.01) w = (int)(h * ratio) & ~1;
            }
            bool jpeg = SelectedScreenshotFormat == "JPEG";
            var dir = System.IO.Path.Combine(App.Paths.Exports, "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"AudioDimension_{DateTime.Now:yyyyMMdd_HHmmss}.{(jpeg ? "jpg" : "png")}");
            _pending = new ScreenshotRequest(path, w, h, jpeg, ScreenshotTransparent && !jpeg);
            ScreenshotStatus = "Capturing...";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Screenshot request failed");
            ScreenshotStatus = "Screenshot failed: " + ex.Message;
        }
    }

    public ScreenshotRequest? TakePendingScreenshot() => Interlocked.Exchange(ref _pending, null);

    [RelayCommand]
    private void OpenScreenshots()
    {
        var dir = System.IO.Path.Combine(App.Paths.Exports, "Screenshots");
        System.IO.Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    // ---- separate preview windows -------------------------------------------------------
    [RelayCommand] private void Fullscreen() => Services.PreviewWindows.Open(this, fullscreen: true);
    [RelayCommand] private void Detach() => Services.PreviewWindows.Open(this, fullscreen: false);
}
