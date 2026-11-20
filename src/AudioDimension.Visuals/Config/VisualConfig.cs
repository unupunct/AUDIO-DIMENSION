using System.Text.Json.Serialization;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Config;

public enum QualityLevel { Low, Medium, High, Ultra, Custom }
public enum BackgroundMode { Solid, Gradient, AnimatedGradient, Starfield, ParticleField, Transparent, Image }

/// <summary>Everything a visualization needs besides audio. Plain data: serialisable into presets and projects.</summary>
public sealed class VisualConfig
{
    public VisualParams Params { get; set; } = new();
    public PaletteSettings Palette { get; set; } = new();
    public CameraSettings Camera { get; set; } = new();
    public BackgroundSettings Background { get; set; } = new();
    public EffectSettings Effects { get; set; } = new();
    public QualitySettings Quality { get; set; } = QualitySettings.For(QualityLevel.High);
    /// <summary>Overlay text layers (title, artist, watermark...). Drawn after post-processing.</summary>
    public System.Collections.ObjectModel.ObservableCollection<Text.TextLayer> TextLayers { get; set; } = new();

    /// <summary>Deep copy via JSON, used for screenshots and exports that tweak settings without touching the live preview.</summary>
    public VisualConfig Clone() => System.Text.Json.JsonSerializer.Deserialize<VisualConfig>(System.Text.Json.JsonSerializer.Serialize(this))!;
}

public sealed class VisualParams
{
    public double Speed { get; set; } = 1.0;            // animation speed multiplier
    public double Reactivity { get; set; } = 1.0;       // how strongly audio drives visuals
    public double Scale { get; set; } = 1.0;            // overall element scale
    public double LineWidth { get; set; } = 3.0;        // design pixels at 1080p
    public double ParticleDensity { get; set; } = 1.0;  // fraction of the quality-limited particle budget
    public double Glow { get; set; } = 1.0;             // brightness multiplier for additive layers
    public int Segments { get; set; }                   // 0 = visualizer default (bars, sides, arms...)
    public int Variant { get; set; }                    // visualizer-specific style switch
    public bool Mirror { get; set; }
    public double Rotation { get; set; }                // extra spin in degrees per second
}

public sealed class PaletteSettings
{
    public string[] Colors { get; set; } = { "#22D3A5", "#22C7E8", "#A78BFA", "#F472B6" };

    private Vector4[]? _cache;
    private string[]? _cacheKey;

    private Vector4[] Stops()
    {
        if (_cache is not null && ReferenceEquals(_cacheKey, Colors)) return _cache;
        _cache = Colors.Length == 0 ? new[] { Vector4.One } : Colors.Select(ColorUtil.Parse).ToArray();
        _cacheKey = Colors;
        return _cache;
    }

    /// <summary>Samples the gradient at t in 0..1.</summary>
    public Vector4 Evaluate(float t)
    {
        var s = Stops();
        if (s.Length == 1) return s[0];
        t = Math.Clamp(t, 0f, 1f) * (s.Length - 1);
        int i = Math.Min((int)t, s.Length - 2);
        return Vector4.Lerp(s[i], s[i + 1], t - i);
    }
}

public sealed class CameraSettings
{
    public double Yaw { get; set; } = 25;
    public double Pitch { get; set; } = 18;
    public double Distance { get; set; } = 9;
    public double Fov { get; set; } = 55;
    public bool Orthographic { get; set; }
    public double AutoRotateSpeed { get; set; } = 6;    // degrees per second
    public double AudioShake { get; set; } = 0.0;       // camera kick on bass, 0..1
    public double Zoom2D { get; set; } = 1.0;

    public void Reset(CameraSettings defaults)
    {
        Yaw = defaults.Yaw; Pitch = defaults.Pitch; Distance = defaults.Distance; Fov = defaults.Fov;
        Orthographic = defaults.Orthographic; AutoRotateSpeed = defaults.AutoRotateSpeed; Zoom2D = defaults.Zoom2D;
    }
}

