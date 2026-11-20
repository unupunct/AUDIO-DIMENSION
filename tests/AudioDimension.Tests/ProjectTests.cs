using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AudioDimension.Audio.Analysis;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using AudioDimension.Presets;
using AudioDimension.Projects;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Text;
using Xunit;

namespace AudioDimension.Tests;

public sealed class ProjectFixture : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ad-proj-" + Guid.NewGuid().ToString("N"));
    public AppPaths Paths { get; }
    public Database Db { get; }
    public ProjectStore Store { get; }
    public ProjectFixture()
    {
        Directory.CreateDirectory(Dir);
        Paths = new AppPaths(Path.Combine(Dir, "data")); Paths.EnsureCreated();
        Db = new Database(Paths.DatabaseFile); Db.Migrate();
        Store = new ProjectStore(Db, Paths);
    }
    public string P(string name) => Path.Combine(Dir, name);
    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }

    /// <summary>A 64x48 solid-colour PNG made with the app's own encoder.</summary>
    public string MakePng(string name, byte r, byte g, byte b)
    {
        var px = new byte[64 * 48 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255; }
        var path = P(name); PngWriter.Save(path, px, 64, 48); return path;
    }

    public ProjectFile SampleProject(string name = "My Show")
    {
        var preset = BuiltInPresets.All.First(x => x.Name == "Cyber Grid").Clone();      // never share objects with the global catalogue
        var p = new ProjectFile { Name = name };
        p.Visual = new ProjectVisual
        {
            VisualizerId = preset.VisualizerId, PresetId = preset.Id, PresetName = preset.Name,
            Params = preset.Visual.Params, Palette = preset.Visual.Palette, Camera = preset.Visual.Camera, Background = preset.Visual.Background, Effects = preset.Visual.Effects,
            AspectName = "16:9"
        };
        foreach (var l in TitleTemplates.All[5].Layers) p.TextLayers.Add(l.Clone());
        p.Audio = new ProjectAudio { Source = "SystemAudio", DeviceId = "{0.0.1.00000000}.{abc}", DeviceName = "Speakers (Test)", InputGain = 1.4, NoiseGateDb = -60, Analysis = preset.Audio };
        p.Recording = new ProjectRecording { Format = "Mp4Hevc", Width = 2560, Height = 1440, Fps = 50, Quality = 82, Encoder = "Software", IncludeAudio = false, AutoRender = false, FilePrefix = "Show" };
        p.Timeline.TextLoopSeconds = 12;
        return p;
    }
}

public class ProjectFormatTests : IClassFixture<ProjectFixture>
{
    private readonly ProjectFixture _fx;
    public ProjectFormatTests(ProjectFixture fx) => _fx = fx;

    private static string Canon(ProjectFile p)
    {
        var c = p.Clone(); c.ModifiedUtc = default; c.Assets.Clear();
        return JsonSerializer.Serialize(c, PresetJson.Options);
    }

    [Fact]
    public void SaveThenOpenPreservesEverythingIncludingEmbeddedAssets()
    {
        var proj = _fx.SampleProject("Round trip");
        var img = _fx.MakePng("bg.png", 200, 30, 30);
        var audio = _fx.P("take.wav"); AudioDimension.Tests.Media.MakeWav(audio, (0.6, 0.4));
        var ws = _fx.Store.NewWorkspace();
        var imgId = ws.Add(AssetKinds.Image, img); var audId = ws.Add(AssetKinds.Audio, audio);
        proj.Visual.Background.Mode = BackgroundMode.Image; proj.Visual.Background.AssetId = imgId;

        var path = _fx.P("round.adproj");
        ProjectPackage.Write(path, proj, ws.Referenced(new[] { imgId, audId }), ws.Meta);
        Assert.True(File.Exists(path)); Assert.False(File.Exists(path + ".saving"));

        var loaded = ProjectPackage.Read(path, _fx.P("extract1"));
        Assert.Empty(loaded.Warnings);
        Assert.Equal(Canon(proj), Canon(loaded.Project));
        Assert.Equal(2, loaded.Project.Assets.Count);
        Assert.Equal(ProjectPackage.Sha256Hex(img), ProjectPackage.Sha256Hex(loaded.AssetPaths[imgId]));
        Assert.Equal(ProjectPackage.Sha256Hex(audio), ProjectPackage.Sha256Hex(loaded.AssetPaths[audId]));
        Assert.Equal("bg.png", loaded.Project.Assets.First(a => a.Id == imgId).FileName);
        // specific content checks the user cares about
        var lp = loaded.Project;
        Assert.Equal("cyber-grid", lp.Visual.VisualizerId); Assert.Equal("Cyber Grid", lp.Visual.PresetName);
        Assert.Equal(proj.Visual.Palette.Colors, lp.Visual.Palette.Colors);
        Assert.Equal(proj.TextLayers.Count, lp.TextLayers.Count); Assert.Equal(proj.TextLayers[0].Animations.Count, lp.TextLayers[0].Animations.Count);
        Assert.Equal(2560, lp.Recording.Width); Assert.Equal(50, lp.Recording.Fps); Assert.Equal("Speakers (Test)", lp.Audio.DeviceName); Assert.Equal(12, lp.Timeline.TextLoopSeconds);
    }

