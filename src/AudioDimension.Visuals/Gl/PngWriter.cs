using System.IO.Compression;

namespace AudioDimension.Visuals.Gl;

/// <summary>Dependency-free PNG encoder (8-bit RGBA) used for screenshots and thumbnails.</summary>
public static class PngWriter
{
    private static readonly uint[] Crc = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }

    public static void Save(string path, byte[] rgba, int width, int height)
    {
        using var fs = File.Create(path);
        Write(fs, rgba, width, height);
    }

    public static void Write(Stream s, byte[] rgba, int width, int height)
    {
        s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)width); BE(ihdr, 4, (uint)height); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(s, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
        {
            var zero = new byte[] { 0 };
            for (int y = 0; y < height; y++) { z.Write(zero); z.Write(rgba, y * width * 4, width * 4); }
        }
        Chunk(s, "IDAT", raw.ToArray());
        Chunk(s, "IEND", Array.Empty<byte>());
    }

    private static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var b in t) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        var crc = new byte[4]; BE(crc, 0, c ^ 0xFFFFFFFF); s.Write(crc);
    }
}
