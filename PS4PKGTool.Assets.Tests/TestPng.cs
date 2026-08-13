namespace PS4PKGTool.Assets.Tests;

/// <summary>Minimal valid PNG generator shared by tests.</summary>
public static class TestPng
{
    public static byte[] Small(int width = 4, int height = 2)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(ms, "IHDR", BitConverter.GetBytes((uint)width).Reverse()
            .Concat(BitConverter.GetBytes((uint)height).Reverse())
            .Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray());

        byte[] raw = new byte[(1 + width * 4) * height];
        for (int r = 0; r < height; r++)
            raw[r * (1 + width * 4)] = 0;

        using var outMs = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(outMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);
        WriteChunk(ms, "IDAT", outMs.ToArray());
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        s.Write(BitConverter.GetBytes((uint)data.Length).Reverse().ToArray());
        s.Write(System.Text.Encoding.ASCII.GetBytes(type));
        s.Write(data);
        uint crc = Crc32(System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray());
        s.Write(BitConverter.GetBytes(crc).Reverse().ToArray());
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
