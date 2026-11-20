using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Glowing multi-layer waveform line.</summary>
public sealed class NeonWaveform : Visualizer
{
    public override string Id => "neon-waveform";
    public override string Name => "Neon Waveform";
    public override string Description => "The live waveform drawn as layered neon tubes.";
    private readonly Vector3[] _pts = new Vector3[256];

    public override void Draw(Canvas c, FrameInput f)
    {
        var w = f.Audio.Waveform; int n = _pts.Length;
        float half = f.Aspect * 0.95f, amp = 0.55f * (float)f.P.Scale * (float)f.P.Reactivity;
        for (int layer = 0; layer < (f.P.Mirror ? 2 : 1); layer++)
        {
            float sign = layer == 0 ? 1 : -1;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);
                float s = w[i * 2] + w[Math.Min(i * 2 + 1, w.Length - 1)];
                float env = MathF.Sin(t * MathF.PI);              // taper ends so the line anchors at the edges
                _pts[i] = new Vector3(-half + t * half * 2, sign * s * 0.5f * amp * (0.35f + 0.65f * env), 0);
            }
            float lw = (float)f.P.LineWidth;
            c.Polyline(_pts, lw * 4.0f, t => f.Pal(t, 0.12f));
            c.Polyline(_pts, lw * 2.0f, t => f.Pal(t, 0.35f));
            c.Polyline(_pts, lw, t => f.Pal(t, 1f));
        }
    }
}

/// <summary>Triggered oscilloscope with graticule and phosphor afterglow.</summary>
public sealed class Oscilloscope : Visualizer
{
    public override string Id => "oscilloscope";
    public override string Name => "Oscilloscope";
    public override string Description => "Triggered scope trace on a graticule with phosphor persistence.";
    private readonly Vector3[] _cur = new Vector3[256], _prev1 = new Vector3[256], _prev2 = new Vector3[256];
    private bool _has;

    public override void Reset() => _has = false;

    public override void Draw(Canvas c, FrameInput f)
    {
        float hw = f.Aspect * 0.9f, hh = 0.72f;
        var grid = f.Pal(0.1f, 0.12f);
        for (int i = -4; i <= 4; i++)
        {
            c.Line(new(i * hw / 4, -hh, 0), new(i * hw / 4, hh, 0), 1.5f, i == 0 ? f.Pal(0.1f, 0.25f) : grid);
            c.Line(new(-hw, i * hh / 4, 0), new(hw, i * hh / 4, 0), 1.5f, i == 0 ? f.Pal(0.1f, 0.25f) : grid);
        }
        var w = f.Audio.Waveform;
        int start = 0;                                           // rising zero-crossing trigger for a stable trace
        for (int i = 1; i < w.Length / 2; i++) if (w[i - 1] < 0 && w[i] >= 0) { start = i; break; }
        float amp = hh * (float)f.P.Scale * (float)f.P.Reactivity;
        if (_has) { Array.Copy(_prev1, _prev2, _cur.Length); Array.Copy(_cur, _prev1, _cur.Length); }
        for (int i = 0; i < _cur.Length; i++)
        {
            float t = (float)i / (_cur.Length - 1);
            _cur[i] = new Vector3(-hw + t * hw * 2, Math.Clamp(w[Math.Min(start + i, w.Length - 1)] * amp * 1.5f, -hh, hh), 0);
        }
        if (!_has) { Array.Copy(_cur, _prev1, _cur.Length); Array.Copy(_cur, _prev2, _cur.Length); _has = true; }
        float lw = (float)f.P.LineWidth;
        c.Polyline(_prev2, lw * 1.6f, f.Pal(0.5f, 0.12f));
        c.Polyline(_prev1, lw * 1.6f, f.Pal(0.5f, 0.25f));
        c.Polyline(_cur, lw * 3f, f.Pal(0.5f, 0.2f));
        c.Polyline(_cur, lw, f.Pal(0.5f, 1f));
    }
}

/// <summary>Waveform bent into a closed ring.</summary>
public sealed class CircularOscilloscope : Visualizer
{
    public override string Id => "circular-oscilloscope";
    public override string Name => "Circular Oscilloscope";
    public override string Description => "The waveform wrapped around a circle; loudness inflates the ring.";
    private readonly Vector3[] _pts = new Vector3[256];
    private float _rot;

    public override void Reset() => _rot = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        var w = f.Audio.Waveform; int n = _pts.Length;
        _rot += f.Dt * (0.15f + (float)f.P.Rotation * MathF.PI / 180f);
        float r0 = 0.5f * (float)f.P.Scale * (1f + f.Bass * 0.1f), amp = 0.4f * (float)f.P.Scale * (float)f.P.Reactivity;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n, a = _rot + MathF.Tau * t;
            float s = (w[i * 2] + w[i * 2 + 1]) * 0.5f;
            float r = r0 + s * amp;
            _pts[i] = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, 0);
        }
        float lw = (float)f.P.LineWidth;
        c.Polyline(_pts, lw * 4, t => f.Pal(t, 0.12f), true);
        c.Polyline(_pts, lw * 2, t => f.Pal(t, 0.35f), true);
        c.Polyline(_pts, lw, t => f.Pal(t, 1f), true);
        c.Ring(Vector3.Zero, r0 * 0.55f, lw * 0.6f, f.Pal(0.9f, 0.2f + f.Level * 0.3f), 64);
    }
}

