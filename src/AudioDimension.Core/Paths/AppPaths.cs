namespace AudioDimension.Core.Paths;

/// <summary>Resolves the per-user data layout under %LOCALAPPDATA%\AudioDimension.</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string Database => Path.Combine(Root, "Database");
    public string Projects { get; set; }
    public string Presets { get; set; }
    public string Templates => Path.Combine(Root, "Templates");
    public string Exports { get; set; }
    public string Cache => Path.Combine(Root, "Cache");
    public string Logs => Path.Combine(Root, "Logs");
    public string Temp { get; set; }
    public string DatabaseFile => Path.Combine(Database, "audiodimension.db");

    public AppPaths(string? root = null)
    {
        // AUDIODIMENSION_DATA_DIR lets tests and portable setups relocate all user data.
        var overrideDir = Environment.GetEnvironmentVariable("AUDIODIMENSION_DATA_DIR");
        Root = root ?? (!string.IsNullOrWhiteSpace(overrideDir) ? overrideDir : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioDimension"));
        Projects = Path.Combine(Root, "Projects");
        Presets = Path.Combine(Root, "Presets");
        Exports = Path.Combine(Root, "Exports");
        Temp = Path.Combine(Root, "Temp");
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, Database, Projects, Presets, Templates, Exports, Cache, Logs, Temp })
            Directory.CreateDirectory(dir);
    }

    /// <summary>Deletes everything in the cache and temp folders; returns bytes freed.</summary>
    public long ClearCache()
    {
        long freed = 0;
        foreach (var dir in new[] { Cache, Temp })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { var len = f.Length; f.Delete(); freed += len; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return freed;
    }

    public long CacheSize()
    {
        long total = 0;
        foreach (var dir in new[] { Cache, Temp })
            if (Directory.Exists(dir))
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                    total += f.Length;
        return total;
    }
}
