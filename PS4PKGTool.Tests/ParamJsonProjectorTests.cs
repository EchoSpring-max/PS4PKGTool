using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrbisPkgTool.Sfo;
using PS4PKGTool.Utilities.Ffpfsc;
using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace PS4PKGTool.Tests;

/// <summary>
/// Tests for <see cref="PS4PKGTool.Utilities.Ffpfsc.ParamJsonProjector"/> â€”
/// the port of the SadykovIV ps4ffpsc <c>build_param_json</c> reference.
/// </summary>
[TestClass]
public sealed class ParamJsonProjectorTests
{
    private static ParamSfo MakeSfo(params (string Key, string Value)[] strings)
    {
        var sfo = new ParamSfo();
        foreach (var (key, value) in strings)
            sfo.SetString(key, value, Math.Max(0x20, value.Length + 1));
        return sfo;
    }

    private static ParamSfo MakeSfoWithInts(
        (string Key, string Value)[] strings, (string Key, int Value)[] ints)
    {
        var sfo = MakeSfo(strings);
        foreach (var (key, value) in ints)
            sfo.SetInt(key, value);
        return sfo;
    }

    [TestMethod]
    public void Build_ProjectsLocalizedTitlesIntoLocaleBuckets()
    {
        var sfo = MakeSfo(
            ("TITLE", "Base Title"),
            ("TITLE_00", "Japanese Name"),
            ("TITLE_01", "English Name"),
            ("TITLE_10", "Chinese Name"));

        byte[] json = ParamJsonProjector.Build("CUSA12345", "Base Title", sfo);

        var payload = JsonNode.Parse(json)!;
        Assert.AreEqual("CUSA12345", payload["titleId"]!.GetValue<string>());
        Assert.AreEqual("Base Title", payload["titleName"]!.GetValue<string>());
        Assert.AreEqual("Japanese Name", payload["localizedParameters"]!["ja-JP"]!["titleName"]!.GetValue<string>());
        Assert.AreEqual("English Name", payload["localizedParameters"]!["en-US"]!["titleName"]!.GetValue<string>());
        Assert.AreEqual("Chinese Name", payload["localizedParameters"]!["zh-Hant"]!["titleName"]!.GetValue<string>());
    }

    [TestMethod]
    public void Build_AlwaysEnsuresEnUsFallback()
    {
        var sfo = MakeSfo(("TITLE", "Only Title"));

        byte[] json = ParamJsonProjector.Build("CUSA12345", "Only Title", sfo);

        var payload = JsonNode.Parse(json)!;
        Assert.AreEqual("Only Title", payload["localizedParameters"]!["en-US"]!["titleName"]!.GetValue<string>());
        Assert.AreEqual("en-US", payload["localizedParameters"]!["defaultLanguage"]!.GetValue<string>());
    }

    [TestMethod]
    public void Build_EnUsPrefersExactTitle01Text()
    {
        var sfo = MakeSfo(("TITLE", "Main"), ("TITLE_01", "English Exact"));

        byte[] json = ParamJsonProjector.Build("CUSA12345", "Main", sfo);

        var payload = JsonNode.Parse(json)!;
        Assert.AreEqual("English Exact", payload["localizedParameters"]!["en-US"]!["titleName"]!.GetValue<string>());
    }

    [TestMethod]
    public void Build_PreservesExistingJsonFields()
    {
        var sfo = MakeSfo(("TITLE", "T"));
        byte[] existing = Encoding.UTF8.GetBytes(
            "{\"discVersion\":\"01.00\",\"localizedParameters\":{\"fr-FR\":{\"titleName\":\"Old\"}}}");

        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo, existing);

