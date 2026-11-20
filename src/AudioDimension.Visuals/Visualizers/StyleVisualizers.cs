using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Particles;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Retro-futuristic scrolling grid with a striped sun; variant 1 raises spectrum mountains along the sides.</summary>
public sealed class CyberGrid : Visualizer
{
    public override string Id => "cyber-grid";
    public override string Name => "Cyber Grid";
    public override string Description => "A neon grid racing toward a striped sun; bass pulses the sun and the side terrain rises with the spectrum.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 9, Distance = 7, Fov = 65, AutoRotateSpeed = 0 };
    private const int Rows = 23, Cols = 21;
    private readonly float[,] _h = new float[Rows, Cols];
    private float _off;

    public override void Reset() { Array.Clear(_h); _off = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        const float floorY = -1.3f, step = 2f;
        float before = _off;
        _off += (3f + f.Level * 16f + f.Bass * 8f) * f.Dt;
        if (MathF.Floor(_off / step) > MathF.Floor(before / step))
        {
            for (int r = Rows - 1; r > 0; r--) for (int i = 0; i < Cols; i++) _h[r, i] = _h[r - 1, i];
            for (int i = 0; i < Cols; i++)
            {
                float x = Math.Abs(i - Cols / 2) * step;
                _h[0, i] = f.P.Variant == 1 && x >= 4 ? f.Spec01((x - 4) / 16f) * 3.2f * (float)f.P.Scale : 0f;
            }
        }
        float frac = (_off % step) / step, lw = (float)f.P.LineWidth * 0.55f;
        Vector3 P(int r, int i) => new((i - Cols / 2) * step, floorY + _h[r, i], -44f + (r + frac) * step);
        for (int r = 0; r < Rows; r++)
        {
            float a = Math.Clamp((r + 1f) / Rows, 0.05f, 1f);
            for (int i = 0; i < Cols - 1; i++) c.Line(P(r, i), P(r, i + 1), lw, f.Pal(0.15f, a * (0.5f + f.Level * 0.5f)));
            if (r < Rows - 1)
                for (int i = 0; i < Cols; i++) c.Line(P(r, i), P(r + 1, i), lw, f.Pal(0.55f, a * 0.8f));
        }
        // striped sun
        float sunR = (6.5f + f.Bass * 2.2f) * (float)f.P.Scale; var sc = new Vector3(0, floorY + sunR * 0.55f, -46f);
        int bands = 14;
        for (int b = 0; b < bands; b++)
        {
            float t0 = (float)b / bands, t1 = (float)(b + 1) / bands;
            if (b < 7 && ((b + (int)(f.Time * 2)) % 2 == 0)) continue;       // moving cut-outs near the bottom
            float y0 = -sunR + t0 * 2 * sunR, y1 = -sunR + t1 * 2 * sunR;
            if (y1 < -sunR * 0.1f) { /* lower half retained with stripes */ }
            float w0 = MathF.Sqrt(Math.Max(0, sunR * sunR - y0 * y0)), w1 = MathF.Sqrt(Math.Max(0, sunR * sunR - y1 * y1));
            var cb = f.Pal(1f - t0 * 0.6f, 0.9f); var ct = f.Pal(1f - t1 * 0.6f, 0.9f);
            c.GlowQuad(sc + new Vector3(-w0, y0, 0), sc + new Vector3(w0, y0, 0), sc + new Vector3(w1, y1, 0), sc + new Vector3(-w1, y1, 0), cb, cb, ct, ct);
        }
    }
}

/// <summary>Columns of falling light. Variant 0 rain, 1 fast diagonal storm with bolts, 2 soft drops.</summary>
public sealed class DigitalRain : Visualizer
{
    public override string Id => "digital-rain";
    public override string Name => "Digital Rain";
    public override string Description => "Streams of light fall faster in the frequency bands that are active.";
    private const int Max = 160;
    private readonly float[] _head = new float[Max], _speed = new float[Max], _len = new float[Max];
    private Random _rng = new(7007);
    private bool _init;
    private float _flash;

