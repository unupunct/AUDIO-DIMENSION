using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Particles;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Bursts of particles fired on every detected beat.</summary>
public sealed class ParticleExplosion : Visualizer
{
    public override string Id => "particle-explosion";
    public override string Name => "Particle Explosion";
    public override string Description => "Each beat detonates a shell of particles; loudness feeds a constant ember stream.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 12, Distance = 11, AutoRotateSpeed = 5 };
    private readonly ParticleSystem _ps = new();
    private Random _rng = new(1001);
    private float _ember;

    public override void Reset() { _ps.Clear(); _rng = new Random(1001); _ember = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        _ps.SetCapacity(f.ParticleBudget);
        float speed = 3f + f.Bass * 5f;
        if (f.Beat)
        {
            int count = (int)(f.ParticleBudget * 0.04f * (0.4f + f.Audio.BeatStrength * 1.2f + f.Bass));
            for (int i = 0; i < count; i++)
            {
                var dir = VizMath.OnSphere(_rng);
                if (f.P.Variant == 1) dir = new Vector3(dir.X * 0.35f, MathF.Abs(dir.Y) + 0.6f, dir.Z * 0.35f).Normalized();      // fountain
                else if (f.P.Variant == 2) dir = new Vector3(dir.X, dir.Y * 0.06f, dir.Z).Normalized();                          // flat disc shockwave
                _ps.Spawn(Vector3.Zero, dir * speed * (0.4f + (float)_rng.NextDouble()), 1.6f + (float)_rng.NextDouble() * 1.8f,
                    3f + (float)_rng.NextDouble() * 6f * (float)f.P.Scale, (float)_rng.NextDouble());
            }
        }
        _ember += f.Level * f.Dt * f.ParticleBudget * 0.05f;
        while (_ember >= 1f)
        {
            _ember -= 1f;
            _ps.Spawn(VizMath.OnSphere(_rng) * 0.2f, VizMath.OnSphere(_rng) * (0.8f + f.Treble * 3f), 1.2f + (float)_rng.NextDouble(), 2.5f, (float)_rng.NextDouble());
        }
        _ps.Update(f.Dt, f.P.Variant == 1 ? 0.35f : 0.7f, new Vector3(0, f.P.Variant == 1 ? -4.5f : -0.4f, 0));
        _ps.Draw(c, t => f.Pal(t), 0.9f);
        float core = 0.15f + f.Bass * 0.5f;
        c.Point(Vector3.Zero, 90 * core * (float)f.P.Scale, f.Pal(0.1f, 0.6f));
        c.Point(Vector3.Zero, 40 * core * (float)f.P.Scale, f.Pal(0.5f, 1f));
    }
}

/// <summary>Rotating spiral galaxy; arms swell with the spectrum.</summary>
public sealed class ParticleGalaxy : Visualizer
{
    public override string Id => "particle-galaxy";
    public override string Name => "Particle Galaxy";
    public override string Description => "A spiral galaxy where each radius shines with its own frequency.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 38, Distance = 11, AutoRotateSpeed = 0 };
    private Vector3[] _base = Array.Empty<Vector3>();
    private float[] _rad = Array.Empty<float>();
    private int _builtFor, _builtArms;
    private float _spin;

    public override void Reset() { _builtFor = 0; _spin = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = Math.Min(f.ParticleBudget, 18000), arms = f.P.Segments > 0 ? f.P.Segments : 3;
        if (n != _builtFor || arms != _builtArms) Build(n, arms);
        _spin += f.Dt * (0.12f + f.Treble * 0.35f + (float)f.P.Rotation * MathF.PI / 180f);
        float pulse = 1f + f.Bass * 0.22f, s = (float)f.P.Scale;
        var rot = Matrix4.CreateRotationY(_spin);
        c.PushModel(rot);
        for (int i = 0; i < n; i++)
        {
            float rn = _rad[i];
            float v = f.Spec01(rn);
            var p = _base[i] * (pulse * s);
            p.Y *= 1f + f.Mid * 1.5f;
            var col = f.Pal(rn * 0.9f + v * 0.1f); col.W = 0.25f + v * 0.9f;
            c.Point(p, 2.2f + v * 5f, col);
        }
        c.PopModel();
        c.Point(Vector3.Zero, 110 * (0.3f + f.Bass * 0.5f) * s, f.Pal(0.05f, 0.5f));
    }

    private void Build(int n, int arms)
    {
        var rng = new Random(2002);
        _base = new Vector3[n]; _rad = new float[n]; _builtFor = n; _builtArms = arms;
        for (int i = 0; i < n; i++)
        {
            float rn = MathF.Pow((float)rng.NextDouble(), 0.75f), r = rn * 6f;
            float theta = rn * 7f + (i % arms) * MathF.Tau / arms + VizMath.Gauss(rng) * (0.12f + 0.25f * (1 - rn));
            float spread = VizMath.Gauss(rng) * 0.1f * (1.1f - rn);
            _base[i] = new Vector3(MathF.Cos(theta) * r, spread * 2f, MathF.Sin(theta) * r);
            _rad[i] = rn;
        }
    }
}

