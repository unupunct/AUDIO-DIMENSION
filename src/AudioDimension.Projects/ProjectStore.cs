using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using Serilog;

namespace AudioDimension.Projects;

public sealed record RecentProject(string Path, string Name, DateTime OpenedUtc, bool Exists);

/// <summary>Holds the live project's embedded assets in a working folder (extracted on open, added on import, written back on save).</summary>
public sealed class AssetWorkspace
{
    private readonly Dictionary<string, string> _files = new();
    private readonly Dictionary<string, (string Kind, string FileName)> _meta = new();
    public string Directory { get; }

    public AssetWorkspace(string directory) { Directory = directory; System.IO.Directory.CreateDirectory(directory); }

    public IReadOnlyDictionary<string, string> Files => _files;
    public IReadOnlyDictionary<string, (string Kind, string FileName)> Meta => _meta;

    public string? Resolve(string? id) => id is not null && _files.TryGetValue(id, out var p) && File.Exists(p) ? p : null;

    /// <summary>Copies a user file into the workspace and returns its new asset id.</summary>
    public string Add(string kind, string sourceFile)
    {
        var fi = new FileInfo(sourceFile);
        if (!fi.Exists) throw new ProjectException("The file could not be found.");
        if (fi.Length > 1_500_000_000L) throw new ProjectException("That file is too large to embed in a project.");
        string id = "a" + Guid.NewGuid().ToString("N")[..12];
        string ext = System.IO.Path.GetExtension(sourceFile).ToLowerInvariant();
        if (ext.Length > 8 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.')) ext = "";
        string dest = System.IO.Path.Combine(Directory, id + ext);
        File.Copy(sourceFile, dest, overwrite: true);
        _files[id] = dest; _meta[id] = (kind, fi.Name);
        return id;
    }

    public void Load(IReadOnlyDictionary<string, string> extracted, IEnumerable<ProjectAsset> manifest)
    {
        Clear();
        foreach (var a in manifest) if (extracted.TryGetValue(a.Id, out var f)) { _files[a.Id] = f; _meta[a.Id] = (a.Kind, a.FileName); }
    }

    public void Remove(string id) { _files.Remove(id); _meta.Remove(id); }

    public void Clear() { _files.Clear(); _meta.Clear(); }

    /// <summary>Only keep assets the project still refers to, so saved packages do not accumulate orphans.</summary>
    public IReadOnlyDictionary<string, string> Referenced(IEnumerable<string?> ids) =>
        ids.Where(i => i is not null && _files.ContainsKey(i!)).Distinct().ToDictionary(i => i!, i => _files[i!]);
}

/// <summary>Project catalogue (SQLite) plus file-level operations: unique names, recent list, duplicate, import.</summary>
public sealed class ProjectStore
{
    private readonly Database _db;
    private readonly AppPaths _paths;
    public ProjectStore(Database db, AppPaths paths) { _db = db; _paths = paths; }

    public string DefaultFolder => _paths.Projects;
    public string WorkRoot => System.IO.Path.Combine(_paths.Cache, "ProjectWork");

    public string UniquePath(string name, string? folder = null)
    {
        folder ??= DefaultFolder; Directory.CreateDirectory(folder);
        string safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "Project";
        string p = System.IO.Path.Combine(folder, safe + ProjectPackage.Extension); int n = 2;
        while (File.Exists(p)) p = System.IO.Path.Combine(folder, $"{safe} ({n++}){ProjectPackage.Extension}");
        return p;
    }

    public AssetWorkspace NewWorkspace()
    {
        CleanWork();
        return new AssetWorkspace(System.IO.Path.Combine(WorkRoot, Guid.NewGuid().ToString("N")[..10]));
    }

    /// <summary>Removes working folders of earlier sessions (extracted assets are re-created from the package on open).</summary>
    public void CleanWork()
    {
        try
        {
            if (!Directory.Exists(WorkRoot)) return;
            foreach (var d in Directory.EnumerateDirectories(WorkRoot))
                try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex) { Log.Debug(ex, "Project work cleanup skipped"); }
    }

    public void RecordSaved(ProjectFile p, string path)
    {
        using (var c = _db.Open())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO projects(id,name,path,created_utc,modified_utc) VALUES($i,$n,$p,$c,$m) ON CONFLICT(id) DO UPDATE SET name=$n, path=$p, modified_utc=$m";
            cmd.Parameters.AddWithValue("$i", p.Id); cmd.Parameters.AddWithValue("$n", p.Name); cmd.Parameters.AddWithValue("$p", path);
            cmd.Parameters.AddWithValue("$c", p.CreatedUtc.ToString("o")); cmd.Parameters.AddWithValue("$m", p.ModifiedUtc.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        RecordOpened(path, p.Name);
    }

    public void RecordOpened(string path, string name)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO recent_projects(path,name,opened_utc) VALUES($p,$n,$t) ON CONFLICT(path) DO UPDATE SET name=$n, opened_utc=$t";
        cmd.Parameters.AddWithValue("$p", System.IO.Path.GetFullPath(path)); cmd.Parameters.AddWithValue("$n", name); cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
        // keep the list to the 15 most recent
        using var trim = c.CreateCommand();
        trim.CommandText = "DELETE FROM recent_projects WHERE path NOT IN (SELECT path FROM recent_projects ORDER BY opened_utc DESC LIMIT 15)";
        trim.ExecuteNonQuery();
    }

    public IReadOnlyList<RecentProject> Recent()
    {
        var list = new List<RecentProject>();
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path,name,opened_utc FROM recent_projects ORDER BY opened_utc DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var path = r.GetString(0);
            list.Add(new RecentProject(path, r.GetString(1), DateTime.TryParse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.UtcNow, File.Exists(path)));
        }
        return list;
    }

    public void RemoveRecent(string path)
    {
        using var c = _db.Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM recent_projects WHERE path=$p"; cmd.Parameters.AddWithValue("$p", System.IO.Path.GetFullPath(path)); cmd.ExecuteNonQuery();
    }

    public void ClearRecent()
    {
        using var c = _db.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM recent_projects"; cmd.ExecuteNonQuery();
    }

    /// <summary>Copies a package under a new id and name (the original is untouched). Returns the new file path.</summary>
    public string Duplicate(string sourcePath, string? newName = null, string? folder = null)
    {
        string tmp = System.IO.Path.Combine(WorkRoot, "dup-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var loaded = ProjectPackage.Read(sourcePath, tmp);
            var p = loaded.Project;
            p.Id = Guid.NewGuid().ToString("N")[..12]; p.Name = newName ?? p.Name + " (copy)"; p.CreatedUtc = DateTime.UtcNow;
            var path = UniquePath(p.Name, folder);
            var meta = p.Assets.ToDictionary(a => a.Id, a => (a.Kind, a.FileName));
            ProjectPackage.Write(path, p, loaded.AssetPaths, meta);
            RecordSaved(p, path);
            return path;
        }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    /// <summary>Validates a package from anywhere and stores it in the projects folder with a fresh id. Throws <see cref="ProjectException"/> when invalid.</summary>
    public string Import(string sourcePath) => Duplicate(sourcePath, ProjectPackage.Peek(sourcePath).Name, null);
}