    [Fact]
    public void PackageFollowsTheDocumentedLayoutAndHasNoAbsolutePaths()
    {
        var proj = _fx.SampleProject("Layout");
        var ws = _fx.Store.NewWorkspace(); var id = ws.Add(AssetKinds.Image, _fx.MakePng("l.png", 1, 2, 3));
        proj.Visual.Background.Mode = BackgroundMode.Image; proj.Visual.Background.AssetId = id; proj.Visual.Background.ImagePath = @"C:\Users\someone\secret\pic.png";   // runtime path must not leak
        var path = _fx.P("layout.adproj"); ProjectPackage.Write(path, proj, ws.Referenced(new[] { id }), ws.Meta);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(new[] { $"assets/{id}.png", "project.json" }, zip.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray());
        string json = new StreamReader(zip.GetEntry("project.json")!.Open()).ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        foreach (var key in new[] { "formatVersion", "id", "name", "visual", "textLayers", "audio", "recording", "timeline", "assets" }) Assert.True(doc.RootElement.TryGetProperty(key, out _), key);
        Assert.Equal(1, doc.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.DoesNotMatch(@"[A-Za-z]:\\\\", json.Replace("\\\\", "\\\\"));     // no drive-letter paths anywhere
        Assert.DoesNotContain("secret", json);
    }

    [Fact]
    public void ACraftedImagePathInsideAProjectIsIgnoredOnOpen()
    {
        var path = _fx.P("crafted.adproj");
        using (var fs = File.Create(path)) using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var p = _fx.SampleProject("Crafted"); p.Visual.Background.Mode = BackgroundMode.Image; p.Visual.Background.ImagePath = @"C:\Windows\win.ini";
            using var w = new StreamWriter(z.CreateEntry("project.json").Open()); w.Write(JsonSerializer.Serialize(p, PresetJson.Options));
        }
        Assert.Null(ProjectPackage.Read(path, _fx.P("crafted-x")).Project.Visual.Background.ImagePath);
    }

