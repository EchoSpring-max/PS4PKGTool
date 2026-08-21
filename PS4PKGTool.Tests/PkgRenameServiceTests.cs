using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgMeta;
using PS4PKGTool.Utilities.PkgRename;

namespace PS4PKGTool.Tests;

/// <summary>
/// PkgRenameService is the port of PS4_Tools GetNewPKGName — the token
/// expansion rules (incl. GetFirmware's hex formatting and
/// NewNameContentId2's Addon split) are pinned here.
/// </summary>
[TestClass]
public sealed class PkgRenameServiceTests
{
    [TestMethod]
    public void GetNewPKGName_ExpandsAllTokens()
    {
        using var fixture = PkgFixture.Create("rename-game", ("app/data.bin", 32));
        // fixture SFO: TITLE=Fix TITLE_ID=CUSA09999 APP_VER=01.00 VERSION=01.00
        // SYSTEM_VER=0x02700000 (40894464 decimal) CATEGORY=gd
        // CONTENT_ID=EP0001-CUSA09999_00-FIX0000000000001

        var (name, source, target) = PkgRenameService.GetNewPKGName(
            fixture.PackagePath, @"C:\out\",
            "{TITLE} {TITLE_ID} {APP_VERSION} {VERSION} {CATEGORY} {CONTENT_ID} {CONTENT_ID2} {REGION} {SYSTEM_VERSION}");

        Assert.AreEqual(fixture.PackagePath, source);
        StringAssert.Contains(name, "Fix CUSA09999 1.00 1.00 Game");
        StringAssert.Contains(name, "EP0001-CUSA09999_00-FIX0000000000001 EP0001-CUSA09999_00-FIX0000000000001-A0100-V0100");
        StringAssert.Contains(name, "EU 2.70");
        Assert.AreEqual(@"C:\out\" + name + ".pkg", target);
    }

    [TestMethod]
    public void GetFirmware_ZeroStaysZero()
    {
        var meta = new PkgMetadata
        {
            Header = new OrbisPkgTool.Pkg.PkgHeader(),
            BuildState = PkgBuildState.Fake,
            Kind = PkgKind.Game,
            ContentId = "EP0001-X",
            RawTitle = "",
            SfoTables = new[]
            {
                new OrbisPkgTool.Sfo.SfoTable("SYSTEM_VER", "0", 0x0404),
            },
        };
        Assert.AreEqual("0", PkgRenameService.GetFirmware(meta));
    }

    [TestMethod]
    public void GetFirmware_FormatsHexWithDot()
    {
        // 40894464 = 0x2700000 → "2700000" → first 3 "270" → "2.70"
        var meta = new PkgMetadata
        {
            Header = new OrbisPkgTool.Pkg.PkgHeader(),
            BuildState = PkgBuildState.Fake,
            Kind = PkgKind.Game,
            ContentId = "EP0001-X",
            RawTitle = "",
            SfoTables = new[]
            {
                new OrbisPkgTool.Sfo.SfoTable("SYSTEM_VER", "40894464", 0x0404),
            },
        };
        Assert.AreEqual("2.70", PkgRenameService.GetFirmware(meta));
    }

    [TestMethod]
    public void NewNameContentId2_AddonUsesA0000()
    {
        var sfo = new OrbisPkgTool.Sfo.ParamSfo();
        sfo.SetString("CONTENT_ID", "EP0001-CUSA09999_00-DLC", 0x30);
        sfo.SetString("APP_VER", "01.00", 8);
        sfo.SetString("VERSION", "01.02", 8);

        var meta = new PkgMetadata
        {
            Header = new OrbisPkgTool.Pkg.PkgHeader(),
            BuildState = PkgBuildState.Fake,
            Kind = PkgKind.Addon,
            ContentId = "EP0001-Y",
            RawTitle = "",
            ParamSfo = sfo,
            SfoTables = sfo.Tables,
        };
        Assert.AreEqual(
            "EP0001-CUSA09999_00-DLC-A0000-V0102",
            PkgRenameService.NewNameContentId2(meta));
    }

    [TestMethod]
    public void NewNameContentId2_GameAppendsAppVerAndVersion()
    {
        var sfo = new OrbisPkgTool.Sfo.ParamSfo();
        sfo.SetString("CONTENT_ID", "EP0001-CUSA09999_00-GAME", 0x30);
        sfo.SetString("APP_VER", "01.03", 8);
        sfo.SetString("VERSION", "01.00", 8);

        var meta = new PkgMetadata
        {
            Header = new OrbisPkgTool.Pkg.PkgHeader(),
            BuildState = PkgBuildState.Fake,
            Kind = PkgKind.Game,
            ContentId = "EP0001-Y",
            RawTitle = "",
            ParamSfo = sfo,
            SfoTables = sfo.Tables,
        };
        Assert.AreEqual(
            "EP0001-CUSA09999_00-GAME-A0103-V0100",
            PkgRenameService.NewNameContentId2(meta));
    }
}
