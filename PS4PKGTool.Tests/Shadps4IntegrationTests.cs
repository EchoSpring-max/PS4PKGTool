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

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

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

        var env = Shadps4EnvironmentResolver.Resolve(null, appData);

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

        var env = Shadps4EnvironmentResolver.Resolve(exe, appData);

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

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4UserDirectoryMode.Custom, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4DetectionConfidence.Low, env.DetectionConfidence);
    }

    [TestMethod]
    public void Detect_NothingConfigured_IsUnknown()
    {
        var env = Shadps4EnvironmentResolver.Resolve(null, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4UserDirectoryMode.Unknown, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4DetectionConfidence.None, env.DetectionConfidence);
    }

    [TestMethod]
    public void Detect_StalePortableFolder_IsIgnoredWithWarning()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        // Stale portable folder: exists but has NO config.
        Directory.CreateDirectory(Path.Combine(exeDir, "user"));

        string appData = Path.Combine(_tempRoot, "appdata");
        WriteValidConfig(Path.Combine(appData, "shadPS4"));

        var env = Shadps4EnvironmentResolver.Resolve(exe, appData);

        Assert.AreEqual(Shadps4UserDirectoryMode.AppData, env.UserDirectoryMode, "the valid AppData config must win");
        Assert.IsTrue(env.Warnings.Any(w => w.Contains("stale portable")), "stale folder must be reported");
    }

    [TestMethod]
    public void Detect_MissingExecutable_StillFindsAppDataWithWarning()
    {
        string appData = Path.Combine(_tempRoot, "appdata");
        WriteValidConfig(Path.Combine(appData, "shadPS4"));

        var env = Shadps4EnvironmentResolver.Resolve(Path.Combine(_tempRoot, "missing", "shadPS4.exe"), appData);

        Assert.AreEqual(Shadps4UserDirectoryMode.AppData, env.UserDirectoryMode);
        Assert.IsTrue(env.Warnings.Any(w => w.Contains("does not exist")), "missing exe must be reported");
        Assert.IsFalse(env.IsUsable);
    }

    [TestMethod]
    public void Detect_DetectsQtLauncherBesideCore()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        string launcher = Path.Combine(exeDir, Shadps4EnvironmentResolver.QtLauncherFileName);
        File.WriteAllText(launcher, "fake launcher");
        WriteValidConfig(Path.Combine(exeDir, "user"));

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(launcher, env.LauncherExePath, "verified launcher filename beside the core is detected");
    }

    [TestMethod]
    public void Detect_MultipleLibraries_AreAllPreserved()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        string userDir = Path.Combine(exeDir, "user");
        Directory.CreateDirectory(userDir);
        string lib1 = Path.Combine(_tempRoot, "PS4 Games");
        string lib2 = Path.Combine(_tempRoot, "PS4 SSD");
        Directory.CreateDirectory(lib1);
        Directory.CreateDirectory(lib2);
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["general"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib1, ["enabled"] = true },
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib2, ["enabled"] = true }),
            },
        };
        File.WriteAllText(Path.Combine(userDir, "config.json"), root.ToString());

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(2, env.InstallDirectories.Count);
        CollectionAssert.Contains(env.InstallDirectories.ToArray(), lib1);
        CollectionAssert.Contains(env.InstallDirectories.ToArray(), lib2);
    }

    [TestMethod]
    public void Detect_EmptyInstallDirs_ReportsWarning()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        string userDir = Path.Combine(exeDir, "user");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "config.json"), """{"general":{"install_dirs":[]}}""");

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(0, env.InstallDirectories.Count);
        Assert.IsTrue(env.Warnings.Any(w => w.Contains("no enabled game libraries")), "empty library list must be reported");
    }

    [TestMethod]
    public void Detect_MissingConfiguredDirectory_IsListedWithWarning()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        string userDir = Path.Combine(exeDir, "user");
        Directory.CreateDirectory(userDir);
        string missing = Path.Combine(_tempRoot, "unplugged drive");
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["general"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = missing, ["enabled"] = true }),
            },
        };
        File.WriteAllText(Path.Combine(userDir, "config.json"), root.ToString());

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(1, env.InstallDirectories.Count, "configured paths are reported even when missing");
        Assert.AreEqual(missing, env.InstallDirectories[0]);
        Assert.IsTrue(env.Warnings.Any(w => w.Contains("does not currently exist")), "missing dir must be reported");
    }

    [TestMethod]
    public void Detect_UnicodeAndSpacesPaths_ArePreserved()
    {
        string exeDir = Path.Combine(_tempRoot, "émulateur test");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        string userDir = Path.Combine(exeDir, "user");
        Directory.CreateDirectory(userDir);
        string lib = Path.Combine(_tempRoot, "My PS4 ゲーム");
        Directory.CreateDirectory(lib);
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["general"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib, ["enabled"] = true }),
            },
        };
        File.WriteAllText(Path.Combine(userDir, "config.json"), root.ToString());

        var env = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4UserDirectoryMode.Portable, env.UserDirectoryMode);
        Assert.AreEqual(lib, env.InstallDirectories[0]);
    }

    [TestMethod]
    public void Detect_ConfigReload_PicksUpChanges()
    {
        string exeDir = Path.Combine(_tempRoot, "emulator");
        Directory.CreateDirectory(exeDir);
        string exe = Path.Combine(exeDir, "shadPS4.exe");
        File.WriteAllText(exe, "fake");
        string userDir = Path.Combine(exeDir, "user");
        Directory.CreateDirectory(userDir);
        string json = Path.Combine(userDir, "config.json");
        string lib1 = Path.Combine(_tempRoot, "lib1");
        Directory.CreateDirectory(lib1);
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["general"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib1, ["enabled"] = true }),
            },
        };
        File.WriteAllText(json, root.ToString());

        var first = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));
        Assert.AreEqual(lib1, first.InstallDirectories[0]);

        // The resolver keeps no persistent cache - a config change is picked up
        // on the next resolve.
        string lib2 = Path.Combine(_tempRoot, "lib2");
        Directory.CreateDirectory(lib2);
        root["general"]["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
            new Newtonsoft.Json.Linq.JObject { ["path"] = lib2, ["enabled"] = true });
        File.WriteAllText(json, root.ToString());

        var second = Shadps4EnvironmentResolver.Resolve(exe, Path.Combine(_tempRoot, "appdata"));
        Assert.AreEqual(lib2, second.InstallDirectories[0], "a config change must be reflected on the next resolve");
    }

    [TestMethod]
    public void ValidateCoreExe_ChecksExistenceAndExtension()
    {
        string exe = Path.Combine(_tempRoot, "shadPS4.exe");
        File.WriteAllText(exe, "fake");

        var missing = Shadps4EnvironmentResolver.ValidateSelectedExecutablePath(Path.Combine(_tempRoot, "gone.exe"));
        Assert.IsFalse(missing.IsValid);

        var notExe = Shadps4EnvironmentResolver.ValidateSelectedExecutablePath(Path.Combine(_tempRoot, "shadPS4.bin"));
        Assert.IsFalse(notExe.IsValid);

        var ok = Shadps4EnvironmentResolver.ValidateSelectedExecutablePath(exe);
        Assert.IsTrue(ok.IsValid, "custom/nightly builds are accepted without metadata checks");
    }

    // ── distribution / executable model ──

    private string MakeExe(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, "fake");
        return path;
    }

    [TestMethod]
    public void Distribution_SelectingCore_DetectsBoth()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        string launcher = MakeExe(dir, Shadps4EnvironmentResolver.QtLauncherFileName);
        WriteValidConfig(Path.Combine(dir, "user"));

        var env = Shadps4EnvironmentResolver.Resolve(core, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(core, env.SelectedExecutablePath);
        Assert.AreEqual(core, env.CoreExePath);
        Assert.AreEqual(launcher, env.LauncherExePath);
        Assert.AreEqual(Shadps4DistributionType.CoreAndQtLauncher, env.DistributionType);
    }

    [TestMethod]
    public void Distribution_SelectingLauncher_DetectsBoth()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        string launcher = MakeExe(dir, Shadps4EnvironmentResolver.QtLauncherFileName);
        WriteValidConfig(Path.Combine(dir, "user"));

        var env = Shadps4EnvironmentResolver.Resolve(launcher, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(launcher, env.SelectedExecutablePath);
        Assert.AreEqual(launcher, env.LauncherExePath);
        Assert.AreEqual(core, env.CoreExePath);
        Assert.AreEqual(Shadps4DistributionType.CoreAndQtLauncher, env.DistributionType);
        Assert.IsTrue(env.IsUsable, "core detected beside the launcher enables launch features");
    }

    [TestMethod]
    public void Distribution_CoreOnly()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        WriteValidConfig(Path.Combine(dir, "user"));

        var env = Shadps4EnvironmentResolver.Resolve(core, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4DistributionType.CoreOnly, env.DistributionType);
        Assert.IsNull(env.LauncherExePath, "missing counterpart is not an error");
        Assert.IsTrue(env.IsUsable);
    }

    [TestMethod]
    public void Distribution_LauncherOnly()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string launcher = MakeExe(dir, Shadps4EnvironmentResolver.QtLauncherFileName);
        WriteValidConfig(Path.Combine(dir, "user"));

        var env = Shadps4EnvironmentResolver.Resolve(launcher, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Shadps4DistributionType.QtLauncherOnly, env.DistributionType);
        Assert.IsNull(env.CoreExePath, "missing core is not an error");
        Assert.IsTrue(env.IsValid, "config still resolves");
        Assert.IsFalse(env.IsUsable, "launch needs the core, which this distribution lacks");
    }

    [TestMethod]
    public void Distribution_CoreInParentDirectory_IsDetected()
    {
        // Verified real layout: a versioned launcher folder inside a root
        // that also holds the core.
        string root = Path.Combine(_tempRoot, "SHADPS4");
        string core = MakeExe(root, Shadps4EnvironmentResolver.CoreExeFileName);
        string launcherDir = Path.Combine(root, "Latest ShadPS4");
        string launcher = MakeExe(launcherDir, Shadps4EnvironmentResolver.QtLauncherFileName);
        string appData = Path.Combine(_tempRoot, "appdata");
        WriteValidConfig(Path.Combine(appData, "shadPS4"));

        var env = Shadps4EnvironmentResolver.Resolve(launcher, appData);

        Assert.AreEqual(core, env.CoreExePath, "the core in the parent folder is detected");
        Assert.AreEqual(launcher, env.LauncherExePath);
        Assert.AreEqual(Shadps4DistributionType.CoreAndQtLauncher, env.DistributionType);
        Assert.IsTrue(env.Warnings.Any(w => w.Contains("parent folder")), "parent-folder detection is reported");
        Assert.IsTrue(env.IsUsable, "launch features work with the parent-folder core");
    }

    // ── config edge cases (JSON DTO robustness) ──

    [TestMethod]
    public void JsonConfig_UnknownProperties_AreIgnored()
    {
        string userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(userDir);
        string lib = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(lib);
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["General"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = lib, ["enabled"] = true }),
                ["some_future_key"] = 123,
                ["nested"] = new Newtonsoft.Json.Linq.JObject { ["x"] = 1 },
            },
            ["TopLevelUnknown"] = new Newtonsoft.Json.Linq.JObject { ["a"] = "b" },
        };
        File.WriteAllText(Path.Combine(userDir, "config.json"), root.ToString());

        var config = new Shadps4JsonConfigReader().Read(Path.Combine(userDir, "config.json"));

        Assert.IsNotNull(config);
        Assert.AreEqual(1, config!.InstallDirs.Count);
        Assert.AreEqual(lib, config.InstallDirs[0].Path);
    }

    [TestMethod]
    public void JsonConfig_MissingGeneralAndInstallDirs_AreValidEmptyStates()
    {
        string userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "config.json"), "{}");

        var config = new Shadps4JsonConfigReader().Read(Path.Combine(userDir, "config.json"));
        Assert.IsNotNull(config, "missing General is not a parse failure");
        Assert.AreEqual(0, config!.InstallDirs.Count);
        Assert.IsNull(config.AddonInstallDir);
    }

    [TestMethod]
    public void JsonConfig_CapitalGeneralKey_ParsesLikeTheRealConfig()
    {
        // Mirrors the verified on-disk layout: "General" with a capital G,
        // forward-slash paths, empty optional dirs.
        string userDir = Path.Combine(_tempRoot, "user");
        Directory.CreateDirectory(userDir);
        string json = """{"General":{"install_dirs":[{"enabled":true,"path":"Z:/PKG/GAME"}],"addon_install_dir":"","home_dir":"","font_dir":"","sys_modules_dir":""}}""";
        File.WriteAllText(Path.Combine(userDir, "config.json"), json);

        var config = new Shadps4JsonConfigReader().Read(Path.Combine(userDir, "config.json"));

        Assert.IsNotNull(config);
        Assert.AreEqual(1, config!.InstallDirs.Count);
        Assert.AreEqual("Z:/PKG/GAME", config.InstallDirs[0].Path);
        Assert.IsNull(config.AddonInstallDir, "empty optional values mean 'not configured'");
    }

    [TestMethod]
    public void Detect_EmptyOptionalPaths_UseVerifiedDefaults()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        string userDir = Path.Combine(dir, "user");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "config.json"),
            """{"General":{"install_dirs":[],"addon_install_dir":"","home_dir":"","font_dir":"","sys_modules_dir":""}}""");

        var env = Shadps4EnvironmentResolver.Resolve(core, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(Path.Combine(userDir, "home"), env.HomeDirectory);
        Assert.AreEqual(Path.Combine(userDir, "fonts"), env.FontDirectory);
        Assert.AreEqual(Path.Combine(userDir, "sys_modules"), env.SysModulesDirectory);
        Assert.AreEqual(Path.Combine(userDir, "addcont"), env.AddonInstallDirectory);
    }

    [TestMethod]
    public void Detect_DisabledLibrary_IsIgnored()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        string userDir = Path.Combine(dir, "user");
        Directory.CreateDirectory(userDir);
        string enabled = Path.Combine(_tempRoot, "enabled lib");
        string disabled = Path.Combine(_tempRoot, "disabled lib");
        Directory.CreateDirectory(enabled);
        Directory.CreateDirectory(disabled);
        var root = new Newtonsoft.Json.Linq.JObject
        {
            ["General"] = new Newtonsoft.Json.Linq.JObject
            {
                ["install_dirs"] = new Newtonsoft.Json.Linq.JArray(
                    new Newtonsoft.Json.Linq.JObject { ["path"] = enabled, ["enabled"] = true },
                    new Newtonsoft.Json.Linq.JObject { ["path"] = disabled, ["enabled"] = false }),
            },
        };
        File.WriteAllText(Path.Combine(userDir, "config.json"), root.ToString());

        var env = Shadps4EnvironmentResolver.Resolve(core, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(1, env.InstallDirectories.Count);
        Assert.AreEqual(enabled, env.InstallDirectories[0]);
    }

    [TestMethod]
    public void Detect_ForwardSlashAndUnavailableDrivePaths_AreNormalizedAndWarned()
    {
        string dir = Path.Combine(_tempRoot, "dist");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        string userDir = Path.Combine(dir, "user");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "config.json"),
            """{"General":{"install_dirs":[{"enabled":true,"path":"Z:/PKG/GAME"},{"enabled":true,"path":"\\\\server\\share\\ps4 games"}]}}""");

        var env = Shadps4EnvironmentResolver.Resolve(core, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(2, env.InstallDirectories.Count);
        Assert.AreEqual("Z:\\PKG\\GAME", env.InstallDirectories[0], "forward slashes normalize to backslashes");
        Assert.AreEqual(@"\\server\share\ps4 games", env.InstallDirectories[1], "UNC paths are preserved");
        Assert.IsTrue(env.Warnings.Any(w => w.Contains("Z:\\PKG\\GAME")), "unavailable drives are warned, not rejected");
    }

    [TestMethod]
    public void Detect_ExeDirectory_IsNotAssumedToBeALibrary()
    {
        string dir = Path.Combine(_tempRoot, "dist with game folders");
        string core = MakeExe(dir, Shadps4EnvironmentResolver.CoreExeFileName);
        Directory.CreateDirectory(Path.Combine(dir, "CUSA00001")); // a game beside the exe
        string userDir = Path.Combine(dir, "user");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "config.json"), """{"General":{}}""");

        var env = Shadps4EnvironmentResolver.Resolve(core, Path.Combine(_tempRoot, "appdata"));

        Assert.AreEqual(0, env.InstallDirectories.Count,
            "game libraries come ONLY from General.install_dirs, never from the executable folder");
    }

    [TestMethod]
    public void Detect_LegacyTomlFallback_WhenNoJson()
    {
        string appData = Path.Combine(_tempRoot, "appdata");
        string userDir = Path.Combine(appData, "shadPS4");
        Directory.CreateDirectory(userDir);
        string lib = Path.Combine(_tempRoot, "legacy lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(userDir, "config.toml"),
            $"[GUI]\ninstallDirs = [\"{lib.Replace("\\", "\\\\")}\"]\ninstallDirsEnabled = [true]\n");

        var env = Shadps4EnvironmentResolver.Resolve(null, appData);

        Assert.AreEqual(Shadps4UserDirectoryMode.AppData, env.UserDirectoryMode);
        Assert.AreEqual(Shadps4ConfigFormat.LegacyToml, env.ConfigFormat);
        Assert.AreEqual(1, env.InstallDirectories.Count);
        Assert.AreEqual(lib, env.InstallDirectories[0]);
    }

    [TestMethod]
    public void ProductionResolver_ContainsNoMachineSpecificPaths()
    {
        // base = <repo>\PS4PKGTool.Tests\bin\Debug\net10.0-windows -> 4 ups = repo root
        string source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "PS4PKGTool", "Utilities", "Shadps4", "Shadps4EnvironmentResolver.cs"));
        Assert.IsFalse(source.Contains(@"C:\Users"), "no hardcoded user profiles");
        Assert.IsFalse(source.Contains("Z:"), "no hardcoded drive letters");
        Assert.IsFalse(source.Contains("Desktop"), "no hardcoded Desktop folders");
    }

    [TestMethod]
    public void Settings_Shadps4InstallDirectory_RoundTrips()
    {
        string file = Path.Combine(_tempRoot, "Settings.conf");
        var settings = new PS4PKGTool.Utilities.Settings.AppSettings
        {
            Shadps4InstallDirectory = @"D:\Games\shadps4 library",
        };
        PS4PKGTool.Utilities.Settings.SettingsManager.SaveSettings(settings, file);

        var loaded = PS4PKGTool.Utilities.Settings.SettingsManager.LoadSettings(file);

        Assert.AreEqual(@"D:\Games\shadps4 library", loaded.Shadps4InstallDirectory);
        Assert.AreEqual("", new PS4PKGTool.Utilities.Settings.AppSettings().Shadps4InstallDirectory,
            "default is empty - no preset is invented without a shadPS4 config");
    }

    [TestMethod]
    public void Settings_PkgDirectories_NestedEntriesAreDroppedOnLoad()
    {
        string file = Path.Combine(_tempRoot, "Settings.conf");
        var settings = new PS4PKGTool.Utilities.Settings.AppSettings
        {
            PkgDirectories = new List<string>
            {
                @"E:\Games",
                @"E:\Games\Base + Update\Some Title", // nested - dropped
                @"E:\Games\CUSA12345",                // nested - dropped
                @"E:\Games\Base + Update\Some Title", // duplicate + nested
                @"D:\Other",
                "",
                "   ",
            },
        };
        PS4PKGTool.Utilities.Settings.SettingsManager.SaveSettings(settings, file);

        var loaded = PS4PKGTool.Utilities.Settings.SettingsManager.LoadSettings(file);

        CollectionAssert.AreEqual(
            new[] { @"E:\Games", @"D:\Other" },
            loaded.PkgDirectories.ToArray(),
            "nested and duplicate entries are cleaned on load; roots are kept");
    }

    [TestMethod]
    public void Settings_PkgDirectories_SiblingGroupsCollapseUnrelatedRootsStay()
    {
        string file = Path.Combine(_tempRoot, "Settings.conf");
        var settings = new PS4PKGTool.Utilities.Settings.AppSettings
        {
            PkgDirectories = new List<string>
            {
                @"E:\Games\A",
                @"E:\Games\B",   // sibling of A - both collapse to E:\Games
                @"E:\GamesX",    // NOT a child of E:\Games - kept
                @"D:\Other",     // different root - kept
            },
        };
        PS4PKGTool.Utilities.Settings.SettingsManager.SaveSettings(settings, file);

        var loaded = PS4PKGTool.Utilities.Settings.SettingsManager.LoadSettings(file);

        CollectionAssert.AreEqual(
            new[] { @"E:\Games", @"E:\GamesX", @"D:\Other" },
            loaded.PkgDirectories.ToArray(),
            "sibling pollution collapses to the shared parent; unrelated roots stay");
    }

    [TestMethod]
    public void Settings_PkgDirectories_TitleMovePollutionCollapsesToSingleRoot()
    {
        // Real-world shape: moving by title once added one entry per title
        // folder (226 entries from 2 real roots). Every entry is a sibling
        // group under B:\PKG, so the whole list normalizes to B:\PKG.
        string file = Path.Combine(_tempRoot, "Settings.conf");
        var settings = new PS4PKGTool.Utilities.Settings.AppSettings
        {
            PkgDirectories = new List<string>
            {
                @"B:\PKG\Game & Patch",
                @"B:\PKG\New folder",
                @"B:\PKG\Base + Update\Title A",
                @"B:\PKG\Base + Update\Title B",
                @"B:\PKG\Base + Update\Title C",
            },
        };
        PS4PKGTool.Utilities.Settings.SettingsManager.SaveSettings(settings, file);

        var loaded = PS4PKGTool.Utilities.Settings.SettingsManager.LoadSettings(file);

        CollectionAssert.AreEqual(
            new[] { @"B:\PKG" },
            loaded.PkgDirectories.ToArray(),
            "the polluted list normalizes to its common root");
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
