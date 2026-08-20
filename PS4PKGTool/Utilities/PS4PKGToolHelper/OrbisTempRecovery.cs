using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    /// <summary>
    /// Crash/failure recovery for the orbis "ASCII temp rename" pattern
    /// ("p4t_v_" temp dirs, "ps4pkgtool_orbis_*.pkg" renamed files).
    ///
    /// Two guarantees:
    ///  - a FAILED initial move never leaks the temp dir (the caller wraps
    ///    MoveIntoOrbisTemp and deletes the dir when it throws);
    ///  - a CRASH after the move can be recovered at next startup: the move
    ///    records the original path in an "original_path.txt" sidecar inside
    ///    the temp dir, and Recover() scans the configured PKG directories
    ///    and their drive roots, restores renamed PKGs to their original
    ///    locations, and removes empty leftover temp dirs.
    /// A temp dir that still contains a .pkg is NEVER deleted (that would be
    /// data loss if the restore move failed).
    /// </summary>
    public static class OrbisTempRecovery
    {
        public const string TempDirPrefix = "p4t_v_";
        public const string SidecarName = "original_path.txt";
        public const string TempPkgPattern = "ps4pkgtool_orbis_*.pkg";
        public const string TempPkgNamePrefix = "ps4pkgtool_orbis_";

        /// <summary>True when any path segment starts with "p4t_v_" (a staging directory).</summary>
        public static bool IsUnderTempDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string segment = Path.GetFileName(path);
            while (!string.IsNullOrEmpty(segment))
            {
                if (segment.StartsWith(TempDirPrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
                path = Path.GetDirectoryName(path) ?? "";
                segment = Path.GetFileName(path);
            }
            return false;
        }

        /// <summary>True when the file name is a staged orbis rename ("ps4pkgtool_orbis_*.pkg").</summary>
        public static bool IsTempPkgFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string name = Path.GetFileName(path);
            return name.StartsWith(TempPkgNamePrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the path is a staging artifact (inside a "p4t_v_*" directory
        /// or a "ps4pkgtool_orbis_*.pkg" rename). Recursive PKG scanners must
        /// skip these - a staged PKG is a moved original, not a new package.
        /// </summary>
        public static bool IsStagingArtifact(string path) =>
            IsUnderTempDirectory(path) || IsTempPkgFile(path);

        /// <summary>Result of a recovery scan.</summary>
        public sealed record RecoverySummary(
            int Restored,
            int EmptyDirsRemoved,
            IReadOnlyList<string> Unresolvable)
        {
            public bool FoundAnything => Restored > 0 || EmptyDirsRemoved > 0 || Unresolvable.Count > 0;
        }

        /// <summary>
        /// Moves a PKG into the orbis temp dir, recording the original path
        /// (sidecar) first so a crash can be recovered. Throws when the move
        /// fails - the caller deletes the temp dir in that case.
        /// </summary>
        public static void MoveIntoOrbisTemp(string origPath, string dir, string tempPath)
        {
            try { File.WriteAllText(Path.Combine(dir, SidecarName), origPath); } catch { }
            File.Move(origPath, tempPath);
        }

        /// <summary>
        /// Deletes a temp dir, but NEVER when it still contains a PKG file
        /// (that would destroy data if the restore move failed).
        /// </summary>
        public static void DeleteOrbisTempDirSafe(string tempFilePath)
        {
            try
            {
                string d = Path.GetDirectoryName(tempFilePath);
                if (string.IsNullOrEmpty(d) || !Path.GetFileName(d).StartsWith(TempDirPrefix, StringComparison.OrdinalIgnoreCase))
                    return;
                if (Directory.EnumerateFiles(d, TempPkgPattern, SearchOption.TopDirectoryOnly).Any())
                    return; // still holds a PKG - never delete
                if (Directory.Exists(d))
                    Directory.Delete(d, true);
            }
            catch { }
        }

        /// <summary>
        /// Scans the given roots (configured PKG directories + their drive
        /// roots) for leftover "p4t_v_" temp dirs one level deep and:
        ///  - restores renamed PKGs via the sidecar (move back + delete dir);
        ///  - removes empty leftover temp dirs;
        ///  - reports dirs that cannot be resolved (no sidecar, original
        ///    missing or already occupied) without touching them.
        /// </summary>
        public static RecoverySummary Recover(IEnumerable<string> scanRoots)
        {
            int restored = 0, removed = 0;
            var unresolvable = new List<string>();

            foreach (string root in scanRoots
                         .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string dir in Directory.GetDirectories(root, TempDirPrefix + "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(dir);
                    if (!name.StartsWith(TempDirPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    string[] pkgs = Directory.GetFiles(dir, TempPkgPattern, SearchOption.TopDirectoryOnly);
                    if (pkgs.Length == 0)
                    {
                        // Ours only when it holds nothing but the sidecar (failed
                        // initial move, or already handled). A dir named p4t_v_
                        // with other content is left alone.
                        string[] files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
                        bool onlySidecar = files.All(f =>
                            Path.GetFileName(f).Equals(SidecarName, StringComparison.OrdinalIgnoreCase));
                        if (onlySidecar)
                        {
                            TryDelete(dir);
                            removed++;
                        }
                        continue;
                    }

                    if (TryRestore(pkgs[0], dir))
                    {
                        restored++;
                        continue;
                    }
                    unresolvable.Add(dir);
                }
            }
            return new RecoverySummary(restored, removed, unresolvable);
        }

        private static bool TryRestore(string pkgPath, string tempDir)
        {
            try
            {
                string sidecar = Path.Combine(tempDir, SidecarName);
                if (!File.Exists(sidecar)) return false;
                string original = File.ReadAllText(sidecar).Trim();
                string originalDir = Path.GetDirectoryName(original) ?? "";
                if (string.IsNullOrWhiteSpace(original) || !Directory.Exists(originalDir)) return false;
                if (File.Exists(original)) return false; // already occupied - leave it
                File.Move(pkgPath, original);
                TryDelete(tempDir);
                Logger.LogInformation($"Orbis temp recovery: restored {Path.GetFileName(original)} to {originalDir}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Orbis temp recovery failed for {tempDir}: {ex.Message}");
                return false;
            }
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir)
                    && !Directory.EnumerateFiles(dir, TempPkgPattern, SearchOption.TopDirectoryOnly).Any())
                {
                    Directory.Delete(dir, true);
                }
            }
            catch { }
        }
    }
}