/// <summary>Fly through a tube of particles; speed and wall radius follow the music.</summary>
public sealed class ParticleTunnel : Visualizer
{
    public override string Id => "particle-tunnel";
    public override string Name => "Particle Tunnel";
    public override string Description => "Rush down a particle tube; the walls breathe with the spectrum.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 0, Distance = 1.5, Fov = 80, AutoRotateSpeed = 0 };
    private float[] _a = Array.Empty<float>(), _r = Array.Empty<float>(), _z = Array.Empty<float>();
    private int _built;

    public override void Reset() => _built = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = Math.Min(f.ParticleBudget, 14000);
        if (n != _built) Build(n);
        const float len = 42f;
        float speed = (3f + f.Level * 22f + f.Bass * 8f) * f.Dt;
        for (int i = 0; i < n; i++)
        {
            _z[i] += speed;
            if (_z[i] > 1.2f) _z[i] -= len;
            float depth = Math.Clamp(-_z[i] / len, 0, 1);
            float t = (_a[i] / MathF.Tau);
            float v = f.Spec01(t < 0.5f ? t * 2 : (1 - t) * 2);
            float r = _r[i] * (float)f.P.Scale * (1f + v * 0.5f);
            float a = _a[i] + _z[i] * 0.04f + f.Time * 0.1f;
            var col = f.Pal((depth * 0.7f + v * 0.3f) % 1f); col.W = (1f - depth) * (0.35f + v);
            c.Point(new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, _z[i]), 2.4f + v * 4f, col);
        }
    }

    private void Build(int n)
    {
        var rng = new Random(3003);
        _a = new float[n]; _r = new float[n]; _z = new float[n]; _built = n;
        for (int i = 0; i < n; i++) { _a[i] = (float)rng.NextDouble() * MathF.Tau; _r[i] = 2.2f + (float)rng.NextDouble() * 1.6f; _z[i] = -(float)rng.NextDouble() * 42f; }
    }
}

/// <summary>Particle sphere whose surface is displaced by the spectrum.</summary>
public sealed class ReactiveSphere : Visualizer
{
    public override string Id => "reactive-sphere";
    public override string Name => "Reactive Particle Sphere";
    public override string Description => "A Fibonacci sphere of particles pushed outward by frequency bands from pole to pole.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 15, Distance = 9, AutoRotateSpeed = 0 };
    private float _spin;
    private float _radius = 2f;

    public override void Reset() { _spin = 0; _radius = 2f; }

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = Math.Min(f.ParticleBudget, 9000);
        _spin += f.Dt * (0.25f + f.Mid * 0.5f + (float)f.P.Rotation * MathF.PI / 180f);
        _radius = VizMath.Smooth(_radius, 2f * (float)f.P.Scale * (1f + f.Bass * 0.25f), f.Dt, 14f);
        const float golden = 2.39996323f;
        c.PushModel(Matrix4.CreateRotationY(_spin) * Matrix4.CreateRotationX(0.35f));
        for (int i = 0; i < n; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / n, rr = MathF.Sqrt(1f - y * y), a = i * golden;
            float lat = MathF.Acos(y) / MathF.PI;
            float v = f.Spec01(f.P.Variant == 1 ? (a / MathF.Tau) % 1f : lat);
            float r = _radius * (1f + v * 0.9f);
            var col = f.Pal(v); col.W = 0.35f + v * 0.8f;
            c.Point(new Vector3(MathF.Cos(a) * rr, y, MathF.Sin(a) * rr) * r, 2f + v * 6f, col);
        }
        c.PopModel();
    }
}

/// <summary>Stars streaming toward the camera; bass stretches them into streaks.</summary>
public sealed class Starfield : Visualizer
{
    public override string Id => "starfield";
    public override string Name => "Starfield";
    public override string Description => "Fly through space; loudness is your throttle and bass stretches stars into light streaks.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 0, Distance = 1.5, Fov = 75, AutoRotateSpeed = 0 };
    private Vector3[] _s = Array.Empty<Vector3>();
    private float[] _h = Array.Empty<float>();
    private int _built;

    public override void Reset() => _built = 0;

