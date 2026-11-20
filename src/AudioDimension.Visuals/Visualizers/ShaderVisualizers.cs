using AudioDimension.Visuals.Render;

namespace AudioDimension.Visuals.Visualizers;

/// <summary>Full-screen GPU plasma modulated by the frequency bands.</summary>
public sealed class Plasma : Visualizer
{
    public override string Id => "plasma";
    public override string Name => "Plasma";
    public override string Description => "Liquid plasma field; each frequency band bends a different layer.";

    private const string Frag = """
        uniform int uVariant;
        void main(){
            vec2 p = (vUV - 0.5) * vec2(uAspect, 1.0) * 3.0;
            float t = uTime * 0.6;
            float v = sin(p.x * 2.0 + t + uBands[0] * 6.0) + sin(p.y * 3.0 - t * 1.3 + uBands[1] * 5.0)
                    + sin((p.x + p.y) * 2.5 + t * 0.7 + uBands[3] * 4.0) + sin(length(p) * 4.0 - t * 1.7 - uAudio.x * 8.0);
            if (uVariant == 1) { v += sin(length(p - vec2(sin(t), cos(t * 0.8))) * 6.0 + uBands[5] * 6.0); v *= 0.8; }
            if (uVariant == 2) { p = vec2(length(p), atan(p.y, p.x) * 2.0); v += sin(p.x * 3.0 - t * 2.0) + sin(p.y + uBands[2] * 5.0); v *= 0.8; }
            v = v * 0.25 + 0.5 + uAudio.w * 0.2;
            vec3 col = pal(fract(v * 0.5 + uTime * 0.02));
            col *= 0.12 + 0.5 * (0.5 + 0.5 * sin(v * 6.0 + uBands[4] * 8.0));
            col *= 0.45 + uAudio.w * 0.9;
            o = vec4(col * uGain, 1.0);
        }
        """;

    public override void Draw(Canvas c, FrameInput f) => c.Fullscreen("plasma", Frag, b => b.Set("uVariant", f.P.Variant));
}