public sealed class BackgroundSettings
{
    public BackgroundMode Mode { get; set; } = BackgroundMode.Gradient;
    public string Color1 { get; set; } = "#0B0D12";
    public string Color2 { get; set; } = "#1A1033";
    public double AnimationSpeed { get; set; } = 0.3;
    public double AudioReactive { get; set; } = 0.5;
    /// <summary>Project asset id of the background image (the file lives inside the project package).</summary>
    public string? AssetId { get; set; }
    /// <summary>Runtime location of the image on disk; resolved from the asset when a project is opened. Never stored in project files.</summary>
    public string? ImagePath { get; set; }
    /// <summary>0 = full brightness, 1 = black. Keeps text and visuals readable over busy pictures.</summary>
    public double ImageDim { get; set; } = 0.35;
}

public sealed class EffectSettings
{
    public bool Bloom { get; set; } = true;
    public double BloomAmount { get; set; } = 0.9;
    public double BloomThreshold { get; set; } = 0.55;
    public bool Vignette { get; set; } = true;
    public double VignetteAmount { get; set; } = 0.45;
    public bool ChromaticAberration { get; set; }
    public double ChromaticAmount { get; set; } = 0.012;
    public bool RgbSeparation { get; set; }
    public double RgbSeparationAmount { get; set; } = 0.004;
    public bool FilmGrain { get; set; }
    public double FilmGrainAmount { get; set; } = 0.06;
    public bool Scanlines { get; set; }
    public double ScanlineAmount { get; set; } = 0.3;
    public bool ColorGrading { get; set; }
    public double Saturation { get; set; } = 1.15;
    public double Contrast { get; set; } = 1.05;
    public double Brightness { get; set; }
    public double HueShift { get; set; }                 // degrees
    public bool Distortion { get; set; }
    public double DistortionAmount { get; set; } = 0.12;
    public bool Blur { get; set; }
    public double BlurAmount { get; set; } = 0.4;
    public bool Noise { get; set; }
    public double NoiseAmount { get; set; } = 0.05;
    public bool MotionBlur { get; set; }
    public double MotionBlurAmount { get; set; } = 0.6;
    public bool LightRays { get; set; }
    public double LightRaysAmount { get; set; } = 0.5;
}

public sealed class QualitySettings
{
    public QualityLevel Level { get; set; } = QualityLevel.High;
    public double RenderScale { get; set; } = 1.0;       // internal resolution multiplier
    public int BloomPasses { get; set; } = 2;            // blur iterations (0 disables the blur chain)
    public int MaxParticles { get; set; } = 20000;
    public int CurveDetail { get; set; } = 2;            // 1 low .. 3 high; multiplies segment counts
    public bool ExpensiveEffects { get; set; } = true;   // allow motion blur / light rays / blur

    public static QualitySettings For(QualityLevel l) => l switch
    {
        QualityLevel.Low => new() { Level = l, RenderScale = 0.6, BloomPasses = 0, MaxParticles = 4000, CurveDetail = 1, ExpensiveEffects = false },
        QualityLevel.Medium => new() { Level = l, RenderScale = 0.8, BloomPasses = 1, MaxParticles = 10000, CurveDetail = 2, ExpensiveEffects = false },
        QualityLevel.Ultra => new() { Level = l, RenderScale = 1.0, BloomPasses = 4, MaxParticles = 60000, CurveDetail = 3, ExpensiveEffects = true },
        _ => new() { Level = QualityLevel.High, RenderScale = 1.0, BloomPasses = 2, MaxParticles = 20000, CurveDetail = 2, ExpensiveEffects = true },
    };
}

public static class ColorUtil
{
    public static Vector4 Parse(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return new Vector4(Convert.ToInt32(hex[..2], 16) / 255f, Convert.ToInt32(hex.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(hex.Substring(4, 2), 16) / 255f, 1f);
        throw new FormatException($"Colour '{hex}' must be #RRGGBB");
    }

    /// <summary>Parses #RRGGBB, returning <paramref name="fallback"/> for invalid text (so half-typed input never breaks rendering).</summary>
    public static Vector4 TryParse(string? hex, Vector4 fallback)
    {
        try { return hex is null ? fallback : Parse(hex); }
        catch (Exception) { return fallback; }
    }

    public static Vector4 FromHsv(float h, float s, float v, float a = 1f)
    {
        h = h - MathF.Floor(h);
        float c = v * s, x = c * (1 - MathF.Abs(h * 6 % 2 - 1)), m = v - c;
        (float r, float g, float b) = (int)(h * 6) switch
        { 0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x) };
        return new Vector4(r + m, g + m, b + m, a);
    }
}
