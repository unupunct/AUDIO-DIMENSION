using System.Text.Json;
using AudioDimension.Audio.Analysis;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using AudioDimension.Presets;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Visualizers;
using Xunit;

namespace AudioDimension.Tests;

public class PresetCatalogTests
{
    private static readonly string[] RequiredNames =
    {
        "Neon Pulse", "Cyber Grid", "Digital Storm", "Synthwave Tunnel", "Electric Horizon", "Neon Reactor", "Cyber Rain", "Laser Geometry",
        "Cosmic Galaxy", "Black Hole", "Solar Explosion", "Nebula Drift", "Starfield Journey", "Quantum Particles", "Cosmic Rings", "Deep Space",
        "Crystal Sphere", "Fractal Motion", "Geometric Collapse", "Infinite Tunnel", "Rotating Cubes", "Sacred Geometry", "Morphing Polyhedrons", "Abstract Dimensions",
        "Particle Universe", "Fireflies", "Particle Storm", "Magnetic Particles", "Energy Explosion", "Particle Rain", "Floating Dust", "Particle Vortex",
        "Classic Wave", "Neon Wave", "Circular Wave", "Wave Mountain", "Liquid Spectrum", "Frequency Ocean", "Audio Ribbon", "Waveform Tunnel",
        "Minimal Spectrum", "Soft Pulse", "Ambient Rings", "Minimal Lines", "Floating Orbs", "Smooth Equalizer", "Glass Spectrum", "Elegant Frequencies",
        "Digital DNA", "Energy Core", "Magnetic Field", "Fluid Universe", "Plasma World", "Dimensional Portal", "Electric Web", "Organic Geometry",
        "Hyperdrive", "Holographic Core", "Fractal Universe", "Dimension Shift"
    };

    [Fact]
    public void ShipsAllSixtyRequestedPresetsInOrder()
    {
        var names = BuiltInPresets.All.Select(p => p.Name).ToArray();
        Assert.Equal(60, names.Length);
        Assert.Equal(RequiredNames, names);
    }

    [Fact]
    public void CategoriesMatchTheRequestedStructure()
    {
        var by = BuiltInPresets.All.GroupBy(p => p.Category).ToDictionary(g => g.Key, g => g.Count());
        foreach (var cat in new[] { PresetCategories.Neon, PresetCategories.Space, PresetCategories.Geometric, PresetCategories.Particles, PresetCategories.Waveforms, PresetCategories.Minimal })
            Assert.Equal(8, by[cat]);
        Assert.Equal(12, by[PresetCategories.Experimental]);
    }

    [Fact]
    public void IdsAreUniqueAndStable()
    {
        Assert.Equal(60, BuiltInPresets.All.Select(p => p.Id).Distinct().Count());
        Assert.All(BuiltInPresets.All, p => Assert.Matches("^[a-z0-9-]+$", p.Id));
    }

    [Fact]
    public void EveryBuiltInPresetIsValidAndComplete()
    {
        foreach (var p in BuiltInPresets.All)
        {
            var errors = PresetValidator.Validate(p);
            Assert.True(errors.Count == 0, $"{p.Name}: {string.Join("; ", errors)}");
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.True(p.Visual.Palette.Colors.Length >= 3, p.Name);
            Assert.NotEmpty(p.Customizable);
            Assert.NotNull(p.Audio);
        }
    }

    [Fact]
    public void PresetsAreBehaviourallyDistinctNotRecoloursOfEachOther()
    {
        // Two presets must differ in rendering implementation, variant, element count, mirroring, stroke weight or rotation - never colour alone.
        string Sig(PresetDefinition p) => $"{p.VisualizerId}|{p.Visual.Params.Variant}|{p.Visual.Params.Segments}|{p.Visual.Params.Mirror}|{p.Visual.Params.LineWidth}|{p.Visual.Params.Rotation}";
        var dupes = BuiltInPresets.All.GroupBy(Sig).Where(g => g.Count() > 1).Select(g => string.Join(" / ", g.Select(p => p.Name))).ToList();
        Assert.True(dupes.Count == 0, "Presets sharing behaviour: " + string.Join("; ", dupes));
        Assert.True(BuiltInPresets.All.Select(p => p.VisualizerId).Distinct().Count() >= 30);
    }

