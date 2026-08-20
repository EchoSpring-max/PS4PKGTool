using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System.Diagnostics;
using System.Text.Json;

namespace PS4PKGTool.Tests;

/// <summary>
/// Tests for the dedicated staged-PKG recovery scanner: crash residues of
/// RenameInPlace and DriveRootStaging, legacy nested p4t_v_* directories,
/// standalone orphans without metadata, active-operation invisibility and
/// the never-overwrite conflict rules.
/// </summary>
[TestClass]
public sealed class StagedPkgRecoveryTests
{
    [TestMethod]
    public void RenameInPlaceCrashResidue_IsDetectedAndRecoveredWithExactName()
    {
        using var fixture = new RecoveryFixture("safe parent", "DRIVECLUB™.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);

        // Simulate a crash: metadata written first, then the rename - and no
        // Restore() ever runs (the process died). The dead PID makes the
        // owner check classify the residue as abandoned.
        string tempPath = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        WriteOwnerSidecar(tempPath, fixture.PackagePath, 9999999, DateTime.UtcNow.AddHours(-1));
        File.Move(fixture.PackagePath, tempPath);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        StagedPkgRecoveryItem item = items[0];
        Assert.AreEqual(StagedPkgRecoveryType.RenameInPlace, item.RecoveryType);
        Assert.AreEqual(StagedPkgRecoveryStatus.ReadyToRecover, item.Status);
        Assert.AreEqual(fixture.PackagePath, item.OriginalPath);
        Assert.IsTrue(item.CanAutoRecover);

        (bool ok, string error) = StagedPkgRecoveryScanner.Recover(item);
        Assert.IsTrue(ok, error);

        // Exact original filename restored, metadata and temp name gone.
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.IsFalse(File.Exists(tempPath));
        Assert.IsFalse(File.Exists(tempPath + OrbisSafePkgOperation.RecoverySidecarSuffix));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
    }

    [TestMethod]
    public void RenameInPlaceCrashResidue_OriginalOccupied_IsConflictAndNeverOverwrites()
    {
        using var fixture = new RecoveryFixture("safe parent", "conflict Ω.pkg");
        string tempPath = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        WriteOwnerSidecar(tempPath, fixture.PackagePath, 9999999, DateTime.UtcNow.AddHours(-1));
        File.Move(fixture.PackagePath, tempPath);
        File.WriteAllText(fixture.PackagePath, "occupied by someone else");

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        StagedPkgRecoveryItem item = items[0];
        Assert.AreEqual(StagedPkgRecoveryStatus.Conflict, item.Status);
        Assert.IsFalse(item.CanAutoRecover);

        // Recovery must refuse, overwrite nothing, delete nothing.
        (bool ok, _) = StagedPkgRecoveryScanner.Recover(item);
        Assert.IsFalse(ok);
        Assert.AreEqual("occupied by someone else", File.ReadAllText(fixture.PackagePath));
        Assert.IsTrue(File.Exists(tempPath));
        Assert.IsTrue(File.Exists(tempPath + OrbisSafePkgOperation.RecoverySidecarSuffix));
    }

    [TestMethod]
    public void AbandonedStagingDirectory_IsRecoveredAndRemoved()
    {
        string stagingRoot = Path.Combine(Path.GetTempPath(),
            "p4t-recover-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            using var fixture = new RecoveryFixture("ゲーム", "Driveclub.pkg");
            byte[] expected = File.ReadAllBytes(fixture.PackagePath);

            // Residue of a crashed DriveRootStaging operation: staging dir with
            // sidecar + moved PKG, no Restore() ever ran.
            string stagingDir = Path.Combine(stagingRoot,
                OrbisTempRecovery.TempDirPrefix + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(stagingDir);
            string stagedPkg = Path.Combine(stagingDir,
                OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
            File.WriteAllText(Path.Combine(stagingDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);
            File.Move(fixture.PackagePath, stagedPkg);

            List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { stagingRoot });

            Assert.AreEqual(1, items.Count);
            StagedPkgRecoveryItem item = items[0];
            Assert.AreEqual(StagedPkgRecoveryStatus.ReadyToRecover, item.Status);
            Assert.AreEqual(fixture.PackagePath, item.OriginalPath);

            (bool ok, string error) = StagedPkgRecoveryScanner.Recover(item);
            Assert.IsTrue(ok, error);

            Assert.IsTrue(File.Exists(fixture.PackagePath));
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
            Assert.IsFalse(Directory.Exists(stagingDir)); // p4t_v_* removed after successful restore
        }
        finally
        {
            try { Directory.Delete(stagingRoot, true); } catch { }
        }
    }

    [TestMethod]
    public void LegacyNestedP4tDirectory_IsFoundThroughConfiguredRoots()
    {
        using var fixture = new RecoveryFixture("Some Game", "game.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);

        // Old-version residue: p4t_v_* nested INSIDE the game folder.
        string legacyDir = Path.Combine(fixture.PackageDirectory, OrbisTempRecovery.TempDirPrefix + "old");
        Directory.CreateDirectory(legacyDir);
        string stagedPkg = Path.Combine(legacyDir,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.WriteAllText(Path.Combine(legacyDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);
        File.Move(fixture.PackagePath, stagedPkg);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        StagedPkgRecoveryItem item = items[0];
        Assert.AreEqual(StagedPkgRecoveryType.LegacyP4t, item.RecoveryType);
        Assert.AreEqual(StagedPkgRecoveryStatus.ReadyToRecover, item.Status);

        (bool ok, string error) = StagedPkgRecoveryScanner.Recover(item);
        Assert.IsTrue(ok, error);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsFalse(Directory.Exists(legacyDir));
    }

    [TestMethod]
    public void LegacyStandaloneOrphan_IsDetectedButManualOnly()
    {
        using var fixture = new RecoveryFixture("Some Game", "game.pkg");
        string orphan = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphan); // no metadata at all

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        StagedPkgRecoveryItem item = items[0];
        Assert.AreEqual(StagedPkgRecoveryType.LegacyStandalone, item.RecoveryType);
        Assert.AreEqual(StagedPkgRecoveryStatus.UnknownOriginal, item.Status);
        Assert.IsFalse(item.CanAutoRecover);
        Assert.IsTrue(string.IsNullOrEmpty(item.OriginalPath));

        // Never auto-recover, never rename, never delete.
        (bool ok, _) = StagedPkgRecoveryScanner.Recover(item);
        Assert.IsFalse(ok);
        Assert.IsTrue(File.Exists(orphan));
    }

    [TestMethod]
    public void ActiveOperation_IsInvisibleToTheRecoveryScanner()
    {
        using var fixture = new RecoveryFixture("safe parent", "active Ω.pkg");
        OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
        try
        {
            Assert.AreEqual(OrbisPkgStageMode.RenameInPlace, operation.Mode);
            Assert.IsTrue(File.Exists(operation.OrbisPath));

            // The live operation must not appear as an abandoned artifact.
            List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });
            Assert.AreEqual(0, items.Count);
        }
        finally
        {
            Assert.IsTrue(operation.Restore().Succeeded);
        }

        // After a clean restore nothing remains either.
        Assert.AreEqual(0, StagedPkgRecoveryScanner.Scan(new[] { fixture.Root }).Count);
    }

    [TestMethod]
    public void NormalScannerExclusion_StillSkipsStagingArtifacts()
    {
        Assert.IsTrue(OrbisTempRecovery.IsStagingArtifact(@"B:\p4t_v_abc\ps4pkgtool_orbis_x.pkg"));
        Assert.IsTrue(OrbisTempRecovery.IsStagingArtifact(@"B:\PKG\Game\p4t_v_abc\game.pkg"));
        Assert.IsTrue(OrbisTempRecovery.IsStagingArtifact(@"B:\PKG\ps4pkgtool_orbis_0123456789abcdef0123456789abcdef.pkg"));
        Assert.IsFalse(OrbisTempRecovery.IsStagingArtifact(@"B:\PKG\Game\game.pkg"));
        // The RenameInPlace crash sidecar is NOT a .pkg - normal scanners never see it.
        Assert.IsFalse(OrbisTempRecovery.IsStagingArtifact(
            @"B:\PKG\ps4pkgtool_orbis_x.pkg.recovery"));
    }

    [TestMethod]
    public void TitleIdDerivation_ExtractsCusaFromContentId()
    {
        Assert.AreEqual("CUSA00900", StagedPkgRecoveryScanner.DeriveTitleId("UP9000-CUSA00900_00-BLOODBORNE000000-A0109-V0100"));
        Assert.AreEqual("", StagedPkgRecoveryScanner.DeriveTitleId(""));
        Assert.AreEqual("", StagedPkgRecoveryScanner.DeriveTitleId("no-title-id-here"));
    }

    // ── Nested legacy chain unwinding ──────────────────────────────────────

    [TestMethod]
    public void RecoverAllSafe_TwoLevelChain_UnwindsFullyInOneCall()
    {
        using var fixture = new RecoveryFixture("Game", "game.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);
        BuildLegacyChain(fixture, 2);

        StagedPkgRecoveryScanner.StagedPkgRecoveryBatchResult batch =
            StagedPkgRecoveryScanner.RecoverAllSafe(new[] { fixture.Root });

        Assert.AreEqual(2, batch.Recovered, string.Join(" | ", batch.Failures));
        Assert.AreEqual(0, batch.Failed);
        Assert.IsTrue(batch.Passes >= 2); // one level per pass
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
    }

    [TestMethod]
    public void RecoverAllSafe_ThreeLevelChain_UnwindsFullyInOneCall()
    {
        using var fixture = new RecoveryFixture("Game", "game.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);
        BuildLegacyChain(fixture, 3);

        StagedPkgRecoveryScanner.StagedPkgRecoveryBatchResult batch =
            StagedPkgRecoveryScanner.RecoverAllSafe(new[] { fixture.Root });

        Assert.AreEqual(3, batch.Recovered, string.Join(" | ", batch.Failures));
        Assert.AreEqual(0, batch.Failed);
        Assert.IsTrue(batch.Passes >= 3);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
    }

    [TestMethod]
    public void RecoverAllSafe_MidChainConflict_StopsOnlyTheAffectedChain()
    {
        using var fixture = new RecoveryFixture("Game", "game.pkg");
        // Clean 2-level chain that must fully unwind...
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);
        BuildLegacyChain(fixture, 2);

        // ...and a separate conflicted chain whose inner level's original is occupied.
        string conflictOriginal = Path.Combine(fixture.Root, "occupied target.pkg");
        File.WriteAllText(conflictOriginal, "occupied");
        string outerDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "outer");
        Directory.CreateDirectory(outerDir);
        string outerStaged = Path.Combine(outerDir,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.WriteAllText(Path.Combine(outerDir, OrbisTempRecovery.SidecarName), conflictOriginal);
        string innerDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "inner");
        Directory.CreateDirectory(innerDir);
        string innerStaged = Path.Combine(innerDir,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.WriteAllText(Path.Combine(innerDir, OrbisTempRecovery.SidecarName), outerStaged);
        File.WriteAllText(innerStaged, "conflicted pkg");

        StagedPkgRecoveryScanner.StagedPkgRecoveryBatchResult batch =
            StagedPkgRecoveryScanner.RecoverAllSafe(new[] { fixture.Root });

        // The clean chain (2 levels) and the conflicted chain's INNER level
        // (whose target path was free) are recovered; only the conflicted
        // outer level stops - and it is fully preserved.
        Assert.AreEqual(3, batch.Recovered, string.Join(" | ", batch.Failures));
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.AreEqual("occupied", File.ReadAllText(conflictOriginal)); // never overwritten
        Assert.IsTrue(File.Exists(outerStaged));                          // inner level restored into it
        Assert.IsTrue(File.Exists(Path.Combine(outerDir, OrbisTempRecovery.SidecarName)));
        Assert.IsTrue(Directory.Exists(outerDir));
    }

    [TestMethod]
    public void RecoverAllSafe_ZeroProgressGuard_StopsImmediately()
    {
        using var fixture = new RecoveryFixture("Game", "game.pkg");
        // A staging dir whose "original" parent is a FILE - File.Exists(original)
        // is false so the item looks recoverable, but the move always fails.
        string blocker = Path.Combine(fixture.Root, "blocker");
        File.WriteAllText(blocker, "not a directory");
        string dir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "fail");
        Directory.CreateDirectory(dir);
        string staged = Path.Combine(dir,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.WriteAllText(Path.Combine(dir, OrbisTempRecovery.SidecarName),
            Path.Combine(blocker, "game.pkg"));
        File.Move(fixture.PackagePath, staged);

        StagedPkgRecoveryScanner.StagedPkgRecoveryBatchResult batch =
            StagedPkgRecoveryScanner.RecoverAllSafe(new[] { fixture.Root });

        // One failing pass and the guard stops the loop - no infinite retries.
        Assert.AreEqual(0, batch.Recovered);
        Assert.AreEqual(1, batch.Failed);
        Assert.AreEqual(1, batch.Passes);
        Assert.IsTrue(File.Exists(staged)); // failed recovery preserves everything
    }

    [TestMethod]
    public void RecoverAllSafe_MaxPassGuard_CapsPathologicalChains()
    {
        using var fixture = new RecoveryFixture("Game", "game.pkg");
        BuildLegacyChain(fixture, StagedPkgRecoveryScanner.MaxRecoveryPasses + 5); // 25 levels

        StagedPkgRecoveryScanner.StagedPkgRecoveryBatchResult batch =
            StagedPkgRecoveryScanner.RecoverAllSafe(new[] { fixture.Root });

        Assert.AreEqual(StagedPkgRecoveryScanner.MaxRecoveryPasses, batch.Recovered);
        Assert.AreEqual(StagedPkgRecoveryScanner.MaxRecoveryPasses, batch.Passes);
        // The remaining levels stay on disk, untouched - never looped forever.
        Assert.IsFalse(File.Exists(fixture.PackagePath));
        Assert.IsTrue(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*").Length > 0);
    }

    // ── Atomic metadata publish ────────────────────────────────────────────

    [TestMethod]
    public void AtomicPublish_TmpFileAlone_IsNeverValidRecoveryState()
    {
        using var fixture = new RecoveryFixture("safe parent", "game Ω.pkg");
        // Simulate a crash DURING the metadata write: only the .tmp exists and
        // the PKG was never renamed (the rename happens after the publish).
        string tmpSidecar = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg.recovery.tmp");
        File.WriteAllText(tmpSidecar, "{\"SchemaVersion\":1, \"OriginalFullPath\":\"" + fixture.PackagePath + "\"}");

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        // The untouched PKG at its original name is NOT a staging artifact,
        // and the half-written tmp is not recovery metadata.
        Assert.AreEqual(0, items.Count);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
    }

    [TestMethod]
    public void AtomicPublish_AbandonedTmpIsDeleted_OnlyWhenOld()
    {
        using var fixture = new RecoveryFixture("safe parent", "game Ω.pkg");
        string staleTmp = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg.recovery.tmp");
        File.WriteAllText(staleTmp, "partial");
        File.SetLastWriteTimeUtc(staleTmp, DateTime.UtcNow.AddDays(-2));

        string freshTmp = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg.recovery.tmp");
        File.WriteAllText(freshTmp, "partial");

        _ = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.IsFalse(File.Exists(staleTmp));   // obviously abandoned - removed
        Assert.IsTrue(File.Exists(freshTmp));    // recent - could belong to a live publish
        Assert.IsTrue(File.Exists(fixture.PackagePath));
    }

    [TestMethod]
    public void MalformedMetadata_NeverCausesDeletion()
    {
        using var fixture = new RecoveryFixture("safe parent", "game Ω.pkg");
        string temp = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, temp);
        File.WriteAllText(temp + OrbisSafePkgOperation.RecoverySidecarSuffix, "{ not json");

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        StagedPkgRecoveryItem item = items[0];
        Assert.AreEqual(StagedPkgRecoveryStatus.MalformedMetadata, item.Status);
        Assert.IsFalse(item.CanAutoRecover);

        (bool ok, _) = StagedPkgRecoveryScanner.Recover(item);
        Assert.IsFalse(ok);
        Assert.IsTrue(File.Exists(temp)); // the possible-only-copy PKG is never deleted
        Assert.IsTrue(File.Exists(temp + OrbisSafePkgOperation.RecoverySidecarSuffix));
    }

    // ── Cross-process owner metadata ───────────────────────────────────────

    [TestMethod]
    public void OwnerMetadata_LiveOwner_IsActiveOperationAndNeverRecovered()
    {
        using var fixture = new RecoveryFixture("safe parent", "game Ω.pkg");
        string temp = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, temp);
        WriteOwnerSidecar(temp, fixture.PackagePath, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime());

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(StagedPkgRecoveryStatus.ActiveOperation, items[0].Status);
        Assert.IsFalse(items[0].CanAutoRecover);
        (bool ok, _) = StagedPkgRecoveryScanner.Recover(items[0]);
        Assert.IsFalse(ok);
        Assert.IsTrue(File.Exists(temp)); // not moved, not deleted
    }

    [TestMethod]
    public void OwnerMetadata_DeadOrMismatchedOwner_IsRecoverable()
    {
        using var fixture = new RecoveryFixture("safe parent", "game Ω.pkg");
        string temp = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, temp);

        // Reused PID: process alive but start time differs -> abandoned, recoverable.
        WriteOwnerSidecar(temp, fixture.PackagePath, Environment.ProcessId, DateTime.UtcNow.AddHours(-5));
        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(StagedPkgRecoveryStatus.ReadyToRecover, items[0].Status);
        (bool ok, _) = StagedPkgRecoveryScanner.Recover(items[0]);
        Assert.IsTrue(ok);
        Assert.IsTrue(File.Exists(fixture.PackagePath));

        // Dead PID -> abandoned, recoverable.
        File.Move(fixture.PackagePath, temp);
        WriteOwnerSidecar(temp, fixture.PackagePath, 9999999, DateTime.UtcNow.AddHours(-5));
        items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(StagedPkgRecoveryStatus.ReadyToRecover, items[0].Status);
        (ok, _) = StagedPkgRecoveryScanner.Recover(items[0]);
        Assert.IsTrue(ok);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
    }

    [TestMethod]
    public void OwnerMetadata_LiveOwnerInStagingDir_BlocksRecovery()
    {
        using var fixture = new RecoveryFixture("ゲーム", "game.pkg");
        string dir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "owned");
        Directory.CreateDirectory(dir);
        string staged = Path.Combine(dir,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.WriteAllText(Path.Combine(dir, OrbisTempRecovery.SidecarName), fixture.PackagePath);
        File.Move(fixture.PackagePath, staged);
        File.WriteAllText(Path.Combine(dir, OrbisSafePkgOperation.OwnerSidecarName),
            JsonSerializer.Serialize(new StagedPkgRecoveryMetadata
            {
                OriginalFullPath = fixture.PackagePath,
                TemporaryFullPath = staged,
                StagingMode = "DriveRootStaging",
                OwnerProcessId = Environment.ProcessId,
                OwnerProcessStartTimeUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
            }));

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(StagedPkgRecoveryStatus.ActiveOperation, items[0].Status);
        Assert.IsFalse(items[0].CanAutoRecover);
        (bool ok, _) = StagedPkgRecoveryScanner.Recover(items[0]);
        Assert.IsFalse(ok);
        Assert.IsTrue(File.Exists(staged));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Builds a legacy nested p4t_v_ chain: original <- L1 <- L2 <- ... <- Ln
    /// with the PKG physically at the innermost level and each sidecar pointing
    /// one level outward (as the old per-level moves wrote them).</summary>
    private static void BuildLegacyChain(RecoveryFixture fixture, int levels)
    {
        string currentPath = fixture.PackagePath;
        for (int i = 1; i <= levels; i++)
        {
            string dir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "L" + i);
            Directory.CreateDirectory(dir);
            string staged = Path.Combine(dir,
                OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
            File.WriteAllText(Path.Combine(dir, OrbisTempRecovery.SidecarName), currentPath);
            File.Move(currentPath, staged);
            currentPath = staged;
        }
    }

    private static void WriteOwnerSidecar(string tempPkgPath, string originalPath, int pid, DateTime startTimeUtc)
    {
        File.WriteAllText(tempPkgPath + OrbisSafePkgOperation.RecoverySidecarSuffix,
            JsonSerializer.Serialize(new StagedPkgRecoveryMetadata
            {
                OriginalFullPath = originalPath,
                TemporaryFullPath = tempPkgPath,
                StagingMode = "RenameInPlace",
                OwnerProcessId = pid,
                OwnerProcessStartTimeUtc = startTimeUtc.ToString("O"),
            }));
    }

    private sealed class RecoveryFixture : IDisposable
    {
        public RecoveryFixture(string directoryName, string fileName)
        {
            Root = Path.Combine(Path.GetTempPath(), "p4t-recovery-test-" + Guid.NewGuid().ToString("N"));
            PackageDirectory = Path.Combine(Root, directoryName);
            Directory.CreateDirectory(PackageDirectory);
            PackagePath = Path.Combine(PackageDirectory, fileName);
            File.WriteAllBytes(PackagePath, Enumerable.Range(0, 96).Select(value => (byte)value).ToArray());
        }

        public string Root { get; }
        public string PackageDirectory { get; }
        public string PackagePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
