using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Particles;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Concentric band rings plus expanding shockwaves on every beat.</summary>
public sealed class ReactiveRings : Visualizer
{
    public override string Id => "reactive-rings";
    public override string Name => "Audio-Reactive Rings";
    public override string Description => "Six rings, one per frequency band, plus shockwaves launched by each beat.";
    private readonly float[] _wave = new float[24];
    private readonly float[] _waveStr = new float[24];
    private int _next;

    public override void Reset() { Array.Clear(_wave); Array.Clear(_waveStr); _next = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        float lw = (float)f.P.LineWidth, s = (float)f.P.Scale;
        bool calm = f.P.Variant == 1;
        if (f.Beat && !calm) { _wave[_next] = 0.05f; _waveStr[_next] = 0.4f + f.Audio.BeatStrength; _next = (_next + 1) % _wave.Length; }
        for (int i = 0; i < _wave.Length; i++)
        {
            if (_waveStr[i] <= 0) continue;
            _wave[i] += f.Dt * 0.9f; _waveStr[i] -= f.Dt * 0.55f;
            if (_waveStr[i] > 0) c.Ring(Vector3.Zero, _wave[i] * s, lw * (1 + _waveStr[i]), f.Pal(0.9f, Math.Clamp(_waveStr[i], 0, 1)), 96);
        }
        for (int b = 0; b < 6; b++)
        {
            float v = f.Band(b);
            float r = (calm ? 0.2f + b * 0.15f + v * 0.04f : 0.14f + b * 0.11f + v * 0.07f) * s;
            c.PushModel(Matrix4.CreateRotationZ(f.Time * (calm ? 0.06f : 0.2f + b * 0.07f) * (b % 2 == 0 ? 1 : -1)));
            // dashed ring: arcs whose length follows the band
            int segs = 24; float arc = MathF.Tau / segs * (0.35f + 0.55f * Math.Min(1f, v + 0.2f));
            for (int k = 0; k < segs; k++)
            {
                float a0 = MathF.Tau * k / segs;
                int steps = 6; Vector3 prev = default;
                for (int j = 0; j <= steps; j++)
                {
                    float a = a0 + arc * j / steps;
                    var p = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, 0);
                    if (j > 0) c.Line(prev, p, lw * (1f + v * 2.2f), f.Pal(b / 5f, 0.35f + v * 0.65f));
                    prev = p;
                }
            }
            c.PopModel();
        }
    }
}

/// <summary>Polygonal tunnel; pulses travel toward the camera as new audio arrives at the far end.</summary>
public sealed class GeometricTunnel : Visualizer
{
    public override string Id => "geometric-tunnel";
    public override string Name => "Geometric Tunnel";
    public override string Description => "Twisting polygon frames rush past; each frame carries the loudness captured when it appeared.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 0, Distance = 1.5, Fov = 80, AutoRotateSpeed = 0 };
    private const int Slices = 26;
    private readonly float[] _amp = new float[Slices];
    private float _offset;
    private readonly Vector3[] _v = new Vector3[16];
    private readonly Vector3[] _vPrev = new Vector3[16];

    public override void Reset() { Array.Clear(_amp); _offset = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        int sides = f.P.Segments >= 3 ? Math.Min(f.P.Segments, 16) : (f.P.Variant == 1 ? 4 : f.P.Variant == 2 ? 8 : 6);
        const float step = 1.6f;
        float before = _offset;
        _offset += (2.5f + f.Level * 14f + f.Bass * 6f) * f.Dt;
        if (MathF.Floor(_offset / step) > MathF.Floor(before / step))
        {
            for (int i = Slices - 1; i > 0; i--) _amp[i] = _amp[i - 1];    // pulses travel toward the viewer
            _amp[0] = f.Level * 0.6f + f.Bass * 0.6f;
        }
        float frac = (_offset % step) / step;
        float lw = (float)f.P.LineWidth;
        for (int i = Slices - 1; i >= 0; i--)
        {
            float z = -(i + 1 - frac) * step + 0.6f;
            float depth = (float)i / Slices;
            float r = (2.6f + _amp[i] * 1.8f) * (float)f.P.Scale;
            float twist = z * 0.07f + f.Time * 0.15f;
            var col = f.Pal(depth, (1f - depth) * (0.4f + _amp[i]));
            for (int k = 0; k < sides; k++)
            {
                float a = twist + MathF.Tau * k / sides;
                _v[k] = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, z);
            }
            for (int k = 0; k < sides; k++) c.Line(_v[k], _v[(k + 1) % sides], lw * (1f + _amp[i] * 2f), col);
            if (i < Slices - 1)
                for (int k = 0; k < sides; k++) c.Line(_vPrev[k], _v[k], lw * 0.5f, new Vector4(col.X, col.Y, col.Z, col.W * 0.4f));
            Array.Copy(_v, _vPrev, sides);
        }
    }
}

