using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrbisPkgTool.Pkg;
using OrbisPkgTool.Trp;
using PS4PKGTool.Utilities.PkgMeta;

namespace PS4PKGTool.Tests;

/// <summary>
/// PkgMetadataReader/PkgMetadata are the drop-in replacements for
/// PS4_Tools Read_PKG/Unprotected_PKG — these tests pin the legacy
/// semantics that downstream code depends on (type strings, region map,
/// official-probe, SFO fallbacks, artwork/trophy loading).
/// </summary>
[TestClass]
public sealed class PkgMetadataTests
{
    private static byte[] PngBytes(byte seed) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, seed, seed, seed];

    private static byte[] TrpBytes()
    {
        var entries = new List<TrpEntry>
        {
            new() { Name = "ICON0.PNG", Data = PngBytes(0x7A) },
            new() { Name = "TROP.SFM", Data = "<trophydata/>"u8.ToArray() },
        };
        return Trp.Write(entries);
    }

    [TestMethod]
    public void Read_GamePkg_MapsCoreMetadata()
    {
        using var fixture = PkgFixture.CreateWithSceSys("meta-game",
            ("icon0.png", PngBytes(1)),
            ("pic0.png", PngBytes(2)),
            ("pic1.png", PngBytes(3)));

        var meta = PkgMetadataReader.Read(fixture.PackagePath);

        Assert.AreEqual("Fix", meta.PS4_Title);
        Assert.AreEqual("CUSA09999", meta.TITLEID);
        Assert.AreEqual("EP0001-CUSA09999_00-FIX0000000000001", meta.Content_ID);
        Assert.AreEqual(PkgKind.Game, meta.PKG_Type);
        Assert.AreEqual("Game", meta.PKG_Type.ToString());
        Assert.AreEqual(PkgBuildState.Fake, meta.PKGState);
        Assert.AreEqual("Fake", meta.PKGState.ToString());
        Assert.AreEqual("EU", meta.Region);
        Assert.AreEqual("01.00", meta.APP_VER);
        Assert.AreEqual("gd", meta.Category);
        Assert.IsNotNull(meta.Icon);
        CollectionAssert.AreEqual(PngBytes(1), meta.Icon);
        CollectionAssert.AreEqual(PngBytes(2), meta.Pic0);
        CollectionAssert.AreEqual(PngBytes(3), meta.Pic1);
        Assert.IsNull(meta.TrpData);
    }

    [TestMethod]
    public void Read_TrophyFallback_SuppliesIconFromTrp()
    {
        byte[] trp = TrpBytes();
        using var fixture = PkgFixture.CreateWithSceSys("meta-trp",
            ("trophy/trophy00.trp", trp));

        var meta = PkgMetadataReader.Read(fixture.PackagePath);

        // No icon0.png in the package → icon falls back to the trophy
        // pack's ICON0.PNG entry.
        Assert.IsNotNull(meta.Icon);
        CollectionAssert.AreEqual(PngBytes(0x7A), meta.Icon);
        Assert.IsNotNull(meta.TrpData);
        CollectionAssert.AreEqual(trp, meta.TrpData);
    }

    [TestMethod]
    public void Read_SystemVerRow_IsDecimalString()
    {
        using var fixture = PkgFixture.Create("meta-sysver", ("app/data.bin", 64));
        var meta = PkgMetadataReader.Read(fixture.PackagePath);

        // CreateGameTemplate sets SYSTEM_VER = 0x02700000 (int format) —
        // the legacy Tables accessor rendered int rows as decimal strings.
        var sysVer = meta.SfoTables!.Single(t => t.Name == "SYSTEM_VER");
        Assert.AreEqual("40894464", sysVer.Value);
        Assert.AreEqual(0x0404, sysVer.Format);
        // The full table is exposed in SFO order.
        Assert.IsTrue(meta.SfoTables.Any(t => t.Name == "TITLE" && t.Value == "Fix"));
    }

    [TestMethod]
    public void DetectBuildState_OfficialProbes()
    {
        // DP probe (u16@0x77 == 0x1E43) wins over the type probe.
        Assert.AreEqual(PkgBuildState.Official_DP,
            PkgMetadataReader.DetectBuildState(new PkgHeader { ContentType = 0x0000001E, ContentFlags = 0x43000000 }));
        Assert.AreEqual(PkgBuildState.Official,
            PkgMetadataReader.DetectBuildState(new PkgHeader { Flags = 0x83000000 }));
        Assert.AreEqual(PkgBuildState.Official,
            PkgMetadataReader.DetectBuildState(new PkgHeader { Flags = 0x81000000 }));
        // PkgBuilder writes Flags=1 → Fake.
        Assert.AreEqual(PkgBuildState.Fake,
            PkgMetadataReader.DetectBuildState(new PkgHeader { Flags = 0x00000001 }));
        Assert.AreEqual(PkgBuildState.Fake,
            PkgMetadataReader.DetectBuildState(new PkgHeader()));
    }

    [TestMethod]
    public void GetPkgType_LegacyCategoryStrings()
    {
        Assert.AreEqual(PkgKind.App, PkgMetadata.GetPkgType("gde"));
        Assert.AreEqual(PkgKind.App, PkgMetadata.GetPkgType("gdk"));
        Assert.AreEqual(PkgKind.Game, PkgMetadata.GetPkgType("gd"));
        Assert.AreEqual(PkgKind.Addon, PkgMetadata.GetPkgType("ac"));
        Assert.AreEqual(PkgKind.Patch, PkgMetadata.GetPkgType("gp"));
        // Legacy returned Unknown for anything else (themes "th" incl.).
        Assert.AreEqual(PkgKind.Unknown, PkgMetadata.GetPkgType("th"));
        Assert.AreEqual(PkgKind.Unknown, PkgMetadata.GetPkgType(""));
    }

    private static string RegionOf(string contentId) => new PkgMetadata
    {
        Header = new PkgHeader(),
        BuildState = PkgBuildState.Fake,
        Kind = PkgKind.Unknown,
        ContentId = contentId,
        RawTitle = "",
    }.Region;

    [TestMethod]
    public void Region_AllLegacyMappings()
    {
        Assert.AreEqual("EU", RegionOf("EP0001-CUSA0"));
        Assert.AreEqual("US", RegionOf("UP0001-CUSA0"));
        Assert.AreEqual("US", RegionOf("IP0001-CUSA0"));
        Assert.AreEqual("JAPAN", RegionOf("JP0001-CUSA0"));
        Assert.AreEqual("HONG KONG", RegionOf("HP0001-CUSA0"));
        Assert.AreEqual("ASIA", RegionOf("AP0001-CUSA0"));
        Assert.AreEqual("KOREA", RegionOf("KP0001-CUSA0"));
        Assert.AreEqual("", RegionOf("XP0001-CUSA0"));
        Assert.AreEqual("", RegionOf(""));
    }

    [TestMethod]
    public void Metadata_MissingSfo_AccessorsFallBackToEmpty()
    {
        var meta = new PkgMetadata
        {
            Header = new PkgHeader(),
            BuildState = PkgBuildState.Fake,
            Kind = PkgKind.Unknown,
            ContentId = "EP0001-X",
            RawTitle = null,
            ParamSfo = null,
            SfoTables = null,
        };
        Assert.AreEqual("", meta.PS4_Title);
        Assert.AreEqual("", meta.TITLEID);
        Assert.AreEqual("", meta.APP_VER);
        Assert.AreEqual("", meta.Category);
        Assert.AreEqual("", meta.SfoContentId);
    }

    [TestMethod]
    public void Read_BadPath_Throws()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => PkgMetadataReader.Read(@"X:\nonexistent\missing.pkg"));
    }
}
