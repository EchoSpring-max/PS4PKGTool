using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Handlers;
using PS4PKGTool.Assets.IO;
using PS4PKGTool.Assets.Models;
using PS4PKGTool.Assets.Unity;

namespace PS4PKGTool.Assets.Tests;

/// <summary>
/// Phase 3c: the Unity serialized-file handler behind the IUnityAssetBackend
/// seam. Synthetic fixtures prove detection/inspect/browse/preview with a
/// self-contained inline texture; the real Overcooked 2 sample validates the
/// whole pipeline; a fake backend proves the handler never depends on the
/// concrete parser.
/// </summary>
[TestClass]
public class Phase3UnitySerializedFileTests
{
    private const string RealSamplePath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_oc2\sharedassets0.assets";

    private readonly AssetInspectionService _service = GenericAssetRegistryBuilder.Build();

    // ── synthetic fixtures: one object, LE, v17 ──

    /// <summary>8x8 DXT1 payload (4 blocks).</summary>
    internal static byte[] Dxt1Payload8x8()
    {
        var payload = new byte[32];
        var block = new byte[] { 0xFF, 0x7F, 0x00, 0x00, 0xE4, 0xFF, 0xFF, 0xFF };
        for (int i = 0; i < 4; i++) block.CopyTo(payload, i * 8);
        return payload;
    }

    /// <summary>Texture2D object with INLINE image data.</summary>
    internal static byte[] BuildInlineTextureObject()
    {
        var obj = new MemoryStream();
        WriteAlignedString(obj, "Tex1");
        WriteI32(obj, 0);              // Texture.m_ForcedFallbackFormat
        obj.WriteByte(0);              // Texture.m_DownscaleFallback
        PadTo4(obj);
        WriteI32(obj, 8);              // m_Width
        WriteI32(obj, 8);              // m_Height
        WriteU32(obj, 32);             // m_CompleteImageSize
        WriteI32(obj, 10);             // m_TextureFormat = DXT1
        WriteI32(obj, 1);              // m_MipCount
        obj.WriteByte(1);              // m_IsReadable
        PadTo4(obj);
        WriteI32(obj, 1);              // m_ImageCount
        WriteI32(obj, 2);              // m_TextureDimension
        for (int i = 0; i < 6; i++) WriteI32(obj, 0); // GLTextureSettings
        WriteI32(obj, 0);              // m_LightmapFormat
        WriteI32(obj, 0);              // m_ColorSpace
        WriteI32(obj, 32);             // image data size (inline)
        obj.Write(Dxt1Payload8x8());
        return obj.ToArray();
    }

    /// <summary>Texture2D object with STREAMED data (StreamingInfo -> .resS companion).</summary>
    internal static byte[] BuildStreamedTextureObject(string streamPath = "synthetic.assets.resS")
    {
        var obj = new MemoryStream();
        WriteAlignedString(obj, "Tex1");
        WriteI32(obj, 0);              // Texture.m_ForcedFallbackFormat
        obj.WriteByte(0);              // Texture.m_DownscaleFallback
        PadTo4(obj);
        WriteI32(obj, 8);              // m_Width
        WriteI32(obj, 8);              // m_Height
        WriteU32(obj, 32);             // m_CompleteImageSize
        WriteI32(obj, 10);             // m_TextureFormat = DXT1
        WriteI32(obj, 1);              // m_MipCount
        obj.WriteByte(1);              // m_IsReadable
        PadTo4(obj);
        WriteI32(obj, 1);              // m_ImageCount
        WriteI32(obj, 2);              // m_TextureDimension
        for (int i = 0; i < 6; i++) WriteI32(obj, 0); // GLTextureSettings
        WriteI32(obj, 0);              // m_LightmapFormat
        WriteI32(obj, 0);              // m_ColorSpace
        WriteI32(obj, 0);              // image data size = 0 -> StreamingInfo follows
        WriteU32(obj, 16);             // stream offset (payload sits at +16 in the companion)
        WriteU32(obj, 32);             // stream size
        WriteAlignedString(obj, streamPath);
        return obj.ToArray();
    }

    /// <summary>TextAsset object (m_Name only - no texture, exercises the listing fallback).</summary>
    internal static byte[] BuildTextAssetObject()
    {
        var obj = new MemoryStream();
        WriteAlignedString(obj, "Text1");
        return obj.ToArray();
    }

