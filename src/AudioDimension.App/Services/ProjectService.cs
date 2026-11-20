using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AudioDimension.App.ViewModels;
using AudioDimension.Presets;
using AudioDimension.Projects;
using AudioDimension.Visuals.Config;
using NAudio.Wave;
using Serilog;
using SkiaSharp;

namespace AudioDimension.App.Services;

/// <summary>
/// Owns the open project: captures the whole studio state into a <see cref="ProjectFile"/>, restores it, saves/opens .adproj packages,
/// manages embedded assets and reports unsaved changes by comparing a snapshot of the state (no per-property wiring to maintain).
/// </summary>
public sealed class ProjectService
{
    private readonly ProjectStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private StudioViewModel? _studio;
    private AudioViewModel? _audio;
    private RecordingViewModel? _recorder;
    private AssetWorkspace _ws;
    private string _savedSnapshot = "";
    private string? _audioAssetId, _audioSourcePath;
    private bool _dirty;

    public ProjectService(ProjectStore store)
    {
        _store = store; _ws = store.NewWorkspace();
        _timer.Tick += (_, _) => CheckDirty();
    }

    public ProjectStore Store => _store;
    public ProjectFile Current { get; private set; } = new();
    public string? CurrentPath { get; private set; }
    public bool IncludeRecording { get; set; }
    public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();
    public AssetWorkspace Workspace => _ws;
    public bool IsDirty { get => _dirty; private set { if (_dirty != value) { _dirty = value; Changed?.Invoke(); } } }
    public string Title => (string.IsNullOrWhiteSpace(Current.Name) ? "Untitled project" : Current.Name) + (IsDirty ? " *" : "");
    public event Action? Changed;

    /// <summary>Called once the view models exist; the untitled starting project is whatever the studio currently shows.</summary>
    public void Attach(StudioViewModel studio, AudioViewModel audio, RecordingViewModel recorder)
    {
        _studio = studio; _audio = audio; _recorder = recorder;
        Current = new ProjectFile { Name = "Untitled project", AppVersion = AppVersion };
        _savedSnapshot = Snapshot();
        _timer.Start();
    }

    public static string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    // ---- capture ---------------------------------------------------------------------------
    public ProjectFile Capture()
    {
        var studio = _studio ?? throw new InvalidOperationException("Project service is not attached.");
        var cfg = studio.Config.Clone();
        var dev = _audio!.SelectedDevice;
        var p = new ProjectFile
        {
            Id = Current.Id, Name = Current.Name, Description = Current.Description, CreatedUtc = Current.CreatedUtc, AppVersion = AppVersion,
            Visual = new ProjectVisual
            {
                VisualizerId = studio.SelectedVisualizer.Id, PresetId = studio.CurrentPreset?.Id, PresetName = studio.CurrentPreset?.Name,
                Params = cfg.Params, Palette = cfg.Palette, Camera = cfg.Camera, Background = cfg.Background, Effects = cfg.Effects, AspectName = studio.SelectedAspect.Name
            },
            TextLayers = cfg.TextLayers.ToList(),
            Audio = new ProjectAudio
            {
                Source = _audio.SelectedSource == _audio.Sources[1] ? "SystemAudio" : "Microphone", DeviceId = dev?.Id, DeviceName = dev?.Name,
                InputGain = App.Audio.Params.InputGain, NoiseGateDb = App.Audio.Params.NoiseGateDb, Analysis = PresetAudio.From(App.Audio.Params)
            },
            Recording = _recorder!.ToProject(),
            Timeline = new ProjectTimeline { TextLoopSeconds = studio.TextLoopSeconds }
        };
        return p;
    }

    private string Snapshot()
    {
        var p = Capture(); p.ModifiedUtc = default; p.Visual.Background.ImagePath = null;
        return JsonSerializer.Serialize(p, PresetJson.Options) + "|" + IncludeRecording + "|" + (_audioAssetId ?? "") + (IncludeRecording ? App.Recording.CapturedAudioPath : "");
    }

    /// <summary>Recomputes the unsaved-changes flag now (the timer calls this about once a second).</summary>
    public void RefreshDirty() => CheckDirty();

    private void CheckDirty()
    {
        try { IsDirty = _studio is not null && Snapshot() != _savedSnapshot; }
        catch (Exception ex) { Log.Debug(ex, "Dirty check skipped"); }
    }

    public void MarkClean() { _savedSnapshot = Snapshot(); IsDirty = false; Changed?.Invoke(); }

    public void Rename(string name)
    {
        Current.Name = string.IsNullOrWhiteSpace(name) ? "Untitled project" : name.Trim();
        Changed?.Invoke();
    }

    // ---- save / open ---------------------------------------------------------------------------
    public void Save(string path)
    {
        var p = Capture();
        if (IncludeRecording && App.Recording.HasCapture)
        {
            string cap = App.Recording.CapturedAudioPath!;
            if (_audioSourcePath != cap)
            {
                if (_audioAssetId is not null) _ws.Remove(_audioAssetId);
                _audioAssetId = _ws.Add(AssetKinds.Audio, cap); _audioSourcePath = cap;
            }
        }
        else if (_audioAssetId is not null && !IncludeRecording) { _ws.Remove(_audioAssetId); _audioAssetId = null; _audioSourcePath = null; }
        ProjectPackage.Write(path, p, _ws.Referenced(new[] { p.Visual.Background.AssetId, _audioAssetId }), _ws.Meta);
        var saved = ProjectPackage.Peek(path);
        Current.Id = p.Id; Current.ModifiedUtc = saved.ModifiedUtc;
        CurrentPath = path;
        _store.RecordSaved(Current, path);
        MarkClean();
    }

