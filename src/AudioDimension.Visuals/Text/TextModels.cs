using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioDimension.Visuals.Text;

public enum TextKind { MainTitle, Subtitle, Artist, SongTitle, Album, Custom, Watermark, Credits, SocialHandle, Caption }
public enum TextAlign { Left, Center, Right }
public enum Easing { Linear, EaseIn, EaseOut, EaseInOut, Bounce, Elastic, Back }
public enum AnimDirection { Forward, Reverse, PingPong }

public enum TextAnimType
{
    FadeIn, FadeOut, SlideLeft, SlideRight, SlideUp, SlideDown, ZoomIn, ZoomOut, Typewriter, LetterReveal,
    Glitch, NeonFlicker, Pulse, Bounce, Floating, Rotation, BlurReveal, WaveMotion, RgbSplit, LightSweep
}

public sealed class TextAnimation : ObservableObject
{
    private TextAnimType _type = TextAnimType.FadeIn;
    private double _duration = 1.0, _delay, _amount = 1.0;
    private Easing _easing = Easing.EaseOut;
    private bool _loop;
    private AnimDirection _direction = AnimDirection.Forward;

    public TextAnimType Type { get => _type; set => SetProperty(ref _type, value); }
    /// <summary>Seconds. For continuous effects this is one cycle.</summary>
    public double Duration { get => _duration; set => SetProperty(ref _duration, value); }
    /// <summary>Seconds after the layer's start time.</summary>
    public double Delay { get => _delay; set => SetProperty(ref _delay, value); }
    public Easing Easing { get => _easing; set => SetProperty(ref _easing, value); }
    public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }
    public AnimDirection Direction { get => _direction; set => SetProperty(ref _direction, value); }
    /// <summary>Strength multiplier (distance, blur, wobble...).</summary>
    public double Amount { get => _amount; set => SetProperty(ref _amount, value); }

    public TextAnimation Clone() => (TextAnimation)MemberwiseClone();
    public override string ToString() => Type.ToString();
}

public sealed class TextShadow { public bool Enabled { get; set; } public string Color { get; set; } = "#000000"; public double Opacity { get; set; } = 0.6; public double Blur { get; set; } = 6; public double OffsetX { get; set; } = 3; public double OffsetY { get; set; } = 3; }
public sealed class TextOutline { public bool Enabled { get; set; } public string Color { get; set; } = "#000000"; public double Width { get; set; } = 2; }
public sealed class TextGlow { public bool Enabled { get; set; } public string Color { get; set; } = "#22D3A5"; public double Radius { get; set; } = 14; public double Intensity { get; set; } = 1; }
public sealed class TextPanel { public bool Enabled { get; set; } public string Color { get; set; } = "#000000"; public double Opacity { get; set; } = 0.5; public double CornerRadius { get; set; } = 12; }
public sealed class TextBorder { public bool Enabled { get; set; } public string Color { get; set; } = "#FFFFFF"; public double Width { get; set; } = 2; public double Opacity { get; set; } = 1; }

