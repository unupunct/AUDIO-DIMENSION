using SkiaSharp;

namespace AudioDimension.Visuals.Text;

/// <summary>Layout result in pixels for a given canvas size (rotation/animation not applied).</summary>
public readonly record struct TextBounds(float CenterX, float CenterY, float Width, float Height)
{
    public bool Contains(float x, float y) => Math.Abs(x - CenterX) <= Width / 2 && Math.Abs(y - CenterY) <= Height / 2;
}

/// <summary>Draws text layers with SkiaSharp: fonts, spacing, shadow, outline, glow, panel, border and all animation effects.</summary>
public static class TextRenderer
{
    private static readonly Dictionary<string, SKTypeface> Faces = new();

    private static SKTypeface Face(string family, int weight, bool italic)
    {
        string key = $"{family}|{weight}|{italic}";
        lock (Faces)
        {
            if (Faces.TryGetValue(key, out var f)) return f;
            var style = new SKFontStyle(Math.Clamp(weight, 100, 900), (int)SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            f = SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
            Faces[key] = f;
            return f;
        }
    }

    public static IReadOnlyList<string> InstalledFonts() =>
        SKFontManager.Default.GetFontFamilies().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    private static SKColor Col(string hex, double opacity = 1)
    {
        if (!SKColor.TryParse(hex, out var c)) c = SKColors.White;
        return c.WithAlpha((byte)Math.Clamp(opacity * c.Alpha, 0, 255));
    }

    private sealed class Layout
    {
        public SKFont Font = null!;
        public string[] Lines = Array.Empty<string>();
        public float[][] Adv = Array.Empty<float[]>();
        public float[] LineW = Array.Empty<float>();
        public float BlockW, BlockH, LineH, Ascent, Pad, Ps, Spacing;
        public int CharCount;
    }

    private static Layout Measure(TextLayer l, int h)
    {
        float ps = h / 1080f, size = Math.Max(4f, (float)l.FontSize * ps * (float)l.Scale);
        var font = new SKFont(Face(l.FontFamily, l.FontWeight, l.Italic), size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        var lines = (l.Text ?? "").Replace("\r", "").Split('\n');
        float spacing = (float)l.LetterSpacing * ps * (float)l.Scale;
        var lay = new Layout { Font = font, Lines = lines, Ps = ps, Spacing = spacing, Pad = (float)l.Padding * ps * (float)l.Scale };
        lay.Adv = new float[lines.Length][]; lay.LineW = new float[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var adv = new float[lines[i].Length]; float w = 0;
            for (int c = 0; c < lines[i].Length; c++) { adv[c] = font.MeasureText(lines[i][c].ToString()) + spacing; w += adv[c]; }
            lay.Adv[i] = adv; lay.LineW[i] = Math.Max(0, w - (adv.Length > 0 ? spacing : 0)); lay.CharCount += lines[i].Length;
        }
        lay.LineH = size * 1.2f * (float)Math.Max(0.5, l.LineSpacing);
        lay.BlockW = lay.LineW.Length == 0 ? 0 : lay.LineW.Max();
        lay.BlockH = lay.LineH * lines.Length;
        lay.Ascent = -font.Metrics.Ascent;
        return lay;
    }

    /// <summary>
    /// Pixel bounds of the layer's box (text plus padding when a panel/border is on) for hit-testing and guides.
    /// X is an anchor: Left-aligned layers start at X, Right-aligned layers end at X, Centre-aligned layers are centred on X.
    /// </summary>
    public static TextBounds Bounds(TextLayer l, int w, int h)
    {
        var lay = Measure(l, h);
        float pad = (l.Panel.Enabled || l.Border.Enabled) ? lay.Pad : 0;
        float bw = Math.Max(lay.BlockW, 4f) + pad * 2, bh = Math.Max(lay.BlockH, 4f) + pad * 2;
        lay.Font.Dispose();
        float shift = l.Align switch { TextAlign.Left => bw / 2, TextAlign.Right => -bw / 2, _ => 0f };
        return new TextBounds((float)l.X * w + shift, (float)l.Y * h, bw, bh);
    }

    /// <summary>Horizontal distance (pixels) from the X anchor to the box centre; used when dragging to keep the grabbed point under the cursor.</summary>
    public static float AnchorShift(TextLayer l, int w, int h) => Bounds(l, w, h).CenterX - (float)l.X * w;

    public static void Render(SKCanvas canvas, int w, int h, IEnumerable<TextLayer> layers, double t)
    {
        foreach (var l in layers)
        {
            var st = TextAnimator.Evaluate(l, t);
            if (!st.Visible || st.Alpha <= 0.002f) continue;
            DrawLayer(canvas, w, h, l, st, t);
        }
    }

    private static void DrawLayer(SKCanvas canvas, int w, int h, TextLayer l, TextState st, double t)
    {
        var lay = Measure(l, h);
        float ps = lay.Ps;
        float padAnchor = (l.Panel.Enabled || l.Border.Enabled) ? lay.Pad : 0, boxW = Math.Max(lay.BlockW, 4f) + padAnchor * 2;
        float shift = l.Align switch { TextAlign.Left => boxW / 2, TextAlign.Right => -boxW / 2, _ => 0f };
        float cx = (float)l.X * w + shift + st.OffsetX * ps, cy = (float)l.Y * h + st.OffsetY * ps;

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees((float)l.Rotation + st.Rotation);
        canvas.Scale(st.Scale, st.Scale);

        using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(Math.Clamp(st.Alpha * l.Opacity, 0, 1) * 255)) };
        if (st.Blur > 0.2f) layerPaint.ImageFilter = SKImageFilter.CreateBlur(st.Blur * ps, st.Blur * ps);
        canvas.SaveLayer(layerPaint);

        float pad = (l.Panel.Enabled || l.Border.Enabled) ? lay.Pad : 0;
        var box = new SKRect(-lay.BlockW / 2 - pad, -lay.BlockH / 2 - pad, lay.BlockW / 2 + pad, lay.BlockH / 2 + pad);
        float radius = (float)l.Panel.CornerRadius * ps;
        if (l.Panel.Enabled)
        {
            using var p = new SKPaint { Color = Col(l.Panel.Color, l.Panel.Opacity), IsAntialias = true };
            canvas.DrawRoundRect(box, radius, radius, p);
        }
        if (l.Border.Enabled)
        {
            using var p = new SKPaint { Color = Col(l.Border.Color, l.Border.Opacity), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, (float)l.Border.Width * ps) };
            canvas.DrawRoundRect(box, radius, radius, p);
        }

