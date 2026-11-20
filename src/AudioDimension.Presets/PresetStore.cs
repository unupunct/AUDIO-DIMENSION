using System.Text.Json;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using Serilog;

namespace AudioDimension.Presets;

public enum PresetSort { Category, Name, FavoritesFirst, Newest, Visualizer }

public sealed record PresetFilter(string Search = "", string? Category = null, bool FavoritesOnly = false, bool CustomOnly = false, PresetSort Sort = PresetSort.Category);

public static class PresetQuery
{
    /// <summary>Search by name (and description/tags), filter by category/favorites/custom, then sort.</summary>
    public static IReadOnlyList<PresetDefinition> Apply(IEnumerable<PresetDefinition> all, PresetFilter f)
    {
        var tokens = (f.Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<PresetDefinition> q = all;
        if (!string.IsNullOrEmpty(f.Category)) q = q.Where(p => p.Category.Equals(f.Category, StringComparison.OrdinalIgnoreCase));
        if (f.FavoritesOnly) q = q.Where(p => p.IsFavorite);
        if (f.CustomOnly) q = q.Where(p => p.IsCustom);
        foreach (var t in tokens)
        {
            var tok = t;
            q = q.Where(p => p.Name.Contains(tok, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(tok, StringComparison.OrdinalIgnoreCase)
                             || p.Category.Contains(tok, StringComparison.OrdinalIgnoreCase) || p.VisualizerId.Contains(tok, StringComparison.OrdinalIgnoreCase)
                             || p.Tags.Any(x => x.Contains(tok, StringComparison.OrdinalIgnoreCase)));
        }
        int CatRank(PresetDefinition p) { int i = Array.IndexOf(PresetCategories.BuiltIn, p.Category); return i < 0 ? 99 : i; }
        return (f.Sort switch
        {
            PresetSort.Name => q.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            PresetSort.FavoritesFirst => q.OrderByDescending(p => p.IsFavorite).ThenBy(CatRank).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            PresetSort.Newest => q.OrderByDescending(p => p.IsCustom).ThenByDescending(p => p.CreatedUtc).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            PresetSort.Visualizer => q.OrderBy(p => p.VisualizerId).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderBy(CatRank).ThenBy(p => p.Category).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        }).ToList();
    }
}

/// <summary>Built-in presets plus user presets and favourites persisted in SQLite.</summary>
public sealed class PresetStore
{
    private readonly Database _db;
    private readonly AppPaths _paths;
    private List<PresetDefinition> _all = new();

    public IReadOnlyList<PresetDefinition> All => _all;
    public event Action? Changed;
    /// <summary>Lets the host supply embedded thumbnails so duplicating a built-in copies its picture.</summary>
    public Func<string, byte[]?>? BuiltInThumbnailLoader { get; set; }

    public PresetStore(Database db, AppPaths paths) { _db = db; _paths = paths; Reload(); }

    public string ThumbnailDirectory => Path.Combine(_paths.Presets, "Thumbs");
    public string ThumbnailPath(string id) => Path.Combine(ThumbnailDirectory, id + ".png");
    public void RaiseChanged() => Changed?.Invoke();
    public PresetDefinition? Find(string id) => _all.FirstOrDefault(p => p.Id == id);

    public void Reload()
    {
        var list = BuiltInPresets.All.Select(p => { var c = p.Clone(); c.IsCustom = false; return c; }).ToList();
        var favs = new HashSet<string>();
        using (var c = _db.Open())
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT preset_id FROM favorites";
                using var r = cmd.ExecuteReader();
                while (r.Read()) favs.Add(r.GetString(0));
            }
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT id, json, created_utc FROM presets WHERE is_custom=1";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    try
                    {
                        var res = PresetValidator.Parse(r.GetString(1));
                        if (!res.Ok) { Log.Warning("Skipping invalid stored preset {Id}: {Err}", r.GetString(0), string.Join("; ", res.Errors)); continue; }
                        var p = res.Presets[0]; p.Id = r.GetString(0); p.IsCustom = true;
                        if (DateTime.TryParse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) p.CreatedUtc = dt;
                        list.Add(p);
                    }
                    catch (Exception ex) { Log.Warning(ex, "Could not read a stored preset"); }
                }
            }
        }
        foreach (var p in list) p.IsFavorite = favs.Contains(p.Id);
        _all = list;
        Changed?.Invoke();
    }

    public string UniqueName(string name, string? ignoreId = null)
    {
        name = name.Trim(); if (name.Length > 80) name = name[..80];
        string candidate = name; int n = 2;
        while (_all.Any(p => p.Id != ignoreId && p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) candidate = $"{name} ({n++})";
        return candidate;
    }

    private void Write(PresetDefinition p, bool insert)
    {
        var errors = PresetValidator.Validate(p);
        if (errors.Count > 0) throw new InvalidDataException("Preset is invalid: " + string.Join("; ", errors));
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = insert
            ? "INSERT INTO presets(id,name,category,is_custom,json,created_utc) VALUES($id,$n,$c,1,$j,$t)"
            : "UPDATE presets SET name=$n, category=$c, json=$j WHERE id=$id AND is_custom=1";
        cmd.Parameters.AddWithValue("$id", p.Id); cmd.Parameters.AddWithValue("$n", p.Name); cmd.Parameters.AddWithValue("$c", p.Category);
        cmd.Parameters.AddWithValue("$j", PresetValidator.Serialize(new[] { p }));
        cmd.Parameters.AddWithValue("$t", p.CreatedUtc.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stores a new custom preset (new id, unique name). Built-ins are never overwritten.</summary>
    public PresetDefinition SaveNew(PresetDefinition source)
    {
        var p = source.Clone();
        p.Id = "custom-" + Guid.NewGuid().ToString("N")[..10]; p.IsCustom = true; p.CreatedUtc = DateTime.UtcNow;
        p.Name = UniqueName(string.IsNullOrWhiteSpace(p.Name) ? "My Preset" : p.Name);
        if (string.IsNullOrWhiteSpace(p.Category)) p.Category = PresetCategories.Custom;
        Write(p, insert: true);
        Reload();
        return Find(p.Id)!;
    }

    public void Update(PresetDefinition p)
    {
        if (!p.IsCustom || Find(p.Id) is not { IsCustom: true }) throw new InvalidOperationException("Only custom presets can be changed.");
        p.Name = UniqueName(p.Name, p.Id);
        Write(p, insert: false); Reload();
    }

    public bool Rename(string id, string newName)
    {
        var p = Find(id);
        if (p is null || !p.IsCustom || string.IsNullOrWhiteSpace(newName)) return false;
        var c = p.Clone(); c.IsCustom = true; c.Name = UniqueName(newName, id);
        Write(c, insert: false); Reload(); return true;
    }

    public bool Delete(string id)
    {
        var p = Find(id);
        if (p is null || !p.IsCustom) return false;
        using (var c = _db.Open())
        {
            foreach (var sql in new[] { "DELETE FROM presets WHERE id=$id AND is_custom=1", "DELETE FROM favorites WHERE preset_id=$id" })
            { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
        }
        try { if (File.Exists(ThumbnailPath(id))) File.Delete(ThumbnailPath(id)); } catch (IOException ex) { Log.Debug(ex, "Thumbnail delete failed"); }
        Reload(); return true;
    }

    public PresetDefinition? Duplicate(string id)
    {
        var p = Find(id); if (p is null) return null;
        var c = p.Clone(); c.Name = p.Name + " (copy)"; c.Author = Environment.UserName;
        var saved = SaveNew(c);
        try
        {
            Directory.CreateDirectory(ThumbnailDirectory);
            if (p.IsCustom && File.Exists(ThumbnailPath(p.Id))) File.Copy(ThumbnailPath(p.Id), ThumbnailPath(saved.Id), true);
            else if (!p.IsCustom && BuiltInThumbnailLoader?.Invoke(p.Id) is { } bytes) File.WriteAllBytes(ThumbnailPath(saved.Id), bytes);
        }
        catch (IOException ex) { Log.Warning(ex, "Could not copy thumbnail"); }
        return saved;
    }

    public ImportResult Import(string path)
    {
        string json;
        try
        {
            if (new FileInfo(path).Length > PresetValidator.MaxFileBytes) return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "The file is too large to be a preset." });
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new ImportResult(Array.Empty<PresetDefinition>(), new[] { "Could not read the file: " + ex.Message }); }
        var res = PresetValidator.Parse(json);
        if (!res.Ok) return res;
        var saved = res.Presets.Select(p => { p.Author = string.IsNullOrWhiteSpace(p.Author) ? "Imported" : p.Author; return SaveNew(p); }).ToList();
        return new ImportResult(saved, Array.Empty<string>());
    }

    public void Export(IEnumerable<string> ids, string path)
    {
        var list = ids.Select(Find).Where(p => p is not null).Select(p => p!).ToList();
        if (list.Count == 0) throw new InvalidOperationException("Nothing to export.");
        File.WriteAllText(path, PresetValidator.Serialize(list));
    }

    public void SetFavorite(string id, bool favorite)
    {
        using (var c = _db.Open())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = favorite ? "INSERT OR IGNORE INTO favorites(preset_id) VALUES($id)" : "DELETE FROM favorites WHERE preset_id=$id";
            cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
        }
        var p = Find(id); if (p is not null) p.IsFavorite = favorite;
        Changed?.Invoke();
    }
}
