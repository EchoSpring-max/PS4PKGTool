using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Tests;

[TestClass]
public class Shadps4PatchCheatDefinitionsTests
{
    [TestMethod]
    public void CheatParser_UsesJsonMetadataAndCountsOperations()
    {
        const string json = """{ "name":"Test", "id":"CUSA00001", "version":"01.09", "process":"eboot.bin", "credits":["A"], "mods":[{"name":"Infinite","hint":"hint","type":"checkbox","memory":[{"offset":"1"},{"offset":"2"}]}] }""";
        bool ok = Shadps4DefinitionParser.TryParseCheat(json, "CUSA00001_01.08_2.json", "shadPS4", out var cheat, out _);
        Assert.IsTrue(ok);
        Assert.AreEqual("01.09", cheat.Version);
        Assert.AreEqual(2, cheat.Mods[0].OperationCount);
    }

    [TestMethod]
    public void CheatParser_RejectsMalformedJson()
    {
        Assert.IsFalse(Shadps4DefinitionParser.TryParseCheat("{", "bad.json", "shadPS4", out _, out var error));
        StringAssert.Contains(error, "Invalid cheat JSON");
    }

    [TestMethod]
    public void PatchParser_ReadsMultipleIdsAndMetadata()
    {
        const string xml = """<Patch><TitleID><ID>CUSA00001</ID><ID>CUSA00002</ID></TitleID><Metadata Name="60 FPS" Author="A" PatchVer="1.0" AppVer="01.09" AppElf="eboot.bin" isEnabled="true"><PatchList><Line Type="bytes"/><Line Type="float32"/></PatchList></Metadata></Patch>""";
        Assert.IsTrue(Shadps4DefinitionParser.TryParsePatch(xml, "test.xml", "shadPS4", out var patch, out _));
        Assert.AreEqual(2, patch.TitleIds.Count);
        Assert.AreEqual(2, patch.Entries[0].OperationCount);
        Assert.AreEqual("bytes, float32", patch.Entries[0].OperationSummary);
    }

    [TestMethod]
    public void VersionMatcher_IsExactOnlyAfterTrimming()
    {
        Assert.AreEqual(Shadps4DefinitionMatch.ExactMatch, Shadps4DefinitionMatcher.Version(" 01.09 ", "01.09"));
        Assert.AreEqual(Shadps4DefinitionMatch.VersionMismatch, Shadps4DefinitionMatcher.Version("banana", "01.09"));
        Assert.AreEqual(Shadps4DefinitionMatch.Unknown, Shadps4DefinitionMatcher.Version("", "01.09"));
    }

    [TestMethod]
    public void PatchStore_ChangesOnlyRequestedEnabledStateAndBuildsIndex()
    {
        string root = Path.Combine(Path.GetTempPath(), "ps4pkgtool-patch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string xml = """<!-- keep --><Patch><TitleID><ID>CUSA00001</ID></TitleID><Metadata Name="One" AppVer="01.00" isEnabled="false" Extra="preserve"><PatchList><Line Type="future" /></PatchList></Metadata><Metadata Name="Two" AppVer="01.00" isEnabled="false" /></Patch>""";
            var store = new Shadps4PatchCheatStore();
            store.InstallPatch(root, "test.xml", xml);
            string path = Path.Combine(Shadps4PatchCheatStore.PatchesDirectory(root), "test.xml");
            store.SetPatchEnabledStates(new[] { new Shadps4PatchStateChange(path, 1, true) });
            string result = File.ReadAllText(path);
            StringAssert.Contains(result, "Name=\"One\"");
            StringAssert.Contains(result, "Extra=\"preserve\"");
            StringAssert.Contains(result, "Name=\"Two\"");
            StringAssert.Contains(result, "isEnabled=\"true\"");
            store.RebuildPatchIndex(root);
            StringAssert.Contains(File.ReadAllText(Path.Combine(Shadps4PatchCheatStore.PatchesDirectory(root), "files.json")), "CUSA00001");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