    internal static byte[] BuildSyntheticSerializedFile(int classId, byte[] objectData, int version = 17)
    {

        // Metadata (little-endian): unity version, platform, stripped types, types, objects.
        var meta = new MemoryStream();
        WriteNullString(meta, version < 16 ? "5.3.8f1" : "2017.3.1p2");
        WriteU32(meta, 31);            // target platform = PS4
        meta.WriteByte(0);             // enableTypeTree = false
        WriteI32(meta, 1);             // type count
        WriteI32(meta, classId);
        if (version >= 16) meta.WriteByte(0); // isStrippedType
        if (version >= 17) WriteI16(meta, 0); // scriptTypeIndex
        meta.Write(new byte[16]);      // oldTypeHash
        WriteI32(meta, 1);             // object count
        PadTo4(meta);                  // v14+: pathId is 4-aligned
        WriteI64(meta, 1);             // pathId
        WriteU32(meta, 0);             // byteStart (relative to dataOffset)
        WriteU32(meta, (uint)objectData.Length);
        WriteI32(meta, version < 16 ? classId : 0); // v16+: typeId -> classId
        if (version < 16)
        {
            WriteI16(meta, (short)classId); // direct classId
            WriteI16(meta, -1);             // scriptTypeIndex
            if (version == 15) meta.WriteByte(0); // stripped
        }
        byte[] metadata = meta.ToArray();

        // Header: the first four fields are ALWAYS big-endian.
        uint dataOffset = 20u + (uint)metadata.Length;
        var file = new MemoryStream();
        WriteU32Be(file, (uint)metadata.Length);
        WriteU32Be(file, dataOffset + (uint)objectData.Length);
        WriteU32Be(file, (uint)version);
        WriteU32Be(file, dataOffset);
        file.WriteByte(0);             // endianness: 0 = little-endian
        file.Write(new byte[3]);       // reserved
        file.Write(metadata);
        file.Write(objectData);
        return file.ToArray();
    }

    // ── handler tests against the synthetic file ──

    [TestMethod]
    public async Task Detect_Inspect_And_Browse_SyntheticFile()
    {
        var source = new MemoryAssetSource(BuildSyntheticSerializedFile(28, BuildInlineTextureObject()), "synthetic.assets");

        var detection = _service.Detect(source);
        Assert.IsNotNull(detection);
        Assert.AreEqual(UnitySerializedFileHandler.FormatId, detection!.Format);

        var descriptor = await _service.InspectAsync(source, detection);
        Assert.AreEqual("1", descriptor.Metadata["Objects"]);
        Assert.AreEqual("1", descriptor.Metadata["Texture2D"]);
        Assert.AreEqual("unity", descriptor.Engine);
        Assert.IsTrue(descriptor.Capabilities.HasFlag(AssetCapabilities.Browse));

        var children = await _service.GetChildrenAsync(source, detection, 0);
        Assert.AreEqual(1, children.Count);
        Assert.IsInstanceOfType<UnityObjectAssetSource>(children[0]);
        Assert.AreEqual(28, ((UnityObjectAssetSource)children[0]).Object.ClassId);
    }

    [TestMethod]
    public async Task Browse_V15File_UsesDirectObjectClassId()
    {
        var source = new MemoryAssetSource(BuildSyntheticSerializedFile(28, BuildTextAssetObject(), version: 15), "v15.assets");
        var detection = _service.Detect(source)!;

        var children = await _service.GetChildrenAsync(source, detection, 0);
        Assert.AreEqual(1, children.Count);
        Assert.AreEqual(28, ((UnityObjectAssetSource)children[0]).Object.ClassId);
    }

    [TestMethod]
    public async Task PreviewFile_WithInlineTexture_ReturnsContactSheet()
    {
        var source = new MemoryAssetSource(BuildSyntheticSerializedFile(28, BuildInlineTextureObject()), "synthetic.assets");
        var detection = _service.Detect(source)!;

        var preview = await _service.TryPreviewAsync(source, detection);
        Assert.IsNotNull(preview);
        Assert.IsNotNull(preview!.Texture, "Decodeable textures must produce a contact sheet, not a listing");
        Assert.IsTrue(preview.Texture!.Width >= 240 && preview.Texture.Height >= 240, "Contact sheet is a scaled grid");
        StringAssert.Contains(preview.Info ?? "", "1 texture");
    }

    [TestMethod]
    public async Task PreviewFile_WithoutDecodeableTextures_FallsBackToListing()
    {
        var source = new MemoryAssetSource(BuildSyntheticSerializedFile(49, BuildTextAssetObject()), "synthetic.assets");
        var detection = _service.Detect(source)!;

        var preview = await _service.TryPreviewAsync(source, detection);
        Assert.IsNotNull(preview);
        Assert.IsNotNull(preview!.Text);
        StringAssert.Contains(preview.Text!, "1 objects");
        StringAssert.Contains(preview.Text!, "TextAsset x1");
    }