    [Fact]
    public void MissingAssetEntryOpensWithAWarning()
    {
        var proj = _fx.SampleProject("Missing"); var ws = _fx.Store.NewWorkspace(); var id = ws.Add(AssetKinds.Image, _fx.MakePng("m.png", 5, 5, 5));
        proj.Visual.Background.Mode = BackgroundMode.Image; proj.Visual.Background.AssetId = id;
        var path = _fx.P("missing.adproj"); ProjectPackage.Write(path, proj, ws.Referenced(new[] { id }), ws.Meta);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) zip.GetEntry($"assets/{id}.png")!.Delete();
        var r = ProjectPackage.Read(path, _fx.P("extract2"));
        Assert.Single(r.Warnings); Assert.Contains("missing", r.Warnings[0]); Assert.Empty(r.AssetPaths);
        Assert.Equal(id, r.Project.Visual.Background.AssetId);       // app layer falls back to a gradient when the asset cannot be resolved
    }

    [Fact]
    public void TamperedAssetsAreDetected()
    {
        var proj = _fx.SampleProject("Tamper"); var ws = _fx.Store.NewWorkspace(); var id = ws.Add(AssetKinds.Image, _fx.MakePng("t.png", 9, 9, 9));
        var path = _fx.P("tamper.adproj"); ProjectPackage.Write(path, proj, ws.Referenced(new[] { id }), ws.Meta);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var e = zip.GetEntry($"assets/{id}.png")!; byte[] data; using (var s = e.Open()) { using var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
            e.Delete(); data[data.Length / 2] ^= 0xFF;
            var ne = zip.CreateEntry($"assets/{id}.png"); using var ws2 = ne.Open(); ws2.Write(data);
        }
        var r = ProjectPackage.Read(path, _fx.P("extract3"));
        Assert.Contains(r.Warnings, w => w.Contains("damaged")); Assert.Empty(r.AssetPaths);
    }

    private string Zip(string name, Action<ZipArchive> fill)
    {
        var path = _fx.P(name);
        using (var fs = File.Create(path)) using (var z = new ZipArchive(fs, ZipArchiveMode.Create)) fill(z);
        return path;
    }

    private static void Add(ZipArchive z, string entry, string text) { using var w = new StreamWriter(z.CreateEntry(entry).Open(), new UTF8Encoding(false)); w.Write(text); }

    private string ManifestJson(Action<ProjectFile>? mutate = null) { var p = _fx.SampleProject("Hostile"); mutate?.Invoke(p); return JsonSerializer.Serialize(p, PresetJson.Options); }

    [Theory]
    [InlineData("zip slip entry")] [InlineData("backslash entry")] [InlineData("absolute entry")] [InlineData("unexpected file")] [InlineData("too many entries")]
    [InlineData("no manifest")] [InlineData("invalid json")] [InlineData("future version")] [InlineData("bad value")] [InlineData("unknown visualizer")]
    [InlineData("asset path traversal")] [InlineData("duplicate asset ids")] [InlineData("oversized manifest")] [InlineData("not a zip")] [InlineData("empty file")] [InlineData("bad checksum field")]
    public void HostileOrDamagedPackagesAreRejectedWithReadableErrors(string what)
    {
        string path;
        switch (what)
        {
            case "zip slip entry": path = Zip("h1.adproj", z => { Add(z, "project.json", ManifestJson()); Add(z, "../evil.txt", "x"); }); break;
            case "backslash entry": path = Zip("h2.adproj", z => { Add(z, "project.json", ManifestJson()); Add(z, "assets\\evil.txt", "x"); }); break;
            case "absolute entry": path = Zip("h3.adproj", z => { Add(z, "project.json", ManifestJson()); Add(z, "/etc/passwd", "x"); }); break;
            case "unexpected file": path = Zip("h4.adproj", z => { Add(z, "project.json", ManifestJson()); Add(z, "run.exe", "MZ"); }); break;
            case "too many entries": path = Zip("h5.adproj", z => { Add(z, "project.json", ManifestJson()); for (int i = 0; i < 120; i++) Add(z, $"assets/f{i}.png", "x"); }); break;
            case "no manifest": path = Zip("h6.adproj", z => Add(z, "assets/a.png", "x")); break;
            case "invalid json": path = Zip("h7.adproj", z => Add(z, "project.json", "{ not json")); break;
            case "future version": path = Zip("h8.adproj", z => Add(z, "project.json", ManifestJson(p => p.FormatVersion = 99))); break;
            case "bad value": path = Zip("h9.adproj", z => Add(z, "project.json", ManifestJson(p => p.Visual.Params.Speed = 999))); break;
            case "unknown visualizer": path = Zip("h10.adproj", z => Add(z, "project.json", ManifestJson(p => p.Visual.VisualizerId = "..\\..\\evil"))); break;
            case "asset path traversal": path = Zip("h11.adproj", z => Add(z, "project.json", ManifestJson(p => p.Assets.Add(new ProjectAsset { Id = "aaaa1111", Kind = "Image", Path = "assets/../../x.png", FileName = "x", Sha256 = new string('a', 64), Bytes = 1 })))); break;
            case "duplicate asset ids": path = Zip("h12.adproj", z => Add(z, "project.json", ManifestJson(p => { for (int i = 0; i < 2; i++) p.Assets.Add(new ProjectAsset { Id = "same1234", Kind = "Image", Path = $"assets/s{i}.png", FileName = "x", Sha256 = new string('a', 64), Bytes = 1 }); }))); break;
            case "oversized manifest": path = Zip("h13.adproj", z => Add(z, "project.json", new string(' ', 5 * 1024 * 1024) + ManifestJson())); break;
            case "not a zip": path = _fx.P("h14.adproj"); File.WriteAllText(path, "this is plainly not a zip archive"); break;
            case "empty file": path = _fx.P("h15.adproj"); File.WriteAllBytes(path, Array.Empty<byte>()); break;
            default: path = Zip("h16.adproj", z => Add(z, "project.json", ManifestJson(p => p.Assets.Add(new ProjectAsset { Id = "bbbb2222", Kind = "Image", Path = "assets/b.png", FileName = "b", Sha256 = "zzzz", Bytes = 1 })))); break;
        }
        var ex = Assert.Throws<ProjectException>(() => ProjectPackage.Read(path, _fx.P("hx-" + what.Replace(' ', '_'))));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.False(File.Exists(_fx.P("evil.txt")) || File.Exists(Path.Combine(_fx.Dir, "..", "evil.txt")), "nothing may be written outside the extract folder");
    }

    [Fact]
    public void MissingProjectFilesAreReportedClearly() =>
        Assert.Contains("could not be found", Assert.Throws<ProjectException>(() => ProjectPackage.Read(_fx.P("nope.adproj"), _fx.P("x"))).Message);

    [Fact]
    public void FailedSavesLeaveTheExistingProjectUntouched()
    {
        var proj = _fx.SampleProject("Safe"); var path = _fx.P("safe.adproj");
        ProjectPackage.Write(path, proj, new Dictionary<string, string>());
        var before = ProjectPackage.Sha256Hex(path);
        Assert.Throws<ProjectException>(() => ProjectPackage.Write(path, proj, new Dictionary<string, string> { ["aaaaaaaa"] = _fx.P("does-not-exist.png") }));
        Assert.Equal(before, ProjectPackage.Sha256Hex(path)); Assert.False(File.Exists(path + ".saving"));
        var invalid = proj.Clone(); invalid.Recording.Fps = 9999;
        Assert.Throws<ProjectException>(() => ProjectPackage.Write(path, invalid, new Dictionary<string, string>()));
        Assert.Equal(before, ProjectPackage.Sha256Hex(path));
        Assert.Throws<ProjectException>(() => ProjectPackage.Write(Path.Combine(path, "sub", "x.adproj"), proj, new Dictionary<string, string>()));   // parent is a file
    }

    [Fact]
    public void PeekReadsTheSummaryWithoutExtracting()
    {
        var path = _fx.P("peek.adproj"); ProjectPackage.Write(path, _fx.SampleProject("Peeked"), new Dictionary<string, string>());
        var s = ProjectPackage.Peek(path); Assert.Equal("Peeked", s.Name); Assert.Equal(0, s.AssetCount);
    }

    [Fact]
    public void ValidatorChecksEverySection()
    {
        Assert.Empty(ProjectValidator.Validate(_fx.SampleProject()));
        var cases = new List<Action<ProjectFile>>
        {
            p => p.Name = "", p => p.Name = new string('x', 200), p => p.Id = "!!", p => p.Audio.Source = "Satellite", p => p.Audio.InputGain = 99, p => p.Recording.Format = "Avi",
            p => p.Recording.Width = 10, p => p.Recording.Fps = 0, p => p.Timeline.TextLoopSeconds = -5, p => p.Visual.Palette.Colors = new[] { "#12" },
            p => p.TextLayers.Add(new TextLayer { FontSize = 99999 }), p => p.Visual.Camera.Pitch = 400,
        };
        foreach (var c in cases) { var p = _fx.SampleProject(); c(p); Assert.NotEmpty(ProjectValidator.Validate(p)); }
        var img = _fx.SampleProject(); img.Visual.Background.Mode = BackgroundMode.Image; img.Visual.Background.AssetId = "abcd1234";
        Assert.Empty(ProjectValidator.Validate(img));                          // projects may use image backgrounds ...
        var asPreset = BuiltInPresets.All[0].Clone(); asPreset.Visual.Background.Mode = BackgroundMode.Image;
        Assert.NotEmpty(PresetValidator.Validate(asPreset));                   // ... presets may not
    }
}

