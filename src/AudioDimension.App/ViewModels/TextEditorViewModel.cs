using System.Collections.ObjectModel;
using System.Windows;
using AudioDimension.Presets;
using AudioDimension.Visuals.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;

namespace AudioDimension.App.ViewModels;

/// <summary>Wrapper so the inspector gets its own DataTemplate while sharing the editor view model.</summary>
public sealed record TextInspector(TextEditorViewModel Editor);

public sealed partial class TextEditorViewModel : PageViewModel
{
    public override string Title => "Text & Titles";
    public override string Glyph => "";
    public override object? InspectorViewModel { get; }

    public StudioViewModel Studio { get; }
    public ObservableCollection<TextLayer> Layers => Studio.Config.TextLayers;
    public ObservableCollection<TitleTemplate> Templates { get; } = new();

    public TextKind[] Kinds { get; } = Enum.GetValues<TextKind>();
    public TextAlign[] Aligns { get; } = Enum.GetValues<TextAlign>();
    public TextAnimType[] AnimTypes { get; } = Enum.GetValues<TextAnimType>();
    public Easing[] Easings { get; } = Enum.GetValues<Easing>();
    public AnimDirection[] Directions { get; } = Enum.GetValues<AnimDirection>();
    public int[] Weights { get; } = { 100, 200, 300, 400, 500, 600, 700, 800, 900 };
    public IReadOnlyList<string> Fonts { get; }
    public string[] AlignPresets { get; } = { "TL", "TC", "TR", "ML", "MC", "MR", "BL", "BC", "BR" };

    [ObservableProperty] private TextKind _newKind = TextKind.MainTitle;
    [ObservableProperty] private TextAnimType _newAnimation = TextAnimType.FadeIn;
    [ObservableProperty] private TitleTemplate? _selectedTemplate;
    [ObservableProperty] private string _newTemplateName = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _snapEnabled = true;

    public TextLayer? SelectedLayer { get => Studio.SelectedTextLayer; set => Studio.SelectedTextLayer = value; }
    public bool HasSelection => Studio.SelectedTextLayer is not null;
    public bool CanDeleteTemplate => SelectedTemplate is { IsCustom: true };

