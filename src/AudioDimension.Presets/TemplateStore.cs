using System.Text.Json;
using AudioDimension.Core.Paths;
using AudioDimension.Visuals.Text;
using Serilog;

namespace AudioDimension.Presets;

public static class TextValidator
{
    public const int MaxFileBytes = 512 * 1024;

    public static IReadOnlyList<string> Validate(TitleTemplate t)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 80 || t.Name.Any(char.IsControl)) e.Add("template name must be 1-80 printable characters");
        if (t.SchemaVersion != 1) e.Add("unsupported template schemaVersion");
        if (t.Layers is null || t.Layers.Count is 0 or > 30) { e.Add("a template needs 1-30 text layers"); return e; }
        int i = 0;
        foreach (var l in t.Layers)
        {
            i++; string n = $"layer {i}";
            if ((l.Text ?? "").Length > 2000) e.Add($"{n}: text too long");
            if (string.IsNullOrWhiteSpace(l.FontFamily) || l.FontFamily.Length > 80 || l.FontFamily.Any(char.IsControl)) e.Add($"{n}: invalid font family");
            Rng(e, n, "fontSize", l.FontSize, 4, 600); Rng(e, n, "fontWeight", l.FontWeight, 100, 900); Rng(e, n, "opacity", l.Opacity, 0, 1);
            Rng(e, n, "x", l.X, -1, 2); Rng(e, n, "y", l.Y, -1, 2); Rng(e, n, "rotation", l.Rotation, -720, 720); Rng(e, n, "scale", l.Scale, 0.05, 10);
            Rng(e, n, "letterSpacing", l.LetterSpacing, -50, 200); Rng(e, n, "lineSpacing", l.LineSpacing, 0.3, 4); Rng(e, n, "padding", l.Padding, 0, 300);
            Rng(e, n, "startTime", l.StartTime, 0, 86400); Rng(e, n, "endTime", l.EndTime, 0, 86400);
            foreach (var (name, hex) in new[] { ("color", l.Color), ("shadow.color", l.Shadow?.Color), ("outline.color", l.Outline?.Color), ("glow.color", l.Glow?.Color), ("panel.color", l.Panel?.Color), ("border.color", l.Border?.Color) })
                if (hex is null || hex.Length is not (7 or 9) || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit)) e.Add($"{n}: {name} must be #RRGGBB");
            if (l.Shadow is null || l.Outline is null || l.Glow is null || l.Panel is null || l.Border is null) { e.Add($"{n}: style sections are required"); continue; }
            Rng(e, n, "shadow.blur", l.Shadow.Blur, 0, 100); Rng(e, n, "outline.width", l.Outline.Width, 0, 40); Rng(e, n, "glow.radius", l.Glow.Radius, 0, 120);
            Rng(e, n, "glow.intensity", l.Glow.Intensity, 0, 4); Rng(e, n, "panel.opacity", l.Panel.Opacity, 0, 1); Rng(e, n, "border.width", l.Border.Width, 0, 40);
            if (!Enum.IsDefined(l.Kind) || !Enum.IsDefined(l.Align)) e.Add($"{n}: unknown kind or alignment");
            if (l.Animations is null || l.Animations.Count > 12) { e.Add($"{n}: at most 12 animations"); continue; }
            foreach (var a in l.Animations)
            {
                if (!Enum.IsDefined(a.Type) || !Enum.IsDefined(a.Easing) || !Enum.IsDefined(a.Direction)) e.Add($"{n}: unknown animation option");
                Rng(e, n, "animation.duration", a.Duration, 0.05, 600); Rng(e, n, "animation.delay", a.Delay, 0, 600); Rng(e, n, "animation.amount", a.Amount, 0, 10);
            }
        }
        return e;
    }

    private static void Rng(List<string> e, string n, string f, double v, double lo, double hi)
    {
        if (double.IsNaN(v) || double.IsInfinity(v) || v < lo || v > hi) e.Add($"{n}: {f} = {v} outside {lo}..{hi}");
    }

    public static (TitleTemplate? Template, IReadOnlyList<string> Errors) Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxFileBytes) return (null, new[] { "The file is empty or too large." });
        try
        {
            var t = JsonSerializer.Deserialize<TitleTemplate>(json, PresetJson.Options);
            if (t is null) return (null, new[] { "The file is empty." });
            var errors = Validate(t);
            return errors.Count == 0 ? (t, errors) : (null, errors);
        }
        catch (JsonException ex) { return (null, new[] { "Invalid template file: " + ex.Message }); }
    }
}

/// <summary>Built-in title templates plus user templates saved as .adtemplate files in the Templates folder.</summary>
public sealed class TemplateStore
{
    private readonly AppPaths _paths;
    public TemplateStore(AppPaths paths) { _paths = paths; }

    public IReadOnlyList<TitleTemplate> BuiltIn => TitleTemplates.All;

    public IReadOnlyList<TitleTemplate> Custom()
    {
        var list = new List<TitleTemplate>();
        if (!Directory.Exists(_paths.Templates)) return list;
        foreach (var f in Directory.EnumerateFiles(_paths.Templates, "*.adtemplate").OrderBy(x => x))
        {
            try
            {
                var (t, errors) = TextValidator.Parse(File.ReadAllText(f));
                if (t is null) { Log.Warning("Skipping invalid template {File}: {Err}", f, string.Join("; ", errors)); continue; }
                t.IsCustom = true; list.Add(t);
            }
            catch (IOException ex) { Log.Warning(ex, "Could not read template {File}", f); }
        }
        return list;
    }

    public IReadOnlyList<TitleTemplate> All() => BuiltIn.Concat(Custom()).ToList();

    private string FileFor(string name) => Path.Combine(_paths.Templates, BuiltInPresets.Slug(name) + ".adtemplate");

    public TitleTemplate Save(string name, IEnumerable<TextLayer> layers, string description = "")
    {
        var copy = layers.Select(l => l.Clone()).ToList();
        var t = new TitleTemplate { Name = name.Trim(), Description = description, Layers = copy, IsCustom = true };
        var errors = TextValidator.Validate(t);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));
        if (BuiltIn.Any(b => b.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase))) t.Name += " (custom)";
        Directory.CreateDirectory(_paths.Templates);
        File.WriteAllText(FileFor(t.Name), JsonSerializer.Serialize(t, PresetJson.Options));
        return t;
    }

    public bool Delete(string name)
    {
        var f = FileFor(name);
        if (!File.Exists(f)) return false;
        File.Delete(f); return true;
    }

    public TitleTemplate? Import(string path, out IReadOnlyList<string> errors)
    {
        string json;
        try { json = File.ReadAllText(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors = new[] { ex.Message }; return null; }
        var (t, errs) = TextValidator.Parse(json); errors = errs;
        return t is null ? null : Save(t.Name, t.Layers, t.Description);
    }

    public void Export(TitleTemplate t, string path) => File.WriteAllText(path, JsonSerializer.Serialize(t, PresetJson.Options));
}
