using System.Text.Json;
using System.Text.Json.Serialization;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Visualizers;

namespace AudioDimension.Presets;

public sealed record ImportResult(IReadOnlyList<PresetDefinition> Presets, IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0 && Presets.Count > 0;
}

/// <summary>On-disk wrapper so several presets can travel together. A bare preset object is also accepted.</summary>
public sealed class PresetFile
{
    public string Format { get; set; } = "AudioDimension.Presets";
    public int SchemaVersion { get; set; } = PresetDefinition.CurrentSchemaVersion;
    public List<PresetDefinition> Presets { get; set; } = new();
}

/// <summary>
/// Validates untrusted preset data. Presets are plain data: this parser only deserialises into typed fields, ignores unknown
/// members, bounds every number and rejects anything outside the schema. Nothing is ever executed or loaded from a preset.
/// </summary>
public static class PresetValidator
{
    public const int MaxFileBytes = 512 * 1024;

    /// <param name="allowAssets">Presets are pure data and cannot carry images; projects can (their assets travel inside the package).</param>
    public static IReadOnlyList<string> Validate(PresetDefinition p, bool allowAssets = false)
    {
        var e = new List<string>();
        string n = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;
        if (p.SchemaVersion < 1 || p.SchemaVersion > PresetDefinition.CurrentSchemaVersion) e.Add($"{n}: unsupported schemaVersion {p.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80 || p.Name.Any(char.IsControl)) e.Add("name must be 1-80 printable characters");
        if (string.IsNullOrWhiteSpace(p.Category) || p.Category.Length > 40 || p.Category.Any(char.IsControl)) e.Add($"{n}: category must be 1-40 printable characters");
        if ((p.Description ?? "").Length > 600) e.Add($"{n}: description longer than 600 characters");
        if ((p.Author ?? "").Length > 80) e.Add($"{n}: author longer than 80 characters");
        if (p.Tags is null || p.Tags.Length > 16 || p.Tags.Any(t => t is null || t.Length > 32)) e.Add($"{n}: tags must be at most 16 entries of 32 characters");
        if (string.IsNullOrEmpty(p.VisualizerId) || !VisualizerRegistry.Exists(p.VisualizerId)) e.Add($"{n}: unknown visualizerId '{p.VisualizerId}'");
        if (p.Customizable is null || p.Customizable.Any(k => !PresetKnobs.Vocabulary.Contains(k))) e.Add($"{n}: customizable contains an unknown option");
        if (p.Visual is null || p.Audio is null) { e.Add($"{n}: visual and audio sections are required"); return e; }

        var v = p.Visual;
        if (v.Params is null || v.Palette is null || v.Camera is null || v.Background is null || v.Effects is null) { e.Add($"{n}: visual section incomplete"); return e; }
        Range(e, n, "params.speed", v.Params.Speed, 0, 4); Range(e, n, "params.reactivity", v.Params.Reactivity, 0, 3);
        Range(e, n, "params.scale", v.Params.Scale, 0.2, 3); Range(e, n, "params.lineWidth", v.Params.LineWidth, 0.5, 16);
        Range(e, n, "params.particleDensity", v.Params.ParticleDensity, 0.05, 2); Range(e, n, "params.glow", v.Params.Glow, 0, 4);
        Range(e, n, "params.segments", v.Params.Segments, 0, 128); Range(e, n, "params.variant", v.Params.Variant, 0, 3);
        Range(e, n, "params.rotation", v.Params.Rotation, -360, 360);

        if (v.Palette.Colors is null || v.Palette.Colors.Length is < 2 or > 8) e.Add($"{n}: palette needs 2-8 colours");
        else foreach (var c in v.Palette.Colors) Hex(e, n, "palette", c);
        Hex(e, n, "background.color1", v.Background.Color1); Hex(e, n, "background.color2", v.Background.Color2);
        if (!Enum.IsDefined(v.Background.Mode)) e.Add($"{n}: unknown background mode");
        if (!allowAssets && (v.Background.Mode == BackgroundMode.Image || v.Background.AssetId is not null || v.Background.ImagePath is not null)) e.Add($"{n}: image backgrounds can only be used in projects, not presets");
        Range(e, n, "background.imageDim", v.Background.ImageDim, 0, 1);
        Range(e, n, "background.animationSpeed", v.Background.AnimationSpeed, 0, 4); Range(e, n, "background.audioReactive", v.Background.AudioReactive, 0, 4);

        var c2 = v.Camera;
        Range(e, n, "camera.yaw", c2.Yaw, -360, 360); Range(e, n, "camera.pitch", c2.Pitch, -90, 90); Range(e, n, "camera.distance", c2.Distance, 0.5, 80);
        Range(e, n, "camera.fov", c2.Fov, 10, 130); Range(e, n, "camera.autoRotateSpeed", c2.AutoRotateSpeed, -180, 180);
        Range(e, n, "camera.audioShake", c2.AudioShake, 0, 1); Range(e, n, "camera.zoom2D", c2.Zoom2D, 0.2, 5);

        var fx = v.Effects;
        foreach (var (name, val, lo, hi) in new (string, double, double, double)[]
        {
            ("bloomAmount", fx.BloomAmount, 0, 5), ("bloomThreshold", fx.BloomThreshold, 0, 1), ("vignetteAmount", fx.VignetteAmount, 0, 1.5),
            ("chromaticAmount", fx.ChromaticAmount, 0, 0.1), ("rgbSeparationAmount", fx.RgbSeparationAmount, 0, 0.05), ("filmGrainAmount", fx.FilmGrainAmount, 0, 1),
            ("scanlineAmount", fx.ScanlineAmount, 0, 1), ("saturation", fx.Saturation, 0, 3), ("contrast", fx.Contrast, 0.3, 3), ("brightness", fx.Brightness, -1, 1),
            ("hueShift", fx.HueShift, -360, 360), ("distortionAmount", fx.DistortionAmount, -1, 1), ("blurAmount", fx.BlurAmount, 0, 1), ("noiseAmount", fx.NoiseAmount, 0, 1),
            ("motionBlurAmount", fx.MotionBlurAmount, 0, 0.98), ("lightRaysAmount", fx.LightRaysAmount, 0, 3)
        })
            Range(e, n, "effects." + name, val, lo, hi);

        var a = p.Audio;
        Range(e, n, "audio.sensitivity", a.Sensitivity, 0.05, 8); Range(e, n, "audio.smoothing", a.Smoothing, 0, 0.99);
        Range(e, n, "audio.bassSensitivity", a.BassSensitivity, 0, 4); Range(e, n, "audio.midSensitivity", a.MidSensitivity, 0, 4);
        Range(e, n, "audio.trebleSensitivity", a.TrebleSensitivity, 0, 4); Range(e, n, "audio.frequencyTilt", a.FrequencyTilt, -1, 1);
        if (a.BandEnabled is null || a.BandEnabled.Length != 6) e.Add($"{n}: audio.bandEnabled must have 6 entries");
        return e;
    }

    private static void Range(List<string> e, string n, string field, double v, double lo, double hi)
    {
        if (double.IsNaN(v) || double.IsInfinity(v) || v < lo || v > hi) e.Add($"{n}: {field} = {v} is outside {lo}..{hi}");
    }

    private static void Hex(List<string> e, string n, string field, string? c)
    {
        if (c is null || c.Length != 7 || c[0] != '#' || !c.Skip(1).All(Uri.IsHexDigit)) e.Add($"{n}: {field} colour '{c}' must be #RRGGBB");
    }

    /// <summary>Parses and validates preset JSON (single preset or a pack). Nothing is returned unless every preset is valid.</summary>
    public static ImportResult Parse(string json)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "The file is empty." });
        if (json.Length > MaxFileBytes) return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "The file is too large to be a preset." });
        List<PresetDefinition> list;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "Presets must be a JSON object." });
            if (doc.RootElement.TryGetProperty("presets", out var arr) || doc.RootElement.TryGetProperty("Presets", out arr))
            {
                if (arr.ValueKind != JsonValueKind.Array) return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "'presets' must be an array." });
                list = JsonSerializer.Deserialize<PresetFile>(json, PresetJson.Options)!.Presets;
            }
            else list = new List<PresetDefinition> { JsonSerializer.Deserialize<PresetDefinition>(json, PresetJson.Options)! };
        }
        catch (JsonException ex) { return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "Invalid preset file: " + ex.Message }); }
        if (list.Count == 0) return new ImportResult(list, new[] { "The file contains no presets." });
        if (list.Count > 200) return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "Too many presets in one file (limit 200)." });
        foreach (var p in list) errors.AddRange(p is null ? new[] { "A preset entry was empty." } : Validate(p));
        return errors.Count == 0 ? new ImportResult(list, errors) : new ImportResult(Array.Empty<PresetDefinition>(), errors);
    }

    public static string Serialize(IEnumerable<PresetDefinition> presets) =>
        JsonSerializer.Serialize(new PresetFile { Presets = presets.ToList() }, PresetJson.Options);
}
