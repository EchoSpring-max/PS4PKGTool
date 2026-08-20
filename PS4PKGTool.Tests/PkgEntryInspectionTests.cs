using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PkgInspection;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class PkgEntryInspectionTests
{
    [TestMethod]
    public async Task Session_IsLazyAndLoadsEntriesOnlyOnce()
    {
        var loader = new CountingLoader(PkgEntryLoadResult.Success(new[]
        {
            new PkgEntryInfo { Name = "param.sfo", Offset = "0x1000", Size = "1 KB" }
        }));
        using var session = new PkgEntryInspectionSession("example.pkg", loader);

        Assert.AreEqual(0, loader.LoadCount);
        PkgEntryLoadResult first = await session.LoadAsync(CancellationToken.None);
        PkgEntryLoadResult second = await session.LoadAsync(CancellationToken.None);

        Assert.AreSame(first, second);
        Assert.AreEqual(1, loader.LoadCount);
        Assert.AreEqual("param.sfo", first.Entries[0].Name);
    }

    [TestMethod]
    public async Task Session_DisposeCancelsEntryLoading()
    {
        var loader = new BlockingLoader();
        var session = new PkgEntryInspectionSession("example.pkg", loader);
        Task<PkgEntryLoadResult> loadTask = session.LoadAsync(CancellationToken.None);
        await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        session.Dispose();

        bool cancelled = false;
        try
        {
            await loadTask;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert.IsTrue(cancelled);
    }

    [TestMethod]
    public async Task ParserFailure_IsReturnedAsSectionLocalFailure()
    {
        string path = CreatePackageFile();
        try
        {
            var service = new PkgEntryInspectionService(new ThrowingReader());
            PkgEntryLoadResult result = await service.LoadAsync(path, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, result.Entries.Count);
            StringAssert.Contains(result.ErrorMessage, "entry parser failed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task EntryLoading_DoesNotMutateGlobalEntryDictionaries()
    {
        string[] idEntries = Helper.Entry.EntryIdNameDictionary
            .Select(pair => pair.Key + "=" + pair.Value).OrderBy(value => value).ToArray();
        string[] encryptedEntries = Helper.Entry.EncryptedEntryOffsetNameDictionary
            .Select(pair => pair.Key + "=" + pair.Value).OrderBy(value => value).ToArray();
        var loader = new CountingLoader(PkgEntryLoadResult.Success(new[]
        {
            new PkgEntryInfo { Name = "icon0.png", Encrypted = "1" }
        }));
        using var session = new PkgEntryInspectionSession("example.pkg", loader);

        await session.LoadAsync(CancellationToken.None);

        CollectionAssert.AreEqual(idEntries, Helper.Entry.EntryIdNameDictionary
            .Select(pair => pair.Key + "=" + pair.Value).OrderBy(value => value).ToArray());
        CollectionAssert.AreEqual(encryptedEntries, Helper.Entry.EncryptedEntryOffsetNameDictionary
            .Select(pair => pair.Key + "=" + pair.Value).OrderBy(value => value).ToArray());
    }

    [TestMethod]
    public async Task CompletedEntryInspection_DoesNotKeepPackageFileOpen()
    {
        string path = CreatePackageFile();
        string movedPath = path + ".moved";
        try
        {
            var service = new PkgEntryInspectionService(new FileReadingEntryReader());
            using (var session = new PkgEntryInspectionSession(path, service))
            {
                PkgEntryLoadResult result = await session.LoadAsync(CancellationToken.None);
                Assert.IsTrue(result.Succeeded);
            }

            File.Move(path, movedPath);
            File.Delete(movedPath);
            Assert.IsFalse(File.Exists(movedPath));
        }
        finally
        {
            File.Delete(path);
            File.Delete(movedPath);
        }
    }

    private static string CreatePackageFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkg");
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }

    private sealed class CountingLoader : IPkgEntryLoader
    {
        private readonly PkgEntryLoadResult _result;

        public CountingLoader(PkgEntryLoadResult result) => _result = result;

        public int LoadCount { get; private set; }

        public Task<PkgEntryLoadResult> LoadAsync(string packagePath, CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class BlockingLoader : IPkgEntryLoader
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PkgEntryLoadResult> LoadAsync(
            string packagePath, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return PkgEntryLoadResult.Success(Array.Empty<PkgEntryInfo>());
        }
    }

    private sealed class ThrowingReader : IPkgEntryReader
    {
        public IReadOnlyList<PkgEntryInfo> Read(
            string packagePath, CancellationToken cancellationToken) =>
            throw new InvalidDataException("entry parser failed");
    }

    private sealed class FileReadingEntryReader : IPkgEntryReader
    {
        public IReadOnlyList<PkgEntryInfo> Read(string packagePath, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = stream.ReadByte();
            return Array.Empty<PkgEntryInfo>();
        }
    }
}
