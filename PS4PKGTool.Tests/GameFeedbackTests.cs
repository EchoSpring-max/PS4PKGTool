using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Shadps4;
using System;
using System.IO;
using System.Text;

namespace PS4PKGTool.Tests
{
    [TestClass]
    public class GameFeedbackTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_feedback_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        [TestMethod]
        public void Store_AppendWritesJsonlAndReadsBack()
        {
            string file = Path.Combine(_tempRoot, "game-feedback.jsonl");
            var entry = new GameFeedbackEntry
            {
                TimestampUtc = "2026-08-15T04:00:00Z",
                TitleId = "CUSA05187",
                Title = "Bloodborne",
                GameVersion = "01.09",
                Core = "2026-08-13 (e81418b4)",
                Launcher = "2026-08-08 (a12b988e)",
                Status = "Playable",
                Comment = "no glitches so far",
            };
            GameFeedbackStore.Append(entry, file);
            GameFeedbackStore.Append(entry, file);

            var all = GameFeedbackStore.ReadAll(file);

            Assert.AreEqual(2, all.Count, "one JSON object per session, appended");
            Assert.AreEqual("CUSA05187", all[1].TitleId);
            Assert.AreEqual("Playable", all[1].Status);
            Assert.AreEqual("no glitches so far", all[1].Comment);
            Assert.AreEqual("2026-08-13 (e81418b4)", all[1].Core);
        }

        [TestMethod]
        public void Store_MissingFileReadsEmpty()
        {
            Assert.AreEqual(0, GameFeedbackStore.ReadAll(Path.Combine(_tempRoot, "nope.jsonl")).Count);
        }

        [TestMethod]
        public void Store_CorruptLineIsSkipped()
        {
            string file = Path.Combine(_tempRoot, "game-feedback.jsonl");
            File.WriteAllText(file, "{not json}\n");

            var all = GameFeedbackStore.ReadAll(file);

            Assert.AreEqual(0, all.Count, "a corrupt line never breaks reading");
        }

        [TestMethod]
        public void Store_SetBestProfileMarksOneAndClearsOthers()
        {
            string file = Path.Combine(_tempRoot, "game-feedback.jsonl");
            var a = new GameFeedbackEntry { TimestampUtc = "2026-08-15T01:00:00Z", TitleId = "CUSA05187", Status = "Boots" };
            var b = new GameFeedbackEntry { TimestampUtc = "2026-08-15T02:00:00Z", TitleId = "CUSA05187", Status = "Playable" };
            var other = new GameFeedbackEntry { TimestampUtc = "2026-08-15T03:00:00Z", TitleId = "CUSA09999", Status = "Playable" };
            GameFeedbackStore.Append(a, file);
            GameFeedbackStore.Append(b, file);
            GameFeedbackStore.Append(other, file);

            // Mark the middle entry as best, then switch to the first.
            GameFeedbackStore.SetBestProfile("CUSA05187", "2026-08-15T02:00:00Z", file);
            GameFeedbackStore.SetBestProfile("CUSA05187", "2026-08-15T01:00:00Z", file);

            var all = GameFeedbackStore.ReadAll(file);
            Assert.IsTrue(all[0].IsBestProfile, "the newly selected entry is best");
            Assert.IsFalse(all[1].IsBestProfile, "the previously best entry is cleared");
            Assert.IsFalse(all[2].IsBestProfile, "other games are untouched");
        }

        [TestMethod]
        public void Store_SetBestProfileUnknownEntryIsNoop()
        {
            string file = Path.Combine(_tempRoot, "game-feedback.jsonl");
            var a = new GameFeedbackEntry { TimestampUtc = "2026-08-15T01:00:00Z", TitleId = "CUSA05187", Status = "Boots" };
            GameFeedbackStore.Append(a, file);

            GameFeedbackStore.SetBestProfile("CUSA05187", "never-existed", file);

            var all = GameFeedbackStore.ReadAll(file);
            Assert.AreEqual(1, all.Count);
            Assert.IsFalse(all[0].IsBestProfile, "no entry matched - nothing changes");
        }

