using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Particles;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Waveform history swept into a ribbon (variant 0) or a tunnel of rings (variant 1).</summary>
public sealed class AudioRibbon : Visualizer
{
    public override string Id => "audio-ribbon";
    public override string Name => "Audio Ribbon";
    public override string Description => "Recent waveform slices form a flowing ribbon, or wrap into a tunnel you fly through.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 12, Pitch = 20, Distance = 9, AutoRotateSpeed = 4 };
    private const int Rows = 60, Pts = 32;
    private readonly float[,] _h = new float[Rows, Pts];
    private readonly Vector3[] _ring = new Vector3[Pts], _prev = new Vector3[Pts];
    private float _acc;

    public override void Reset() { Array.Clear(_h); _acc = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        _acc += f.Dt;
        if (_acc > 0.03f)
        {
            _acc = 0;
            for (int r = Rows - 1; r > 0; r--) for (int i = 0; i < Pts; i++) _h[r, i] = _h[r - 1, i];
            var w = f.Audio.Waveform;
            for (int i = 0; i < Pts; i++) _h[0, i] = w[Math.Min(i * 16, w.Length - 1)] * (float)f.P.Reactivity;
        }
        float lw = (float)f.P.LineWidth * 0.6f, s = (float)f.P.Scale;
        if (f.P.Variant == 1)
        {
            for (int r = 0; r < Rows; r++)
            {
                float z = 2f - r * 0.55f, fade = 1f - r / (float)Rows;
                float rad = (2.4f + MathF.Sin(r * 0.1f + f.Time) * 0.25f) * s;
                for (int i = 0; i < Pts; i++)
                {
                    float a = MathF.Tau * i / Pts + z * 0.05f, rr = rad + _h[r, i] * 1.1f * s;
                    _ring[i] = new Vector3(MathF.Cos(a) * rr, MathF.Sin(a) * rr, z);
                }
                var col = f.Pal(r / (float)Rows, fade);
                c.Polyline(_ring, lw, col, true);
                if (r > 0) for (int i = 0; i < Pts; i += 2) c.Line(_prev[i], _ring[i], lw * 0.5f, new Vector4(col.X, col.Y, col.Z, col.W * 0.5f));
                Array.Copy(_ring, _prev, Pts);
            }
            return;
        }
        for (int r = 0; r < Rows - 1; r++)
        {
            float z0 = 2.5f - r * 0.27f, z1 = z0 - 0.27f, x0 = MathF.Sin(r * 0.12f + f.Time) * 0.8f, x1 = MathF.Sin((r + 1) * 0.12f + f.Time) * 0.8f;
            float fade = 1f - r / (float)Rows;
            for (int i = 0; i < Pts - 1; i++)
            {
                float xa = (i / (float)(Pts - 1) - 0.5f) * 8f * s, xb = ((i + 1) / (float)(Pts - 1) - 0.5f) * 8f * s;
                Vector3 a = new(xa + x0, _h[r, i] * 1.8f * s, z0), b = new(xb + x0, _h[r, i + 1] * 1.8f * s, z0);
                Vector3 cc = new(xb + x1, _h[r + 1, i + 1] * 1.8f * s, z1), d = new(xa + x1, _h[r + 1, i] * 1.8f * s, z1);
                float t = i / (float)(Pts - 1);
                c.GlowQuad(a, b, cc, d, f.Pal(t, fade * 0.12f), f.Pal(t, fade * 0.12f), f.Pal(t, fade * 0.08f), f.Pal(t, fade * 0.08f));
                if (r % 2 == 0) c.Line(a, b, lw, f.Pal(t, fade));
            }
        }
    }
}

