using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Particles;

/// <summary>
/// Pooled CPU particle simulation. Arrays are allocated once per capacity and reused, so spawning and updating
/// never allocates in the render loop. Spawning stops at the quality-limited capacity.
/// </summary>
public sealed class ParticleSystem
{
    public Vector3[] Pos = Array.Empty<Vector3>();
    public Vector3[] Vel = Array.Empty<Vector3>();
    public float[] Life = Array.Empty<float>();
    public float[] MaxLife = Array.Empty<float>();
    public float[] Size = Array.Empty<float>();
    public float[] Hue = Array.Empty<float>();
    public int Count { get; private set; }
    public int Capacity { get; private set; }

    public void SetCapacity(int capacity)
    {
        capacity = Math.Max(16, capacity);
        if (capacity == Capacity) return;
        if (capacity > Pos.Length)
        {
            int n = Math.Max(capacity, Pos.Length * 2);
            Array.Resize(ref Pos, n); Array.Resize(ref Vel, n); Array.Resize(ref Life, n);
            Array.Resize(ref MaxLife, n); Array.Resize(ref Size, n); Array.Resize(ref Hue, n);
        }
        Capacity = capacity;
        if (Count > Capacity) Count = Capacity;
    }

    public void Clear() => Count = 0;

    public bool Spawn(Vector3 pos, Vector3 vel, float life, float size, float hue)
    {
        if (Count >= Capacity) return false;
        int i = Count++;
        Pos[i] = pos; Vel[i] = vel; Life[i] = life; MaxLife[i] = life; Size[i] = size; Hue[i] = hue;
        return true;
    }

    public void Update(float dt, float drag, Vector3 gravity)
    {
        float damp = MathF.Exp(-drag * dt);
        for (int i = 0; i < Count; i++)
        {
            Life[i] -= dt;
            if (Life[i] <= 0)
            {
                int last = --Count;           // swap-remove keeps the pool dense
                if (i != last)
                {
                    Pos[i] = Pos[last]; Vel[i] = Vel[last]; Life[i] = Life[last];
                    MaxLife[i] = MaxLife[last]; Size[i] = Size[last]; Hue[i] = Hue[last];
                }
                i--;
                continue;
            }
            Vel[i] = (Vel[i] + gravity * dt) * damp;
            Pos[i] += Vel[i] * dt;
        }
    }

    public void Draw(Canvas c, FrameInputPalette pal, float brightness = 1f, float sizeScale = 1f)
    {
        for (int i = 0; i < Count; i++)
        {
            float k = Life[i] / MaxLife[i];
            var col = pal(Hue[i]);
            col.W = MathF.Pow(k, 0.7f) * brightness;
            c.Point(Pos[i], Size[i] * sizeScale * (0.5f + 0.5f * k), col);
        }
    }
}

public delegate Vector4 FrameInputPalette(float t);

internal static class VizMath
{
    public static float Gauss(Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }

    public static Vector3 OnSphere(Random r)
    {
        float z = (float)(r.NextDouble() * 2 - 1), a = (float)(r.NextDouble() * Math.PI * 2), s = MathF.Sqrt(1 - z * z);
        return new Vector3(s * MathF.Cos(a), z, s * MathF.Sin(a));
    }

    public static float Smooth(float current, float target, float dt, float rate) => current + (target - current) * (1 - MathF.Exp(-rate * dt));
}
