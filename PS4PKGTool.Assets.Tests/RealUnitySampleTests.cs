using PS4PKGTool.Assets.Codecs;
using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.Handlers;
using PS4PKGTool.Assets.IO;

namespace PS4PKGTool.Assets.Tests;

/// <summary>
/// Validates the serialized-file parser + hardcoded Texture2D layout against a
/// REAL Overcooked 2 sharedassets0.assets (Unity 2017.3.1p2, PS4 build,
/// big-endian, stripped type trees). Extracted from the B: PKG library.
/// </summary>
[TestClass]
public class RealUnitySampleTests
{
    private const string AssetsPath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_oc2\sharedassets0.assets";
    private const string ResourcesPath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_oc2\resources.assets";

    private static bool SamplePresent => File.Exists(AssetsPath);

    [TestMethod]
    public void ParseRealSharedAssets0()
    {
        if (!SamplePresent) { Assert.Inconclusive("Real sample not present — extract Overcooked 2's sharedassets0.assets first."); return; }

        var source = new FileAssetSource(AssetsPath, "PKG entry");
        Assert.IsTrue(UnitySerializedFile.IsLikelySerializedFile(source), "Header version probe should accept the sample.");

        var sf = UnitySerializedFile.Parse(source);
        Console.WriteLine($"version={sf.Version} unity={sf.UnityVersion} endian={sf.BigEndian} typeTree={sf.EnableTypeTree} objects={sf.Objects.Count}");

        var textures = sf.Objects.Where(o => o.ClassId == 28).ToList();
        Console.WriteLine($"textures={textures.Count}");
        Console.WriteLine("classIds: " + string.Join(",", sf.Objects.GroupBy(o => o.ClassId).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}").Take(12)));
        Console.WriteLine($"offset range: {sf.Objects.Min(o => o.Offset)}..{sf.Objects.Max(o => o.Offset)}");

        Assert.AreEqual(17, sf.Version);
        Assert.IsTrue(sf.UnityVersion.StartsWith("2017.3"), $"Unity version should be 2017.x, got '{sf.UnityVersion}'");
        // Unity serialized data follows the BUILD MACHINE's endianness, not the target
        // platform — this PS4 package was built on x86, so the marker byte is 0 (LE).
        // AssetStudio: m_FileEndianess == 0 -> LittleEndian.
        Assert.IsFalse(sf.BigEndian, "This sample's marker byte is 0 -> little-endian body");
        Assert.IsFalse(sf.EnableTypeTree, "This build ships stripped type trees");
        // This file is a sprite-atlas (45 Sprites + 2 Texture2D + TextAssets); 58 objects total.
        Assert.IsTrue(sf.Objects.Count > 50);
        // This file holds the game's splash/loading art; the bulk of textures
        // live in resources.assets. Two is expected here.
        Assert.IsTrue(textures.Count > 0, $"sharedassets0 should hold Texture2D objects, got {textures.Count}");
    }

    [TestMethod]
    public void ReadFirstTexturesHardcodedLayout()
    {
        if (!SamplePresent) { Assert.Inconclusive("Real sample not present."); return; }

        var source = new FileAssetSource(AssetsPath, "PKG entry");
        var sf = UnitySerializedFile.Parse(source);
        var textures = sf.Objects.Where(o => o.ClassId == 28).ToList();
        Assert.IsTrue(textures.Count > 0, "No textures in sample.");

        int inline = 0, streamed = 0, validDims = 0;
        foreach (var obj in textures)
        {
            var info = UnitySerializedFile.ReadTexture2D(source, obj, sf.BigEndian);
            Assert.IsNotNull(info);
            Console.WriteLine($"  {info!.Name,-32} {info.Width}x{info.Height} fmt={info.Format} mips={info.MipCount} inline={info.ImageBytes?.Length ?? 0} stream='{info.StreamDataPath}'@{info.StreamOffset}+{info.StreamSize}");
            Assert.IsTrue(info.Width is > 0 and <= 16384 && info.Height is > 0 and <= 16384, "Texture2D dimensions out of range");
            Assert.IsTrue(info.Format >= 1, "Texture format should parse");
            validDims++;
            if (info.ImageBytes is { Length: > 0 }) inline++;
            if (!string.IsNullOrEmpty(info.StreamDataPath)) streamed++;
        }

        Assert.AreEqual(textures.Count, validDims, "Every Texture2D should have sane dimensions (hardcoded layout is correct)");
        Assert.IsTrue(inline + streamed > 0, "At least one texture should carry image data (inline or streamed)");
        Console.WriteLine($"summary: inline={inline} streamed={streamed}");
    }

    [TestMethod]
    public void ParseResourcesAssets_RichTextureSet()
    {
        if (!File.Exists(ResourcesPath)) { Assert.Inconclusive("resources.assets not present — extract it for full validation."); return; }

        var source = new FileAssetSource(ResourcesPath, "PKG entry");
        var sf = UnitySerializedFile.Parse(source);
        var textures = sf.Objects.Where(o => o.ClassId == 28).ToList();
        Console.WriteLine($"version={sf.Version} objects={sf.Objects.Count} textures={textures.Count}");

        Assert.AreEqual(17, sf.Version);
        Assert.IsFalse(sf.BigEndian, "Same build machine endianness as the rest of the package");
        Assert.IsTrue(textures.Count > 100, $"resources.assets should hold the bulk of the textures, got {textures.Count}");

        int inline = 0, streamed = 0;
        foreach (var obj in textures)
        {
            var info = UnitySerializedFile.ReadTexture2D(source, obj, sf.BigEndian);
            Assert.IsNotNull(info);
            Assert.IsTrue(info!.Width is > 0 and <= 16384 && info.Height is > 0 and <= 16384, "Texture2D dimensions out of range");
            Assert.IsTrue(info.Format >= 1, "Texture format should parse");
            if (info.ImageBytes is { Length: > 0 }) inline++;
            if (!string.IsNullOrEmpty(info.StreamDataPath)) streamed++;
        }

        // PS4 build pipeline streams texture data out to .resS - the whole set
        // is expected to be external here (small textures too).
        Assert.IsTrue(streamed > 0, "Expected streamed textures in resources.assets");
        Assert.AreEqual(textures.Count, inline + streamed, "Every texture must be inline or streamed, nothing else");
        Console.WriteLine($"summary(all {textures.Count}): inline={inline} streamed={streamed}");
    }

    [TestMethod]
    public void PreviewRealSample_BuildsContactSheet_WithResSCompanion()
    {
        string resS = AssetsPath + ".resS";
        if (!File.Exists(AssetsPath) || !File.Exists(resS)) { Assert.Inconclusive("Real sample + resS not present."); return; }

        var dir = Path.GetDirectoryName(AssetsPath)!;
        var source = new FileAssetSource(AssetsPath, "PKG entry",
            rel => File.Exists(Path.Combine(dir, rel))
                ? new FileAssetSource(Path.Combine(dir, rel), "Unity .resS stream")
                : null);
        var service = GenericAssetRegistryBuilder.Build();
        var detection = service.Detect(source)!;
        Assert.AreEqual(UnitySerializedFileHandler.FormatId, detection.Format);

        // The app flow: whole-file preview resolves the .resS and decodes both
        // streamed DXT5 textures into a contact sheet.
        var preview = service.TryPreviewAsync(source, detection).GetAwaiter().GetResult();
        Assert.IsNotNull(preview?.Texture, "Contact sheet expected with the companion available");
        Assert.IsTrue(preview!.Texture!.Width >= 240 && preview.Texture.Height >= 240);
        StringAssert.Contains(preview.Info ?? "", "2 textures");
        Console.WriteLine($"contact sheet: {preview.Info} ({preview.Texture.Width}x{preview.Texture.Height})");
    }

    [TestMethod]
    public void DecodeFirstDxtTextureEndToEnd()
    {
        if (!SamplePresent) { Assert.Inconclusive("Real sample not present."); return; }

        var source = new FileAssetSource(AssetsPath, "PKG entry");
        var sf = UnitySerializedFile.Parse(source);
        var textures = sf.Objects.Where(o => o.ClassId == 28).ToList();
        Assert.IsTrue(textures.Count > 0);

        // TextureFormat: DXT1=10, DXT3=11, DXT5=12 — decodeable by our BC decoder.
        foreach (var obj in textures)
        {
            var info = UnitySerializedFile.ReadTexture2D(source, obj, sf.BigEndian);
            if (info is null || info.Format is not (10 or 11 or 12)) continue;
            if (info.Width <= 0 || info.Height <= 0) continue;

            byte[]? payload = info.ImageBytes;
            if (payload is null && !string.IsNullOrEmpty(info.StreamDataPath))
            {
                // Resolve the external .resS reference (relative to the .assets file).
                string resPath = Path.Combine(Path.GetDirectoryName(AssetsPath)!, info.StreamDataPath);
                if (!File.Exists(resPath)) continue;
                using var fs = File.OpenRead(resPath);
                fs.Position = info.StreamOffset;
                payload = new byte[info.StreamSize];
                int got = fs.Read(payload, 0, payload.Length);
                if (got != payload.Length) continue;
            }
            if (payload is null || payload.Length < 8) continue;

            string fourCc = info.Format switch { 10 => "DXT1", 11 => "DXT3", _ => "DXT5" };
            byte[] rgba = DdsDecoder.DecodeBcPayload(info.Width, info.Height, fourCc, payload);

            Console.WriteLine($"decoded: {info.Name} {info.Width}x{info.Height} {fourCc} -> {rgba.Length} RGBA bytes (expected {info.Width * info.Height * 4})");
            Assert.AreEqual(info.Width * info.Height * 4, rgba.Length, "Decoded RGBA buffer must match dimensions");
            return; // one success proves the pipeline
        }

        Assert.Fail("No DXT1/3/5 texture with readable image data found in the sample.");
    }
}
