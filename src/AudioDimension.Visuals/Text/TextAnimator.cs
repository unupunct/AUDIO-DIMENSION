namespace AudioDimension.Visuals.Text;

/// <summary>Resolved appearance of a layer at one instant. Offsets are in design pixels (1080p).</summary>
public struct TextState
{
    public bool Visible;
    public float Alpha, OffsetX, OffsetY, Scale, Rotation, Blur;
    /// <summary>1 = all characters shown. Typewriter shows the first floor(Reveal*count) characters.</summary>
    public float Reveal;
    /// <summary>0..1 progress of the staggered per-letter fade/slide.</summary>
    public float LetterReveal;
    public float Glitch, RgbSplit;
    /// <summary>Light sweep position 0..1, or -1 for none.</summary>
    public float Sweep;
    public float WaveAmp, WavePhase;

    public static TextState Rest => new() { Visible = true, Alpha = 1, Scale = 1, Reveal = 1, LetterReveal = 1, Sweep = -1 };
}

/// <summary>
/// Pure, deterministic animation maths: given a layer and a timeline time it returns the layer's state. The same function drives the
/// live preview, screenshots and the exported video, so text is always in sync with the recording timeline.
/// </summary>
public static class TextAnimator
{
    public static bool IsContinuous(TextAnimType t) => t is TextAnimType.Glitch or TextAnimType.NeonFlicker or TextAnimType.Pulse or TextAnimType.Bounce
        or TextAnimType.Floating or TextAnimType.Rotation or TextAnimType.WaveMotion or TextAnimType.RgbSplit or TextAnimType.LightSweep;

    public static float Ease(Easing e, float p)
    {
        p = Math.Clamp(p, 0f, 1f);
        switch (e)
        {
            case Easing.EaseIn: return p * p * p;
            case Easing.EaseOut: { float q = 1 - p; return 1 - q * q * q; }
            case Easing.EaseInOut: return p < 0.5f ? 4 * p * p * p : 1 - MathF.Pow(-2 * p + 2, 3) / 2;
            case Easing.Back: { const float c1 = 1.70158f, c3 = c1 + 1; float q = p - 1; return 1 + c3 * q * q * q + c1 * q * q; }
            case Easing.Elastic:
                if (p <= 0f) return 0; if (p >= 1f) return 1;
                return MathF.Pow(2, -10 * p) * MathF.Sin((p * 10 - 0.75f) * (2 * MathF.PI / 3)) + 1;
            case Easing.Bounce:
            {
                const float n1 = 7.5625f, d1 = 2.75f;
                if (p < 1 / d1) return n1 * p * p;
                if (p < 2 / d1) { p -= 1.5f / d1; return n1 * p * p + 0.75f; }
                if (p < 2.5 / d1) { p -= 2.25f / d1; return n1 * p * p + 0.9375f; }
                p -= 2.625f / d1; return n1 * p * p + 0.984375f;
            }
            default: return p;
        }
    }

