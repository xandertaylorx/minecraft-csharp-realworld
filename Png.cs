using System.IO.Compression;
namespace McClone;

public static class Png
{
    static uint[] table;
    static uint Crc(byte[] d, int off, int len, uint crc = 0xFFFFFFFF)
    {
        if (table == null)
        {
            table = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; table[n] = c; }
        }
        for (int i = 0; i < len; i++) crc = table[(crc ^ d[off + i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; len[0] = (byte)(data.Length >> 24); len[1] = (byte)(data.Length >> 16); len[2] = (byte)(data.Length >> 8); len[3] = (byte)data.Length;
        s.Write(len);
        var tb = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(tb); s.Write(data);
        uint crc = Crc(tb, 0, 4);
        crc = Crc(data, 0, data.Length, crc) ^ 0xFFFFFFFF;
        s.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
    }
    /// <summary>rgba is top-down.</summary>
    public static void Save(string path, int w, int h, byte[] rgba)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
        ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }
}
