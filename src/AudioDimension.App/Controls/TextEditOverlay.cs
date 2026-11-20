using System.Windows;
using System.Windows.Media;
using AudioDimension.Visuals.Text;

namespace AudioDimension.App.Controls;

/// <summary>Draws the selection box of the active text layer plus snap guide lines while dragging.</summary>
public sealed class TextEditOverlay : FrameworkElement
{
    public TextLayer? Layer { get; set; }
    private double? _gx, _gy;

    public void SetGuides(double? x, double? y) { _gx = x; _gy = y; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var accent = (Brush)(Application.Current.TryFindResource("AccentBrush") ?? Brushes.Cyan);
        if (Layer is { } l && w > 8 && h > 8)
        {
            var b = TextRenderer.Bounds(l, (int)w, (int)h);
            var pen = new Pen(accent, 1.5) { DashStyle = DashStyles.Dash };
            var r = new Rect(b.CenterX - b.Width / 2 - 4, b.CenterY - b.Height / 2 - 4, b.Width + 8, b.Height + 8);
            dc.PushTransform(new RotateTransform(l.Rotation, b.CenterX, b.CenterY));
            dc.DrawRectangle(null, pen, r);
            foreach (var c in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight }) dc.DrawRectangle(accent, null, new Rect(c.X - 3, c.Y - 3, 6, 6));
            dc.Pop();
        }
        var gp = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 64, 160)), 1);
        if (_gx is double gx) dc.DrawLine(gp, new Point(gx * w, 0), new Point(gx * w, h));
        if (_gy is double gy) dc.DrawLine(gp, new Point(0, gy * h), new Point(w, gy * h));
    }
}