/// <summary>3D landscape built from waveform history.</summary>
public sealed class WaveformLandscape3D : Visualizer
{
    public override string Id => "waveform-landscape";
    public override string Name => "3D Waveform Landscape";
    public override string Description => "Successive waveform slices form a flowing 3D terrain.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 15, Pitch = 32, Distance = 12, AutoRotateSpeed = 3 };
    private const int Rows = 30, Cols = 64;
    private readonly float[,] _h = new float[Rows, Cols];
    private readonly Vector3[] _line = new Vector3[Cols];
    private float _acc;

    public override void Reset() { Array.Clear(_h); _acc = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        _acc += f.Dt;
        if (_acc > 0.045f)
        {
            _acc = 0;
            for (int r = Rows - 1; r > 0; r--) for (int i = 0; i < Cols; i++) _h[r, i] = _h[r - 1, i];
            var w = f.Audio.Waveform;
            for (int i = 0; i < Cols; i++) _h[0, i] = w[i * 8] * (float)f.P.Reactivity;
        }
        float amp = 2.4f * (float)f.P.Scale, sx = 0.2f, sz = 0.4f;
        for (int r = Rows - 1; r > 0; r--)
        {
            float fade = 1f - r / (float)Rows * 0.85f;
            for (int i = 0; i < Cols - 1; i++)
            {
                var c00 = Col(f, i, r, fade); var c10 = Col(f, i + 1, r, fade); var c01 = Col(f, i, r - 1, fade); var c11 = Col(f, i + 1, r - 1, fade);
                c.SolidQuad(P(i, r), P(i + 1, r), P(i + 1, r - 1), P(i, r - 1), c00, c10, c11, c01);
            }
        }
        for (int r = 0; r < Rows; r++)
        {
            for (int i = 0; i < Cols; i++) _line[i] = P(i, r) + new Vector3(0, 0.01f, 0);
            float fade = 1f - r / (float)Rows * 0.9f;
            c.Polyline(_line, (float)f.P.LineWidth * 0.6f, t => f.Pal(t, fade));
        }
        Vector3 P(int i, int r) => new((i - Cols / 2f) * sx * 2f, _h[r, i] * amp, (r - Rows / 2f) * sz);
        Vector4 Col(FrameInput fi, int i, int r, float fade) { var v = fi.Pal(Math.Clamp(MathF.Abs(_h[r, i]) * 2.2f, 0, 1)); v.W = fade; return v * new Vector4(0.45f, 0.45f, 0.45f, 1f); }
    }
}

/// <summary>Layered flowing sine ribbons, each driven by a frequency band.</summary>
public sealed class EnergyWaves : Visualizer
{
    public override string Id => "energy-waves";
    public override string Name => "Energy Waves";
    public override string Description => "Stacked wave ribbons whose amplitude and speed follow the frequency bands.";
    private readonly float[] _phase = new float[16];
    private readonly Vector3[] _pts = new Vector3[160];

    public override void Reset() => Array.Clear(_phase);

    public override void Draw(Canvas c, FrameInput f)
    {
        int layers = f.P.Segments > 0 ? Math.Min(f.P.Segments, 16) : 7;
        float half = f.Aspect * 0.98f;
        for (int l = 0; l < layers; l++)
        {
            float band = l < 6 ? f.Band(l) : f.Level;
            float t0 = (float)l / Math.Max(1, layers - 1);
            _phase[l] += f.Dt * (0.8f + l * 0.35f + band * 4f);
            float amp = (0.08f + band * 0.55f) * (float)f.P.Scale, freq = 1.6f + l * 0.55f;
            float yOff = (t0 - 0.5f) * 0.9f * (f.P.Variant == 1 ? 0.2f : 1f);
            for (int i = 0; i < _pts.Length; i++)
            {
                float t = (float)i / (_pts.Length - 1), x = -half + t * half * 2;
                float env = MathF.Sin(t * MathF.PI);
                float y = MathF.Sin(t * freq * MathF.Tau * 0.5f + _phase[l]) * amp * env
                          + MathF.Sin(t * freq * 2.3f + _phase[l] * 1.7f) * amp * 0.35f * env;
                _pts[i] = new Vector3(x, yOff + y, 0);
            }
            float lw = (float)f.P.LineWidth;
            var col = f.Pal(t0);
            c.Polyline(_pts, lw * 3.5f, new Vector4(col.X, col.Y, col.Z, 0.1f + band * 0.15f));
            c.Polyline(_pts, lw * 0.9f, new Vector4(col.X, col.Y, col.Z, 0.55f + band * 0.45f));
        }
    }
}