    [TestMethod]
    public async Task PreviewStreamedTexture_ResolvesCompanion()
    {
        // Companion file: 16 garbage bytes prefix, then the DXT1 payload at offset 16.
        string tempDir = Path.Combine(Path.GetTempPath(), "p4t_3d_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tempDir);
        try
        {
            var companion = new byte[16 + 32];
            Dxt1Payload8x8().CopyTo(companion, 16);
            File.WriteAllBytes(Path.Combine(tempDir, "synthetic.assets.resS"), companion);
            File.WriteAllBytes(Path.Combine(tempDir, "synthetic.assets"),
                BuildSyntheticSerializedFile(28, BuildStreamedTextureObject()));

            var source = new FileAssetSource(Path.Combine(tempDir, "synthetic.assets"), "PKG entry",
                rel => File.Exists(Path.Combine(tempDir, rel))
                    ? new FileAssetSource(Path.Combine(tempDir, rel), "Unity .resS stream")
                    : null);
            var detection = _service.Detect(source)!;
            Assert.AreEqual(UnitySerializedFileHandler.FormatId, detection.Format);

            // Whole-file preview resolves the companion and decodes the streamed texture.
            var preview = await _service.TryPreviewAsync(source, detection);
            Assert.IsNotNull(preview?.Texture, "Streamed texture should decode via the companion resolver");
            StringAssert.Contains(preview!.Info ?? "", "1 texture");

            // Child preview decodes the exact texture dimensions.
            var child = (await _service.GetChildrenAsync(source, detection, 0))[0];
            var childPreview = await _service.TryPreviewAsync(child, detection);
            Assert.IsNotNull(childPreview?.Texture);
            Assert.AreEqual(8, childPreview!.Texture!.Width);
            Assert.AreEqual(8, childPreview.Texture.Height);
            Assert.AreEqual(8 * 8 * 4, childPreview.Texture.Rgba8.Length);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [TestMethod]
    public async Task PreviewStreamedTexture_WithoutResolver_ThrowsHint()
    {
        var source = new MemoryAssetSource(BuildSyntheticSerializedFile(28, BuildStreamedTextureObject()), "synthetic.assets");
        var detection = _service.Detect(source)!;
        var child = (await _service.GetChildrenAsync(source, detection, 0))[0];

        var ex = await Assert.ThrowsAsync<UnsupportedAssetException>(
            () => _service.TryPreviewAsync(child, detection));
        StringAssert.Contains(ex.Message, "resS");
    }

    [TestMethod]
    public async Task PreviewInlineTextureChild_Decodes()
    {
        var source = new MemoryAssetSource(BuildSyntheticSerializedFile(28, BuildInlineTextureObject()), "synthetic.assets");
        var detection = _service.Detect(source)!;
        var child = (await _service.GetChildrenAsync(source, detection, 0))[0];

        var preview = await _service.TryPreviewAsync(child, detection);
        Assert.IsNotNull(preview);
        Assert.IsNotNull(preview!.Texture);
        Assert.AreEqual(8, preview.Texture!.Width);
        Assert.AreEqual(8, preview.Texture.Height);
        Assert.AreEqual(8 * 8 * 4, preview.Texture.Rgba8.Length);
        StringAssert.Contains(preview.Info ?? "", "DXT1");
    }

    [TestMethod]
    public async Task PreviewStreamedTexture_EmptyPath_UsesAssetsResSCompanion()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "p4t_3d_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tempDir);
        try
        {
            var companion = new byte[16 + 32];
            Dxt1Payload8x8().CopyTo(companion, 16);
            File.WriteAllBytes(Path.Combine(tempDir, "synthetic.assets.resS"), companion);
            File.WriteAllBytes(Path.Combine(tempDir, "synthetic.assets"),
                BuildSyntheticSerializedFile(28, BuildStreamedTextureObject(string.Empty)));

            var source = new FileAssetSource(Path.Combine(tempDir, "synthetic.assets"), "PKG entry",
                rel => File.Exists(Path.Combine(tempDir, rel))
                    ? new FileAssetSource(Path.Combine(tempDir, rel), "Unity .resS stream")
                    : null);
            var detection = _service.Detect(source)!;
            var child = (await _service.GetChildrenAsync(source, detection, 0))[0];

            var preview = await _service.TryPreviewAsync(child, detection);
            Assert.IsNotNull(preview?.Texture, "An empty stream path should resolve to synthetic.assets.resS.");
            Assert.AreEqual(8, preview!.Texture!.Width);
            Assert.AreEqual(8, preview.Texture.Height);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    // ── real Overcooked 2 sample ──

    [TestMethod]
    public async Task Detect_And_Inspect_RealSample()
    {
        if (!File.Exists(RealSamplePath)) { Assert.Inconclusive("Real sample not present."); return; }
        var source = new FileAssetSource(RealSamplePath, "PKG entry");

        var detection = _service.Detect(source);
        Assert.IsNotNull(detection);
        Assert.AreEqual(UnitySerializedFileHandler.FormatId, detection!.Format);

        var descriptor = await _service.InspectAsync(source, detection);
        Assert.AreEqual("58", descriptor.Metadata["Objects"]);
        Assert.AreEqual("2", descriptor.Metadata["Texture2D"]);
    }

    [TestMethod]
    public async Task Browse_RealSample_ListsContentObjects()
    {
        if (!File.Exists(RealSamplePath)) { Assert.Inconclusive("Real sample not present."); return; }
        var source = new FileAssetSource(RealSamplePath, "PKG entry");
        var detection = _service.Detect(source)!;

        var children = await _service.GetChildrenAsync(source, detection, 0);
        Assert.IsTrue(children.Count >= 2);
        Assert.IsTrue(children.All(c => c is UnityObjectAssetSource));
        Assert.AreEqual(2, children.Count(c => ((UnityObjectAssetSource)c).Object.ClassId == 28));
    }

    [TestMethod]
    public void PreviewStreamedTextureChild_ReportsResS_NotCrash()
    {
        if (!File.Exists(RealSamplePath)) { Assert.Inconclusive("Real sample not present."); return; }
        var source = new FileAssetSource(RealSamplePath, "PKG entry");
        var detection = _service.Detect(source)!;
        // First Texture2D child is streamed to .resS in this build - preview
        // must surface UnsupportedAssetException (graceful), never a crash.
        var children = _service.GetChildrenAsync(source, detection, 0).GetAwaiter().GetResult();
        var texChild = children.First(c => ((UnityObjectAssetSource)c).Object.ClassId == 28);

        var handler = new UnitySerializedFileHandler();
        var ex = Assert.ThrowsExactly<UnsupportedAssetException>(() =>
            handler.PreviewAsync(texChild, detection).GetAwaiter().GetResult());
        StringAssert.Contains(ex.Message, "resS");
    }

    // ── the seam: handler works with ANY backend ──

    [TestMethod]
    public async Task Handler_WorksWithFakeBackend()
    {
        var handler = new UnitySerializedFileHandler(new FakeBackend());
        var source = new MemoryAssetSource(new byte[] { 1, 2, 3, 4 }, "fake.assets");
        var detection = new AssetDetectionResult { Format = UnitySerializedFileHandler.FormatId, Engine = "unity", Confidence = 1.0 };

        var descriptor = await handler.InspectAsync(source, detection);
        Assert.AreEqual("1", descriptor.Metadata["Objects"]);
        Assert.AreEqual("fake", descriptor.Metadata["Backend"]);

        var children = await handler.GetChildrenAsync(source, detection, 0);
        Assert.AreEqual(1, children.Count);

        var preview = await handler.PreviewAsync(children[0], detection);
        Assert.IsNotNull(preview!.Texture);
        Assert.AreEqual(2, preview.Texture!.Width);
        Assert.AreEqual(2, preview.Texture.Height);
    }

    private sealed class FakeBackend : IUnityAssetBackend
    {
        public string Name => "fake";
        public IReadOnlyList<UnityObjectRef> ListObjects(IAssetSource source)
            => new[] { new UnityObjectRef("Texture2D", 28, 1, 0, 16) };

        public UnityTextureInfo? ReadTexture2D(IAssetSource source, UnityObjectRef obj)
            => new UnityTextureInfo
            {
                Name = "FakeTex", Width = 2, Height = 2, Format = 4, MipCount = 1,
                InlineBytes = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 },
            };

        public TextureData? DecodeTexture(UnityTextureInfo info)
            => new TextureData { Width = info.Width, Height = info.Height, Rgba8 = info.InlineBytes! };
    }

    // ── fixture writers (all little-endian unless noted) ──

    private static void WriteAlignedString(Stream s, string str)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(str);
        WriteI32(s, bytes.Length);
        s.Write(bytes);
        while ((s.Position % 4) != 0) s.WriteByte(0);
    }

    private static void WriteNullString(Stream s, string str)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(str);
        s.Write(bytes);
        s.WriteByte(0);
    }

    private static void PadTo4(Stream s)
    {
        while ((s.Position % 4) != 0) s.WriteByte(0);
    }

    private static void WriteI32(Stream s, int v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU32(Stream s, uint v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteI16(Stream s, short v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteI64(Stream s, long v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU32Be(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }
}