        int visibleChars = (int)MathF.Floor(st.Reveal * lay.CharCount + 0.0001f);
        if (st.Reveal >= 0.999f) visibleChars = lay.CharCount;

        // pass helper: draws every visible glyph with the given paint, applying per-letter effects
        void Pass(SKPaint paint, float dx = 0, float dy = 0, bool stroke = false)
        {
            int idx = 0;
            float y0 = -lay.BlockH / 2 + lay.Ascent + (lay.LineH - lay.Font.Size * 1.2f) / 2;
            for (int li = 0; li < lay.Lines.Length; li++)
            {
                float lw = lay.LineW[li];
                float x = l.Align switch { TextAlign.Left => -lay.BlockW / 2, TextAlign.Right => lay.BlockW / 2 - lw, _ => -lw / 2 };
                float y = y0 + li * lay.LineH;
                for (int c = 0; c < lay.Lines[li].Length; c++, idx++)
                {
                    if (idx >= visibleChars) { x += lay.Adv[li][c]; continue; }
                    float la = 1f, ly = 0f;
                    if (st.LetterReveal < 0.999f)
                    {
                        float k = Math.Clamp((st.LetterReveal * (lay.CharCount + 5) - idx) / 5f, 0f, 1f);
                        la = k; ly = (1 - k) * 22f * ps;
                    }
                    if (st.WaveAmp > 0) ly += MathF.Sin(st.WavePhase + idx * 0.55f) * st.WaveAmp * ps;
                    if (la <= 0.001f) { x += lay.Adv[li][c]; continue; }
                    byte a0 = paint.Color.Alpha; if (la < 1f) paint.Color = paint.Color.WithAlpha((byte)(a0 * la));
                    canvas.DrawText(lay.Lines[li][c].ToString(), x + dx, y + ly + dy, SKTextAlign.Left, lay.Font, paint);
                    if (la < 1f) paint.Color = paint.Color.WithAlpha(a0);
                    x += lay.Adv[li][c];
                }
            }
        }

