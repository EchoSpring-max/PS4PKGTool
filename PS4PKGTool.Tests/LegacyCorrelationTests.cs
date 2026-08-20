using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Tests;

/// <summary>
/// Legacy correlation tests: matching MissingPkg recovery records with
/// standalone legacy staged PKGs. Only Exact (staged-filename chain
/// evidence) and High (same directory + Title ID + type + uniqueness)
/// may produce one combined recoverable item; everything ambiguous stays
/// separate and manual.
/// </summary>
[TestClass]
public sealed class LegacyCorrelationTests
{
    private (string Title, string TitleId, string ContentId, string PkgType)? _metadataOverride;

    private (string, string, string, string) ReadMetadata(string path)
    {
        return _metadataOverride
            ?? (string.Empty, string.Empty, string.Empty, string.Empty);
    }

    [TestInitialize]
    public void Setup()
    {
        StagedPkgRecoveryScanner.OrphanMetadataReader = ReadMetadata;
        _metadataOverride = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        StagedPkgRecoveryScanner.OrphanMetadataReader = null;
    }

    [TestMethod]
    public void ExactChainMatch_ProducesOneCombinedRecoverableItem()
    {
        using var fixture = new CorrelationFixture("Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);

        string orphanName = OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg";
        string orphanPath = Path.Combine(fixture.PackageDirectory, orphanName);
        File.Move(fixture.PackagePath, orphanPath); // standalone orphan in the game folder

        // Record B: recorded the FINAL original.
        string recordBDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "B");
        Directory.CreateDirectory(recordBDir);
        File.WriteAllText(Path.Combine(recordBDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);

        // Record A: recorded the staged file inside record B - its filename
        // equals the orphan's name (exact chain evidence).
        string recordADir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "A");
        Directory.CreateDirectory(recordADir);
        File.WriteAllText(Path.Combine(recordADir, OrbisTempRecovery.SidecarName),
            Path.Combine(recordBDir, orphanName));

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        StagedPkgRecoveryItem combined = items.Single(i =>
            i.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate);
        Assert.AreEqual(StagedPkgCorrelationConfidence.Exact, combined.CorrelationConfidence);
        Assert.AreEqual(StagedPkgRecoveryStatus.MatchedLegacyResidue, combined.Status);
        Assert.AreEqual(orphanPath, combined.CurrentPath);
        Assert.AreEqual(fixture.PackagePath, combined.OriginalPath); // chain-walked final original
        Assert.IsTrue(combined.CanAutoRecover);

        (bool ok, string error) = StagedPkgRecoveryScanner.Recover(combined);
        Assert.IsTrue(ok, error);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsFalse(File.Exists(orphanPath));
        Assert.IsFalse(Directory.Exists(recordADir)); // stale record removed after verification
    }