    [Fact]
    public void EveryBuiltInPresetHasItsOwnThumbnailImage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AudioDimension.App"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var thumbs = Path.Combine(dir!.FullName, "src", "AudioDimension.App", "Assets", "Thumbnails");
        var hashes = new HashSet<string>();
        foreach (var p in BuiltInPresets.All)
        {
            var file = Path.Combine(thumbs, p.Id + ".png");
            Assert.True(File.Exists(file), $"missing thumbnail for {p.Name}");
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.Length > 3000 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G', $"{p.Name}: not a real PNG");
            Assert.True(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))), $"{p.Name}: thumbnail duplicates another preset's picture");
        }
    }

    [Fact]
    public void EveryPresetUsesARegisteredVisualizer() =>
        Assert.All(BuiltInPresets.All, p => Assert.True(VisualizerRegistry.Exists(p.VisualizerId), p.Name));

    [Fact]
    public void ApplyingAPresetCopiesLookButKeepsQuality()
    {
        var p = BuiltInPresets.All.First(x => x.Name == "Hyperdrive");
        var cfg = new VisualConfig { Quality = QualitySettings.For(QualityLevel.Low) };
        p.ApplyTo(cfg);
        Assert.Equal(QualityLevel.Low, cfg.Quality.Level);
        Assert.Equal(p.Visual.Params.Variant, cfg.Params.Variant);
        Assert.Equal(p.Visual.Palette.Colors, cfg.Palette.Colors);
        cfg.Params.Speed = 3.3;                                  // editing the live config must not touch the preset
        Assert.NotEqual(3.3, p.Visual.Params.Speed);
    }

    [Fact]
    public void PresetAudioSettingsApplyToTheEngineParameters()
    {
        var a = new PresetAudio { Sensitivity = 1.7, Smoothing = 0.2, BassSensitivity = 1.4, TrebleSensitivity = 0.5, FrequencyTilt = 0.3, AutoGain = false, BandEnabled = new[] { true, false, true, true, true, true } };
        var prm = new AudioParams(); a.ApplyTo(prm);
        Assert.Equal(1.7, prm.Sensitivity); Assert.Equal(0.5, prm.TrebleSensitivity); Assert.False(prm.AutoGain); Assert.False(prm.BandEnabled[1]);
        Assert.Equal(1.4, PresetAudio.From(prm).BassSensitivity);
    }

    [Fact]
    public void JsonRoundTripPreservesEverything()
    {
        var p = BuiltInPresets.All.First(x => x.Name == "Solar Explosion");
        var res = PresetValidator.Parse(PresetValidator.Serialize(new[] { p }));
        Assert.True(res.Ok, string.Join(";", res.Errors));
        var q = res.Presets[0];
        Assert.Equal(JsonSerializer.Serialize(p, PresetJson.Options), JsonSerializer.Serialize(q, PresetJson.Options));
    }
}

public class PresetValidationTests
{
    private static string Json(Action<PresetDefinition> mutate)
    {
        var p = BuiltInPresets.All[0].Clone(); mutate(p);
        return JsonSerializer.Serialize(p, PresetJson.Options);
    }