    public TextEditorViewModel(StudioViewModel studio)
    {
        Studio = studio;
        InspectorViewModel = new TextInspector(this);
        Fonts = TextRenderer.InstalledFonts();
        Studio.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioViewModel.SelectedTextLayer)) { OnPropertyChanged(nameof(SelectedLayer)); OnPropertyChanged(nameof(HasSelection)); }
        };
        ReloadTemplates();
        SelectedTemplate = Templates.FirstOrDefault();
    }

    partial void OnSelectedTemplateChanged(TitleTemplate? value) => OnPropertyChanged(nameof(CanDeleteTemplate));

    private void ReloadTemplates()
    {
        var keep = SelectedTemplate?.Name;
        Templates.Clear();
        foreach (var t in App.Templates.All()) Templates.Add(t);
        SelectedTemplate = Templates.FirstOrDefault(t => t.Name == keep) ?? Templates.FirstOrDefault();
    }

    // ---- layers --------------------------------------------------------------------------
    [RelayCommand]
    private void AddLayer()
    {
        var l = TextLayer.Create(NewKind);
        // stack new layers below existing ones so they do not land exactly on top of each other
        l.Y = Math.Clamp(l.Y + Layers.Count(x => x.Kind == NewKind) * 0.08, 0.05, 0.95);
        Layers.Add(l); SelectedLayer = l;
        Status = $"Added {TextLayer.KindLabel(l.Kind).ToLowerInvariant()} layer.";
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
        if (SelectedLayer is null) return;
        var c = SelectedLayer.Clone(); c.Y = Math.Min(0.95, c.Y + 0.06);
        Layers.Insert(Layers.IndexOf(SelectedLayer) + 1, c); SelectedLayer = c;
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        if (SelectedLayer is null) return;
        int i = Layers.IndexOf(SelectedLayer);
        Layers.Remove(SelectedLayer);
        SelectedLayer = Layers.Count == 0 ? null : Layers[Math.Min(i, Layers.Count - 1)];
    }

    [RelayCommand]
    private void MoveLayerUp() { if (SelectedLayer is { } l) { int i = Layers.IndexOf(l); if (i > 0) { Layers.Move(i, i - 1); } } }

    [RelayCommand]
    private void MoveLayerDown() { if (SelectedLayer is { } l) { int i = Layers.IndexOf(l); if (i >= 0 && i < Layers.Count - 1) { Layers.Move(i, i + 1); } } }

    [RelayCommand]
    private void ClearLayers() { Layers.Clear(); SelectedLayer = null; Status = "Removed all text layers."; }

    /// <summary>Places the selected layer on a 3x3 grid (top/middle/bottom x left/centre/right) with matching alignment.</summary>
    [RelayCommand]
    private void AlignTo(string cell)
    {
        if (SelectedLayer is not { } l || cell is not { Length: 2 }) return;
        const double margin = 0.06;
        l.Y = cell[0] switch { 'T' => margin + 0.04, 'B' => 1 - margin - 0.04, _ => 0.5 };
        switch (cell[1]) { case 'L': l.Align = TextAlign.Left; l.X = margin; break; case 'R': l.Align = TextAlign.Right; l.X = 1 - margin; break; default: l.Align = TextAlign.Center; l.X = 0.5; break; }
    }

    // ---- animations -------------------------------------------------------------------------
    [RelayCommand]
    private void AddAnimation()
    {
        if (SelectedLayer is not { } l) return;
        bool cont = TextAnimator.IsContinuous(NewAnimation);
        l.Animations.Add(new TextAnimation { Type = NewAnimation, Duration = cont ? 2.0 : 1.0, Easing = cont ? Easing.Linear : Easing.EaseOut, Loop = cont });
        Studio.RestartTimelineCommand.Execute(null);
    }

    [RelayCommand]
    private void RemoveAnimation(TextAnimation? a) { if (a is not null) SelectedLayer?.Animations.Remove(a); }

    // ---- templates ------------------------------------------------------------------------------
    [RelayCommand]
    private void ApplyTemplate()
    {
        if (SelectedTemplate is not { } t) return;
        if (Layers.Count > 0 && MessageBox.Show($"Replace the {Layers.Count} existing text layer(s) with '{t.Name}'?", "Apply title template", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Layers.Clear();
        foreach (var l in t.Layers) Layers.Add(l.Clone());
        SelectedLayer = Layers.FirstOrDefault();
        Studio.RestartTimelineCommand.Execute(null);
        Status = $"Applied '{t.Name}'.";
    }

    [RelayCommand]
    private void AddTemplate()
    {
        if (SelectedTemplate is not { } t) return;
        foreach (var l in t.Layers) Layers.Add(l.Clone());
        SelectedLayer = Layers.LastOrDefault();
        Studio.RestartTimelineCommand.Execute(null);
        Status = $"Added the layers of '{t.Name}'.";
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        if (Layers.Count == 0) { Status = "Add at least one text layer first."; return; }
        string name = string.IsNullOrWhiteSpace(NewTemplateName) ? "My title" : NewTemplateName.Trim();
        try
        {
            var t = App.Templates.Save(name, Layers, "Custom template.");
            NewTemplateName = ""; ReloadTemplates(); SelectedTemplate = Templates.FirstOrDefault(x => x.Name == t.Name);
            Status = $"Saved template '{t.Name}'.";
        }
        catch (Exception ex) { Log.Error(ex, "Saving template failed"); Status = "Could not save template: " + ex.Message; }
    }

    [RelayCommand]
    private void DeleteTemplate()
    {
        if (SelectedTemplate is not { IsCustom: true } t) { Status = "Built-in templates cannot be deleted."; return; }
        if (App.Templates.Delete(t.Name)) { ReloadTemplates(); Status = $"Deleted '{t.Name}'."; }
    }

    [RelayCommand]
    private void ImportTemplate()
    {
        var dlg = new OpenFileDialog { Title = "Import title template", Filter = "Audio Dimension template (*.adtemplate;*.json)|*.adtemplate;*.json" };
        if (dlg.ShowDialog() != true) return;
        var t = App.Templates.Import(dlg.FileName, out var errors);
        if (t is null) { MessageBox.Show("That file is not a valid title template:\n\n" + string.Join("\n", errors.Take(6)), "Import template", MessageBoxButton.OK, MessageBoxImage.Warning); Status = "Import rejected."; return; }
        ReloadTemplates(); SelectedTemplate = Templates.FirstOrDefault(x => x.Name == t.Name); Status = $"Imported '{t.Name}'.";
    }

    [RelayCommand]
    private void ExportTemplate()
    {
        if (SelectedTemplate is not { } t) return;
        var dlg = new SaveFileDialog { Title = "Export title template", Filter = "Audio Dimension template (*.adtemplate)|*.adtemplate", FileName = BuiltInPresets.Slug(t.Name) + ".adtemplate" };
        if (dlg.ShowDialog() != true) return;
        try { App.Templates.Export(t, dlg.FileName); Status = $"Exported '{t.Name}'."; }
        catch (Exception ex) { Log.Error(ex, "Template export failed"); Status = "Export failed: " + ex.Message; }
    }
}
