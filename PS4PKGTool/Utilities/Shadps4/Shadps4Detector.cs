using System;
using System.Collections.Generic;
using System.IO;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>
    /// Resolves a shadPS4 installation into a Shadps4Environment.
    ///
    /// Verified against shadPS4 main:
    /// - Windows user-dir resolution (src/common/path_util.cpp): portable
    ///   "user" folder next to the executable first (the emulator checks the
    ///   process CWD; the exe directory is the practical equivalent), then
    ///   %APPDATA%\shadPS4. Both locations are auto-created by the emulator,
    ///   so folder existence alone is NOT evidence - a config file must be
    ///   present (config.json current backend, config.toml legacy).
    /// - When both locations carry a config, the result is Ambiguous and no
    ///   directory is selected silently.
    /// </summary>
    public static class Shadps4Detector
    {
        /// <summary>The portable user-folder name next to the emulator exe.</summary>
        public const string PortableDirName = "user";

        /// <summary>The AppData folder name shadPS4 uses.</summary>
        public const string AppDataDirName = "shadPS4";

        private static readonly IShadps4ConfigReader[] Readers =
        {
            new Shadps4JsonConfigReader(),
            new Shadps4TomlConfigReader(),
        };

        /// <summary>
        /// Detects the shadPS4 environment from the configured exe paths.
        /// appDataRoot is injectable for tests (defaults to Roaming AppData).
        /// </summary>
        public static Shadps4Environment Detect(string? coreExePath, string? launcherExePath, string? appDataRoot = null)
        {
            string appData = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var candidates = new List<(string UserDir, bool IsPortable)>();

            // Portable candidate: user\ next to the core exe (or launcher).
            string? exeDir = null;
            if (!string.IsNullOrWhiteSpace(coreExePath) && File.Exists(coreExePath))
                exeDir = Path.GetDirectoryName(coreExePath);
            else if (!string.IsNullOrWhiteSpace(launcherExePath) && File.Exists(launcherExePath))
                exeDir = Path.GetDirectoryName(launcherExePath);

            if (!string.IsNullOrWhiteSpace(exeDir))
                candidates.Add((Path.Combine(exeDir, PortableDirName), true));

            candidates.Add((Path.Combine(appData, AppDataDirName), false));

            // Inspect every candidate: a config file must exist and parse.
            string? portableConfig = null, portableUserDir = null;
            string? appDataConfig = null, appDataUserDir = null;
            Shadps4Config? portableValues = null, appDataValues = null;

            foreach (var (userDir, isPortable) in candidates)
            {
                if (!Directory.Exists(userDir)) continue;
                foreach (var reader in Readers)
                {
                    foreach (var configFile in Directory.GetFiles(userDir, "config.json"))
                    {
                        if (!reader.Handles(configFile)) continue;
                        var values = reader.Read(configFile);
                        if (values == null) continue; // present but unreadable -> keep scanning
                        if (isPortable) { portableConfig = configFile; portableUserDir = userDir; portableValues = values; }
                        else { appDataConfig = configFile; appDataUserDir = userDir; appDataValues = values; }
                    }
                    foreach (var configFile in Directory.GetFiles(userDir, "config.toml"))
                    {
                        if (!reader.Handles(configFile)) continue;
                        var values = reader.Read(configFile);
                        if (values == null) continue;
                        if (isPortable) { portableConfig ??= configFile; portableUserDir ??= userDir; portableValues ??= values; }
                        else { appDataConfig ??= configFile; appDataUserDir ??= userDir; appDataValues ??= values; }
                    }
                }
            }

            var candidatePaths = new List<string>();
            if (portableConfig != null) candidatePaths.Add(portableConfig);
            if (appDataConfig != null) candidatePaths.Add(appDataConfig);

            // Ambiguous: two valid configs from different layouts.
            if (portableConfig != null && appDataConfig != null)
            {
                return new Shadps4Environment
                {
                    CoreExePath = coreExePath,
                    LauncherExePath = launcherExePath,
                    UserDirectoryMode = Shadps4UserDirectoryMode.Ambiguous,
                    DetectionConfidence = Shadps4DetectionConfidence.Low,
                    CandidateConfigPaths = candidatePaths,
                };
            }

            string? chosenUserDir = portableUserDir ?? appDataUserDir;
            string? chosenConfig = portableConfig ?? appDataConfig;
            Shadps4Config? chosenValues = portableValues ?? appDataValues;

            if (chosenUserDir != null && chosenValues != null)
            {
                var mode = portableConfig != null
                    ? Shadps4UserDirectoryMode.Portable
                    : Shadps4UserDirectoryMode.AppData;
                bool hasExe = !string.IsNullOrWhiteSpace(coreExePath) && File.Exists(coreExePath);
                return BuildEnvironment(coreExePath, launcherExePath, chosenUserDir, chosenConfig, chosenValues,
                    mode, hasExe ? Shadps4DetectionConfidence.High : Shadps4DetectionConfidence.Low, candidatePaths);
            }

            // A user folder exists somewhere but no readable config - layout
            // defaults only, so the environment is not fully trustworthy.
            foreach (var (userDir, _) in candidates)
            {
                if (!Directory.Exists(userDir)) continue;
                return new Shadps4Environment
                {
                    CoreExePath = coreExePath,
                    LauncherExePath = launcherExePath,
                    UserDirectory = userDir,
                    UserDirectoryMode = Shadps4UserDirectoryMode.Custom,
                    DetectionConfidence = Shadps4DetectionConfidence.Low,
                    SaveDataDirectory = Path.Combine(userDir, "home"),
                    AddonDirectory = Path.Combine(userDir, "addcont"),
                    CandidateConfigPaths = candidatePaths,
                };
            }

            return new Shadps4Environment
            {
                CoreExePath = coreExePath,
                LauncherExePath = launcherExePath,
                UserDirectoryMode = Shadps4UserDirectoryMode.Unknown,
                DetectionConfidence = Shadps4DetectionConfidence.None,
                CandidateConfigPaths = candidatePaths,
            };
        }

        private static Shadps4Environment BuildEnvironment(
            string? coreExe, string? launcherExe, string userDir, string? configPath,
            Shadps4Config values, Shadps4UserDirectoryMode mode,
            Shadps4DetectionConfidence confidence, List<string> candidates)
        {
            var dirs = new List<string>();
            foreach (var d in values.InstallDirs)
                if (d.Enabled && !string.IsNullOrWhiteSpace(d.Path))
                    dirs.Add(d.Path);

            string home = !string.IsNullOrWhiteSpace(values.HomeDir)
                ? values.HomeDir!
                : Path.Combine(userDir, "home");
            string addon = !string.IsNullOrWhiteSpace(values.AddonInstallDir)
                ? values.AddonInstallDir!
                : Path.Combine(userDir, "addcont");

            return new Shadps4Environment
            {
                CoreExePath = coreExe,
                LauncherExePath = launcherExe,
                UserDirectory = userDir,
                ConfigPath = configPath,
                UserDirectoryMode = mode,
                DetectionConfidence = confidence,
                InstallDirectories = dirs,
                SaveDataDirectory = home,
                AddonDirectory = addon,
                CandidateConfigPaths = candidates,
            };
        }
    }
}
