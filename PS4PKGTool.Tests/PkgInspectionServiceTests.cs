using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgInspection;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class PkgInspectionServiceTests
{
    [TestMethod]
    public void Inspect_MapsCoreMetadataAndSfoValues()
    {
        string path = CreatePackageFile(2048);
        var raw = new RawPkgInspectionData
        {
            Title = "Example Game",
            TitleId = "CUSA12345",
            ContentId = "UP0000-CUSA12345_00-EXAMPLE000000000",
            PackageCategory = "GAME",
            PackageState = "FAKE",
            ApplicationVersion = "01.23",
            SfoEntries = new[]
            {
                new PkgSfoEntry("TITLE", "Example Game"),
                new PkgSfoEntry("VERSION", "01.02"),
                new PkgSfoEntry("SYSTEM_VER", "2304"),
                new PkgSfoEntry("PUBTOOLINFO", "sdk_ver=0A500000,c_date=2026-08-16")
            }
        };

        try
        {
            using PkgInspectionSnapshot snapshot = new PkgInspectionService(new StubReader(raw)).Inspect(path);

            Assert.AreEqual("Example Game", snapshot.Title);
            Assert.AreEqual("CUSA12345", snapshot.TitleId);
            Assert.AreEqual("GAME", snapshot.PackageCategory);
            Assert.AreEqual("FAKE", snapshot.PackageState);
            Assert.AreEqual("01.23", snapshot.ApplicationVersion);
            Assert.AreEqual("01.02", snapshot.PackageVersion);
            Assert.AreEqual("10.50", snapshot.RequiredFirmware);
            Assert.AreEqual(4, snapshot.SfoEntries.Count);
            Assert.AreEqual("0A500000", snapshot.BuildInfoFields
                .Single(field => field.Name == "PS4 SDK Version").Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.PackageSize));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Inspect_MissingOptionalData_ProducesUsableEmptySnapshot()
    {
        string path = CreatePackageFile(1);
        try
        {
            using PkgInspectionSnapshot snapshot = new PkgInspectionService(
                new StubReader(new RawPkgInspectionData())).Inspect(path);

            Assert.AreEqual(string.Empty, snapshot.Title);
            Assert.AreEqual(string.Empty, snapshot.TitleId);
            Assert.AreEqual(0, snapshot.SfoEntries.Count);
            Assert.IsNull(snapshot.Icon0);
            Assert.IsNull(snapshot.Pic0);
            Assert.IsNull(snapshot.Pic1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Inspect_FormatsExtendedSystemVersionEncoding()
    {
        string path = CreatePackageFile(1);
        try
        {
            using PkgInspectionSnapshot snapshot = new PkgInspectionService(new StubReader(
                new RawPkgInspectionData
                {
                    SfoEntries = new[] { new PkgSfoEntry("SYSTEM_VER", "173015040") } // 0x0A500000
                })).Inspect(path);

            Assert.AreEqual("10.50", snapshot.RequiredFirmware);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Inspect_ImagesAreDetachedAndPackageFileIsReleased()
    {
        string path = CreatePackageFile(32);
        byte[] png = CreatePng(Color.MediumPurple);
        using PkgInspectionSnapshot snapshot = new PkgInspectionService(new StubReader(
            new RawPkgInspectionData { Icon0Bytes = png })).Inspect(path);

        Array.Clear(png);
        File.Delete(path);

        Assert.IsNotNull(snapshot.Icon0);
        Assert.AreEqual(8, snapshot.Icon0.Width);
        Assert.AreEqual(Color.MediumPurple.ToArgb(), snapshot.Icon0.GetPixel(0, 0).ToArgb());
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Dispose_DisposesOwnedImages_AndIsIdempotent()
    {
        string path = CreatePackageFile(32);
        var snapshot = new PkgInspectionService(new StubReader(new RawPkgInspectionData
        {
            Icon0Bytes = CreatePng(Color.CadetBlue),
            Pic0Bytes = CreatePng(Color.Orange),
            Pic1Bytes = CreatePng(Color.ForestGreen)
        })).Inspect(path);
        Bitmap icon = snapshot.Icon0;

        try
        {
            snapshot.Dispose();
            snapshot.Dispose();

            Assert.IsTrue(snapshot.IsDisposed);
            Assert.ThrowsExactly<ArgumentException>(() => _ = icon.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Inspect_DamagedOptionalArtwork_IsTreatedAsMissing()
    {
        string path = CreatePackageFile(1);
        try
        {
            using PkgInspectionSnapshot snapshot = new PkgInspectionService(new StubReader(
                new RawPkgInspectionData { Pic0Bytes = new byte[] { 1, 2, 3, 4 } })).Inspect(path);

            Assert.IsNull(snapshot.Pic0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Inspect_ProjectsHeaderFieldsFromAuthoritativeReadResult()
    {
        string path = CreatePackageFile(1);
        try
        {
            using PkgInspectionSnapshot snapshot = new PkgInspectionService(new StubReader(
                new RawPkgInspectionData
                {
                    HeaderFields = new[]
                    {
                        new PkgInspectionField("Magic", "0x7F434E54"),
                        new PkgInspectionField("Content Type", "Game")
                    }
                })).Inspect(path);

            Assert.AreEqual(2, snapshot.HeaderFields.Count);
            Assert.AreEqual("Magic", snapshot.HeaderFields[0].Name);
            Assert.AreEqual("0x7F434E54", snapshot.HeaderFields[0].Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Inspect_MissingHeaderValues_ProducesEmptyHeaderProjection()
    {
        string path = CreatePackageFile(1);
        try
        {
            using PkgInspectionSnapshot snapshot = new PkgInspectionService(new StubReader(
                new RawPkgInspectionData { HeaderFields = null! })).Inspect(path);

            Assert.AreEqual(0, snapshot.HeaderFields.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void BuildInfo_ValidPubToolInfo_IsMappedWithFriendlyNames()
    {
        IReadOnlyList<PkgInspectionField> fields = PkgBuildInfoParser.Parse(
            "c_date=2026-08-16,sdk_ver=09000000,st_type=digital50,c_time=10:42:30");

        Assert.AreEqual("2026-08-16", fields.Single(field => field.Name == "Creation Date").Value);
        Assert.AreEqual("09000000", fields.Single(field => field.Name == "PS4 SDK Version").Value);
        Assert.AreEqual("digital50", fields.Single(field => field.Name == "Storage Type").Value);
        Assert.AreEqual("10:42:30", fields.Single(field => field.Name == "Creation Time").Value);
    }

    [TestMethod]
    public void BuildInfo_MissingPubToolInfo_IsEmpty()
    {
        Assert.AreEqual(0, PkgBuildInfoParser.Parse(null).Count);
        Assert.AreEqual(0, PkgBuildInfoParser.Parse(string.Empty).Count);
    }

    [TestMethod]
    public void BuildInfo_MalformedTokens_AreIgnoredWithoutThrowing()
    {
        IReadOnlyList<PkgInspectionField> fields = PkgBuildInfoParser.Parse(
            "broken-token,=missing-key,c_date=2026-08-16,sdk_ver=,also-broken");

        Assert.AreEqual(2, fields.Count);
        Assert.AreEqual("2026-08-16", fields.Single(field => field.Name == "Creation Date").Value);
        Assert.AreEqual(string.Empty, fields.Single(field => field.Name == "PS4 SDK Version").Value);
    }

    [TestMethod]
    public void RealReader_ReadsBuiltFixturePkg()
    {
        using var fixture = PkgFixture.CreateWithSceSys("inspect-real",
            ("icon0.png", CreatePng(Color.Red)),
            ("pic0.png", CreatePng(Color.Green)),
            ("pic1.png", CreatePng(Color.Blue)));

        var raw = new Ps4ToolsPkgInspectionReader().Read(fixture.PackagePath);

        Assert.AreEqual("Fix", raw.Title);
        Assert.AreEqual("CUSA09999", raw.TitleId);
        Assert.AreEqual("EP0001-CUSA09999_00-FIX0000000000001", raw.ContentId);
        Assert.AreEqual("Game", raw.PackageCategory);
        Assert.AreEqual("Fake", raw.PackageState);
        Assert.AreEqual("01.00", raw.ApplicationVersion);
        Assert.IsTrue(raw.SfoEntries.Any(e => e.Name == "TITLE" && e.Value == "Fix"));
        // The 46 legacy header rows are exposed as fields.
        Assert.AreEqual(46, raw.HeaderFields.Count);
        Assert.AreEqual("pkg_magic", raw.HeaderFields[0].Name);
        Assert.AreEqual("pkg_digest", raw.HeaderFields[45].Name);
        Assert.IsNotNull(raw.Icon0Bytes);
        Assert.IsNotNull(raw.Pic0Bytes);
        Assert.IsNotNull(raw.Pic1Bytes);
    }

    private static string CreatePackageFile(int length)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkg");
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }

    private static byte[] CreatePng(Color color)
    {
        using var bitmap = new Bitmap(8, 8);
        using (Graphics graphics = Graphics.FromImage(bitmap))
            graphics.Clear(color);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class StubReader : IPkgInspectionReader
    {
        private readonly RawPkgInspectionData _data;

        public StubReader(RawPkgInspectionData data) => _data = data;

        public RawPkgInspectionData Read(string packagePath) => _data;
    }
}
