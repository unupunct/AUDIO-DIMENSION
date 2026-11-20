using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Particles;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>GPU fractals. Variant 0 Julia set, 1 kaleidoscopic fold, 2 log-polar portal.</summary>
public sealed class FractalViz : Visualizer
{
    public override string Id => "fractal";
    public override string Name => "Fractal";
    public override string Description => "Audio-modulated fractals: bass bends the Julia constant, bands warp the folds.";

    private const string Frag = """
        uniform int uVariant;
        void main(){
            vec2 p = (vUV - 0.5) * vec2(uAspect, 1.0) * 2.0;
            float t = uTime * 0.25;
            vec3 col = vec3(0.0);
            if (uVariant == 0) {
                float a = t * 0.7 + uAudio.x * 1.5;
                vec2 c = 0.7885 * vec2(cos(a), sin(a)) * (0.92 + uBands[2] * 0.15);
                vec2 z = p * (1.3 - uAudio.w * 0.2); float it = 0.0;
                for (int i = 0; i < 56; i++) { z = vec2(z.x*z.x - z.y*z.y, 2.0*z.x*z.y) + c; if (dot(z,z) > 16.0) break; it += 1.0; }
                float s = it / 56.0; float sm = it - log2(log2(dot(z,z) + 1.0)) + 4.0;
                col = pal(fract(sm * 0.04 + t * 0.2)) * (0.15 + pow(s, 0.6) * (0.9 + uAudio.w * 0.8));
                if (it >= 55.0) col = pal(0.0) * 0.12 * (1.0 + uAudio.x);
            } else if (uVariant == 1) {
                vec2 q = p * 1.4; float acc = 0.0;
                float ca = cos(t), sa = sin(t); q = mat2(ca, -sa, sa, ca) * q;
                float prev = 0.0; for (int i = 0; i < 7; i++) { q = abs(q) / max(dot(q, q), 0.25) - vec2(0.85 + uBands[1] * 0.25, 0.55 + uBands[3] * 0.2); float l = length(q); acc += exp(-abs(l - prev) * 3.0) * 0.5 + exp(-l * 2.2); prev = l; }
                float k = smoothstep(1.2, 4.5, acc); col = pal(fract(acc * 0.22 + t * 0.2)) * (0.03 + 0.85 * k * k * (0.55 + uAudio.w * 0.8 + uAudio.y * 0.4));
            } else {
                float r = length(p) + 1e-3, ang = atan(p.y, p.x);
                vec2 q = vec2(log(r) * 1.4 - t * 3.0 - uAudio.x * 0.8, ang * 3.0 / 6.2831853 + t * 0.5);
                float pat = 0.0; vec2 f = q;
                for (int i = 0; i < 4; i++) { f = abs(fract(f * 1.6) - 0.5) * 2.0; pat += (f.x * f.y) / (1.0 + float(i)); }
                float ring = exp(-r * 3.2) * (0.12 + uAudio.w * 0.5);
                col = pal(fract(pat * 0.3 + t * 0.4)) * (0.04 + pat * 0.14) * (0.6 + uAudio.w * 0.8) * smoothstep(0.0, 0.5, r) + pal(0.9) * ring * (0.3 + uBands[0]);
            }
            o = vec4(col * uGain, 1.0);
        }
        """;

    public override void Draw(Canvas c, FrameInput f) => c.Fullscreen("fractal", Frag, b => b.Set("uVariant", f.P.Variant));
}

/// <summary>Flowing GPU fields. Variant 0 domain-warped fluid, 1 nebula drift, 2 organic cells.</summary>
public sealed class FluidField : Visualizer
{
    public override string Id => "fluid-field";
    public override string Name => "Fluid Field";
    public override string Description => "Warped fluid, drifting nebula or living cells, deformed by the frequency bands.";

