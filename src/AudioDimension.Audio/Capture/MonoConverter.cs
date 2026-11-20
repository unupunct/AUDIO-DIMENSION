using NAudio.Wave;

namespace AudioDimension.Audio.Capture;

public static class MonoConverter
{
    /// <summary>Converts interleaved PCM16/24/32 or float32 bytes to mono floats in -1..1.</summary>
    public static float[] ToMono(byte[] buffer, int bytes, WaveFormat format)
    {
        var fmt = format;
        if (fmt is WaveFormatExtensible ext)
        {
            try { fmt = ext.ToStandardWaveFormat(); } catch (InvalidOperationException) { }
        }
        int ch = Math.Max(1, format.Channels);
        int bps = format.BitsPerSample / 8;
        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
        int frames = bytes / (bps * ch);
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < ch; c++)
            {
                int o = (f * ch + c) * bps;
                sum += bps switch
                {
                    4 when isFloat => BitConverter.ToSingle(buffer, o),
                    4 => BitConverter.ToInt32(buffer, o) / 2147483648f,
                    3 => ((buffer[o] << 8) | (buffer[o + 1] << 16) | (buffer[o + 2] << 24)) / 2147483648f,
                    2 => BitConverter.ToInt16(buffer, o) / 32768f,
                    1 => (buffer[o] - 128) / 128f,
                    _ => 0f
                };
            }
            mono[f] = sum / ch;
        }
        return mono;
    }
}
