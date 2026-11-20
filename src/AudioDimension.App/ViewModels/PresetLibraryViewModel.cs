using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using AudioDimension.App.Services;
using AudioDimension.Presets;
using AudioDimension.Visuals.Visualizers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;

namespace AudioDimension.App.ViewModels;

public sealed partial class PresetCardViewModel : ObservableObject
{
    private bool _syncing;
    public PresetDefinition Preset { get; }
    public PresetCardViewModel(PresetDefinition p)
    {
        Preset = p; _syncing = true; IsFavorite = p.IsFavorite; _syncing = false;
        Thumbnail = ThumbnailLoader.Load(p);
    }
    public string Id => Preset.Id;
    public string Name => Preset.Name;
    public string Category => Preset.Category;
    public string Description => Preset.Description;
    public bool IsCustom => Preset.IsCustom;
    public string Source => Preset.IsCustom ? "Custom" : "Built-in";
    public string VisualizerName => VisualizerRegistry.Exists(Preset.VisualizerId) ? VisualizerRegistry.Create(Preset.VisualizerId).Name : Preset.VisualizerId;
    public string Customizable => string.Join(", ", Preset.Customizable);
    public ImageSource? Thumbnail { get; }
    public IReadOnlyList<string> Colors => Preset.Visual.Palette.Colors;

    [ObservableProperty] private bool _isFavorite;
    partial void OnIsFavoriteChanged(bool value) { if (!_syncing) App.Presets.SetFavorite(Id, value); }
    public void SyncFavorite(bool v) { _syncing = true; IsFavorite = v; _syncing = false; }
}

public sealed partial class PresetLibraryViewModel : PageViewModel
{
    public override string Title => "Preset Library";
    public override string Glyph => "";

    private readonly StudioViewModel _studio;
    private readonly Action<PageViewModel> _navigate;
    private bool _refreshing;

    public StudioViewModel PreviewStudio { get; }
    public ObservableCollection<PresetCardViewModel> Cards { get; } = new();
    public string[] Categories { get; } = new[] { "All categories" }.Concat(PresetCategories.BuiltIn).Append(PresetCategories.Custom).ToArray();
    public PresetSort[] SortOptions { get; } = Enum.GetValues<PresetSort>();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedCategory = "All categories";
    [ObservableProperty] private PresetSort _selectedSort = PresetSort.Category;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _customOnly;
    [ObservableProperty] private PresetCardViewModel? _selected;
    [ObservableProperty] private string _renameText = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _countText = "";

    public PresetLibraryViewModel(StudioViewModel studio, AudioViewModel audio, Action<PageViewModel> navigate)
    {
        _studio = studio; _navigate = navigate;
        PreviewStudio = new StudioViewModel(audio, previewOnly: true);
        App.Presets.Changed += () => Application.Current?.Dispatcher.BeginInvoke(Refresh);
        Refresh();
        Selected = Cards.FirstOrDefault();
    }

    partial void OnSearchChanged(string value) => Refresh();
    partial void OnSelectedCategoryChanged(string value) => Refresh();
    partial void OnSelectedSortChanged(PresetSort value) => Refresh();
    partial void OnFavoritesOnlyChanged(bool value) => Refresh();
    partial void OnCustomOnlyChanged(bool value) => Refresh();

    partial void OnSelectedChanged(PresetCardViewModel? value)
    {
        if (_refreshing || value is null) return;
        RenameText = value.Name;
        PreviewStudio.ApplyPreset(value.Preset, applyAudio: false);
        OnPropertyChanged(nameof(CanEdit));
    }

