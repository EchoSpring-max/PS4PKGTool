using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Models;

namespace PS4PKGTool.Assets.Tests;

[TestClass]
public class Phase2APs4NativeTests
{
    private readonly AssetInspectionService _service = GenericAssetRegistryBuilder.Build();

    private static IAssetSource Bytes(byte[] data, string name) => new MemoryAssetSource(data, name, "test");

    // ── GNF: detect, metadata, capability model ───────────────────────────
    [TestMethod]
    public async Task Gnf_Detect_Metadata_Capabilities()
    {
        var gnf = CreateGnfHeader(width: 1920, height: 1080, format: 0x1B /* BC1 */, mips: 6);
        var source = Bytes(gnf, "texture.gnf");

        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("gnf", descriptor!.Format);
        Assert.AreEqual("1920", descriptor.Metadata["Width"]);
        Assert.AreEqual("1080", descriptor.Metadata["Height"]);
        Assert.AreEqual("BC1_UNORM", descriptor.Metadata["Pixel Format"]);
        Assert.AreEqual("6", descriptor.Metadata["Mip Maps"]);

        // Phase 2A: no decode — Preview is metadata text, ExportRaw always works.
        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.Preview));
        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.ExportRaw));
        Assert.IsFalse(descriptor.Capabilities.HasFlag(AssetCapabilities.Decode));

        var preview = await _service.TryPreviewAsync(source, new AssetDetectionResult { Format = "gnf" });
        Assert.IsNotNull(preview);
        Assert.IsNotNull(preview!.Text);
        Assert.IsTrue(preview.Text.Contains("BC1_UNORM"));
    }

    [TestMethod]
    public async Task Gnf_UnknownFormat_StillInspectable()
    {
        var gnf = CreateGnfHeader(width: 64, height: 64, format: 0xFF /* unknown */, mips: 1);
        var source = Bytes(gnf, "weird.gnf");
        var descriptor = await _service.InspectAsync(source);
        Assert.AreEqual("gnf", descriptor!.Format);
        Assert.AreEqual("0xFF", descriptor.Metadata["Pixel Format"]);
    }

    // ── ATRAC9: detect, metadata ──────────────────────────────────────────
    [TestMethod]
    public async Task Atrac9_Detect_Metadata()
    {
        var at9 = CreateAt9Header(channels: 2, rateIndex: 7 /* 44100 */, bitrateIndex: 4, superframeSize: 512, frameSize: 128);
        var source = Bytes(at9, "bgm.at9");

        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("atrac9", descriptor!.Format);
        Assert.AreEqual("2", descriptor!.Metadata["Channels"]);
        Assert.AreEqual("44100 Hz", descriptor.Metadata["Sample Rate"]);
        Assert.AreEqual("512", descriptor.Metadata["Superframe Size"]);

        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.ExportRaw));
        Assert.IsFalse(descriptor.Capabilities.HasFlag(AssetCapabilities.Decode));

        var preview = await _service.TryPreviewAsync(source, new AssetDetectionResult { Format = "atrac9" });
        Assert.IsNotNull(preview);
        Assert.IsTrue(preview!.Text!.Contains("44100 Hz"));
    }

    [TestMethod]
    public async Task Atrac9_BadHeader_StructuredError()
    {
        var source = Bytes(new byte[] { 0x41, 0x54, 0x39 }, "truncated.at9"); // magic only
        var ex = await Assert.ThrowsAsync<CorruptAssetException>(
            () => _service.InspectAsync(source));
        Assert.IsInstanceOfType<AssetException>(ex);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static byte[] CreateGnfHeader(uint width, uint height, uint format, uint mips)
    {
        var b = new byte[128];
        b[0] = (byte)'G'; b[1] = (byte)'N'; b[2] = (byte)'F'; b[3] = 0;
        BitConverter.GetBytes(0x000003F1u).CopyTo(b, 4);   // version
        BitConverter.GetBytes(2u).CopyTo(b, 8);            // texture type: 2D
        BitConverter.GetBytes(format).CopyTo(b, 12);
        BitConverter.GetBytes(width).CopyTo(b, 16);
        BitConverter.GetBytes(height).CopyTo(b, 20);
        BitConverter.GetBytes(1u).CopyTo(b, 24);           // depth
        BitConverter.GetBytes(mips).CopyTo(b, 28);
        return b;
    }

    private static byte[] CreateAt9Header(byte channels, byte rateIndex, byte bitrateIndex, ushort superframeSize, ushort frameSize)
    {
        var b = new byte[48];
        b[0] = (byte)'A'; b[1] = (byte)'T'; b[2] = (byte)'9';
        b[3] = 1;                                          // version
        b[4] = 1; b[5] = 0;                                // flags: config present
        b[6] = channels;
        b[7] = rateIndex;
        b[8] = bitrateIndex;
        BitConverter.GetBytes(superframeSize).CopyTo(b, 10);
        BitConverter.GetBytes(frameSize).CopyTo(b, 12);
        return b;
    }
}
