using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgInspection;
using System.IO;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class PkgExtractionTests
{
    // ── SanitizeFolderName (kept: full-width substitutes, reserved names) ──

    [TestMethod]
    public void SanitizeFolderName_PreservesSafeNames()
        => Assert.AreEqual("Bloodborne", PkgExtractionService.SanitizeFolderName("Bloodborne"));

    [TestMethod]
    public void SanitizeFolderName_FullWidthForForbiddenChars()
        => Assert.AreEqual("A：B", PkgExtractionService.SanitizeFolderName("A:B"));

    [TestMethod]
    public void SanitizeFolderName_EmptyReturnsUnderscore()
        => Assert.AreEqual("_", PkgExtractionService.SanitizeFolderName(""));

    [TestMethod]
    public void SanitizeFolderName_ReservedDeviceNamePrefixed()
        => Assert.AreEqual("_CON", PkgExtractionService.SanitizeFolderName("CON"));

    [TestMethod]
    public void SanitizeFolderName_ReplacesSlash()
        => Assert.IsFalse(PkgExtractionService.SanitizeFolderName("A/B").Contains('/'));

    // ── real PKG extraction via PkgFixture + PkgReader.ExtractAll ─────────

    [TestMethod]
    public async Task ExtractAll_RoundTripsSc0AndImage0Contents()
    {
        using var fixture = PkgFixture.Create("extract", ("a.bin", 3), ("dir/b.bin", 5));
        string outDir = Path.Combine(Path.GetTempPath(), "p4t-extract-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (succeeded, message) = await new PkgExtractionService()
                .ExtractFullAsync(fixture.PackagePath, outDir, null, CancellationToken.None);

            Assert.IsTrue(succeeded, message);
            // Image0 files round-trip byte-for-byte
            string aPath = Path.Combine(outDir, "Image0", "a.bin");
            string bPath = Path.Combine(outDir, "Image0", "dir", "b.bin");
            Assert.IsTrue(File.Exists(aPath), "Image0/a.bin missing");
            Assert.IsTrue(File.Exists(bPath), "Image0/dir/b.bin missing");
            CollectionAssert.AreEqual(fixture.Files[0].Data, File.ReadAllBytes(aPath));
            CollectionAssert.AreEqual(fixture.Files[1].Data, File.ReadAllBytes(bPath));
            // Sc0/param.sfo hoisted by PkgBuilder surfaces here too
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "Sc0", "param.sfo")));
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ExtractAll_UnicodePackagePathExtractsFromOriginal()
    {
        using var fixture = PkgFixture.Create("日本語 folder", "game Ω !@#.pkg",
            ("a.bin", 4), ("data/readme.txt", 16));
        string outDir = Path.Combine(Path.GetTempPath(), "p4t-extract-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (succeeded, message) = await new PkgExtractionService()
                .ExtractFullAsync(fixture.PackagePath, outDir, null, CancellationToken.None);

            Assert.IsTrue(succeeded, message);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "Image0", "a.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "Image0", "data", "readme.txt")));
            // Package untouched — no staging ever happened
            Assert.IsTrue(File.Exists(fixture.PackagePath));
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ExtractAll_CustomPasscodeRoundTrips()
    {
        const string passcode = "0123456789abcdef0123456789abcdef";
        using var fixture = PkgFixture.CreateWithPasscode("passcode", passcode, ("a.bin", 8));
        string outDir = Path.Combine(Path.GetTempPath(), "p4t-extract-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (succeeded, message) = await new PkgExtractionService(passcode)
                .ExtractFullAsync(fixture.PackagePath, outDir, null, CancellationToken.None);

            Assert.IsTrue(succeeded, message);
            CollectionAssert.AreEqual(fixture.Files[0].Data,
                File.ReadAllBytes(Path.Combine(outDir, "Image0", "a.bin")));
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ExtractAll_NotAPkgReportsFailureWithoutThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), "p4t-garbage-" + Guid.NewGuid().ToString("N") + ".pkg");
        string outDir = Path.Combine(Path.GetTempPath(), "p4t-extract-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(path, new byte[128]);

            var (succeeded, message) = await new PkgExtractionService()
                .ExtractFullAsync(path, outDir, null, CancellationToken.None);

            Assert.IsFalse(succeeded);
            Assert.IsFalse(string.IsNullOrEmpty(message));
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task ExtractAll_CancellationReportsCancelled()
    {
        using var fixture = PkgFixture.Create("cancel", ("a.bin", 3));
        string outDir = Path.Combine(Path.GetTempPath(), "p4t-extract-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var (succeeded, message) = await new PkgExtractionService()
                .ExtractFullAsync(fixture.PackagePath, outDir, null, cts.Token);

            Assert.IsFalse(succeeded);
            Assert.AreEqual("Cancelled", message);
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }
}
