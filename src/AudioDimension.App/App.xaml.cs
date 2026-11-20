using System.Windows;
using System.Windows.Media;
using AudioDimension.App.ViewModels;
using AudioDimension.Audio.Capture;
using AudioDimension.App.Views;
using AudioDimension.Core.Data;
using AudioDimension.Core.Logging;
using AudioDimension.Core.Paths;
using AudioDimension.Core.Settings;
using AudioDimension.Presets;
using AudioDimension.Recording;
using AudioDimension.Projects;
using Serilog;

namespace AudioDimension.App;

public partial class App : Application
{
    public static AppPaths Paths { get; private set; } = null!;
    public static Database Db { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static AudioEngine Audio { get; } = new();
    public static PresetStore Presets { get; private set; } = null!;
    public static TemplateStore Templates { get; private set; } = null!;
    public static RecordingSession Recording { get; private set; } = null!;
    public static Services.ProjectService Projects { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Paths = new AppPaths();
            Paths.EnsureCreated();
            LoggingSetup.Initialize(Paths.Logs);
            Log.Information("Audio Dimension starting");

            Db = new Database(Paths.DatabaseFile);
            Db.Migrate();
            Settings = new SettingsService(Db, Paths);
            Settings.Load();
            Settings.Changed += ApplyTheme;
            ApplyTheme(Settings.Current);
            Templates = new TemplateStore(Paths);
            Recording = new RecordingSession(Audio, Paths, Db);
            RecordingSession.CleanOldRecordings(Paths);
            Projects = new Services.ProjectService(new ProjectStore(Db, Paths));
            Presets = new PresetStore(Db, Paths) { BuiltInThumbnailLoader = Services.ThumbnailLoader.LoadBuiltInBytes };
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failure");
            MessageBox.Show($"Audio Dimension could not start.\n\n{ex.Message}\n\nDetails were written to the log folder.",
                "Audio Dimension", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception.Message}\n\nThe details were logged.",
                "Audio Dimension", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var win = new MainWindow { DataContext = new MainViewModel() };
        MainWindow = win;
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (MainWindow?.DataContext as MainViewModel)?.Shutdown();
        Services.PreviewWindows.CloseAll();
        Recording?.Dispose();
        Audio.Dispose(); // stop any active capture before exit
        Log.Information("Audio Dimension exiting");
        LoggingSetup.Shutdown();
        base.OnExit(e);
    }

    /// <summary>Applies theme + accent colour to the live resource dictionary.</summary>
    public static void ApplyTheme(AppSettings s)
    {
        var res = Current.Resources;
        var (bg, panel, card, border) = s.Theme == "Midnight"
            ? ("#0B0E1A", "#10142A", "#171C36", "#262C4D")
            : ("#14161A", "#1B1E24", "#23272F", "#2E333C");
        res["BgColor"] = (Color)ColorConverter.ConvertFromString(bg);
        res["PanelColor"] = (Color)ColorConverter.ConvertFromString(panel);
        res["CardColor"] = (Color)ColorConverter.ConvertFromString(card);
        res["BorderColorC"] = (Color)ColorConverter.ConvertFromString(border);
        try { res["AccentColor"] = (Color)ColorConverter.ConvertFromString(s.AccentColor); }
        catch (FormatException) { Log.Warning("Invalid accent colour {C}", s.AccentColor); }
    }
}
