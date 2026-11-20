namespace AudioDimension.Recording;

public enum VideoFormat { Mp4H264, Mp4Hevc, WebM }
public enum EncoderPreference { Auto, Software, Nvenc, QuickSync, Amf }

public sealed class ExportOptions
{
    public VideoFormat Format { get; set; } = VideoFormat.Mp4H264;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 30;
    /// <summary>1 (smallest) .. 100 (best).</summary>
    public int Quality { get; set; } = 75;
    public EncoderPreference Encoder { get; set; } = EncoderPreference.Auto;
    public bool IncludeAudio { get; set; } = true;
    public string OutputPath { get; set; } = "";

    public static readonly int[] StandardFps = { 24, 25, 30, 50, 60 };
    public static readonly (string Name, int W, int H)[] StandardSizes =
    {
        ("1280 x 720", 1280, 720), ("1920 x 1080", 1920, 1080), ("2560 x 1440", 2560, 1440), ("3840 x 2160", 3840, 2160)
    };

    public string Extension => Format == VideoFormat.WebM ? ".webm" : ".mp4";

    public static string FormatName(VideoFormat f) => f switch { VideoFormat.Mp4H264 => "MP4 (H.264)", VideoFormat.Mp4Hevc => "MP4 (H.265/HEVC)", _ => "WebM (VP9)" };

    /// <summary>Returns a list of problems (empty = valid). Dimensions are rounded to even numbers by the encoder pipeline.</summary>
    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        if (Width < 128 || Height < 128 || Width > 7680 || Height > 4320) e.Add("Resolution must be between 128x128 and 7680x4320.");
        if (Fps < 1 || Fps > 120) e.Add("Frame rate must be between 1 and 120.");
        if (Quality is < 1 or > 100) e.Add("Quality must be between 1 and 100.");
        if (string.IsNullOrWhiteSpace(OutputPath)) e.Add("Choose an output file.");
        return e;
    }

    public int EvenWidth => Width & ~1;
    public int EvenHeight => Height & ~1;

    /// <summary>Rough video bitrate (kbit/s) used for the file-size estimate.</summary>
    public double EstimatedVideoKbps()
    {
        double bpp = 0.015 + 0.085 * Math.Pow(Quality / 100.0, 1.6);        // bits per pixel per frame
        double k = Format switch { VideoFormat.Mp4Hevc => 0.65, VideoFormat.WebM => 0.75, _ => 1.0 };
        return EvenWidth * (double)EvenHeight * Fps * bpp * k / 1000.0 * 0.35;   // synthetic graphics compress well
    }

    public double EstimatedAudioKbps => IncludeAudio ? (Format == VideoFormat.WebM ? 160 : 192) : 0;

    /// <summary>Estimated final file size in bytes for the given duration.</summary>
    public long EstimateBytes(double seconds) => (long)((EstimatedVideoKbps() + EstimatedAudioKbps) * 1000.0 / 8.0 * Math.Max(0, seconds));
}

public sealed record ExportProgress(string Stage, long Frame, long TotalFrames, TimeSpan Elapsed, TimeSpan? Remaining, double RenderFps)
{
    public double Fraction => TotalFrames <= 0 ? 0 : Math.Clamp((double)Frame / TotalFrames, 0, 1);
}

public sealed record ExportResult(string Path, long Frames, double DurationSeconds, long SizeBytes, TimeSpan Elapsed, string EncoderName);

/// <summary>Builds the FFmpeg command line. Pure function so it can be unit tested without starting a process.</summary>
public static class FfmpegArgs
{
    public static int CrfFor(int quality) => Math.Clamp((int)Math.Round(34 - 0.17 * quality), 14, 36);

