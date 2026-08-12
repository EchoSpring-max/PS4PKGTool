using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Codecs;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.IO;
using PS4PKGTool.Assets.Models;

namespace PS4PKGTool.Assets.Tests;

[TestClass]
public class Phase1GenericFormatsTests
{
    private readonly AssetInspectionService _service = GenericAssetRegistryBuilder.Build();

    private static IAssetSource Bytes(byte[] data, string name) => new MemoryAssetSource(data, name, "test");

    // ── PNG detection + preview ──────────────────────────────────────────
    [TestMethod]
    public async Task Png_Detect_Inspect_Preview()
    {
        byte[] png = CreateMinimalPng(4, 2);
        var source = Bytes(png, "icon.png");

        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("png", descriptor!.Format);
        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.Preview));

        var preview = await _service.TryPreviewAsync(source, new AssetDetectionResult { Format = "png" });
        Assert.IsNotNull(preview);
        Assert.IsNotNull(preview!.Texture);
        Assert.AreEqual(4, preview.Texture!.Width);
        Assert.AreEqual(2, preview.Texture.Height);
    }

    // ── DDS BC1 decode + preview ─────────────────────────────────────────
    [TestMethod]
    public async Task Dds_Detect_Inspect_Preview()
    {
        byte[] dds = CreateBc1Dds(8, 8);
        var source = Bytes(dds, "tex.dds");

        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("dds", descriptor!.Format);
        Assert.AreEqual("8", descriptor!.Metadata["Width"]);
        Assert.AreEqual("8", descriptor.Metadata["Height"]);

        var preview = await _service.TryPreviewAsync(source, new AssetDetectionResult { Format = "dds" });
        Assert.IsNotNull(preview);
        Assert.IsNotNull(preview!.Texture);
        Assert.AreEqual(8, preview.Texture!.Width);
        Assert.AreEqual(8, preview.Texture.Height);
        Assert.AreEqual(8 * 8 * 4, preview.Texture.Rgba8.Length);
    }

    // ── DDS unsupported BC7 → structured error, raw still available ───────
    [TestMethod]
    public async Task Dds_UnsupportedFormat_StructuredError_RawExportWorks()
    {
        byte[] dds = CreateDdsWithFourCc(8, 8, "DX10", dx10Format: 80); // BC7
        var source = Bytes(dds, "tex.dds");

        var descriptor = await _service.InspectAsync(source);
        Assert.AreEqual("dds", descriptor!.Format);

        await Assert.ThrowsAsync<UnsupportedAssetException>(
            () => _service.TryPreviewAsync(source, new AssetDetectionResult { Format = "dds" }));

        // Raw export still available per capability model.
        Assert.IsTrue(descriptor!.Capabilities.HasFlag(AssetCapabilities.ExportRaw));
    }

    // ── WAV metadata ─────────────────────────────────────────────────────
    [TestMethod]
    public async Task Wav_Detect_Inspect_Metadata()
    {
        byte[] wav = CreateMinimalWav(44100, 2, 16, samples: 100);
        var source = Bytes(wav, "sound.wav");

        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("wav", descriptor!.Format);
        Assert.AreEqual("44100 Hz", descriptor!.Metadata["Sample Rate"]);
        Assert.AreEqual("2", descriptor.Metadata["Channels"]);
        Assert.AreEqual("16-bit", descriptor.Metadata["Bit Depth"]);
    }

    // ── OGG detection ────────────────────────────────────────────────────
    [TestMethod]
    public async Task Ogg_Detect_Inspect()
    {
        var source = Bytes(new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x44, 0xAC, 0x00, 0x00, 0x02 }, "voice.ogg");
        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("ogg", descriptor!.Format);
        Assert.AreEqual("44100 Hz", descriptor!.Metadata["Sample Rate"]);
    }

    // ── Text detection + preview ─────────────────────────────────────────
    [TestMethod]
    public async Task Text_Detect_Preview()
    {
        var source = Bytes(System.Text.Encoding.UTF8.GetBytes("param=1\nname=\"test\"\n"), "config.txt");
        var descriptor = await _service.InspectAsync(source);
        Assert.AreEqual("text", descriptor!.Format);

        var preview = await _service.TryPreviewAsync(source, new AssetDetectionResult { Format = "text" });
        Assert.IsNotNull(preview);
        Assert.IsTrue(preview!.Text!.Contains("param=1"));
    }

    // ── Unknown binary → extension fallback + raw export ─────────────────
    [TestMethod]
    public async Task UnknownBinary_FallbackAndRaw()
    {
        var source = Bytes(new byte[] { 0x01, 0x02, 0x00, 0x03 }, "model.psk");
        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("psk", descriptor!.Format);
        Assert.IsTrue(descriptor!.Capabilities.HasFlag(AssetCapabilities.ExportRaw));
        Assert.IsFalse(descriptor.Capabilities.HasFlag(AssetCapabilities.Preview));
    }

    // ── Helpers: minimal valid files ─────────────────────────────────────

    private static byte[] CreateMinimalPng(int width, int height)
    {
        // 1x1-style minimal PNG: signature + IHDR + IDAT (zlib) + IEND.
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(ms, "IHDR", BitConverter.GetBytes((uint)width).Reverse()
            .Concat(BitConverter.GetBytes((uint)height).Reverse())
            .Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray());
        // IDAT: zlib stream; each row = 1 filter byte (0) + width*4 RGBA bytes.
        byte[] raw = new byte[(1 + width * 4) * height];
        for (int r = 0; r < height; r++)
            raw[r * (1 + width * 4)] = 0; // filter type None
        var deflated = Deflate(raw);
        WriteChunk(ms, "IDAT", deflated);
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

    private static byte[] Deflate(byte[] raw)
    {
        using var outMs = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(outMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);
        return outMs.ToArray();
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

    private static byte[] CreateBc1Dds(int width, int height)
    {
        var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DDS "));
        ms.Write(new byte[124]);
        var buf = ms.ToArray();
        BitConverter.GetBytes(124).CopyTo(buf, 4);       // header size
        BitConverter.GetBytes(0x1007).CopyTo(buf, 8);    // flags: DDSD_CAPS|HEIGHT|WIDTH|PIXELFORMAT
        BitConverter.GetBytes(height).CopyTo(buf, 12);
        BitConverter.GetBytes(width).CopyTo(buf, 16);
        BitConverter.GetBytes(width * height / 2).CopyTo(buf, 20); // pitch
        BitConverter.GetBytes(32).CopyTo(buf, 76);       // DDS_PIXELFORMAT.dwSize
        System.Text.Encoding.ASCII.GetBytes("DXT1").CopyTo(buf, 84); // dwFourCC
        // Payload: one 8-byte block per 4x4 tile — two colors + indices.
        int blocksW = (width + 3) / 4, blocksH = (height + 3) / 4;
        var block = new byte[8];
        block[0] = 0xFF; block[1] = 0x7F; // c0 (white-ish)
        block[2] = 0x00; block[3] = 0x00; // c1 (black)
        block[4] = 0xE4; block[5] = 0xFF; block[6] = 0xFF; block[7] = 0xFF;
        var payload = new byte[blocksW * blocksH * 8];
        for (int i = 0; i < blocksW * blocksH; i++)
            block.CopyTo(payload, i * 8);
        var result = new byte[buf.Length + payload.Length];
        buf.CopyTo(result, 0);
        payload.CopyTo(result, buf.Length);
        return result;
    }

    private static byte[] CreateDdsWithFourCc(int width, int height, string fourCc, int dx10Format = 0)
    {
        var dds = CreateBc1Dds(width, height);
        System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(dds, 84); // dwFourCC
        if (fourCc == "DX10")
        {
            var dx10 = new byte[dds.Length + 20];
            dds.CopyTo(dx10, 0);
            BitConverter.GetBytes(dx10Format).CopyTo(dx10, 128); // DXGI format
            return dx10;
        }
        return dds;
    }

    private static byte[] CreateMinimalWav(int sampleRate, short channels, short bits, int samples)
    {
        var ms = new MemoryStream();
        void FourCc(string s) => ms.Write(System.Text.Encoding.ASCII.GetBytes(s));
        void U32(uint v) => ms.Write(BitConverter.GetBytes(v));
        void U16(ushort v) => ms.Write(BitConverter.GetBytes(v));

        int dataSize = samples * channels * (bits / 8);
        FourCc("RIFF"); U32((uint)(36 + dataSize)); FourCc("WAVE");
        FourCc("fmt "); U32(16); U16(1); U16((ushort)channels); U32((uint)sampleRate);
        U32((uint)(sampleRate * channels * (bits / 8))); U16((ushort)(channels * (bits / 8))); U16((ushort)bits);
        FourCc("data"); U32((uint)dataSize);
        ms.Write(new byte[dataSize]);
        return ms.ToArray();
    }
}
