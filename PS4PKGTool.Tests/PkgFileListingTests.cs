using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgInspection;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System.Diagnostics;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class PkgFileListingTests
{
    private const string ListingOutput = """
        D  0 2026-01-01 00:00:00 Image0
        D  0 2026-01-01 00:00:00 Image0/sce sys
        F  524288 2026-01-01 00:00:00 Image0/sce sys/icon 0.png
        F  1536 2026-01-01 00:00:00 Image0/sce sys/日本語 param.sfo
        F  4096 2026-01-01 00:00:00 Image0/eboot.bin
        D  0 2026-01-01 00:00:00 Image0/other
        F  99 2026-01-01 00:00:00 Image0/other/icon 0.png
        D  0 2026-01-01 00:00:00 Sc0
        F  88 2026-01-01 00:00:00 Sc0/param.sfo
        """;

    [TestMethod]
    public void Parser_HandlesRootsNestedFoldersSpacesUnicodeDuplicatesAndSizes()
    {
        PkgFileParseResult parsed = new PkgFileListingParser().Parse(ListingOutput);

        Assert.AreEqual(9, parsed.Entries.Count);
        Assert.AreEqual(0, parsed.MalformedLineCount);
        Assert.IsTrue(parsed.Entries.Any(entry => entry.FullPath == "Image0" && entry.IsDirectory));
        Assert.IsTrue(parsed.Entries.Any(entry => entry.FullPath == "Sc0" && entry.IsDirectory));
        Assert.AreEqual(524288, parsed.Entries.Single(entry =>
            entry.FullPath == "Image0/sce sys/icon 0.png").Size);
        Assert.IsTrue(parsed.Entries.Any(entry => entry.Name == "日本語 param.sfo"));
        Assert.AreEqual(2, parsed.Entries.Count(entry => entry.Name == "icon 0.png"));
        Assert.IsTrue(parsed.Entries.Any(entry => entry.FullPath == "Image0/eboot.bin"));
    }

    [TestMethod]
    public void Parser_IgnoresMalformedLinesAndReportsThem()
    {
        string output = "F invalid prefix\nD 0 date time MissingRoot/folder\n" +
                        "F 12 date time Image0/good file.bin\nprogress message";

        PkgFileParseResult parsed = new PkgFileListingParser().Parse(output);

        Assert.AreEqual(1, parsed.Entries.Count);
        Assert.AreEqual("good file.bin", parsed.Entries[0].Name);
        Assert.AreEqual(2, parsed.MalformedLineCount);
    }

    [TestMethod]
    public void TreeBuilder_CreatesIndexedHierarchyWithoutMergingDuplicateNames()
    {
        PkgFileParseResult parsed = new PkgFileListingParser().Parse(ListingOutput);
        IReadOnlyList<PkgFileNode> roots = new PkgFileTreeBuilder().Build(parsed.Entries);

        Assert.AreEqual(2, roots.Count);
        PkgFileNode image = roots.Single(root => root.Name == "Image0");
        PkgFileNode sc = roots.Single(root => root.Name == "Sc0");
        Assert.IsTrue(image.Children.Any(child => child.Name == "eboot.bin"));
        Assert.IsTrue(image.Children.Single(child => child.Name == "sce sys")
            .Children.Any(child => child.Name == "icon 0.png"));
        Assert.IsTrue(image.Children.Single(child => child.Name == "other")
            .Children.Any(child => child.Name == "icon 0.png"));
        Assert.IsTrue(sc.Children.Any(child => child.Name == "param.sfo"));
    }

    [TestMethod]
    public void ParserAndTreeBuilder_HandleLargeListings()
    {
        var output = new System.Text.StringBuilder("D 0 date time Image0\n");
        for (int index = 0; index < 10000; index++)
            output.Append("F ").Append(index + 1).Append(" date time Image0/data/folder")
                .Append(index % 100).Append("/file ").Append(index).AppendLine(".bin");

        PkgFileParseResult parsed = new PkgFileListingParser().Parse(output.ToString());
        IReadOnlyList<PkgFileNode> roots = new PkgFileTreeBuilder().Build(parsed.Entries);

        Assert.AreEqual(10001, parsed.Entries.Count);
        Assert.AreEqual(1, roots.Count);
        Assert.AreEqual(100, roots[0].Children.Single(child => child.Name == "data").Children.Count);
    }

    [TestMethod]
    public async Task Session_IsLazyAndCachesSecondFilesActivation()
    {
        var loader = new CountingListingLoader(SuccessResult());
        using var session = new PkgFileListingSession(
            "sample.pkg", PkgFileListingService.DefaultPasscode, loader);

        Assert.IsFalse(session.HasStarted);
        Assert.AreEqual(0, loader.Count);
        PkgFileListingResult first = await session.LoadAsync(CancellationToken.None);
        PkgFileListingResult second = await session.LoadAsync(CancellationToken.None);

        Assert.IsTrue(session.HasStarted);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, loader.Count);
    }

    [TestMethod]
    public async Task Service_UsesArgumentListSafeInputAndRestoresUnchangedPackage()
    {
        // Unicode file name + safe parent -> RenameInPlace: the package is
        // renamed inside its own directory, no p4t_v_* is created, and the
        // original name is restored afterwards.
        using var fixture = new PackageFixture("folder with spaces", "game 日本語 !@#.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);
        string tool = fixture.CreateToolPlaceholder();
        string safePath = null;
        var runner = new RecordingRunner((startInfo, _, _) =>
        {
            safePath = startInfo.ArgumentList[^1];
            Assert.IsFalse(File.Exists(fixture.PackagePath));
            Assert.IsTrue(File.Exists(safePath));
            Assert.IsTrue(IsAscii(safePath));
            StringAssert.StartsWith(Path.GetFileName(safePath), "ps4pkgtool_orbis_");
            Assert.AreEqual(fixture.PackageDirectory, Path.GetDirectoryName(safePath));
            CollectionAssert.Contains(startInfo.ArgumentList.ToArray(), "img_file_list");
            CollectionAssert.Contains(startInfo.ArgumentList.ToArray(), "--passcode");
            CollectionAssert.Contains(startInfo.ArgumentList.ToArray(), PkgFileListingService.DefaultPasscode);
            return Task.FromResult(new OrbisProcessResult
            {
                ExitCode = 0,
                StandardOutput = ListingOutput
            });
        });

        PkgFileListingResult result = await CreateService(tool, runner)
            .ListAsync(fixture.PackagePath, string.Empty, CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsEmpty(Directory.GetFiles(
            fixture.PackageDirectory, OrbisTempRecovery.TempPkgPattern));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
        Assert.AreEqual(1, runner.Count);
    }

    [TestMethod]
    public async Task Service_AsciiSafePackage_IsPassedDirectlyWithoutStaging()
    {
        using var fixture = new PackageFixture("folder with spaces", "game !@#.pkg");
        byte[] expected = File.ReadAllBytes(fixture.PackagePath);
        string tool = fixture.CreateToolPlaceholder();
        string passedPath = null;
        var runner = new RecordingRunner((startInfo, _, _) =>
        {
            passedPath = startInfo.ArgumentList[^1];
            // Direct mode: the original path goes to orbis untouched - the
            // package must still be at its original location during the run.
            Assert.AreEqual(fixture.PackagePath, passedPath);
            Assert.IsTrue(File.Exists(fixture.PackagePath));
            return Task.FromResult(new OrbisProcessResult
            {
                ExitCode = 0,
                StandardOutput = ListingOutput
            });
        });

        PkgFileListingResult result = await CreateService(tool, runner)
            .ListAsync(fixture.PackagePath, string.Empty, CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(fixture.PackagePath));
        Assert.IsEmpty(Directory.GetDirectories(
            fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
        Assert.AreEqual(1, runner.Count);
    }

    [TestMethod]
    public async Task MissingTool_IsLocalFailureAndDoesNotMovePackage()
    {
        using var fixture = new PackageFixture("packages", "missing-tool.pkg");
        var runner = new RecordingRunner((_, _, _) =>
            throw new AssertFailedException("The process runner must not be called."));

        PkgFileListingResult result = await CreateService(
            Path.Combine(fixture.Root, "missing-orbis.exe"), runner)
            .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.ErrorMessage, "not found");
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.AreEqual(0, runner.Count);
    }

    [TestMethod]
    public void SafeInput_SupportsSpacesUnicodeAndSymbolsAndUsesUniqueGuidNames()
    {
        // ASCII parent + non-ASCII file name -> RenameInPlace (no p4t_v_*).
        {
            using var fixture = new PackageFixture("with spaces", "Game Ω !.pkg");
            OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
            Assert.AreEqual(OrbisPkgStageMode.RenameInPlace, operation.Mode);
            Assert.IsTrue(IsAscii(operation.OrbisPath));
            Assert.IsNull(operation.TemporaryDirectory);
            Assert.AreEqual(fixture.PackageDirectory, Path.GetDirectoryName(operation.OrbisPath));
            StringAssert.StartsWith(Path.GetFileName(operation.OrbisPath), "ps4pkgtool_orbis_");
            Assert.IsTrue(operation.Restore().Succeeded);
            Assert.IsTrue(File.Exists(fixture.PackagePath));
            Assert.IsEmpty(Directory.GetDirectories(
                fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
        }

        // Non-ASCII parent -> DriveRootStaging (sidecar + same-drive staging root).
        {
            OrbisSafePkgOperation.StagingRootOverride = null;
            string overrideRoot = Path.Combine(Path.GetTempPath(),
                "p4t-stage-root-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(overrideRoot);
            OrbisSafePkgOperation.StagingRootOverride = overrideRoot;
            try
            {
                using var fixture = new PackageFixture("日本語", "Game Ω !.pkg");
                OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
                Assert.AreEqual(OrbisPkgStageMode.DriveRootStaging, operation.Mode);
                Assert.IsTrue(IsAscii(operation.OrbisPath));
                Assert.IsNotNull(operation.TemporaryDirectory);
                Assert.AreEqual(overrideRoot, Path.GetDirectoryName(operation.TemporaryDirectory));
                StringAssert.StartsWith(Path.GetFileName(operation.TemporaryDirectory), OrbisTempRecovery.TempDirPrefix);
                StringAssert.StartsWith(Path.GetFileName(operation.OrbisPath), "ps4pkgtool_orbis_");
                Assert.AreEqual(Path.GetPathRoot(fixture.PackagePath), Path.GetPathRoot(operation.OrbisPath));
                Assert.AreEqual(fixture.PackagePath, File.ReadAllText(Path.Combine(
                    operation.TemporaryDirectory, OrbisTempRecovery.SidecarName)));
                Assert.IsTrue(operation.Restore().Succeeded);
                Assert.IsTrue(File.Exists(fixture.PackagePath));
                Assert.IsFalse(Directory.Exists(operation.TemporaryDirectory));
            }
            finally
            {
                OrbisSafePkgOperation.StagingRootOverride = null;
                try { Directory.Delete(overrideRoot, true); } catch { }
            }
        }

        // Fully ASCII path -> Direct (no move, no rename, no p4t_v_*).
        {
            using var fixture = new PackageFixture("symbols !@#$", "Game !.pkg");
            OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
            Assert.AreEqual(OrbisPkgStageMode.Direct, operation.Mode);
            Assert.AreEqual(fixture.PackagePath, operation.OrbisPath);
            Assert.IsNull(operation.TemporaryDirectory);
            Assert.IsTrue(File.Exists(fixture.PackagePath));
            Assert.IsTrue(operation.Restore().Succeeded);
            Assert.IsTrue(File.Exists(fixture.PackagePath));
            Assert.IsEmpty(Directory.GetDirectories(
                fixture.Root, OrbisTempRecovery.TempDirPrefix + "*"));
        }
    }

    [TestMethod]
    public async Task ToolFailureAndTimeout_RestoreAndSafelyCleanInput()
    {
        using var fixture = new PackageFixture("packages", "failure.pkg");
        string tool = fixture.CreateToolPlaceholder();
        var failureRunner = new RecordingRunner((_, _, _) => Task.FromResult(
            new OrbisProcessResult { ExitCode = 9, StandardError = "wrong passcode" }));
        PkgFileListingResult failure = await CreateService(tool, failureRunner)
            .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, CancellationToken.None);
        Assert.IsFalse(failure.Succeeded);
        StringAssert.Contains(failure.ErrorMessage, "wrong passcode");
        Assert.IsTrue(File.Exists(fixture.PackagePath));

        var timeoutRunner = new RecordingRunner((_, _, _) => Task.FromResult(
            new OrbisProcessResult { ExitCode = -1, TimedOut = true }));
        PkgFileListingResult timeout = await CreateService(tool, timeoutRunner)
            .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, CancellationToken.None);
        Assert.IsFalse(timeout.Succeeded);
        StringAssert.Contains(timeout.ErrorMessage, "timed out");
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.IsEmpty(Directory.EnumerateDirectories(
            fixture.PackageDirectory, OrbisTempRecovery.TempDirPrefix + "*"));
    }

    [TestMethod]
    public async Task CancellationStopsRunnerAndRestoresPackage()
    {
        using var fixture = new PackageFixture("packages", "cancel.pkg");
        string tool = fixture.CreateToolPlaceholder();
        var runner = new BlockingRunner();
        using var cts = new CancellationTokenSource();
        Task<PkgFileListingResult> listing = CreateService(tool, runner)
            .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, cts.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await listing);
        Assert.IsTrue(runner.CancellationObserved);
        Assert.IsTrue(File.Exists(fixture.PackagePath));
        Assert.IsEmpty(Directory.EnumerateDirectories(
            fixture.PackageDirectory, OrbisTempRecovery.TempDirPrefix + "*"));
    }

    [TestMethod]
    public async Task ParserFailureRestoresPackageAndRemainsSectionLocal()
    {
        using var fixture = new PackageFixture("packages", "parse.pkg");
        string tool = fixture.CreateToolPlaceholder();
        var runner = new RecordingRunner((_, _, _) => Task.FromResult(
            new OrbisProcessResult
            {
                ExitCode = 0,
                StandardOutput = "F not-a-size date Image0/file.bin"
            }));
        var packageSnapshot = new PkgInspectionSnapshot { Title = "Still loaded" };
        try
        {
            PkgFileListingResult result = await CreateService(tool, runner)
                .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, CancellationToken.None);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("Still loaded", packageSnapshot.Title);
            Assert.IsTrue(File.Exists(fixture.PackagePath));
        }
        finally { packageSnapshot.Dispose(); }
    }

    [TestMethod]
    public void RestoreFailurePreservesRecoveryMetadataAndStartupRecoveryCanRestore()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(),
            "p4t-recover-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overrideRoot);
        OrbisSafePkgOperation.StagingRootOverride = overrideRoot;
        try
        {
            using var fixture = new PackageFixture("日本語", "recover.pkg");
            byte[] originalContents = File.ReadAllBytes(fixture.PackagePath);
            OrbisSafePkgOperation operation = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
            File.WriteAllText(fixture.PackagePath, "occupied");

            OrbisSafePkgRestoreResult failed = operation.Restore();

            Assert.IsFalse(failed.Succeeded);
            Assert.IsTrue(File.Exists(operation.OrbisPath));
            Assert.IsTrue(File.Exists(Path.Combine(
                operation.TemporaryDirectory!, OrbisTempRecovery.SidecarName)));

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

    [TestMethod]
    public void SimultaneousSafeOperationsDoNotCollide()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(),
            "p4t-simul-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overrideRoot);
        OrbisSafePkgOperation.StagingRootOverride = overrideRoot;
        try
        {
            using var fixture = new PackageFixture("packages 日本語", "one.pkg");
            string secondPath = Path.Combine(fixture.PackageDirectory, "two.pkg");
            File.WriteAllText(secondPath, "two");
            OrbisSafePkgOperation first = OrbisSafePkgOperation.Prepare(fixture.PackagePath);
            OrbisSafePkgOperation second = OrbisSafePkgOperation.Prepare(secondPath);
            try
            {
                Assert.AreEqual(OrbisPkgStageMode.DriveRootStaging, first.Mode);
                Assert.AreEqual(OrbisPkgStageMode.DriveRootStaging, second.Mode);
                Assert.AreNotEqual(first.TemporaryDirectory, second.TemporaryDirectory);
                Assert.AreNotEqual(first.OrbisPath, second.OrbisPath);
            }
            finally
            {
                Assert.IsTrue(first.Restore().Succeeded);
                Assert.IsTrue(second.Restore().Succeeded);
            }
        }
        finally
        {
            OrbisSafePkgOperation.StagingRootOverride = null;
            try { Directory.Delete(overrideRoot, true); } catch { }
        }
    }

    [TestMethod]
    public async Task ViewerListingDoesNotMutateGlobalTreeState()
    {
        var originalNode = Helper.TreeView.currentNode;
        var originalRoots = Helper.TreeView.rootNodes;
        string originalName = Helper.TreeView.Nodename;
        var loader = new CountingListingLoader(SuccessResult());
        using var session = new PkgFileListingSession(
            "sample.pkg", PkgFileListingService.DefaultPasscode, loader);

        await session.LoadAsync(CancellationToken.None);

        Assert.AreSame(originalNode, Helper.TreeView.currentNode);
        Assert.AreSame(originalRoots, Helper.TreeView.rootNodes);
        Assert.AreEqual(originalName, Helper.TreeView.Nodename);
    }

    private static PkgFileListingService CreateService(
        string toolPath, IOrbisProcessRunner runner) =>
        new PkgFileListingService(
            toolPath,
            runner,
            new PkgFileListingParser(),
            new PkgFileTreeBuilder(),
            TimeSpan.FromMilliseconds(100));

    private static PkgFileListingResult SuccessResult() => new()
    {
        Succeeded = true,
        Entries = new[] { new PkgFileEntry { FullPath = "Image0/file.bin", Name = "file.bin" } }
    };

    private static bool IsAscii(string value) => value.All(character => character < 128);

    private sealed class CountingListingLoader : IPkgFileListingLoader
    {
        private readonly PkgFileListingResult _result;

        public CountingListingLoader(PkgFileListingResult result) => _result = result;
        public int Count { get; private set; }

        public Task<PkgFileListingResult> ListAsync(
            string packagePath, string passcode, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingRunner : IOrbisProcessRunner
    {
        private readonly Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<OrbisProcessResult>> _run;

        public RecordingRunner(
            Func<ProcessStartInfo, TimeSpan, CancellationToken, Task<OrbisProcessResult>> run) =>
            _run = run;
        public int Count { get; private set; }

        public Task<OrbisProcessResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Count++;
            return _run(startInfo, timeout, cancellationToken);
        }
    }

    private sealed class BlockingRunner : IOrbisProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<OrbisProcessResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("Cancellation was not observed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class PackageFixture : IDisposable
    {
        public PackageFixture(string directoryName, string fileName)
        {
            Root = Path.Combine(Path.GetTempPath(), "p4t-list-test-" + Guid.NewGuid().ToString("N"));
            PackageDirectory = Path.Combine(Root, directoryName);
            Directory.CreateDirectory(PackageDirectory);
            PackagePath = Path.Combine(PackageDirectory, fileName);
            File.WriteAllBytes(PackagePath, Enumerable.Range(0, 128).Select(value => (byte)value).ToArray());
        }

        public string Root { get; }
        public string PackageDirectory { get; }
        public string PackagePath { get; }

        public string CreateToolPlaceholder()
        {
            string path = Path.Combine(Root, "orbis-pub-cmd.exe");
            File.WriteAllText(path, "placeholder");
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