    public override void Reset() { _init = false; _rng = new Random(7007); _flash = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = f.P.Segments > 0 ? Math.Min(f.P.Segments, Max) : (f.P.Variant == 2 ? 150 : 72);
        if (!_init)
        {
            for (int i = 0; i < Max; i++) { _head[i] = (float)_rng.NextDouble() * 2.4f - 1.2f; _speed[i] = 0.35f + (float)_rng.NextDouble() * 0.6f; _len[i] = 0.25f + (float)_rng.NextDouble() * 0.6f; }
            _init = true;
        }
        bool storm = f.P.Variant == 1, drops = f.P.Variant == 2;
        if (f.Beat) _flash = 1f; _flash = Math.Max(0, _flash - f.Dt * 4f);
        float half = f.Aspect * (storm ? 1.5f : 1.05f), lw = (float)f.P.LineWidth * (drops ? 0.5f : 0.75f);
        c.PushModel(storm ? Matrix4.CreateRotationZ(0.35f) : Matrix4.Identity);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1), v = f.Spec01(t);
            float speed = _speed[i] * (storm ? 2.2f : 1f) * (0.4f + v * 2.5f + f.Level * 1.2f) * (float)f.P.Scale;
            _head[i] -= speed * f.Dt;
            if (_head[i] < -1.5f) { _head[i] = 1.5f + (float)_rng.NextDouble() * 0.6f; _len[i] = 0.25f + (float)_rng.NextDouble() * 0.6f; }
            float x = -half + t * half * 2f;
            float len = _len[i] * (0.4f + v * 1.4f);
            var head = new Vector3(x, _head[i], 0);
            var col = f.Pal(t);
            if (drops) { c.Point(head, 8f + v * 10f, new Vector4(col.X, col.Y, col.Z, 0.6f + v)); c.Line(head, head + new Vector3(0, len * 0.35f, 0), lw, new Vector4(col.X, col.Y, col.Z, 0.5f), new Vector4(col.X, col.Y, col.Z, 0f)); continue; }
            c.Line(head, head + new Vector3(0, len, 0), lw, new Vector4(col.X, col.Y, col.Z, 1f), new Vector4(col.X, col.Y, col.Z, 0f));
            c.Point(head, 6f + v * 8f, new Vector4(1, 1, 1, 0.5f + v));
        }
        c.PopModel();
        if (storm && _flash > 0.05f)
        {
            float bx = ((float)_rng.NextDouble() * 2 - 1) * f.Aspect * 0.8f;
            Vector3 p = new(bx, 1f, 0);
            for (int k = 0; k < 8; k++) { var q = p + new Vector3(((float)_rng.NextDouble() - 0.5f) * 0.25f, -0.26f, 0); c.Line(p, q, lw * 1.5f, f.Pal(0.9f, _flash)); p = q; }
        }
    }
}

/// <summary>Spiralling particle flows. Variant 0 black hole, 1 magnetic dipole streams, 2 tornado.</summary>
public sealed class Vortex : Visualizer
{
    public override string Id => "vortex";
    public override string Name => "Vortex";
    public override string Description => "Particles spiral inward, along magnetic field lines or up a tornado; energy speeds the flow.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 32, Distance = 7, AutoRotateSpeed = 3 };
    private float[] _u = Array.Empty<float>(), _a = Array.Empty<float>(), _k = Array.Empty<float>();
    private int _built;

    public override void Reset() => _built = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = Math.Min(f.ParticleBudget, 12000);
        if (n != _built) Build(n);
        float flow = (0.08f + f.Level * 0.5f + f.Bass * 0.25f) * f.Dt, s = (float)f.P.Scale;
        int variant = f.P.Variant;
        for (int i = 0; i < n; i++)
        {
            _u[i] -= flow * (0.5f + _k[i]);
            if (_u[i] < 0) _u[i] += 1f;
            float u = _u[i], v = f.Spec01(u), a = _a[i];
            Vector3 p; float bright;
            if (variant == 0)
            {
                float r = 0.35f + 5.8f * u * u;
                float ang = a + f.Time * (1.2f / (r * 0.5f + 0.3f));
                p = new Vector3(MathF.Cos(ang) * r, (_k[i] - 0.5f) * 0.25f * r * 0.4f, MathF.Sin(ang) * r) * s;
                bright = (1f - u) * 1.1f;
            }
            else if (variant == 1)
            {
                float l = 1.3f + _k[i] * 3.2f, th = 0.18f + u * (MathF.PI - 0.36f);
                float r = l * MathF.Sin(th) * MathF.Sin(th) * 1.4f;
                float ang = a + f.Time * 0.25f;
                p = new Vector3(r * MathF.Sin(th) * MathF.Cos(ang), r * MathF.Cos(th) * 1.6f, r * MathF.Sin(th) * MathF.Sin(ang)) * s;
                bright = 0.4f + v;
            }
            else
            {
                float h = (1f - u) * 6f - 2f, r = (0.35f + (1f - u) * 2.4f) * (1f + v * 0.6f);
                float ang = a + f.Time * (3f - (1f - u) * 1.5f);
                p = new Vector3(MathF.Cos(ang) * r, h, MathF.Sin(ang) * r) * s;
                bright = 0.3f + u * 0.9f;
            }
            var col = f.Pal(variant == 0 ? 1f - u : u); col.W = Math.Clamp(bright * (0.4f + v), 0.05f, 1f);
            c.Point(p, 2f + v * 4f, col);
        }
        if (variant == 0)
        {
            c.SolidSphere(Vector3.Zero, 0.34f * s, new Vector4(0, 0, 0, 1f), 16);
            c.Ring(Vector3.Zero, 0.4f * s * (1 + f.Bass * 0.25f), (float)f.P.LineWidth * 1.4f, f.Pal(0.9f, 0.9f), 64);
        }
    }

    private void Build(int n)
    {
        var rng = new Random(8008);
        _u = new float[n]; _a = new float[n]; _k = new float[n]; _built = n;
        for (int i = 0; i < n; i++) { _u[i] = (float)rng.NextDouble(); _a[i] = (float)rng.NextDouble() * MathF.Tau; _k[i] = (float)rng.NextDouble(); }
    }
}

