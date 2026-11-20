using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Classic bar spectrum with falling peak caps and an optional mirrored reflection.</summary>
public sealed class ClassicSpectrum : Visualizer
{
    public override string Id => "classic-spectrum";
    public override string Name => "Classic Spectrum";
    public override string Description => "Frequency bars with peak caps, driven by the live FFT.";
    private readonly float[] _peaks = new float[128];

    public override void Reset() => Array.Clear(_peaks);

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = f.P.Segments > 0 ? Math.Min(f.P.Segments, 128) : 48;
        float halfW = f.Aspect * 0.92f, totalW = halfW * 2, gap = totalW / n * 0.22f, bw = totalW / n - gap;
        float scale = (float)f.P.Scale;
        float baseY = f.P.Mirror ? 0f : -0.8f, maxH = (f.P.Mirror ? 0.8f : 1.5f) * scale;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            float v = f.Spec01(t);
            float h = 0.012f + v * maxH;
            float x = -halfW + i * (bw + gap);
            c.Rect(x, baseY, bw, h, f.Pal(t, 0.35f), f.Pal(t, 1f));
            if (f.P.Mirror) c.Rect(x, baseY - h * 0.7f, bw, h * 0.7f, f.Pal(t, 0f), f.Pal(t, 0.35f));
            else c.Rect(x, baseY - h * 0.25f, bw, h * 0.25f, f.Pal(t, 0f), f.Pal(t, 0.25f));
            _peaks[i] = Math.Max(h, _peaks[i] - f.Dt * 0.45f);
            c.Rect(x, baseY + _peaks[i] + 0.012f, bw, 0.012f, f.Pal(t, 1f), f.Pal(t, 1f));
        }
    }
}

/// <summary>Radial spectrum: bars around a circle that kicks with the bass.</summary>
public sealed class CircularSpectrum : Visualizer
{
    public override string Id => "circular-spectrum";
    public override string Name => "Circular Spectrum";
    public override string Description => "Spectrum wrapped around a pulsing ring.";
    private float _angle;

    public override void Reset() => _angle = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = f.P.Segments > 0 ? f.P.Segments : 96;
        _angle += f.Dt * (0.25f + (float)f.P.Rotation * MathF.PI / 180f);
        float r0 = 0.36f * (float)f.P.Scale * (1f + f.Bass * 0.14f);
        float w = (float)f.P.LineWidth * 1.6f;
        bool inward = f.P.Variant == 1;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float v = f.Spec01(t < 0.5f ? t * 2 : (1 - t) * 2);  // mirrored so the ring is symmetric
            float a = _angle + MathF.Tau * t;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            float len = 0.015f + v * 0.5f * (float)f.P.Scale;
            var p0 = dir * r0;
            var p1 = inward ? dir * (r0 - len) : dir * (r0 + len);
            c.Line(p0, p1, w, f.Pal(t, 0.55f), f.Pal(t, 1f));
            c.Point(p1, 4 + v * 7, f.Pal(t, 0.9f));
        }
        c.Ring(Vector3.Zero, r0 * 0.96f, w * 0.5f, f.Pal(0.2f, 0.5f + f.Level * 0.4f), 96);
        c.Ring(Vector3.Zero, r0 * 0.5f * (1 + f.Bass * 0.4f), w * 0.4f, f.Pal(0.8f, 0.35f), 64);
    }
}

/// <summary>LED-style equaliser with lit segments and held peaks.</summary>
public sealed class NeonEqualizer : Visualizer
{
    public override string Id => "neon-equalizer";
    public override string Name => "Neon Equalizer";
    public override string Description => "Segmented neon columns driven by the spectrum.";
    private readonly float[] _hold = new float[64];
    private readonly float[] _smooth = new float[64];

    public override void Reset() { Array.Clear(_hold); Array.Clear(_smooth); }

    public override void Draw(Canvas c, FrameInput f)
    {
        int cols = f.P.Segments > 0 ? Math.Min(f.P.Segments, 64) : 20, rows = 20;
        float halfW = f.Aspect * 0.85f, totalW = halfW * 2, cw = totalW / cols, bw = cw * 0.72f;
        float top = 0.85f * (float)f.P.Scale, bottom = -0.85f * (float)f.P.Scale, ch = (top - bottom) / rows, bh = ch * 0.72f;
        for (int i = 0; i < cols; i++)
        {
            float t = (float)i / (cols - 1);
            float v = f.Spec01(t);
            _smooth[i] += (v - _smooth[i]) * Math.Min(1f, f.Dt * 18f);
            int lit = (int)MathF.Round(Math.Clamp(_smooth[i], 0, 1) * rows);
            _hold[i] = Math.Max(lit, _hold[i] - f.Dt * 5f);
            float x = -halfW + i * cw + (cw - bw) / 2;
            for (int r = 0; r < rows; r++)
            {
                float y = bottom + r * ch;
                float k = (float)r / (rows - 1);
                var col = f.Pal(f.P.Variant == 1 ? t : k);
                bool on = r < lit, peak = r == (int)_hold[i] && r >= lit && r > 0;
                col.W = on ? 1f : peak ? 0.9f : 0.07f;
                c.GlowQuad(new(x, y, 0), new(x + bw, y, 0), new(x + bw, y + bh, 0), new(x, y + bh, 0), col);
            }
        }
    }
}