        [TestMethod]
        public void Sfo_ReadGameInfo_ReturnsTitleAndAppVersion()
        {
            string sfoPath = Path.Combine(_tempRoot, "CUSA23558", "sce_sys", "param.sfo");
            WriteParamSfo(sfoPath, "Terra Bomber", "1.00");

            var info = ParamSfoReader.ReadGameInfo(sfoPath);

            Assert.IsNotNull(info);
            Assert.AreEqual("Terra Bomber", info.Value.Title);
            Assert.AreEqual("1.00", info.Value.AppVersion);
        }

        [TestMethod]
        public void Sfo_ReadGameInfo_UnreadableReturnsNull()
        {
            string sfoPath = Path.Combine(_tempRoot, "param.sfo");
            File.WriteAllText(sfoPath, "not a psf file");

            Assert.IsNull(ParamSfoReader.ReadGameInfo(sfoPath));
            Assert.IsNull(ParamSfoReader.ReadAppVersion(sfoPath), "legacy reader keeps its null contract");
        }

        /// <summary>
        /// Builds a minimal real param.sfo (PSF format: header + entry table
        /// + key table + data table) with TITLE and APP_VER string entries.
        /// </summary>
        private static void WriteParamSfo(string path, string title, string appVer)
        {
            var keys = new[] { "TITLE", "APP_VER" };
            var values = new[] { title, appVer };

            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                uint entryTableSize = (uint)(keys.Length * 16);
                uint keyTableSize = 0;
                foreach (string k in keys) keyTableSize += (uint)k.Length + 1;
                // Real layout (verified against an extracted game's sfo):
                // header (20) -> entry table -> key table -> data table.
                uint keyOffset = 20 + entryTableSize;
                uint dataOffset = keyOffset + keyTableSize;

                bw.Write(new byte[] { 0x00, 0x50, 0x53, 0x46 }); // "\0PSF"
                bw.Write(0x00000101u);                            // version
                bw.Write(keyOffset);                              // key table offset
                bw.Write(dataOffset);                             // data table offset
                bw.Write(keys.Length);                            // entry count

                uint keyPos = 0, dataPos = 0;
                for (int i = 0; i < keys.Length; i++)
                {
                    byte[] val = Encoding.UTF8.GetBytes(values[i] + "\0");
                    bw.Write((ushort)keyPos);
                    bw.Write((ushort)0x0204);                     // PSF string
                    bw.Write(val.Length);                         // length
                    bw.Write(val.Length);                         // max length
                    bw.Write(dataPos);                            // value offset
                    keyPos += (uint)keys[i].Length + 1;
                    dataPos += (uint)val.Length;
                }
                foreach (string k in keys)
                {
                    bw.Write(Encoding.ASCII.GetBytes(k));
                    bw.Write((byte)0);
                }
                foreach (string v in values)
                {
                    bw.Write(Encoding.UTF8.GetBytes(v));
                    bw.Write((byte)0);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, ms.ToArray());
        }

        [TestMethod]
        public void Formatter_MatchesUpstreamSubmissionTemplate()
        {
            var entry = new GameFeedbackEntry
            {
                Title = "Terra Bomber",
                TitleId = "CUSA23558",
                GameVersion = "1.00",
                EmulatorVersion = "0.17.0",
                Status = "Boots",
                Os = "Linux",
                Processor = "i7 10870H",
                GraphicsCard = "RTX 3070",
                Error = "[Debug] <Critical> liverpool_to_vk.cpp:138 PrimitiveType: Unreachable code!",
                Comment = "Boots on 0.17.0, but will crash within a few seconds.",
            };

            string text = Shadps4SubmissionFormatter.FormatSubmissionText(entry);

            const string expected =
                "Game Name\nTerra Bomber\n\n" +
                "Game serial\nCUSA23558\n\n" +
                "Game version\n1.00\n\n" +
                "Used emulator's version (only major released versions are acceptable)\n0.17.0\n\n" +
                "Current status\nBoots\n\n" +
                "Operating System\nLinux\n\n" +
                "Processor\ni7 10870H\n\n" +
                "Graphics Card\nRTX 3070\n\n" +
                "Error\n[Debug] <Critical> liverpool_to_vk.cpp:138 PrimitiveType: Unreachable code!\n\n" +
                "Description\nBoots on 0.17.0, but will crash within a few seconds.";
            Assert.AreEqual(expected, text.Replace("\r\n", "\n"),
                "template structure matches the upstream issue format (line endings aside)");
        }

