using System;
using System.Collections.Generic;
using System.IO;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>
    /// Resolves a shadPS4 installation into a Shadps4Environment by reading
    /// shadPS4's OWN configuration - PS4PKGTool keeps no duplicate copy of
    /// these paths and never writes shadPS4's config.
    ///
    /// Verified against shadPS4 main (current source):
    /// - Windows user-dir resolution (src/common/path_util.cpp): portable
    ///   "user" folder next to the executable (the emulator checks the
    ///   process CWD; the exe directory is the practical equivalent), then
    ///   %APPDATA%\shadPS4 (Roaming). Both locations are auto-created by the
    ///   emulator, so folder existence alone is NOT evidence - a config file
    ///   must be present (config.json current backend, config.toml legacy).
    /// - Qt launcher filename: shadPS4QtLauncher.exe, shipped next to the core.
    /// - When both layouts carry a config, the result is Ambiguous and no
    ///   directory is selected silently.
    ///
    /// Detection is READ-ONLY: nothing is created, moved or rewritten.
    /// There is intentionally no persistent cache - resolution is cheap and
    /// always reflects the current filesystem/config state.
    /// </summary>
    public static class Shadps4EnvironmentResolver
    {
        /// <summary>The portable user-folder name next to the emulator exe.</summary>
        public const string PortableDirName = "user";

        /// <summary>The AppData folder name shadPS4 uses.</summary>
        public const string AppDataDirName = "shadPS4";

        /// <summary>Verified Qt launcher filename (Windows).</summary>
        public const string QtLauncherFileName = "shadPS4QtLauncher.exe";

        private static readonly IShadps4ConfigReader[] Readers =
        {
            new Shadps4JsonConfigReader(),
            new Shadps4TomlConfigReader(),
        };

        /// <summary>
        /// Validates a user-selected core executable path. Loose on purpose:
        /// custom/nightly builds must not be rejected over metadata quirks.
        /// </summary>
        public static (bool IsValid, string? Error) ValidateCoreExePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (false, "No executable selected.");
            if (!File.Exists(path))
                return (false, "The selected file does not exist.");
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return (false, "The selected file is not an executable (.exe).");
            return (true, null);
        }

        /// <summary>
        /// Detects the shadPS4 environment from the configured exe paths.
        /// appDataRoot is injectable for tests (defaults to Roaming AppData).
        /// </summary>
        public static Shadps4Environment Resolve(string? coreExePath, string? launcherExePath = null, string? appDataRoot = null)
        {
            string appData = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var warnings = new List<string>();

            string? exeDir = null;
            if (!string.IsNullOrWhiteSpace(coreExePath) && File.Exists(coreExePath))
            {
                exeDir = Path.GetDirectoryName(coreExePath);
            }
            else if (!string.IsNullOrWhiteSpace(coreExePath))
            {
                warnings.Add($"Selected shadPS4 core executable does not exist: {coreExePath}");
                exeDir = Path.GetDirectoryName(coreExePath);
            }
            else if (!string.IsNullOrWhiteSpace(launcherExePath) && File.Exists(launcherExePath))
            {
                exeDir = Path.GetDirectoryName(launcherExePath);
            }

            // Detect the Qt launcher next to the core (verified filename);
            // the user-selected launcher path wins when it exists.
            string? detectedLauncher = null;
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                string candidate = Path.Combine(exeDir, QtLauncherFileName);
                if (File.Exists(candidate))
                    detectedLauncher = candidate;
            }
            string? launcher = !string.IsNullOrWhiteSpace(launcherExePath) && File.Exists(launcherExePath)
                ? launcherExePath
                : detectedLauncher;
            if (string.IsNullOrWhiteSpace(launcher))
            {
                // Not an error: the launcher is optional for this integration.
            }

            var candidates = new List<(string UserDir, bool IsPortable)>();
            if (!string.IsNullOrWhiteSpace(exeDir))
                candidates.Add((Path.Combine(exeDir, PortableDirName), true));
            candidates.Add((Path.Combine(appData, AppDataDirName), false));

            // Inspect every candidate: a config file must exist and parse.
            // A folder without any readable config is not a valid environment.
            string? portableConfig = null, portableUserDir = null;
            string? appDataConfig = null, appDataUserDir = null;
            Shadps4Config? portableValues = null, appDataValues = null;
            bool portablePresent = false, appDataPresent = false;

            foreach (var (userDir, isPortable) in candidates)
            {
                if (!Directory.Exists(userDir)) continue;
                if (isPortable) portablePresent = true; else appDataPresent = true;

                string? foundConfig = null;
                Shadps4Config? foundValues = null;
                foreach (var configFile in GetConfigCandidates(userDir))
                {
                    foreach (var reader in Readers)
                    {
                        if (!reader.Handles(configFile)) continue;
                        var values = reader.Read(configFile);
                        if (values == null)
                        {
                            warnings.Add($"Unreadable shadPS4 config ignored: {configFile}");
                            continue;
                        }
                        foundConfig = configFile;
                        foundValues = values;
                        break;
                    }
                    if (foundConfig != null) break;
                }

                if (isPortable) { portableConfig = foundConfig; portableUserDir = userDir; portableValues = foundValues; }
                else { appDataConfig = foundConfig; appDataUserDir = userDir; appDataValues = foundValues; }
            }

            var candidatePaths = new List<string>();
            if (portableConfig != null) candidatePaths.Add(portableConfig);
            if (appDataConfig != null) candidatePaths.Add(appDataConfig);

            // Ambiguous: two valid configs from different layouts. No silent pick.
            if (portableConfig != null && appDataConfig != null)
            {
                warnings.Add("Multiple shadPS4 configurations were found:");
                warnings.Add($"  Portable: {portableConfig}");
                warnings.Add($"  AppData:  {appDataConfig}");
                return new Shadps4Environment
                {
                    CoreExePath = coreExePath,
                    LauncherExePath = launcher,
                    UserDirectoryMode = Shadps4UserDirectoryMode.Ambiguous,
                    DetectionConfidence = Shadps4DetectionConfidence.Low,
                    CandidateConfigPaths = candidatePaths,
                    Warnings = warnings,
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

                // A stale portable folder beside the exe while AppData is active
                // is a real scenario - surface it rather than ignore silently.
                if (mode == Shadps4UserDirectoryMode.AppData && portablePresent)
                    warnings.Add($"Ignored stale portable user folder (no readable config): {Path.Combine(exeDir ?? "", PortableDirName)}");

                return BuildEnvironment(coreExePath, launcher, chosenUserDir, chosenConfig, chosenValues,
                    mode, hasExe ? Shadps4DetectionConfidence.High : Shadps4DetectionConfidence.Low,
                    candidatePaths, warnings);
            }

            // A user folder exists somewhere but no readable config - layout
            // defaults only, so the environment is not fully trustworthy.
            foreach (var (userDir, isPortable) in candidates)
            {
                if (!Directory.Exists(userDir)) continue;
                warnings.Add($"shadPS4 user folder found without a readable config: {userDir} (layout defaults assumed)");
                return new Shadps4Environment
                {
                    CoreExePath = coreExePath,
                    LauncherExePath = launcher,
                    UserDirectory = userDir,
                    UserDirectoryMode = Shadps4UserDirectoryMode.Custom,
                    DetectionConfidence = Shadps4DetectionConfidence.Low,
                    SaveDataDirectory = Path.Combine(userDir, "home"),
                    AddonInstallDirectory = Path.Combine(userDir, "addcont"),
                    CandidateConfigPaths = candidatePaths,
                    Warnings = warnings,
                };
            }

            return new Shadps4Environment
            {
                CoreExePath = coreExePath,
                LauncherExePath = launcher,
                UserDirectoryMode = Shadps4UserDirectoryMode.Unknown,
                DetectionConfidence = Shadps4DetectionConfidence.None,
                CandidateConfigPaths = candidatePaths,
                Warnings = warnings,
            };
        }

        private static IEnumerable<string> GetConfigCandidates(string userDir)
        {
            // yield inside try/catch is not allowed - collect first.
            var result = new List<string>();
            try
            {
                foreach (var name in new[] { "config.json", "config.toml" })
                {
                    string path = Path.Combine(userDir, name);
                    if (File.Exists(path)) result.Add(path);
                }
            }
            catch
            {
                // inaccessible directory - no candidates
            }
            return result;
        }

        private static Shadps4Environment BuildEnvironment(
            string? coreExe, string? launcher, string userDir, string? configPath,
            Shadps4Config values, Shadps4UserDirectoryMode mode,
            Shadps4DetectionConfidence confidence, List<string> candidates, List<string> warnings)
        {
            var dirs = new List<string>();
            foreach (var d in values.InstallDirs)
            {
                if (!d.Enabled || string.IsNullOrWhiteSpace(d.Path)) continue;
                string normalized = NormalizeConfiguredPath(d.Path, userDir, warnings);
                if (Directory.Exists(normalized))
                    dirs.Add(normalized);
                else
                {
                    dirs.Add(normalized);
                    warnings.Add($"Configured game directory does not currently exist: {normalized}");
                }
            }
            if (values.InstallDirs.Count == 0)
                warnings.Add("shadPS4 has no game libraries configured.");

            string home = NormalizeConfiguredPath(
                string.IsNullOrWhiteSpace(values.HomeDir) ? Path.Combine(userDir, "home") : values.HomeDir!,
                userDir, warnings);
            string addon = NormalizeConfiguredPath(
                string.IsNullOrWhiteSpace(values.AddonInstallDir) ? Path.Combine(userDir, "addcont") : values.AddonInstallDir!,
                userDir, warnings);

            return new Shadps4Environment
            {
                CoreExePath = coreExe,
                LauncherExePath = launcher,
                UserDirectory = userDir,
                ConfigPath = configPath,
                UserDirectoryMode = mode,
                DetectionConfidence = confidence,
                InstallDirectories = dirs,
                SaveDataDirectory = home,
                AddonInstallDirectory = addon,
                CandidateConfigPaths = candidates,
                Warnings = warnings,
            };
        }

        /// <summary>
        /// Normalizes a configured path: expands environment variables, strips
        /// surrounding quotes, trims trailing separators, and resolves relative
        /// paths against the user directory (with a warning). Read-only.
        /// </summary>
        private static string NormalizeConfiguredPath(string path, string userDir, List<string> warnings)
        {
            string p = path.Trim();
            if (p.Length >= 2 && ((p[0] == '"' && p[^1] == '"') || (p[0] == '\'' && p[^1] == '\'')))
                p = p[1..^1];
            if (p.Contains('%') || p.Contains("${"))
            {
                try { p = Environment.ExpandEnvironmentVariables(p); }
                catch { warnings.Add($"Could not expand environment variables in configured path: {path}"); }
            }
            p = p.Trim().TrimEnd('\\', '/');
            if (!Path.IsPathRooted(p))
            {
                warnings.Add($"Relative configured path resolved against the user directory: {path}");
                p = Path.GetFullPath(Path.Combine(userDir, p));
            }
            return p;
        }
    }
}