/// <summary>Heightfield sea driven by the six bands, with ripples launched on beats.</summary>
public sealed class FrequencyOcean : Visualizer
{
    public override string Id => "frequency-ocean";
    public override string Name => "Frequency Ocean";
    public override string Description => "A lit ocean surface; each frequency band raises its own wave train and beats send ripples outward.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 20, Pitch = 28, Distance = 14, AutoRotateSpeed = 3 };
    private const int N = 40;
    private readonly float[,] _hh = new float[N + 1, N + 1];
    private float _ringR = 99, _ringS;
    private static readonly float[] Fr = { 0.35f, 0.5f, 0.8f, 1.2f, 1.8f, 2.6f }, Am = { 1.0f, 0.85f, 0.6f, 0.45f, 0.32f, 0.22f };

    public override void Reset() { _ringR = 99; _ringS = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        if (f.Beat) { _ringR = 0; _ringS = 0.8f + f.Audio.BeatStrength; }
        _ringR += f.Dt * 5f; _ringS = Math.Max(0, _ringS - f.Dt * 0.5f);
        float span = 7.5f * (float)f.P.Scale, step = span * 2 / N, t = f.Time;
        bool peaks = f.P.Variant == 1;
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
            {
                float x = -span + i * step, z = -span + j * step, h = 0;
                for (int k = 0; k < 6; k++)
                {
                    float th = k * 1.1f, w = MathF.Sin(Fr[k] * (x * MathF.Cos(th) + z * MathF.Sin(th)) + t * (0.8f + k * 0.35f));
                    h += (peaks ? 1f - MathF.Abs(w) : w) * Am[k] * (0.08f + f.Band(k) * 1.5f);
                }
                float r = MathF.Sqrt(x * x + z * z);
                h += _ringS * 1.3f * MathF.Exp(-(r - _ringR) * (r - _ringR) / 1.2f);
                _hh[i, j] = h * 0.8f;
            }
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                Vector3 P(int a, int b) => new(-span + a * step, _hh[a, b], -span + b * step);
                float hv = Math.Clamp((_hh[i, j] + 1.2f) / 3f, 0, 1);
                var col = f.Pal(hv); col.W = 1f;
                var shade = col * new Vector4(1.15f, 1.15f, 1.15f, 1f);
                c.SolidQuad(P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1), shade);
                if (hv > 0.72f) c.Point(P(i, j) + new Vector3(0, 0.05f, 0), 4f, f.Pal(hv, 0.5f));
            }
    }
}

/// <summary>Node network (variant 0) or dipole magnetic field lines (variant 1).</summary>
public sealed class ElectricWeb : Visualizer
{
    public override string Id => "electric-web";
    public override string Name => "Electric Web";
    public override string Description => "Nodes link up when energy rises, or field lines stream between two poles.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 15, Distance = 10, AutoRotateSpeed = 6 };
    private const int Nodes = 52;
    private readonly Vector3[] _base = new Vector3[Nodes], _cur = new Vector3[Nodes];
    private readonly float[] _ph = new float[Nodes];
    private bool _init;
    private float _flash;
    private readonly Vector3[] _line = new Vector3[40];

