namespace AudioDimension.Core.Settings;

public sealed class AppSettings
{
    // Audio
    public string DefaultInputDeviceId { get; set; } = "";
    public string DefaultSystemDeviceId { get; set; } = "";
    public int SampleRate { get; set; } = 48000;
    public int BufferMilliseconds { get; set; } = 20;
    public double Sensitivity { get; set; } = 1.0;
    public double AudioSmoothing { get; set; } = 0.6;

    // Rendering
    public string GraphicsQuality { get; set; } = "High";      // Low, Medium, High, Ultra, Custom
    public int TargetFrameRate { get; set; } = 60;
    public bool VSync { get; set; } = true;
    public int AntiAliasingSamples { get; set; } = 4;           // 0,2,4,8
    public int ParticleLimit { get; set; } = 20000;
    public string EffectsQuality { get; set; } = "High";
    public bool LightweightMode { get; set; }

    // Recording
    public string DefaultFormat { get; set; } = "MP4 (H.264)";
    public string DefaultResolution { get; set; } = "1920x1080";
    public int DefaultFrameRate { get; set; } = 30;
    public int VideoQuality { get; set; } = 75;                 // 1-100
    public string ExportFolder { get; set; } = "";
    public string EncoderPreference { get; set; } = "Auto";     // Auto, Software, NVENC, QuickSync, AMF

    // Interface
    public string Theme { get; set; } = "Graphite";             // Graphite, Midnight
    public string AccentColor { get; set; } = "#22D3A5";
    public double UiScale { get; set; } = 1.0;
    public bool AnimationsEnabled { get; set; } = true;
    public bool InspectorOpen { get; set; } = true;
    public string LastVisualizerId { get; set; } = "circular-spectrum";

    // Storage (empty = default under %LOCALAPPDATA%\AudioDimension)
    public string ProjectDirectory { get; set; } = "";
    public string PresetDirectory { get; set; } = "";
    public string TempDirectory { get; set; } = "";
}
