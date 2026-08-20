using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgInspection;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System.Diagnostics;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class PkgFileListingTests
{
    // ── tree builder (kept: feeds Main/Viewer file trees) ──────────────

    private static IReadOnlyList<PkgFileEntry> SampleEntries() => new[]
    {
        new PkgFileEntry { FullPath = "Image0", IsDirectory = true },
        new PkgFileEntry { FullPath = "Image0/sce sys", IsDirectory = true },
        new PkgFileEntry { FullPath = "Image0/sce sys/icon 0.png", Name = "icon 0.png", Size = 524288 },
        new PkgFileEntry { FullPath = "Image0/sce sys/日本語 param.sfo", Name = "日本語 param.sfo", Size = 1536 },
        new PkgFileEntry { FullPath = "Image0/eboot.bin", Name = "eboot.bin", Size = 4096 },
        new PkgFileEntry { FullPath = "Image0/other", IsDirectory = true },
        new PkgFileEntry { FullPath = "Image0/other/icon 0.png", Name = "icon 0.png", Size = 99 },
        new PkgFileEntry { FullPath = "Sc0", IsDirectory = true },
        new PkgFileEntry { FullPath = "Sc0/param.sfo", Name = "param.sfo", Size = 88 },
    };

    [TestMethod]
    public void TreeBuilder_CreatesIndexedHierarchyWithoutMergingDuplicateNames()
    {
        IReadOnlyList<PkgFileNode> roots = new PkgFileTreeBuilder().Build(SampleEntries());

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
    public void TreeBuilder_SortsDirectoriesBeforeFilesCaseInsensitive()
    {
        IReadOnlyList<PkgFileNode> roots = new PkgFileTreeBuilder().Build(SampleEntries());

        PkgFileNode image = roots.Single(root => root.Name == "Image0");
        // directories first, then files in ordinal-ignored-case order
        for (int i = 0; i < image.Children.Count; i++)
        {
            for (int j = i + 1; j < image.Children.Count; j++)
            {
                if (image.Children[i].IsDirectory == image.Children[j].IsDirectory)
                    Assert.IsTrue(StringComparer.OrdinalIgnoreCase.Compare(
                        image.Children[i].Name, image.Children[j].Name) <= 0,
                        $"{image.Children[i].Name} must sort before {image.Children[j].Name}");
                else
                {
                    Assert.IsTrue(image.Children[i].IsDirectory,
                        $"directory {image.Children[i].Name} must sort before file {image.Children[j].Name}");
                }
            }
        }
    }

    [TestMethod]
    public void TreeBuilder_HandlesLargeListings()
    {
        var entries = new List<PkgFileEntry> { new() { FullPath = "Image0", IsDirectory = true } };
        for (int index = 0; index < 10000; index++)
            entries.Add(new PkgFileEntry
            {
                FullPath = $"Image0/data/folder{index % 100}/file {index}.bin",
                Name = $"file {index}.bin",
                Size = index + 1
            });

        IReadOnlyList<PkgFileNode> roots = new PkgFileTreeBuilder().Build(entries);

        Assert.AreEqual(1, roots.Count);
        Assert.AreEqual(100, roots[0].Children.Single(child => child.Name == "data").Children.Count);
    }

    // ── session (kept: lazy load + cache contract) ─────────────────────

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

    // ── service: real PKG fixtures via OrbisPkgTool.PkgBuilder ─────────

    [TestMethod]
    public async Task Service_ListsRealPkg_Sc0AndImage0WithSizes()
    {
        using var fixture = PkgFixture.Create("list", ("a.bin", 3), ("dir/b.bin", 5));

        PkgFileListingResult result = await new PkgFileListingService()
            .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        CollectionAssert.Contains(result.Entries.Select(e => e.FullPath).ToList(), "Image0/a.bin");
        CollectionAssert.Contains(result.Entries.Select(e => e.FullPath).ToList(), "Image0/dir/b.bin");
        // PkgBuilder hoists sce_sys/param.sfo into the Sc0 entry table
        // (standard PS4 PKG layout) — it surfaces as Sc0/param.sfo.
        CollectionAssert.Contains(result.Entries.Select(e => e.FullPath).ToList(), "Sc0/param.sfo");
        Assert.AreEqual(3, result.Entries.Single(e => e.FullPath == "Image0/a.bin").Size);
        Assert.AreEqual(5, result.Entries.Single(e => e.FullPath == "Image0/dir/b.bin").Size);
        // tree roots: Image0 (+ Sc0 when present), sorted
        Assert.IsTrue(result.Roots.Any(root => root.Name == "Image0" && root.IsDirectory));
        PkgFileNode image0 = result.Roots.Single(root => root.Name == "Image0");
        Assert.IsTrue(image0.Children.Any(child => child.Name == "sce_sys"));
        // directories carry zero size, files their logical size
        Assert.AreEqual(0, image0.Size);
        Assert.AreEqual(3, image0.Children.Single(child => child.Name == "a.bin").Size);
    }

    [TestMethod]
    public async Task Service_UnicodePackagePath_ListsWithoutStaging()
    {
        // The old orbis-pub-cmd spawn needed an ASCII-safe temp rename for
        // paths like this; the in-process reader opens the original path
        // read-only and Unicode just works.
        using var fixture = PkgFixture.Create("日本語 folder", "game Ω !@#.pkg",
            ("a.bin", 4), ("data/readme.txt", 16));

        PkgFileListingResult result = await new PkgFileListingService()
            .ListAsync(fixture.PackagePath, PkgFileListingService.DefaultPasscode, CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.IsTrue(result.Entries.Any(e => e.FullPath == "Image0/a.bin"));
        // package untouched — no rename/staging ever happened
        Assert.IsTrue(File.Exists(fixture.PackagePath));
    }

    [TestMethod]
    public async Task Service_CustomPasscode_RoundTrips()
    {
        const string passcode = "0123456789abcdef0123456789abcdef";
        using var fixture = PkgFixture.CreateWithPasscode("passcode", passcode, ("a.bin", 8));

        PkgFileListingResult result = await new PkgFileListingService()
            .ListAsync(fixture.PackagePath, passcode, CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.IsTrue(result.Entries.Any(e => e.FullPath == "Image0/a.bin"));
    }

    [TestMethod]
    public async Task Service_NotAPkg_ReportsFailureWithoutThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), "p4t-garbage-" + Guid.NewGuid().ToString("N") + ".pkg");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[128]);

            PkgFileListingResult result = await new PkgFileListingService()
                .ListAsync(path, PkgFileListingService.DefaultPasscode, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    // ── AddPasscodeArgument (vestigial: still used by extraction, ──────
    //    viewer extraction and shadps4 install spawns until they migrate)

    [TestMethod]
    public void AddPasscodeArgument_SentinelNoPasscode_DefaultAndCustom()
    {
        var sentinel = new ProcessStartInfo();
        PkgFileListingService.AddPasscodeArgument(sentinel, PkgFileListingService.NoPasscode);
        CollectionAssert.AreEqual(new[] { "--no_passcode" }, sentinel.ArgumentList);

        var none = new ProcessStartInfo();
        PkgFileListingService.AddPasscodeArgument(none, null);
        CollectionAssert.AreEqual(
            new[] { "--passcode", PkgFileListingService.DefaultPasscode }, none.ArgumentList);

        var custom = new ProcessStartInfo();
        PkgFileListingService.AddPasscodeArgument(custom, "0123456789abcdef0123456789abcdef");
        CollectionAssert.AreEqual(
            new[] { "--passcode", "0123456789abcdef0123456789abcdef" }, custom.ArgumentList);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static PkgFileListingResult SuccessResult() => new()
    {
        Succeeded = true,
        Entries = new[] { new PkgFileEntry { FullPath = "Image0/file.bin", Name = "file.bin" } }
    };

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

    // PkgFixture is shared across the listing/extraction/inspection test
    // suites - see PkgFixture.cs in this project.
}