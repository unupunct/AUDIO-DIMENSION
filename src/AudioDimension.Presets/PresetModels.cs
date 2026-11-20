using System.Text.Json.Serialization;
using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;

namespace AudioDimension.Presets;

public static class PresetCategories
{
    public const string Neon = "Neon & Cyberpunk", Space = "Space & Cosmos", Geometric = "Geometric", Particles = "Particles",
        Waveforms = "Waveforms", Minimal = "Minimal & Elegant", Experimental = "Experimental", Custom = "Custom";

    public static readonly string[] BuiltIn = { Neon, Space, Geometric, Particles, Waveforms, Minimal, Experimental };
}

/// <summary>Knobs a preset exposes for customization (shown in the library; the inspector always lists all controls).</summary>
public static class PresetKnobs
{
    public static readonly string[] Vocabulary =
    {
        "speed", "reactivity", "scale", "lineWidth", "glow", "particleDensity", "rotation", "elements", "variant", "mirror",
        "palette", "camera", "background", "effects", "sensitivity", "frequencyResponse"
    };
}

/// <summary>Visual part of a preset. Quality is deliberately excluded: it belongs to the machine, not the look.</summary>
public sealed class PresetVisual
{
    public VisualParams Params { get; set; } = new();
    public PaletteSettings Palette { get; set; } = new();
    public CameraSettings Camera { get; set; } = new();
    public BackgroundSettings Background { get; set; } = new();
    public EffectSettings Effects { get; set; } = new();
}

/// <summary>Audio analysis defaults stored in a preset (sensitivity and frequency response).</summary>
public sealed class PresetAudio
{
    public double Sensitivity { get; set; } = 1.0;
    public double Smoothing { get; set; } = 0.6;
    public double BassSensitivity { get; set; } = 1.0;
    public double MidSensitivity { get; set; } = 1.0;
    public double TrebleSensitivity { get; set; } = 1.0;
    public double FrequencyTilt { get; set; }
    public bool AutoGain { get; set; } = true;
    public bool PeakDetection { get; set; } = true;
    public bool[] BandEnabled { get; set; } = { true, true, true, true, true, true };

    public void ApplyTo(AudioParams p)
    {
        p.Sensitivity = Sensitivity; p.Smoothing = Smoothing; p.BassSensitivity = BassSensitivity; p.MidSensitivity = MidSensitivity;
        p.TrebleSensitivity = TrebleSensitivity; p.FrequencyTilt = FrequencyTilt; p.AutoGain = AutoGain; p.PeakDetection = PeakDetection;
        for (int i = 0; i < 6; i++) p.BandEnabled[i] = BandEnabled.Length > i ? BandEnabled[i] : true;
    }

    public static PresetAudio From(AudioParams p) => new()
    {
        Sensitivity = p.Sensitivity, Smoothing = p.Smoothing, BassSensitivity = p.BassSensitivity, MidSensitivity = p.MidSensitivity,
        TrebleSensitivity = p.TrebleSensitivity, FrequencyTilt = p.FrequencyTilt, AutoGain = p.AutoGain, PeakDetection = p.PeakDetection,
        BandEnabled = (bool[])p.BandEnabled.Clone()
    };
}

/// <summary>
/// A preset = metadata + which rendering implementation to use + configuration. Pure data: presets never contain code
/// and the renderer only reads the fields below. See docs/PRESET_SCHEMA.md.
/// </summary>
public sealed class PresetDefinition
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = PresetCategories.Custom;
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>Id of a registered rendering implementation (see VisualizerRegistry).</summary>
    public string VisualizerId { get; set; } = "";
    public PresetVisual Visual { get; set; } = new();
    public PresetAudio Audio { get; set; } = new();
    public string[] Customizable { get; set; } = Array.Empty<string>();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Name;

    [JsonIgnore] public bool IsCustom { get; set; }
    [JsonIgnore] public bool IsFavorite { get; set; }

    public PresetDefinition Clone()
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<PresetDefinition>(System.Text.Json.JsonSerializer.Serialize(this, PresetJson.Options), PresetJson.Options)!;
        c.IsCustom = IsCustom; c.IsFavorite = IsFavorite;
        return c;
    }

    /// <summary>Copies the preset's look into a live config (quality is preserved).</summary>
    public void ApplyTo(VisualConfig config)
    {
        var v = Clone().Visual;
        config.Params = v.Params; config.Palette = v.Palette; config.Camera = v.Camera; config.Background = v.Background; config.Effects = v.Effects;
    }

    public static PresetDefinition FromConfig(VisualConfig cfg, PresetAudio audio, string visualizerId, string name, string category, string description)
    {
        var tmp = cfg.Clone();
        return new PresetDefinition
        {
            Name = name, Category = category, Description = description, VisualizerId = visualizerId, Audio = audio, Author = Environment.UserName,
            Visual = new PresetVisual { Params = tmp.Params, Palette = tmp.Palette, Camera = tmp.Camera, Background = tmp.Background, Effects = tmp.Effects },
            Customizable = PresetKnobs.Vocabulary, IsCustom = true
        };
    }
}

public static class PresetJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,     // files use camelCase as documented; older PascalCase files still load
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.Strict
    };
}
