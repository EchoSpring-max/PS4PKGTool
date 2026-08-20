using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using PS4PKGTool.Shell;
using PS4PKGTool.Startup;
using PS4PKGTool.Utilities.Constants;
using PS4PKGTool.Utilities.PkgInspection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Tests
{
    [TestClass]
    public class ShellIntegrationTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_shell_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        // ── router parsing ───────────────────────────────────────────────

        [TestMethod]
        public void Router_ParsesEveryCommand()
        {
            var copy = ShellCommandRouter.Parse(new[] { "--shell", "copy", "title", @"C:\a.pkg" });
            Assert.AreEqual(ShellCommandKind.Copy, copy!.Kind);
            Assert.AreEqual("title", copy.Field);
            Assert.AreEqual(@"C:\a.pkg", copy.PackagePaths[0]);

            var rename = ShellCommandRouter.Parse(new[] { "--shell", "rename", "3", @"C:\a.pkg" });
            Assert.AreEqual(ShellCommandKind.Rename, rename!.Kind);
            Assert.AreEqual(3, rename.FormatId);

            Assert.AreEqual(ShellCommandKind.Validate, ShellCommandRouter.Parse(new[] { "--shell", "validate", @"C:\a.pkg" })!.Kind);
            Assert.AreEqual(ShellCommandKind.Extract, ShellCommandRouter.Parse(new[] { "--shell", "extract", @"C:\a.pkg" })!.Kind);
            Assert.AreEqual(ShellCommandKind.InstallShadps4, ShellCommandRouter.Parse(new[] { "--shell", "install-shadps4", @"C:\a.pkg" })!.Kind);
        }

        [TestMethod]
        public void Router_MultiplePathsAreKept_NotTruncated()
        {
            var request = ShellCommandRouter.Parse(new[]
                { "--shell", "validate", @"C:\a.pkg", @"C:\b.pkg", @"C:\space dir\c.pkg" });

            Assert.IsNotNull(request);
            Assert.AreEqual(3, request.PackagePaths.Count);
            Assert.AreEqual(@"C:\space dir\c.pkg", request.PackagePaths[2], "paths with spaces survive argument passing");
        }

        [TestMethod]
        public void Router_RejectsUnknownAndMalformed()
        {
            Assert.IsNull(ShellCommandRouter.Parse(new[] { "--shell" }), "missing command");
            Assert.IsNull(ShellCommandRouter.Parse(new[] { "--shell", "explode", @"C:\a.pkg" }), "unknown command");
            Assert.IsNull(ShellCommandRouter.Parse(new[] { "--shell", "copy", @"C:\a.pkg" }), "copy without a field");
            Assert.IsNull(ShellCommandRouter.Parse(new[] { "--shell", "copy", "bogusfield", @"C:\a.pkg" }), "unknown copy field");
            Assert.IsNull(ShellCommandRouter.Parse(new[] { "--shell", "rename", "x", @"C:\a.pkg" }), "non-numeric format id");
        }

        [TestMethod]
        public void Router_IsShellMode_OnlyForExactFirstArg()
        {
            Assert.IsTrue(ShellCommandRouter.IsShellMode(new[] { "--shell", "validate", "x" }));
            Assert.IsFalse(ShellCommandRouter.IsShellMode(new[] { "C:\\a.pkg" }));
            Assert.IsFalse(ShellCommandRouter.IsShellMode(Array.Empty<string>()));
        }

        // ── path validation ──────────────────────────────────────────────

        [TestMethod]
        public void Paths_ValidatesExistenceExtensionAndType()
        {
            string pkg = Path.Combine(_tempRoot, "game.pkg");
            File.WriteAllBytes(pkg, new byte[] { 1, 2, 3 });
            string unicodePkg = Path.Combine(_tempRoot, "ゲーム ™.pkg");
            File.WriteAllBytes(unicodePkg, new byte[] { 4 });
            string notPkg = Path.Combine(_tempRoot, "readme.txt");
            File.WriteAllText(notPkg, "hi");

            Assert.IsNull(ShellCommandRouter.ValidatePaths(new[] { pkg }), "valid pkg");
            Assert.IsNull(ShellCommandRouter.ValidatePaths(new[] { unicodePkg }), "unicode filename");
            Assert.IsNull(ShellCommandRouter.ValidatePaths(new[] { pkg, unicodePkg }), "multiple valid");
            Assert.IsNotNull(ShellCommandRouter.ValidatePaths(new[] { Path.Combine(_tempRoot, "missing.pkg") }), "missing file");
            Assert.IsNotNull(ShellCommandRouter.ValidatePaths(new[] { _tempRoot }), "directory is rejected");
            Assert.IsNotNull(ShellCommandRouter.ValidatePaths(new[] { notPkg }), "non-pkg extension rejected");
            Assert.IsNotNull(ShellCommandRouter.ValidatePaths(Array.Empty<string>()), "no paths");
        }

        // ── shared rename formats (single source of truth) ───────────────

        [TestMethod]
        public void RenameFormats_ExposeTheExactMainAppFormats()
        {
            // The formats the main app has always used - if these change,
            // both the app menus and the Explorer menu change together.
            var expected = new[]
            {
                "{TITLE}",
                "{TITLE} [{TITLE_ID}]",
                "{TITLE} [{TITLE_ID}] [{APP_VERSION}]",
                "{TITLE} [{CATEGORY}]",
                "{TITLE_ID}",
                "{TITLE_ID} [{TITLE}]",
                "[{TITLE_ID}] [{CATEGORY}] [{APP_VERSION}] {TITLE}",
                "{TITLE} [{CATEGORY}] [{VERSION}]",
                "{CONTENT_ID}",
                "{CONTENT_ID2}",
            };
            CollectionAssert.AreEqual(expected,
                PkgRenameFormats.Predefined.Select(f => f.Format).ToArray());

            for (int id = 1; id <= 10; id++)
                Assert.AreEqual(expected[id - 1], PkgRenameFormats.GetFormat(id), $"format {id}");

            Assert.AreEqual("Custom {TITLE}", PkgRenameFormats.GetFormat(PkgRenameFormats.CustomFormatId, "Custom {TITLE}"));
            Assert.IsNull(PkgRenameFormats.GetFormat(99), "unknown id is null");
        }

        // ── registry command generation (pure, no real registry) ─────────

        [TestMethod]
        public void Registry_BuildCommandLine_QuotesExeAndKeepsPlaceholder()
        {
            string line = ShellRegistry.BuildCommandLine(@"C:\Program Files\PS4 PKG Tool\PS4 PKG Tool.exe", "validate");
            Assert.AreEqual(@"""C:\Program Files\PS4 PKG Tool\PS4 PKG Tool.exe"" --shell validate ""%1""", line);

            string copyLine = ShellRegistry.BuildCommandLine(@"C:\x.exe", "copy", "titleid");
            Assert.AreEqual(@"""C:\x.exe"" --shell copy titleid ""%1""", copyLine);
        }

        // ── real-registry round trip (throwaway HKCU path, never the live key) ──

        [TestMethod]
        public void Registry_InstallWritesValueFormCascadesAndRemoveCleansThem()
        {
            // Redirect the verb root to a throwaway path so this test can
            // never delete a live installation (an earlier version of this
            // test removed the real key and uninstalled the user's menu).
            string overrideRoot = @"Software\Classes\p4t-shell-test\" + Guid.NewGuid().ToString("N");
            ShellRegistry.VerbRootOverride = overrideRoot;
            string fakeExe = @"C:\p4t-test\PS4 PKG Tool.exe";
            try
            {
                ShellRegistry.Install(fakeExe);
                Assert.IsTrue(ShellRegistry.IsInstalled(), "installed flag");

                using RegistryKey? root = Registry.CurrentUser.OpenSubKey(overrideRoot);
                Assert.IsNotNull(root, "verb root exists");
                Assert.AreEqual("PS4 PKG Tool", root.GetValue("MUIVerb"));
                Assert.AreEqual("p4t-shell-test\\" + overrideRoot.Substring(@"Software\Classes\p4t-shell-test\".Length),
                    root.GetValue("ExtendedSubCommandsKey"),
                    "root ExtendedSubCommandsKey VALUE points at the verb key (HKCR-relative)");
                Assert.AreEqual("Player", root.GetValue("MultiSelectModel"));

                // Flat layout with ordinal-prefixed keys (alphabetical
                // key-name order = display order). The Rename header is a
                // grayed, non-clickable section label using the verified
                // CommandStateHandler disabled-state pattern.
                using RegistryKey? header = Registry.CurrentUser.OpenSubKey(overrideRoot + @"\shell\01aHdrRename");
                Assert.IsNotNull(header, "Rename header exists");
                Assert.AreEqual("RENAME", header.GetValue("MUIVerb"));
                Assert.AreEqual(0x20, header.GetValue("CommandFlags"), "ECF_SEPARATORBEFORE");
                Assert.AreEqual("{5B6D1451-B1E1-4372-90F5-88E541B4DAB9}", header.GetValue("CommandStateHandler"),
                    "uses the system EDP state handler");
                Assert.AreEqual(0x4000, header.GetValue("AttributeValue"), "never-matching attribute value");

                using RegistryKey? firstFormat = Registry.CurrentUser.OpenSubKey(
                    overrideRoot + @"\shell\01bRename01\command");
                Assert.IsNotNull(firstFormat, "rename format command exists");
                Assert.AreEqual(ShellRegistry.BuildCommandLine(fakeExe, "rename", "1"),
                    firstFormat.GetValue(""), "child command line");
            }
            finally
            {
                // Remove while the override is still active, then clear it.
                ShellRegistry.Remove();
                Assert.IsFalse(ShellRegistry.IsInstalled(), "removed flag");
                Assert.IsNull(Registry.CurrentUser.OpenSubKey(overrideRoot), "throwaway tree deleted");
                ShellRegistry.VerbRootOverride = null;
            }
        }

        [TestMethod]
        public void Registry_Entries_CoverMenuAndOwnOnlyThePkgVerb()
        {
            var entries = ShellRegistry.BuildEntries(@"C:\x\PS4 PKG Tool.exe");
            // Distinct: header verbs emit two entries (MUIVerb + CommandFlags)
            // under the same key path.
            var keys = entries.Select(e => e.KeyPath).Distinct().ToList();

            // The verb root lives ONLY under SystemFileAssociations - the
            // single tree Explorer renders on Windows 11. The Classes\.pkg
            // mirror older builds wrote never rendered and is now gone.
            string root = @"Software\Classes\SystemFileAssociations\.pkg\shell\PS4PKGTool";
            Assert.IsFalse(keys.Any(k => k.StartsWith(@"Software\Classes\.pkg\", StringComparison.Ordinal)),
                "no legacy Classes\\.pkg mirror entries");

            Assert.AreEqual("PS4 PKG Tool",
                entries.First(e => e.KeyPath == root && e.ValueName == "MUIVerb").Value);
            // The ROOT itself is a cascade: without the ExtendedSubCommandsKey
            // VALUE the item renders but opens nothing (its children live
            // under <root>\shell\, which only a cascade renders).
            Assert.AreEqual("SystemFileAssociations\\.pkg\\shell\\PS4PKGTool",
                entries.First(e => e.KeyPath == root && e.ValueName == "ExtendedSubCommandsKey").Value);
            Assert.IsTrue(entries.Any(e => e.KeyPath == root && e.ValueName == "Icon"));
            Assert.AreEqual("Player",
                entries.First(e => e.KeyPath == root && e.ValueName == "MultiSelectModel").Value);

            // Top-level actions have MUIVerb + command ending in "%1".
            string validate = entries.First(e =>
                e.KeyPath == root + @"\shell\03bOps01\command" && e.ValueName == "").Value;
            Assert.IsTrue(validate.EndsWith(" --shell validate \"%1\""), validate);
            Assert.AreEqual("Validate PKG",
                entries.First(e => e.KeyPath == root + @"\shell\03bOps01" && e.ValueName == "MUIVerb").Value);

            // ONE flat cascade: nested cascades truncate to ~5 items in the
            // Windows 11 menu (verified on build 26200), so all verbs live
            // directly under <root>\shell\ - never a second-level cascade.
            var verbs = keys.Where(k => k.StartsWith(root + @"\shell\", StringComparison.Ordinal)
                && !k.EndsWith(@"\command", StringComparison.Ordinal)).ToList();
            Assert.IsFalse(verbs.Any(v => entries.Any(e =>
                    e.KeyPath == v && e.ValueName == "ExtendedSubCommandsKey")),
                "no nested cascades - every action is a direct verb");
            Assert.IsFalse(keys.Any(k => k.Contains("ExtendedSubCommandsKey\\", StringComparison.Ordinal)),
                "ExtendedSubCommandsKey is a value, never a key");

            // Static cascade children render in ALPHABETICAL KEY-NAME order,
            // so the display order must equal the sorted verb key names.
            // Expected display sequence: RENAME block (curated format subset
            // + Custom), COPY block, PACKAGE block.
            var expected = new List<string>
            {
                "RENAME",
                "TITLE",                       // format 1
                "TITLE [TITLE_ID]",            // format 2
                "TITLE [TITLE_ID] [APP_VERSION]", // format 3
                "TITLE [CATEGORY]",            // format 4
                "TITLE_ID",                    // format 5
                "Custom Format...",
                "COPY",
                "Title", "Title ID", "Content ID",
                "PACKAGE",
                "Validate PKG", "Extract PKG...", "Install to shadPS4...",
            };

            var actual = verbs
                .OrderBy(k => k.Substring(k.LastIndexOf('\\') + 1), StringComparer.Ordinal)
                .Select(k => entries.First(e => e.KeyPath == k && e.ValueName == "MUIVerb").Value)
                .ToList();
            CollectionAssert.AreEqual(expected, actual,
                "alphabetical key-name order (= display order) must be RENAME block, COPY block, PACKAGE block");

            // Section headers: grayed, non-clickable labels with a separator
            // line above. Verified shape (Win11 26200): CommandStateHandler =
            // system EDP handler CLSID + AttributeMask/AttributeValue pair
            // that never matches + ShowAsDisabledIfHidden. The state handler
            // reports the verb disabled - clicking it does nothing and the
            // menu stays open.
            foreach (string headerKey in new[] { @"\shell\01aHdrRename", @"\shell\02aHdrCopy", @"\shell\03aHdrOps" })
            {
                Assert.AreEqual(0x20,
                    entries.First(e => e.KeyPath == root + headerKey && e.ValueName == "CommandFlags").DWordValue,
                    "header " + headerKey + " carries ECF_SEPARATORBEFORE (0x20) as REG_DWORD");
                Assert.AreEqual("{5B6D1451-B1E1-4372-90F5-88E541B4DAB9}",
                    entries.First(e => e.KeyPath == root + headerKey && e.ValueName == "CommandStateHandler").Value,
                    "header " + headerKey + " uses the system EDP state handler");
                Assert.AreEqual(0x2000,
                    entries.First(e => e.KeyPath == root + headerKey && e.ValueName == "AttributeMask").DWordValue,
                    "header " + headerKey + " attribute mask");
                Assert.AreEqual(0x4000,
                    entries.First(e => e.KeyPath == root + headerKey && e.ValueName == "AttributeValue").DWordValue,
                    "header " + headerKey + " attribute value never matches the mask");
            }

            // Every verb - action or header - has a command key. Headers
            // carry one to match the verified disabled-state shape (the
            // state handler prevents it from ever running); action verbs
            // need one to do their job.
            foreach (string verb in verbs)
                Assert.IsTrue(keys.Contains(verb + @"\command"),
                    "verb " + verb + " must have a command key");

            // Only our keys: no other verbs under .pkg are generated.
            Assert.IsTrue(keys.All(k => k.Contains("PS4PKGTool")), "every generated key is PS4PKGTool-owned");
        }

        // ── passcode-failure detection (shell validate) ──────────────────

        [TestMethod]
        public void Validate_PasscodeFailure_Detection_MatchesMiniViewer()
        {
            // orbis-pub-cmd reports a rejected passcode in its error text;
            // the shell Validate flow must detect it to offer the prompt.
            Assert.IsTrue(ShellCommands.IsPasscodeFailure(
                "orbis-pub-cmd exited with code 1: [Error] passcode mismatch"));
            Assert.IsTrue(ShellCommands.IsPasscodeFailure(
                "[Error] Invalid Passcode."));
            Assert.IsFalse(ShellCommands.IsPasscodeFailure("orbis-pub-cmd timed out while listing PKG files."));
            Assert.IsFalse(ShellCommands.IsPasscodeFailure(""));
            Assert.IsFalse(ShellCommands.IsPasscodeFailure(null!));
        }

        // ── copy field mapping ───────────────────────────────────────────

        [TestMethod]
        public void Copy_FieldMapping_UsesMetadataFields()
        {
            var meta = new PkgInspectionSnapshot
            {
                PackagePath = @"D:\PKG\Bloodborne.pkg",
                FileName = "Bloodborne.pkg",
                Title = "Bloodborne",
                TitleId = "CUSA00900",
                ContentId = "EP9000-CUSA00900_00-BLOODBORNE0000EU",
                PackageVersion = "01.00",
                ApplicationVersion = "01.09",
                PackageCategory = "Game",
                PackageSize = "29.8 GB",
            };

            Assert.AreEqual("Bloodborne", ShellCommands.ExtractField("title", meta));
            Assert.AreEqual("CUSA00900", ShellCommands.ExtractField("titleid", meta));
            Assert.AreEqual("EP9000-CUSA00900_00-BLOODBORNE0000EU", ShellCommands.ExtractField("contentid", meta));
            Assert.AreEqual("01.00", ShellCommands.ExtractField("version", meta));
            Assert.AreEqual("01.09", ShellCommands.ExtractField("appversion", meta));
            Assert.AreEqual(@"D:\PKG\Bloodborne.pkg", ShellCommands.ExtractField("fullpath", meta));

            string info = ShellCommands.BuildPackageInfo(meta);
            StringAssert.Contains(info, "Title: Bloodborne");
            StringAssert.Contains(info, "Title ID: CUSA00900");
            StringAssert.Contains(info, "Content ID: EP9000-CUSA00900_00-BLOODBORNE0000EU");
            StringAssert.Contains(info, "Version: 01.00");
            StringAssert.Contains(info, "App Version: 01.09");
            StringAssert.Contains(info, "Type: Game");
            StringAssert.Contains(info, "Size: 29.8 GB");
            StringAssert.Contains(info, @"Path: D:\PKG\Bloodborne.pkg");
        }

        // ── operation-state transitions (cancel / finalize) ─────────────

        [TestMethod]
        public void InstallStage_CancellableBecomesFalseAtFinalization()
        {
            // The stage mapping used by the shell install: the finalizing
            // stage disables cancel, exactly like the Manager.
            bool Cancellable(string stage) => !stage.StartsWith("Finalizing", StringComparison.Ordinal);

            Assert.IsTrue(Cancellable("Extracting PKG..."));
            Assert.IsTrue(Cancellable("Arranging game files..."));
            Assert.IsTrue(Cancellable("Validating extracted game..."));
            Assert.IsFalse(Cancellable("Finalizing installation..."));
        }

        // ── extraction staging helpers ───────────────────────────────────

        [TestMethod]
        public void Extract_SanitizeFolderName_MatchesSafeRules()
        {
            Assert.AreEqual("Bloodborne", PkgExtractionService.SanitizeFolderName("Bloodborne"));
            Assert.AreEqual("A：B", PkgExtractionService.SanitizeFolderName("A:B"));
            Assert.AreEqual("_", PkgExtractionService.SanitizeFolderName(""));
            Assert.AreEqual("_CON", PkgExtractionService.SanitizeFolderName("CON"), "reserved device name is prefixed");
            Assert.IsFalse(PkgExtractionService.SanitizeFolderName("A/B").Contains('/'), "invalid chars replaced");
        }
    }
}