public class ProjectStoreTests : IClassFixture<ProjectFixture>
{
    private readonly ProjectFixture _fx;
    public ProjectStoreTests(ProjectFixture fx) => _fx = fx;

    private string Save(string name, string? folder = null)
    {
        var p = _fx.SampleProject(name); var path = _fx.Store.UniquePath(name, folder ?? _fx.P("lib"));
        ProjectPackage.Write(path, p, new Dictionary<string, string>()); _fx.Store.RecordSaved(p, path); return path;
    }

    [Fact]
    public void UniquePathsNeverOverwrite()
    {
        var folder = _fx.P("uniq");
        var a = _fx.Store.UniquePath("Show", folder); File.WriteAllText(a, "x");
        var b = _fx.Store.UniquePath("Show", folder); File.WriteAllText(b, "x");
        Assert.NotEqual(a, b); Assert.EndsWith("Show (2).adproj", b);
        Assert.EndsWith("Bad_Name_.adproj", _fx.Store.UniquePath("Bad:Name?", folder));
    }

    [Fact]
    public void RecentProjectsAreOrderedCappedAndFlagMissingFiles()
    {
        _fx.Store.ClearRecent();
        var paths = Enumerable.Range(0, 18).Select(i => Save($"R{i}", _fx.P("recent"))).ToList();
        var recent = _fx.Store.Recent();
        Assert.Equal(15, recent.Count);
        Assert.Equal(Path.GetFullPath(paths[^1]), recent[0].Path);
        Assert.All(recent, r => Assert.True(r.Exists));
        File.Delete(paths[^1]);
        Assert.False(_fx.Store.Recent()[0].Exists);
        _fx.Store.RemoveRecent(paths[^1]); Assert.DoesNotContain(_fx.Store.Recent(), r => r.Path == Path.GetFullPath(paths[^1]));
        _fx.Store.RecordOpened(paths[3], "R3"); Assert.Equal(Path.GetFullPath(paths[3]), _fx.Store.Recent()[0].Path);
        _fx.Store.ClearRecent(); Assert.Empty(_fx.Store.Recent());
    }

