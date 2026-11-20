using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AudioDimension.App.Services;
using AudioDimension.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;

namespace AudioDimension.App.ViewModels;

public sealed record AssetRow(string Name, string Kind, string Size);

public sealed partial class ProjectsViewModel : PageViewModel
{
    public override string Title => "Projects";
    public override string Glyph => "";

    private const string Filter = "Audio Dimension project (*.adproj)|*.adproj";
    private readonly ProjectService _svc;
    private bool _syncing;

    public ObservableCollection<RecentProject> Recent { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<AssetRow> Assets { get; } = new();

    [ObservableProperty] private string _projectName = "";
    [ObservableProperty] private string _pathText = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _modifiedText = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _hasPath;
    [ObservableProperty] private bool _hasRecording;
    [ObservableProperty] private bool _includeRecording;
    [ObservableProperty] private string _recordingInfo = "";
    [ObservableProperty] private bool _hasWarnings;

    public ProjectsViewModel(ProjectService svc)
    {
        _svc = svc;
        _svc.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Sync);
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IsSelected) && IsSelected) { RefreshRecent(); Sync(); } };
        Sync(); RefreshRecent();
    }

    partial void OnProjectNameChanged(string value) { if (!_syncing) _svc.Rename(value); }
    partial void OnIncludeRecordingChanged(bool value) { if (!_syncing) _svc.IncludeRecording = value; }

    private void Sync()
    {
        _syncing = true;
        try
        {
            ProjectName = _svc.Current.Name;
            IsDirty = _svc.IsDirty; HasPath = _svc.CurrentPath is not null;
            PathText = _svc.CurrentPath ?? "Not saved yet - press Save to choose where it goes.";
            ModifiedText = _svc.CurrentPath is null ? "" : $"Last saved {_svc.Current.ModifiedUtc.ToLocalTime():g}";
            HasRecording = App.Recording.HasCapture;
            IncludeRecording = _svc.IncludeRecording && HasRecording;
            RecordingInfo = HasRecording ? $"{App.Recording.CapturedSeconds:0.0} s, {App.Recording.CapturedBytes / 1048576.0:0.0} MB" : "No recording available";
            Warnings.Clear(); foreach (var w in _svc.Warnings) Warnings.Add(w); HasWarnings = Warnings.Count > 0;
            Assets.Clear();
            foreach (var (id, file) in _svc.Workspace.Files)
                if (File.Exists(file)) Assets.Add(new AssetRow(_svc.Workspace.Meta.TryGetValue(id, out var m) ? m.FileName : id, _svc.Workspace.Meta.TryGetValue(id, out var m2) ? m2.Kind : "", $"{new FileInfo(file).Length / 1024.0:N0} KB"));
        }
        finally { _syncing = false; }
    }

    private void RefreshRecent()
    {
        try { Recent.Clear(); foreach (var r in _svc.Store.Recent()) Recent.Add(r); }
        catch (Exception ex) { Log.Warning(ex, "Could not load recent projects"); }
    }

    /// <summary>Asks what to do with unsaved changes. Returns false if the user cancelled.</summary>
    public bool ConfirmDiscardChanges()
    {
        _svc.Rename(ProjectName);
        if (!_svc.IsDirty) return true;
        var r = MessageBox.Show($"Save changes to '{_svc.Current.Name}' before continuing?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        return r == MessageBoxResult.No || SaveInternal();
    }

    private bool SaveInternal(bool forceDialog = false)
    {
        string? path = _svc.CurrentPath;
        if (path is null || forceDialog)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save project", Filter = Filter, DefaultExt = ProjectPackage.Extension, AddExtension = true, InitialDirectory = _svc.Store.DefaultFolder,
                FileName = System.IO.Path.GetFileName(_svc.Store.UniquePath(_svc.Current.Name))
            };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try { _svc.Save(path); Status = $"Saved '{_svc.Current.Name}'."; RefreshRecent(); return true; }
        catch (ProjectException ex) { Status = ex.Message; MessageBox.Show(ex.Message, "Save project", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        catch (Exception ex) { Log.Error(ex, "Saving project failed"); Status = "The project could not be saved: " + ex.Message; return false; }
    }

    [RelayCommand] private void Save() => SaveInternal();
    [RelayCommand] private void SaveAs() => SaveInternal(forceDialog: true);

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmDiscardChanges()) return;
        _svc.New(); Status = "Started a new project."; RefreshRecent();
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new OpenFileDialog { Title = "Open project", Filter = Filter, InitialDirectory = _svc.Store.DefaultFolder };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
    }

    public void OpenPath(string path)
    {
        if (!ConfirmDiscardChanges()) return;
        try { _svc.Open(path); Status = $"Opened '{_svc.Current.Name}'." + (_svc.Warnings.Count > 0 ? " Some items need attention (see below)." : ""); }
        catch (ProjectException ex) { Status = ex.Message; MessageBox.Show(ex.Message, "Open project", MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (Exception ex) { Log.Error(ex, "Opening project failed"); Status = "The project could not be opened: " + ex.Message; }
        RefreshRecent();
    }

    [RelayCommand] private void OpenRecent(RecentProject? r) { if (r is not null) OpenPath(r.Path); }

    [RelayCommand] private void RemoveRecent(RecentProject? r) { if (r is not null) { _svc.Store.RemoveRecent(r.Path); RefreshRecent(); } }

    [RelayCommand] private void ClearRecent() { _svc.Store.ClearRecent(); RefreshRecent(); }

    [RelayCommand]
    private void Duplicate()
    {
        if (_svc.CurrentPath is null || _svc.IsDirty) { if (!SaveInternal()) return; }
        try { var copy = _svc.Store.Duplicate(_svc.CurrentPath!); Status = $"Created a copy: {System.IO.Path.GetFileName(copy)}"; RefreshRecent(); }
        catch (ProjectException ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void Import()
    {
        var dlg = new OpenFileDialog { Title = "Import project", Filter = Filter };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var path = _svc.Store.Import(dlg.FileName);
            RefreshRecent(); Status = $"Imported into your projects folder: {System.IO.Path.GetFileName(path)}";
            if (MessageBox.Show("Open the imported project now?", "Import project", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) OpenPath(path);
        }
        catch (ProjectException ex)
        {
            Status = "Import rejected: " + ex.Message;
            MessageBox.Show("That file could not be imported.\n\n" + ex.Message, "Import project", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ExportCopy()
    {
        var dlg = new SaveFileDialog { Title = "Export a copy of this project", Filter = Filter, DefaultExt = ProjectPackage.Extension, AddExtension = true, FileName = _svc.Current.Name + ProjectPackage.Extension };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var keep = _svc.CurrentPath; var dirty = _svc.IsDirty;
            _svc.Rename(ProjectName);
            var p = _svc.Capture();
            ProjectPackage.Write(dlg.FileName, p, _svc.Workspace.Referenced(new[] { p.Visual.Background.AssetId }), _svc.Workspace.Meta);
            Status = $"Exported a copy to {dlg.FileName}";
        }
        catch (ProjectException ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(_svc.Store.DefaultFolder);
        Process.Start(new ProcessStartInfo(_svc.Store.DefaultFolder) { UseShellExecute = true });
    }
}
