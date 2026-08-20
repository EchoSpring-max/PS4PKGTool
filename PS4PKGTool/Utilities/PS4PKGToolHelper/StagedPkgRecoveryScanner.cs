using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    public enum StagedPkgRecoveryType
    {
        /// <summary>Staged at the drive root by the current DriveRootStaging mode.</summary>
        DriveRootStaging,

        /// <summary>Renamed in place by the current RenameInPlace mode (has .recovery metadata).</summary>
        RenameInPlace,

        /// <summary>Older-version "p4t_v_*" directory found outside a drive root (legacy location).</summary>
        LegacyP4t,

        /// <summary>Standalone "ps4pkgtool_orbis_*.pkg" without any recovery metadata.</summary>
        LegacyStandalone,

        /// <summary>A MissingPkg recovery record confidently correlated with a
        /// standalone legacy staged PKG - one logical package, one combined row.</summary>
        LegacyRecoveryCandidate,
    }

    /// <summary>How confidently a legacy recovery record matches a standalone
    /// staged PKG. Only Exact and High may produce a combined recoverable item.</summary>
    public enum StagedPkgCorrelationConfidence
    {
        None,
        Ambiguous,
        High,
        Exact,
    }

    public enum StagedPkgRecoveryStatus
    {
        /// <summary>Exact original path is known and free - can be restored automatically.</summary>
        ReadyToRecover,

        /// <summary>The original path already exists - automatic recovery would overwrite it.</summary>
        Conflict,

        /// <summary>The original file name is not recorded anywhere - manual review only.</summary>
        UnknownOriginal,

        /// <summary>Recovery metadata exists but cannot be trusted.</summary>
        MalformedMetadata,

        /// <summary>The staged PKG itself is missing (only a leftover folder/metadata remains).</summary>
        MissingPkg,

        /// <summary>Owned by a process that is still running (another PS4PKGTool
        /// instance) - the operation may be live, so recovery is blocked.</summary>
        ActiveOperation,

        /// <summary>A legacy recovery record + standalone staged PKG were
        /// confidently matched; the recorded original path is known and free,
        /// so the pair can be restored as one logical package.</summary>
        MatchedLegacyResidue,
    }

    /// <summary>
    /// One discovered staged PKG leftover. Read-only snapshot produced by the
    /// recovery scanner; <see cref="StagedPkgRecoveryScanner.Recover"/> is the
    /// only place allowed to move or delete anything.
    /// </summary>
    public sealed class StagedPkgRecoveryItem
    {
        public string CurrentPath = "";
        public string OriginalPath = "";
        public StagedPkgRecoveryType RecoveryType;
        public StagedPkgRecoveryStatus Status;
        public string Title = "";
        public string TitleId = "";
        public string ContentId = "";
        public string PkgType = "";
        public long Size;
        public DateTime CreatedAtUtc;
        public bool CanAutoRecover => (Status == StagedPkgRecoveryStatus.ReadyToRecover
                || Status == StagedPkgRecoveryStatus.MatchedLegacyResidue)
            && !string.IsNullOrWhiteSpace(OriginalPath) && !string.IsNullOrWhiteSpace(CurrentPath);
        public string ConflictReason = "";
        /// <summary>The p4t_v_* directory when this item lives in one.</summary>
        public string? StagingDirectory;
        /// <summary>Legacy correlation: the p4t_v_* directory the recovery record lives in.</summary>
        public string? RecoveryRecordPath;
        /// <summary>Legacy correlation: the exact original path recorded by the recovery record.</summary>
        public string? RecordedOriginalPath;
        /// <summary>Legacy correlation: how confidently the record and the staged PKG match.</summary>
        public StagedPkgCorrelationConfidence CorrelationConfidence = StagedPkgCorrelationConfidence.None;
        /// <summary>Legacy correlation: human-readable description of the evidence used.</summary>
        public string CorrelationEvidence = "";
    }

    /// <summary>
    /// Dedicated recovery scanner for staged PKG leftovers - deliberately
    /// SEPARATE from the normal package enumeration, which skips staging
    /// artifacts. Finds:
    ///  - current DriveRootStaging leftovers at drive roots (sidecar original_path.txt)
    ///  - current RenameInPlace leftovers (JSON .recovery sidecar)
    ///  - legacy p4t_v_* directories at any depth under configured roots
    ///  - legacy standalone ps4pkgtool_orbis_*.pkg files without metadata
    /// Never touches files: scanning and metadata reading are read-only.
    /// </summary>
    public static class StagedPkgRecoveryScanner
    {
        /// <summary>Scans the given package roots and their drive roots for staging
        /// leftovers. Active operations are invisible to the scanner. Results are
        /// deduplicated by normalized current path.</summary>
        public static List<StagedPkgRecoveryItem> Scan(IEnumerable<string> packageRoots)
        {
            var items = new List<StagedPkgRecoveryItem>();
            var seenPkg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenDir = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var scanRoots = new List<string>();
            foreach (string root in packageRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                string full;
                try { full = Path.GetFullPath(root); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
                if (Directory.Exists(full) && !scanRoots.Contains(full, StringComparer.OrdinalIgnoreCase))
                    scanRoots.Add(full);
                // Drive root of each configured location - current DriveRootStaging
                // puts its p4t_v_* directly there.
                try
                {
                    string driveRoot = Path.GetPathRoot(full) ?? "";
                    if (driveRoot.Length > 0 && !scanRoots.Contains(driveRoot, StringComparer.OrdinalIgnoreCase))
                        scanRoots.Add(driveRoot);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
            }

            foreach (string root in scanRoots)
            {
                if (!Directory.Exists(root)) continue;
                bool isDriveRoot = string.Equals(Path.GetPathRoot(root), root, StringComparison.OrdinalIgnoreCase);

                // p4t_v_* staging directories.
                SearchOption dirDepth = isDriveRoot ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                foreach (string dir in EnumerateSafe(() => Directory.EnumerateDirectories(
                             root, OrbisTempRecovery.TempDirPrefix + "*", dirDepth)))
                {
                    if (OrbisSafePkgOperation.IsPathInActiveOperation(dir)) continue; // live operation - invisible
                    if (!seenDir.Add(Path.GetFullPath(dir))) continue;
                    AddP4tItem(dir, items, seenPkg);
                    DeleteAbandonedMetadataTmp(dir); // litter from a crashed atomic publish - never tied to a PKG decision
                }

                // Standalone ps4pkgtool_orbis_*.pkg renames (RenameInPlace or legacy orphans).
                SearchOption fileDepth = isDriveRoot ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                foreach (string file in EnumerateSafe(() => Directory.EnumerateFiles(
                             root, OrbisTempRecovery.TempPkgPattern, fileDepth)))
                {
                    string full = Path.GetFullPath(file);
                    if (OrbisSafePkgOperation.IsPathInActiveOperation(full)) continue;
                    if (OrbisTempRecovery.IsUnderTempDirectory(full)) continue; // handled by the p4t_v_ scan
                    if (!seenPkg.Add(full)) continue;
                    items.Add(BuildStandaloneItem(full));
                    DeleteAbandonedMetadataTmp(Path.GetDirectoryName(full));
                }

                // Sweep abandoned atomic-publish leftovers anywhere under
                // configured roots (a crash mid-write leaves "<final>.tmp"
                // before any PKG was renamed - pure litter, never tied to a
                // package decision).
                if (!isDriveRoot)
                    DeleteAbandonedMetadataTmpRecursive(root);
            }

            return CorrelateLegacyPairs(items)
                .OrderByDescending(i => i.CanAutoRecover)
                .ThenBy(i => i.CurrentPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── Legacy correlation ──────────────────────────────────────────────

        /// <summary>
        /// Correlates MissingPkg recovery records with standalone legacy
        /// orphans so ONE logical package is shown as ONE row instead of two.
        /// Conservative by design:
        ///  - Exact: the record's sidecar names a staged file whose name equals
        ///    the orphan's name (chain evidence) - the final original is walked
        ///    along the chain of records when possible.
        ///  - High: same original directory + matching Title ID + matching
        ///    package type + exactly one candidate (and no other record claims it).
        ///  - Ambiguous: multiple plausible candidates / shared orphans - never
        ///    auto-linked, the rows stay separate with an explanation.
        ///  - Title text alone is never sufficient evidence.
        /// </summary>
        private static List<StagedPkgRecoveryItem> CorrelateLegacyPairs(List<StagedPkgRecoveryItem> items)
        {
            var records = items
                .Where(i => i.Status == StagedPkgRecoveryStatus.MissingPkg
                    && i.StagingDirectory != null
                    && !string.IsNullOrWhiteSpace(i.RecordedOriginalPath))
                .ToList();
            var orphans = items
                .Where(i => i.RecoveryType == StagedPkgRecoveryType.LegacyStandalone)
                .ToList();

            if (records.Count == 0 || orphans.Count == 0)
                return items;

            // Records are resolved by their directory for chain walking.
            var recordByDir = records.ToDictionary(
                r => Path.GetFullPath(r.StagingDirectory!), StringComparer.OrdinalIgnoreCase);
            var claimedOrphans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var consumedRecords = new HashSet<StagedPkgRecoveryItem>();
            var combined = new List<StagedPkgRecoveryItem>();
            var ambiguousRecords = new HashSet<StagedPkgRecoveryItem>();

            // Phase 1 - Exact chain evidence: the record names a staged file
            // whose file name equals an orphan's name.
            foreach (StagedPkgRecoveryItem record in records)
            {
                string recorded = record.RecordedOriginalPath!;
                string recordedFileName = Path.GetFileName(recorded);
                if (!recordedFileName.StartsWith(OrbisTempRecovery.TempPkgNamePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                List<StagedPkgRecoveryItem> exactMatches = orphans
                    .Where(o => string.Equals(Path.GetFileName(o.CurrentPath), recordedFileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exactMatches.Count != 1 || claimedOrphans.Contains(exactMatches[0].CurrentPath))
                    continue;

                StagedPkgRecoveryItem orphan = exactMatches[0];
                string finalOriginal = ResolveChainOriginal(recorded, recordByDir);
                combined.Add(BuildCombinedItem(record, orphan, finalOriginal,
                    StagedPkgCorrelationConfidence.Exact,
                    "The recovery record names this staged file directly."));
                claimedOrphans.Add(orphan.CurrentPath);
                consumedRecords.Add(record);
            }

            // Phase 2 - High-confidence candidates: same original directory
            // AND/OR matching Title ID, matching type, uniqueness on BOTH
            // sides (one record <-> one orphan).
            var highCandidates = new Dictionary<string, List<(StagedPkgRecoveryItem Record, string Evidence)>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StagedPkgRecoveryItem record in records)
            {
                if (consumedRecords.Contains(record)) continue;
                string recorded = record.RecordedOriginalPath!;
                string originalDir = Path.GetDirectoryName(recorded) ?? "";
                string recordTitleId = DeriveTitleId(recorded);
                string recordType = DerivePkgTypeFromPath(recorded);
                var candidates = new List<(StagedPkgRecoveryItem Orphan, string Evidence)>();
                foreach (StagedPkgRecoveryItem orphan in orphans)
                {
                    if (claimedOrphans.Contains(orphan.CurrentPath)) continue;
                    if (string.Equals(Path.GetDirectoryName(orphan.CurrentPath) ?? "", originalDir, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add((orphan, "the staged package is in the recorded original directory"));
                        continue;
                    }
                    if (recordTitleId.Length > 0 && string.Equals(orphan.TitleId, recordTitleId, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add((orphan, "the staged package and the recorded path share Title ID " + recordTitleId));
                    }
                }

                // Type mismatch disqualifies a candidate unless exact evidence exists.
                if (recordType.Length > 0)
                {
                    candidates.RemoveAll(c =>
                        c.Orphan.PkgType.Length > 0 && !string.Equals(
                            NormalizePkgType(c.Orphan.PkgType), recordType, StringComparison.OrdinalIgnoreCase));
                }

                if (candidates.Count == 1)
                {
                    string key = candidates[0].Orphan.CurrentPath;
                    if (!highCandidates.TryGetValue(key, out var list))
                    {
                        list = new List<(StagedPkgRecoveryItem, string)>();
                        highCandidates[key] = list;
                    }
                    list.Add((record, candidates[0].Evidence));
                }
                else if (candidates.Count > 1)
                {
                    // More than one plausible orphan for this record - never auto-link.
                    record.CorrelationConfidence = StagedPkgCorrelationConfidence.Ambiguous;
                    record.CorrelationEvidence =
                        "More than one staged package could belong to this recovery record. PS4 PKG Tool will not choose automatically.";
                    ambiguousRecords.Add(record);
                }
            }

            // A High link requires uniqueness on BOTH sides: exactly one
            // record per orphan and exactly one orphan per record.
            foreach ((string orphanPath, List<(StagedPkgRecoveryItem Record, string Evidence)> claims) in highCandidates)
            {
                if (claims.Count != 1) continue;
                StagedPkgRecoveryItem record = claims[0].Record;
                if (ambiguousRecords.Contains(record)) continue;
                StagedPkgRecoveryItem orphan = orphans.First(o =>
                    string.Equals(o.CurrentPath, orphanPath, StringComparison.OrdinalIgnoreCase));
                combined.Add(BuildCombinedItem(record, orphan, record.RecordedOriginalPath!,
                    StagedPkgCorrelationConfidence.High, claims[0].Evidence));
                claimedOrphans.Add(orphan.CurrentPath);
                consumedRecords.Add(record);
            }

            // One orphan claimed by multiple records with only weak evidence:
            // all those records stay ambiguous, nothing auto-links.
            foreach ((string orphanPath, List<(StagedPkgRecoveryItem Record, string Evidence)> claims) in highCandidates)
            {
                if (claims.Count <= 1 || claimedOrphans.Contains(orphanPath)) continue;
                foreach ((StagedPkgRecoveryItem record, _) in claims)
                {
                    if (consumedRecords.Contains(record)) continue;
                    record.CorrelationConfidence = StagedPkgCorrelationConfidence.Ambiguous;
                    record.CorrelationEvidence =
                        "More than one recovery record could own this staged package. PS4 PKG Tool will not choose automatically.";
                }
            }

            if (consumedRecords.Count == 0)
                return items;

            var result = new List<StagedPkgRecoveryItem>();
            result.AddRange(combined);
            result.AddRange(items.Where(i => !consumedRecords.Contains(i) && !claimedOrphans.Contains(i.CurrentPath)));
            return result;
        }

        /// <summary>
        /// Walks the chain of recovery records from a recorded staged path to
        /// the outermost recorded original. Capped and cycle-safe - never
        /// loops on a circular sidecar chain.
        /// </summary>
        private static string ResolveChainOriginal(string recordedPath,
            Dictionary<string, StagedPkgRecoveryItem> recordByDir)
        {
            string current = recordedPath;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int depth = 0; depth < 10; depth++)
            {
                if (!visited.Add(current)) break;
                string dir = Path.GetDirectoryName(current) ?? "";
                if (string.IsNullOrEmpty(dir) || !recordByDir.TryGetValue(dir, out StagedPkgRecoveryItem? record))
                    return current;
                string next = record.RecordedOriginalPath ?? "";
                if (string.IsNullOrWhiteSpace(next)) return current;
                current = next;
            }
            return current;
        }

        private static StagedPkgRecoveryItem BuildCombinedItem(StagedPkgRecoveryItem record,
            StagedPkgRecoveryItem orphan, string finalOriginal, StagedPkgCorrelationConfidence confidence,
            string evidence)
        {
            var item = new StagedPkgRecoveryItem
            {
                RecoveryType = StagedPkgRecoveryType.LegacyRecoveryCandidate,
                CurrentPath = orphan.CurrentPath,
                OriginalPath = finalOriginal,
                RecoveryRecordPath = record.StagingDirectory,
                RecordedOriginalPath = finalOriginal,
                StagingDirectory = record.StagingDirectory,
                CorrelationConfidence = confidence,
                CorrelationEvidence = evidence,
                Title = orphan.Title,
                TitleId = orphan.TitleId,
                ContentId = orphan.ContentId,
                PkgType = orphan.PkgType,
                Size = orphan.Size,
                CreatedAtUtc = orphan.CreatedAtUtc,
            };

            if (string.IsNullOrWhiteSpace(finalOriginal) || string.Equals(
                    Path.GetFullPath(finalOriginal), Path.GetFullPath(orphan.CurrentPath), StringComparison.OrdinalIgnoreCase))
            {
                // The staged package is already at its recorded location - only
                // the stale record remains. Not auto-recoverable, but removable.
                item.Status = StagedPkgRecoveryStatus.MissingPkg;
                item.ConflictReason = "The staged package is already at its recorded location; only the stale recovery record remains.";
                return item;
            }

            if (File.Exists(finalOriginal))
            {
                item.Status = StagedPkgRecoveryStatus.Conflict;
                item.ConflictReason = "The recorded original path already exists:\n" + finalOriginal;
                return item;
            }

            item.Status = StagedPkgRecoveryStatus.MatchedLegacyResidue;
            return item;
        }

        /// <summary>Extracts the PKG type signal from a recorded original path
        /// ("00 - Base.pkg" -> base, "01 - Update ...pkg" -> update). Empty when
        /// the path carries no usable signal.</summary>
        internal static string DerivePkgTypeFromPath(string path)
        {
            string name = Path.GetFileName(path);
            if (name.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Patch", StringComparison.OrdinalIgnoreCase))
                return "update";
            if (name.Contains(" - Base", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Base.pkg", StringComparison.OrdinalIgnoreCase))
                return "base";
            return "";
        }

        private static string NormalizePkgType(string pkgType)
        {
            if (pkgType.Equals("GAME", StringComparison.OrdinalIgnoreCase)
                || pkgType.Equals("BASE", StringComparison.OrdinalIgnoreCase))
                return "base";
            if (pkgType.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
                || pkgType.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)
                || pkgType.Equals("GP", StringComparison.OrdinalIgnoreCase))
                return "update";
            return pkgType;
        }

        /// <summary>
        /// Restores one item to its exact original path. Strict guards:
        /// never overwrites an existing original, never acts on active
        /// operations, verifies the move result before removing any metadata
        /// or staging directory, and leaves everything behind on failure.
        /// </summary>
        public static (bool Recovered, string Error) Recover(StagedPkgRecoveryItem item)
        {
            if (item == null)
                return (false, "No staged package selected.");

            if (!item.CanAutoRecover)
                return (false, "This staged package cannot be recovered automatically: " +
                    (item.ConflictReason.Length > 0 ? item.ConflictReason : item.Status.ToString()) + ".");

            if (!File.Exists(item.CurrentPath))
                return (false, "The staged package no longer exists at: " + item.CurrentPath);

            if (OrbisSafePkgOperation.IsPathInActiveOperation(item.CurrentPath))
                return (false, "The staged package is in use by an active operation.");

            if (File.Exists(item.OriginalPath))
                return (false, "The original path already exists - recovery would overwrite it:\n" + item.OriginalPath);

            try
            {
                // The original folder may have been deleted since the crash.
                string parent = Path.GetDirectoryName(item.OriginalPath) ?? "";
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);

                // 1. move the staged PKG back to its exact original path
                File.Move(item.CurrentPath, item.OriginalPath);

                // 2. verify the original now exists
                if (!File.Exists(item.OriginalPath))
                    return (false, "The move did not produce the original package file. Nothing was deleted.");

                // 3. only now remove recovery metadata / the staging directory
                if (item.RecoveryType == StagedPkgRecoveryType.RenameInPlace)
                {
                    try { File.Delete(item.CurrentPath + OrbisSafePkgOperation.RecoverySidecarSuffix); } catch { }
                }
                else if (item.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate)
                {
                    // The recovered source was a standalone orphan; the stale
                    // record directory is removed only when it holds known
                    // metadata and no PKG (never unknown content).
                    if (!string.IsNullOrEmpty(item.RecoveryRecordPath))
                        TryRemoveStaleRecoveryDirectory(item.RecoveryRecordPath);
                }
                else
                {
                    // DeleteOrbisTempDirSafe refuses to delete a directory that
                    // still holds a PKG - after the successful move it holds at
                    // most the sidecar, which goes with it.
                    OrbisTempRecovery.DeleteOrbisTempDirSafe(item.CurrentPath);
                }
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, "Recovery failed: " + ex.Message +
                    "\nThe staged package and its recovery metadata were preserved.");
            }
        }

        /// <summary>Result of a batch recovery run.</summary>
        public sealed class StagedPkgRecoveryBatchResult
        {
            public int Recovered;
            public int Failed;
            /// <summary>How many scan/recover passes ran (nested chains unwind
            /// one level per pass until nothing new is recoverable).</summary>
            public int Passes;
            public readonly List<string> Failures = new();
        }

        /// <summary>
        /// ONE "Recover All Safe" action that automatically unwinds nested
        /// legacy staging chains: repeat scan -> recover everything safe ->
        /// re-scan until nothing new is recoverable. Guards: a maximum of
        /// <see cref="MaxRecoveryPasses"/> passes, an immediate stop when a
        /// pass makes zero progress, and deduplication of already-processed
        /// paths - it can never loop forever. Conflicts, malformed metadata,
        /// unknown originals, active items and failed recoveries only stop
        /// the affected item, never overwrite or delete anything.
        /// </summary>
        public static StagedPkgRecoveryBatchResult RecoverAllSafe(IEnumerable<string> packageRoots)
        {
            var result = new StagedPkgRecoveryBatchResult();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int pass = 1; pass <= MaxRecoveryPasses; pass++)
            {
                var safe = new List<StagedPkgRecoveryItem>();
                foreach (StagedPkgRecoveryItem item in Scan(packageRoots))
                {
                    if (item.CanAutoRecover && processed.Add(item.CurrentPath))
                        safe.Add(item);
                }

                if (safe.Count == 0)
                    break; // nothing new is recoverable - the chain is fully unwound (or blocked)

                int passRecovered = 0;
                foreach (StagedPkgRecoveryItem item in safe)
                {
                    (bool ok, string error) = Recover(item);
                    if (ok)
                    {
                        result.Recovered++;
                        passRecovered++;
                        Logger.LogInformation($"Staged PKG recovery: restored {Path.GetFileName(item.OriginalPath)} to {Path.GetDirectoryName(item.OriginalPath)}");
                    }
                    else
                    {
                        result.Failed++;
                        if (result.Failures.Count < 20)
                            result.Failures.Add(Path.GetFileName(item.CurrentPath) + ": " + error);
                        Logger.LogWarning("Staged PKG recovery failed: " + error);
                    }
                }

                result.Passes = pass;
                if (passRecovered == 0)
                    break; // zero-progress guard - do not loop forever
            }
            return result;
        }

        /// <summary>Hard ceiling on recovery passes - protects against
        /// pathological chains (e.g. 100-deep nested p4t_v_* directories).</summary>
        public const int MaxRecoveryPasses = 20;

        /// <summary>Files a staging/recovery directory may legitimately hold:
        /// known metadata and known extraction scratch. Anything else blocks
        /// automatic removal.</summary>
        private static readonly HashSet<string> KnownRecoveryFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            OrbisTempRecovery.SidecarName,          // original_path.txt
            OrbisSafePkgOperation.OwnerSidecarName, // owner.json
            "npbind.dat",
            "changeinfo.xml",
        };

        /// <summary>True when the directory is a p4t_v_* staging directory that
        /// holds NO pkg, NO subdirectories and only known recovery/scratch
        /// files - i.e. safe to remove as a stale recovery record. Never
        /// recursive over unknown content.</summary>
        internal static bool IsStaleRecordRemovable(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
            if (!Path.GetFileName(directory).StartsWith(
                    OrbisTempRecovery.TempDirPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            if (Directory.EnumerateFiles(directory, "*.pkg", SearchOption.TopDirectoryOnly).Any())
                return false; // never delete a directory containing any pkg
            if (Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Any())
                return false; // nested content (e.g. a legacy chain level) is not ours to remove
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (KnownRecoveryFiles.Contains(name)) continue;
                if (name.EndsWith(OrbisSafePkgOperation.RecoverySidecarSuffix, StringComparison.OrdinalIgnoreCase)) continue;          // *.recovery
                if (name.EndsWith(OrbisSafePkgOperation.RecoverySidecarSuffix + ".tmp", StringComparison.OrdinalIgnoreCase)) continue; // *.recovery.tmp
                if (name.Equals(OrbisSafePkgOperation.OwnerSidecarName + ".tmp", StringComparison.OrdinalIgnoreCase)) continue;         // owner.json.tmp
                return false; // unknown user file - preserve the directory
            }
            return true;
        }

        /// <summary>Removes a stale recovery directory only when it holds known
        /// metadata/scratch files and no package. Never deletes unknown content.</summary>
        internal static (bool Removed, string Reason) TryRemoveStaleRecoveryDirectory(string directory)
        {
            if (!IsStaleRecordRemovable(directory))
                return (false, "The directory contains a package, unknown files, or subdirectories and was preserved.");
            try
            {
                Directory.Delete(directory, true);
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, "Could not remove the directory: " + ex.Message);
            }
        }

        // ── Item builders ───────────────────────────────────────────────────

        /// <summary>Owner metadata is live when the recorded PID still runs AND
        /// its start time matches (a reused PID with a different start time
        /// counts as dead, so stale metadata never blocks recovery).</summary>
        private static bool OwnerIsAlive(StagedPkgRecoveryMetadata metadata)
        {
            if (metadata.OwnerProcessId <= 0) return false;
            if (!DateTime.TryParse(metadata.OwnerProcessStartTimeUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime start))
            {
                // Unparseable start time: fall back to the PID-only check
                // (a running PID without a matching start time is treated as
                // dead, so this errs toward recoverable, never toward blocking).
                return OrbisSafePkgOperation.IsOwnerProcessAlive(metadata.OwnerProcessId, DateTime.MinValue);
            }
            return OrbisSafePkgOperation.IsOwnerProcessAlive(metadata.OwnerProcessId, start);
        }

        /// <summary>Removes obviously abandoned atomic-publish leftovers
        /// ("*.pkg.recovery.tmp" / "owner.json.tmp" older than a day). A
        /// crash mid-write leaves these BEFORE any PKG rename, so the staged
        /// PKG never depends on them - this is pure litter collection, never
        /// tied to a package decision.</summary>
        internal static void DeleteAbandonedMetadataTmp(string? directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            DateTime threshold = DateTime.UtcNow.AddDays(-1);
            foreach (string file in EnumerateSafe(() => Directory.EnumerateFiles(
                         directory, "*" + OrbisSafePkgOperation.RecoverySidecarSuffix + ".tmp",
                         SearchOption.TopDirectoryOnly)))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < threshold)
                        File.Delete(file);
                }
                catch { }
            }
        }

        /// <summary>Recursive variant for configured package roots.</summary>
        private static void DeleteAbandonedMetadataTmpRecursive(string root)
        {
            DateTime threshold = DateTime.UtcNow.AddDays(-1);
            foreach (string file in EnumerateSafe(() => Directory.EnumerateFiles(
                         root, "*" + OrbisSafePkgOperation.RecoverySidecarSuffix + ".tmp",
                         SearchOption.AllDirectories)))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < threshold)
                        File.Delete(file);
                }
                catch { }
            }
        }

        private static void AddP4tItem(string dir, List<StagedPkgRecoveryItem> items,
            HashSet<string> seenPkg)
        {
            // A staging directory whose PARENT is the drive root comes from the
            // current DriveRootStaging mode; anything deeper is a legacy location.
            bool atDriveRoot = string.Equals(
                Path.GetDirectoryName(dir) ?? "", Path.GetPathRoot(dir), StringComparison.OrdinalIgnoreCase);

            var item = new StagedPkgRecoveryItem
            {
                StagingDirectory = dir,
                RecoveryType = atDriveRoot ? StagedPkgRecoveryType.DriveRootStaging : StagedPkgRecoveryType.LegacyP4t,
            };

            string[] pkgs = Directory.GetFiles(dir, OrbisTempRecovery.TempPkgPattern, SearchOption.TopDirectoryOnly);
            if (pkgs.Length == 0)
            {
                item.Status = StagedPkgRecoveryStatus.MissingPkg;
                item.ConflictReason = "The staging directory no longer contains a package.";
                item.RecoveryRecordPath = dir;
                // The record may still carry the recorded original path - keep it
                // so the legacy correlation pass can match a standalone orphan.
                string missingSidecar = Path.Combine(dir, OrbisTempRecovery.SidecarName);
                if (File.Exists(missingSidecar))
                {
                    try { item.RecordedOriginalPath = File.ReadAllText(missingSidecar).Trim(); } catch { }
                }
                if (string.IsNullOrWhiteSpace(item.RecordedOriginalPath))
                    item.ConflictReason = "No recovery metadata - the original path is unknown.";
                items.Add(item);
                return;
            }

            string pkg = pkgs[0];
            item.CurrentPath = pkg;
            item.Size = FileSize(pkg);
            item.CreatedAtUtc = SafeCreationTime(pkg);
            seenPkg.Add(Path.GetFullPath(pkg));

            // Cross-process protection: the owner.json written by the staging
            // operation records which process created this directory. A live
            // owner means the operation may still be running - block recovery.
            string ownerSidecar = Path.Combine(dir, OrbisSafePkgOperation.OwnerSidecarName);
            StagedPkgRecoveryMetadata? owner = File.Exists(ownerSidecar)
                ? OrbisSafePkgOperation.ReadRecoveryMetadata(ownerSidecar) : null;
            if (owner != null && OwnerIsAlive(owner))
            {
                item.Status = StagedPkgRecoveryStatus.ActiveOperation;
                item.ConflictReason = "This staging directory belongs to a running PS4PKGTool operation.";
                items.Add(item);
                return;
            }

            string sidecar = Path.Combine(dir, OrbisTempRecovery.SidecarName);
            if (!File.Exists(sidecar))
            {
                item.Status = StagedPkgRecoveryStatus.UnknownOriginal;
                item.ConflictReason = "No recovery metadata - the original path is unknown.";
                FillOrphanMetadata(item);
                items.Add(item);
                return;
            }

            string original;
            try { original = File.ReadAllText(sidecar).Trim(); }
            catch { original = ""; }

            if (string.IsNullOrWhiteSpace(original))
            {
                item.Status = StagedPkgRecoveryStatus.MalformedMetadata;
                item.ConflictReason = "The recovery metadata is empty.";
                FillOrphanMetadata(item);
                items.Add(item);
                return;
            }

            item.OriginalPath = original;
            if (File.Exists(original))
            {
                item.Status = StagedPkgRecoveryStatus.Conflict;
                item.ConflictReason = "The original path already exists:\n" + original;
            }
            else
            {
                item.Status = StagedPkgRecoveryStatus.ReadyToRecover;
            }
            items.Add(item);
        }

        private static StagedPkgRecoveryItem BuildStandaloneItem(string file)
        {
            var item = new StagedPkgRecoveryItem
            {
                CurrentPath = file,
                Size = FileSize(file),
                CreatedAtUtc = SafeCreationTime(file),
            };

            string sidecar = file + OrbisSafePkgOperation.RecoverySidecarSuffix;
            if (File.Exists(sidecar))
            {
                StagedPkgRecoveryMetadata? metadata = OrbisSafePkgOperation.ReadRecoveryMetadata(sidecar);
                if (metadata != null && !string.IsNullOrWhiteSpace(metadata.OriginalFullPath))
                {
                    item.RecoveryType = StagedPkgRecoveryType.RenameInPlace;
                    item.OriginalPath = metadata.OriginalFullPath;
                    item.CreatedAtUtc = DateTime.TryParse(metadata.CreatedAtUtc, out DateTime created)
                        ? created : item.CreatedAtUtc;
                    // Cross-process protection: a live owner blocks recovery.
                    if (OwnerIsAlive(metadata))
                    {
                        item.Status = StagedPkgRecoveryStatus.ActiveOperation;
                        item.ConflictReason = "This rename belongs to a running PS4PKGTool operation.";
                        return item;
                    }
                    if (File.Exists(item.OriginalPath))
                    {
                        item.Status = StagedPkgRecoveryStatus.Conflict;
                        item.ConflictReason = "The original path already exists:\n" + item.OriginalPath;
                    }
                    else
                    {
                        item.Status = StagedPkgRecoveryStatus.ReadyToRecover;
                    }
                    return item;
                }

                item.RecoveryType = StagedPkgRecoveryType.RenameInPlace;
                item.Status = StagedPkgRecoveryStatus.MalformedMetadata;
                item.ConflictReason = "The recovery metadata is unreadable.";
                return item;
            }

            // No metadata at all - a legacy orphan. Never guess the name.
            item.RecoveryType = StagedPkgRecoveryType.LegacyStandalone;
            item.Status = StagedPkgRecoveryStatus.UnknownOriginal;
            item.ConflictReason = "Original filename unavailable - manual review required.";
            FillOrphanMetadata(item);
            return item;
        }

        /// <summary>Test seam: overrides the read-only orphan metadata lookup
        /// (Title/TitleId/ContentId/PkgType). Production uses Read_PKG.</summary>
        internal static Func<string, (string Title, string TitleId, string ContentId, string PkgType)>?
            OrphanMetadataReader = null;

        /// <summary>Read-only PKG metadata for orphans (title / IDs / type) so the
        /// dialog can show what the package IS without touching it.</summary>
        private static void FillOrphanMetadata(StagedPkgRecoveryItem item)
        {
            if (OrphanMetadataReader != null)
            {
                try
                {
                    (item.Title, item.TitleId, item.ContentId, item.PkgType) =
                        OrphanMetadataReader(item.CurrentPath);
                }
                catch { }
                return;
            }
            try
            {
                var pkg = PS4_Tools.PKG.SceneRelated.Read_PKG(item.CurrentPath);
                item.Title = pkg.PS4_Title ?? "";
                item.ContentId = pkg.Content_ID ?? "";
                item.PkgType = pkg.PKG_Type.ToString();
                item.TitleId = DeriveTitleId(item.ContentId);
            }
            catch
            {
                // Best effort - an unreadable orphan still shows its path.
            }
        }

        /// <summary>Sony content IDs and file paths embed the title ID
        /// ("UP9000-CUSA00900_00-...", "Game [CUSA12085] 00 - Base.pkg").
        /// The value is tokenized on every non-alphanumeric boundary so
        /// bracketed or spaced paths still resolve.</summary>
        internal static string DeriveTitleId(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId)) return "";
            foreach (string token in contentId.Split(
                         new[] { '-', '_', ' ', '[', ']', '(', ')', '.', '\\', '/', ';', ',' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string s = new string(token.Where(char.IsLetterOrDigit).ToArray());
                if (s.Length == 9 && s.StartsWith("CUSA", StringComparison.OrdinalIgnoreCase)
                    && s[4..].All(char.IsDigit))
                    return s.ToUpperInvariant();
            }
            return "";
        }

        private static long FileSize(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        private static DateTime SafeCreationTime(string path)
        {
            try { return File.GetCreationTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }

        private static IEnumerable<string> EnumerateSafe(Func<IEnumerable<string>> enumerate)
        {
            try { return enumerate(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
        }
    }
}