        [TestMethod]
        public void Report_MatchesMarkdownTemplateWithChecklistAndPlaceholders()
        {
            var entry = new GameFeedbackEntry
            {
                Title = "Terra Bomber",
                TitleId = "CUSA23558",
                GameVersion = "1.00",
                EmulatorVersion = "0.17.0",
                Status = "Boots",
                Os = "Windows 11",
                Processor = "i7 10870H",
                GraphicsCard = "RTX 3070",
                Error = "crash in liverpool_to_vk.cpp",
                Comment = "Boots but crashes within seconds.",
            };
            var checklist = new[]
            {
                true, false, true, false, false, true,
            };

            string text = CompatibilityReport.BuildMarkdown(entry, checklist, "CUSA23558.log").Replace("\r\n", "\n");

            const string expected =
                "### Checklist\n" +
                "- [x] Tested on required release build\n" +
                "- [ ] Checked for an existing report\n" +
                "- [x] Own and unmodified game dump\n" +
                "- [ ] Required firmware modules installed\n" +
                "- [ ] Sync logging enabled\n" +
                "- [x] Default emulation settings used\n" +
                "\n### Game Name\n\nTerra Bomber\n" +
                "\n### Game serial\n\nCUSA23558\n" +
                "\n### Game version\n\nv1.00\n" +
                "\n### Used emulator's version\n\n0.17.0\n" +
                "\n### Current status\n\nBoots\n" +
                "\n### Operating System\n\nWindows 11\n" +
                "\n### Processor\n\ni7 10870H\n" +
                "\n### Graphics Card\n\nRTX 3070\n" +
                "\n### Error\n\ncrash in liverpool_to_vk.cpp\n" +
                "\n### Description\n\nBoots but crashes within seconds.\n" +
                "### Screenshots\n\n<!-- Add screenshots here -->\n" +
                "\n### Log File\n\n<!-- Attach CUSA23558.log here -->";
            Assert.AreEqual(expected, text,
                "markdown structure matches the shadPS4 template with checklist and placeholders");
        }

        [TestMethod]
        public void Report_NoChecklistMeansAllUnchecked_UnknownsAreMarked()
        {
            var entry = new GameFeedbackEntry
            {
                TitleId = "CUSA23558",
                Status = "In-Game",
            };

            string text = CompatibilityReport.BuildMarkdown(entry, logFileName: "").Replace("\r\n", "\n");

            StringAssert.Contains(text, "- [ ] Tested on required release build");
            StringAssert.Contains(text, "- [ ] Default emulation settings used");
            StringAssert.Contains(text, "\n### Game Name\n\n(unknown)");
            StringAssert.Contains(text, "\n### Game version\n\n(unknown)");
            StringAssert.Contains(text, "\n### Operating System\n\n(unknown)");
            StringAssert.Contains(text, "<!-- Attach the log file here -->");
            Assert.IsFalse(text.Contains("v(unknown)"), "version unknown marker must not get a v prefix");
        }

        [TestMethod]
        public void Report_VersionDisplay_NormalizesVPrefix()
        {
            Assert.AreEqual("v1.00", CompatibilityReport.VersionDisplay("1.00"));
            Assert.AreEqual("v01.09", CompatibilityReport.VersionDisplay("01.09"));
            Assert.AreEqual("v0.17.0", CompatibilityReport.VersionDisplay("v0.17.0"));
            Assert.AreEqual("(unknown)", CompatibilityReport.VersionDisplay(""));
        }

        // ── structured session fields (Phase B: historical correctness) ──

        [TestMethod]
        public void Store_OldRecordWithoutNewFields_DeserializesWithDefaults()
        {
            // A JSONL line exactly as written before the session fields
            // existed - must keep loading and show defaults, never fail.
            string file = Path.Combine(_tempRoot, "game-feedback.jsonl");
            File.WriteAllText(file,
                "{\"TimestampUtc\":\"2026-08-01T04:00:00Z\",\"TitleId\":\"CUSA05187\"," +
                "\"Title\":\"Bloodborne\",\"GameVersion\":\"01.09\",\"Core\":\"v.0.17.0\"," +
                "\"Status\":\"In-Game\",\"Comment\":\"old record\"}\n");

            var all = GameFeedbackStore.ReadAll(file);

            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("Bloodborne", all[0].Title);
            Assert.AreEqual("In-Game", all[0].Status);
            Assert.IsNull(all[0].RuntimeSeconds, "old records have no runtime");
            Assert.IsNull(all[0].ExitCode, "old records have no exit code");
            Assert.AreEqual("", all[0].TerminationKind);
            Assert.AreEqual("", all[0].CoreId);
            Assert.IsNull(all[0].LogReference);
            Assert.IsFalse(all[0].Crashed, "no termination info means not crashed");
            Assert.IsFalse(all[0].IsBestProfile);
        }