        var payload = JsonNode.Parse(json)!;
        Assert.AreEqual("01.00", payload["discVersion"]!.GetValue<string>());
        Assert.AreEqual("Old", payload["localizedParameters"]!["fr-FR"]!["titleName"]!.GetValue<string>());
        // en-US was added even though the existing JSON lacked it
        Assert.IsNotNull(payload["localizedParameters"]!["en-US"]);
    }

    [TestMethod]
    public void Build_DiscardsBomMarkedExistingData()
    {
        var sfo = MakeSfo(("TITLE", "T"));
        byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
        byte[] body = Encoding.UTF8.GetBytes("{\"preexisting\":\"value\"}");
        byte[] existing = bom.Concat(body).ToArray();

        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo, existing);

        var payload = JsonNode.Parse(json)!;
        Assert.IsNull(payload["preexisting"]);
    }

    [TestMethod]
    public void Build_MirrorsOnlyNonZeroUserDefinedParams()
    {
        var sfo = MakeSfoWithInts(
            new[] { ("TITLE", "T") },
            new[] { ("USER_DEFINED_PARAM_1", 3), ("USER_DEFINED_PARAM_2", 0), ("USER_DEFINED_PARAM_4", -1) });

        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo);

        var payload = JsonNode.Parse(json)!;
        Assert.AreEqual(3, payload["userDefinedParam1"]!.GetValue<int>());
        Assert.IsNull(payload["userDefinedParam2"]);
        Assert.IsNull(payload["userDefinedParam3"]);
        // Non-zero includes negative values â€” the reference mirrors value != 0.
        Assert.AreEqual(-1, payload["userDefinedParam4"]!.GetValue<int>());
    }

    [TestMethod]
    public void Build_OutputIsUtf8NoBomSortedAndNewlineTerminated()
    {
        var sfo = MakeSfo(("TITLE", "T"), ("TITLE_01", "E"));

        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo);

        Assert.IsFalse(json[0] == 0xEF, "Output must not start with a UTF-8 BOM");
        Assert.AreEqual((byte)'\n', json[^1], "Output must end with a newline");
        string text = Encoding.UTF8.GetString(json);
        StringAssert.Contains(text, "\"titleId\"", "Keys are present");

        // Top-level keys are written in ordinal-sorted order:
        //   "localizedParameters" < "titleId" < "titleName"
        // Anchor the needle at a line start so nested keys at a deeper
        // indent (e.g. localizedParameters.en-US.titleName) never match.
        int FindTopLevelKey(string key)
        {
            string needle = "\n  \"" + key + "\":";
            int idx = text.IndexOf(needle, StringComparison.Ordinal);
            Assert.AreNotEqual(-1, idx, $"Top-level key '{key}' not found");
            return idx;
        }
        int localized = FindTopLevelKey("localizedParameters");
        int titleId = FindTopLevelKey("titleId");
        int titleName = FindTopLevelKey("titleName");
        Assert.IsTrue(localized < titleId && titleId < titleName, "Top-level keys must be sorted");
    }

    [TestMethod]
    public void Build_DoesNotEscapeNonAscii()
    {
        var sfo = MakeSfo(("TITLE", "T"), ("TITLE_00", "ã‚¿ã‚¤ãƒˆãƒ«"));

        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo);

        string text = Encoding.UTF8.GetString(json);
        StringAssert.Contains(text, "ã‚¿ã‚¤ãƒˆãƒ«", "Non-ASCII must be written raw (ensure_ascii=False)");
    }

    [TestMethod]
    public void ChooseTitle_PrefersTitleField()
    {
        var sfo = MakeSfo(("TITLE", "Main"), ("TITLE_01", "English"));
        Assert.AreEqual("Main", ParamJsonProjector.ChooseTitle(sfo));
    }

    [TestMethod]
    public void ChooseTitle_FallsBackToTitle01ThenAnyTitle()
    {
        var noMain = MakeSfo(("TITLE_05", "Fifth"), ("TITLE_01", "English"));
        Assert.AreEqual("English", ParamJsonProjector.ChooseTitle(noMain));

        var noEnglish = MakeSfo(("TITLE_05", "Fifth"));
        Assert.AreEqual("Fifth", ParamJsonProjector.ChooseTitle(noEnglish));
    }

    [TestMethod]
    public void ChooseTitle_FallsBackToContentIdOrTitleId()
    {
        var sfo = MakeSfo(("CONTENT_ID", "EP0001-CUSA09999_00-X"));
        Assert.AreEqual("EP0001-CUSA09999_00-X", ParamJsonProjector.ChooseTitle(sfo));

        var sfo2 = MakeSfo(("TITLE_ID", "CUSA09999"));
        Assert.AreEqual("CUSA09999", ParamJsonProjector.ChooseTitle(sfo2));
    }

    [TestMethod]
    public void ChooseTitle_TrimsWhitespace()
    {
        var sfo = MakeSfo(("TITLE", "  Padded  "));
        Assert.AreEqual("Padded", ParamJsonProjector.ChooseTitle(sfo));
    }

    [TestMethod]
    public void ChooseTitle_UnknownGameFallback()
    {
        var sfo = new ParamSfo();
        Assert.AreEqual("Unknown Game", ParamJsonProjector.ChooseTitle(sfo));
    }

    [TestMethod]
    public void Validate_AcceptsWellFormedJson()
    {
        var sfo = MakeSfo(("TITLE", "T"));
        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo);

        var payload = ParamJsonProjector.Validate(json, "CUSA12345");
        Assert.AreEqual("T", payload["titleName"]!.GetValue<string>());
    }

    [TestMethod]
    public void Validate_RejectsBom()
    {
        var sfo = MakeSfo(("TITLE", "T"));
        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo);
        byte[] bommed = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(json).ToArray();

        Assert.ThrowsExactly<FormatException>(() => ParamJsonProjector.Validate(bommed, "CUSA12345"));
    }

    [TestMethod]
    public void Validate_RejectsTitleIdMismatch()
    {
        var sfo = MakeSfo(("TITLE", "T"));
        byte[] json = ParamJsonProjector.Build("CUSA12345", "T", sfo);

        Assert.ThrowsExactly<FormatException>(() => ParamJsonProjector.Validate(json, "CUSA99999"));
    }

    [TestMethod]
    public void Validate_RejectsMissingTitleName()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"titleId\":\"CUSA12345\"}");

        Assert.ThrowsExactly<FormatException>(() => ParamJsonProjector.Validate(json, "CUSA12345"));
    }

    [TestMethod]
    public void TitleLocales_MatchesReferenceOrder()
    {
        string[] expected =
        [
            "ja-JP", "en-US", "fr-FR", "es-ES", "de-DE", "it-IT", "nl-NL", "pt-PT", "ru-RU", "ko-KR",
            "zh-Hant", "zh-Hans", "fi-FI", "sv-SE", "da-DK", "no-NO", "pl-PL", "pt-BR", "en-GB", "tr-TR",
            "es-419", "ar-AE", "fr-CA", "cs-CZ", "hu-HU", "el-GR", "ro-RO", "th-TH", "vi-VN", "id-ID",
        ];
        CollectionAssert.AreEqual(expected, ParamJsonProjector.TitleLocales);
    }
}
