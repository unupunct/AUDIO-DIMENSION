using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using AudioDimension.Core.Settings;
using Xunit;

namespace AudioDimension.Tests;

public class FoundationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ad-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (AppPaths, Database, SettingsService) Create()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var db = new Database(paths.DatabaseFile);
        db.Migrate();
        return (paths, db, new SettingsService(db, paths));
    }

    [Fact]
    public void PathsCreateExpectedLayout()
    {
        var (p, _, _) = Create();
        foreach (var d in new[] { p.Database, p.Projects, p.Presets, p.Templates, p.Exports, p.Cache, p.Logs, p.Temp })
            Assert.True(Directory.Exists(d), d);
    }

    [Fact]
    public void SettingsRoundTripThroughSqlite()
    {
        var (_, db, svc) = Create();
        svc.Load();
        var s = SettingsService.Clone(svc.Current);
        s.TargetFrameRate = 30; s.AccentColor = "#A78BFA";
        svc.Save(s);

        var svc2 = new SettingsService(db, new AppPaths(_root));
        svc2.Load();
        Assert.Equal(30, svc2.Current.TargetFrameRate);
        Assert.Equal("#A78BFA", svc2.Current.AccentColor);
    }

    [Fact]
    public void CorruptSettingsFallBackToDefaults()
    {
        var (_, db, svc) = Create();
        db.SetSetting("app", "{not json");
        svc.Load();
        Assert.Equal(new AppSettings().TargetFrameRate, svc.Current.TargetFrameRate);
    }

    [Fact]
    public void ClearCacheRemovesFiles()
    {
        var (p, _, _) = Create();
        File.WriteAllText(Path.Combine(p.Cache, "a.bin"), "hello");
        Assert.True(p.CacheSize() > 0);
        p.ClearCache();
        Assert.Equal(0, p.CacheSize());
    }
}