    [Fact]
    public void SavingCataloguesTheProject()
    {
        var p = _fx.SampleProject("Catalogued"); var path = _fx.Store.UniquePath("Catalogued", _fx.P("cat"));
        ProjectPackage.Write(path, p, new Dictionary<string, string>()); _fx.Store.RecordSaved(p, path);
        using var c = _fx.Db.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM projects WHERE id=$i"; cmd.Parameters.AddWithValue("$i", p.Id);
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
        _fx.Store.RecordSaved(p, path);      // saving again updates rather than duplicates
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void DuplicateMakesAnIndependentCopyWithItsAssets()
    {
        var p = _fx.SampleProject("Original"); var ws = _fx.Store.NewWorkspace(); var id = ws.Add(AssetKinds.Image, _fx.MakePng("d.png", 10, 120, 10));
        p.Visual.Background.Mode = BackgroundMode.Image; p.Visual.Background.AssetId = id;
        var src = _fx.Store.UniquePath("Original", _fx.P("dup")); ProjectPackage.Write(src, p, ws.Referenced(new[] { id }), ws.Meta);
        var hash = ProjectPackage.Sha256Hex(src);
        var copyPath = _fx.Store.Duplicate(src, folder: _fx.P("dup"));
        var copy = ProjectPackage.Read(copyPath, _fx.P("dupx"));
        Assert.Equal("Original (copy)", copy.Project.Name); Assert.NotEqual(p.Id, copy.Project.Id);
        Assert.Single(copy.AssetPaths); Assert.Empty(copy.Warnings);
        Assert.Equal(hash, ProjectPackage.Sha256Hex(src));       // original untouched
        Assert.Equal(p.Visual.Palette.Colors, copy.Project.Visual.Palette.Colors);
    }

    [Fact]
    public void ImportValidatesThenStoresUnderANewId()
    {
        var outside = _fx.P("elsewhere"); Directory.CreateDirectory(outside);
        var p = _fx.SampleProject("Shared Show"); var file = Path.Combine(outside, "shared.adproj"); ProjectPackage.Write(file, p, new Dictionary<string, string>());
        var imported = _fx.Store.Import(file);
        Assert.StartsWith(Path.GetFullPath(_fx.Store.DefaultFolder), Path.GetFullPath(imported));
        var r = ProjectPackage.Read(imported, _fx.P("impx"));
        Assert.Equal("Shared Show", r.Project.Name); Assert.NotEqual(p.Id, r.Project.Id);

        var bad = Path.Combine(outside, "bad.adproj"); File.WriteAllText(bad, "junk");
        int before = Directory.GetFiles(_fx.Store.DefaultFolder, "*.adproj").Length;
        Assert.Throws<ProjectException>(() => _fx.Store.Import(bad));
        Assert.Equal(before, Directory.GetFiles(_fx.Store.DefaultFolder, "*.adproj").Length);
    }

    [Fact]
    public void AssetWorkspaceTracksOnlyWhatIsReferenced()
    {
        var ws = _fx.Store.NewWorkspace();
        var a = ws.Add(AssetKinds.Image, _fx.MakePng("w1.png", 1, 1, 1)); var b = ws.Add(AssetKinds.Image, _fx.MakePng("w2.png", 2, 2, 2));
        Assert.NotNull(ws.Resolve(a)); Assert.Null(ws.Resolve("nope")); Assert.Null(ws.Resolve(null));
        var refd = ws.Referenced(new string?[] { a, null, "ghost", a });
        Assert.Single(refd); Assert.True(refd.ContainsKey(a));
        ws.Remove(b); Assert.Null(ws.Resolve(b));
        Assert.Throws<ProjectException>(() => ws.Add(AssetKinds.Image, _fx.P("absent.png")));
        File.Delete(ws.Files[a]); Assert.Null(ws.Resolve(a));          // a vanished file is treated as missing, not as an error
        _fx.Store.CleanWork(); Assert.False(Directory.Exists(ws.Directory));
    }
}

[Collection("gl")]
public sealed class ImageBackgroundTests : IClassFixture<ProjectFixture>
{
    private readonly GlFixture _gl; private readonly ProjectFixture _fx;
    public ImageBackgroundTests(GlFixture gl, ProjectFixture fx) { _gl = gl; _fx = fx; }