/// <summary>Nested wireframe polyhedra that spin and swell with the bands.</summary>
public sealed class RotatingGeometry : Visualizer
{
    public override string Id => "rotating-geometry";
    public override string Name => "Rotating Geometric Structures";
    public override string Description => "Nested glowing polyhedra; each shell scales with its own frequency band.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 15, Distance = 9, AutoRotateSpeed = 0 };

    private static readonly Vector3[] CubeV = Enumerable.Range(0, 8).Select(i => new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1)).ToArray();
    private static readonly (int, int)[] CubeE = { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) };
    private static readonly Vector3[] OctV = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
    private static readonly (int, int)[] OctE = { (0, 2), (0, 3), (0, 4), (0, 5), (1, 2), (1, 3), (1, 4), (1, 5), (2, 4), (4, 3), (3, 5), (5, 2) };
    private static readonly Vector3[] IcoV = BuildIco();
    private static readonly (int, int)[] IcoE = BuildIcoEdges();

    private static Vector3[] BuildIco()
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new[] { new Vector3(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
        return v.Select(p => p.Normalized() * 1.4f).ToArray();
    }

    private static (int, int)[] BuildIcoEdges()
    {
        var e = new List<(int, int)>(); float min = float.MaxValue;
        for (int i = 0; i < IcoV.Length; i++) for (int j = i + 1; j < IcoV.Length; j++) min = Math.Min(min, (IcoV[i] - IcoV[j]).Length);
        for (int i = 0; i < IcoV.Length; i++) for (int j = i + 1; j < IcoV.Length; j++) if ((IcoV[i] - IcoV[j]).Length < min * 1.05f) e.Add((i, j));
        return e.ToArray();
    }

    public override void Draw(Canvas c, FrameInput f)
    {
        int layers = f.P.Segments > 0 ? Math.Min(f.P.Segments, 6) : 3;
        float lw = (float)f.P.LineWidth;
        for (int k = 0; k < layers; k++)
        {
            int shape = (k + f.P.Variant) % 3;
            var (verts, edges) = shape == 0 ? (CubeV, CubeE) : shape == 1 ? (OctV, OctE) : (IcoV, IcoE);
            float band = f.Band(k % 6);
            float norm = shape == 0 ? 0.62f : 1f;
            float scale = (0.6f + k * 0.55f) * (1f + band * 0.55f) * (float)f.P.Scale * norm;
            float dir = k % 2 == 0 ? 1 : -1;
            var m = Matrix4.CreateScale(scale) * Matrix4.CreateRotationX(f.Time * (0.35f + 0.12f * k) * dir) * Matrix4.CreateRotationY(f.Time * (0.5f + 0.1f * k)) * Matrix4.CreateRotationZ(f.Time * 0.15f * k);
            c.PushModel(m);
            var col = f.Pal(k / (float)Math.Max(1, layers - 1), 0.5f + band * 0.5f);
            foreach (var (a, b) in edges) c.Line(verts[a], verts[b], lw * (1f + band * 1.5f), col);
            foreach (var v in verts) c.Point(v, 8 + band * 14, f.Pal(k / (float)Math.Max(1, layers - 1), 0.9f));
            c.PopModel();
        }
        c.Point(Vector3.Zero, 70f * (0.3f + f.Bass * 0.8f), f.Pal(0.5f, 0.5f));
    }
}
