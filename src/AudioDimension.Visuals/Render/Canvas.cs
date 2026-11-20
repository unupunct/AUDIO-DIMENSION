using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Render;

internal sealed class FloatList
{
    public float[] A = new float[1 << 16];
    public int N;
    public void Clear() => N = 0;
    public void Ensure(int add) { if (N + add > A.Length) Array.Resize(ref A, Math.Max(A.Length * 2, N + add)); }
}

/// <summary>
/// CPU-side geometry collector. Visualizers draw into it each frame; the renderer uploads the lists in one pass.
/// Buffers are reused between frames so steady-state drawing does not allocate.
/// Lists: Solid (lit, depth-tested), Glow (additive world triangles), Points (additive sprites), Screen (additive NDC triangles, used for thick lines).
/// </summary>
public sealed class Canvas
{
    internal readonly FloatList Solid = new();   // pos3 nrm3 col4 = 10
    internal readonly FloatList Glow = new();    // pos3 nrm3 col4 = 10
    internal readonly FloatList Points = new();  // pos3 col4 size1 = 8
    internal readonly FloatList Screen = new();  // pos2 col4 = 6

    public Matrix4 ViewProj { get; internal set; } = Matrix4.Identity;
    public int Width { get; internal set; } = 1920;
    public int Height { get; internal set; } = 1080;
    /// <summary>Scene height / 1080 - converts "design pixels" to real pixels.</summary>
    public float PixelScale => Height / 1080f;
    public int CurveDetail { get; internal set; } = 2;

    private Matrix4 _model = Matrix4.Identity;
    private bool _hasModel;
    private readonly List<Matrix4> _stack = new();

    internal List<(string Key, string Frag, Action<ShaderBinder> Bind)> FullscreenPasses { get; } = new();

    internal void Clear()
    {
        Solid.Clear(); Glow.Clear(); Points.Clear(); Screen.Clear();
        FullscreenPasses.Clear();
        _model = Matrix4.Identity; _hasModel = false; _stack.Clear();
    }

    // ---- model transform ------------------------------------------------
    public void PushModel(Matrix4 m)
    {
        _stack.Add(_model);
        _model = _hasModel ? m * _model : m;
        _hasModel = true;
    }

    public void PopModel()
    {
        if (_stack.Count == 0) return;
        _model = _stack[^1]; _stack.RemoveAt(_stack.Count - 1);
        _hasModel = _stack.Count > 0 || _model != Matrix4.Identity;
    }

    private Vector3 P(Vector3 p) => _hasModel ? Vector3.TransformPosition(p, _model) : p;
    private Vector3 N(Vector3 n) => _hasModel ? Vector3.Normalize(Vector3.TransformNormal(n, _model)) : n;

    /// <summary>Queue a fullscreen fragment-shader pass drawn before geometry (plasma, nebula...).</summary>
    public void Fullscreen(string key, string fragSource, Action<ShaderBinder> bind) => FullscreenPasses.Add((key, fragSource, bind));

    // ---- triangles --------------------------------------------------------
    private static void V10(FloatList l, Vector3 p, Vector3 n, Vector4 c)
    {
        int i = l.N; var a = l.A;
        a[i] = p.X; a[i + 1] = p.Y; a[i + 2] = p.Z; a[i + 3] = n.X; a[i + 4] = n.Y; a[i + 5] = n.Z;
        a[i + 6] = c.X; a[i + 7] = c.Y; a[i + 8] = c.Z; a[i + 9] = c.W; l.N = i + 10;
    }

    public void SolidTri(Vector3 a, Vector3 b, Vector3 c, Vector4 col) => SolidTri(a, b, c, col, col, col);

    public void SolidTri(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc)
    {
        a = P(a); b = P(b); c = P(c);
        var n = Vector3.Cross(b - a, c - a);
        n = n.LengthSquared > 1e-12f ? n.Normalized() : Vector3.UnitY;
        Solid.Ensure(30);
        V10(Solid, a, n, ca); V10(Solid, b, n, cb); V10(Solid, c, n, cc);
    }

