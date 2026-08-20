using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgInspection;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.TrophyMetadata;
using System.Drawing;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class TrophyInspectionTests
{
    [TestMethod]
    public async Task Session_IsLazyAndCachesFirstSuccessfulResult()
    {
        var loader = new CountingLoader(LoadedResult());
        using var session = new TrophyInspectionSession("sample.pkg", loader);

        Assert.IsFalse(session.HasStarted);
        Assert.AreEqual(0, loader.LoadCount);

        TrophyInspectionResult first = await session.LoadAsync("CONTENT", CancellationToken.None);
        TrophyInspectionResult second = await session.LoadAsync("CONTENT", CancellationToken.None);

        Assert.IsTrue(session.HasStarted);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, loader.LoadCount);
    }

    [TestMethod]
    public async Task ValidResource_IsMappedAndTemporaryResourcesAreCleaned()
    {
        string root = CreateTemporaryDirectory();
        var extractor = new WritingExtractor(PkgTrophyResourceStatus.Extracted);
        var metadata = new StubMetadataReader(ValidMetadata());
        var service = CreateService(root, extractor, metadata, new BitmapDecoder());
        TrophyInspectionResult result = await service.LoadAsync(
            "sample.pkg", "UP0000-CUSA00001_00-TEST000000000000", CancellationToken.None);

        try
        {
            Assert.AreEqual(TrophyInspectionStatus.Loaded, result.Status);
            Assert.AreEqual(1, result.Trophies.Count);
            Assert.AreEqual("First Trophy", result.Trophies[0].Name);
            Assert.AreEqual("Gold", result.Trophies[0].Grade);
            Assert.IsNotNull(result.Trophies[0].Icon);
            Assert.AreEqual(1, metadata.ReadCount);
            AssertDirectoryHasNoChildren(root);
        }
        finally
        {
            result.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task NoTrophyResource_IsReportedWithoutMetadataParsing()
    {
        string root = CreateTemporaryDirectory();
        var metadata = new StubMetadataReader(ValidMetadata());
        try
        {
            TrophyInspectionResult result = await CreateService(
                root,
                new WritingExtractor(PkgTrophyResourceStatus.NotFound),
                metadata,
                new BitmapDecoder()).LoadAsync("sample.pkg", "CONTENT", CancellationToken.None);
            using (result)
            {
                Assert.AreEqual(TrophyInspectionStatus.NoTrophyResource, result.Status);
                Assert.AreEqual(0, metadata.ReadCount);
            }
            AssertDirectoryHasNoChildren(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task EncryptedTrophyResource_IsReportedAsInaccessible()
    {
        string root = CreateTemporaryDirectory();
        var metadata = new StubMetadataReader(ValidMetadata());
        try
        {
            TrophyInspectionResult result = await CreateService(
                root,
                new WritingExtractor(PkgTrophyResourceStatus.Inaccessible),
                metadata,
                new BitmapDecoder()).LoadAsync("sample.pkg", "CONTENT", CancellationToken.None);
            using (result)
            {
                Assert.AreEqual(TrophyInspectionStatus.Inaccessible, result.Status);
                StringAssert.Contains(result.Message, "encrypted");
                Assert.AreEqual(0, metadata.ReadCount);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task MalformedTrp_IsSectionLocalFailureAndCleansTemporaryResources()
    {
        string root = CreateTemporaryDirectory();
        var packageSnapshot = new PkgInspectionSnapshot { Title = "Still available" };
        try
        {
            TrophyInspectionResult result = await CreateService(
                root,
                new WritingExtractor(PkgTrophyResourceStatus.Extracted),
                new SharedTrophyMetadataReader(),
                new BitmapDecoder()).LoadAsync("sample.pkg", "CONTENT", CancellationToken.None);
            using (result)
            {
                Assert.AreEqual(TrophyInspectionStatus.Failed, result.Status);
                Assert.AreEqual("Still available", packageSnapshot.Title);
            }
            AssertDirectoryHasNoChildren(root);
        }
        finally
        {
            packageSnapshot.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task MetadataParserFailure_IsSectionLocalAndCleansTemporaryResources()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            TrophyInspectionResult result = await CreateService(
                root,
                new WritingExtractor(PkgTrophyResourceStatus.Extracted),
                new ThrowingMetadataReader(),
                new BitmapDecoder()).LoadAsync("sample.pkg", "CONTENT", CancellationToken.None);
            using (result)
            {
                Assert.AreEqual(TrophyInspectionStatus.Failed, result.Status);
                StringAssert.Contains(result.Message, "metadata parser failed");
            }
            AssertDirectoryHasNoChildren(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task IconDecodeFailure_DoesNotDiscardReadableTrophyMetadata()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            TrophyInspectionResult result = await CreateService(
                root,
                new WritingExtractor(PkgTrophyResourceStatus.Extracted),
                new StubMetadataReader(ValidMetadata()),
                new ThrowingImageDecoder()).LoadAsync("sample.pkg", "CONTENT", CancellationToken.None);
            using (result)
            {
                Assert.AreEqual(TrophyInspectionStatus.Loaded, result.Status);
                Assert.AreEqual(1, result.Trophies.Count);
                Assert.IsNull(result.Trophies[0].Icon);
                Assert.AreEqual(1, result.IconDecodeFailures);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task ViewerSessionClose_CancelsLoadAndCleansTemporaryResources()
    {
        string root = CreateTemporaryDirectory();
        var extractor = new BlockingExtractor();
        var service = CreateService(
            root, extractor, new StubMetadataReader(ValidMetadata()), new BitmapDecoder());
        var session = new TrophyInspectionSession("sample.pkg", service);
        Task<TrophyInspectionResult> load = session.LoadAsync("CONTENT", CancellationToken.None);
        await extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        session.Dispose();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await load);
        AssertDirectoryHasNoChildren(root);
        Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public async Task LoadingDoesNotMutateLegacyGlobalTrophyState()
    {
        var originalReader = Helper.Trophy.trophy;
        Image[] originalImages = Helper.Trophy.ImageToExtractList.ToArray();
        string[] originalNames = Helper.Trophy.TrophyFilenameToExtractList.ToArray();
        var loader = new CountingLoader(LoadedResult());
        using var session = new TrophyInspectionSession("sample.pkg", loader);

        await session.LoadAsync("CONTENT", CancellationToken.None);

        Assert.AreSame(originalReader, Helper.Trophy.trophy);
        CollectionAssert.AreEqual(originalImages, Helper.Trophy.ImageToExtractList.ToArray());
        CollectionAssert.AreEqual(originalNames, Helper.Trophy.TrophyFilenameToExtractList.ToArray());
    }

    [TestMethod]
    public void ResultDisposal_DisposesAllViewerOwnedImagesAndIsIdempotent()
    {
        var first = new Bitmap(3, 3);
        var second = new Bitmap(4, 4);
        var result = new TrophyInspectionResult
        {
            Status = TrophyInspectionStatus.Loaded,
            Trophies = new[]
            {
                new TrophyInspectionItem { Icon = first },
                new TrophyInspectionItem { Icon = second }
            }
        };

        result.Dispose();
        result.Dispose();

        Assert.IsTrue(result.IsDisposed);
        Assert.ThrowsExactly<ArgumentException>(() => _ = first.Width);
        Assert.ThrowsExactly<ArgumentException>(() => _ = second.Width);
    }

    [TestMethod]
    public async Task CompletedLoadAndSessionDisposal_ReleasePackageFile()
    {
        string packagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkg");
        string movedPath = packagePath + ".moved";
        File.WriteAllBytes(packagePath, new byte[32]);
        var loader = new FileReadingLoader();
        try
        {
            using (var session = new TrophyInspectionSession(packagePath, loader))
            {
                using TrophyInspectionResult result = await session.LoadAsync(
                    "CONTENT", CancellationToken.None);
            }

            File.Move(packagePath, movedPath);
            File.Delete(movedPath);
            Assert.IsFalse(File.Exists(movedPath));
        }
        finally
        {
            File.Delete(packagePath);
            File.Delete(movedPath);
        }
    }

    private static TrophyInspectionService CreateService(
        string root,
        IPkgTrophyResourceExtractor extractor,
        ITrophyMetadataReader metadataReader,
        ITrophyImageDecoder imageDecoder) =>
        new TrophyInspectionService(
            extractor, metadataReader, new FixedIdProvider(), imageDecoder, root);

    private static TrophyMetadataResult ValidMetadata() => new()
    {
        Trophies = new[]
        {
            new TrophyInfo
            {
                Id = 7,
                Name = "First Trophy",
                Description = "Description",
                Grade = TrophyGrade.Gold,
                IconData = new byte[] { 1, 2, 3 }
            }
        },
        MetadataPayloadFound = true,
        MetadataEntryName = "TROP.SFM",
        StatusMessage = "Loaded 1 trophy."
    };

    private static TrophyInspectionResult LoadedResult() => new()
    {
        Status = TrophyInspectionStatus.Loaded,
        Trophies = new[] { new TrophyInspectionItem { Name = "Trophy" } }
    };

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ps4pkgtool-trophy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertDirectoryHasNoChildren(string root) =>
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(root));

    private sealed class CountingLoader : ITrophyInspectionLoader
    {
        private readonly TrophyInspectionResult _result;

        public CountingLoader(TrophyInspectionResult result) => _result = result;
        public int LoadCount { get; private set; }

        public Task<TrophyInspectionResult> LoadAsync(
            string packagePath, string contentId, CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FileReadingLoader : ITrophyInspectionLoader
    {
        public Task<TrophyInspectionResult> LoadAsync(
            string packagePath, string contentId, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = stream.ReadByte();
            return Task.FromResult(new TrophyInspectionResult
            {
                Status = TrophyInspectionStatus.NoTrophyResource
            });
        }
    }

    private sealed class WritingExtractor : IPkgTrophyResourceExtractor
    {
        private readonly PkgTrophyResourceStatus _status;

        public WritingExtractor(PkgTrophyResourceStatus status) => _status = status;

        public PkgTrophyResourceResult Extract(
            string packagePath, string temporaryDirectory, CancellationToken cancellationToken)
        {
            string trpPath = Path.Combine(temporaryDirectory, "sample.trp");
            File.WriteAllBytes(trpPath, new byte[] { 1, 2, 3, 4 });
            return new PkgTrophyResourceResult
            {
                Status = _status,
                TrpPath = trpPath,
                Message = _status switch
                {
                    PkgTrophyResourceStatus.NotFound => "No trophy data was found in this package.",
                    PkgTrophyResourceStatus.Inaccessible => "The trophy resource is encrypted.",
                    _ => string.Empty
                }
            };
        }
    }

    private sealed class BlockingExtractor : IPkgTrophyResourceExtractor
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PkgTrophyResourceResult Extract(
            string packagePath, string temporaryDirectory, CancellationToken cancellationToken)
        {
            File.WriteAllText(Path.Combine(temporaryDirectory, "partial.trp"), "partial");
            Started.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Cancellation was not observed.");
        }
    }

    private sealed class StubMetadataReader : ITrophyMetadataReader
    {
        private readonly TrophyMetadataResult _result;

        public StubMetadataReader(TrophyMetadataResult result) => _result = result;
        public int ReadCount { get; private set; }

        public TrophyMetadataResult Read(string trpPath, string npCommunicationId)
        {
            ReadCount++;
            return _result;
        }
    }

    private sealed class ThrowingMetadataReader : ITrophyMetadataReader
    {
        public TrophyMetadataResult Read(string trpPath, string npCommunicationId) =>
            throw new InvalidDataException("metadata parser failed");
    }

    private sealed class FixedIdProvider : INpCommunicationIdProvider
    {
        public string Find(string contentId) => "NPWR00001_00";
    }

    private sealed class BitmapDecoder : ITrophyImageDecoder
    {
        public Image Decode(byte[] bytes) => new Bitmap(8, 8);
    }

    private sealed class ThrowingImageDecoder : ITrophyImageDecoder
    {
        public Image Decode(byte[] bytes) => throw new ArgumentException("Invalid icon image");
    }
}
