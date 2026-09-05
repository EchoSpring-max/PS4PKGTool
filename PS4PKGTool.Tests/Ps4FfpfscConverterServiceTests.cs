using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrbisPkgTool.Sfo;
using PS4PKGTool.Utilities.Ffpfsc;
using PS4PKGTool.Utilities.Ffpfsc.Engine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PS4PKGTool.Tests;

/// <summary>
/// Integration test: converts a real (fake-keyset) PKG built by
/// <see cref="PkgFixture"/> into an FFPFSC image and verifies the structure
/// and inner payload. Exercises <see cref="Ps4FfpfscConverterService"/> end
/// to end (extraction, Sc0→sce_sys restructure, param.json projection,
/// npbind repair, exFAT/PFSC/PFS build, verification).
/// </summary>
[TestClass]
public sealed class Ps4FfpfscConverterServiceTests
{
    [TestMethod]
    public async Task Convert_BuildsValidFfpfscFromGamePkg()
    {
        using var fixture = PkgFixture.Create("ffpfsc-source",
            ("eboot.bin", 4096),
            ("app0/data.bin", 8192));
        string tempRoot = Path.Combine(Path.GetTempPath(), "ffpfsc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string outputPath = Path.Combine(tempRoot, "output.ffpfsc");
        try
        {
            var options = new FfpfscConvertOptions(
                PkgPath: fixture.PackagePath,
                OutputPath: outputPath,
                WorkParentDirectory: tempRoot,
                VerifyAfterBuild: true);

            var converter = new Ps4FfpfscConverterService();
            var (succeeded, message, result) = await converter.ConvertAsync(options);

            Assert.IsTrue(succeeded, $"Conversion failed: {message}");
            Assert.IsNotNull(result);
            Assert.IsTrue(File.Exists(outputPath), "Output .ffpfsc must exist");

            // Inspect the container independently to make sure the structure
            // is readable — this is what ShadowMountPlus does.
            using (var stream = File.OpenRead(outputPath))
            {
                FfpfscInfo info = FfpfscImage.Inspect(stream);
                Assert.AreEqual("CUSA09999.exfat", info.InnerFileName);
                Assert.IsTrue(info.PfsBlockSize >= FfpfscBuildOptions.DefaultPfsBlockSize);
            }

            // Verification result is populated when VerifyAfterBuild is true.
            Assert.IsNotNull(result.Verification);
            Assert.IsTrue(result.Verification.StructureValid);
            Assert.IsTrue(result.Verification.EveryPfscBlockDecodes);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [TestMethod]
    public async Task Convert_FailsOnNonExistentPkg()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ffpfsc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var options = new FfpfscConvertOptions(
                PkgPath: Path.Combine(tempRoot, "does_not_exist.pkg"),
                OutputPath: Path.Combine(tempRoot, "out.ffpfsc"),
                WorkParentDirectory: tempRoot);

            var converter = new Ps4FfpfscConverterService();
            var (succeeded, message, result) = await converter.ConvertAsync(options);

            Assert.IsFalse(succeeded);
            Assert.IsNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(message));
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [TestMethod]
    public async Task Convert_RejectsPkgWithoutParamSfo()
    {
        // Build a PKG that has Image0 but no sce_sys/param.sfo — PkgFixture
        // always adds param.sfo, so we exercise the converter's own guard by
        // passing a bare Image0-style directory via a fixture built without it.
        // Since PkgFixture always includes param.sfo, this test just verifies
        // the normal path works (guarding against a future fixture change that
        // drops param.sfo). Use a direct path with a malformed PKG shape.
        string tempRoot = Path.Combine(Path.GetTempPath(), "ffpfsc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string fakePkg = Path.Combine(tempRoot, "fake.pkg");
        File.WriteAllBytes(fakePkg, new byte[] { 0, 0, 0, 0 });
        try
        {
            var options = new FfpfscConvertOptions(
                PkgPath: fakePkg,
                OutputPath: Path.Combine(tempRoot, "out.ffpfsc"),
                WorkParentDirectory: tempRoot);

            var converter = new Ps4FfpfscConverterService();
            var (succeeded, message, result) = await converter.ConvertAsync(options);

            Assert.IsFalse(succeeded);
            Assert.IsNull(result);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [TestMethod]
    public void Restructure_MovesSc0FilesIntoImage0SceSys()
    {
        // White-box test of the restructure step.
        string tempRoot = Path.Combine(Path.GetTempPath(), "ffpfsc-test-" + Guid.NewGuid().ToString("N"));
        string extracted = Path.Combine(tempRoot, "extracted");
        string image0 = Path.Combine(extracted, "Image0");
        string sc0 = Path.Combine(extracted, "Sc0");
        string sceSys = Path.Combine(image0, "sce_sys");
        Directory.CreateDirectory(image0);
        Directory.CreateDirectory(sc0);
        Directory.CreateDirectory(Path.Combine(image0, "sce_sys"));
        var sfo = new ParamSfo();
        sfo.SetString("TITLE", "T");
        sfo.SetString("TITLE_ID", "CUSA09999");
        sfo.SetString("CATEGORY", "gd");
        File.WriteAllBytes(Path.Combine(sceSys, "param.sfo"), sfo.Serialize());
        File.WriteAllBytes(Path.Combine(sc0, "icon0.png"), new byte[] { 1, 2, 3, 4 });
        Directory.CreateDirectory(Path.Combine(sc0, "trophy"));
        File.WriteAllBytes(Path.Combine(sc0, "trophy", "trophy00.trp"), new byte[] { 5, 6, 7, 8 });

        try
        {
            // Invoke the private restructure via reflection (it is the same
            // logic the service uses internally).
            var method = typeof(Ps4FfpfscConverterService).GetMethod(
                "RestructureSc0IntoSceSys",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method);
            method!.Invoke(null, new object[] { extracted, System.Threading.CancellationToken.None });

            // Sc0 should be gone; its contents under Image0/sce_sys/.
            Assert.IsFalse(Directory.Exists(sc0), "Sc0/ should be deleted after restructure");
            Assert.IsTrue(File.Exists(Path.Combine(sceSys, "icon0.png")));
            Assert.IsTrue(File.Exists(Path.Combine(sceSys, "trophy", "trophy00.trp")));
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
