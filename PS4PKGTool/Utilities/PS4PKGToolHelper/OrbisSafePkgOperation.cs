using System;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    internal enum OrbisPkgStageMode
    {
        /// <summary>The original path is already orbis-safe - passed through untouched.</summary>
        Direct,

        /// <summary>Parent directory is safe but the file name is not - the file is renamed in place.</summary>
        RenameInPlace,

        /// <summary>The parent path itself is unsafe - the PKG is moved to a short
        /// ASCII staging directory at the root of the same drive (crash-recoverable).</summary>
        DriveRootStaging,
    }

    /// <summary>
    /// Crash-recovery metadata written next to a RenameInPlace staging file
    /// ("&lt;temp&gt;.recovery"). Written BEFORE the rename so a crash, kill or
    /// power loss leaves enough information to restore the exact original
    /// file name afterwards. Never present during normal operation - it is
    /// removed once Restore() has verified the original path.
    /// </summary>
    internal sealed class StagedPkgRecoveryMetadata
    {
        public int SchemaVersion { get; set; } = 1;
        public string OriginalFullPath { get; set; } = "";
        public string TemporaryFullPath { get; set; } = "";
        public string StagingMode { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
        /// <summary>PID of the process that staged the package. Together with
        /// OwnerProcessStartTimeUtc it lets the recovery scanner distinguish a
        /// LIVE operation (never recover) from an abandoned one, even across
        /// processes and PID reuse.</summary>
        public int OwnerProcessId { get; set; }
        public string OwnerProcessStartTimeUtc { get; set; } = "";
    }

    internal sealed class OrbisSafePkgRestoreResult
    {
        public bool Succeeded { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
        public string RecoveryDirectory { get; init; } = string.Empty;
    }

    /// <summary>
    /// One orbis-pub-cmd-safe package staging operation with three modes
    /// (see <see cref="OrbisPkgStageMode"/>). The operation object owns the
    /// whole lifecycle: <see cref="Prepare"/> decides the mode, <see cref="OrbisPath"/>
    /// is what every caller passes to orbis-pub-cmd, and <see cref="Restore"/>
    /// undoes the staging - a no-op in Direct mode.
    ///
    /// DriveRootStaging writes the same "original_path.txt" sidecar as the
    /// legacy staging so <see cref="OrbisTempRecovery.Recover"/> keeps working
    /// for crash recovery after this operation.
    /// </summary>
    internal sealed class OrbisSafePkgOperation
    {
        /// <summary>Legacy MAX_PATH (260) minus the null terminator - orbis-pub-cmd
        /// uses ANSI Win32 APIs, so this is its hard ceiling.</summary>
        public const int MaxOrbisPathLength = 259;

        /// <summary>Longest parent directory that still fits a
        /// "ps4pkgtool_orbis_&lt;32-char-guid&gt;.pkg" rename under MAX_PATH.</summary>
        public const int MaxStagedParentLength = MaxOrbisPathLength - 54;

        /// <summary>Test hook: overrides the staging root for DriveRootStaging.
        /// Production always stages at the drive root of the source package.</summary>
        internal static string? StagingRootOverride = null;

        /// <summary>Sidecar suffix for RenameInPlace crash metadata
        /// ("ps4pkgtool_orbis_&lt;guid&gt;.pkg.recovery").</summary>
        public const string RecoverySidecarSuffix = ".recovery";

        /// <summary>
        /// In-memory registry of staging paths owned by live operations. The
        /// recovery scanner and recovery actions consult it so an abandoned
        /// artifact is never touched while this process still uses it. A path
        /// left behind by a crashed process is NOT in any registry - which is
        /// exactly what makes it recoverable.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ActiveStagedPaths =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when the given path is currently staged by a live operation.</summary>
        public static bool IsPathInActiveOperation(string fullPath)
        {
            try { return ActiveStagedPaths.ContainsKey(Path.GetFullPath(fullPath)); }
            catch { return false; }
        }

        private static void RegisterActivePath(string path)
        {
            try { ActiveStagedPaths[Path.GetFullPath(path)] = 0; } catch { }
        }

        private static void UnregisterActivePath(string path)
        {
            try { ActiveStagedPaths.TryRemove(Path.GetFullPath(path), out _); } catch { }
        }

        /// <summary>Writes staging crash metadata ATOMICALLY:
        /// 1. serialize the complete metadata (including the owning process)
        /// 2. write it to &lt;final&gt;.tmp and flush it to disk
        /// 3. atomically rename &lt;final&gt;.tmp -> &lt;final&gt;
        /// Only after this returns does the caller rename the PKG, so a crash
        /// at any point leaves either no metadata (PKG untouched) or complete
        /// valid metadata (PKG already staged) - never a half-written file.</summary>
        internal static void WriteRecoveryMetadata(string originalPath, string temporaryPath, OrbisPkgStageMode mode)
        {
            PublishMetadataAtomic(temporaryPath + RecoverySidecarSuffix, new StagedPkgRecoveryMetadata
            {
                SchemaVersion = 1,
                OriginalFullPath = Path.GetFullPath(originalPath),
                TemporaryFullPath = Path.GetFullPath(temporaryPath),
                StagingMode = mode.ToString(),
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                OwnerProcessId = Environment.ProcessId,
                OwnerProcessStartTimeUtc = CurrentProcessStartTimeUtc.ToString("O"),
            });
        }

        /// <summary>Sidecar inside a DriveRootStaging directory that records the
        /// owning process, so a second PS4PKGTool instance's recovery scan can
        /// tell a live operation from an abandoned one. Kept separate from the
        /// legacy single-line original_path.txt for backward compatibility.</summary>
        public const string OwnerSidecarName = "owner.json";

        /// <summary>Persists owner metadata for a DriveRootStaging directory
        /// BEFORE the PKG is moved into it.</summary>
        internal static void WriteOwnerMetadata(string stagingDirectory, string temporaryPath, string originalPath)
        {
            PublishMetadataAtomic(Path.Combine(stagingDirectory, OwnerSidecarName), new StagedPkgRecoveryMetadata
            {
                SchemaVersion = 1,
                OriginalFullPath = Path.GetFullPath(originalPath),
                TemporaryFullPath = Path.GetFullPath(temporaryPath),
                StagingMode = OrbisPkgStageMode.DriveRootStaging.ToString(),
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                OwnerProcessId = Environment.ProcessId,
                OwnerProcessStartTimeUtc = CurrentProcessStartTimeUtc.ToString("O"),
            });
        }

        /// <summary>Atomic metadata publish: complete JSON -> &lt;final&gt;.tmp
        /// (flushed to disk) -> rename to &lt;final&gt;. A crash at any point
        /// leaves either no metadata or complete valid metadata.</summary>
        private static void PublishMetadataAtomic(string sidecarPath, StagedPkgRecoveryMetadata metadata)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(metadata);
            string tmp = sidecarPath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new System.IO.StreamWriter(fs, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true); // durable before the atomic publish
            }
            File.Move(tmp, sidecarPath, overwrite: true);
        }

        private static readonly DateTime CurrentProcessStartTimeUtc = GetCurrentProcessStartTimeUtc();

        private static DateTime GetCurrentProcessStartTimeUtc()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
            catch { return DateTime.UtcNow; }
        }

        /// <summary>
        /// True when the given PID belongs to a process that is STILL RUNNING
        /// and whose start time matches - i.e. the staging operation may still
        /// be live. A reused PID (start time differs) counts as dead, so stale
        /// metadata never blocks recovery forever. Unverifiable-but-running
        /// processes count as alive (conservative: block recovery).
        /// </summary>
        internal static bool IsOwnerProcessAlive(int processId, DateTime startTimeUtc)
        {
            if (processId <= 0) return false;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (process == null) return false;
                try
                {
                    DateTime actual = process.StartTime.ToUniversalTime();
                    return Math.Abs((actual - startTimeUtc).TotalSeconds) < 2;
                }
                catch
                {
                    // The process exists but its start time cannot be read
                    // (access denied / already exiting) - conservative: treat
                    // the operation as potentially live.
                    return true;
                }
            }
            catch (ArgumentException) { return false; }                       // PID does not exist
            catch (InvalidOperationException) { return false; }               // process exited
            catch (System.ComponentModel.Win32Exception) { return true; }     // exists, unverifiable - alive
        }

        /// <summary>Reads a RenameInPlace .recovery sidecar; null when missing/invalid.</summary>
        internal static StagedPkgRecoveryMetadata? ReadRecoveryMetadata(string sidecarPath)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<StagedPkgRecoveryMetadata>(
                    File.ReadAllText(sidecarPath));
            }
            catch { return null; }
        }

        private bool _restored;

        private OrbisSafePkgOperation(
            string originalPath, string orbisPath, string? temporaryDirectory, OrbisPkgStageMode mode)
        {
            OriginalPath = originalPath;
            OrbisPath = orbisPath;
            TemporaryDirectory = temporaryDirectory;
            Mode = mode;
        }

        public OrbisPkgStageMode Mode { get; }
        public string OriginalPath { get; }
        /// <summary>The path every caller must pass to orbis-pub-cmd.</summary>
        public string OrbisPath { get; }
        /// <summary>Staging directory (DriveRootStaging only; null otherwise).</summary>
        public string? TemporaryDirectory { get; }
        public bool IsStaged => Mode != OrbisPkgStageMode.Direct;
        public bool IsRecoverable => Mode == OrbisPkgStageMode.DriveRootStaging;

        /// <summary>
        /// Conservative path check for orbis-pub-cmd, which uses ANSI file
        /// APIs. ASCII is deliberately used as the portable safe subset: a
        /// character that happens to exist in one machine's active code page
        /// may not exist in another's. Control characters are rejected too.
        /// </summary>
        public static bool IsAsciiSafePath(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.All(character => character >= 0x20 && character <= 0x7E);

        /// <summary>True when the ORIGINAL path may be handed to orbis-pub-cmd directly:
        /// ASCII-only, no control characters, under the legacy MAX_PATH ceiling, not a
        /// staging directory and not a staged rename.</summary>
        public static bool IsOrbisSafePath(string fullPath)
        {
            if (!IsAsciiSafePath(fullPath)) return false;
            if (fullPath.Length > MaxOrbisPathLength) return false;
            return !OrbisTempRecovery.IsStagingArtifact(fullPath);
        }

        /// <summary>
        /// Decides the staging mode and applies it:
        ///  - fully safe path         -> Direct (no move, no rename, no p4t_v_*)
        ///  - safe parent, bad name   -> RenameInPlace
        ///  - unsafe parent           -> DriveRootStaging at the same drive's root
        /// A source that is already a staging/recovery PKG is REJECTED (never
        /// staged a second time - that is how packages ended up nested).
        /// </summary>
        public static OrbisSafePkgOperation Prepare(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("A package path is required.", nameof(packagePath));

            string originalPath = Path.GetFullPath(packagePath);
            if (!File.Exists(originalPath))
                throw new FileNotFoundException("The selected PKG was not found.", originalPath);

            string fileName = Path.GetFileName(originalPath);

            // Anti-nesting guard - must come before any staging decision.
            if (OrbisTempRecovery.IsStagingArtifact(originalPath))
            {
                string original = "";
                string? sidecar = Path.Combine(
                    Path.GetDirectoryName(originalPath) ?? "", OrbisTempRecovery.SidecarName);
                if (File.Exists(sidecar))
                {
                    try { original = File.ReadAllText(sidecar).Trim(); } catch { }
                }
                throw new InvalidOperationException(
                    "'" + fileName + "' is an internal staging/recovery PKG and cannot be staged again." +
                    (original.Length > 0 ? " It belongs to: " + original : "") +
                    " Use the app's startup recovery instead.");
            }

            string parentDir = Path.GetDirectoryName(originalPath) ?? "";
            bool parentSafe = parentDir.Length > 0
                && IsAsciiSafePath(parentDir)
                && parentDir.Length <= MaxStagedParentLength;

            // Mode A - everything safe: use the original path untouched.
            if (parentSafe && IsAsciiSafePath(fileName) && originalPath.Length <= MaxOrbisPathLength)
                return new OrbisSafePkgOperation(originalPath, originalPath, null, OrbisPkgStageMode.Direct);

            // Mode B - safe parent, unsafe (or over-long) file name: rename in place.
            if (parentSafe)
            {
                string tempPath = Path.Combine(parentDir,
                    OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
                // Required order: persist recovery metadata FIRST, then rename -
                // a crash between the two steps leaves the metadata behind and
                // the recovery scanner can restore the exact original name.
                WriteRecoveryMetadata(originalPath, tempPath, OrbisPkgStageMode.RenameInPlace);
                try
                {
                    File.Move(originalPath, tempPath);
                }
                catch
                {
                    // Rename failed - the metadata is now unused; remove it if safe.
                    try { File.Delete(tempPath + RecoverySidecarSuffix); } catch { }
                    throw;
                }
                RegisterActivePath(tempPath);
                return new OrbisSafePkgOperation(originalPath, tempPath, null, OrbisPkgStageMode.RenameInPlace);
            }

            // Mode C - unsafe parent: stage at the root of the same drive so the
            // temporary path itself stays ASCII and short. Same drive keeps
            // File.Move a rename instead of a huge cross-volume copy.
            string stagingRoot = StagingRootOverride ?? Path.GetPathRoot(originalPath) ?? Path.GetTempPath();
            string temporaryDirectory = Path.Combine(stagingRoot,
                OrbisTempRecovery.TempDirPrefix + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(temporaryDirectory);
            string safePackagePath = Path.Combine(temporaryDirectory,
                OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
            try
            {
                // Metadata first (owner + legacy sidecar), then the move - a
                // crash between the two steps is recoverable.
                WriteOwnerMetadata(temporaryDirectory, safePackagePath, originalPath);
                OrbisTempRecovery.MoveIntoOrbisTemp(originalPath, temporaryDirectory, safePackagePath);
                RegisterActivePath(safePackagePath);
                RegisterActivePath(temporaryDirectory);
                return new OrbisSafePkgOperation(
                    originalPath, safePackagePath, temporaryDirectory, OrbisPkgStageMode.DriveRootStaging);
            }
            catch
            {
                OrbisTempRecovery.DeleteOrbisTempDirSafe(safePackagePath); // a failed move must not leak the temp dir
                throw;
            }
        }

        /// <summary>Undoes the staging. Direct: no-op. Never overwrites an occupied
        /// original, never deletes a staging directory that still holds the PKG,
        /// and reports the recovery location on failure.</summary>
        public OrbisSafePkgRestoreResult Restore()
        {
            if (_restored)
                return new OrbisSafePkgRestoreResult { Succeeded = true };

            OrbisSafePkgRestoreResult result;
            switch (Mode)
            {
                case OrbisPkgStageMode.Direct:
                    _restored = true;
                    return new OrbisSafePkgRestoreResult { Succeeded = true };
                case OrbisPkgStageMode.RenameInPlace:
                    result = RestoreRenameInPlace();
                    break;
                default:
                    result = RestoreDriveRootStaging();
                    break;
            }

            // The operation is finished either way - drop the registry entries
            // so a later recovery scan can see any artifact a failed restore
            // left behind.
            if (IsStaged)
            {
                UnregisterActivePath(OrbisPath);
                if (!string.IsNullOrEmpty(TemporaryDirectory))
                    UnregisterActivePath(TemporaryDirectory);
            }
            return result;
        }

        private OrbisSafePkgRestoreResult RestoreRenameInPlace()
        {
            string recoveryDir = Path.GetDirectoryName(OriginalPath) ?? "";

            if (!File.Exists(OrbisPath))
            {
                if (File.Exists(OriginalPath))
                {
                    _restored = true;
                    return new OrbisSafePkgRestoreResult { Succeeded = true };
                }
                return Failure("The temporary and original package files are both missing.", recoveryDir);
            }

            if (File.Exists(OriginalPath))
            {
                // Never overwrite the original - preserve the temp file and report the conflict.
                return Failure(
                    "The original package path is occupied. The temporary rename was preserved at: " + OrbisPath,
                    recoveryDir);
            }

            try
            {
                File.Move(OrbisPath, OriginalPath);
            }
            catch (Exception ex)
            {
                return Failure(
                    "The package could not be restored to its original name. It remains at: " + OrbisPath +
                    " (" + ex.Message + ")", recoveryDir);
            }

            // Verify the exact original name now exists, then remove the crash
            // metadata. On any failure the temporary PKG and its metadata are
            // both preserved.
            if (!File.Exists(OriginalPath))
                return Failure(
                    "The rename-back did not produce the original package file. The temporary PKG and its " +
                    "recovery metadata were preserved at: " + OrbisPath, recoveryDir);

            _restored = true;
            try { File.Delete(OrbisPath + RecoverySidecarSuffix); } catch { }
            return new OrbisSafePkgRestoreResult { Succeeded = true };
        }

        private OrbisSafePkgRestoreResult RestoreDriveRootStaging()
        {
            string recoveryDir = TemporaryDirectory ?? "";

            if (!File.Exists(OrbisPath))
            {
                if (File.Exists(OriginalPath))
                {
                    _restored = true;
                    OrbisTempRecovery.DeleteOrbisTempDirSafe(OrbisPath);
                    return new OrbisSafePkgRestoreResult { Succeeded = true };
                }
                return Failure("The temporary and original package files are both missing.", recoveryDir);
            }

            if (File.Exists(OriginalPath))
            {
                // Never overwrite the original - preserve everything and report the conflict.
                return Failure(
                    "The original package path is occupied. The staged package and recovery metadata were preserved.",
                    recoveryDir);
            }

            try
            {
                // 1. move the staged PKG back
                File.Move(OrbisPath, OriginalPath);
            }
            catch (Exception ex)
            {
                return Failure("The package could not be restored to its original path. " + ex.Message, recoveryDir);
            }

            // 2. verify the original now exists
            if (!File.Exists(OriginalPath))
                return Failure(
                    "The move-back did not produce the original package file. The staged data was preserved in: " +
                    recoveryDir, recoveryDir);

            // 3. only now remove the sidecar + the (now PKG-free) staging directory.
            // DeleteOrbisTempDirSafe refuses to delete a directory that still holds a PKG.
            _restored = true;
            OrbisTempRecovery.DeleteOrbisTempDirSafe(OrbisPath);
            return new OrbisSafePkgRestoreResult { Succeeded = true };
        }

        /// <summary>
        /// Walks up from the given directory to the first ASCII-safe ancestor.
        /// Used for orbis OUTPUT paths (extraction workspaces), where moving
        /// to the drive root would not be appropriate.
        /// </summary>
        internal static string FindAsciiParentDirectory(string directory)
        {
            string candidate = Path.GetFullPath(directory);
            while (!string.IsNullOrEmpty(candidate) && !IsAsciiSafePath(candidate))
                candidate = Path.GetDirectoryName(candidate) ?? "";

            if (string.IsNullOrEmpty(candidate))
                candidate = Path.GetTempPath();

            return candidate;
        }

        private static OrbisSafePkgRestoreResult Failure(string message, string recoveryDirectory) => new()
        {
            Succeeded = false,
            ErrorMessage = message,
            RecoveryDirectory = recoveryDirectory,
        };
    }
}