    public bool CanEdit => Selected is { IsCustom: true };

    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var filter = new PresetFilter(Search, SelectedCategory == "All categories" ? null : SelectedCategory, FavoritesOnly, CustomOnly, SelectedSort);
            var list = PresetQuery.Apply(App.Presets.All, filter);
            string? keep = Selected?.Id;
            bool same = list.Count == Cards.Count && list.Select(p => p.Id).SequenceEqual(Cards.Select(c => c.Id)) && list.All(p => Cards.First(c => c.Id == p.Id).Preset.Name == p.Name);
            if (same)
            {
                foreach (var c in Cards) c.SyncFavorite(App.Presets.Find(c.Id)?.IsFavorite ?? false);
            }
            else
            {
                Cards.Clear();
                foreach (var p in list) Cards.Add(new PresetCardViewModel(p));
                Selected = keep is null ? null : Cards.FirstOrDefault(c => c.Id == keep);
            }
            CountText = $"{Cards.Count} of {App.Presets.All.Count} presets";
        }
        finally { _refreshing = false; }
        if (Selected is null && Cards.Count > 0) Selected = Cards[0];
        OnPropertyChanged(nameof(CanEdit));
    }

    [RelayCommand]
    private void ClearFilters() { Search = ""; SelectedCategory = "All categories"; FavoritesOnly = false; CustomOnly = false; }

    [RelayCommand]
    private void ApplyToStudio()
    {
        if (Selected is null) return;
        _studio.ApplyPreset(Selected.Preset);
        Status = $"Loaded '{Selected.Name}' into Visualizer Studio.";
        _navigate(_studio);
    }

    [RelayCommand]
    private void ToggleFavorite(PresetCardViewModel? card) { if (card is not null) card.IsFavorite = !card.IsFavorite; }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is null) return;
        var copy = App.Presets.Duplicate(Selected.Id);
        if (copy is null) return;
        Selected = Cards.FirstOrDefault(c => c.Id == copy.Id);
        Status = $"Created '{copy.Name}'.";
    }

    [RelayCommand]
    private void Rename()
    {
        if (Selected is not { IsCustom: true } s) return;
        if (string.IsNullOrWhiteSpace(RenameText)) { Status = "Enter a name first."; return; }
        string id = s.Id;
        if (App.Presets.Rename(id, RenameText)) { Selected = Cards.FirstOrDefault(c => c.Id == id); Status = "Renamed."; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { IsCustom: true } s) { Status = "Built-in presets cannot be deleted. Duplicate one to make your own."; return; }
        if (MessageBox.Show($"Delete the preset '{s.Name}'? This cannot be undone.", "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (App.Presets.Delete(s.Id)) Status = $"Deleted '{s.Name}'.";
    }

    [RelayCommand]
    private void Export()
    {
        if (Selected is null) return;
        var dlg = new SaveFileDialog { Title = "Export preset", Filter = "Audio Dimension preset (*.adpreset)|*.adpreset|JSON (*.json)|*.json", FileName = Selected.Id + ".adpreset" };
        if (dlg.ShowDialog() != true) return;
        try { App.Presets.Export(new[] { Selected.Id }, dlg.FileName); Status = $"Exported '{Selected.Name}'."; }
        catch (Exception ex) { Log.Error(ex, "Preset export failed"); Status = "Export failed: " + ex.Message; }
    }

    [RelayCommand]
    private void ExportCustom()
    {
        var ids = App.Presets.All.Where(p => p.IsCustom).Select(p => p.Id).ToList();
        if (ids.Count == 0) { Status = "You have no custom presets to export yet."; return; }
        var dlg = new SaveFileDialog { Title = "Export all custom presets", Filter = "Audio Dimension preset pack (*.adpreset)|*.adpreset", FileName = "my-presets.adpreset" };
        if (dlg.ShowDialog() != true) return;
        try { App.Presets.Export(ids, dlg.FileName); Status = $"Exported {ids.Count} presets."; }
        catch (Exception ex) { Log.Error(ex, "Preset export failed"); Status = "Export failed: " + ex.Message; }
    }

    [RelayCommand]
    private void Import()
    {
        var dlg = new OpenFileDialog { Title = "Import presets", Filter = "Audio Dimension presets (*.adpreset;*.json)|*.adpreset;*.json", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        int ok = 0; var problems = new List<string>(); string? lastId = null;
        foreach (var f in dlg.FileNames)
        {
            var res = App.Presets.Import(f);
            if (res.Ok) { ok += res.Presets.Count; lastId = res.Presets[^1].Id; }
            else problems.Add($"{System.IO.Path.GetFileName(f)}: {string.Join(" ", res.Errors.Take(3))}");
        }
        Status = $"Imported {ok} preset(s)." + (problems.Count > 0 ? $" {problems.Count} file(s) were rejected." : "");
        if (lastId is not null) { CustomOnly = false; Selected = Cards.FirstOrDefault(c => c.Id == lastId); }
        if (problems.Count > 0)
            MessageBox.Show("Some files could not be imported because they are not valid presets:\n\n" + string.Join("\n\n", problems), "Import presets", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(App.Paths.Presets);
        Process.Start(new ProcessStartInfo(App.Paths.Presets) { UseShellExecute = true });
    }
}