        if (l.Shadow.Enabled)
        {
            using var p = new SKPaint { Color = Col(l.Shadow.Color, l.Shadow.Opacity), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.1f, (float)l.Shadow.Blur * ps / 2f)) };
            Pass(p, (float)l.Shadow.OffsetX * ps, (float)l.Shadow.OffsetY * ps);
        }
        if (l.Glow.Enabled)
        {
            using var p = new SKPaint { Color = Col(l.Glow.Color, Math.Min(1.0, 0.85 * l.Glow.Intensity)), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, (float)l.Glow.Radius * ps / 2f)) };
            Pass(p); if (l.Glow.Intensity > 1) Pass(p);
        }
        if (l.Outline.Enabled)
        {
            using var p = new SKPaint { Color = Col(l.Outline.Color), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, (float)l.Outline.Width * ps * 2), StrokeJoin = SKStrokeJoin.Round };
            Pass(p, stroke: true);
        }

        // chromatic split copies (RGB Split / Glitch)
        float split = st.RgbSplit + st.Glow_Split();
        if (split > 0.2f)
        {
            using var pr = new SKPaint { Color = new SKColor(255, 0, 60, 170), IsAntialias = true, BlendMode = SKBlendMode.Plus };
            using var pb = new SKPaint { Color = new SKColor(0, 200, 255, 170), IsAntialias = true, BlendMode = SKBlendMode.Plus };
            Pass(pr, -split * ps, 0); Pass(pb, split * ps, 0);
        }

        using var fill = new SKPaint { Color = Col(l.Color), IsAntialias = true };
        bool sweep = st.Sweep >= 0f;
        if (sweep) canvas.SaveLayer();
        if (st.Glitch > 0.01f)
        {
            // horizontal slices displaced by a deterministic per-frame offset
            int frame = (int)Math.Floor(t * 14);
            const int slices = 6;
            float top = box.Top - 10 * ps, hh = (box.Height + 20 * ps) / slices;
            for (int k = 0; k < slices; k++)
            {
                float off = (TextAnimator.Hash(frame * 97 + k * 13) - 0.5f) * 2f * st.Glitch * 34f * ps;
                canvas.Save(); canvas.ClipRect(new SKRect(box.Left - 80 * ps, top + k * hh, box.Right + 80 * ps, top + (k + 1) * hh));
                Pass(fill, off, 0); canvas.Restore();
            }
        }
        else Pass(fill);
        if (sweep)
        {
            float bw = lay.BlockW + pad * 2 + 80 * ps, bx = -bw / 2 + st.Sweep * bw * 1.4f - bw * 0.2f;
            using var band = new SKPaint
            {
                BlendMode = SKBlendMode.SrcATop,
                Shader = SKShader.CreateLinearGradient(new SKPoint(bx - 40 * ps, 0), new SKPoint(bx + 40 * ps, 0),
                    new[] { SKColors.Transparent, new SKColor(255, 255, 255, 235), SKColors.Transparent }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(new SKRect(bx - 40 * ps, box.Top - 40 * ps, bx + 40 * ps, box.Bottom + 40 * ps), band);
            canvas.Restore();
        }

        lay.Font.Dispose();
        canvas.Restore();   // layer
        canvas.Restore();   // transform
    }

    private static float Glow_Split(this TextState s) => s.Glitch > 0.01f ? s.Glitch * 9f : 0f;
}