    private (int r, int g, int b) Mean(VisualConfig cfg, out string? error)
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var rend = _gl.Renderer!; using var tgt = new OffscreenTarget(192, 108);
        cfg.Effects.Bloom = false; cfg.Effects.Vignette = false;
        rend.Render(new AudioDimension.Visuals.Visualizers.Oscilloscope(), cfg, AnalysisFrame.Silent, 0.016, tgt.Fbo, 192, 108);
        error = rend.LastError;
        var px = tgt.ReadPixels(); long r = 0, g = 0, b = 0; int n = px.Length / 4;
        for (int i = 0; i < px.Length; i += 4) { r += px[i]; g += px[i + 1]; b += px[i + 2]; }
        return ((int)(r / n), (int)(g / n), (int)(b / n));
    }

    private VisualConfig WithImage(string? path, double dim = 0)
    {
        var c = new VisualConfig(); c.Background.Mode = BackgroundMode.Image; c.Background.ImagePath = path; c.Background.ImageDim = dim; return c;
    }

    [Fact]
    public void ImageBackgroundShowsThePicture()
    {
        var red = Mean(WithImage(_fx.MakePng("red.png", 220, 20, 20)), out var err);
        Assert.Null(err); Assert.True(red.r > 150 && red.g < 60 && red.b < 60, $"expected red, got {red}");
        var blue = Mean(WithImage(_fx.MakePng("blue.png", 20, 20, 220)), out _);
        Assert.True(blue.b > 150 && blue.r < 60, $"expected blue, got {blue}");
    }

    [Fact]
    public void DimDarkensTheImage()
    {
        var png = _fx.MakePng("dim.png", 200, 200, 200);
        var full = Mean(WithImage(png, 0), out _); var dimmed = Mean(WithImage(png, 0.7), out _);
        Assert.True(dimmed.r < full.r * 0.45, $"{dimmed} vs {full}");
    }

    [Fact]
    public void MissingOrBrokenImagesFallBackInsteadOfCrashing()
    {
        var gone = Mean(WithImage(_fx.P("vanished.png")), out var e1);
        Assert.NotNull(e1); Assert.Contains("missing", e1!, StringComparison.OrdinalIgnoreCase);
        Assert.True(gone.r + gone.g + gone.b > 6, "falls back to the default gradient, not a blank frame");
        var notImage = _fx.P("fake.png"); File.WriteAllText(notImage, "definitely not an image");
        Mean(WithImage(notImage), out var e2); Assert.NotNull(e2);
        Mean(WithImage(null), out var e3); Assert.NotNull(e3);
        // and a good image works again afterwards on the same renderer
        Mean(WithImage(_fx.MakePng("again.png", 90, 90, 90)), out var e4); Assert.Null(e4);
    }

    [Fact]
    public void ImagePathSurvivesConfigClonesUsedForExport()
    {
        var png = _fx.MakePng("clone.png", 220, 20, 20);
        var clone = WithImage(png).Clone();
        Assert.Equal(png, clone.Background.ImagePath);
        var m = Mean(clone, out _); Assert.True(m.r > 150);
    }
}
