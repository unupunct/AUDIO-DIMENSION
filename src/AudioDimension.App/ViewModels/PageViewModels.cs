using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioDimension.App.ViewModels;

public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Glyph { get; }
    /// <summary>Content for the right-hand inspector panel while this page is shown (null = default hint).</summary>
    public virtual object? InspectorViewModel => null;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Honest empty state for sections whose engine is built in a later development stage.</summary>
public sealed class PendingPageViewModel : PageViewModel
{
    public PendingPageViewModel(string title, string glyph, string stage, string summary)
    { Title = title; Glyph = glyph; Stage = stage; Summary = summary; }
    public override string Title { get; }
    public override string Glyph { get; }
    public string Stage { get; }
    public string Summary { get; }
}

public sealed class DashboardViewModel : PageViewModel
{
    public DashboardViewModel() => PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IsSelected) && IsSelected) OnPropertyChanged(string.Empty); };

    public override string Title => "Dashboard";
    public override string Glyph => "";
    public string DataFolder => App.Paths.Root;
    public string LogFolder => App.Paths.Logs;
    public string DatabaseFile => App.Paths.DatabaseFile;
    public int ProjectCount => App.Db.Count("projects");
    public int CustomPresetCount => App.Db.Count("presets");
    public int ExportCount => App.Db.Count("export_history");
    public string Runtime => $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}  ·  Windows {Environment.OSVersion.Version}";
}

public sealed class AboutViewModel : PageViewModel
{
    public override string Title => "About";
    public override string Glyph => "";
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public string Privacy => "Audio Dimension runs entirely on your computer. No telemetry, no accounts, no uploads. " +
        "Audio is only captured while you have an active preview or recording session.";
}
