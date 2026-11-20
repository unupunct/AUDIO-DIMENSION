using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;

namespace AudioDimension.Recording;

/// <summary>Raised for failures the user can understand (missing FFmpeg, no disk space, encoder failure...). Technical detail is logged separately.</summary>
public sealed class ExportException : Exception
{
    public ExportException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record FfmpegInfo(string Path, string Version);

public static class FfmpegLocator
{
    /// <summary>Optional explicit location (settings). Checked first.</summary>
    public static string? CustomPath { get; set; }

    /// <summary>Looks next to the app (Tools\ffmpeg), then walks up from the app folder to the repo's third_party\ffmpeg\bin for development.</summary>
    /// <summary>Test hook: replaces the search (return null to simulate FFmpeg being absent).</summary>
    public static Func<string?>? FinderOverride { get; set; }

    public static string? FindExecutable()
    {
        if (FinderOverride is not null) return FinderOverride();
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(CustomPath)) candidates.Add(CustomPath!);
        candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", "ffmpeg.exe"));
        candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"));
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            candidates.Add(System.IO.Path.Combine(d.FullName, "third_party", "ffmpeg", "bin", "ffmpeg.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Finds FFmpeg and confirms it runs. Throws a readable <see cref="ExportException"/> when it is missing or broken.</summary>
    public static FfmpegInfo Require()
    {
        var path = FindExecutable();
        if (path is null)
            throw new ExportException("FFmpeg was not found. Video export needs the bundled FFmpeg components (Tools\\ffmpeg\\ffmpeg.exe). Reinstall Audio Dimension or run tools\\get-ffmpeg.ps1.");
        try
        {
            var r = ProcessRunner.Run(path, new[] { "-hide_banner", "-version" }, 15000);
            var m = Regex.Match(r.Stdout, @"ffmpeg version (\S+)");
            if (r.ExitCode != 0 || !m.Success) throw new ExportException("FFmpeg is present but did not start correctly. Some of its component files may be missing or damaged.");
            return new FfmpegInfo(path, m.Groups[1].Value);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error(ex, "FFmpeg could not be started from {Path}", path);
            throw new ExportException("FFmpeg could not be started. Its component files may be blocked by security software or damaged.", ex);
        }
    }
}

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

public static class ProcessRunner
{
    public static ProcessResult Run(string exe, IEnumerable<string> args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + exe);
        var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch (InvalidOperationException) { } return new ProcessResult(-1, so.Result, "timed out"); }
        return new ProcessResult(p.ExitCode, so.Result, se.Result);
    }
}

/// <summary>Detects which encoders this FFmpeg build and this PC can really use (hardware encoders are test-encoded, not just listed).</summary>
public sealed class EncoderCapabilities
{
    private static EncoderCapabilities? _cache;
    public HashSet<string> Listed { get; init; } = new();
    public HashSet<string> Working { get; init; } = new();

    public static EncoderCapabilities Probe(string ffmpeg, bool refresh = false)
    {
        if (_cache is not null && !refresh) return _cache;
        var listed = new HashSet<string>(); var working = new HashSet<string>();
        try
        {
            var r = ProcessRunner.Run(ffmpeg, new[] { "-hide_banner", "-encoders" }, 20000);
            foreach (Match m in Regex.Matches(r.Stdout, @"^\s[VASFXBD.]{6}\s(\S+)", RegexOptions.Multiline)) listed.Add(m.Groups[1].Value);
            foreach (var sw in new[] { "libx264", "libx265", "libvpx-vp9", "aac", "libopus" }) if (listed.Contains(sw)) working.Add(sw);
            foreach (var hw in new[] { "h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_amf", "hevc_amf" })
            {
                if (!listed.Contains(hw)) continue;
                var t = ProcessRunner.Run(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=256x256:d=0.2", "-frames:v", "2", "-pix_fmt", "yuv420p", "-c:v", hw, "-f", "null", "-" }, 15000);
                if (t.ExitCode == 0) working.Add(hw);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Encoder probe failed"); }
        return _cache = new EncoderCapabilities { Listed = listed, Working = working };
    }
}
