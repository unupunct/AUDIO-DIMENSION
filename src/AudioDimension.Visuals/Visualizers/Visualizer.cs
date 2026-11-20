using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Render;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Per-frame data handed to a visualizer. One instance is reused, so visualizers must not keep references to it.</summary>
public sealed class FrameInput
{
    public AnalysisFrame Audio { get; internal set; } = AnalysisFrame.Silent;
    public VisualConfig Config { get; internal set; } = new();
    /// <summary>Animation clock in seconds, already scaled by Params.Speed.</summary>
    public float Time { get; internal set; }
    /// <summary>Frame delta in seconds, scaled by Params.Speed.</summary>
    public float Dt { get; internal set; }
    public float Aspect { get; internal set; } = 16f / 9f;
    public int ParticleBudget { get; internal set; } = 20000;
    public VisualParams P => Config.Params;

    private float R => (float)Config.Params.Reactivity;
    public float Bass => Math.Clamp(Audio.Bass * R, 0, 1.5f);
    public float Mid => Math.Clamp(Audio.Mid * R, 0, 1.5f);
    public float Treble => Math.Clamp(Audio.Treble * R, 0, 1.5f);
    public float Level => Math.Clamp(Audio.Level * R, 0, 1.5f);
    public bool Beat => Audio.IsBeat;
    public float Band(int i) => Math.Clamp(Audio.Bands[i] * R, 0, 1.5f);

    /// <summary>Spectrum value at integer bin.</summary>
    public float Spec(int i) => Math.Clamp(Audio.Spectrum[Math.Clamp(i, 0, Audio.Spectrum.Length - 1)] * R, 0, 1.5f);

    /// <summary>Linearly interpolated spectrum at x in 0..1 (low to high frequency).</summary>
    public float Spec01(float x)
    {
        var s = Audio.Spectrum;
        float f = Math.Clamp(x, 0, 1) * (s.Length - 1);
        int i = (int)f; int j = Math.Min(i + 1, s.Length - 1);
        return Math.Clamp((s[i] + (s[j] - s[i]) * (f - i)) * R, 0, 1.5f);
    }

    public Vector4 Pal(float t) => Config.Palette.Evaluate(t);
    public Vector4 Pal(float t, float alpha) { var c = Config.Palette.Evaluate(t); c.W = alpha; return c; }
}

/// <summary>
/// A reusable visualization. Implementations only emit geometry through <see cref="Canvas"/>; GL state, post effects and
/// camera belong to the renderer, so adding a visualizer never touches the rendering engine.
/// </summary>
public abstract class Visualizer
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual bool Is3D => false;
    /// <summary>Camera defaults for this visualizer (used by Reset Camera).</summary>
    public virtual CameraSettings DefaultCamera => new();
    public virtual void Reset() { }
    public abstract void Draw(Canvas c, FrameInput f);
}
