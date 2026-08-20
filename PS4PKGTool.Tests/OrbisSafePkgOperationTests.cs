using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Tests;

/// <summary>
/// Decision logic + lifecycle tests for the three-mode orbis staging
/// (Direct / RenameInPlace / DriveRootStaging) and its anti-nesting guard.
/// </summary>
[TestClass]
public sealed class OrbisSafePkgOperationTests
{
    // ── Safety classification ──────────────────────────────────────────────

    [TestMethod]
    public void Classification_AsciiPathWithSpacesAndSymbols_IsSafe()
    {
        Assert.IsTrue(OrbisSafePkgOperation.IsOrbisSafePath(
            @"B:\PKG\Base + Update\Spyro Reignited Trilogy\Spyro.pkg"));
        Assert.IsTrue(OrbisSafePkgOperation.IsAsciiSafePath(@"B:\PKG\game (1) [v2].pkg"));
    }

    [TestMethod]
    public void Classification_UnicodeFilenameOrParent_IsUnsafe()
    {
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(@"B:\PKG\Safe Folder\DRIVECLUB™.pkg"));
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(@"B:\PKG\ゲーム\Driveclub.pkg"));
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(@"B:\PKG\游戏\game.pkg"));
    }

    [TestMethod]
    public void Classification_ControlCharacters_AreUnsafe()
    {
        Assert.IsFalse(OrbisSafePkgOperation.IsAsciiSafePath("B:\\game.pkg"));
        Assert.IsFalse(OrbisSafePkgOperation.IsAsciiSafePath("B:\\game\t.pkg"));
    }

    [TestMethod]
    public void Classification_OverLongPath_IsUnsafe()
    {
        string longPath = @"B:\PKG\" + new string('a', 300) + ".pkg";
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(longPath));
        Assert.IsTrue(OrbisSafePkgOperation.IsAsciiSafePath(longPath)); // ASCII but too long
    }

    [TestMethod]
    public void Classification_StagingArtifacts_AreNeverSafe()
    {
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(
            @"B:\p4t_v_abc\ps4pkgtool_orbis_x.pkg"));
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(
            @"B:\PKG\p4t_v_abc\game.pkg"));
        Assert.IsFalse(OrbisSafePkgOperation.IsOrbisSafePath(
            @"B:\PKG\ps4pkgtool_orbis_0123.pkg"));
    }

    // ── Direct mode ────────────────────────────────────────────────────────

    [TestMethod]
    public void Direct_AsciiPath_NoMoveNoRenameNoStagingDir()
    {
        using var fixture = new StageFixture("ascii dir", "game (1).pkg");
        OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);

        Assert.AreEqual(OrbisPkgStageMode.Direct, operation.Mode);
        Assert.AreEqual(fixture.PackagePath, operation.OrbisPath);
        Assert.IsNull(operation.TemporaryDirectory);
        Assert.IsFalse(operation.IsStaged);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));

        // Restore is a harmless no-op.
        Assert.IsTrue(operation.Restore().Succeeded);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
    }

    // ── RenameInPlace mode ─────────────────────────────────────────────────

    [TestMethod]
    public void RenameInPlace_UnicodeFilename_RenamesInSameDirectoryAndRestores()
    {
        using var fixture = new StageFixture("safe parent", "DRIVECLUB™.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);
        OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);

        Assert.AreEqual(OrbisPkgStageMode.RenameInPlace, operation.Mode);
        Assert.AreEqual(fixture.PackageDirectory, Path.GetDirectoryName(operation.OrbisPath));
        Assert.IsNull(operation.TemporaryDirectory);
        Assert.IsTrue(IsAscii(operation.OrbisPath));
        StringAssert.StartsWith(Path.GetFileName(operation.OrbisPath), "ps4pkgtool_orbis_");
        Assert.IsFalse(File.Exists(fixture.PackagePath));
        Assert.IsTrue(File.Exists(operation.OrbisPath));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));

        Assert.IsTrue(operation.Restore().Succeeded);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.IsFalse(File.Exists(operation.OrbisPath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
    }

    [TestMethod]
    public void RenameInPlace_BodyException_FinallyStillRestores()
    {
        using var fixture = new StageFixture("safe parent", "ex ゲーム.pkg");
        OrbisSafePkgOperation? operation = null;
        try
        {
            operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
            throw new InvalidOperationException("simulated extraction failure");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            Assert.IsNotNull(operation);
            Assert.IsTrue(operation!.Restore().Succeeded);
        }
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
    }

    [TestMethod]
    public void RenameInPlace_RestoreConflict_NeverOverwritesAndReportsPath()
    {
        using var fixture = new StageFixture("safe parent", "conflict Ω.pkg");
        OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
        File.WriteAllText(fixture.PackagePath, "occupied by someone else");

        OrbisSafePkgRestoreResult failed = operation.Restore();

        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("occupied by someone else", File.ReadAllText(fixture.PackagePath));
        Assert.IsTrue(File.Exists(operation.OrbisPath)); // temp rename preserved
        Assert.AreEqual(fixture.PackageDirectory, failed.RecoveryDirectory);
    }

    // ── DriveRootStaging mode ──────────────────────────────────────────────

    [TestMethod]
    public void DriveRootStaging_UnicodeParent_StagesAtRootSameDriveAndCleansUp()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(),
            "p4t-stage-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overrideRoot);
        OrbisSafePkgOperation.StagingRootOverride = overrideRoot;
        try
        {
            using var fixture = new StageFixture("ゲーム", "Driveclub.pkg");
            byte[] expected = File.ReadAllBytes(fixture.PackagePath);
            OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);

            Assert.AreEqual(OrbisPkgStageMode.DriveRootStaging, operation.Mode);
            Assert.IsNotNull(operation.TemporaryDirectory);
            Assert.AreEqual(overrideRoot, Path.GetDirectoryName(operation.TemporaryDirectory));
            StringAssert.StartsWith(
                Path.GetFileName(operation.TemporaryDirectory), OrbisTempRecovery.TempDirPrefix);
            Assert.IsTrue(IsAscii(operation.OrbisPath));
            Assert.AreEqual(
                Path.GetPathRoot(fixture.PackagePath), Path.GetPathRoot(operation.OrbisPath));
            Assert.IsFalse(File.Exists(fixture.PackagePath));
            Assert.AreEqual(fixture.PackagePath, File.ReadAllText(Path.Combine(
                operation.TemporaryDirectory, OrbisTempRecovery.SidecarName)));

            Assert.IsTrue(operation.Restore().Succeeded);
            Assert.IsTrue(File.Exists(fixture.PackagePath));
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
            Assert.IsFalse(Directory.Exists(operation.TemporaryDirectory)); // p4t_v_* removed
        }
        finally
        {
            OrbisSafePkgOperation.StagingRootOverride = null;
            try { Directory.Delete(overrideRoot, true); } catch { }
        }
    }

    [TestMethod]
    public void DriveRootStaging_RestoreConflict_PreservesStagedPkgAndRecoveryDir()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(),
            "p4t-conflict-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overrideRoot);
        OrbisSafePkgOperation.StagingRootOverride = overrideRoot;
        try
        {
            using var fixture = new StageFixture("ゲーム", "conflict.pkg");
            byte[] originalContents = File.ReadAllBytes(fixture.PackagePath);
            OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
            File.WriteAllText(fixture.PackagePath, "occupied");

            OrbisSafePkgRestoreResult failed = operation.Restore();

            Assert.IsFalse(failed.Succeeded);
            Assert.AreEqual("occupied", File.ReadAllText(fixture.PackagePath)); // not overwritten
            Assert.IsTrue(File.Exists(operation.OrbisPath));                    // staged pkg preserved
            Assert.IsTrue(File.Exists(Path.Combine(
                operation.TemporaryDirectory!, OrbisTempRecovery.SidecarName)));
            Assert.AreEqual(operation.TemporaryDirectory, failed.RecoveryDirectory);

            // Startup recovery can still resolve it.
            File.Delete(fixture.PackagePath);
            OrbisTempRecovery.RecoverySummary recovered = OrbisTempRecovery.Recover(
                new[] { overrideRoot });
            Assert.AreEqual(1, recovered.Restored);
            CollectionAssert.AreEqual(originalContents, File.ReadAllBytes(fixture.PackagePath));
        }
        finally
        {
            OrbisSafePkgOperation.StagingRootOverride = null;
            try { Directory.Delete(overrideRoot, true); } catch { }
        }
    }

    // ── Anti-nesting guard ─────────────────────────────────────────────────

    [TestMethod]
    public void AntiNesting_PkgUnderTempDir_IsRejectedWithoutNewStaging()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(),
            "p4t-antinest-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overrideRoot);
        OrbisSafePkgOperation.StagingRootOverride = overrideRoot;
        try
        {
            string tempDir = Path.Combine(overrideRoot, OrbisTempRecovery.TempDirPrefix + "AAA");
            Directory.CreateDirectory(tempDir);
            string nested = Path.Combine(tempDir, "ps4pkgtool_orbis_x.pkg");
            File.WriteAllText(nested, "staged copy");

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => OrbisSafePkgOperation.Prepare(nested));
            StringAssert.Contains(exception.Message, "staging/recovery PKG");

            // No second p4t_v_* may appear and the file must not move.
            Assert.IsTrue(File.Exists(nested));
            Assert.AreEqual(1, Directory.GetDirectories(
                overrideRoot, OrbisTempRecovery.TempDirPrefix + "*").Length);
        }
        finally
        {
            OrbisSafePkgOperation.StagingRootOverride = null;
            try { Directory.Delete(overrideRoot, true); } catch { }
        }
    }

    [TestMethod]
    public void AntiNesting_StagedRenameName_IsRejected()
    {
        using var fixture = new StageFixture("ascii", "ps4pkgtool_orbis_0123456789abcdef0123456789abcdef.pkg");
        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => OrbisSafePkgOperation.Prepare(fixture.PackagePath));
        StringAssert.Contains(exception.Message, "staging/recovery PKG");
        Assert.IsTrue(File.Exists(fixture.PackagePath));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool IsAscii(string value) => value.All(character => character < 128);

    private sealed class StageFixture : IDisposable
    {
        public StageFixture(string directoryName, string fileName)
        {
            Root = Path.Combine(Path.GetTempPath(), "p4t-op-test-" + Guid.NewGuid().ToString("N"));
            PackageDirectory = Path.Combine(Root, directoryName);
            Directory.CreateDirectory(PackageDirectory);
            PackagePath = Path.Combine(PackageDirectory, fileName);
            File.WriteAllBytes(PackagePath, Enumerable.Range(0, 64).Select(value => (byte)value).ToArray());
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
