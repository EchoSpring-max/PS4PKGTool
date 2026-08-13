using System.IO.Compression;
using K4os.Compression.LZ4;
using PS4PKGTool.Assets.Errors;

namespace PS4PKGTool.Assets.Codecs;

/// <summary>
/// PAK entry decompression: None (raw), Zlib (RFC 1950 wrapper over raw
/// deflate), Gzip, LZ4 block (UE4's FLZ4). Oodle is intentionally absent -
/// it surfaces as a missing-dependency error (never auto-downloaded).
/// </summary>
public static class PakCompression
{
    public const long MaxDecompressed = 256L * 1024 * 1024; // guard against decompression bombs

    public static byte[] Decompress(string method, ReadOnlySpan<byte> data, long uncompressedSize)
    {
        if (uncompressedSize > MaxDecompressed)
            throw new UnsupportedAssetException(
                $"Entry decompresses to {uncompressedSize} bytes - over the {MaxDecompressed} safety cap.");

        switch (method)
        {
            case "None":
                return data.ToArray();
            case "Zlib":
                return DecompressZlib(data, uncompressedSize);
            case "Gzip":
                return DecompressGzip(data, uncompressedSize);
            case "LZ4":
                return DecompressLz4(data, uncompressedSize);
            case "Oodle":
                throw new MissingDependencyException(
                    "Entry uses Oodle compression - the Oodle DLL is not bundled (see ASSET_FRAMEWORK.md). Raw export of the compressed bytes remains available.");
            default:
                throw new UnsupportedAssetException($"Unknown compression method '{method}'.");
        }
    }

    /// <summary>RFC 1950 zlib: 2-byte header (CMF/FLG), raw deflate stream, optional adler32 tail.</summary>
    private static byte[] DecompressZlib(ReadOnlySpan<byte> data, long expected)
    {
        if (data.Length < 6 || (data[0] & 0x0F) != 8)
            throw new CorruptAssetException("Invalid zlib header.");
        using var input = new MemoryStream(data[2..].ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        return ReadAll(deflate, expected);
    }

    private static byte[] DecompressGzip(ReadOnlySpan<byte> data, long expected)
    {
        using var input = new MemoryStream(data.ToArray());
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        return ReadAll(gz, expected);
    }

    private static byte[] DecompressLz4(ReadOnlySpan<byte> data, long expected)
    {
        var output = new byte[expected];
        int written = LZ4Codec.Decode(data.ToArray(), 0, data.Length, output, 0, output.Length);
        if (written != expected)
            throw new CorruptAssetException($"LZ4 decode wrote {written} bytes, expected {expected}.");
        return output;
    }

    private static byte[] ReadAll(Stream s, long expected)
    {
        using var ms = new MemoryStream(expected > 0 && expected <= int.MaxValue ? (int)expected : 0);
        s.CopyTo(ms);
        if (ms.Length != expected)
            throw new CorruptAssetException($"Decompressed size mismatch: got {ms.Length}, expected {expected}.");
        return ms.ToArray();
    }
}
