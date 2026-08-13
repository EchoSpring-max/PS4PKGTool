using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.Unreal;

namespace PS4PKGTool.Assets.Containers;

/// <summary>
/// Unreal Engine PAK container parser (read-only), written against real bytes
/// and verified with a UE4 patch pak from CODE VEIN (PS4, header-stripped,
/// version 4).
///
/// Layout (little-endian):
///   - Optional FPakInfo header at file start (stripped in shipping builds).
///   - Entry data (no alignment requirements).
///   - Index (TMap&lt;FString, FPakEntry&gt;) at IndexOffset.
///   - Trailer (last 44 bytes): Magic u32, Version i32, IndexOffset i64,
///     IndexSize i64, IndexHash 20B. UE5 (0x5A6F12E2) adds EncryptionKeyGuid
///     (60-byte trailer).
///
/// Supported: pak versions 3-7 (the UE4 range). Encrypted index surfaces as
/// "requires AES key"; Oodle entries surface as missing-dependency.
/// </summary>
public sealed class UnrealPak
{
    public const uint MagicUe4 = 0x5A6F12E1;
    public const uint MagicUe5 = 0x5A6F12E2;

    private const int TrailerSize = 44;              // Magic+Version+IndexOffset+IndexSize+IndexHash
    private const int TrailerSizeUe5 = 60;           // + EncryptionKeyGuid (16B)
    private const long MaxIndexSize = 256L * 1024 * 1024;
    private const int MaxEntries = 10_000_000;

    public sealed class PakHeader
    {
        public required uint Magic { get; set; }
        public required int Version { get; set; }
        public required long IndexOffset { get; set; }
        public required long IndexSize { get; set; }
        public bool HeaderStripped { get; set; }
        public string MountPoint { get; set; } = "";      // enriched by ReadIndex
        public int EntryCount { get; set; }               // declared in the index
        public int SkippedEntries { get; set; }           // deleted/duplicate records skipped
        public string Engine => Magic == MagicUe5 ? "UE5" : "UE4";
    }

    /// <summary>Header + index in one call (the header is enriched with index info).</summary>
    public static (PakHeader Header, IReadOnlyList<PakEntryRef> Entries) Parse(IAssetSource source)
    {
        var header = ParseHeader(source);
        var entries = ReadIndex(source, header);
        return (header, entries);
    }

    /// <summary>Probe for the trailer magic (the header is often stripped).</summary>
    public static bool IsLikelyPak(IAssetSource source)
    {
        try
        {
            long len = source.Length;
            if (len < TrailerSize) return false;
            int window = (int)Math.Min(TrailerSizeUe5, len);
            using var s = source.OpenRead(len - window, window);
            var tail = new byte[s.Length];
            s.ReadExactly(tail);
            uint m60 = BitConverter.ToUInt32(tail, 0);                              // 60-byte trailer (UE5)
            uint m44 = window >= TrailerSize ? BitConverter.ToUInt32(tail, window - TrailerSize) : 0;
            return m60 is MagicUe4 or MagicUe5 || m44 is MagicUe4 or MagicUe5;
        }
        catch { return false; }
    }

    public static PakHeader ParseHeader(IAssetSource source)
    {
        long len = source.Length;
        if (len < TrailerSize) throw new CorruptAssetException("PAK too small for a trailer.");

        // The trailer starts with the magic: 60 bytes (UE5, + EncryptionKeyGuid)
        // or 44 bytes (UE4). Read a window and locate the magic at its start.
        int window = (int)Math.Min(TrailerSizeUe5, len);
        byte[] tail;
        using (var s = source.OpenRead(len - window, window))
        {
            tail = new byte[s.Length];
            s.ReadExactly(tail);
        }

        uint magic;
        int trailerSize;
        uint m60 = BitConverter.ToUInt32(tail, 0);
        uint m44 = window >= TrailerSize ? BitConverter.ToUInt32(tail, window - TrailerSize) : 0;
        if (m60 is MagicUe4 or MagicUe5) { magic = m60; trailerSize = TrailerSizeUe5; }
        else if (m44 is MagicUe4 or MagicUe5) { magic = m44; trailerSize = TrailerSize; }
        else throw new CorruptAssetException("PAK trailer magic not found (encrypted index or unsupported layout).");

        byte[] t;
        using (var s = source.OpenRead(len - trailerSize, trailerSize))
        {
            t = new byte[s.Length];
            s.ReadExactly(t);
        }

        int version = BitConverter.ToInt32(t, 4);
        long indexOffset = BitConverter.ToInt64(t, 8);
        long indexSize = BitConverter.ToInt64(t, 16);
        if (version < 1 || version > 12)
            throw new CorruptAssetException($"Unsupported PAK version {version}.");
        if (indexOffset < 0 || indexOffset + indexSize > len)
            throw new CorruptAssetException("PAK index lies outside the file.");

        // Header-stripped detection: try the FPakInfo header at file start.
        bool stripped = true;
        try
        {
            using var hs = source.OpenRead(0, 8);
            var head = new byte[hs.Length];
            hs.ReadExactly(head);
            stripped = BitConverter.ToUInt32(head, 0) != magic;
        }
        catch { stripped = true; }

        return new PakHeader
        {
            Magic = magic,
            Version = version,
            IndexOffset = indexOffset,
            IndexSize = indexSize,
            HeaderStripped = stripped,
        };
    }

