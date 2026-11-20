using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AudioDimension.Presets;
using Serilog;

namespace AudioDimension.Projects;

public sealed record ProjectLoadResult(ProjectFile Project, IReadOnlyDictionary<string, string> AssetPaths, IReadOnlyList<string> Warnings);

public sealed record ProjectSummary(string Id, string Name, DateTime ModifiedUtc, int AssetCount);

/// <summary>
/// Reads and writes .adproj packages: a zip containing project.json and assets/*. Opening is defensive (entry-name checks, size caps,
/// checksum verification) and saving is atomic (temp file then replace), so a bad file or a failed save never damages existing work.
/// </summary>
public static class ProjectPackage
{
    public const string Extension = ".adproj";
    public const string ManifestName = "project.json";
    public const int MaxEntries = 100;
    public const long MaxManifestBytes = 4 * 1024 * 1024;
    public const long MaxTotalBytes = 3L * 1024 * 1024 * 1024;

    public static string Sha256Hex(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>
    /// Writes the project. <paramref name="assetFiles"/> maps asset id to the file on disk (ids with no entry in <c>project.Assets</c> are added using
    /// <paramref name="meta"/>). The manifest's asset list is rebuilt from the files so it always matches what is inside the package.
    /// </summary>
    public static void Write(string path, ProjectFile project, IReadOnlyDictionary<string, string> assetFiles, IReadOnlyDictionary<string, (string Kind, string FileName)>? meta = null)
    {
        var p = project.Clone();
        p.ModifiedUtc = DateTime.UtcNow;
        p.Visual.Background.ImagePath = null;          // runtime location only: projects never store paths to user files
        p.Assets.Clear();
        var entries = new List<(ProjectAsset asset, string file)>();
        foreach (var (id, file) in assetFiles)
        {
            if (!File.Exists(file)) throw new ProjectException($"The asset file for '{id}' is missing, so the project was not saved: {file}");
            var fi = new FileInfo(file);
            (string Kind, string FileName) m = meta is not null && meta.TryGetValue(id, out var mm) ? mm : (AssetKinds.Image, fi.Name);
            string ext = Path.GetExtension(m.FileName).ToLowerInvariant(); if (ext.Length > 8 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.')) ext = "";
            var asset = new ProjectAsset { Id = id, Kind = m.Kind, Path = $"assets/{id}{ext}", FileName = Path.GetFileName(m.FileName), Sha256 = Sha256Hex(file), Bytes = fi.Length };
            p.Assets.Add(asset); entries.Add((asset, file));
        }
        var errors = ProjectValidator.Validate(p);
        if (errors.Count > 0) throw new ProjectException("The project could not be saved because it is not valid: " + string.Join(" ", errors.Take(4)));

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        try { Directory.CreateDirectory(dir!); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new ProjectException($"The folder '{dir}' cannot be used: {ex.Message}", ex); }
        string tmp = path + ".saving";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var man = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using (var s = man.Open()) JsonSerializer.Serialize(s, p, PresetJson.Options);
                foreach (var (asset, file) in entries)
                {
                    // images and audio are already compressed; storing them is faster and lossless
                    var e = zip.CreateEntry(asset.Path, CompressionLevel.NoCompression);
                    using var es = e.Open(); using var src = File.OpenRead(file); src.CopyTo(es);
                }
            }
            File.Move(tmp, path, overwrite: true);      // atomic replace: the previous file stays intact until this succeeds
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            throw new ProjectException(ex is IOException io && io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) ? "There is not enough disk space to save the project." : "The project could not be saved: " + ex.Message, ex);
        }
        catch { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } throw; }
        Log.Information("Project saved: {Path} ({Assets} assets)", path, p.Assets.Count);
    }

    private static ZipArchive OpenZip(string path)
    {
        if (!File.Exists(path)) throw new ProjectException("The project file could not be found. It may have been moved or deleted.");
        try { return ZipFile.OpenRead(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        { throw new ProjectException("This file is not a valid Audio Dimension project (it is damaged or not a project package).", ex); }
    }

    private static ProjectFile ReadManifest(ZipArchive zip)
    {
        if (zip.Entries.Count > MaxEntries) throw new ProjectException("This file contains too many entries to be a project.");
        long declared = 0;
        foreach (var e in zip.Entries)
        {
            string n = e.FullName;
            bool ok = n == ManifestName || (n.StartsWith("assets/", StringComparison.Ordinal) && !n.EndsWith('/'));
            if (!ok || n.Contains("..") || n.Contains('\\') || n.StartsWith('/') || n.Contains(':')) throw new ProjectException($"The project contains an unexpected or unsafe entry ('{n}') and was not opened.");
            declared += e.Length;
        }
        if (declared > MaxTotalBytes) throw new ProjectException("The project is larger than the allowed size and was not opened.");
        var entry = zip.GetEntry(ManifestName) ?? throw new ProjectException("This file is not a project: project.json is missing.");
        if (entry.Length > MaxManifestBytes) throw new ProjectException("project.json is too large.");
        ProjectFile? p;
        try
        {
            using var s = entry.Open();
            p = JsonSerializer.Deserialize<ProjectFile>(s, PresetJson.Options);
        }
        catch (JsonException ex) { throw new ProjectException("The project description is damaged: " + ex.Message, ex); }
        if (p is null) throw new ProjectException("The project description is empty.");
        var errors = ProjectValidator.Validate(p);
        if (errors.Count > 0) throw new ProjectException("The project is not valid and was not opened: " + string.Join(" ", errors.Take(4)));
        p.Visual.Background.ImagePath = null;          // a crafted file must not be able to point the renderer at a local path; it is resolved from the embedded asset instead
        return p;
    }

    /// <summary>Reads only the manifest (cheap; used for lists).</summary>
    public static ProjectSummary Peek(string path)
    {
        using var zip = OpenZip(path);
        var p = ReadManifest(zip);
        return new ProjectSummary(p.Id, p.Name, p.ModifiedUtc, p.Assets.Count);
    }

    /// <summary>
    /// Opens a project and extracts its assets into <paramref name="extractDir"/>. Assets that are missing or fail their checksum are skipped
    /// with a warning, so the project still opens (with the dependent feature falling back) instead of failing outright.
    /// </summary>
    public static ProjectLoadResult Read(string path, string extractDir)
    {
        using var zip = OpenZip(path);
        var p = ReadManifest(zip);
        Directory.CreateDirectory(extractDir);
        var paths = new Dictionary<string, string>(); var warnings = new List<string>();
        foreach (var a in p.Assets)
        {
            var entry = zip.GetEntry(a.Path);
            if (entry is null) { warnings.Add($"The file for '{a.FileName}' is missing from the project."); continue; }
            if (entry.Length != a.Bytes) { warnings.Add($"'{a.FileName}' is damaged (size mismatch) and was ignored."); continue; }
            string dest = Path.Combine(extractDir, Path.GetFileName(a.Path));
            try
            {
                using (var es = entry.Open()) using (var fs = File.Create(dest)) es.CopyTo(fs);
                if (!string.Equals(Sha256Hex(dest), a.Sha256, StringComparison.OrdinalIgnoreCase))
                { File.Delete(dest); warnings.Add($"'{a.FileName}' is damaged (checksum mismatch) and was ignored."); continue; }
                paths[a.Id] = dest;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { warnings.Add($"'{a.FileName}' could not be extracted: {ex.Message}"); }
        }
        return new ProjectLoadResult(p, paths, warnings);
    }
}
