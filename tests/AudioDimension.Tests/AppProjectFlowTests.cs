using System.Reflection;
using AppType = AudioDimension.App.App;
using AudioDimension.App.Services;
using AudioDimension.App.ViewModels;
using AudioDimension.Audio.Capture;
using AudioDimension.Core.Data;
using AudioDimension.Core.Paths;
using AudioDimension.Core.Settings;
using AudioDimension.Presets;
using AudioDimension.Projects;
using AudioDimension.Recording;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Text;
using Xunit;

namespace AudioDimension.Tests;

/// <summary>
/// Builds the app's real view models and services on an STA thread against a temporary data folder, so the whole project flow
/// (capture state, save, change, reopen, dirty tracking) is tested without any dialogs.
/// </summary>
internal static class AppHarness
{
    private static void SetStatic(string name, object value) =>
        typeof(AppType).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetSetMethod(true)!.Invoke(null, new[] { value });

    public static void Run(Action<MainViewModel, string> body)
    {
        Exception? error = null;
        var dir = Path.Combine(Path.GetTempPath(), "ad-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var t = new Thread(() =>
        {
            try
            {
                var paths = new AppPaths(Path.Combine(dir, "data")); paths.EnsureCreated();
                var db = new Database(paths.DatabaseFile); db.Migrate();
                var settings = new SettingsService(db, paths); settings.Load();
                SetStatic("Paths", paths); SetStatic("Db", db); SetStatic("Settings", settings);
                SetStatic("Presets", new PresetStore(db, paths)); SetStatic("Templates", new TemplateStore(paths));
                SetStatic("Recording", new RecordingSession(AppType.Audio, paths, db));
                SetStatic("Projects", new ProjectService(new ProjectStore(db, paths)));
                var main = new MainViewModel();
                body(main, dir);
                main.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start();
        if (!t.Join(TimeSpan.FromMinutes(3))) throw new TimeoutException("App harness hung");
        try { Directory.Delete(dir, true); } catch { }
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    public static (StudioViewModel studio, AudioViewModel audio, RecordingViewModel rec, TextEditorViewModel text) Parts(MainViewModel m) =>
        (m.Pages.OfType<StudioViewModel>().Single(), m.Pages.OfType<AudioViewModel>().Single(), m.Pages.OfType<RecordingViewModel>().Single(), m.Pages.OfType<TextEditorViewModel>().Single());

    public static string MakePng(string path, byte r, byte g, byte b)
    {
        var px = new byte[64 * 48 * 4]; for (int i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255; }
        PngWriter.Save(path, px, 64, 48); return path;
    }
}

[Collection("app")]
public class AppProjectFlowTests
{
    [Fact]
    public void WholeStudioStateSurvivesSaveAndReopen()
    {
        AppHarness.Run((main, dir) =>
        {
            var (studio, audio, rec, text) = AppHarness.Parts(main);
            studio.ApplyPreset(AppType.Presets.Find("black-hole")!);
            studio.Config.Params.Speed = 1.7; studio.Config.Params.Segments = 12;
            studio.Config.Palette.Colors = new[] { "#112233", "#445566", "#778899" };
            studio.Config.Camera.Yaw = 77; studio.Config.Effects.Bloom = false; studio.Config.Background.Mode = BackgroundMode.Gradient; studio.Config.Background.Color1 = "#123456";
            foreach (var l in TitleTemplates.All[1].Layers) studio.Config.TextLayers.Add(l.Clone());
            studio.TextLoopSeconds = 7;
            AppType.Audio.Params.Sensitivity = 2.2; AppType.Audio.Params.BassSensitivity = 0.4; AppType.Audio.Params.BandEnabled[1] = false; AppType.Audio.Params.InputGain = 1.5; AppType.Audio.Params.NoiseGateDb = -55;
            rec.SelectedFps = 50; rec.Quality = 61; rec.IncludeAudio = false; rec.FilePrefix = "Gig";
            AppType.Projects.SetBackgroundImage(AppHarness.MakePng(Path.Combine(dir, "bg.png"), 200, 40, 40));
            AppType.Projects.Rename("Integration show");

            var path = Path.Combine(dir, "integration.adproj");
            AppType.Projects.Save(path);
            Assert.False(AppType.Projects.IsDirty); Assert.Equal("Integration show", AppType.Projects.Title); Assert.Equal(path, AppType.Projects.CurrentPath);

            // scribble over everything
            studio.ApplyPreset(AppType.Presets.Find("hyperdrive")!); studio.Config.TextLayers.Clear(); studio.Config.Params.Speed = 0.3;
            AppType.Projects.ClearBackgroundImage(); rec.SelectedFps = 24; AppType.Audio.Params.Sensitivity = 0.5; AppType.Audio.Params.BandEnabled[1] = true;
            AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty);

            AppType.Projects.Open(path);
            Assert.False(AppType.Projects.IsDirty); Assert.Equal("Integration show", AppType.Projects.Current.Name); Assert.Empty(AppType.Projects.Warnings);
            Assert.Equal("black-hole", studio.CurrentPreset?.Id); Assert.Equal("vortex", studio.SelectedVisualizer.Id);
            Assert.Equal(1.7, studio.Config.Params.Speed); Assert.Equal(12, studio.Config.Params.Segments);
            Assert.Equal(new[] { "#112233", "#445566", "#778899" }, studio.Config.Palette.Colors);
            Assert.Equal(77, studio.Config.Camera.Yaw); Assert.False(studio.Config.Effects.Bloom);
            Assert.Equal(BackgroundMode.Image, studio.Config.Background.Mode);
            Assert.NotNull(studio.Config.Background.AssetId); Assert.True(File.Exists(studio.Config.Background.ImagePath));
            Assert.Equal(TitleTemplates.All[1].Layers.Count, studio.Config.TextLayers.Count);
            Assert.Equal(TitleTemplates.All[1].Layers[0].Text, studio.Config.TextLayers[0].Text);
            Assert.Equal(TitleTemplates.All[1].Layers[0].Animations.Count, studio.Config.TextLayers[0].Animations.Count);
            Assert.Equal(7, studio.TextLoopSeconds);
            Assert.Equal(2.2, AppType.Audio.Params.Sensitivity); Assert.Equal(0.4, AppType.Audio.Params.BassSensitivity); Assert.False(AppType.Audio.Params.BandEnabled[1]);
            Assert.Equal(1.5, AppType.Audio.Params.InputGain); Assert.Equal(-55, AppType.Audio.Params.NoiseGateDb);
            Assert.Equal(50, rec.SelectedFps); Assert.Equal(61, rec.Quality); Assert.False(rec.IncludeAudio); Assert.Equal("Gig", rec.FilePrefix);
            Assert.Equal(2, rec.ToProject().Fps / 25);
            Assert.Equal(EngineState.Stopped, AppType.Audio.State);       // opening a project never starts capturing
        });
    }

    [Fact]
    public void UnsavedChangesAreDetectedAndClearedBySaving()
    {
        AppHarness.Run((main, dir) =>
        {
            var (studio, _, rec, _) = AppHarness.Parts(main);
            var path = Path.Combine(dir, "dirty.adproj"); AppType.Projects.Save(path);
            AppType.Projects.RefreshDirty(); Assert.False(AppType.Projects.IsDirty);
            Assert.DoesNotContain("*", AppType.Projects.Title);
            studio.Config.Params.Scale = 1.9; AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty); Assert.EndsWith("*", AppType.Projects.Title);
            AppType.Projects.Save(path); Assert.False(AppType.Projects.IsDirty);
            studio.Config.TextLayers.Add(TextLayer.Create(TextKind.Watermark)); AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty);
            AppType.Projects.Save(path);
            studio.Config.TextLayers[0].Text = "edited"; AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty);
            AppType.Projects.Save(path);
            rec.SelectedFps = 60; AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty);
            AppType.Projects.Save(path);
            AppType.Projects.Rename("Renamed"); AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty);
            AppType.Audio.Params.Smoothing = 0.11; AppType.Projects.Save(path); AppType.Audio.Params.Smoothing = 0.22; AppType.Projects.RefreshDirty(); Assert.True(AppType.Projects.IsDirty);
        });
    }

    [Fact]
    public void NewProjectResetsToDefaultsAndAUnsavedName()
    {
        AppHarness.Run((main, dir) =>
        {
            var (studio, _, rec, _) = AppHarness.Parts(main);
            studio.ApplyPreset(AppType.Presets.Find("hyperdrive")!); studio.Config.TextLayers.Add(TextLayer.Create(TextKind.Credits)); rec.SelectedFps = 60;
            AppType.Projects.SetBackgroundImage(AppHarness.MakePng(Path.Combine(dir, "n.png"), 1, 2, 3));
            AppType.Projects.Rename("Old"); AppType.Projects.Save(Path.Combine(dir, "old.adproj"));
            AppType.Projects.New();
            Assert.Equal("Untitled project", AppType.Projects.Current.Name); Assert.Null(AppType.Projects.CurrentPath);
            Assert.Empty(studio.Config.TextLayers); Assert.Equal(BackgroundMode.Gradient, studio.Config.Background.Mode); Assert.Null(studio.Config.Background.AssetId);
            Assert.Equal(30, rec.SelectedFps); Assert.False(AppType.Projects.IsDirty); Assert.Empty(AppType.Projects.Workspace.Files);
        });
    }

    [Fact]
    public void MissingBackgroundPictureFallsBackWithAWarning()
    {
        AppHarness.Run((main, dir) =>
        {
            var (studio, _, _, _) = AppHarness.Parts(main);
            AppType.Projects.SetBackgroundImage(AppHarness.MakePng(Path.Combine(dir, "gone.png"), 9, 9, 9));
            var path = Path.Combine(dir, "gone.adproj"); AppType.Projects.Save(path);
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("assets/")).ToList()) e.Delete();
            AppType.Projects.Open(path);
            Assert.Equal(BackgroundMode.Gradient, studio.Config.Background.Mode); Assert.Null(studio.Config.Background.ImagePath);
            Assert.Contains(AppType.Projects.Warnings, w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void UnknownAudioDevicesFallBackToTheDefaultWithANote()
    {
        AppHarness.Run((main, dir) =>
        {
            var (_, audio, _, _) = AppHarness.Parts(main);
            var p = AppType.Projects.Capture(); p.Audio.Source = "SystemAudio"; p.Audio.DeviceId = "{9.9.9}.{not-here}"; p.Audio.DeviceName = "Studio Monitors (unplugged)";
            var path = Path.Combine(dir, "dev.adproj"); ProjectPackage.Write(path, p, new Dictionary<string, string>());
            var before = AppType.Settings.Current.DefaultSystemDeviceId;
            AppType.Projects.Open(path);
            Assert.Contains(AppType.Projects.Warnings, w => w.Contains("Studio Monitors"));
            Assert.Equal(audio.Sources[1], audio.SelectedSource);
            Assert.Equal(before, AppType.Settings.Current.DefaultSystemDeviceId);      // opening a project does not rewrite the user's default device
            Assert.Equal(EngineState.Stopped, AppType.Audio.State);
        });
    }

    [Fact]
    public void RecordingCanTravelInsideTheProject()
    {
        AppHarness.Run((main, dir) =>
        {
            var wav = Media.MakeWav(Path.Combine(dir, "take.wav"), (1.2, 0.4));
            AppType.Recording.UseExternalAudio(wav, 1.2);
            AppType.Projects.IncludeRecording = true;
            var path = Path.Combine(dir, "withaudio.adproj"); AppType.Projects.Save(path);
            Assert.Contains(ProjectPackage.Peek(path).AssetCount.ToString(), "1");
            AppType.Recording.DiscardCapture(); Assert.False(AppType.Recording.HasCapture);
            AppType.Projects.New(); AppType.Projects.Open(path);
            Assert.True(AppType.Recording.HasCapture); Assert.True(AppType.Projects.IncludeRecording);
            Assert.InRange(AppType.Recording.CapturedSeconds, 1.1, 1.3);
            Assert.NotEqual(wav, AppType.Recording.CapturedAudioPath);        // it now points at the copy extracted from the project
            AppType.Projects.IncludeRecording = false; AppType.Projects.Save(path); Assert.Equal(0, ProjectPackage.Peek(path).AssetCount);
        });
    }

    [Fact]
    public void BackgroundPicturesAreValidatedBeforeTheyAreEmbedded()
    {
        AppHarness.Run((main, dir) =>
        {
            var fake = Path.Combine(dir, "fake.png"); File.WriteAllText(fake, "not an image");
            Assert.Contains("not a picture", Assert.Throws<ProjectException>(() => AppType.Projects.SetBackgroundImage(fake)).Message);
            Assert.Throws<ProjectException>(() => AppType.Projects.SetBackgroundImage(Path.Combine(dir, "missing.png")));
            Assert.Empty(AppType.Projects.Workspace.Files);
            var ok = AppHarness.MakePng(Path.Combine(dir, "ok.png"), 1, 1, 1);
            AppType.Projects.SetBackgroundImage(ok); Assert.Single(AppType.Projects.Workspace.Files);
            AppType.Projects.SetBackgroundImage(AppHarness.MakePng(Path.Combine(dir, "ok2.png"), 2, 2, 2));
            Assert.Single(AppType.Projects.Workspace.Files);                  // replacing a picture does not leave the old one behind
            AppType.Projects.ClearBackgroundImage(); Assert.Empty(AppType.Projects.Workspace.Files);
        });
    }

    [Fact]
    public void OpeningADamagedProjectLeavesTheCurrentWorkUntouched()
    {
        AppHarness.Run((main, dir) =>
        {
            var (studio, _, _, _) = AppHarness.Parts(main);
            studio.Config.Params.Speed = 2.5; AppType.Projects.RefreshDirty();
            var bad = Path.Combine(dir, "bad.adproj"); File.WriteAllText(bad, "garbage");
            Assert.Throws<ProjectException>(() => AppType.Projects.Open(bad));
            Assert.Equal(2.5, studio.Config.Params.Speed);
        });
    }

    [Fact]
    public void ProjectsAppearInTheRecentList()
    {
        AppHarness.Run((main, dir) =>
        {
            AppType.Projects.Rename("Listed"); var path = Path.Combine(dir, "listed.adproj"); AppType.Projects.Save(path);
            Assert.Contains(AppType.Projects.Store.Recent(), r => r.Name == "Listed" && r.Exists);
            AppType.Projects.Open(path);
            Assert.Equal(Path.GetFullPath(path), AppType.Projects.Store.Recent()[0].Path);
        });
    }
}

[CollectionDefinition("app", DisableParallelization = true)]
public class AppCollection { }
