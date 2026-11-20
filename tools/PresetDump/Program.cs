using AudioDimension.Presets;

// Writes the 60 built-in presets as a .adpreset pack so other platforms (macOS) share the exact same data.
var path = args.Length > 0 ? args[0] : "builtin-presets.json";
File.WriteAllText(path, PresetValidator.Serialize(BuiltInPresets.All));
foreach (var g in BuiltInPresets.All.GroupBy(p => p.VisualizerId).OrderBy(g => g.Key))
    Console.WriteLine($"{g.Key}: {g.Count()}");
Console.WriteLine($"Total {BuiltInPresets.All.Count} -> {Path.GetFullPath(path)}");