    private const string Frag = """
        uniform int uVariant;
        vec2 hash2(vec2 p){ return fract(sin(vec2(dot(p, vec2(127.1,311.7)), dot(p, vec2(269.5,183.3)))) * 43758.5453); }
        void main(){
            vec2 p = (vUV - 0.5) * vec2(uAspect, 1.0) * 2.4;
            float t = uTime * 0.18;
            vec3 col;
            if (uVariant == 0) {
                vec2 q = vec2(fbm(p + t + uBands[0]), fbm(p + vec2(5.2, 1.3) - t));
                vec2 r = vec2(fbm(p + 4.0 * q + vec2(1.7, 9.2) + uBands[2] * 1.2), fbm(p + 4.0 * q + vec2(8.3, 2.8) - t * 0.7));
                float f = fbm(p + 3.5 * r + uAudio.x * 0.8);
                col = pal(f + length(q) * 0.3) * (0.06 + f * f * 0.85) * (0.4 + uAudio.w * 0.7);
            } else if (uVariant == 1) {
                float n = fbm(p * 1.1 + vec2(t, -t * 0.6) + uAudio.x * 0.5), m = fbm(p * 2.3 - vec2(t * 1.2, t) + 3.0);
                float stars = pow(hash(floor(p * 42.0)), 400.0) * (0.5 + uAudio.z * 2.0);
                col = pal(n) * pow(n, 2.4) * 1.5 + pal(1.0 - m) * pow(m, 3.2) * 0.9 + vec3(stars);
                col *= 0.35 + uAudio.w * 0.9 + uBands[1] * 0.4;
            } else {
                vec2 g = p * 2.2 + vec2(sin(t * 2.0), cos(t * 1.7)); vec2 id = floor(g), f = fract(g);
                float d1 = 8.0, d2 = 8.0;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
                    vec2 o2 = vec2(x, y); vec2 h = hash2(id + o2);
                    vec2 pt = o2 + 0.5 + 0.45 * sin(t * 3.0 + 6.2831 * h + uBands[int(h.x * 5.0)] * 3.0);
                    float d = length(pt - f); if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                float edge = smoothstep(0.1, 0.0, d2 - d1);
                col = pal(d1 * 1.2 + t) * (0.05 + 0.35 * (1.0 - d1)) * (0.4 + uAudio.w * 0.6) + pal(0.9) * edge * (0.2 + uBands[3] * 0.6);
            }
            o = vec4(col * uGain, 1.0);
        }
        """;

    public override void Draw(Canvas c, FrameInput f) => c.Fullscreen("fluid", Frag, b => b.Set("uVariant", f.P.Variant));
}

/// <summary>Line-art sacred geometry. Variant 0 flower of life, 1 orbit rings, 2 star polygons.</summary>
public sealed class SacredGeometry : Visualizer
{
    public override string Id => "sacred-geometry";
    public override string Name => "Sacred Geometry";
    public override string Description => "Interlocking circles, orbits or star polygons that breathe with the bands.";
    private readonly Vector3[] _poly = new Vector3[16];

    public override void Draw(Canvas c, FrameInput f)
    {
        float s = (float)f.P.Scale, lw = (float)f.P.LineWidth * 0.6f;
        if (f.P.Variant == 0)
        {
            float r = 0.17f * s * (1f + f.Bass * 0.12f);
            c.PushModel(Matrix4.CreateRotationZ(f.Time * 0.08f));
            int idx = 0;
            void Circle(Vector3 p) { float b = f.Band(idx % 6); c.Ring(p, r * (1f + b * 0.18f), lw * (1f + b * 2f), f.Pal((idx % 19) / 18f, 0.35f + b * 0.65f), 48); idx++; }
            Circle(Vector3.Zero);
            for (int i = 0; i < 6; i++) { float a = MathF.PI / 3 * i; Circle(new(MathF.Cos(a) * r, MathF.Sin(a) * r, 0)); }
            for (int i = 0; i < 6; i++) { float a = MathF.PI / 3 * i; Circle(new(MathF.Cos(a) * r * 2, MathF.Sin(a) * r * 2, 0)); float a2 = a + MathF.PI / 6; Circle(new(MathF.Cos(a2) * r * 1.732f, MathF.Sin(a2) * r * 1.732f, 0)); }
            c.PopModel();
        }
        else if (f.P.Variant == 1)
        {
            for (int i = 0; i < 6; i++)
            {
                float b = f.Band(i), r = (0.14f + i * 0.13f) * s;
                c.Ring(Vector3.Zero, r, lw * 0.6f, f.Pal(i / 5f, 0.25f + b * 0.4f), 96);
                float a = f.Time * (0.5f + i * 0.22f) * (i % 2 == 0 ? 1f : -1f);
                c.Point(new(MathF.Cos(a) * r, MathF.Sin(a) * r, 0), 10f + b * 26f, f.Pal(i / 5f, 1f));
                c.Point(new(MathF.Cos(a + MathF.PI) * r, MathF.Sin(a + MathF.PI) * r, 0), 6f + b * 14f, f.Pal(i / 5f + 0.1f, 0.8f));
            }
            c.Point(Vector3.Zero, 40f + f.Bass * 90f, f.Pal(0.5f, 0.5f));
        }
        else
        {
            for (int layer = 0; layer < 5; layer++)
            {
                float b = f.Band(layer), r = (0.25f + layer * 0.16f) * s * (1f + b * 0.2f);
                int n = 5 + layer * 2 > 16 ? 16 : 5 + layer * 2; int stepK = layer % 2 == 0 ? 2 : 3;
                float rot = f.Time * (0.15f + layer * 0.05f) * (layer % 2 == 0 ? 1 : -1);
                for (int i = 0; i < n; i++) { float a = rot + MathF.Tau * i / n; _poly[i] = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, 0); }
                var col = f.Pal(layer / 4f, 0.3f + b * 0.7f);
                for (int i = 0; i < n; i++) c.Line(_poly[i], _poly[(i + stepK) % n], lw * (1f + b * 2f), col);
            }
        }
    }
}