    public override void Draw(Canvas c, FrameInput f)
    {
        int n = Math.Min(f.ParticleBudget, 8000);
        if (n != _built) Build(n);
        bool hyper = f.P.Variant == 1;
        float speed = (2.5f + f.Level * 30f + f.Bass * 12f) * f.Dt * (hyper ? 3.2f : 1f);
        float streak = (0.1f + f.Bass * 2.2f + f.Level * 0.8f) * (hyper ? 2.6f : 1f);
        for (int i = 0; i < n; i++)
        {
            _s[i].Z += speed;
            if (_s[i].Z > 1.4f) { _s[i].Z -= 62f; }
            float depth = Math.Clamp(-_s[i].Z / 60f, 0, 1);
            var col = f.Pal(_h[i]); col.W = (1f - depth) * 0.9f;
            col = Vector4.Lerp(col, new Vector4(1, 1, 1, col.W), 0.5f);
            c.Point(_s[i], 2.5f + (1 - depth) * 3f + f.Treble * 3f, col);
            if (streak > 0.5f && (hyper ? i % 3 == 0 : i % 4 == 0))
                c.Line(_s[i], _s[i] - new Vector3(0, 0, streak * (1 - depth)), 1.5f, new Vector4(col.X, col.Y, col.Z, 0f), new Vector4(col.X, col.Y, col.Z, col.W * (hyper ? 0.3f : 0.8f)));
        }
    }

    private void Build(int n)
    {
        var rng = new Random(4004);
        _s = new Vector3[n]; _h = new float[n]; _built = n;
        for (int i = 0; i < n; i++)
        {
            _s[i] = new Vector3((float)(rng.NextDouble() * 2 - 1) * 14f, (float)(rng.NextDouble() * 2 - 1) * 9f, -(float)rng.NextDouble() * 62f);
            _h[i] = (float)rng.NextDouble();
        }
    }
}

/// <summary>A planet with a particle ring, nebula backdrop and drifting stars.</summary>
public sealed class SpaceEnvironment : Visualizer
{
    public override string Id => "space-environment";
    public override string Name => "Audio-Reactive Space";
    public override string Description => "A nebula sky, a lit planet that swells with bass and an orbiting ring that sparkles with treble.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 20, Pitch = 14, Distance = 9, AutoRotateSpeed = 3 };
    private Vector3[] _ring = Array.Empty<Vector3>();
    private float[] _rh = Array.Empty<float>();
    private Vector3[] _stars = Array.Empty<Vector3>();
    private int _built;
    private float _spin, _planet = 1.5f;

    private const string Nebula = """
        void main(){
            vec2 p = (vUV - 0.5) * vec2(uAspect, 1.0) * 2.2;
            float t = uTime * 0.05;
            float n = fbm(p * 1.4 + vec2(t, -t * 0.7) + uAudio.x * 0.6);
            float m = fbm(p * 2.6 - vec2(t * 1.3, t) + 4.0);
            vec3 col = pal(n) * pow(n, 2.2) * 1.6 + pal(1.0 - m) * pow(m, 3.0) * 0.8;
            col *= (0.35 + uAudio.w * 0.8 + uBands[2] * 0.4);
            o = vec4(col * 0.8, 1.0);
        }
        """;

    public override void Reset() { _built = 0; _spin = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        if (_built == 0) Build();
        c.Fullscreen("nebula", Nebula, _ => { });
        _spin += f.Dt * 0.25f;
        _planet = VizMath.Smooth(_planet, 1.5f * (float)f.P.Scale * (1f + f.Bass * 0.1f), f.Dt, 12f);
        foreach (var s in _stars) c.Point(s, 2f + f.Treble * 2f, new Vector4(1, 1, 1, 0.6f));
        var tilt = Matrix4.CreateRotationZ(0.35f);
        c.PushModel(Matrix4.CreateRotationY(_spin) * tilt);
        c.SolidSphere(Vector3.Zero, _planet, f.Pal(0.35f, 1f), 28);
        c.PopModel();
        c.Point(Vector3.Zero, 420f * _planet / 1.5f * (0.15f + f.Bass * 0.2f), f.Pal(0.2f, 0.18f));   // atmosphere glow
        c.PushModel(Matrix4.CreateRotationY(_spin * 0.6f) * tilt);
        int n = Math.Min(_ring.Length, f.ParticleBudget);
        for (int i = 0; i < n; i++)
        {
            float v = f.Spec01((float)i / n);
            var col = f.Pal(_rh[i]); col.W = 0.25f + v * 0.9f + f.Treble * 0.3f;
            c.Point(_ring[i] * (_planet / 1.5f), 2f + v * 3f, col);
        }
        c.PopModel();
    }

    private void Build()
    {
        var rng = new Random(5005);
        _ring = new Vector3[3500]; _rh = new float[3500]; _stars = new Vector3[260]; _built = 1;
        for (int i = 0; i < _ring.Length; i++)
        {
            float a = (float)rng.NextDouble() * MathF.Tau, r = 2.3f + (float)rng.NextDouble() * 1.4f;
            _ring[i] = new Vector3(MathF.Cos(a) * r, VizMath.Gauss(rng) * 0.04f, MathF.Sin(a) * r);
            _rh[i] = (r - 2.3f) / 1.4f;
        }
        for (int i = 0; i < _stars.Length; i++) _stars[i] = VizMath.OnSphere(rng) * (18f + (float)rng.NextDouble() * 10f);
    }
}
