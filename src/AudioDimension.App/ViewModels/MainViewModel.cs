using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioDimension.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public IReadOnlyList<PageViewModel> Pages { get; }
    [ObservableProperty] private PageViewModel _selectedPage;
    [ObservableProperty] private bool _inspectorOpen = App.Settings.Current.InspectorOpen;
    [ObservableProperty] private double _uiScale = App.Settings.Current.UiScale;

    [ObservableProperty] private bool _inspectorVisible;
    private readonly RecordingViewModel? _recorder;
    public ProjectsViewModel Projects { get; }
    public string WindowTitle => "Audio Dimension - " + App.Projects.Title;
    /// <summary>Offers to save unsaved changes. Returns false when the user cancels (the window then stays open).</summary>
    public bool ConfirmClose() => Projects.ConfirmDiscardChanges();
    [ObservableProperty] private string _recText = "";
    [ObservableProperty] private bool _recVisible;

    private void SyncRec()
    {
        var st = App.Recording.State;
        RecVisible = st != AudioDimension.Recording.RecordingState.Idle;
        RecText = st switch { AudioDimension.Recording.RecordingState.Recording => "REC", AudioDimension.Recording.RecordingState.Paused => "REC PAUSED", AudioDimension.Recording.RecordingState.Rendering => "RENDERING VIDEO", _ => "" };
    }

    public void Shutdown() => _recorder?.Shutdown();

    [ObservableProperty] private string _captureText = "";
    [ObservableProperty] private bool _captureLive;

    private void SyncCapture()
    {
        var st = App.Audio.State;
        CaptureLive = st is Audio.Capture.EngineState.Capturing or Audio.Capture.EngineState.Reconnecting;
        string what = App.Audio.Source == Audio.Devices.AudioSource.Microphone ? "MICROPHONE" : "SYSTEM AUDIO";
        CaptureText = st switch
        {
            Audio.Capture.EngineState.Capturing => $"{what} LIVE",
            Audio.Capture.EngineState.Reconnecting => $"{what} - RECONNECTING",
            _ => ""
        };
    }

    public MainViewModel()
    {
        App.Audio.StateChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(SyncCapture);
        var audioVm = new AudioViewModel();
        var studioVm = new StudioViewModel(audioVm);
        var library = new PresetLibraryViewModel(studioVm, audioVm, p => SelectedPage = p);
        var recorder = new RecordingViewModel(studioVm, App.Recording);
        studioVm.Recorder = recorder;
        App.Projects.Attach(studioVm, audioVm, recorder);
        Projects = new ProjectsViewModel(App.Projects);
        App.Projects.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(WindowTitle)));
        _recorder = recorder;
        App.Recording.StateChanged += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(SyncRec);
        Pages = new PageViewModel[]
        {
            new DashboardViewModel(),
            studioVm,
            library,
            new TextEditorViewModel(studioVm),
            audioVm,
            recorder,
            Projects,
            new SettingsViewModel(),
            new AboutViewModel(),
        };
        foreach (var page in Pages)
            page.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PageViewModel.IsSelected) && sender is PageViewModel pv && pv.IsSelected)
                    SelectedPage = pv;
            };
        _selectedPage = Pages[0];
        _selectedPage.IsSelected = true;
        UpdateInspectorVisibility();
        App.Settings.Changed += s => UiScale = s.UiScale;
    }

    partial void OnSelectedPageChanged(PageViewModel? oldValue, PageViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        newValue.IsSelected = true;
        UpdateInspectorVisibility();
    }

    partial void OnInspectorOpenChanged(bool value) => UpdateInspectorVisibility();

    private void UpdateInspectorVisibility() => InspectorVisible = InspectorOpen && SelectedPage?.InspectorViewModel is not null;

    [RelayCommand]
    private void ToggleInspector()
    {
        InspectorOpen = !InspectorOpen;
        var s = App.Settings.Current;
        s.InspectorOpen = InspectorOpen;
        App.Settings.Save(s);
    }

    [RelayCommand] private void Navigate(PageViewModel page) => SelectedPage = page;
}