    [Theory]
    [InlineData("unknown visualizer")]
    [InlineData("path traversal visualizer")]
    [InlineData("speed out of range")]
    [InlineData("bad colour")]
    [InlineData("single colour palette")]
    [InlineData("empty name")]
    [InlineData("control chars in name")]
    [InlineData("future schema")]
    [InlineData("NaN-like huge number")]
    [InlineData("unknown customizable option")]
    [InlineData("wrong band count")]
    public void RejectsInvalidPresets(string what)
    {
        var json = Json(p =>
        {
            switch (what)
            {
                case "unknown visualizer": p.VisualizerId = "does-not-exist"; break;
                case "path traversal visualizer": p.VisualizerId = "..\\..\\evil.dll"; break;
                case "speed out of range": p.Visual.Params.Speed = 99; break;
                case "bad colour": p.Visual.Palette.Colors = new[] { "#GGGGGG", "#FFFFFF" }; break;
                case "single colour palette": p.Visual.Palette.Colors = new[] { "#FFFFFF" }; break;
                case "empty name": p.Name = "  "; break;
                case "control chars in name": p.Name = "Bad\u0007Name"; break;
                case "future schema": p.SchemaVersion = 99; break;
                case "NaN-like huge number": p.Visual.Camera.Distance = 1e30; break;
                case "unknown customizable option": p.Customizable = new[] { "runScript" }; break;
                case "wrong band count": p.Audio.BandEnabled = new[] { true }; break;
            }
        });
        var res = PresetValidator.Parse(json);
        Assert.False(res.Ok);
        Assert.NotEmpty(res.Errors);
        Assert.Empty(res.Presets);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"presets\": 5}")]
    [InlineData("{\"presets\": []}")]
    public void RejectsMalformedFiles(string text) => Assert.False(PresetValidator.Parse(text).Ok);

    [Fact]
    public void RejectsOversizedFiles() => Assert.False(PresetValidator.Parse(new string(' ', PresetValidator.MaxFileBytes + 10) + "{}").Ok);

    [Fact]
    public void UnknownMembersAreIgnoredNeverExecuted()
    {
        var json = Json(p => { }).Replace("\"Id\"", "\"onLoad\": \"calc.exe\", \"scripts\": [\"x\"], \"Id\"");
        var res = PresetValidator.Parse(json);
        Assert.True(res.Ok, string.Join(";", res.Errors));
        Assert.Equal(BuiltInPresets.All[0].Name, res.Presets[0].Name);
    }

    [Fact]
    public void AcceptsABareSinglePresetAndAPack()
    {
        Assert.True(PresetValidator.Parse(Json(_ => { })).Ok);
        var pack = PresetValidator.Serialize(BuiltInPresets.All.Take(5));
        var res = PresetValidator.Parse(pack);
        Assert.True(res.Ok); Assert.Equal(5, res.Presets.Count);
    }

    [Fact]
    public void OneBadPresetInAPackRejectsTheWholePack()
    {
        var list = BuiltInPresets.All.Take(3).Select(p => p.Clone()).ToList(); list[1].Visual.Params.Speed = 50;
        Assert.False(PresetValidator.Parse(PresetValidator.Serialize(list)).Ok);
    }
}