    /// <summary>Deterministic 0..1 hash used for glitch/flicker so every render of the same time is identical.</summary>
    public static float Hash(int n)
    {
        unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u; x = (x >> 22) ^ x; return (x & 0xFFFFFF) / (float)0x1000000; }
    }

    /// <summary>Whether the layer exists on the timeline at <paramref name="t"/>.</summary>
    public static bool IsActive(TextLayer l, double t) => l.Visible && t >= l.StartTime && (l.EndTime <= 0 || t < l.EndTime);

    public static TextState Evaluate(TextLayer layer, double t)
    {
        var s = TextState.Rest;
        if (!IsActive(layer, t)) { s.Visible = false; s.Alpha = 0; return s; }
        double local = t - layer.StartTime;
        foreach (var a in layer.Animations) Apply(ref s, a, local, layer);
        s.Alpha = Math.Clamp(s.Alpha, 0f, 1f);
        return s;
    }

    private static void Apply(ref TextState s, TextAnimation a, double local, TextLayer layer)
    {
        double d = Math.Max(0.01, a.Duration), lt = local - a.Delay;
        float amt = (float)a.Amount;
        float prog; // 0..1 progress of the (possibly looping / reversed) cycle
        if (a.Loop && lt >= 0) prog = (float)((lt % d) / d);
        else prog = (float)Math.Clamp(lt / d, 0, 1);
        if (a.Direction == AnimDirection.Reverse) prog = 1 - prog;
        else if (a.Direction == AnimDirection.PingPong) prog = prog < 0.5f ? prog * 2 : (1 - prog) * 2;

        bool started = lt >= 0;
        bool running = started && (a.Loop || lt <= d);
        float e = Ease(a.Easing, prog);
        float phase = (float)(lt / d);

        switch (a.Type)
        {
            // ---- transitions: hold the start pose before they begin and the rest pose after they finish
            case TextAnimType.FadeIn: s.Alpha *= e; break;
            case TextAnimType.FadeOut: s.Alpha *= 1 - e; break;
            case TextAnimType.SlideLeft: s.OffsetX += (1 - e) * 360f * amt; s.Alpha *= Math.Min(1f, e * 2.5f); break;
            case TextAnimType.SlideRight: s.OffsetX -= (1 - e) * 360f * amt; s.Alpha *= Math.Min(1f, e * 2.5f); break;
            case TextAnimType.SlideUp: s.OffsetY += (1 - e) * 200f * amt; s.Alpha *= Math.Min(1f, e * 2.5f); break;
            case TextAnimType.SlideDown: s.OffsetY -= (1 - e) * 200f * amt; s.Alpha *= Math.Min(1f, e * 2.5f); break;
            case TextAnimType.ZoomIn: s.Scale *= 0.15f + 0.85f * e; s.Alpha *= Math.Min(1f, e * 2f); break;
            case TextAnimType.ZoomOut: s.Scale *= 1f + (1 - e) * 1.6f * amt; s.Alpha *= Math.Min(1f, e * 2f); break;
            case TextAnimType.Typewriter: s.Reveal = Math.Min(s.Reveal, e); break;
            case TextAnimType.LetterReveal: s.LetterReveal = Math.Min(s.LetterReveal, e); break;
            case TextAnimType.BlurReveal: s.Blur = Math.Max(s.Blur, (1 - e) * 28f * amt); s.Alpha *= Math.Min(1f, e * 2f); break;

            // ---- continuous effects: only active inside their window
            case TextAnimType.Glitch:
                if (!running) break;
                { int frame = (int)Math.Floor(lt * 14); float h = Hash(frame * 31 + 7); s.Glitch = Math.Max(s.Glitch, h > 0.55f ? (0.4f + 0.6f * Hash(frame * 17 + 3)) * amt : 0f); }
                break;
            case TextAnimType.NeonFlicker:
                if (!running) break;
                { int frame = (int)Math.Floor(lt * 20); float h = Hash(frame * 13 + 5); s.Alpha *= h < 0.10f ? 0.2f : h < 0.22f ? 0.6f : 1f; }
                break;
            case TextAnimType.Pulse:
                if (running) s.Scale *= 1f + 0.08f * amt * MathF.Sin(MathF.Tau * phase);
                break;
            case TextAnimType.Bounce:
                if (running) s.OffsetY -= MathF.Abs(MathF.Sin(MathF.PI * phase)) * 38f * amt;
                break;
            case TextAnimType.Floating:
                if (running) { s.OffsetY += MathF.Sin(MathF.Tau * phase) * 16f * amt; s.OffsetX += MathF.Cos(MathF.Tau * phase / 1.3f) * 8f * amt; }
                break;
            case TextAnimType.Rotation:
                if (started) s.Rotation += a.Loop ? (float)(360.0 * lt / d) * (a.Direction == AnimDirection.Reverse ? -1 : 1) : e * 360f;
                break;
            case TextAnimType.WaveMotion:
                if (running) { s.WaveAmp = Math.Max(s.WaveAmp, 22f * amt); s.WavePhase = phase * MathF.Tau; }
                break;
            case TextAnimType.RgbSplit:
                if (running) s.RgbSplit = Math.Max(s.RgbSplit, 7f * amt * (0.5f + 0.5f * MathF.Sin(MathF.Tau * phase)));
                break;
            case TextAnimType.LightSweep:
                if (running) s.Sweep = a.Loop ? prog : e;
                break;
        }
    }
}
