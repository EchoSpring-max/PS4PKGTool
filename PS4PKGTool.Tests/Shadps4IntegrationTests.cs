using PS4PKGTool.Utilities;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Tests;

[TestClass]
public class Shadps4IntegrationTests
{
    private string _tempRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_shadps4_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    // ── config parsing ──

    [TestMethod]
    public void JsonConfig_ParsesInstallDirsAddonAndHome()
    {
        string userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(userDir);
        string lib1 = Path.Combine(_tempRoot, "lib1");
        string lib2 = Path.Combine(_tempRoot, "lib2");
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["general"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib1, ["enabled"] = true },
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib2, ["enabled"] = false }),
                ["addon_install_dir"] = "C:\\addons",
                ["home_dir"] = "C:\\home",
            },
        };
        File.WriteAllText(Path.Combine(userDir, "config.json"), root.ToString());

        var reader = new Shadps4JsonConfigReader();
        var config = reader.Read(Path.Combine(userDir, "config.json"));

        Assert.IsNotNull(config);
        Assert.AreEqual(2, config!.InstallDirs.Count);
        Assert.IsTrue(config.InstallDirs[0].Enabled);
        Assert.IsFalse(config.InstallDirs[1].Enabled);
        Assert.AreEqual("C:\\addons", config.AddonInstallDir);
        Assert.AreEqual("C:\\home", config.HomeDir);
    }

    [TestMethod]
    public void TomlConfig_ParsesLegacyGuiSection()
    {
        string userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(userDir);
        string lib = Path.Combine(_tempRoot, "lib");
        string toml = $"[GUI]\ninstallDirs = [\"{lib.Replace("\\", "\\\\")}\"]\ninstallDirsEnabled = [true]\naddonInstallDir = \"C:\\\\addcont\"\n";
        File.WriteAllText(Path.Combine(userDir, "config.toml"), toml);

        var reader = new Shadps4TomlConfigReader();
        var config = reader.Read(Path.Combine(userDir, "config.toml"));

        Assert.IsNotNull(config);
        Assert.AreEqual(1, config!.InstallDirs.Count);
        Assert.IsTrue(config.InstallDirs[0].Enabled);
        Assert.AreEqual(lib, config.InstallDirs[0].Path);
        Assert.AreEqual("C:\\addcont", config.AddonInstallDir);
    }

    [TestMethod]
    public void MalformedConfig_ReturnsNull()
    {
        string userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(userDir);
        string json = Path.Combine(userDir, "config.json");
        File.WriteAllText(json, "{ this is not json !!!");
        string toml = Path.Combine(userDir, "config.toml");
        File.WriteAllText(toml, "[GUI\ninstallDirs = [broken");

        Assert.IsNull(new Shadps4JsonConfigReader().Read(json));
        Assert.IsNull(new Shadps4TomlConfigReader().Read(toml));
    }

    // ── environment detection ──

    private string WriteValidConfig(string userDir)
    {
        Directory.CreateDirectory(userDir);
        string json = Path.Combine(userDir, "config.json");
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["general"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = "C:\\Games\\ps4", ["enabled"] = true }),
            },
        };
        File.WriteAllText(json, root.ToString());
        return json;
    }

    [TestMethod]
    public void Detect_PortableLayout()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        WriteValidConfig(Path.Combine(exeDir, "user"));

        var env = Shadps4Detector.Detect(exe, null, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4UserDirectoryMode.Portable, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4DetectionConfidence.High, env.DetectionConfidence);
        Assert.AreEqual(1, env.InstallDirectories.Count);
        Assert.AreEqual("C:\\Games\\ps4", env.InstallDirectories[0]);
        Assert.IsTrue(env.IsUsable);
    }

    [TestMethod]
    public void Detect_AppDataLayout_WithoutExe()
    {
        string appData = Path.Combine(_tempRoot, "appdata");
        string userDir = Path.Combine(appData, "shadPS4");
        WriteValidConfig(userDir);

        var env = Shadps4Detector.Detect(null, null, appData);

        Assert.AreEqual(Shadps4UserDirectoryMode.AppData, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4DetectionConfidence.Low, env.DetectionConfidence);
        Assert.IsFalse(env.IsUsable); // no core exe configured
        Assert.IsNotNull(env.UserDirectory);
    }

    [TestMethod]
    public void Detect_AmbiguousWhenBothConfigsExist()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        WriteValidConfig(Path.Combine(exeDir, "user"));

        string appData = Path.Combine(_tempRoot, "appdata");
        WriteValidConfig(Path.Combine(appData, "shadPS4"));

        var env = Shadps4Detector.Detect(exe, null, appData);

        Assert.AreEqual(Shadps4UserDirectoryMode.Ambiguous, env.UserDirectoryMode);
        Assert.AreEqual(2, env.CandidateConfigPaths.Count);
        Assert.IsFalse(env.IsUsable);
    }

    [TestMethod]
    public void Detect_StaleUserFolderWithoutConfig_IsCustom()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        // user folder exists but has NO config (the emulator auto-creates it)
        Directory.CreateDirectory(Path.Combine(exeDir, "user"));

        var env = Shadps4Detector.Detect(exe, null, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4UserDirectoryMode.Custom, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4DetectionConfidence.Low, env.DetectionConfidence);
    }

    [TestMethod]
    public void Detect_NothingConfigured_IsUnknown()
    {
        var env = Shadps4Detector.Detect(null, null, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4UserDirectoryMode.Unknown, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4DetectionConfidence.None, env.DetectionConfidence);
    }

    // ── launcher ──

    [TestMethod]
    public void LaunchArguments_UseSafeArgumentForms()
    {
        CollectionAssert.AreEqual(new[] { "CUSA12345" }, Shadps4Launcher.BuildTitleArguments(" CUSA12345 "));
        CollectionAssert.AreEqual(new[] { "-g", @"D:\Games\CUSA12345\eboot.bin" },
            Shadps4Launcher.BuildExecutableArguments(@"D:\Games\CUSA12345\eboot.bin"));
    }

    [TestMethod]
    public void LaunchInstalledTitle_RejectsInvalidIdAndMissingExe()
    {
        var env = new Shadps4Environment { CoreExePath = Path.Combine(_tempRoot, "missing.exe") };
        var launcher = new Shadps4Launcher();

        var invalid = launcher.LaunchInstalledTitle(env, "not-a-cusa");
        Assert.AreEqual(Shadps4LaunchStatus.InvalidTitleId, invalid.Status);

        var missing = launcher.LaunchInstalledTitle(env, "CUSA12345");
        Assert.AreEqual(Shadps4LaunchStatus.ExecutableMissing, missing.Status);
    }

    [TestMethod]
    public void FindInstalledEboot_SearchesAllLibraries()
    {
        string lib1 = Path.Combine(_tempRoot, "lib1");
        string lib2 = Path.Combine(_tempRoot, "lib2");
        string gameDir = Path.Combine(lib2, "CUSA12345");
        Directory.CreateDirectory(gameDir);
        string eboot = Path.Combine(gameDir, "eboot.bin");
        File.WriteAllText(eboot, "fake");

        var env = new Shadps4Environment { InstallDirectories = new List<string> { lib1, lib2 } };

        Assert.AreEqual(eboot, Shadps4Launcher.FindInstalledEboot(env, "cusa12345")); // case-insensitive
    }

    // ── filters ──

    [TestMethod]
    public void Filter_CompatAlone()
    {
        Assert.AreEqual("[ShadPS4] = 'Playable'", PkgFilter.BuildExpression(null, "Playable", null));
        Assert.AreEqual("([ShadPS4] IS NULL OR [ShadPS4] = '')", PkgFilter.BuildExpression(null, "Unknown", null));
    }

    [TestMethod]
    public void Filter_TypeAndCompatCompose()
    {
        string expr = PkgFilter.BuildExpression("Game", "Playable", null);
        Assert.IsTrue(expr.Contains("[Category] LIKE '%Game%'"), expr);
        Assert.IsTrue(expr.Contains("[ShadPS4] = 'Playable'"), expr);
        Assert.IsTrue(expr.Contains(" AND "), expr);
    }

    [TestMethod]
    public void Filter_TypeCompatAndSearchCompose()
    {
        string expr = PkgFilter.BuildExpression("Game", "Boots", "blood");
        Assert.IsTrue(expr.Contains(" AND "), expr);
        Assert.IsTrue(expr.Contains("'%blood%'"), expr);
        Assert.AreEqual(3, expr.Split(" AND ").Length);
        // the search part is parenthesized so its ORs do not leak
        Assert.IsTrue(expr.Contains("AND ("), expr);
        Assert.IsTrue(expr.EndsWith(")"), expr);
    }

    [TestMethod]
    public void Filter_EscapesQuotes()
    {
        string expr = PkgFilter.BuildExpression(null, null, "it's");
        Assert.IsTrue(expr.Contains("it''s"), expr);
    }

    [TestMethod]
    public void StatusRank_UsesSemanticOrder()
    {
        Assert.IsTrue(Shadps4Compat.StatusRank("Playable") > Shadps4Compat.StatusRank("In-Game"));
        Assert.IsTrue(Shadps4Compat.StatusRank("In-Game") > Shadps4Compat.StatusRank("Menus"));
        Assert.IsTrue(Shadps4Compat.StatusRank("Menus") > Shadps4Compat.StatusRank("Boots"));
        Assert.IsTrue(Shadps4Compat.StatusRank("Boots") > Shadps4Compat.StatusRank("Nothing"));
        Assert.IsTrue(Shadps4Compat.StatusRank("Nothing") > Shadps4Compat.StatusRank(""));
    }

    // ── install service ──

    // Real orbis extraction produces the PKG tree: Image0/ (game files) + Sc0/
    // (system metadata that belongs under sce_sys in the final dump layout).
    private static bool FakeExtract(string pkg, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(dest, "Image0", "sce_sys"));
        File.WriteAllText(Path.Combine(dest, "Image0", "sce_sys", "param.sfo"), "image0 param");
        File.WriteAllText(Path.Combine(dest, "Image0", "eboot.bin"), "fake eboot");
        Directory.CreateDirectory(Path.Combine(dest, "Image0", "data"));
        Directory.CreateDirectory(Path.Combine(dest, "Sc0"));
        File.WriteAllText(Path.Combine(dest, "Sc0", "param.sfo"), "sc0 param"); // conflict -> Image0 wins
        File.WriteAllText(Path.Combine(dest, "Sc0", "icon0.png"), "icon");
        File.WriteAllText(Path.Combine(dest, "Sc0", "pic0.png"), "pic");
        return true;
    }

    private static bool FailingExtract(string pkg, string dest, CancellationToken ct)
        => false;

    private string MakeFakePkg(string dir, string name = "game.pkg")
    {
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[1024 * 1024]); // 1 MB
        return path;
    }

    private Shadps4InstallService MakeService(Func<string, string, CancellationToken, bool> extractor)
        => new()
        {
            ExtractOverride = extractor,
            FreeSpaceOverride = _ => 100L * 1024 * 1024 * 1024,
        };

    [TestMethod]
    public void Install_StagingSuccess_ProducesDumpLayoutCusaFolder()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string pkg = MakeFakePkg(_tempRoot);

        var result = MakeService(FakeExtract).Install(pkg, "CUSA12345", lib, false);

        Assert.AreEqual(Shadps4InstallStatus.Success, result.Status);
        string game = Path.Combine(lib, "CUSA12345");
        Assert.IsTrue(Directory.Exists(game));
        // shadPS4 expects a dump-style folder: Image0 content at the root,
        // Sc0 metadata under sce_sys.
        Assert.IsTrue(File.Exists(Path.Combine(game, "eboot.bin")));
        Assert.IsTrue(Directory.Exists(Path.Combine(game, "data")));
        Assert.IsTrue(Directory.Exists(Path.Combine(game, "sce_sys")));
        Assert.AreEqual("image0 param", File.ReadAllText(Path.Combine(game, "sce_sys", "param.sfo")), "Image0 copy must win conflicts");
        Assert.IsTrue(File.Exists(Path.Combine(game, "sce_sys", "icon0.png")), "Sc0 files land under sce_sys");
        Assert.IsTrue(File.Exists(Path.Combine(game, "sce_sys", "pic0.png")), "Sc0 files land under sce_sys");
        Assert.IsFalse(File.Exists(Path.Combine(game, "param.sfo")), "Sc0 content must NOT sit at the root");
        Assert.IsFalse(Directory.Exists(Path.Combine(game, "Image0")), "Image0 tree must be flattened away");
        Assert.IsFalse(Directory.Exists(Path.Combine(game, "Sc0")), "Sc0 tree must be flattened away");
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, ".ps4pkgtool-CUSA12345.tmp")), "staging folder must be gone");
    }

    [TestMethod]
    public void Flatten_Image0WinsOnSc0Conflict()
    {
        string folder = Path.Combine(_tempRoot, "extracted");
        Directory.CreateDirectory(Path.Combine(folder, "Image0", "sce_sys"));
        File.WriteAllText(Path.Combine(folder, "Image0", "sce_sys", "param.sfo"), "image0");
        Directory.CreateDirectory(Path.Combine(folder, "Sc0"));
        File.WriteAllText(Path.Combine(folder, "Sc0", "param.sfo"), "sc0");

        Shadps4InstallService.FlattenToDumpLayout(folder);

        Assert.AreEqual("image0", File.ReadAllText(Path.Combine(folder, "sce_sys", "param.sfo")), "Image0 copy must win conflicts");
        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "Image0")));
        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "Sc0")));
    }

    [TestMethod]
    public void Flatten_Sc0SceSysSubtree_MergesContentsDirectly()
    {
        string folder = Path.Combine(_tempRoot, "extracted");
        Directory.CreateDirectory(Path.Combine(folder, "Image0", "sce_sys"));
        File.WriteAllText(Path.Combine(folder, "Image0", "eboot.bin"), "eboot");
        // Some PKGs keep Sc0 metadata inside its own sce_sys subtree.
        Directory.CreateDirectory(Path.Combine(folder, "Sc0", "sce_sys"));
        File.WriteAllText(Path.Combine(folder, "Sc0", "sce_sys", "pic0.png"), "pic");

        Shadps4InstallService.FlattenToDumpLayout(folder);

        Assert.IsTrue(File.Exists(Path.Combine(folder, "sce_sys", "pic0.png")), "Sc0\\sce_sys content merges directly into sce_sys");
        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "sce_sys", "sce_sys")), "no nested sce_sys\\sce_sys");
        Assert.IsFalse(Directory.Exists(Path.Combine(folder, "Sc0")));
    }

    [TestMethod]
    public void Install_ExtractionFailure_CleansStaging()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string pkg = MakeFakePkg(_tempRoot);

        var result = MakeService(FailingExtract).Install(pkg, "CUSA12345", lib, false);

        Assert.AreEqual(Shadps4InstallStatus.ExtractionFailed, result.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, ".ps4pkgtool-CUSA12345.tmp")));
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, "CUSA12345")));
    }

    [TestMethod]
    public void Install_ExistingInstall_IsProtected()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string final = Path.Combine(lib, "CUSA12345");
        Directory.CreateDirectory(final);
        File.WriteAllText(Path.Combine(final, "eboot.bin"), "old");
        string pkg = MakeFakePkg(_tempRoot);

        var result = MakeService(FakeExtract).Install(pkg, "CUSA12345", lib, replaceExisting: false);

        Assert.AreEqual(Shadps4InstallStatus.ExistingInstall, result.Status);
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(final, "eboot.bin")), "existing install must be untouched");
    }

    [TestMethod]
    public void Install_ReplaceExisting_OnlyAfterApproval()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string final = Path.Combine(lib, "CUSA12345");
        Directory.CreateDirectory(final);
        File.WriteAllText(Path.Combine(final, "eboot.bin"), "old");
        string pkg = MakeFakePkg(_tempRoot);

        var result = MakeService(FakeExtract).Install(pkg, "CUSA12345", lib, replaceExisting: true);

        Assert.AreEqual(Shadps4InstallStatus.Success, result.Status);
        Assert.AreEqual("fake eboot", File.ReadAllText(Path.Combine(final, "eboot.bin")));
    }

    [TestMethod]
    public void Install_InsufficientSpace_RejectsBeforeWriting()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string pkg = MakeFakePkg(_tempRoot);
        var svc = MakeService(FakeExtract);
        svc.FreeSpaceOverride = _ => 1L; // 1 byte free

        var result = svc.Install(pkg, "CUSA12345", lib, false);

        Assert.AreEqual(Shadps4InstallStatus.InsufficientSpace, result.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, ".ps4pkgtool-CUSA12345.tmp")));
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, "CUSA12345")));
    }

    [TestMethod]
    public void Install_Cancellation_CleansStaging()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string pkg = MakeFakePkg(_tempRoot);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = MakeService(FakeExtract).Install(pkg, "CUSA12345", lib, false, ct: cts.Token);

        Assert.AreEqual(Shadps4InstallStatus.Cancelled, result.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, ".ps4pkgtool-CUSA12345.tmp")));
    }

    [TestMethod]
    public void Install_ValidationFails_WhenNoEboot()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        string pkg = MakeFakePkg(_tempRoot);
        var svc = MakeService((p, d, ct) =>
        {
            Directory.CreateDirectory(Path.Combine(d, "Image0", "sce_sys"));
            return true; // no eboot.bin written
        });

        var result = svc.Install(pkg, "CUSA12345", lib, false);

        Assert.AreEqual(Shadps4InstallStatus.ValidationFailed, result.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(lib, "CUSA12345")));
    }

    // ── param.sfo ──

    [TestMethod]
    public void ParamSfo_ReadsAppVersion()
    {
        // Minimal PSF layout:
        //   header (20 bytes)
        //   key table  @32: entry (16 bytes) + key string "APP_VER\0" (8 bytes)
        //   data table @56: value "1.09"
        var keyBytes = System.Text.Encoding.ASCII.GetBytes("APP_VER\0");
        var dataBytes = System.Text.Encoding.ASCII.GetBytes("1.09");
        var buf = new byte[56 + dataBytes.Length];
        buf[0] = 0x00; buf[1] = 0x50; buf[2] = 0x53; buf[3] = 0x46;          // magic
        BitConverter.GetBytes((uint)0x0101).CopyTo(buf, 4);                   // version
        BitConverter.GetBytes((uint)0x20).CopyTo(buf, 8);                     // key table offset = 32
        BitConverter.GetBytes((uint)56).CopyTo(buf, 12);                      // data table offset = 56
        BitConverter.GetBytes((uint)1).CopyTo(buf, 16);                       // entries = 1
        BitConverter.GetBytes((ushort)16).CopyTo(buf, 32);                    // entry: key offset (after the 16-byte entry)
        BitConverter.GetBytes((ushort)0x0204).CopyTo(buf, 34);                // entry: string type
        BitConverter.GetBytes((uint)4).CopyTo(buf, 36);                       // entry: length
        BitConverter.GetBytes((uint)4).CopyTo(buf, 40);                       // entry: max length
        BitConverter.GetBytes((uint)0).CopyTo(buf, 44);                       // entry: data offset (relative to data table)
        keyBytes.CopyTo(buf, 48);                                             // key table string
        dataBytes.CopyTo(buf, 56);                                            // data table value

        string path = Path.Combine(_tempRoot, "param.sfo");
        File.WriteAllBytes(path, buf);

        Assert.AreEqual("1.09", ParamSfoReader.ReadAppVersion(path));
    }

    [TestMethod]
    public void ParamSfo_InvalidFile_ReturnsNull()
    {
        string path = Path.Combine(_tempRoot, "param.sfo");
        File.WriteAllText(path, "not a psf");
        Assert.IsNull(ParamSfoReader.ReadAppVersion(path));
        Assert.IsNull(ParamSfoReader.ReadAppVersion(Path.Combine(_tempRoot, "missing.sfo")));
    }
}