public sealed class PresetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ad-presets-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly Database _db;

    public PresetStoreTests()
    {
        _paths = new AppPaths(_root); _paths.EnsureCreated();
        _db = new Database(_paths.DatabaseFile); _db.Migrate();
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private PresetStore NewStore() => new(_db, _paths);

    private static PresetDefinition Mine(string name = "Mine") =>
        PresetDefinition.FromConfig(new VisualConfig(), new PresetAudio(), "circular-spectrum", name, PresetCategories.Custom, "my test preset");

    [Fact]
    public void StartsWithSixtyBuiltIns() => Assert.Equal(60, NewStore().All.Count(p => !p.IsCustom));

    [Fact]
    public void SavingCustomPresetsPersistsAcrossRestarts()
    {
        var s = NewStore();
        var saved = s.SaveNew(Mine("Persist me"));
        Assert.True(saved.IsCustom); Assert.StartsWith("custom-", saved.Id);
        var s2 = NewStore();
        Assert.Contains(s2.All, p => p.Id == saved.Id && p.Name == "Persist me" && p.IsCustom);
    }

    [Fact]
    public void NamesAreMadeUnique()
    {
        var s = NewStore();
        var a = s.SaveNew(Mine("Twin")); var b = s.SaveNew(Mine("Twin")); var c = s.SaveNew(Mine("Neon Pulse"));
        Assert.Equal("Twin", a.Name); Assert.Equal("Twin (2)", b.Name); Assert.Equal("Neon Pulse (2)", c.Name);
    }

    [Fact]
    public void CannotModifyOrDeleteBuiltIns()
    {
        var s = NewStore();
        Assert.False(s.Delete("neon-pulse")); Assert.False(s.Rename("neon-pulse", "Hacked"));
        Assert.Throws<InvalidOperationException>(() => { var p = s.Find("neon-pulse")!.Clone(); p.IsCustom = true; s.Update(p); });
        Assert.Equal("Neon Pulse", s.Find("neon-pulse")!.Name);
    }

    [Fact]
    public void DuplicateRenameDelete()
    {
        var s = NewStore();
        var copy = s.Duplicate("fireflies")!;
        Assert.True(copy.IsCustom); Assert.Equal("Fireflies (copy)", copy.Name); Assert.Equal("fireflies", copy.VisualizerId);
        Assert.Equal(s.Find("fireflies")!.Visual.Params.Variant, copy.Visual.Params.Variant);
        Assert.True(s.Rename(copy.Id, "Night Lights")); Assert.Equal("Night Lights", s.Find(copy.Id)!.Name);
        Assert.True(s.Delete(copy.Id)); Assert.Null(s.Find(copy.Id));
        Assert.Equal(60, s.All.Count);
    }

    [Fact]
    public void DuplicatingABuiltInCopiesItsThumbnail()
    {
        var s = NewStore(); s.BuiltInThumbnailLoader = id => id == "hyperdrive" ? new byte[] { 1, 2, 3 } : null;
        var copy = s.Duplicate("hyperdrive")!;
        Assert.True(File.Exists(s.ThumbnailPath(copy.Id)));
        s.Delete(copy.Id);
        Assert.False(File.Exists(s.ThumbnailPath(copy.Id)));
    }

    [Fact]
    public void FavouritesPersistAndFilter()
    {
        var s = NewStore();
        s.SetFavorite("fireflies", true); s.SetFavorite("hyperdrive", true);
        var s2 = NewStore();
        Assert.True(s2.Find("fireflies")!.IsFavorite);
        Assert.Equal(2, PresetQuery.Apply(s2.All, new PresetFilter(FavoritesOnly: true)).Count);
        s2.SetFavorite("fireflies", false);
        Assert.Single(PresetQuery.Apply(NewStore().All, new PresetFilter(FavoritesOnly: true)));
    }

    [Fact]
    public void SearchFilterAndSort()
    {
        var s = NewStore();
        var neon = PresetQuery.Apply(s.All, new PresetFilter("neon"));
        Assert.Contains(neon, p => p.Name == "Neon Pulse"); Assert.Contains(neon, p => p.Name == "Neon Wave");
        Assert.Equal(8, PresetQuery.Apply(s.All, new PresetFilter(Category: PresetCategories.Space)).Count);
        Assert.Single(PresetQuery.Apply(s.All, new PresetFilter("black hole")));
        Assert.Empty(PresetQuery.Apply(s.All, new PresetFilter("zzzz-not-here")));

        var byName = PresetQuery.Apply(s.All, new PresetFilter(Sort: PresetSort.Name)).Select(p => p.Name).ToList();
        Assert.Equal(byName.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(), byName);
        s.SetFavorite("deep-space", true);
        Assert.Equal("Deep Space", PresetQuery.Apply(s.All, new PresetFilter(Sort: PresetSort.FavoritesFirst))[0].Name);
        var mine = s.SaveNew(Mine("Zed"));
        Assert.Equal(mine.Id, PresetQuery.Apply(s.All, new PresetFilter(Sort: PresetSort.Newest))[0].Id);
        Assert.Single(PresetQuery.Apply(s.All, new PresetFilter(CustomOnly: true)));
        Assert.Equal(PresetCategories.Neon, PresetQuery.Apply(s.All, new PresetFilter(Sort: PresetSort.Category))[0].Category);
    }

    [Fact]
    public void ExportThenImportRoundTrips()
    {
        var s = NewStore(); var a = s.SaveNew(Mine("Exported A")); var b = s.SaveNew(Mine("Exported B"));
        var file = Path.Combine(_root, "pack.adpreset");
        s.Export(new[] { a.Id, b.Id, "neon-pulse" }, file);
        var other = Path.Combine(_root, "second"); var paths2 = new AppPaths(other); paths2.EnsureCreated();
        var db2 = new Database(paths2.DatabaseFile); db2.Migrate();
        var s2 = new PresetStore(db2, paths2);
        var res = s2.Import(file);
        Assert.True(res.Ok, string.Join(";", res.Errors));
        Assert.Equal(3, res.Presets.Count);
        Assert.All(res.Presets, p => Assert.True(p.IsCustom));
        Assert.Contains(s2.All, p => p.IsCustom && p.Name == "Neon Pulse (2)" && p.VisualizerId == "circular-spectrum");
    }

    [Fact]
    public void ImportingInvalidFilesChangesNothing()
    {
        var s = NewStore(); int before = s.All.Count;
        var bad = Path.Combine(_root, "bad.adpreset");
        File.WriteAllText(bad, "{ \"visualizerId\": \"nope\", \"name\": \"x\" }");
        var res = s.Import(bad);
        Assert.False(res.Ok); Assert.NotEmpty(res.Errors);
        Assert.Equal(before, NewStore().All.Count);
        Assert.False(s.Import(Path.Combine(_root, "missing.adpreset")).Ok);
    }
}

