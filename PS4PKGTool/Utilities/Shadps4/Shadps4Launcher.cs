using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PS4PKGTool.Utilities.Shadps4
{
    public enum Shadps4LaunchStatus
    {
        /// <summary>The emulator process was started.</summary>
        Started,
        /// <summary>The configured core exe is missing or unusable.</summary>
        ExecutableMissing,
        /// <summary>The Title ID is not a valid CUSA ID.</summary>
        InvalidTitleId,
        /// <summary>The game is not installed in any configured library.</summary>
        GameNotFound,
        /// <summary>The executable path to boot does not exist.</summary>
        ExecutableNotFound,
        /// <summary>Process.Start threw (reported message).</summary>
        StartFailed,
    }

    /// <summary>
    /// Invokes the shadPS4 core executable. Owns the CLI surface so
    /// version-specific differences stay in one place.
    ///
    /// Verified against shadPS4 main (src/main.cpp):
    /// - The positional argument (or -g/--game) accepts a game ID (CUSAxxxxx)
    ///   which is searched in the configured install dirs (depth 5), or an
    ///   existing filesystem path to boot directly.
    /// - PS4PKGTool never waits for the emulator, so no exit codes are
    ///   interpreted - only process-start failures are reported.
    /// </summary>
    public sealed class Shadps4Launcher
    {
        private static readonly Regex CusaId = new(@"^CUSA\d{5}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>True when a shadPS4 process appears to be running.</summary>
        public static bool IsEmulatorRunning()
        {
            foreach (var name in new[] { "shadPS4", "shadPS4QtLauncher" })
                if (Process.GetProcessesByName(name).Length > 0)
                    return true;
            return false;
        }

        /// <summary>The CLI arguments for launching an installed title by ID.</summary>
        public static string[] BuildTitleArguments(string titleId) => new[] { titleId.Trim() };

        /// <summary>The CLI arguments for booting a specific executable path.</summary>
        public static string[] BuildExecutableArguments(string executablePath) => new[] { "-g", executablePath };

        /// <summary>
        /// Searches every configured library for the game folder and returns
        /// the path to its eboot.bin, or null when not installed. Mirrors the
        /// emulator's search: the CUSA-named folder is located up to 5 levels
        /// under the library root, the eboot up to 5 levels under that.
        /// </summary>
        public static string? FindInstalledEboot(Shadps4Environment env, string titleId)
        {
            foreach (var library in env.InstallDirectories)
            {
                if (string.IsNullOrWhiteSpace(library) || !Directory.Exists(library)) continue;
                string? gameFolder = FindFolderByTitleId(library, titleId, depth: 5);
                if (gameFolder == null) continue;
                string? eboot = FindEboot(gameFolder, depth: 5);
                if (eboot != null) return eboot;
            }
            return null;
        }

        /// <summary>Launches an installed title by CUSA ID.</summary>
        public (Shadps4LaunchStatus Status, string Message) LaunchInstalledTitle(Shadps4Environment env, string titleId)
        {
            if (!CusaId.IsMatch(titleId ?? ""))
                return (Shadps4LaunchStatus.InvalidTitleId, $"'{titleId}' is not a valid CUSA Title ID.");

            string exe = env.CoreExePath ?? "";
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return (Shadps4LaunchStatus.ExecutableMissing, "shadPS4 core executable not found. Configure it in Program Settings.");

            if (FindInstalledEboot(env, titleId) == null)
                return (Shadps4LaunchStatus.GameNotFound, $"Game {titleId} is not installed in any configured shadPS4 library.");

            return Start(exe, BuildTitleArguments(titleId));
        }

        /// <summary>Boots a specific executable (extracted game) directly.</summary>
        public (Shadps4LaunchStatus Status, string Message) LaunchExecutable(Shadps4Environment env, string executablePath)
        {
            string exe = env.CoreExePath ?? "";
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return (Shadps4LaunchStatus.ExecutableMissing, "shadPS4 core executable not found. Configure it in Program Settings.");

            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return (Shadps4LaunchStatus.ExecutableNotFound, $"Executable not found: {executablePath}");

            return Start(exe, BuildExecutableArguments(executablePath));
        }

        /// <summary>Locates eboot.bin inside an extracted game folder (depth-limited).</summary>
        public static string? FindEboot(string folder, int depth)
        {
            if (!Directory.Exists(folder)) return null;
            try
            {
                var stack = new Stack<(string Dir, int Depth)>();
                stack.Push((folder, 0));
                string? deepest = null;
                int deepestDepth = int.MaxValue;
                while (stack.Count > 0)
                {
                    var (dir, d) = stack.Pop();
                    if (d > depth) continue;
                    foreach (var entry in Directory.GetFileSystemEntries(dir))
                    {
                        if (Directory.Exists(entry))
                        {
                            if (d < depth) stack.Push((entry, d + 1));
                        }
                        else if (Path.GetFileName(entry).Equals("eboot.bin", StringComparison.OrdinalIgnoreCase))
                        {
                            if (d < deepestDepth) { deepestDepth = d; deepest = entry; }
                        }
                    }
                }
                return deepest;
            }
            catch
            {
                return null;
            }
        }

        private static string? FindFolderByTitleId(string root, string titleId, int depth)
        {
            try
            {
                var stack = new Stack<(string Dir, int Depth)>();
                stack.Push((root, 0));
                while (stack.Count > 0)
                {
                    var (dir, d) = stack.Pop();
                    if (d > depth) continue;
                    foreach (var entry in Directory.GetDirectories(dir))
                    {
                        string name = Path.GetFileName(entry);
                        if (name.Equals(titleId, StringComparison.OrdinalIgnoreCase))
                            return entry;
                        if (d < depth) stack.Push((entry, d + 1));
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static (Shadps4LaunchStatus Status, string Message) Start(string exe, string[] arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                foreach (var arg in arguments)
                    psi.ArgumentList.Add(arg);
                Process.Start(psi);
                return (Shadps4LaunchStatus.Started, "shadPS4 launched.");
            }
            catch (Exception ex)
            {
                return (Shadps4LaunchStatus.StartFailed, ex.Message);
            }
        }
    }
}
