using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AudioDimension.App.Controls;
using AudioDimension.App.ViewModels;

namespace AudioDimension.App.Services;

/// <summary>Creates borderless / fullscreen preview windows. Each owns its own GL context and renderer.</summary>
public static class PreviewWindows
{
    private static Window? _current;

    public static void Open(StudioViewModel studio, bool fullscreen)
    {
        _current?.Close();
        var preview = new GlPreview { DataContext = studio, IsMainPreview = false };
        var win = new Window
        {
            Title = "Audio Dimension - Preview",
            Content = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.Black, Child = preview },
            WindowStyle = WindowStyle.None,
            ResizeMode = fullscreen ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip,
            Background = System.Windows.Media.Brushes.Black,
            ShowInTaskbar = true,
            Width = 1280, Height = 720,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Topmost = fullscreen
        };
        bool full = fullscreen;
        void Place()
        {
            var h = new WindowInteropHelper(win).Handle;
            var m = studio.SelectedMonitor ?? MonitorService.List()[0];
            if (full) MonitorService.Place(h, m.X, m.Y, m.Width, m.Height);
            else MonitorService.Place(h, m.X + m.Width / 2 - 640, m.Y + m.Height / 2 - 360, 1280, 720);
        }
        win.SourceInitialized += (_, _) => Place();
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close();
            else if (e.Key == Key.F11 || e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt) { full = !full; win.Topmost = full; win.ResizeMode = full ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip; Place(); }
        };
        win.MouseLeftButtonDown += (_, e) => { if (!full && e.ClickCount == 1) { try { win.DragMove(); } catch (InvalidOperationException) { } } else if (e.ClickCount == 2) { full = !full; win.Topmost = full; Place(); } };
        win.Closed += (_, _) => { studio.IsDetached = false; if (ReferenceEquals(_current, win)) _current = null; };
        _current = win;
        studio.IsDetached = true;
        win.Show();
    }

    public static void CloseAll() => _current?.Close();
}