    public void Open(string path)
    {
        _ws = _store.NewWorkspace();
        var res = ProjectPackage.Read(path, _ws.Directory);
        Apply(res, path);
        _store.RecordOpened(path, res.Project.Name);
    }

    public void New()
    {
        _ws = _store.NewWorkspace();
        var p = new ProjectFile { Name = "Untitled project", AppVersion = AppVersion };
        p.Visual.VisualizerId = _studio!.Visualizers[0].Id;
        var d = new VisualConfig();
        p.Visual.Params = d.Params; p.Visual.Palette = d.Palette; p.Visual.Camera = d.Camera; p.Visual.Background = d.Background; p.Visual.Effects = d.Effects;
        p.Recording = new ProjectRecording();
        Apply(new ProjectLoadResult(p, new Dictionary<string, string>(), Array.Empty<string>()), null);
    }

    private void Apply(ProjectLoadResult res, string? path)
    {
        var p = res.Project; var warnings = new List<string>(res.Warnings);
        _ws.Load(res.AssetPaths, p.Assets);

        var bg = p.Visual.Background;
        if (bg.Mode == BackgroundMode.Image)
        {
            var img = _ws.Resolve(bg.AssetId);
            if (img is null) { warnings.Add("The background picture is missing from this project, so the default gradient is used instead."); bg.Mode = BackgroundMode.Gradient; bg.AssetId = null; }
            else bg.ImagePath = img;
        }

        _studio!.ApplyLook(p.Visual.VisualizerId, p.Visual.Params, p.Visual.Palette, p.Visual.Camera, bg, p.Visual.Effects, p.TextLayers, p.Visual.AspectName, p.Timeline.TextLoopSeconds, p.Visual.PresetId, p.Visual.PresetName);
        var a = p.Audio;
        a.Analysis.ApplyTo(App.Audio.Params); App.Audio.Params.InputGain = a.InputGain; App.Audio.Params.NoiseGateDb = a.NoiseGateDb;
        var note = _audio!.ApplyProjectAudio(a.Source, a.DeviceId, a.DeviceName); if (note is not null) warnings.Add(note);
        _audio.RefreshParams();
        _recorder!.ApplyProject(p.Recording);
        var recDir = _recorder.OutputFolder;
        if (!Directory.Exists(recDir)) { try { Directory.CreateDirectory(recDir); } catch (Exception) { _recorder.OutputFolder = App.Paths.Exports; } }

        _audioAssetId = null; _audioSourcePath = null; IncludeRecording = false;
        var audioAsset = p.Assets.FirstOrDefault(x => x.Kind == AssetKinds.Audio && _ws.Resolve(x.Id) is not null);
        if (audioAsset is not null)
        {
            try
            {
                string f = _ws.Resolve(audioAsset.Id)!;
                using var r = new AudioFileReader(f);
                App.Recording.UseExternalAudio(f, r.TotalTime.TotalSeconds);
                _audioAssetId = audioAsset.Id; _audioSourcePath = f; IncludeRecording = true;
            }
            catch (Exception ex) { warnings.Add("The recording stored in this project could not be read: " + ex.Message); }
        }

        Current = new ProjectFile { Id = p.Id, Name = p.Name, Description = p.Description, CreatedUtc = p.CreatedUtc, ModifiedUtc = p.ModifiedUtc, AppVersion = p.AppVersion };
        CurrentPath = path; Warnings = warnings;
        _savedSnapshot = Snapshot(); IsDirty = false; Changed?.Invoke();
        Log.Information("Project loaded: {Name} ({Warnings} warnings)", p.Name, warnings.Count);
    }

    // ---- background picture asset ----------------------------------------------------------------
    public void SetBackgroundImage(string file)
    {
        var fi = new FileInfo(file);
        if (!fi.Exists) throw new ProjectException("That picture could not be found.");
        if (fi.Length > 60_000_000) throw new ProjectException("That picture is larger than 60 MB. Please choose a smaller one.");
        using (var codec = SKCodec.Create(file))
        {
            if (codec is null) throw new ProjectException("That file is not a picture Audio Dimension can read (PNG, JPEG, BMP, GIF or WebP).");
            if (codec.Info.Width > 16384 || codec.Info.Height > 16384) throw new ProjectException("That picture is too large (over 16384 pixels on a side).");
        }
        var bg = _studio!.Config.Background;
        if (bg.AssetId is not null) _ws.Remove(bg.AssetId);
        string id = _ws.Add(AssetKinds.Image, file);
        bg.AssetId = id; bg.ImagePath = _ws.Resolve(id); bg.Mode = BackgroundMode.Image;
    }

    public void ClearBackgroundImage()
    {
        var bg = _studio!.Config.Background;
        if (bg.AssetId is not null) _ws.Remove(bg.AssetId);
        bg.AssetId = null; bg.ImagePath = null;
        if (bg.Mode == BackgroundMode.Image) bg.Mode = BackgroundMode.Gradient;
    }
}