/// <summary>One overlay text layer. Positions are fractions of the frame (0..1); sizes are in "design pixels" at 1080p so layouts survive any resolution.</summary>
public sealed class TextLayer : ObservableObject
{
    private TextKind _kind = TextKind.Custom;
    private string _text = "Your text";
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 64, _opacity = 1, _letterSpacing, _lineSpacing = 1, _x = 0.5, _y = 0.5, _rotation, _scale = 1, _padding = 16, _startTime, _endTime;
    private int _fontWeight = 400;
    private bool _italic, _visible = true;
    private string _color = "#FFFFFF";
    private TextAlign _align = TextAlign.Center;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public TextKind Kind { get => _kind; set { if (SetProperty(ref _kind, value)) OnPropertyChanged(nameof(DisplayName)); } }
    public string Text { get => _text; set { if (SetProperty(ref _text, value)) OnPropertyChanged(nameof(DisplayName)); } }
    public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }
    public int FontWeight { get => _fontWeight; set => SetProperty(ref _fontWeight, value); }
    public bool Italic { get => _italic; set => SetProperty(ref _italic, value); }
    public string Color { get => _color; set => SetProperty(ref _color, value); }
    public double Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }
    public TextAlign Align { get => _align; set => SetProperty(ref _align, value); }
    public double LetterSpacing { get => _letterSpacing; set => SetProperty(ref _letterSpacing, value); }
    public double LineSpacing { get => _lineSpacing; set => SetProperty(ref _lineSpacing, value); }
    public double X { get => _x; set => SetProperty(ref _x, value); }
    public double Y { get => _y; set => SetProperty(ref _y, value); }
    public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }
    public double Scale { get => _scale; set => SetProperty(ref _scale, value); }
    public double Padding { get => _padding; set => SetProperty(ref _padding, value); }
    public bool Visible { get => _visible; set => SetProperty(ref _visible, value); }
    /// <summary>Timeline seconds at which the layer appears.</summary>
    public double StartTime { get => _startTime; set => SetProperty(ref _startTime, value); }
    /// <summary>Timeline seconds at which it disappears; 0 = stays until the end.</summary>
    public double EndTime { get => _endTime; set => SetProperty(ref _endTime, value); }

    public TextShadow Shadow { get; set; } = new();
    public TextOutline Outline { get; set; } = new();
    public TextGlow Glow { get; set; } = new();
    public TextPanel Panel { get; set; } = new();
    public TextBorder Border { get; set; } = new();
    public ObservableCollection<TextAnimation> Animations { get; set; } = new();

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var t = (Text ?? "").Replace("\n", " ").Trim();
            if (t.Length > 22) t = t[..22] + "...";
            return $"{KindLabel(Kind)}: {t}";
        }
    }

    public static string KindLabel(TextKind k) => k switch
    {
        TextKind.MainTitle => "Title", TextKind.Subtitle => "Subtitle", TextKind.Artist => "Artist", TextKind.SongTitle => "Song",
        TextKind.Album => "Album", TextKind.Watermark => "Watermark", TextKind.Credits => "Credits", TextKind.SocialHandle => "Social",
        TextKind.Caption => "Caption", _ => "Text"
    };

    public TextLayer Clone()
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<TextLayer>(System.Text.Json.JsonSerializer.Serialize(this))!;
        c.Id = Guid.NewGuid().ToString("N")[..8];
        return c;
    }

    /// <summary>Sensible starting style for each layer kind.</summary>
    public static TextLayer Create(TextKind kind)
    {
        var l = new TextLayer { Kind = kind };
        switch (kind)
        {
            case TextKind.MainTitle: l.Text = "Main Title"; l.FontSize = 110; l.FontWeight = 700; l.Y = 0.42; l.Shadow.Enabled = true; break;
            case TextKind.Subtitle: l.Text = "Subtitle"; l.FontSize = 48; l.FontWeight = 400; l.Y = 0.54; l.Color = "#D8DCE6"; break;
            case TextKind.Artist: l.Text = "Artist Name"; l.FontSize = 84; l.FontWeight = 800; l.Y = 0.40; l.Shadow.Enabled = true; break;
            case TextKind.SongTitle: l.Text = "Song Title"; l.FontSize = 54; l.FontWeight = 500; l.Y = 0.52; break;
            case TextKind.Album: l.Text = "Album Name"; l.FontSize = 34; l.FontWeight = 300; l.Y = 0.60; l.Color = "#C7CCDA"; l.LetterSpacing = 4; break;
            case TextKind.Watermark: l.Text = "@yourbrand"; l.FontSize = 28; l.Opacity = 0.6; l.X = 0.93; l.Y = 0.94; l.Align = TextAlign.Right; break;
            case TextKind.Credits: l.Text = "Produced by\nYour Name"; l.FontSize = 32; l.Y = 0.84; l.LineSpacing = 1.2; break;
            case TextKind.SocialHandle: l.Text = "@yourhandle"; l.FontSize = 34; l.Y = 0.9; l.Color = "#7DF9FF"; l.Glow = new TextGlow { Enabled = true, Color = "#00AACC", Radius = 10, Intensity = 0.8 }; break;
            case TextKind.Caption:
                l.Text = "Animated caption"; l.FontSize = 56; l.FontWeight = 700; l.Y = 0.78;
                l.Panel = new TextPanel { Enabled = true, Opacity = 0.55, CornerRadius = 14 };
                l.Animations.Add(new TextAnimation { Type = TextAnimType.Typewriter, Duration = 2.0, Easing = Easing.Linear });
                break;
            default: l.Text = "Custom text"; l.FontSize = 64; break;
        }
        return l;
    }
}
