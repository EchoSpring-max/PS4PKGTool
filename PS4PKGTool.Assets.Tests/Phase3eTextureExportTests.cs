using PS4PKGTool.Assets.Abstractions;
using PS4PKGTool.Assets.Containers;
using PS4PKGTool.Assets.Errors;
using PS4PKGTool.Assets.IO;
using PS4PKGTool.Assets.Models;
using PS4PKGTool.Assets.Unity;
using StbImageSharp;

namespace PS4PKGTool.Assets.Tests;

/// <summary>
/// Phase 3e: PNG export of Unity textures. Round-trips through the real PNG
/// encoder/decoder and validates against the real Overcooked 2 sample.
/// </summary>
[TestClass]
public class Phase3eTextureExportTests
{
    private const string RealSamplePath = @"C:\Users\User\AppData\Local\Temp\p4t_spike_oc2\sharedassets0.assets";

    private readonly AssetInspectionService _service = GenericAssetRegistryBuilder.Build();

    private static string NewTempDir() => Path.Combine(Path.GetTempPath(), "p4t_3e_" + Guid.NewGuid().ToString("N").Substring(0, 6));

    private static ImageResult DecodePng(string path)
        => ImageResult.FromStream(File.OpenRead(path), ColorComponents.RedGreenBlueAlpha);

    [TestMethod]
    public void ExportWholeFile_InlineTexture_WritesPng()
    {
        string dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var source = new MemoryAssetSource(
                Phase3UnitySerializedFileTests.BuildSyntheticSerializedFile(28, Phase3UnitySerializedFileTests.BuildInlineTextureObject()),
                "synthetic.assets");
            var detection = _service.Detect(source)!;
            var descriptor = _service.InspectAsync(source, detection).GetAwaiter().GetResult();

            var exporter = new UnityTextureExporter();
            exporter.ExportConvertedAsync(source, descriptor, dir, CancellationToken.None).GetAwaiter().GetResult();

            var pngs = Directory.GetFiles(dir, "*.png");
            Assert.AreEqual(1, pngs.Length);
            var img = DecodePng(pngs[0]);
            Assert.AreEqual(8, img.Width);
            Assert.AreEqual(8, img.Height);
            Assert.AreEqual(8 * 8 * 4, img.Data.Length);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ExportWholeFile_StreamedTexture_ResolvesCompanion()
    {
        string dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var companion = new byte[16 + 32];
            Phase3UnitySerializedFileTests.Dxt1Payload8x8().CopyTo(companion, 16);
            File.WriteAllBytes(Path.Combine(dir, "synthetic.assets.resS"), companion);
            File.WriteAllBytes(Path.Combine(dir, "synthetic.assets"),
                Phase3UnitySerializedFileTests.BuildSyntheticSerializedFile(28, Phase3UnitySerializedFileTests.BuildStreamedTextureObject()));

            var source = new FileAssetSource(Path.Combine(dir, "synthetic.assets"), "PKG entry",
                rel => File.Exists(Path.Combine(dir, rel))
                    ? new FileAssetSource(Path.Combine(dir, rel), "Unity .resS stream")
                    : null);
            var detection = _service.Detect(source)!;
            var descriptor = _service.InspectAsync(source, detection).GetAwaiter().GetResult();

            string outDir = Path.Combine(dir, "out");
            new UnityTextureExporter().ExportConvertedAsync(source, descriptor, outDir, CancellationToken.None).GetAwaiter().GetResult();

            var pngs = Directory.GetFiles(outDir, "*.png");
            Assert.AreEqual(1, pngs.Length);
            var img = DecodePng(pngs[0]);
            Assert.AreEqual(8, img.Width);
            Assert.AreEqual(8, img.Height);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ExportTextureChild_WritesSinglePng()
    {
        string dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var source = new MemoryAssetSource(
                Phase3UnitySerializedFileTests.BuildSyntheticSerializedFile(28, Phase3UnitySerializedFileTests.BuildInlineTextureObject()),
                "synthetic.assets");
            var detection = _service.Detect(source)!;
            var child = _service.GetChildrenAsync(source, detection, 0).GetAwaiter().GetResult()[0];
            // The exporter ignores the descriptor; a minimal one suffices (inspecting
            // a child as a serialized file would be wrong - it is object bytes).
            var descriptor = new AssetDescriptor { Name = child.Name, Format = "unity-serialized-file", Size = child.Length };

            string outFile = Path.Combine(dir, "tex.png");
            new UnityTextureExporter().ExportConvertedAsync(child, descriptor, outFile, CancellationToken.None).GetAwaiter().GetResult();

            var img = DecodePng(outFile);
            Assert.AreEqual(8, img.Width);
            Assert.AreEqual(8, img.Height);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ExportRaw_CopiesSourceBytes()
    {
        string dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            var source = new MemoryAssetSource(bytes, "raw.bin");
            var descriptor = new AssetDescriptor { Name = "raw.bin", Format = "unknown", Size = bytes.Length };
            string outFile = Path.Combine(dir, "raw.bin");

            new UnityTextureExporter().ExportRawAsync(source, descriptor, outFile, CancellationToken.None).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(outFile));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ExportRealSample_WritesBothTextures()
    {
        string resS = RealSamplePath + ".resS";
        if (!File.Exists(RealSamplePath) || !File.Exists(resS)) { Assert.Inconclusive("Real sample + resS not present."); return; }

        string dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var dirOfAssets = Path.GetDirectoryName(RealSamplePath)!;
            var source = new FileAssetSource(RealSamplePath, "PKG entry",
                rel => File.Exists(Path.Combine(dirOfAssets, rel))
                    ? new FileAssetSource(Path.Combine(dirOfAssets, rel), "Unity .resS stream")
                    : null);
            var detection = _service.Detect(source)!;
            var descriptor = _service.InspectAsync(source, detection).GetAwaiter().GetResult();

            string outDir = Path.Combine(dir, "out");
            new UnityTextureExporter().ExportConvertedAsync(source, descriptor, outDir, CancellationToken.None).GetAwaiter().GetResult();

            var pngs = Directory.GetFiles(outDir, "*.png");
            Assert.AreEqual(2, pngs.Length, "Both DXT5 textures export");
            var dims = pngs.Select(p => (DecodePng(p).Width, DecodePng(p).Height)).OrderBy(d => d.Width).ToList();
            Assert.AreEqual((768, 1024), dims[0], "UI_LoadingPage");
            Assert.AreEqual((2048, 1024), dims[1], "SplashScreen");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void ExportNoDecodeableTextures_Throws()
    {
        string dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var source = new MemoryAssetSource(
                Phase3UnitySerializedFileTests.BuildSyntheticSerializedFile(49, Phase3UnitySerializedFileTests.BuildTextAssetObject()),
                "synthetic.assets");
            var detection = _service.Detect(source)!;
            var descriptor = _service.InspectAsync(source, detection).GetAwaiter().GetResult();

            var ex = Assert.ThrowsExactly<UnsupportedAssetException>(() =>
                new UnityTextureExporter().ExportConvertedAsync(source, descriptor, Path.Combine(dir, "out"), CancellationToken.None)
                    .GetAwaiter().GetResult());
            StringAssert.Contains(ex.Message, "No decodeable textures");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