/// <summary>Drifting glow points. Variant 0 fireflies, 1 floating dust, 2 soft orbs, 3 dense orbiting universe.</summary>
public sealed class Fireflies : Visualizer
{
    public override string Id => "fireflies";
    public override string Name => "Fireflies";
    public override string Description => "Wandering lights that glow brighter with the music.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 10, Distance = 11, AutoRotateSpeed = 2 };
    private Vector3[] _base = Array.Empty<Vector3>();
    private float[] _ph = Array.Empty<float>(), _w = Array.Empty<float>();
    private int _built, _builtVariant = -1;

    public override void Reset() => _built = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        int variant = Math.Clamp(f.P.Variant, 0, 3);
        float scale = Math.Clamp(f.ParticleBudget / 20000f, 0.25f, 3f);
        int n = variant switch { 0 => (int)(260 * scale), 1 => (int)(2600 * scale), 2 => 64, _ => (int)(5500 * scale) };
        if (n != _built || variant != _builtVariant) Build(n, variant);
        float t = f.Time, s = (float)f.P.Scale;
        for (int i = 0; i < n; i++)
        {
            float ph = _ph[i], w = _w[i], v = f.Spec01((float)i / n);
            Vector3 p; float size, alpha;
            if (variant == 3)
            {
                float r = _base[i].X, ang = _base[i].Z + t * (0.6f / (0.4f + r * 0.5f));
                p = new Vector3(MathF.Cos(ang) * r, _base[i].Y + MathF.Sin(t * w + ph) * 0.15f, MathF.Sin(ang) * r) * s;
                size = 1.8f + v * 3.5f; alpha = 0.25f + v * 0.9f;
            }
            else
            {
                float amp = variant == 1 ? 0.5f : 1.4f, sp = variant == 1 ? 0.12f : 0.35f;
                p = (_base[i] + new Vector3(MathF.Sin(t * sp * w + ph) * amp, MathF.Sin(t * sp * w * 0.8f + ph * 2) * amp, MathF.Sin(t * sp * w * 1.2f + ph * 3) * amp)) * s;
                if (variant == 0) { size = 9f + v * 9f; alpha = (0.55f + 0.45f * MathF.Sin(t * w * 1.5f + ph * 5)) * (0.7f + v + f.Level * 0.6f); }
                else if (variant == 1) { size = 1.8f + v * 2f; alpha = 0.25f + f.Level * 0.8f + v * 0.5f; }
                else { size = (45f + 55f * ((ph / MathF.Tau) % 1f)) * (1f + f.Bass * 0.5f); alpha = 0.06f + v * 0.22f + f.Level * 0.06f; }
            }
            var col = f.Pal(((ph / MathF.Tau) % 1f)); col.W = Math.Clamp(alpha, 0, 1);
            c.Point(p, size, col);
        }
    }

    private void Build(int n, int variant)
    {
        var rng = new Random(9009 + variant);
        _base = new Vector3[n]; _ph = new float[n]; _w = new float[n]; _built = n; _builtVariant = variant;
        for (int i = 0; i < n; i++)
        {
            _ph[i] = (float)rng.NextDouble() * MathF.Tau; _w[i] = 0.6f + (float)rng.NextDouble() * 1.6f;
            if (variant == 3) _base[i] = new Vector3(0.5f + MathF.Pow((float)rng.NextDouble(), 0.8f) * 5.5f, VizMath.Gauss(rng) * 0.35f, (float)rng.NextDouble() * MathF.Tau);
            else _base[i] = new Vector3(VizMath.Gauss(rng) * 3.2f, VizMath.Gauss(rng) * 1.8f, VizMath.Gauss(rng) * 3.2f);
        }
    }
}