        [TestMethod]
        public void Store_NewFieldsRoundTrip_ExactValuesPreserved()
        {
            string file = Path.Combine(_tempRoot, "game-feedback.jsonl");
            var entry = new GameFeedbackEntry
            {
                TimestampUtc = "2026-08-19T04:00:00Z",
                TitleId = "CUSA00900",
                Title = "Terra Bomber",
                Status = "In-Game",
                ResultId = "0123456789abcdef0123456789abcdef",
                ResultSource = "LaunchSession",
                RuntimeSeconds = 1302,
                TerminationKind = "AccessViolation",
                ExitCode = unchecked((int)0xC0000005),
                ExitStatusName = "STATUS_ACCESS_VIOLATION",
                CoreId = "bed2fd8d5f1481e84486204e0aaee80d2409b1c1",
                CoreCommit = "bed2fd8d",
                CoreVersion = "0.17.1.0",
                LogReference = @"C:\AppData\PS4PKGTool\Shadps4Reports\Logs\abc.log",
                Comment = "crash entering Cathedral Ward",
            };
            GameFeedbackStore.Append(entry, file);

            var all = GameFeedbackStore.ReadAll(file);

            Assert.AreEqual(1, all.Count);
            var loaded = all[0];
            Assert.AreEqual("0123456789abcdef0123456789abcdef", loaded.ResultId);
            Assert.AreEqual("LaunchSession", loaded.ResultSource);
            Assert.AreEqual(1302, loaded.RuntimeSeconds);
            Assert.AreEqual("AccessViolation", loaded.TerminationKind);
            Assert.AreEqual(0xC0000005, unchecked((uint)loaded.ExitCode!.Value));
            Assert.AreEqual("STATUS_ACCESS_VIOLATION", loaded.ExitStatusName);
            Assert.AreEqual("bed2fd8d5f1481e84486204e0aaee80d2409b1c1", loaded.CoreId);
            Assert.AreEqual("bed2fd8d", loaded.CoreCommit);
            Assert.AreEqual("0.17.1.0", loaded.CoreVersion);
            Assert.AreEqual(@"C:\AppData\PS4PKGTool\Shadps4Reports\Logs\abc.log", loaded.LogReference);
            Assert.IsTrue(loaded.Crashed, "AccessViolation is an error category - crashed, yet status stays independent");
            Assert.AreEqual("In-Game", loaded.Status, "compatibility status is NOT derived from the crash");
        }

        [TestMethod]
        public void Crashed_UsesTerminationClassification_NotExitCodeNonZero()
        {
            // Exit code 1 (Unknown) is NOT a crash per the termination
            // subsystem; an error NTSTATUS is.
            Assert.IsFalse(new GameFeedbackEntry { TerminationKind = "Unknown", ExitCode = 1 }.Crashed);
            Assert.IsFalse(new GameFeedbackEntry { TerminationKind = "NormalExit", ExitCode = 0 }.Crashed);
            Assert.IsFalse(new GameFeedbackEntry { TerminationKind = "" }.Crashed);
            Assert.IsTrue(new GameFeedbackEntry { TerminationKind = "AccessViolation" }.Crashed);
            Assert.IsTrue(new GameFeedbackEntry { TerminationKind = "FailFast" }.Crashed);
        }

        [TestMethod]
        public void Runtime_FormatsNumericallyFromSeconds()
        {
            Assert.AreEqual("18s", TestResultDisplay.FormatRuntime(18));
            Assert.AreEqual("47m", TestResultDisplay.FormatRuntime(47 * 60));
            Assert.AreEqual("47m 12s", TestResultDisplay.FormatRuntime(2832));
            Assert.AreEqual("2h 13m", TestResultDisplay.FormatRuntime(2 * 3600 + 13 * 60 + 4));
            Assert.AreEqual("Unknown", TestResultDisplay.FormatRuntime(null));
        }

