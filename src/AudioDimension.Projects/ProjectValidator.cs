using System.Text.RegularExpressions;
using AudioDimension.Presets;
using AudioDimension.Visuals.Text;

namespace AudioDimension.Projects;

/// <summary>Validates untrusted project data. Everything is bounds-checked; nothing in a project is ever executed.</summary>
public static partial class ProjectValidator
{
    public const int MaxAssets = 40;

    [GeneratedRegex(@"^[a-z0-9\-]{4,40}$")] private static partial Regex IdPattern();
    [GeneratedRegex(@"^assets/[A-Za-z0-9][A-Za-z0-9._\-]{0,100}$")] private static partial Regex AssetPath();

    public static IReadOnlyList<string> Validate(ProjectFile p)
    {
        var e = new List<string>();
        if (p.FormatVersion < 1 || p.FormatVersion > ProjectFile.CurrentFormat) e.Add($"This project uses format version {p.FormatVersion}, which this version of Audio Dimension cannot open.");
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100 || p.Name.Any(char.IsControl)) e.Add("The project name must be 1-100 printable characters.");
        if ((p.Description ?? "").Length > 2000) e.Add("The description is too long.");
        if (!IdPattern().IsMatch(p.Id ?? "")) e.Add("The project id is invalid.");
        if (p.Visual is null || p.Audio is null || p.Recording is null || p.Timeline is null || p.TextLayers is null || p.Assets is null) { e.Add("The project is missing required sections."); return e; }

        // reuse the preset rules for the whole look + audio response (projects may reference an image asset, presets may not)
        var look = new PresetDefinition
        {
            Name = "project", Category = "Custom", VisualizerId = p.Visual.VisualizerId, Customizable = Array.Empty<string>(),
            Visual = new PresetVisual { Params = p.Visual.Params, Palette = p.Visual.Palette, Camera = p.Visual.Camera, Background = p.Visual.Background, Effects = p.Visual.Effects },
            Audio = p.Audio.Analysis
        };
        e.AddRange(PresetValidator.Validate(look, allowAssets: true).Select(x => x.Replace("project: ", "")));
        Rng(e, "audio.inputGain", p.Audio.InputGain, 0, 8); Rng(e, "audio.noiseGateDb", p.Audio.NoiseGateDb, -120, 0);
        if (p.Audio.Source is not ("Microphone" or "SystemAudio")) e.Add("Unknown audio source.");
        if ((p.Audio.DeviceId ?? "").Length > 300 || (p.Audio.DeviceName ?? "").Length > 300) e.Add("Audio device information is too long.");
        if ((p.Visual.PresetId ?? "").Length > 80 || (p.Visual.PresetName ?? "").Length > 80 || (p.Visual.AspectName ?? "").Length > 40) e.Add("Preset reference is too long.");

        if (p.TextLayers.Count > 30) e.Add("Too many text layers (limit 30).");
        else if (p.TextLayers.Count > 0)
            e.AddRange(TextValidator.Validate(new TitleTemplate { Name = "project", Layers = p.TextLayers }));

        var r = p.Recording;
        if (!Enum.TryParse<Recording.VideoFormat>(r.Format, out _)) e.Add("Unknown video format.");
        if (!Enum.TryParse<Recording.EncoderPreference>(r.Encoder, out _)) e.Add("Unknown encoder preference.");
        Rng(e, "recording.width", r.Width, 128, 7680); Rng(e, "recording.height", r.Height, 128, 4320); Rng(e, "recording.fps", r.Fps, 1, 120); Rng(e, "recording.quality", r.Quality, 1, 100);
        if ((r.FilePrefix ?? "").Length > 60 || (r.FilePrefix ?? "").Any(char.IsControl)) e.Add("Invalid file name prefix.");
        Rng(e, "timeline.textLoopSeconds", p.Timeline.TextLoopSeconds, 0, 3600);

        if (p.Assets.Count > MaxAssets) e.Add("Too many assets.");
        var ids = new HashSet<string>();
        foreach (var a in p.Assets)
        {
            if (!IdPattern().IsMatch(a.Id ?? "") || !ids.Add(a.Id!)) e.Add($"Asset id '{a.Id}' is invalid or duplicated.");
            if (!AssetPath().IsMatch(a.Path ?? "") || a.Path.Contains("..")) e.Add($"Asset '{a.Id}' has an unsafe path.");
            if (a.Kind is not (AssetKinds.Image or AssetKinds.Audio)) e.Add($"Asset '{a.Id}' has an unknown kind.");
            if (a.Bytes < 0 || a.Bytes > 2_000_000_000L) e.Add($"Asset '{a.Id}' has an invalid size.");
            if ((a.Sha256 ?? "").Length != 64 || !a.Sha256!.All(Uri.IsHexDigit)) e.Add($"Asset '{a.Id}' has an invalid checksum.");
            if ((a.FileName ?? "").Length > 200 || (a.FileName ?? "").Any(char.IsControl)) e.Add($"Asset '{a.Id}' has an invalid file name.");
        }
        return e;
    }

    private static void Rng(List<string> e, string f, double v, double lo, double hi)
    {
        if (double.IsNaN(v) || double.IsInfinity(v) || v < lo || v > hi) e.Add($"{f} = {v} is outside {lo}..{hi}");
    }
}