    public static string ResolveEncoder(VideoFormat format, EncoderPreference pref, EncoderCapabilities caps)
    {
        if (format == VideoFormat.WebM) return "libvpx-vp9";
        bool hevc = format == VideoFormat.Mp4Hevc;
        string sw = hevc ? "libx265" : "libx264";
        string nv = hevc ? "hevc_nvenc" : "h264_nvenc", qsv = hevc ? "hevc_qsv" : "h264_qsv", amf = hevc ? "hevc_amf" : "h264_amf";
        string? Pick(string name) => caps.Working.Contains(name) ? name : null;
        return pref switch
        {
            EncoderPreference.Software => sw,
            EncoderPreference.Nvenc => Pick(nv) ?? sw,
            EncoderPreference.QuickSync => Pick(qsv) ?? sw,
            EncoderPreference.Amf => Pick(amf) ?? sw,
            _ => Pick(nv) ?? Pick(qsv) ?? Pick(amf) ?? sw,
        };
    }

    public static List<string> Build(ExportOptions o, string encoder, string? audioPath)
    {
        int crf = CrfFor(o.Quality);
        var a = new List<string>
        {
            "-y", "-hide_banner", "-nostats", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "rgba", "-video_size", $"{o.EvenWidth}x{o.EvenHeight}", "-framerate", o.Fps.ToString(), "-i", "pipe:0"
        };
        bool audio = o.IncludeAudio && audioPath is not null;
        if (audio) { a.Add("-i"); a.Add(audioPath!); }
        a.AddRange(new[] { "-map", "0:v:0" });
        if (audio) a.AddRange(new[] { "-map", "1:a:0" });
        a.AddRange(new[] { "-vf", "vflip,format=yuv420p", "-r", o.Fps.ToString() });       // GL reads bottom-up, so flip here instead of on the CPU
        switch (encoder)
        {
            case "libx264": a.AddRange(new[] { "-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(), "-pix_fmt", "yuv420p" }); break;
            case "libx265": a.AddRange(new[] { "-c:v", "libx265", "-preset", "medium", "-crf", (crf + 3).ToString(), "-tag:v", "hvc1", "-pix_fmt", "yuv420p", "-x265-params", "log-level=error" }); break;
            case "libvpx-vp9": a.AddRange(new[] { "-c:v", "libvpx-vp9", "-crf", (crf + 4).ToString(), "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p" }); break;
            case "h264_nvenc": case "hevc_nvenc":
                a.AddRange(new[] { "-c:v", encoder, "-preset", "p5", "-rc", "vbr", "-cq", crf.ToString(), "-b:v", "0", "-pix_fmt", "yuv420p" });
                if (encoder == "hevc_nvenc") a.AddRange(new[] { "-tag:v", "hvc1" });
                break;
            case "h264_qsv": case "hevc_qsv":
                a.AddRange(new[] { "-c:v", encoder, "-global_quality", crf.ToString(), "-pix_fmt", "yuv420p" });
                if (encoder == "hevc_qsv") a.AddRange(new[] { "-tag:v", "hvc1" });
                break;
            case "h264_amf": case "hevc_amf":
                a.AddRange(new[] { "-c:v", encoder, "-quality", "quality", "-rc", "cqp", "-qp_i", crf.ToString(), "-qp_p", crf.ToString(), "-pix_fmt", "yuv420p" });
                if (encoder == "hevc_amf") a.AddRange(new[] { "-tag:v", "hvc1" });
                break;
            default: throw new ExportException($"Unsupported encoder '{encoder}'.");
        }
        if (audio)
        {
            a.AddRange(new[] { "-ac", "2" });                       // surround captures (5.1 loopback devices) are downmixed to stereo for compatibility
            if (o.Format == VideoFormat.WebM) a.AddRange(new[] { "-c:a", "libopus", "-b:a", "160k" });
            else a.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
            a.Add("-shortest");
        }
        else a.Add("-an");
        if (o.Format != VideoFormat.WebM) a.AddRange(new[] { "-movflags", "+faststart" });
        a.Add(o.OutputPath);
        return a;
    }
}