/// <summary>Lit 3D bars: a spectrum "wall" with time flowing into the depth.</summary>
public sealed class Bars3D : Visualizer
{
    public override string Id => "bars-3d";
    public override string Name => "3D Frequency Bars";
    public override string Description => "A field of lit 3D bars; the newest spectrum is at the front and history recedes.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 18, Pitch = 30, Distance = 11, AutoRotateSpeed = 4 };
    private const int Rows = 14, MaxCols = 48;
    private readonly float[,] _hist = new float[Rows, MaxCols];
    private float _acc;

    public override void Reset() { Array.Clear(_hist); _acc = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        int cols = f.P.Segments > 0 ? Math.Min(f.P.Segments, MaxCols) : 32;
        _acc += f.Dt;
        if (_acc > 0.07f)
        {
            _acc = 0;
            for (int r = Rows - 1; r > 0; r--) for (int i = 0; i < MaxCols; i++) _hist[r, i] = _hist[r - 1, i];
            for (int i = 0; i < MaxCols; i++) _hist[0, i] = f.Spec01((float)i / Math.Max(1, cols - 1));
        }
        float sp = 0.42f * (float)f.P.Scale, hs = sp * 0.38f, maxH = 3.2f * (float)f.P.Scale;
        for (int r = Rows - 1; r >= 0; r--)
        {
            float fade = 1f - r / (float)Rows * 0.75f;
            for (int i = 0; i < cols; i++)
            {
                float v = _hist[r, i];
                float h = 0.04f + v * maxH;
                var col = f.Pal((float)i / cols, fade);
                var top = f.Pal(Math.Min(1f, (float)i / cols + 0.2f), fade);
                c.SolidBox(new((i - (cols - 1) / 2f) * sp, h / 2, (r - Rows / 2f) * sp * 1.15f), new(hs, h / 2, hs), col, top);
            }
        }
        // floor glow
        float w = cols * sp * 0.55f;
        c.GlowQuad(new(-w, 0, -Rows * sp * 0.7f), new(w, 0, -Rows * sp * 0.7f), new(w, 0, Rows * sp * 0.6f), new(-w, 0, Rows * sp * 0.6f),
            f.Pal(0.5f, 0.05f + f.Level * 0.1f));
    }
}

/// <summary>Smooth 3D terrain built from spectrum history, lit and outlined.</summary>
public sealed class FrequencyMountains : Visualizer
{
    public override string Id => "frequency-mountains";
    public override string Name => "Frequency Mountains";
    public override string Description => "A lit mountain range whose peaks rise with each frequency; older audio recedes.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 24, Distance = 13, AutoRotateSpeed = 2 };
    private const int Rows = 26, Cols = 64;
    private readonly float[,] _h = new float[Rows, Cols];
    private readonly Vector3[] _line = new Vector3[Cols];
    private float _acc;

    public override void Reset() { Array.Clear(_h); _acc = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        _acc += f.Dt;
        if (_acc > 0.06f)
        {
            _acc = 0;
            for (int r = Rows - 1; r > 0; r--) for (int i = 0; i < Cols; i++) _h[r, i] = _h[r - 1, i];
            for (int i = 0; i < Cols; i++)
            {
                float a = f.Spec(i), b = f.Spec(i - 1), d = f.Spec(i + 1);
                _h[0, i] = (a * 2 + b + d) / 4f;     // neighbour smoothing gives rounded peaks
            }
        }
        float sx = 0.2f * (float)f.P.Scale, sz = 0.42f, amp = 4.2f * (float)f.P.Scale;
        for (int r = Rows - 1; r > 0; r--)
        {
            float fade = 1f - r / (float)Rows * 0.8f;
            for (int i = 0; i < Cols - 1; i++)
            {
                Vector3 p00 = P(i, r), p10 = P(i + 1, r), p01 = P(i, r - 1), p11 = P(i + 1, r - 1);
                var c00 = Col(f, i, r, fade); var c10 = Col(f, i + 1, r, fade); var c01 = Col(f, i, r - 1, fade); var c11 = Col(f, i + 1, r - 1, fade);
                c.SolidQuad(p00, p10, p11, p01, c00, c10, c11, c01);
            }
        }
        for (int r = 0; r < Rows; r += 2)
        {
            for (int i = 0; i < Cols; i++) _line[i] = P(i, r) + new Vector3(0, 0.01f, 0);
            float fade = 1f - r / (float)Rows * 0.85f;
            c.Polyline(_line, 1.6f, t => f.Pal(t, 0.9f * fade));
        }
        Vector3 P(int i, int r) => new((i - Cols / 2f) * sx * 2.4f, _h[r, i] * amp, (r - Rows / 2f) * sz);
        Vector4 Col(FrameInput fi, int i, int r, float fade) { var v = fi.Pal(Math.Clamp(_h[r, i] * 1.4f, 0, 1)); v.W = fade; return v * new Vector4(0.8f, 0.8f, 0.8f, 1f); }
    }
}