/// <summary>A glowing star with prominences that arc away and fall back.</summary>
public sealed class SolarFlare : Visualizer
{
    public override string Id => "solar-flare";
    public override string Name => "Solar Flare";
    public override string Description => "A star spits plasma on every beat; the loops fall back under gravity.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 0, Pitch = 12, Distance = 11, AutoRotateSpeed = 4 };
    private readonly ParticleSystem _ps = new();
    private Random _rng = new(1313);
    private float _acc;

    public override void Reset() { _ps.Clear(); _rng = new Random(1313); _acc = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        _ps.SetCapacity(f.ParticleBudget);
        float s = (float)f.P.Scale, r0 = 1.4f * s * (1f + f.Bass * 0.08f);
        _acc += f.Level * f.Dt * f.ParticleBudget * 0.12f;
        int burst = f.Beat ? (int)(f.ParticleBudget * 0.05f * (0.5f + f.Audio.BeatStrength)) : 0;
        int spawn = (int)_acc + burst; _acc -= (int)_acc;
        for (int i = 0; i < spawn; i++)
        {
            var dir = VizMath.OnSphere(_rng);
            _ps.Spawn(dir * r0, dir * (1.6f + (float)_rng.NextDouble() * 3f + f.Bass * 3f), 1.2f + (float)_rng.NextDouble() * 1.8f, 3f + (float)_rng.NextDouble() * 5f, (float)_rng.NextDouble());
        }
        for (int i = 0; i < _ps.Count; i++)
        {
            var p = _ps.Pos[i]; float d2 = p.LengthSquared;
            _ps.Vel[i] -= p / (d2 * MathF.Sqrt(d2) + 0.2f) * 4.5f * f.Dt;
        }
        _ps.Update(f.Dt, 0.08f, Vector3.Zero);
        _ps.Draw(c, t => f.Pal(0.1f + t * 0.5f), 0.9f);
        c.SolidSphere(Vector3.Zero, r0, f.Pal(0.05f, 1f), 28);
        c.Point(Vector3.Zero, 520f * s * (0.35f + f.Bass * 0.25f), f.Pal(0.1f, 0.22f));
        c.Point(Vector3.Zero, 260f * s * (0.35f + f.Level * 0.3f), f.Pal(0.25f, 0.4f));
    }
}

/// <summary>3D lattice of spinning cubes. Variant 0 pulsing lattice, 1 collapse/explode on beats.</summary>
public sealed class CubeField : Visualizer
{
    public override string Id => "cube-field";
    public override string Name => "Cube Field";
    public override string Description => "A lattice of cubes sized by the spectrum; in collapse mode it implodes and bursts on the beat.";
    public override bool Is3D => true;
    public override CameraSettings DefaultCamera => new() { Yaw = 30, Pitch = 24, Distance = 14, AutoRotateSpeed = 8 };
    private float _collapse, _burst;

    public override void Reset() { _collapse = 0; _burst = 0; }

    public override void Draw(Canvas c, FrameInput f)
    {
        int g = f.P.Segments is >= 3 and <= 7 ? f.P.Segments : 5;
        float sp = 1.6f * (float)f.P.Scale;
        bool collapse = f.P.Variant == 1;
        _collapse = VizMath.Smooth(_collapse, f.Bass, f.Dt, 10f);
        if (f.Beat) _burst = 1f; _burst = Math.Max(0, _burst - f.Dt * 2.2f);
        float pull = collapse ? 1f - _collapse * 0.7f + _burst * 1.4f : 1f;
        int count = g * g * g, idx = 0;
        for (int i = 0; i < g; i++)
            for (int j = 0; j < g; j++)
                for (int k = 0; k < g; k++, idx++)
                {
                    var pos = new Vector3(i - (g - 1) / 2f, j - (g - 1) / 2f, k - (g - 1) / 2f) * sp * pull;
                    float v = f.Spec01((float)((idx * 7) % count) / count);
                    float size = (0.12f + v * 0.5f) * (float)f.P.Scale, h = (idx * 0.37f) % 1f;
                    var rot = Matrix4.CreateRotationX(f.Time * (0.4f + h) + v) * Matrix4.CreateRotationY(f.Time * (0.6f + h * 0.7f)) * Matrix4.CreateTranslation(pos);
                    c.PushModel(rot);
                    var col = f.Pal(h); col.W = 0.55f + v * 0.45f;
                    c.SolidBox(Vector3.Zero, new Vector3(size), col, f.Pal((h + 0.3f) % 1f));
                    c.PopModel();
                }
    }
}
