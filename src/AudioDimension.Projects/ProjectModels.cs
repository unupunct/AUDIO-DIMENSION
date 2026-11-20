using AudioDimension.Presets;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Text;

namespace AudioDimension.Projects;

/// <summary>Raised for project problems the user can act on (corrupt file, unsupported version...).</summary>
public sealed class ProjectException : Exception
{
    public ProjectException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class AssetKinds { public const string Image = "Image", Audio = "Audio"; }

/// <summary>An embedded file. <see cref="Path"/> is relative inside the package; nothing in a project is an absolute path to user files.</summary>
public sealed class ProjectAsset
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = AssetKinds.Image;
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Bytes { get; set; }
}

public sealed class ProjectVisual
{
    public string VisualizerId { get; set; } = "circular-spectrum";
    /// <summary>The preset this look started from (informational: the full configuration below is what is used, so deleting a preset never breaks a project).</summary>
    public string? PresetId { get; set; }
    public string? PresetName { get; set; }
    public VisualParams Params { get; set; } = new();
    public PaletteSettings Palette { get; set; } = new();
    public CameraSettings Camera { get; set; } = new();
    public BackgroundSettings Background { get; set; } = new();
    public EffectSettings Effects { get; set; } = new();
    public string AspectName { get; set; } = "16:9";
}

public sealed class ProjectAudio
{
    public string Source { get; set; } = "Microphone";
    /// <summary>Windows endpoint id; devices differ per PC, so the name is kept as a fallback and the PC default is used when neither is found.</summary>
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public double InputGain { get; set; } = 1;
    public double NoiseGateDb { get; set; } = -80;
    public PresetAudio Analysis { get; set; } = new();
}

public sealed class ProjectRecording
{
    public string Format { get; set; } = "Mp4H264";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 30;
    public int Quality { get; set; } = 75;
    public string Encoder { get; set; } = "Auto";
    public bool IncludeAudio { get; set; } = true;
    public bool AutoRender { get; set; } = true;
    public string FilePrefix { get; set; } = "AudioDimension";
}

public sealed class ProjectTimeline { public double TextLoopSeconds { get; set; } = 10; }

/// <summary>Documented project format (see docs/PROJECT_FORMAT.md). Serialised as project.json inside a .adproj package.</summary>
public sealed class ProjectFile
{
    public const int CurrentFormat = 1;
    public int FormatVersion { get; set; } = CurrentFormat;
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Untitled project";
    public string Description { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public string AppVersion { get; set; } = "1.0.0";
    public ProjectVisual Visual { get; set; } = new();
    public List<TextLayer> TextLayers { get; set; } = new();
    public ProjectAudio Audio { get; set; } = new();
    public ProjectRecording Recording { get; set; } = new();
    public ProjectTimeline Timeline { get; set; } = new();
    public List<ProjectAsset> Assets { get; set; } = new();

    public ProjectFile Clone() => System.Text.Json.JsonSerializer.Deserialize<ProjectFile>(System.Text.Json.JsonSerializer.Serialize(this, PresetJson.Options), PresetJson.Options)!;
}