    public static IReadOnlyList<PakEntryRef> ReadIndex(IAssetSource source, PakHeader header)
    {
        if (header.IndexSize > MaxIndexSize)
            throw new UnsupportedAssetException($"PAK index is {header.IndexSize} bytes - over the {MaxIndexSize} safety cap.");
        if (header.Version is < 1 or > 12)
            throw new UnsupportedAssetException($"PAK version {header.Version} is not supported yet.");
        if (header.Version >= 8)
            throw new UnsupportedAssetException($"PAK version {header.Version} entry layout is not supported yet (versions 3-7 verified).");

        byte[] idx;
        using (var s = source.OpenRead(header.IndexOffset, header.IndexSize))
        {
            idx = new byte[s.Length];
            s.ReadExactly(idx);
        }

        try
        {
            int pos = 0;
            string mountPoint = ReadIndexString(idx, ref pos);
            int count = ReadI32(idx, ref pos);
            if (count < 0 || count > MaxEntries) throw new CorruptAssetException($"Invalid PAK entry count {count}.");

            var entries = new List<PakEntryRef>(count);
            int skipped = 0;
            for (int i = 0; i < count; i++)
            {
                string path = mountPoint + ReadIndexString(idx, ref pos);
                long offset = ReadI64(idx, ref pos);
                long compressed = ReadI64(idx, ref pos);
                long uncompressed = ReadI64(idx, ref pos);
                uint method = header.Version >= 3 ? ReadU32(idx, ref pos) : 0;
                pos += 20; // SHA1 hash

                bool encrypted = false;
                if (header.Version >= 3)
                {
                    if (method != 0)
                    {
                        int blocks = ReadI32(idx, ref pos);
                        if (blocks < 0 || blocks > 1 << 20) throw new CorruptAssetException("PAK compression block count out of range.");
                        pos += blocks * 16;                       // {start, end} per block
                    }
                    byte flags = idx[pos++];
                    _ = ReadU32(idx, ref pos);                    // compression block size
                    encrypted = (flags & 0x01) != 0;
                }

                // Deleted/duplicate records (patch paks) can carry out-of-file
                // offsets - skip them like CUE4Parse does, they have no data here.
                if (offset < 0 || offset >= source.Length)
                {
                    skipped++;
                    continue;
                }

                entries.Add(new PakEntryRef(
                    path,
                    offset,
                    compressed,
                    uncompressed,
                    CompressionName(method),
                    encrypted));
            }
            header.MountPoint = mountPoint;
            header.EntryCount = count;
            header.SkippedEntries = skipped;
            return entries;
        }
        catch (IndexOutOfRangeException)
        {
            // The index did not parse as plain data - most likely AES-encrypted.
            throw new UnsupportedAssetException(
                "PAK index is encrypted (AES-256-ECB) - an AES key is required to browse this pak.");
        }
    }

    /// <summary>
    /// Index strings (mount point + entry paths): int32 byte length INCLUDING the
    /// null terminator, then UTF-8 bytes + null. Verified against a real UE4 pak.
    /// </summary>
    private static string ReadIndexString(byte[] d, ref int p)
    {
        int len = ReadI32(d, ref p);
        if (len <= 0) return "";
        if (len > 1 << 20 || p + len > d.Length)
            throw new CorruptAssetException("PAK index string is out of range.");
        string s = System.Text.Encoding.UTF8.GetString(d, p, len - 1); // strip the null
        p += len;
        return s;
    }

    /// <summary>Maps the numeric compression method (pak versions 3-7).</summary>
    public static string CompressionName(uint method) => method switch
    {
        0 => "None",
        1 => "Zlib",
        2 => "Gzip",
        3 => "Oodle",
        4 => "LZ4",
        _ => $"Unknown({method})",
    };

    private static int ReadI32(byte[] d, ref int p)
    {
        int v = BitConverter.ToInt32(d, p); p += 4; return v;
    }

    private static uint ReadU32(byte[] d, ref int p)
    {
        uint v = BitConverter.ToUInt32(d, p); p += 4; return v;
    }

    private static long ReadI64(byte[] d, ref int p)
    {
        long v = BitConverter.ToInt64(d, p); p += 8; return v;
    }

    /// <summary>UE FString: int32 length in TCHARs (negative = empty), then UTF-16LE bytes.</summary>
    private static string ReadFString(byte[] d, ref int p)
    {
        int len = ReadI32(d, ref p);
        if (len <= 0) return "";
        if (len > 1 << 20 || p + len * 2 > d.Length)
            throw new CorruptAssetException("PAK entry path string is out of range.");
        string s = System.Text.Encoding.Unicode.GetString(d, p, len * 2);
        p += len * 2;
        return s;
    }
}
