using System.Windows.Controls;

namespace AudioDimension.App.Views;

public partial class PendingView : UserControl { public PendingView() => InitializeComponent(); }
public partial class AboutView : UserControl { public AboutView() => InitializeComponent(); }
public partial class DashboardView : UserControl { public DashboardView() => InitializeComponent(); }
public partial class SettingsView : UserControl { public SettingsView() => InitializeComponent(); }
public partial class AudioView : UserControl { public AudioView() => InitializeComponent(); }
public partial class PresetLibraryView : UserControl
{
    public PresetLibraryView() => InitializeComponent();

    private void OnCardDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.PresetLibraryViewModel vm) vm.ApplyToStudioCommand.Execute(null);
    }
}
public partial class TextEditorView : UserControl { public TextEditorView() => InitializeComponent(); }
public partial class TextInspectorView : UserControl { public TextInspectorView() => InitializeComponent(); }
public partial class RecordingView : UserControl { public RecordingView() => InitializeComponent(); }
public partial class ProjectsView : UserControl { public ProjectsView() => InitializeComponent(); }
public partial class StudioView : UserControl { public StudioView() => InitializeComponent(); }
public partial class StudioInspectorView : UserControl { public StudioInspectorView() => InitializeComponent(); }