/// <summary>Smooth filled spectrum curve. Variant 0 liquid, 1 glass bars with reflection, 2 elegant mirrored line.</summary>
public sealed class LiquidSpectrum : Visualizer
{
    public override string Id => "liquid-spectrum";
    public override string Name => "Liquid Spectrum";
    public override string Description => "A smooth, flowing spectrum curve with a soft reflection.";
    private readonly float[] _s = new float[128];
    private readonly Vector3[] _pts = new Vector3[128], _mir = new Vector3[128];

    public override void Reset() => Array.Clear(_s);

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = f.P.Variant == 1 ? Math.Clamp(f.P.Segments > 0 ? f.P.Segments : 32, 8, 96) : 96;
        float half = f.Aspect * 0.92f, baseY = f.P.Variant == 2 ? 0f : -0.1f, amp = 0.75f * (float)f.P.Scale;
        float k = Math.Min(1f, f.Dt * 16f), lw = (float)f.P.LineWidth;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1), tm = Math.Abs(2 * t - 1);       // bass in the centre, treble at the edges
            _s[i] += (f.Spec01(tm) - _s[i]) * k;
        }
        var pts = _pts; var mir = _mir;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1), x = -half + t * half * 2, h = 0.015f + _s[i] * amp;
            pts[i] = new Vector3(x, baseY + h, 0); mir[i] = new Vector3(x, baseY - h * (f.P.Variant == 2 ? 1f : 0.55f), 0);
        }
        if (f.P.Variant == 1)
        {
            float bw = half * 2f / n * 0.72f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1), x = pts[i].X - bw / 2, h = pts[i].Y - baseY;
                c.GlowQuad(new(x, baseY, 0), new(x + bw, baseY, 0), new(x + bw, baseY + h, 0), new(x, baseY + h, 0), f.Pal(t, 0.08f), f.Pal(t, 0.08f), f.Pal(t, 0.32f), f.Pal(t, 0.32f));
                c.Line(new(x, baseY + h, 0), new(x + bw, baseY + h, 0), lw, f.Pal(t, 1f));
                c.Line(new(x, baseY, 0), new(x, baseY + h, 0), lw * 0.4f, f.Pal(t, 0.5f));
                c.Line(new(x + bw, baseY, 0), new(x + bw, baseY + h, 0), lw * 0.4f, f.Pal(t, 0.5f));
                c.GlowQuad(new(x, baseY, 0), new(x + bw, baseY, 0), new(x + bw, baseY - h * 0.5f, 0), new(x, baseY - h * 0.5f, 0), f.Pal(t, 0.14f), f.Pal(t, 0.14f), f.Pal(t, 0f), f.Pal(t, 0f));
            }
            return;
        }
        if (f.P.Variant == 0)
            for (int i = 0; i < n - 1; i++)
            {
                float t0 = (float)i / (n - 1), t1 = (float)(i + 1) / (n - 1);
                c.GlowQuad(new(pts[i].X, baseY, 0), new(pts[i + 1].X, baseY, 0), pts[i + 1], pts[i], f.Pal(t0, 0.04f), f.Pal(t1, 0.04f), f.Pal(t1, 0.5f), f.Pal(t0, 0.5f));
                c.GlowQuad(new(mir[i].X, baseY, 0), new(mir[i + 1].X, baseY, 0), mir[i + 1], mir[i], f.Pal(t0, 0.03f), f.Pal(t1, 0.03f), f.Pal(t1, 0.2f), f.Pal(t0, 0.2f));
            }
        c.Polyline(pts.AsSpan(0, n), lw * 3.5f, t => f.Pal(t, 0.14f)); c.Polyline(pts.AsSpan(0, n), lw, t => f.Pal(t, 1f));
        c.Polyline(mir.AsSpan(0, n), lw * (f.P.Variant == 2 ? 1f : 0.6f), t => f.Pal(t, f.P.Variant == 2 ? 1f : 0.4f));
        if (f.P.Variant == 2) for (int i = 2; i < n - 2; i += 4) { c.Point(pts[i], 5f + _s[i] * 8f, f.Pal((float)i / n, 0.9f)); c.Point(mir[i], 5f + _s[i] * 8f, f.Pal((float)i / n, 0.9f)); }
    }
}