    public void SolidTriN(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector4 col)
    {
        Solid.Ensure(30);
        V10(Solid, P(a), N(na), col); V10(Solid, P(b), N(nb), col); V10(Solid, P(c), N(nc), col);
    }

    public void SolidQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 col) => SolidQuad(a, b, c, d, col, col, col, col);

    public void SolidQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd)
    {
        SolidTri(a, b, c, ca, cb, cc);
        SolidTri(a, c, d, ca, cc, cd);
    }

    public void GlowTri(Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc)
    {
        Glow.Ensure(30);
        var z = Vector3.UnitZ;
        V10(Glow, P(a), z, ca); V10(Glow, P(b), z, cb); V10(Glow, P(c), z, cc);
    }

    public void GlowQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 col) => GlowQuad(a, b, c, d, col, col, col, col);

    public void GlowQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd)
    {
        GlowTri(a, b, c, ca, cb, cc);
        GlowTri(a, c, d, ca, cc, cd);
    }

    /// <summary>Axis-aligned lit box (centre + half extents).</summary>
    public void SolidBox(Vector3 c, Vector3 h, Vector4 col, Vector4? topCol = null)
    {
        var t = topCol ?? col;
        var p000 = c + new Vector3(-h.X, -h.Y, -h.Z); var p100 = c + new Vector3(h.X, -h.Y, -h.Z);
        var p110 = c + new Vector3(h.X, h.Y, -h.Z); var p010 = c + new Vector3(-h.X, h.Y, -h.Z);
        var p001 = c + new Vector3(-h.X, -h.Y, h.Z); var p101 = c + new Vector3(h.X, -h.Y, h.Z);
        var p111 = c + new Vector3(h.X, h.Y, h.Z); var p011 = c + new Vector3(-h.X, h.Y, h.Z);
        SolidQuad(p001, p101, p111, p011, col, col, t, t);   // front
        SolidQuad(p100, p000, p010, p110, col, col, t, t);   // back
        SolidQuad(p101, p100, p110, p111, col, col, t, t);   // right
        SolidQuad(p000, p001, p011, p010, col, col, t, t);   // left
        SolidQuad(p011, p111, p110, p010, t);                // top
        SolidQuad(p000, p100, p101, p001, col);              // bottom
    }

    /// <summary>Lit UV sphere with smooth normals.</summary>
    public void SolidSphere(Vector3 c, float r, Vector4 col, int seg = 20)
    {
        int rings = Math.Max(4, seg / 2);
        for (int i = 0; i < rings; i++)
        {
            float v0 = MathF.PI * i / rings, v1 = MathF.PI * (i + 1) / rings;
            for (int j = 0; j < seg; j++)
            {
                float u0 = MathF.Tau * j / seg, u1 = MathF.Tau * (j + 1) / seg;
                Vector3 n00 = Sph(u0, v0), n10 = Sph(u1, v0), n01 = Sph(u0, v1), n11 = Sph(u1, v1);
                SolidTriN(c + n00 * r, c + n01 * r, c + n11 * r, n00, n01, n11, col);
                SolidTriN(c + n00 * r, c + n11 * r, c + n10 * r, n00, n11, n10, col);
            }
        }
    }

    private static Vector3 Sph(float u, float v) => new(MathF.Sin(v) * MathF.Cos(u), MathF.Cos(v), MathF.Sin(v) * MathF.Sin(u));

    // ---- points -------------------------------------------------------------
    /// <summary>Soft round sprite; size in design pixels (1080p).</summary>
    public void Point(Vector3 p, float size, Vector4 col)
    {
        p = P(p);
        Points.Ensure(8);
        var a = Points.A; int i = Points.N;
        a[i] = p.X; a[i + 1] = p.Y; a[i + 2] = p.Z; a[i + 3] = col.X; a[i + 4] = col.Y; a[i + 5] = col.Z; a[i + 6] = col.W; a[i + 7] = size;
        Points.N = i + 8;
    }

    // ---- thick lines (screen space) ----------------------------------------------
    /// <summary>Camera-independent thick line. widthPx in design pixels.</summary>
    public void Line(Vector3 a, Vector3 b, float widthPx, Vector4 col) => Line(a, b, widthPx, col, col);

    public void Line(Vector3 a, Vector3 b, float widthPx, Vector4 ca, Vector4 cb)
    {
        a = P(a); b = P(b);
        var va = Vector4.TransformRow(new Vector4(a, 1), ViewProj);
        var vb = Vector4.TransformRow(new Vector4(b, 1), ViewProj);
        const float near = 0.05f;
        if (va.W < near && vb.W < near) return;
        if (va.W < near) { float t = (near - va.W) / (vb.W - va.W); va = Vector4.Lerp(va, vb, t); ca = Vector4.Lerp(ca, cb, t); }
        else if (vb.W < near) { float t = (near - vb.W) / (va.W - vb.W); vb = Vector4.Lerp(vb, va, t); cb = Vector4.Lerp(cb, ca, t); }

        float ax = va.X / va.W, ay = va.Y / va.W, bx = vb.X / vb.W, by = vb.Y / vb.W;
        if ((ax > 3 && bx > 3) || (ax < -3 && bx < -3) || (ay > 3 && by > 3) || (ay < -3 && by < -3)) return;
        float halfW = Width * 0.5f, halfH = Height * 0.5f;
        float dx = (bx - ax) * halfW, dy = (by - ay) * halfH;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) { dx = 1; dy = 0; len = 1; }
        float w = Math.Max(1f, widthPx * PixelScale) * 0.5f;
        float nx = -dy / len * w / halfW, ny = dx / len * w / halfH;   // perpendicular in NDC
        // extend slightly along the direction so consecutive segments overlap without gaps
        float ex = dx / len * w * 0.5f / halfW, ey = dy / len * w * 0.5f / halfH;
        Screen.Ensure(36);
        var l = Screen;
        S6(l, ax - ex + nx, ay - ey + ny, ca); S6(l, ax - ex - nx, ay - ey - ny, ca); S6(l, bx + ex + nx, by + ey + ny, cb);
        S6(l, ax - ex - nx, ay - ey - ny, ca); S6(l, bx + ex - nx, by + ey - ny, cb); S6(l, bx + ex + nx, by + ey + ny, cb);
    }

    private static void S6(FloatList l, float x, float y, Vector4 c)
    {
        int i = l.N; var a = l.A;
        a[i] = x; a[i + 1] = y; a[i + 2] = c.X; a[i + 3] = c.Y; a[i + 4] = c.Z; a[i + 5] = c.W; l.N = i + 6;
    }

    public void Polyline(ReadOnlySpan<Vector3> pts, float widthPx, Vector4 col, bool closed = false)
    {
        for (int i = 0; i + 1 < pts.Length; i++) Line(pts[i], pts[i + 1], widthPx, col);
        if (closed && pts.Length > 2) Line(pts[^1], pts[0], widthPx, col);
    }

    /// <summary>Polyline whose colour follows a gradient callback (t = 0..1 along the line).</summary>
    public void Polyline(ReadOnlySpan<Vector3> pts, float widthPx, Func<float, Vector4> color, bool closed = false)
    {
        int n = pts.Length;
        if (n < 2) return;
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
            Line(pts[i], pts[(i + 1) % n], widthPx, color((float)i / segs), color((float)(i + 1) / segs));
    }

    /// <summary>Circle in the model's XY plane.</summary>
    public void Ring(Vector3 centre, float radius, float widthPx, Vector4 col, int segments = 64)
    {
        segments = Math.Max(8, segments * CurveDetail / 2);
        Vector3 prev = centre + new Vector3(radius, 0, 0);
        for (int i = 1; i <= segments; i++)
        {
            float a = MathF.Tau * i / segments;
            var p = centre + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0);
            Line(prev, p, widthPx, col);
            prev = p;
        }
    }

    /// <summary>Filled quad in XY (additive), handy for 2D bars.</summary>
    public void Rect(float x, float y, float w, float h, Vector4 colBottom, Vector4 colTop, float z = 0)
        => GlowQuad(new(x, y, z), new(x + w, y, z), new(x + w, y + h, z), new(x, y + h, z), colBottom, colBottom, colTop, colTop);
}