    public override void Reset() { _init = false; _flash = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        if (!_init)
        {
            var rng = new Random(1212);
            for (int i = 0; i < Nodes; i++) { _base[i] = VizMath.OnSphere(rng) * (2f + (float)rng.NextDouble() * 1.8f); _ph[i] = (float)rng.NextDouble() * MathF.Tau; }
            _init = true;
        }
        if (f.Beat) _flash = 1f; _flash = Math.Max(0, _flash - f.Dt * 3f);
        float s = (float)f.P.Scale, lw = (float)f.P.LineWidth * 0.5f;
        if (f.P.Variant == 1)
        {
            float[] ls = { 1.2f, 2.0f, 3.0f, 4.2f };
            for (int li = 0; li < ls.Length; li++)
            {
                float band = f.Band(li + 1);
                for (int k = 0; k < 10; k++)
                {
                    float phi = MathF.Tau * k / 10 + f.Time * 0.2f;
                    for (int j = 0; j < _line.Length; j++)
                    {
                        float th = 0.18f + (MathF.PI - 0.36f) * j / (_line.Length - 1), r = ls[li] * MathF.Sin(th) * MathF.Sin(th) * 1.5f * s;
                        _line[j] = new Vector3(r * MathF.Sin(th) * MathF.Cos(phi), r * MathF.Cos(th) * 1.7f, r * MathF.Sin(th) * MathF.Sin(phi));
                    }
                    c.Polyline(_line, lw * (1f + band * 3f), f.Pal(li / 3f, 0.25f + band * 0.6f));
                    float u = (f.Time * (0.2f + f.Level * 0.5f) + k * 0.1f + li * 0.07f) % 1f; int idx = Math.Min((int)(u * (_line.Length - 1)), _line.Length - 1);
                    c.Point(_line[idx], 6f + band * 10f, f.Pal(li / 3f + 0.2f, 1f));
                }
            }
            c.Point(new Vector3(0, 0, 0), 30f + f.Bass * 60f, f.Pal(0.5f, 0.35f));
            return;
        }
        float ang = f.Time * 0.15f; var rot = Matrix4.CreateRotationY(ang);
        for (int i = 0; i < Nodes; i++)
        {
            float v = f.Spec01((float)i / Nodes);
            var b = _base[i] * (1f + v * 0.45f) * s;
            b += new Vector3(MathF.Sin(f.Time * 0.7f + _ph[i]), MathF.Sin(f.Time * 0.5f + _ph[i] * 2), MathF.Sin(f.Time * 0.9f + _ph[i] * 3)) * 0.25f;
            _cur[i] = Vector3.TransformPosition(b, rot);
            c.Point(_cur[i], 6f + v * 12f, f.Pal((float)i / Nodes, 0.9f));
        }
        float thr = (1.7f + f.Level * 2.2f) * s;
        for (int i = 0; i < Nodes; i++)
            for (int j = i + 1; j < Nodes; j++)
            {
                float d = (_cur[i] - _cur[j]).Length;
                if (d > thr) continue;
                float a = (1f - d / thr) * (0.35f + f.Bass * 0.6f + _flash * 0.6f);
                c.Line(_cur[i], _cur[j], lw, f.Pal(d / thr, a));
            }
    }
}

/// <summary>Double helix whose radius swells with the spectrum along its length.</summary>
public sealed class DnaHelix : Visualizer
{
    public override string Id => "dna-helix";
    public override string Name => "Digital DNA";
    public override string Description => "Twin strands twist faster with loudness; rungs and radius follow the spectrum along the helix.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 5, Distance = 10, AutoRotateSpeed = 0 };
    private const int N = 64;
    private readonly Vector3[] _a = new Vector3[N], _b = new Vector3[N];
    private float _twist;

    public override void Reset() => _twist = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        _twist += f.Dt * (0.8f + f.Level * 2.5f);
        float s = (float)f.P.Scale, lw = (float)f.P.LineWidth;
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1), y = (t - 0.5f) * 8f * s, v = f.Spec01(t), a = y * 1.5f + _twist, r = (0.9f + v * 1.1f) * s;
            _a[i] = new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r); _b[i] = new Vector3(-MathF.Cos(a) * r, y, -MathF.Sin(a) * r);
        }
        c.PushModel(Matrix4.CreateRotationZ(0.35f));
        c.Polyline(_a, lw * 1.4f, t => f.Pal(t * 0.4f, 0.9f)); c.Polyline(_b, lw * 1.4f, t => f.Pal(0.6f + t * 0.4f, 0.9f));
        for (int i = 0; i < N; i++)
        {
            float t = (float)i / (N - 1), v = f.Spec01(t);
            c.Point(_a[i], 6f + v * 10f, f.Pal(0.1f, 1f)); c.Point(_b[i], 6f + v * 10f, f.Pal(0.9f, 1f));
            if (i % 2 == 0) c.Line(_a[i], _b[i], lw * 0.55f, f.Pal(t, 0.25f + v * 0.75f));
        }
        c.PopModel();
    }
}

