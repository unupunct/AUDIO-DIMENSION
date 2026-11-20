namespace AudioDimension.Visuals.Text;

public sealed class TitleTemplate
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<TextLayer> Layers { get; set; } = new();
    public bool IsCustom { get; set; }
    public override string ToString() => Name;
}

/// <summary>Ready-made title styles. Each is just data: a list of text layers with animations.</summary>
public static class TitleTemplates
{
    private static readonly Lazy<IReadOnlyList<TitleTemplate>> Cache = new(Build);
    public static IReadOnlyList<TitleTemplate> All => Cache.Value;

    private static TextAnimation A(TextAnimType t, double dur = 1, double delay = 0, Easing e = Easing.EaseOut, bool loop = false, double amount = 1, AnimDirection dir = AnimDirection.Forward)
        => new() { Type = t, Duration = dur, Delay = delay, Easing = e, Loop = loop, Amount = amount, Direction = dir };

    private static TextLayer L(TextKind kind, string text, double size, double y, string font = "Segoe UI", int weight = 400, string color = "#FFFFFF", double spacing = 0, double x = 0.5, params TextAnimation[] anims)
    {
        var l = new TextLayer { Kind = kind, Text = text, FontSize = size, Y = y, X = x, FontFamily = font, FontWeight = weight, Color = color, LetterSpacing = spacing };
        foreach (var a in anims) l.Animations.Add(a);
        return l;
    }

