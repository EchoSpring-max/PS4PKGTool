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

    [TestMethod]
    public void ProbeBasePakIndex()
    {
        const string basePak = @"C:\Users\User\AppData\Local\Temp\p4t_spike_ue\base\pakchunk0-ps4.pak";
        if (!File.Exists(basePak)) { Assert.Inconclusive("Base pak not present."); return; }
        long indexOffset = 14444780512;
        long indexSize = 16337131;
        var source = new FileAssetSource(basePak, "PKG entry");

        byte[] idx;
        using (var s = source.OpenRead(indexOffset, indexSize))
        {
            idx = new byte[s.Length];
            s.ReadExactly(idx);
        }
        Console.WriteLine($"preamble: {BitConverter.ToString(idx, 0, 64)}");

        // Mount point: {int32 byteLen, bytes+null}, then count, then entries.
        int pos = 0;
        int mountLen = BitConverter.ToInt32(idx, pos); pos += 4;
        string mount = System.Text.Encoding.UTF8.GetString(idx, pos, mountLen - 1); pos += mountLen;
        int total = BitConverter.ToInt32(idx, pos); pos += 4;
        Console.WriteLine($"mount='{mount}' count={total}");

        int parsed = 0;
        var methods = new Dictionary<uint, int>();
        while (parsed < total && pos + 8 < idx.Length)
        {
            int len = BitConverter.ToInt32(idx, pos);
            if (len <= 0 || len > 1 << 20 || pos + 4 + len + 53 > idx.Length)
            {
                Console.WriteLine($"  BREAK at #{parsed}: bad pathLen {len} at pos {pos}");
                break;
            }
            string path = System.Text.Encoding.UTF8.GetString(idx, pos + 4, len - 1);
            int rec = pos + 4 + len;
            long offset = BitConverter.ToInt64(idx, rec);
            long size = BitConverter.ToInt64(idx, rec + 8);
            long uncompressed = BitConverter.ToInt64(idx, rec + 16);
            uint method = BitConverter.ToUInt32(idx, rec + 24);
            int recEnd = rec + 53;
            if (method != 0)
            {
                int blocks = BitConverter.ToInt32(idx, rec + 49);
                if (blocks < 0 || blocks > 1 << 20)
                {
                    Console.WriteLine($"  BREAK at #{parsed}: bad block count {blocks} method={method} path={path}");
                    break;
                }
                recEnd = rec + 49 + 4 + blocks * 16;
            }
            methods.TryGetValue(method, out int c);
            methods[method] = c + 1;
            if (parsed < 5) Console.WriteLine($"  #{parsed}: {path} off={offset} size={size} raw={uncompressed} method={method}");
            parsed++;
            pos = recEnd;
        }
        Console.WriteLine($"parsed {parsed} entries, next pos {pos} of {idx.Length} ({(double)pos / idx.Length:P1})");
        Console.WriteLine("methods: " + string.Join(", ", methods.Select(m => $"{m.Key}={m.Value}")));

        // Dump the transition region (100 bytes around the break).
        int probe = Math.Max(0, pos - 40);
        Console.WriteLine($"transition@{probe}: {BitConverter.ToString(idx, probe, 100)}");
        var sb = new System.Text.StringBuilder();
        for (int i = probe; i < probe + 100; i++)
            sb.Append(idx[i] >= 32 && idx[i] < 127 ? (char)idx[i] : '.');
        Console.WriteLine($"ascii: {sb}");
    }

    [TestMethod]
    public void ExtractTextureFromBasePak()
    {
        const string basePak = @"C:\Users\User\AppData\Local\Temp\p4t_spike_ue\base\pakchunk0-ps4.pak";
        if (!File.Exists(basePak)) { Assert.Inconclusive("Base pak not present."); return; }
        var dir = @"C:\Users\User\AppData\Local\Temp\p4t_spike_ue\base";
        var source = new FileAssetSource(basePak, "PKG entry");
        var backend = new UnrealPakBackend();
        var entries = backend.ListEntries(source);
        Console.WriteLine($"base pak entries: {entries.Count}");
        // Prefer small textures: T_ prefixed .uasset with a matching .uexp.
        var textures = entries.Where(e => e.Path.Contains(".uasset") && e.UncompressedSize > 0 && e.UncompressedSize < 200_000
            && (Path.GetFileName(e.Path).StartsWith("T_") || e.Path.Contains("/Textures/"))).ToList();
        Console.WriteLine($"candidate textures: {textures.Count}");
        foreach (var e in textures.Take(5))
            Console.WriteLine($"  {e.Path} raw={e.UncompressedSize}");

        var pick = textures.FirstOrDefault();
        if (pick == null) { Assert.Inconclusive("No small texture found."); return; }
        var name = Path.GetFileName(pick.Path);
        using (var s = backend.OpenEntryStream(source, pick))
        {
            var buf = new byte[s.Length];
            s.ReadExactly(buf);
            File.WriteAllBytes(Path.Combine(dir, name), buf);
            Console.WriteLine($"extracted {name} ({buf.Length})");
        }
        var uexp = entries.FirstOrDefault(e => e.Path == pick.Path.Replace(".uasset", ".uexp"));
        if (uexp != null)
        {
            using var s = backend.OpenEntryStream(source, uexp);
            var buf = new byte[s.Length];
            s.ReadExactly(buf);
            File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(uexp.Path)), buf);
            Console.WriteLine($"extracted {Path.GetFileName(uexp.Path)} ({buf.Length})");
        }
    }

    [TestMethod]
    public void ExtractRealUassetPair_ForPackageParser()
    {
        if (!File.Exists(RealPakPath)) { Assert.Inconclusive("Real pak not present."); return; }
        var dir = Path.GetDirectoryName(RealPakPath)!;
        var source = new FileAssetSource(RealPakPath, "PKG entry");
        var backend = new UnrealPakBackend();
        var entries = backend.ListEntries(source);

        foreach (var name in new[] { "DA_DLCFlags.uasset", "DA_DLCFlags.uexp", "M_FX_BloodEffect_Hit_03.uasset", "M_FX_BloodEffect_Hit_03.uexp" })
        {
            var entry = entries.FirstOrDefault(e => e.Path.EndsWith(name));
            if (entry == null) continue;
            using var s = backend.OpenEntryStream(source, entry);
            var buf = new byte[s.Length];
            s.ReadExactly(buf);
            File.WriteAllBytes(Path.Combine(dir, name), buf);
            Console.WriteLine($"extracted {name} ({buf.Length} bytes)");
        }
    }

    [TestMethod]
    public void ListAssetEntries_RealPak()
    {
        if (!File.Exists(RealPakPath)) { Assert.Inconclusive("Real pak not present."); return; }
        var source = new FileAssetSource(RealPakPath, "PKG entry");
        var entries = new UnrealPakBackend().ListEntries(source);
        var assets = entries.Where(e => e.Path.Contains(".uasset") || e.Path.Contains(".uexp") || e.Path.Contains(".ubulk")).ToList();
        Console.WriteLine($"asset-ish entries: {assets.Count} / {entries.Count}");
        foreach (var e in assets.Take(60))
            Console.WriteLine($"  {e.Path} stored={e.Size} raw={e.UncompressedSize}");
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
