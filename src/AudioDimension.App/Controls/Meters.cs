using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioDimension.App.Controls;

/// <summary>Draws an array of 0..1 values as vertical bars (spectrum / band display).</summary>
public sealed class SpectrumView : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(float[]), typeof(SpectrumView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[]? Values { get => (float[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(TryFindResource("BgBrush") as Brush ?? Brushes.Black, null, new Rect(0, 0, w, h), 6, 6);
        var v = Values;
        if (v is null || v.Length == 0 || w < 4 || h < 4) return;
        var brush = TryFindResource("AccentBrush") as Brush ?? Brushes.Teal;
        double gap = v.Length > 16 ? 1.5 : 6;
        double bw = Math.Max(1, (w - gap * (v.Length - 1)) / v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            double bh = Math.Max(2, Math.Clamp(v[i], 0, 1) * (h - 4));
            dc.DrawRoundedRectangle(brush, null, new Rect(i * (bw + gap), h - bh - 2, bw, bh), 1.5, 1.5);
        }
    }
}

/// <summary>Horizontal level meter with a peak-hold tick. Level and Peak are 0..1 on a dB-scaled axis.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PeakProperty = DependencyProperty.Register(
        nameof(Peak), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double Peak { get => (double)GetValue(PeakProperty); set => SetValue(PeakProperty, value); }

    private static readonly Brush Gradient = Make();
    private static Brush Make()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0xD3, 0xA5), 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0xD3, 0xA5), 0.65));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF5, 0xC8, 0x42), 0.85));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xF0, 0x61, 0x6D), 1));
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(TryFindResource("BgBrush") as Brush ?? Brushes.Black, null, new Rect(0, 0, w, h), h / 2, h / 2);
        double fill = Math.Clamp(Level, 0, 1) * w;
        if (fill > 1)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, fill, h), h / 2, h / 2));
            dc.DrawRectangle(Gradient, null, new Rect(0, 0, w, h));
            dc.Pop();
        }
        double px = Math.Clamp(Peak, 0, 1) * w;
        if (px > 2) dc.DrawRectangle(Brushes.White, null, new Rect(Math.Min(px, w - 2) - 1, 1, 2, h - 2));
    }
}
