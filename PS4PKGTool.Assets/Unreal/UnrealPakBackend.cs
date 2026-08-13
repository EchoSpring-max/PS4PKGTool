using System.Runtime.CompilerServices;
using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Codecs;
using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.Errors;

namespace PS4PKGTool.Assets.Unreal;

/// <summary>
/// IUnrealAssetBackend implemented with the self-written PAK parser (verified
/// against a real UE4 patch pak). A future CUE4Parse adapter would implement
/// the same contract.
/// </summary>
public sealed class UnrealPakBackend : IUnrealAssetBackend
{
    public string Name => "self-written PAK parser (verified UE4)";

    private sealed class PakData
    {
        public required UnrealPak.PakHeader Header { get; init; }
        public required IReadOnlyList<PakEntryRef> Entries { get; init; }
    }

    private static readonly ConditionalWeakTable<IAssetSource, PakData> Cache = new();

    private static PakData Get(IAssetSource source)
        => Cache.GetValue(source, s =>
        {
            var (header, entries) = UnrealPak.Parse(s);
            return new PakData { Header = header, Entries = entries };
        });

    public IReadOnlyList<PakEntryRef> ListEntries(IAssetSource source) => Get(source).Entries;

    public Stream OpenEntryStream(IAssetSource source, PakEntryRef entry)
    {
        if (entry.Encrypted)
            throw new UnsupportedAssetException($"Entry '{entry.Path}' is AES-encrypted - decryption is not supported yet.");

        var pak = Get(source);
        long offset = ResolveOffset(pak, entry);

        byte[] data;
        using (var s = source.OpenRead(offset, entry.Size))
        {
            data = new byte[s.Length];
            s.ReadExactly(data);
        }
        byte[] decompressed = PakCompression.Decompress(entry.Compression, data, entry.UncompressedSize);
        return new MemoryStream(decompressed, writable: false);
    }

    /// <summary>
    /// v4: entry offsets are absolute. v5+ (RelativeChunkOffsets): stored offsets
    /// are relative to the first entry's stored offset.
    /// </summary>
    private static long ResolveOffset(PakData pak, PakEntryRef entry)
    {
        if (pak.Header.Version < 5 || pak.Entries.Count == 0) return entry.Offset;
        return pak.Entries[0].Offset + entry.Offset;
    }
}
