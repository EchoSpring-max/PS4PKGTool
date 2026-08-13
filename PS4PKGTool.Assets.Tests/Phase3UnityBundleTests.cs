using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Models;

namespace PS4PKGTool.Assets.Tests;

[TestClass]
public class Phase3UnityBundleTests
{
    private readonly AssetInspectionService _service = GenericAssetRegistryBuilder.Build();

    private static IAssetSource Bytes(byte[] data, string name) => new MemoryAssetSource(data, name, "test");

    // ── UnityFS (uncompressed, blocks at start, v7): detect → browse → member inspect ──
    [TestMethod]
    public async Task UnityBundle_Detect_Browse_ListMembers()
    {
        byte[] png = TestPng.Small();
        byte[] bundle = BuildUnityFs(new Dictionary<string, byte[]>
        {
            ["CAB-abc123"] = png,
            ["assets/res/texture.bytes"] = new byte[] { 0x41, 0x42, 0x43 },
        });

        var source = Bytes(bundle, "level1.bundle");



        var descriptor = await _service.InspectAsync(source);
        Assert.IsNotNull(descriptor);
        Assert.AreEqual("unity-bundle", descriptor!.Format);
        Assert.AreEqual("unity", descriptor.Engine);
        Assert.AreEqual("2", descriptor.Metadata["Files"]);
        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.Browse));

        // Browse members.
        var children = await _service.GetChildrenAsync(source, new AssetDetectionResult { Format = "unity-bundle", Engine = "unity" }, depth: 0);
        Assert.AreEqual(2, children.Count);

        // Members are regular sources: a PNG member should detect and preview as PNG.
        var pngMember = children.First(c => c.Name == "CAB-abc123");
        var pngDescriptor = await _service.InspectAsync(pngMember);
        Assert.AreEqual("png", pngDescriptor!.Format);

        var pngPreview = await _service.TryPreviewAsync(pngMember, new AssetDetectionResult { Format = "png" });
        Assert.IsNotNull(pngPreview);
        Assert.IsNotNull(pngPreview!.Texture);
    }

    // ── Compressed bundle → structured unsupported at member open ─────────
    [TestMethod]
    public async Task UnityBundle_Compressed_MemberOpenUnsupported()
    {
        byte[] bundle = BuildUnityFsCompressedFlag();
        var source = Bytes(bundle, "compressed.bundle");

        var descriptor = await _service.InspectAsync(source);
        Assert.AreEqual("unity-bundle", descriptor!.Format);
        Assert.IsTrue(descriptor!.Metadata.ContainsKey("Compression"));

        // Member enumeration must raise the structured unsupported error, not crash.
        await Assert.ThrowsAsync<UnsupportedAssetException>(
            () => _service.GetChildrenAsync(source, new AssetDetectionResult { Format = "unity-bundle" }, depth: 0));
    }

    // ── Bad magic → structured error ──────────────────────────────────────
    [TestMethod]
    public async Task UnityBundle_BadMagic_StructuredError()
    {
        // Valid UnityFS magic but a truncated body (version only) → structured error.
        var source = Bytes(new byte[] { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00, 0x07, 0x00, 0x00, 0x00 }, "truncated.bundle");
        var ex = await Assert.ThrowsAsync<CorruptAssetException>(
            () => _service.InspectAsync(source));
        Assert.IsInstanceOfType<AssetException>(ex);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static byte[] BuildUnityFs(Dictionary<string, byte[]> files)
    {
        var ms = new MemoryStream();

        // Payload: concatenate member blobs.
        var payload = new MemoryStream();
        long offset = 0;
        var entries = new List<(string name, long off, long size)>();
        foreach (var kv in files)
        {
            entries.Add((kv.Key, offset, kv.Value.Length));
            payload.Write(kv.Value);
            offset += kv.Value.Length;
        }
        byte[] payloadBytes = payload.ToArray();

        // Blocks info (v7, flags=0: no compression, blocks at start).
        var info = new MemoryStream();
        WriteU32(info, 0);                                    // dataOffset - patched after infoBytes is built
        WriteU32(info, 1);                                    // blockCount
        WriteU32(info, (uint)payloadBytes.Length);            // block uncompressed size
        WriteU32(info, (uint)payloadBytes.Length);            // block compressed size (== uncompressed)
        WriteU16(info, 0);                                    // block flags (no compression)
        WriteU32(info, (uint)entries.Count);
        foreach (var (name, off, size) in entries)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            WriteU32(info, (uint)nameBytes.Length);
            info.Write(nameBytes);
            WriteU32(info, (uint)off);
            WriteU32(info, (uint)size);
            WriteU32(info, 0);                                // file flags (v7)
        }
        byte[] infoBytes = info.ToArray();

        // Header (v7).
        ms.Write(System.Text.Encoding.ASCII.GetBytes("UnityFS\0"));
        WriteU32(ms, 7);                                      // version
        WriteU32(ms, (uint)System.Text.Encoding.UTF8.GetBytes("2017.3.1f1").Length);
        ms.Write(System.Text.Encoding.UTF8.GetBytes("2017.3.1f1"));
        WriteU32(ms, 0);                                      // header size (unused by parse)
        WriteU64(ms, 0);                                      // file size (unused by parse)
        WriteU32(ms, 0);                                      // compressed block size (unused)
        WriteU32(ms, (uint)payloadBytes.Length);
        WriteU32(ms, 0);                                      // flags
        // dataOffset = actual header length + blocks info length
        BitConverter.GetBytes((uint)ms.Length + (uint)infoBytes.Length).CopyTo(infoBytes, 0);

        ms.Write(infoBytes);
        ms.Write(payloadBytes);
        return ms.ToArray();
    }

    private static byte[] BuildUnityFsCompressedFlag()
    {
        // Same layout as BuildUnityFs but block flags bit0 = 1 (LZMA-ish) so
        // AnyCompressedBlocks is true.
        var files = new Dictionary<string, byte[]> { ["CAB-x"] = new byte[] { 1, 2, 3 } };
        var bundle = BuildUnityFs(files);
        // Patch the single block's flags: blocks info starts after the v7 header.
        // Header = magic(8) + version(4) + nameLen(4) + name(10) + v7 fields(24) = 50.
        int headerLen = 8 + 4 + 4 + System.Text.Encoding.UTF8.GetBytes("2017.3.1f1").Length + 24;
        // blocks info: dataOffset(4) + blockCount(4) + block[uncomp(4)+comp(4)+flags(2)]
        int blockFlagsOffset = headerLen + 8 + 8;
        bundle[blockFlagsOffset] = 1; // compression bit
        return bundle;
    }

    private static void WriteU32(Stream s, uint v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU64(Stream s, long v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU16(Stream s, ushort v) => s.Write(BitConverter.GetBytes(v));
}
