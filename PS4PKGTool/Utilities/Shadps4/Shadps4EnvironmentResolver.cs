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
    /// Verified behavior:
    /// - The SELECTED EXECUTABLE and the ACTIVE CONFIGURATION are separate
    ///   concepts: a freshly extracted shadPS4QtLauncher.exe immediately shows
    ///   existing games because it reads the shared config.
    /// - Current Windows config: %APPDATA%\shadPS4\config.json (verified on
    ///   this machine; section "General", keys install_dirs/addon_install_dir/
    ///   home_dir/font_dir/sys_modules_dir, forward-slash paths, empty
    ///   optional values).
    /// - Legacy config.toml ([GUI] installDirs/installDirsEnabled/
    ///   addonInstallDir) remains as a verified fallback for older builds.
    /// - Portable user\ folder next to the exe (src/common/path_util.cpp).
    /// - Qt launcher filename shadPS4QtLauncher.exe (verified from current
    ///   distributions).
    ///
    /// Detection is READ-ONLY: nothing is created, moved or rewritten, and
    /// there is no persistent cache.
    /// </summary>
    public static class Shadps4EnvironmentResolver
    {
        /// <summary>The portable user-folder name next to the emulator exe.</summary>
        public const string PortableDirName = "user";

        /// <summary>The AppData folder name shadPS4 uses.</summary>
        public const string AppDataDirName = "shadPS4";

        /// <summary>Verified emulator-core filename (Windows).</summary>
        public const string CoreExeFileName = "shadPS4.exe";

        /// <summary>Verified Qt launcher filename (Windows).</summary>
        public const string QtLauncherFileName = "shadPS4QtLauncher.exe";

        private static readonly IShadps4ConfigReader[] Readers =
        {
            new Shadps4JsonConfigReader(),
            new Shadps4TomlConfigReader(),
        };

        /// <summary>
        /// Validates the user-selected executable path. Loose on purpose:
        /// custom/nightly builds must not be rejected over metadata quirks.
        /// </summary>
        public static (bool IsValid, string? Error) ValidateSelectedExecutablePath(string? path)
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
        /// Detects the shadPS4 environment from the user-selected executable
        /// (shadPS4.exe or shadPS4QtLauncher.exe). appDataRoot is injectable
        /// for tests (defaults to Roaming AppData).
        /// </summary>
        public static Shadps4Environment Resolve(string? selectedExecutablePath, string? appDataRoot = null)
        {
            string appData = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var warnings = new List<string>();

            string? selectedDir = null;
            if (!string.IsNullOrWhiteSpace(selectedExecutablePath))
            {
                if (!File.Exists(selectedExecutablePath))
                    warnings.Add($"Selected shadPS4 executable does not exist: {selectedExecutablePath}");
                selectedDir = Path.GetDirectoryName(selectedExecutablePath);
            }

            // Classify the distribution: verified core/launcher filenames in
            // the selected directory, then its parent and grandparent.
            // Verified real layout: versioned launcher folders ("Latest
            // ShadPS4\" with only the launcher) sit under a root that holds
            // the core (shadPS4.exe). Nothing beyond these names is invented.
            string? core = null, launcher = null;
            string? coreDir = null;
            if (!string.IsNullOrWhiteSpace(selectedDir) && Directory.Exists(selectedDir))
            {
                var probeDirs = new List<string> { selectedDir };
                string? parent = Directory.GetParent(selectedDir)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent)) probeDirs.Add(parent);
                string? grandparent = Directory.GetParent(parent ?? "")?.FullName;
                if (!string.IsNullOrWhiteSpace(grandparent)) probeDirs.Add(grandparent);

                foreach (var dir in probeDirs)
                {
                    if (core == null && File.Exists(Path.Combine(dir, CoreExeFileName)))
                    {
                        core = Path.Combine(dir, CoreExeFileName);
                        coreDir = dir;
                    }
                    if (launcher == null && File.Exists(Path.Combine(dir, QtLauncherFileName)))
                        launcher = Path.Combine(dir, QtLauncherFileName);
                }
            }
            if (core != null && !string.Equals(coreDir, selectedDir, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"shadPS4 core detected in a parent folder: {core}");
                // Real incident: the launcher was a 9-day-newer nightly than the
                // core it found in the parent folder, and the game crashed in
                // the emulator. The versions may not match - say how to fix it.
                warnings.Add("The core and the selected launcher may be from different builds. If games fail to boot, place the matching shadPS4.exe next to the launcher and select it in Settings.");
            }

            var distribution = (core != null, launcher != null) switch
            {
                (true, true) => Shadps4DistributionType.CoreAndQtLauncher,
                (true, false) => Shadps4DistributionType.CoreOnly,
                (false, true) => Shadps4DistributionType.QtLauncherOnly,
                _ => Shadps4DistributionType.Unknown,
            };
            if (distribution == Shadps4DistributionType.Unknown)
                warnings.Add("Neither shadPS4.exe nor shadPS4QtLauncher.exe was found beside the selected executable.");

            // Config candidates in precedence order: shared AppData first
            // (the launcher and current builds read it), then the portable
            // user folder beside the executable.
            var appDataUserDir = Path.Combine(appData, AppDataDirName);
            string? portableUserDir = !string.IsNullOrWhiteSpace(selectedDir)
                ? Path.Combine(selectedDir, PortableDirName)
                : null;

            // Each candidate may hold config.json (current) and/or config.toml (legacy).
            (string Config, Shadps4Config Values, Shadps4ConfigFormat Format)? appDataFound = null;
            (string Config, Shadps4Config Values, Shadps4ConfigFormat Format)? portableFound = null;
            bool appDataFolderPresent = false, portableFolderPresent = false;

            appDataFound = InspectCandidate(appDataUserDir, warnings, out appDataFolderPresent);
            if (portableUserDir != null)
                portableFound = InspectCandidate(portableUserDir, warnings, out portableFolderPresent);

            var candidatePaths = new List<string>();
            if (appDataFound != null) candidatePaths.Add(appDataFound.Value.Config);
            if (portableFound != null) candidatePaths.Add(portableFound.Value.Config);

            // Ambiguous: valid configs in both layouts - no silent pick.
            if (appDataFound != null && portableFound != null)
            {
                warnings.Add("Multiple shadPS4 configurations were found:");
                warnings.Add($"  AppData:  {appDataFound.Value.Config}");
                warnings.Add($"  Portable: {portableFound.Value.Config}");
                return Build(selectedExecutablePath, core, launcher, distribution,
                    null, null, Shadps4ConfigFormat.Unknown, Shadps4UserDirectoryMode.Ambiguous,
                    Shadps4DetectionConfidence.Low, Array.Empty<string>(), null, null, null, null,
                    candidatePaths, warnings);
            }

            var found = appDataFound ?? portableFound;
            if (found != null)
            {
                bool isPortable = appDataFound == null;
                var mode = isPortable ? Shadps4UserDirectoryMode.Portable : Shadps4UserDirectoryMode.AppData;
                string userDir = isPortable ? portableUserDir! : appDataUserDir;
                var values = found.Value.Values;

                if (!isPortable && portableFolderPresent)
                    warnings.Add($"Ignored stale portable user folder (no readable config): {portableUserDir}");

                bool hasCore = core != null && File.Exists(core);
                var dirs = ResolveInstallDirectories(values, userDir, warnings);
                string home = ResolveDefaulted(values.HomeDir, Path.Combine(userDir, "home"), userDir, warnings);
                string fonts = ResolveDefaulted(values.FontDir, Path.Combine(userDir, "fonts"), userDir, warnings);
                string sysModules = ResolveDefaulted(values.SysModulesDir, Path.Combine(userDir, "sys_modules"), userDir, warnings);
                string addon = ResolveDefaulted(values.AddonInstallDir, Path.Combine(userDir, "addcont"), userDir, warnings);

                return Build(selectedExecutablePath, core, launcher, distribution,
                    userDir, found.Value.Config, found.Value.Format, mode,
                    hasCore ? Shadps4DetectionConfidence.High : Shadps4DetectionConfidence.Low,
                    dirs, home, fonts, sysModules, addon, candidatePaths, warnings);
            }

            // A user folder exists somewhere but no readable config - layout
            // defaults only, so the environment is not fully trustworthy.
            foreach (var (dir, present) in new[] { (appDataUserDir, appDataFolderPresent), (portableUserDir, portableFolderPresent) })
            {
                if (dir == null || !present) continue;
                warnings.Add($"shadPS4 user folder found without a readable config: {dir} (layout defaults assumed)");
                return Build(selectedExecutablePath, core, launcher, distribution,
                    dir, null, Shadps4ConfigFormat.Unknown, Shadps4UserDirectoryMode.Custom,
                    Shadps4DetectionConfidence.Low, Array.Empty<string>(),
                    Path.Combine(dir, "home"), Path.Combine(dir, "fonts"),
                    Path.Combine(dir, "sys_modules"), Path.Combine(dir, "addcont"),
                    candidatePaths, warnings);
            }

            return Build(selectedExecutablePath, core, launcher, distribution,
                null, null, Shadps4ConfigFormat.Unknown, Shadps4UserDirectoryMode.Unknown,
                Shadps4DetectionConfidence.None, Array.Empty<string>(), null, null, null, null,
                candidatePaths, warnings);
        }

        /// <summary>
        /// Inspects one user-directory candidate. A folder only counts when it
        /// holds a READABLE config (the emulator auto-creates the folders, so
        /// existence alone is not evidence). config.json takes precedence over
        /// config.toml inside the same folder.
        /// </summary>
        private static (string Config, Shadps4Config Values, Shadps4ConfigFormat Format)? InspectCandidate(
            string userDir, List<string> warnings, out bool folderPresent)
        {
            folderPresent = Directory.Exists(userDir);
            if (!folderPresent) return null;

            try
            {
                foreach (var name in new[] { "config.json", "config.toml" })
                {
                    string path = Path.Combine(userDir, name);
                    if (!File.Exists(path)) continue;
                    foreach (var reader in Readers)
                    {
                        if (!reader.Handles(path)) continue;
                        var values = reader.Read(path);
                        if (values == null)
                        {
                            warnings.Add($"Unreadable shadPS4 config ignored: {path}");
                            continue;
                        }
                        var format = name == "config.json"
                            ? Shadps4ConfigFormat.Json
                            : Shadps4ConfigFormat.LegacyToml;
                        return (path, values, format);
                    }
                }
            }
            catch
            {
                // inaccessible directory - treated as not present
            }
            return null;
        }

        private static IReadOnlyList<string> ResolveInstallDirectories(
            Shadps4Config values, string userDir, List<string> warnings)
        {
            var dirs = new List<string>();
            foreach (var d in values.InstallDirs)
            {
                if (!d.Enabled || string.IsNullOrWhiteSpace(d.Path)) continue;
                string normalized = NormalizeConfiguredPath(d.Path, userDir, warnings);
                dirs.Add(normalized);
                if (!Directory.Exists(normalized))
                    warnings.Add($"Configured game directory does not currently exist: {normalized}");
            }
            if (dirs.Count == 0)
                warnings.Add("shadPS4 has no enabled game libraries configured.");
            return dirs;
        }

        /// <summary>Configured value or the verified default; empty values are not failures.</summary>
        private static string ResolveDefaulted(string? configured, string fallback, string userDir, List<string> warnings)
            => NormalizeConfiguredPath(string.IsNullOrWhiteSpace(configured) ? fallback : configured!, userDir, warnings);

        /// <summary>
        /// Normalizes a configured path: expands environment variables, strips
        /// surrounding quotes, converts forward slashes to backslashes,
        /// trims trailing separators, and resolves relative paths against the
        /// user directory (with a warning). Read-only.
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
            p = p.Trim();
            p = p.Replace('/', '\\'); // verified: configs mix separators ("C:/games" style forward slashes)
            p = p.TrimEnd('\\', '/');
            if (p.Length >= 2 && p[1] == ':' && p.Length == 2)
                p += '\\'; // bare drive letter "<drive>:" -> "<drive>:\"
            if (!Path.IsPathRooted(p))
            {
                warnings.Add($"Relative configured path resolved against the user directory: {path}");
                p = Path.GetFullPath(Path.Combine(userDir, p));
            }
            return p;
        }

        private static Shadps4Environment Build(
            string? selected, string? core, string? launcher, Shadps4DistributionType distribution,
            string? userDir, string? configPath, Shadps4ConfigFormat format, Shadps4UserDirectoryMode mode,
            Shadps4DetectionConfidence confidence, IReadOnlyList<string> installDirs,
            string? home, string? fonts, string? sysModules, string? addon,
            IReadOnlyList<string> candidates, IReadOnlyList<string> warnings)
            => new()
            {
                SelectedExecutablePath = selected,
                CoreExePath = core,
                LauncherExePath = launcher,
                DistributionType = distribution,
                UserDirectory = userDir,
                ConfigPath = configPath,
                ConfigFormat = format,
                UserDirectoryMode = mode,
                DetectionConfidence = confidence,
                InstallDirectories = installDirs,
                HomeDirectory = home,
                FontDirectory = fonts,
                SysModulesDirectory = sysModules,
                AddonInstallDirectory = addon,
                CandidateConfigPaths = candidates,
                Warnings = warnings,
            };
    }
}
