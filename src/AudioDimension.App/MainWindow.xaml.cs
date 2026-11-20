using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AudioDimension.App.ViewModels;

namespace AudioDimension.App;

public partial class MainWindow : Window
{
    private bool _built;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildPages();
        Closing += (_, e) => { if (DataContext is MainViewModel vm && !vm.ConfirmClose()) e.Cancel = true; };
    }

    /// <summary>One ContentControl per page, kept alive and shown/hidden by IsSelected (plain content controls stay visible to UI Automation).</summary>
    private void BuildPages()
    {
        if (_built || DataContext is not MainViewModel vm) return;
        _built = true;
        foreach (var page in vm.Pages)
        {
            var host = new ContentControl { Content = page, IsTabStop = false, Focusable = false };
            host.SetBinding(VisibilityProperty, new Binding(nameof(PageViewModel.IsSelected)) { Source = page, Converter = new BooleanToVisibilityConverter() });
            PageHost.Children.Add(host);
        }
    }
}
