using System.IO.Compression;
using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Codecs;
using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Handlers;
using PS4PKGTool.Assets.IO;
using PS4PKGTool.Assets.Unreal;

namespace PS4PKGTool.Assets.Tests;

/// <summary>
/// Phase 4a: Unreal PAK container. Synthetic v4 fixtures prove parse/browse/
/// decompress; the real CODE VEIN patch pak validates the layout.
/// </summary>
[TestClass]
public class Phase4UnrealPakTests
{
    private const string RealPakPath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_ue\pakchunk0-ps4_0_p.pak";

    private readonly AssetInspectionService _service = GenericAssetRegistryBuilder.Build();

    // ── fixtures ──

    /// <summary>RFC 1950 zlib: 0x78 0x9C header + raw deflate + adler32.</summary>
    private static byte[] Zlib(byte[] raw)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            ds.Write(raw);
        uint a = 1, b = 0;
        foreach (var x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
        uint adler = (b << 16) | a;
        ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a version-4 pak in the VERIFIED real layout: mount point string,
    /// entry data (absolute offsets), index with byte-length-prefixed paths,
    /// 53-byte FPakEntry records, 44-byte trailer.
    /// </summary>
    private static byte[] BuildSyntheticPak(params (string path, byte[] stored, long uncompressed, uint method)[] entries)
    {
        long offset = 0;
        var index = new MemoryStream();
        WriteIndexString(index, "test/");            // mount point
        WriteI32(index, entries.Length);
        foreach (var (path, stored, uncompressed, method) in entries)
        {
            WriteIndexString(index, path);
            WriteI64(index, offset);
            WriteI64(index, stored.Length);
            WriteI64(index, uncompressed);
            WriteU32(index, method);
            index.Write(new byte[20]);               // SHA1 hash (unchecked)
            if (method != 0)
            {
                WriteI32(index, 0);                  // compression blocks (none)
            }
            index.WriteByte(0);                      // flags
            WriteU32(index, 0);                      // compression block size
            offset += stored.Length;
        }
        byte[] indexBytes = index.ToArray();

        var file = new MemoryStream();
        foreach (var (_, stored, _, _) in entries) file.Write(stored);
        file.Write(indexBytes);
        WriteU32(file, UnrealPak.MagicUe4);
        WriteI32(file, 4);
        WriteI64(file, offset);
        WriteI64(file, indexBytes.Length);
        file.Write(new byte[20]);
        return file.ToArray();
    }

    /// <summary>Index string: int32 byte length incl null, UTF-8 bytes + null.</summary>
    private static void WriteIndexString(Stream s, string str)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(str);
        WriteI32(s, bytes.Length + 1);
        s.Write(bytes);
        s.WriteByte(0);
    }

    // ── compression ──

    [TestMethod]
    public void ZlibRoundTrip()
    {
        byte[] raw = System.Text.Encoding.UTF8.GetBytes("hello pak content hello pak content");
        byte[] packed = Zlib(raw);
        byte[] back = PakCompression.Decompress("Zlib", packed, raw.Length);
        CollectionAssert.AreEqual(raw, back);
    }

    [TestMethod]
    public void OodleEntry_ThrowsMissingDependency()
    {
        byte[] data = { 1, 2, 3, 4 };
        var pak = new MemoryAssetSource(BuildSyntheticPak(("Content/Data/x.bin", data, data.Length, 3u)), "x.pak");
        var backend = new UnrealPakBackend();
        var entry = backend.ListEntries(pak)[0];
        var ex = Assert.ThrowsExactly<MissingDependencyException>(() => backend.OpenEntryStream(pak, entry));
        StringAssert.Contains(ex.Message, "Oodle");
    }

    // ── synthetic v4 pak ──

    [TestMethod]
    public void ParseSyntheticPak()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        byte[] raw = System.Text.Encoding.UTF8.GetBytes("some text data that compresses well, " + new string('x', 200));
        byte[] packed = Zlib(raw);

        var pak = new MemoryAssetSource(
            BuildSyntheticPak(
                ("Content/Textures/a.png", png, png.Length, 0u),
                ("Content/Data/b.bin", packed, raw.Length, 1u)),
            "test.pak");

        var detection = _service.Detect(pak);
        Assert.IsNotNull(detection);
        Assert.AreEqual(UnrealPakHandler.FormatId, detection!.Format);

        var header = UnrealPak.ParseHeader(pak);
        Assert.AreEqual(UnrealPak.MagicUe4, header.Magic);
        Assert.AreEqual(4, header.Version);
        Assert.IsTrue(header.HeaderStripped, "Synthetic pak has no file-start header");

        var backend = new UnrealPakBackend();
        var entries = backend.ListEntries(pak);
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual("test/Content/Textures/a.png", entries[0].Path, "Paths are mount-point prefixed");
        Assert.AreEqual("None", entries[0].Compression);
        Assert.AreEqual("test/Content/Data/b.bin", entries[1].Path);
        Assert.AreEqual("Zlib", entries[1].Compression);
        Assert.AreEqual(raw.Length, entries[1].UncompressedSize);

