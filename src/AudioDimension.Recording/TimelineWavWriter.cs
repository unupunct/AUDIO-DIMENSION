using System.Diagnostics;
using AudioDimension.Audio.Capture;
using NAudio.Wave;

namespace AudioDimension.Recording;

/// <summary>
/// Writes captured audio to a WAV file on a wall-clock timeline. Loopback devices stop delivering data while nothing plays, so gaps are
/// filled with silence; pausing freezes the clock so the paused span is simply cut out. The file length therefore always equals the
/// recorded duration, which is what keeps exported video and audio in sync.
/// </summary>
public sealed class TimelineWavWriter : IDisposable
{
    private readonly object _lock = new();
    private readonly WaveFileWriter _writer;
    private readonly WaveFormat _format;
    private readonly Stopwatch _clock = new();
    private long _frames;
    private bool _paused, _disposed;
    private const double GapToleranceSeconds = 0.04;

    public string Path { get; }
    public int SampleRate => _format.SampleRate;
    public double DurationSeconds { get { lock (_lock) return (double)_frames / _format.SampleRate; } }
    public long BytesWritten { get { lock (_lock) return _frames * _format.BlockAlign; } }
    public bool IsPaused { get { lock (_lock) return _paused; } }

    /// <summary>Device format is normalised to a plain PCM16 or IEEE-float WAV.</summary>
    public static WaveFormat Normalise(WaveFormat f)
    {
        var s = f;
        if (f is WaveFormatExtensible ext) { try { s = ext.ToStandardWaveFormat(); } catch (InvalidOperationException) { } }
        return s.Encoding == WaveFormatEncoding.IeeeFloat ? WaveFormat.CreateIeeeFloatWaveFormat(s.SampleRate, s.Channels) : new WaveFormat(s.SampleRate, s.BitsPerSample, s.Channels);
    }

    public TimelineWavWriter(string path, WaveFormat deviceFormat)
    {
        Path = path;
        _format = Normalise(deviceFormat);
        _writer = new WaveFileWriter(path, _format);
        _clock.Start();
    }

    public void Write(byte[] buffer, int count)
    {
        lock (_lock)
        {
            if (_disposed || _paused || count <= 0) return;
            long incoming = count / _format.BlockAlign;
            double elapsed = _clock.Elapsed.TotalSeconds;
            long expected = (long)(elapsed * _format.SampleRate);
            long gap = expected - (_frames + incoming);
            if (gap > GapToleranceSeconds * _format.SampleRate) WriteSilence(gap);
            _writer.Write(buffer, 0, (int)(incoming * _format.BlockAlign));
            _frames += incoming;
        }
    }

    private void WriteSilence(long frames)
    {
        var zeros = new byte[Math.Min(frames, 48000) * _format.BlockAlign];
        long left = frames;
        while (left > 0)
        {
            int n = (int)Math.Min(left, 48000);
            _writer.Write(zeros, 0, n * _format.BlockAlign);
            left -= n;
        }
        _frames += frames;
    }

    public void Pause() { lock (_lock) { if (_paused) return; _paused = true; _clock.Stop(); } }
    public void Resume() { lock (_lock) { if (!_paused) return; _paused = false; _clock.Start(); } }

    /// <summary>Pads the tail with silence up to the elapsed time and closes the file.</summary>
    public double Finish()
    {
        lock (_lock)
        {
            if (_disposed) return (double)_frames / _format.SampleRate;
            long expected = (long)(_clock.Elapsed.TotalSeconds * _format.SampleRate);
            if (expected - _frames > GapToleranceSeconds * _format.SampleRate) WriteSilence(expected - _frames);
            _writer.Dispose(); _disposed = true;
            return (double)_frames / _format.SampleRate;
        }
    }

    public void Dispose() { lock (_lock) { if (!_disposed) { _writer.Dispose(); _disposed = true; } } }
}