[Collection("gl")]
public class PresetRenderTests
{
    private readonly GlFixture _gl;
    public PresetRenderTests(GlFixture gl) => _gl = gl;

    public static IEnumerable<object[]> Names() => BuiltInPresets.All.Select(p => new object[] { p.Id });

    private static long Lit(byte[] px) { long n = 0; for (int i = 0; i < px.Length; i += 4) if (px[i] + px[i + 1] + px[i + 2] > 45) n++; return n; }
    private static long Diff(byte[] a, byte[] b) { long d = 0; for (int i = 0; i < a.Length; i += 4) d += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]); return d; }

    [Theory]
    [MemberData(nameof(Names))]
    public void EveryPresetRendersAndReactsToAudio(string id)
    {
        Assert.True(_gl.Begin(), _gl.Unavailable);
        var preset = BuiltInPresets.All.First(p => p.Id == id);
        var r = _gl.Renderer!; using var tgt = new OffscreenTarget(480, 270);
        var viz = VisualizerRegistry.Create(preset.VisualizerId);
        var cfg = new VisualConfig(); preset.ApplyTo(cfg);
        var music = TestAudio.MusicLike(1.6);
        byte[] Run(IReadOnlyList<AnalysisFrame> frames)
        {
            viz.Reset(); r.ResetTime();
            foreach (var f in frames) r.Render(viz, cfg, f, 1.0 / 60, tgt.Fbo, tgt.Width, tgt.Height);
            return tgt.ReadPixels();
        }
        var quiet = Run(music.Select(_ => AnalysisFrame.Silent).ToList());
        var loud = Run(music);
        Assert.Null(r.LastError);
        Assert.True(Lit(loud) > 300, $"{preset.Name}: nearly blank");
        Assert.True(Diff(quiet, loud) > 40000, $"{preset.Name}: barely reacts to audio ({Diff(quiet, loud)})");
        var dir = Environment.GetEnvironmentVariable("AD_PRESET_DUMP_DIR");
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); PngWriter.Save(Path.Combine(dir, id + ".png"), loud, tgt.Width, tgt.Height); }
    }
}