        // Decompressed reads match the originals.
        using (var s = backend.OpenEntryStream(pak, entries[0]))
        {
            var buf = new byte[s.Length];
            s.ReadExactly(buf);
            CollectionAssert.AreEqual(png, buf);
        }
        using (var s = backend.OpenEntryStream(pak, entries[1]))
        {
            var buf = new byte[s.Length];
            s.ReadExactly(buf);
            CollectionAssert.AreEqual(raw, buf);
        }
    }

    [TestMethod]
    public async Task Handler_InspectsAndPreviewsSyntheticPak()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47 };
        var pak = new MemoryAssetSource(BuildSyntheticPak(("Content/a.png", png, png.Length, 0u)), "test.pak");
        var detection = _service.Detect(pak)!;

        var descriptor = await _service.InspectAsync(pak, detection);
        Assert.AreEqual("4", descriptor.Metadata["Version"]);
        Assert.AreEqual("unreal", descriptor.Engine);
        Assert.AreEqual("1", descriptor.Metadata["Entries"]);

        var preview = await _service.TryPreviewAsync(pak, detection);
        Assert.IsNotNull(preview?.Text);
        StringAssert.Contains(preview!.Text!, "Unreal PAK");
        StringAssert.Contains(preview.Text!, "test/Content/a.png");

        var children = await _service.GetChildrenAsync(pak, detection, 0);
        Assert.AreEqual(1, children.Count);
        Assert.IsInstanceOfType<PakEntrySource>(children[0]);
    }

    // ── real CODE VEIN patch pak ──

    [TestMethod]
    public void ParseRealCodeVeinPak()
    {
        if (!File.Exists(RealPakPath)) { Assert.Inconclusive("Real pak not present - extract CODE VEIN update pakchunk0-ps4_0_p.pak first."); return; }
        var source = new FileAssetSource(RealPakPath, "PKG entry");

        var detection = _service.Detect(source);
        Assert.IsNotNull(detection);
        Assert.AreEqual(UnrealPakHandler.FormatId, detection!.Format);

        var (header, _) = UnrealPak.Parse(source);
        Console.WriteLine($"magic=0x{header.Magic:X8} version={header.Version} engine={header.Engine} stripped={header.HeaderStripped} index@{header.IndexOffset}+{header.IndexSize} mount='{header.MountPoint}' declared={header.EntryCount} skipped={header.SkippedEntries}");
        Assert.AreEqual(UnrealPak.MagicUe4, header.Magic);
        Assert.AreEqual(4, header.Version);
        Assert.IsTrue(header.HeaderStripped);
        Assert.AreEqual("../../../", header.MountPoint, "Patch paks mount at the parent path");

        var backend = new UnrealPakBackend();
        var entries = backend.ListEntries(source);
        Console.WriteLine($"entries={entries.Count}");
        Assert.IsTrue(entries.Count > 100, $"Expected a patch pak with many entries, got {entries.Count}");
        Assert.IsTrue(entries.Count <= header.EntryCount, "Skipped records must not exceed the declared count");

        foreach (var e in entries.Take(8))
            Console.WriteLine($"  {e.Path} stored={e.Size} raw={e.UncompressedSize} {e.Compression}");
        Console.WriteLine("mix: " + string.Join(", ", entries.GroupBy(e => e.Compression).Select(g => $"{g.Key}={g.Count()}")));

        // Decompress the first uncompressed entry - the data must round-trip.
        var noneEntry = entries.First(e => e.Compression == "None");
        using (var s = backend.OpenEntryStream(source, noneEntry))
        {
            Assert.AreEqual(noneEntry.UncompressedSize, s.Length);
            byte[] buf = new byte[s.Length];
            s.ReadExactly(buf);
            Assert.IsTrue(buf.Length > 0);
            Console.WriteLine($"first None entry '{noneEntry.Path}' round-trips ({buf.Length} bytes)");
        }

        // Zlib entries (if any) must decompress to their declared raw size.
        var zlibEntry = entries.FirstOrDefault(e => e.Compression == "Zlib");
        if (zlibEntry != null)
        {
            using var s = backend.OpenEntryStream(source, zlibEntry);
            Assert.AreEqual(zlibEntry.UncompressedSize, s.Length);
            Console.WriteLine($"zlib entry '{zlibEntry.Path}' round-trips ({s.Length} bytes)");
        }
    }

    // ── writers (little-endian) ──

    private static void WriteI32(Stream s, int v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU32(Stream s, uint v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteI64(Stream s, long v) => s.Write(BitConverter.GetBytes(v));

    private static void WriteFString(Stream s, string str)
    {
        WriteI32(s, str.Length);
        s.Write(System.Text.Encoding.Unicode.GetBytes(str));
    }
}