    [TestMethod]
    public void HighConfidenceMatch_SameDirTitleIdTypeUnique_CombinesAndRecovers()
    {
        using var fixture = new CorrelationFixture("Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);

        string orphanPath = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphanPath);
        _metadataOverride = ("Spyro Reignited Trilogy", "CUSA12085", "UP0000-CUSA12085_00-X", "GAME");

        string recordDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "old");
        Directory.CreateDirectory(recordDir);
        File.WriteAllText(Path.Combine(recordDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        // One combined logical row - the MissingPkg record and the orphan are
        // deduplicated, not shown as two artifacts.
        Assert.AreEqual(1, items.Count);
        StagedPkgRecoveryItem combined = items[0];
        Assert.AreEqual(StagedPkgRecoveryType.LegacyRecoveryCandidate, combined.RecoveryType);
        Assert.AreEqual(StagedPkgCorrelationConfidence.High, combined.CorrelationConfidence);
        Assert.AreEqual(StagedPkgRecoveryStatus.MatchedLegacyResidue, combined.Status);
        Assert.IsTrue(combined.CanAutoRecover);

        (bool ok, string error) = StagedPkgRecoveryScanner.Recover(combined);
        Assert.IsTrue(ok, error);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsFalse(Directory.Exists(recordDir));
    }

    [TestMethod]
    public void MultipleOrphanCandidates_NeverAutoLinked()
    {
        using var fixture = new CorrelationFixture("Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg");
        string orphan1 = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        string orphan2 = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphan1);
        File.WriteAllText(orphan2, "second candidate");
        _metadataOverride = ("Spyro", "CUSA12085", "UP0000-CUSA12085_00-X", "GAME");

        string recordDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "old");
        Directory.CreateDirectory(recordDir);
        File.WriteAllText(Path.Combine(recordDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        // No combined item, nothing auto-recoverable, everything preserved.
        Assert.IsFalse(items.Any(i => i.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate));
        Assert.IsFalse(items.Any(i => i.CanAutoRecover));
        Assert.IsTrue(File.Exists(orphan1));
        Assert.IsTrue(File.Exists(orphan2));
        StagedPkgRecoveryItem record = items.Single(i => i.Status == StagedPkgRecoveryStatus.MissingPkg);
        Assert.AreEqual(StagedPkgCorrelationConfidence.Ambiguous, record.CorrelationConfidence);
    }

    [TestMethod]
    public void OneOrphanMultipleRecords_NeverAutoLinked()
    {
        using var fixture = new CorrelationFixture("Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg");
        string orphanPath = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphanPath);
        _metadataOverride = ("Spyro", "CUSA12085", "UP0000-CUSA12085_00-X", "GAME");

        foreach (string id in new[] { "R1", "R2" })
        {
            string dir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, OrbisTempRecovery.SidecarName), fixture.PackagePath);
        }

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.IsFalse(items.Any(i => i.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate));
        Assert.IsFalse(items.Any(i => i.CanAutoRecover));
        Assert.IsTrue(File.Exists(orphanPath));
        Assert.AreEqual(2, items.Count(i => i.Status == StagedPkgRecoveryStatus.MissingPkg));
    }

    [TestMethod]
    public void TitleTextOnly_NeverLinks()
    {
        using var fixture = new CorrelationFixture("Some Other Game.pkg");
        // Orphan in a DIFFERENT directory with only a matching title and no IDs.
        string orphanDir = Path.Combine(fixture.Root, "elsewhere");
        Directory.CreateDirectory(orphanDir);
        string orphanPath = Path.Combine(orphanDir,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphanPath);
        _metadataOverride = ("Some Other Game", "", "", "");

        string recordDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "old");
        Directory.CreateDirectory(recordDir);
        File.WriteAllText(Path.Combine(recordDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.IsFalse(items.Any(i => i.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate));
        Assert.IsTrue(File.Exists(orphanPath));
    }

    [TestMethod]
    public void TypeMismatch_BlocksCorrelation()
    {
        using var fixture = new CorrelationFixture("Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg");
        string orphanPath = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphanPath);
        // Same dir + same title ID, but the orphan is a PATCH while the
        // recorded path says Base.
        _metadataOverride = ("Spyro Reignited Trilogy", "CUSA12085", "UP0000-CUSA12085_00-X", "PATCH");

        string recordDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "old");
        Directory.CreateDirectory(recordDir);
        File.WriteAllText(Path.Combine(recordDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        Assert.IsFalse(items.Any(i => i.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate));
        Assert.IsTrue(File.Exists(orphanPath));
    }

    [TestMethod]
    public void MatchedPair_TargetConflict_NeverOverwrites()
    {
        using var fixture = new CorrelationFixture("Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg");
        string orphanPath = Path.Combine(fixture.PackageDirectory,
            OrbisTempRecovery.TempPkgNamePrefix + Guid.NewGuid().ToString("N") + ".pkg");
        File.Move(fixture.PackagePath, orphanPath);
        File.WriteAllText(fixture.PackagePath, "occupied by someone else");
        _metadataOverride = ("Spyro", "CUSA12085", "UP0000-CUSA12085_00-X", "GAME");

        string recordDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "old");
        Directory.CreateDirectory(recordDir);
        File.WriteAllText(Path.Combine(recordDir, OrbisTempRecovery.SidecarName), fixture.PackagePath);

        List<StagedPkgRecoveryItem> items = StagedPkgRecoveryScanner.Scan(new[] { fixture.Root });

        StagedPkgRecoveryItem combined = items.Single(i =>
            i.RecoveryType == StagedPkgRecoveryType.LegacyRecoveryCandidate);
        Assert.AreEqual(StagedPkgRecoveryStatus.Conflict, combined.Status);
        Assert.IsFalse(combined.CanAutoRecover);
        (bool ok, _) = StagedPkgRecoveryScanner.Recover(combined);
        Assert.IsFalse(ok);
        Assert.AreEqual("occupied by someone else", File.ReadAllText(fixture.PackagePath));
        Assert.IsTrue(File.Exists(orphanPath));
    }

    [TestMethod]
    public void StaleRecordRemoval_KnownFilesOnlyRemoved_UnknownFilePreserved()
    {
        using var fixture = new CorrelationFixture("game.pkg");
        string knownDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "known");
        Directory.CreateDirectory(knownDir);
        File.WriteAllText(Path.Combine(knownDir, OrbisTempRecovery.SidecarName), "B:\\x.pkg");
        File.WriteAllText(Path.Combine(knownDir, OrbisSafePkgOperation.OwnerSidecarName), "{}");

        string unknownDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "unknown");
        Directory.CreateDirectory(unknownDir);
        File.WriteAllText(Path.Combine(unknownDir, OrbisTempRecovery.SidecarName), "B:\\x.pkg");
        File.WriteAllText(Path.Combine(unknownDir, "user notes.txt"), "keep me");

        string withPkgDir = Path.Combine(fixture.Root, OrbisTempRecovery.TempDirPrefix + "withpkg");
        Directory.CreateDirectory(withPkgDir);
        File.WriteAllText(Path.Combine(withPkgDir, OrbisTempRecovery.SidecarName), "B:\\x.pkg");
        File.WriteAllText(Path.Combine(withPkgDir, "anything.pkg"), "a package");

        Assert.IsTrue(StagedPkgRecoveryScanner.IsStaleRecordRemovable(knownDir));
        (bool removed, _) = StagedPkgRecoveryScanner.TryRemoveStaleRecoveryDirectory(knownDir);
        Assert.IsTrue(removed);
        Assert.IsFalse(Directory.Exists(knownDir));

        Assert.IsFalse(StagedPkgRecoveryScanner.IsStaleRecordRemovable(unknownDir));
        (removed, _) = StagedPkgRecoveryScanner.TryRemoveStaleRecoveryDirectory(unknownDir);
        Assert.IsFalse(removed);
        Assert.IsTrue(Directory.Exists(unknownDir));

        Assert.IsFalse(StagedPkgRecoveryScanner.IsStaleRecordRemovable(withPkgDir));
        Assert.IsTrue(Directory.Exists(withPkgDir));
    }

    [TestMethod]
    public void DeriveTitleId_HandlesBracketsAndNoise()
    {
        Assert.AreEqual("CUSA12085", StagedPkgRecoveryScanner.DeriveTitleId(
            @"B:\PKG\Spyro Reignited Trilogy [CUSA12085] 00 - Base.pkg"));
        Assert.AreEqual("CUSA00900", StagedPkgRecoveryScanner.DeriveTitleId(
            "UP9000-CUSA00900_00-BLOODBORNE000000-A0109-V0100"));
    }

    [TestMethod]
    public void DerivePkgTypeFromPath_DetectsBaseAndUpdate()
    {
        Assert.AreEqual("base", StagedPkgRecoveryScanner.DerivePkgTypeFromPath(
            @"B:\PKG\Game [CUSA12085] 00 - Base.pkg"));
        Assert.AreEqual("update", StagedPkgRecoveryScanner.DerivePkgTypeFromPath(
            @"B:\PKG\Game [CUSA12085] 01 - Update v01.02.pkg"));
        Assert.AreEqual("", StagedPkgRecoveryScanner.DerivePkgTypeFromPath(@"B:\PKG\game.pkg"));
    }

    private sealed class CorrelationFixture : IDisposable
    {
        public CorrelationFixture(string fileName)
        {
            Root = Path.Combine(Path.GetTempPath(), "p4t-correl-test-" + Guid.NewGuid().ToString("N"));
            PackageDirectory = Path.Combine(Root, "Game Folder");
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