        [TestMethod]
        public void ExitCode_FormatsHex_OrNotRecorded()
        {
            Assert.AreEqual("0xC0000005", TestResultDisplay.FormatExitCode(unchecked((int)0xC0000005)));
            Assert.AreEqual("0x00000000", TestResultDisplay.FormatExitCode(0));
            Assert.AreEqual("Not recorded", TestResultDisplay.FormatExitCode(null));
        }

        [TestMethod]
        public void Report_NormalizesStatusSpelling_ForUpstreamTemplate()
        {
            Assert.AreEqual("Ingame", CompatibilityReport.NormalizeStatus("In-Game"));
            Assert.AreEqual("Ingame", CompatibilityReport.NormalizeStatus("In Game"));
            Assert.AreEqual("Playable", CompatibilityReport.NormalizeStatus("Playable"));
            Assert.AreEqual("Boots", CompatibilityReport.NormalizeStatus("Boots"));
            Assert.AreEqual("", CompatibilityReport.NormalizeStatus(""));

            var entry = new GameFeedbackEntry { Title = "T", TitleId = "CUSA00001", Status = "In-Game" };
            string markdown = CompatibilityReport.BuildMarkdown(entry, logFileName: "").Replace("\r\n", "\n");
            StringAssert.Contains(markdown, "\n### Current status\n\nIngame");
        }

        [TestMethod]
        public void Report_OldRecordWithoutSessionFields_StillGeneratesMarkdown()
        {
            var entry = new GameFeedbackEntry
            {
                TimestampUtc = "2026-08-01T04:00:00Z",
                Title = "Terra Bomber",
                TitleId = "CUSA23558",
                GameVersion = "1.00",
                EmulatorVersion = "0.17.0",
                Status = "Boots",
                Os = "Windows 11",
            };

            string text = CompatibilityReport.BuildMarkdown(entry, logFileName: "").Replace("\r\n", "\n");

            StringAssert.Contains(text, "\n### Current status\n\nBoots");
            StringAssert.Contains(text, "<!-- Attach the log file here -->");
            Assert.IsFalse(text.Contains("Unknown"), "missing session fields do not corrupt the markdown");
        }

        [TestMethod]
        public void Logs_Resolve_OnlyTheResultsOwnLog_NeverTheCurrentOne()
        {
            string preserved = Path.Combine(_tempRoot, "result-a.log");
            File.WriteAllText(preserved, "session log A");

            // Manual / old result: no reference - explicitly no log.
            Assert.IsNull(TestResultLogs.Resolve(new GameFeedbackEntry()));
            Assert.IsNull(TestResultLogs.Resolve(new GameFeedbackEntry { LogReference = null }));

            // A reference whose file is gone is "not preserved", never a
            // substitute.
            Assert.IsNull(TestResultLogs.Resolve(new GameFeedbackEntry
            {
                LogReference = Path.Combine(_tempRoot, "gone.log"),
            }));

            // The result's own snapshot resolves.
            Assert.AreEqual(preserved, TestResultLogs.Resolve(new GameFeedbackEntry { LogReference = preserved }));
        }

        [TestMethod]
        public void LogSnapshot_PreservesSessionLog_ImmuneToLaterOverwrites()
        {
            // The actual bug: shadPS4 reuses one log file, so a later run
            // overwrites it. The snapshot must keep the ORIGINAL content.
            string sourceLog = Path.Combine(_tempRoot, "shad_log.txt");
            File.WriteAllText(sourceLog, "run A: crash in liverpool_to_vk.cpp");
            string snapshotsDir = Path.Combine(_tempRoot, "Logs");

            string snapshot = TestResultLogSnapshot.Preserve("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", sourceLog, snapshotsDir);

            Assert.IsNotNull(snapshot);
            Assert.IsTrue(File.Exists(snapshot));
            // shadPS4 writes the next session's log over the same file...
            File.WriteAllText(sourceLog, "run B: completely different session");
            // ...and the preserved snapshot still holds run A's content.
            Assert.AreEqual("run A: crash in liverpool_to_vk.cpp", File.ReadAllText(snapshot));
        }

        [TestMethod]
        public void LogSnapshot_MissingSource_ReturnsNull_ResultCanStillSave()
        {
            Assert.IsNull(TestResultLogSnapshot.Preserve("id", Path.Combine(_tempRoot, "missing.log")));
            Assert.IsNull(TestResultLogSnapshot.Preserve("", Path.Combine(_tempRoot, "missing.log")));
        }
    }
}