    private static IReadOnlyList<TitleTemplate> Build()
    {
        var list = new List<TitleTemplate>();
        void Add(string name, string desc, params TextLayer[] layers) => list.Add(new TitleTemplate { Name = name, Description = desc, Layers = layers.ToList() });

        {
            var t = L(TextKind.MainTitle, "YOUR TITLE", 104, 0.45, "Georgia", 300, "#EDE6D3", 20, 0.5, A(TextAnimType.BlurReveal, 2.2, 0.3, Easing.EaseOut), A(TextAnimType.ZoomIn, 6, 0, Easing.Linear, amount: 0.15));
            t.Shadow = new TextShadow { Enabled = true, Opacity = 0.7, Blur = 18, OffsetX = 0, OffsetY = 6 };
            var s = L(TextKind.Subtitle, "A FILM BY YOU", 28, 0.57, "Georgia", 400, "#B9B09A", 12, 0.5, A(TextAnimType.FadeIn, 1.4, 1.8));
            Add("Cinematic Intro", "Widescreen serif title that slowly resolves out of a blur.", t, s);
        }
        {
            var artist = L(TextKind.Artist, "ARTIST NAME", 120, 0.40, "Segoe UI", 800, "#FFFFFF", 4, 0.5, A(TextAnimType.ZoomIn, 0.9, 0.1, Easing.Back));
            artist.Shadow = new TextShadow { Enabled = true, Opacity = 0.6, Blur = 12, OffsetX = 0, OffsetY = 5 };
            var song = L(TextKind.SongTitle, "Song Title", 56, 0.54, "Segoe UI", 500, "#E4E4EE", 2, 0.5, A(TextAnimType.SlideUp, 0.8, 0.6, Easing.EaseOut));
            var album = L(TextKind.Album, "ALBUM NAME", 28, 0.62, "Segoe UI", 300, "#A9AEC2", 10, 0.5, A(TextAnimType.FadeIn, 1.0, 1.2));
            Add("Music Artist", "Big artist name with song and album that slide in beneath it.", artist, song, album);
        }
        {
            var title = L(TextKind.MainTitle, "Podcast Title", 84, 0.42, "Segoe UI", 700, "#FFFFFF", 0, 0.08, A(TextAnimType.SlideRight, 0.8, 0, Easing.EaseOut, amount: 0.8), A(TextAnimType.LightSweep, 2.4, 1.2, Easing.EaseInOut, true));
            title.Align = TextAlign.Left; title.Panel = new TextPanel { Enabled = true, Color = "#101020", Opacity = 0.7, CornerRadius = 16 }; title.Padding = 28;
            title.Border = new TextBorder { Enabled = true, Color = "#7C4DFF", Width = 3 };
            var ep = L(TextKind.Subtitle, "Episode 01  -  Guest Name", 38, 0.57, "Segoe UI", 400, "#CFD2E6", 1, 0.08, A(TextAnimType.FadeIn, 0.8, 1.0));
            ep.Align = TextAlign.Left;
            Add("Podcast Title", "Left-aligned show title in a framed panel with a light sweep.", title, ep);
        }
        {
            var name = L(TextKind.MainTitle, "RADIO 101", 118, 0.44, "Segoe UI", 800, "#FFFFFF", 6, 0.5, A(TextAnimType.SlideDown, 0.7, 0, Easing.Bounce));
            name.Italic = true; name.Panel = new TextPanel { Enabled = true, Color = "#E53935", Opacity = 0.9, CornerRadius = 8 }; name.Padding = 26;
            var air = L(TextKind.Caption, "ON AIR", 40, 0.62, "Segoe UI", 800, "#FF5252", 12, 0.5, A(TextAnimType.Pulse, 1.0, 0.8, Easing.Linear, true), A(TextAnimType.NeonFlicker, 3, 1.0, Easing.Linear, true));
            air.Glow = new TextGlow { Enabled = true, Color = "#FF1744", Radius = 18, Intensity = 1.2 };
            Add("Radio Show", "Station banner with a pulsing ON AIR light.", name, air);
        }
        {
            var t = L(TextKind.MainTitle, "Minimal", 64, 0.5, "Segoe UI", 200, "#F2F2F2", 10, 0.5, A(TextAnimType.FadeIn, 1.6, 0.2, Easing.EaseInOut));
            Add("Minimal Title", "A single thin line of text that fades in. Nothing else.", t);
        }
        {
            var t = L(TextKind.MainTitle, "NEON NIGHTS", 110, 0.46, "Segoe UI", 700, "#E8FFFF", 8, 0.5, A(TextAnimType.FadeIn, 0.6, 0.2), A(TextAnimType.NeonFlicker, 4, 1.0, Easing.Linear, true));
            t.Glow = new TextGlow { Enabled = true, Color = "#00E5FF", Radius = 26, Intensity = 1.6 };
            t.Outline = new TextOutline { Enabled = true, Color = "#FF2BD6", Width = 1.5 };
            var s = L(TextKind.Subtitle, "LIVE SESSION", 32, 0.58, "Segoe UI", 500, "#FF8AE8", 14, 0.5, A(TextAnimType.FadeIn, 0.8, 1.0));
            s.Glow = new TextGlow { Enabled = true, Color = "#FF2BD6", Radius = 14, Intensity = 1 };
            Add("Neon Title", "Glowing neon tube lettering with a nervous flicker.", t, s);
        }
        {
            var t = L(TextKind.MainTitle, "CYBER//PUNK", 112, 0.46, "Consolas", 800, "#FCEE09", 4, 0.5, A(TextAnimType.Glitch, 3, 0.4, Easing.Linear, true), A(TextAnimType.RgbSplit, 2, 0, Easing.Linear, true, 0.6));
            t.Outline = new TextOutline { Enabled = true, Color = "#FF003C", Width = 2 };
            t.Panel = new TextPanel { Enabled = true, Color = "#000000", Opacity = 0.65, CornerRadius = 0 }; t.Padding = 30;
            t.Border = new TextBorder { Enabled = true, Color = "#00F0FF", Width = 2 };
            var s = L(TextKind.Caption, "> SYSTEM ONLINE_", 34, 0.6, "Consolas", 500, "#00F0FF", 3, 0.5, A(TextAnimType.Typewriter, 1.6, 0.6, Easing.Linear));
            Add("Cyberpunk Title", "Yellow-on-black terminal title with glitch and RGB split.", t, s);
        }
        {
            var t = L(TextKind.MainTitle, "FUTURE", 108, 0.45, "Bahnschrift", 500, "#CFF8FF", 24, 0.5, A(TextAnimType.SlideRight, 1.0, 0, Easing.EaseOut), A(TextAnimType.LightSweep, 3.0, 1.2, Easing.EaseInOut, true));
            t.Glow = new TextGlow { Enabled = true, Color = "#4FC3F7", Radius = 20, Intensity = 1 };
            var s = L(TextKind.Subtitle, "NEXT GENERATION SOUND", 28, 0.57, "Bahnschrift", 300, "#7DD3FC", 16, 0.5, A(TextAnimType.LetterReveal, 1.8, 0.9, Easing.Linear));
            Add("Futuristic Title", "Wide, techy lettering that slides in and gets swept by light.", t, s);
        }
        {
            var t = L(TextKind.MainTitle, "Elegance", 120, 0.45, "Georgia", 400, "#D4AF37", 4, 0.5, A(TextAnimType.FadeIn, 1.8, 0.2, Easing.EaseInOut), A(TextAnimType.Floating, 6, 0, Easing.Linear, true, 0.4));
            t.Italic = true; t.Shadow = new TextShadow { Enabled = true, Opacity = 0.5, Blur = 10, OffsetX = 0, OffsetY = 4 };
            var s = L(TextKind.Subtitle, "A COLLECTION OF SOUND", 26, 0.58, "Georgia", 400, "#E9DDB5", 14, 0.5, A(TextAnimType.FadeIn, 1.6, 1.4));
            s.Border = new TextBorder { Enabled = true, Color = "#D4AF37", Width = 1.5, Opacity = 0.8 }; s.Padding = 18;
            Add("Elegant Typography", "Italic serif in gold with a gentle float.", t, s);
        }
        {
            var t = L(TextKind.MainTitle, "GLITCH", 150, 0.46, "Segoe UI", 900, "#FFFFFF", 6, 0.5, A(TextAnimType.Glitch, 2.4, 0, Easing.Linear, true), A(TextAnimType.RgbSplit, 1.2, 0, Easing.Linear, true, 1.1), A(TextAnimType.ZoomIn, 0.4, 0, Easing.Back));
            var c = L(TextKind.Caption, "SIGNAL LOST", 30, 0.62, "Consolas", 600, "#FF4D6D", 10, 0.5, A(TextAnimType.NeonFlicker, 2, 0.5, Easing.Linear, true));
            Add("Glitch Intro", "Heavy block title that tears and splits colour channels.", t, c);
        }
        return list;
    }
}