/// <summary>Faceted crystal sphere. Variant 0 solid crystal, 1 energy core with long shards, 2 holographic wireframe.</summary>
public sealed class CrystalCore : Visualizer
{
    public override string Id => "crystal-core";
    public override string Name => "Crystal Core";
    public override string Description => "An icosphere whose facets extrude into crystals driven by the spectrum.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 14, Distance = 9, AutoRotateSpeed = 8 };

    private static (Vector3[] V, int[] F) _ico2, _ico1;
    private float[] _disp = Array.Empty<float>();

    private static (Vector3[], int[]) Build(int subdiv)
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new List<Vector3> { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
        var f = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        for (int s = 0; s < subdiv; s++)
        {
            var cache = new Dictionary<long, int>(); var nf = new List<int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).Normalized()); return cache[key] = v.Count - 1;
            }
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        return (v.ToArray(), f.ToArray());
    }

    public override void Draw(Canvas c, FrameInput f)
    {
        int variant = Math.Clamp(f.P.Variant, 0, 2);
        var (V, F) = variant == 2 ? (_ico1 = _ico1.V is null ? Build(1) : _ico1) : (_ico2 = _ico2.V is null ? Build(2) : _ico2);
        int faces = F.Length / 3;
        if (_disp.Length < faces) _disp = new float[faces];
        float s = 1.9f * (float)f.P.Scale * (1f + f.Bass * 0.12f), ext = variant == 1 ? 1.6f : variant == 2 ? 0.45f : 0.7f;
        for (int fi = 0; fi < faces; fi++)
        {
            Vector3 a = V[F[fi * 3]], b = V[F[fi * 3 + 1]], cc = V[F[fi * 3 + 2]], cen = ((a + b + cc) / 3f);
            float u = MathF.Atan2(cen.Z, cen.X) / MathF.Tau + 0.5f;
            float v = f.Spec01(0.5f * (cen.Y * 0.5f + 0.5f) + 0.5f * Math.Abs(2 * u - 1));
            _disp[fi] += (v - _disp[fi]) * Math.Min(1f, f.Dt * 14f);
            float h = _disp[fi];
            Vector3 apex = cen.Normalized() * s * (1f + h * ext * (float)f.P.Reactivity);
            Vector3 pa = a * s, pb = b * s, pc = cc * s;
            var col = f.Pal(Math.Clamp(h * 1.2f, 0, 1));
            if (variant == 2)
            {
                var lc = new Vector4(col.X, col.Y, col.Z, 0.35f + h * 0.65f); float lw = (float)f.P.LineWidth * 0.5f;
                c.Line(pa, pb, lw, lc); c.Line(pb, pc, lw, lc); c.Line(pc, pa, lw, lc);
                c.Line(apex, pa, lw * 0.6f, lc * 0.6f); c.Line(apex, pb, lw * 0.6f, lc * 0.6f); c.Line(apex, pc, lw * 0.6f, lc * 0.6f);
            }
            else
            {
                col.W = 1f;
                c.SolidTri(pa, pb, apex, col); c.SolidTri(pb, pc, apex, col); c.SolidTri(pc, pa, apex, col);
            }
        }
        if (variant == 1)
        {
            c.SolidSphere(Vector3.Zero, 0.7f * (1f + f.Bass * 0.5f), f.Pal(0.95f, 1f), 18);
            c.Point(Vector3.Zero, 220f * (0.3f + f.Bass * 0.6f), f.Pal(0.8f, 0.5f));
        }
        if (variant == 2)
        {
            float y = MathF.Sin(f.Time * 0.9f) * s;
            float rr = MathF.Sqrt(Math.Max(0.01f, s * s - y * y)) * 1.3f;
            c.PushModel(Matrix4.CreateRotationX(MathF.PI / 2) * Matrix4.CreateTranslation(0, y, 0));
            c.Ring(Vector3.Zero, rr, (float)f.P.LineWidth, f.Pal(0.9f, 0.8f), 64);
            c.PopModel();
        }
    }
}
