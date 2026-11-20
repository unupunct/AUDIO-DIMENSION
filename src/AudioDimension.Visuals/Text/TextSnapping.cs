namespace AudioDimension.Visuals.Text;

/// <summary>Snap logic for dragging text in the preview: centre/thirds for the box centre, safe margins for the box edges.</summary>
public static class TextSnapping
{
    public const double Threshold = 8;
    public const double Margin = 0.05;

    /// <summary>
    /// Returns the snapped box centre and the guide positions (as fractions of the frame) that were hit, or null when nothing snapped.
    /// </summary>
    public static (double Cx, double Cy, double? GuideX, double? GuideY) Snap(double cx, double cy, double boxW, double boxH, double frameW, double frameH, double threshold = Threshold)
    {
        double bestX = threshold + 1, bestY = threshold + 1, dx = 0, dy = 0;
        double? gx = null, gy = null;
        foreach (var target in new[] { frameW * 0.5, frameW / 3.0, 2 * frameW / 3.0 })
        { var d = target - cx; if (Math.Abs(d) < bestX) { bestX = Math.Abs(d); dx = d; gx = target; } }
        foreach (var (target, side) in new[] { (frameW * Margin, -1), (frameW * (1 - Margin), 1) })
        { var d = target - (cx + side * boxW / 2); if (Math.Abs(d) < bestX) { bestX = Math.Abs(d); dx = d; gx = target; } }
        foreach (var target in new[] { frameH * 0.5, frameH / 3.0, 2 * frameH / 3.0 })
        { var d = target - cy; if (Math.Abs(d) < bestY) { bestY = Math.Abs(d); dy = d; gy = target; } }
        foreach (var (target, side) in new[] { (frameH * Margin, -1), (frameH * (1 - Margin), 1) })
        { var d = target - (cy + side * boxH / 2); if (Math.Abs(d) < bestY) { bestY = Math.Abs(d); dy = d; gy = target; } }
        if (bestX <= threshold) cx += dx; else gx = null;
        if (bestY <= threshold) cy += dy; else gy = null;
        return (cx, cy, gx is null ? null : gx / frameW, gy is null ? null : gy / frameH);
    }
}
