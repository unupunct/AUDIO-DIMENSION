using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioDimension.App.Controls;

/// <summary>Centres its child at a fixed aspect ratio inside the available space (0 = fill).</summary>
public sealed class AspectPanel : Decorator
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(AspectPanel),
        new FrameworkPropertyMetadata(16.0 / 9, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Ratio { get => (double)GetValue(RatioProperty); set => SetValue(RatioProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        Child?.Measure(Fit(available));
        return available;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var s = Fit(finalSize);
        Child?.Arrange(new Rect((finalSize.Width - s.Width) / 2, (finalSize.Height - s.Height) / 2, s.Width, s.Height));
        return finalSize;
    }

    private Size Fit(Size a)
    {
        if (double.IsInfinity(a.Width) || double.IsInfinity(a.Height)) return new Size(Math.Min(a.Width, 1280), Math.Min(a.Height, 720));
        if (Ratio <= 0) return a;
        double w = a.Width, h = w / Ratio;
        if (h > a.Height) { h = a.Height; w = h * Ratio; }
        return new Size(Math.Max(1, w), Math.Max(1, h));
    }
}

/// <summary>Rule-of-thirds, centre cross and safe-area guides drawn over the preview.</summary>
public sealed class GuidesOverlay : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
        var centre = new Pen(new SolidColorBrush(Color.FromArgb(150, 0x22, 0xD3, 0xA5)), 1);
        for (int i = 1; i < 3; i++)
        {
            dc.DrawLine(pen, new Point(w * i / 3, 0), new Point(w * i / 3, h));
            dc.DrawLine(pen, new Point(0, h * i / 3), new Point(w, h * i / 3));
        }
        dc.DrawLine(centre, new Point(w / 2, 0), new Point(w / 2, h));
        dc.DrawLine(centre, new Point(0, h / 2), new Point(w, h / 2));
        dc.DrawRectangle(null, pen, new Rect(w * 0.05, h * 0.05, w * 0.9, h * 0.9));   // title-safe area
    }
}
