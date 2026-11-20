using System.Diagnostics;
using AudioDimension.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioDimension.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    public override string Title => "Settings";
    public override string Glyph => "";

    public string[] Qualities { get; } = { "Low", "Medium", "High", "Ultra", "Custom" };
    public int[] SampleRates { get; } = { 44100, 48000, 96000 };
    public int[] FrameRates { get; } = { 24, 25, 30, 50, 60 };
    public int[] AaLevels { get; } = { 0, 2, 4, 8 };
    public string[] Formats { get; } = { "MP4 (H.264)", "MP4 (H.265/HEVC)", "WebM" };
    public string[] Resolutions { get; } = { "1280x720", "1920x1080", "2560x1440", "3840x2160" };
    public string[] Encoders { get; } = { "Auto", "Software", "NVENC", "QuickSync", "AMF" };
    public string[] Themes { get; } = { "Graphite", "Midnight" };
    public string[] Accents { get; } = { "#22D3A5", "#22C7E8", "#A78BFA", "#4C8DF6", "#F472B6" };

    [ObservableProperty] private AppSettings _draft = SettingsService.Clone(App.Settings.Current);
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _cacheInfo = "";

    public SettingsViewModel() => RefreshCache();

    private void RefreshCache() => CacheInfo = $"Cache + temp: {App.Paths.CacheSize() / 1048576.0:F1} MB";

    [RelayCommand]
    private void Save()
    {
        try
        {
            App.Settings.Save(SettingsService.Clone(Draft));
            Status = "Settings saved.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Saving settings failed");
            Status = "Could not save settings: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        Draft = new AppSettings();
        Status = "Defaults loaded - press Save to apply.";
    }

    [RelayCommand]
    private void SetAccent(string color)
    {
        Draft.AccentColor = color;
        Save();
    }

    [RelayCommand]
    private void ClearCache()
    {
        var freed = App.Paths.ClearCache();
        RefreshCache();
        Status = $"Freed {freed / 1048576.0:F1} MB.";
    }

    [RelayCommand]
    private void OpenDataFolder() => Process.Start(new ProcessStartInfo(App.Paths.Root) { UseShellExecute = true });
}
