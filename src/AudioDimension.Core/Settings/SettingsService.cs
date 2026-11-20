using System.Text.Json;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using Serilog;

namespace AudioDimension.Core.Settings;

public sealed class SettingsService
{
    private const string Key = "app";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly Database _db;
    private readonly AppPaths _paths;

    public AppSettings Current { get; private set; } = new();
    public event Action<AppSettings>? Changed;

    public SettingsService(Database db, AppPaths paths)
    {
        _db = db;
        _paths = paths;
    }

    public void Load()
    {
        try
        {
            var raw = _db.GetSetting(Key);
            Current = raw is null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(raw, Json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Settings were corrupt; falling back to defaults");
            Current = new AppSettings();
        }
        ApplyStorage(Current);
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        _db.SetSetting(Key, JsonSerializer.Serialize(settings, Json));
        ApplyStorage(settings);
        Log.Information("Settings saved");
        Changed?.Invoke(settings);
    }

    public static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, Json), Json)!;

    private void ApplyStorage(AppSettings s)
    {
        string Or(string v, string d) => string.IsNullOrWhiteSpace(v) ? d : v;
        _paths.Projects = Or(s.ProjectDirectory, Path.Combine(_paths.Root, "Projects"));
        _paths.Presets = Or(s.PresetDirectory, Path.Combine(_paths.Root, "Presets"));
        _paths.Temp = Or(s.TempDirectory, Path.Combine(_paths.Root, "Temp"));
        _paths.Exports = Or(s.ExportFolder, Path.Combine(_paths.Root, "Exports"));
        foreach (var d in new[] { _paths.Projects, _paths.Presets, _paths.Temp, _paths.Exports })
        {
            try { Directory.CreateDirectory(d); }
            catch (Exception ex) { Log.Warning(ex, "Cannot create storage directory {Dir}", d); }
        }
    }
}
