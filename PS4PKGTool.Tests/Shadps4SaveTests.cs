using System;
using System.IO;
using System.Linq;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Tests;

[TestClass]
public class Shadps4SaveTests
{
    private string _tempRoot = null!;
    private string _userDir = null!;
    private string _backupRoot = null!;
    private MutableClock _clock = null!;

    private static readonly DateTime T = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

    private sealed class MutableClock : IClock
    {
        public DateTime Now;
        public MutableClock(DateTime now) => Now = now;
        public DateTime UtcNow => Now;
    }

    /// <summary>Wraps the real filesystem and can fail any single operation.</summary>
    private sealed class FaultyFileSystemOps : IFileSystemOps
    {
        private readonly IFileSystemOps _inner = new RealFileSystemOps();
        public int CopyCalls, MoveCalls, DeleteCalls;
        public int? FailCopyAt, FailMoveAt, FailDeleteAt;
        public long FreeSpace = long.MaxValue;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CopyDirectory(string source, string destination)
        {
            if (FailCopyAt.HasValue && ++CopyCalls == FailCopyAt.Value) throw new IOException("fault injected");
            _inner.CopyDirectory(source, destination);
        }
        public void MoveDirectory(string source, string destination)
        {
            if (FailMoveAt.HasValue && ++MoveCalls == FailMoveAt.Value) throw new IOException("fault injected");
            _inner.MoveDirectory(source, destination);
        }
        public void DeleteDirectory(string path)
        {
            if (FailDeleteAt.HasValue && ++DeleteCalls == FailDeleteAt.Value) throw new IOException("fault injected");
            _inner.DeleteDirectory(path);
        }
        public long GetFreeSpace(string path) => FreeSpace;
        public long GetDirectorySize(string path) => _inner.GetDirectorySize(path);
    }

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_saves_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _userDir = Path.Combine(_tempRoot, "userdir");
        _backupRoot = Path.Combine(_tempRoot, "backups");
        _clock = new MutableClock(T);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    /// <summary>Creates a fake save folder: &lt;userDir&gt;\home\&lt;user&gt;\savedata\&lt;titleId&gt;\&lt;slot&gt;\</summary>
    private string MakeSave(string userId, string titleId, string slot, string content)
    {
        string p = Path.Combine(Shadps4SaveStore.SavesRoot(_userDir), userId, "savedata", titleId, slot);
        Directory.CreateDirectory(p);
        Directory.CreateDirectory(Path.Combine(p, "sce_sys"));
        File.WriteAllText(Path.Combine(p, "sce_sys", "param.sfo"), "fake sfo");
        File.WriteAllText(Path.Combine(p, "userdata0000"), content);
        return p;
    }

    private static Shadps4SaveStore NewStore(IFileSystemOps fs, Func<string, string?>? titleResolver = null)
        => new(new MutableClock(T), fs, titleResolver);

    // ── browse ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ListSaveGames_EnumeratesAllUsersAndOrphans()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        MakeSave("1001", "CUSA00900", "SaveData0", "other user");
        MakeSave("1000", "CUSA99999", "Slot0", "orphan save");

        var games = NewStore(new RealFileSystemOps()).ListSaveGames(_userDir);

        Assert.AreEqual(3, games.Count);
        var g1 = games.Single(g => g.UserId == "1000" && g.TitleId == "CUSA00900");
        var orphan = games.Single(g => g.TitleId == "CUSA99999");
        Assert.AreEqual("1000", orphan.UserId);
        Assert.AreEqual(1, g1.Slots.Count);
        Assert.AreEqual("SPRJ0005", g1.Slots[0].Name);
        Assert.IsTrue(g1.Slots[0].RecognizedSlot);
        Assert.IsTrue(g1.TotalSize > 0);
    }

    [TestMethod]
    public void ListSaveGames_ResolvesTitleAndClassifiesEntries()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "x");
        string gamePath = Path.Combine(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900"));
        // A non-slot directory and a loose file at the game root.
        Directory.CreateDirectory(Path.Combine(gamePath, "misc"));
        File.WriteAllText(Path.Combine(gamePath, "loose.dat"), "loose");

        var store = NewStore(new RealFileSystemOps(), _ => "Bloodborne");
        var game = store.ListSaveGames(_userDir).Single();

        Assert.AreEqual("Bloodborne", game.Title);
        Assert.AreEqual(3, game.Slots.Count);
        Assert.IsTrue(game.Slots.Single(s => s.Name == "SPRJ0005").RecognizedSlot);
        Assert.IsFalse(game.Slots.Single(s => s.Name == "misc").RecognizedSlot);
        Assert.IsFalse(game.Slots.Single(s => s.Name == "loose.dat").RecognizedSlot);
    }

    [TestMethod]
    public void ListSaveGames_BrokenTitle_KeepsTheGame()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "x");

        var games = NewStore(new RealFileSystemOps(), _ => null).ListSaveGames(_userDir);

        Assert.AreEqual(1, games.Count);
        Assert.IsNull(games[0].Title);
        Assert.AreEqual("CUSA00900", games[0].TitleId);
    }

    [TestMethod]
    public void ListSaveGames_NoSaves_IsEmpty()
    {
        Assert.AreEqual(0, NewStore(new RealFileSystemOps()).ListSaveGames(_userDir).Count);
    }

    // ── manifest ───────────────────────────────────────────────────────

    [TestMethod]
    public void Manifest_RoundTrips()
    {
        var manifest = new Shadps4SaveManifest
        {
            SchemaVersion = Shadps4SaveManifest.CurrentSchemaVersion,
            SnapshotId = "20260815T100000Z_abcd12",
            TitleId = "CUSA00900",
            UserId = "1000",
            BackedUpAtUtc = T,
            OriginalSourcePath = @"C:\x\savedata",
            Consistency = Shadps4SaveManifest.ConsistencyLiveUnverified,
        };
        string path = Path.Combine(_tempRoot, "manifest.json");
        Shadps4SaveManifest.Write(path, manifest);

        var loaded = Shadps4SaveManifest.TryLoad(path);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("CUSA00900", loaded!.TitleId);
        Assert.AreEqual("1000", loaded.UserId);
        Assert.AreEqual(Shadps4SaveManifest.ConsistencyLiveUnverified, loaded.Consistency);
    }

    [TestMethod]
    public void Manifest_CorruptOrForeignSchema_IsRejected()
    {
        string corrupt = Path.Combine(_tempRoot, "corrupt.json");
        File.WriteAllText(corrupt, "{ not json");
        Assert.IsNull(Shadps4SaveManifest.TryLoad(corrupt));

        string foreign = Path.Combine(_tempRoot, "foreign.json");
        File.WriteAllText(foreign, "{\"schemaVersion\": 99, \"snapshotId\": \"x\", \"titleId\": \"CUSA1\", \"userId\": \"1000\", \"backedUpAtUtc\": \"2026-01-01T00:00:00Z\"}");
        Assert.IsNull(Shadps4SaveManifest.TryLoad(foreign));

        Assert.IsNull(Shadps4SaveManifest.TryLoad(Path.Combine(_tempRoot, "missing.json")));
    }

    // ── backup ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Backup_CreatesSnapshotWithManifestAndData()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());

        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, liveUnverified: false);

        Assert.IsNotNull(backup);
        Assert.IsTrue(Directory.Exists(backup!.Path));
        Assert.IsTrue(File.Exists(Path.Combine(backup.Path, "manifest.json")));
        Assert.IsTrue(File.Exists(Path.Combine(backup.Path, "data", "SPRJ0005", "userdata0000")));
        Assert.IsFalse(backup.LiveUnverified);
        // The snapshot captures the slot metadata too - never regenerated.
        Assert.IsTrue(File.Exists(Path.Combine(backup.Path, "data", "SPRJ0005", "sce_sys", "param.sfo")));
    }

    [TestMethod]
    public void Backup_SecondBackupAddsNewestFirst()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var clock = new MutableClock(T);
        var store = new Shadps4SaveStore(clock, new RealFileSystemOps());

        store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        clock.Now = T.AddHours(1);
        store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, true);

        var backups = store.ListBackups(_backupRoot, "1000", "CUSA00900");

        Assert.AreEqual(2, backups.Count);
        Assert.IsTrue(backups[0].BackedUpAtUtc > backups[1].BackedUpAtUtc, "newest first");
        Assert.IsTrue(backups[0].LiveUnverified);
        Assert.IsFalse(backups[1].LiveUnverified);
    }

    [TestMethod]
    public void ListBackups_IgnoresFoldersWithoutManifest()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());
        store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        // Foreign folder copied in by hand - never a restore source.
        string foreign = Path.Combine(Shadps4SaveStore.GameBackupsDir(_backupRoot, "1000", "CUSA00900"), "handmade");
        Directory.CreateDirectory(foreign);

        Assert.AreEqual(1, store.ListBackups(_backupRoot, "1000", "CUSA00900").Count);
    }

    // ── restore ────────────────────────────────────────────────────────

    [TestMethod]
    public void Restore_ReplacesLiveSaveAndKeepsSafetyBackup()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());
        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        // The game overwrites its save.
        File.WriteAllText(Path.Combine(slot, "userdata0000"), "v2");

        var result = store.RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.Restored, result.Status);
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(slot, "userdata0000")));
        // Journal directories are gone.
        Assert.IsFalse(Directory.Exists(Path.Combine(Shadps4SaveStore.UserSavesDir(_userDir, "1000"), ".CUSA00900.previous")));
        Assert.IsFalse(Directory.Exists(Path.Combine(Shadps4SaveStore.UserSavesDir(_userDir, "1000"), ".CUSA00900.restore")));
        // Safety backup (v2) plus the original backup.
        Assert.AreEqual(2, store.ListBackups(_backupRoot, "1000", "CUSA00900").Count);
        Assert.IsNotNull(result.SafetyBackup);
    }

    [TestMethod]
    public void Restore_RefusedWhileCoreRunning()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());
        store.IsCoreRunning = () => true;
        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);

        var result = store.RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.RefusedRunning, result.Status);
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(slot, "userdata0000")));
        Assert.IsNull(result.SafetyBackup);
    }

    [TestMethod]
    public void Restore_SecondCheckCatchesEmulatorStartingMidRestore()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());
        int calls = 0;
        store.IsCoreRunning = () => ++calls > 1; // stopped at check 1, running at check 2
        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);

        var result = store.RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.RefusedRunning, result.Status);
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(slot, "userdata0000")));
        // Staging was discarded.
        Assert.IsFalse(Directory.Exists(Path.Combine(Shadps4SaveStore.UserSavesDir(_userDir, "1000"), ".CUSA00900.restore")));
        Assert.IsNotNull(result.SafetyBackup, "the safety backup was still taken before staging");
    }

    [TestMethod]
    public void Restore_RefusedWhenSpaceInsufficient()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var fs = new FaultyFileSystemOps { FreeSpace = 0 };
        var store = NewStore(fs);
        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);

        var result = store.RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.SpaceInsufficient, result.Status);
        Assert.IsNull(result.SafetyBackup);
    }

    [TestMethod]
    public void Restore_SafetyBackupFailure_AbortsWithCurrentUntouched()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var backup = NewStore(new RealFileSystemOps()).BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        var fs = new FaultyFileSystemOps { FailCopyAt = 1 }; // the auto-backup copy

        var result = new Shadps4SaveStore(new MutableClock(T), fs)
            .RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.Failed, result.Status);
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(slot, "userdata0000")));
    }

    [TestMethod]
    public void Restore_StagingFailure_LeavesCurrentUntouched()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var backup = NewStore(new RealFileSystemOps()).BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        var fs = new FaultyFileSystemOps { FailCopyAt = 2 }; // restore copy 1 = auto-backup, copy 2 = staging

        var result = new Shadps4SaveStore(new MutableClock(T), fs)
            .RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.Failed, result.Status);
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(slot, "userdata0000")));
    }

    [TestMethod]
    public void Restore_FirstRenameFailure_LeavesCurrentUntouched()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var backup = NewStore(new RealFileSystemOps()).BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        var fs = new FaultyFileSystemOps { FailMoveAt = 1 }; // current → previous

        var result = new Shadps4SaveStore(new MutableClock(T), fs)
            .RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.Failed, result.Status);
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(slot, "userdata0000")));
    }

    [TestMethod]
    public void Restore_SecondRenameFailure_RollsBackPrevious()
    {
        string slot = MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());
        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);
        File.WriteAllText(Path.Combine(slot, "userdata0000"), "v2");

        var fs = new FaultyFileSystemOps { FailMoveAt = 2 }; // .restore → current
        var store2 = new Shadps4SaveStore(new MutableClock(T), fs);

        var result = store2.RestoreSave(_userDir, "1000", "CUSA00900", backup!, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.Failed, result.Status);
        // The previous save was put back.
        Assert.AreEqual("v2", File.ReadAllText(Path.Combine(slot, "userdata0000")));
    }

    [TestMethod]
    public void Restore_BackupOutsideManagedRoot_IsRefused()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        var store = NewStore(new RealFileSystemOps());
        var rogue = new Shadps4SaveBackup("rogue", "CUSA00900", "1000", T, false,
            Path.Combine(_tempRoot, "elsewhere"));

        var result = store.RestoreSave(_userDir, "1000", "CUSA00900", rogue, _backupRoot);

        Assert.AreEqual(Shadps4SaveRestoreStatus.Failed, result.Status);
        Assert.IsTrue(result.Message.Contains("outside the managed backup root"));
    }

    // ── interrupted-restore recovery ───────────────────────────────────

    private string SavedataDir() => Shadps4SaveStore.UserSavesDir(_userDir, "1000");

    [TestMethod]
    public void Recovery_StaleStaging_IsDiscarded()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        Directory.CreateDirectory(Path.Combine(SavedataDir(), ".CUSA00900.restore"));

        string report = NewStore(new RealFileSystemOps()).RecoverInterruptedRestore(_userDir);

        Assert.IsTrue(report.Contains("Discarded"));
        Assert.IsTrue(Directory.Exists(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900")));
        Assert.IsFalse(Directory.Exists(Path.Combine(SavedataDir(), ".CUSA00900.restore")));
    }

    [TestMethod]
    public void Recovery_InterruptedSwap_CompletesFromStaging()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        // Simulate a crash between "current → previous" and ".restore → current".
        Directory.Move(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900"),
            Path.Combine(SavedataDir(), ".CUSA00900.previous"));
        string staging = Path.Combine(SavedataDir(), ".CUSA00900.restore");
        Directory.CreateDirectory(Path.Combine(staging, "SPRJ0005"));
        File.WriteAllText(Path.Combine(staging, "SPRJ0005", "userdata0000"), "v3");

        string report = NewStore(new RealFileSystemOps()).RecoverInterruptedRestore(_userDir);

        Assert.IsTrue(report.Contains("Completed"));
        string live = Path.Combine(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900"), "SPRJ0005", "userdata0000");
        Assert.IsTrue(File.Exists(live));
        Assert.AreEqual("v3", File.ReadAllText(live), "the staged restore wins");
        Assert.IsFalse(Directory.Exists(Path.Combine(SavedataDir(), ".CUSA00900.previous")));
    }

    [TestMethod]
    public void Recovery_PreviousOnly_RollsBack()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        Directory.Move(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900"),
            Path.Combine(SavedataDir(), ".CUSA00900.previous"));

        string report = NewStore(new RealFileSystemOps()).RecoverInterruptedRestore(_userDir);

        Assert.IsTrue(report.Contains("Rolled back"));
        Assert.IsTrue(Directory.Exists(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900")));
    }

    [TestMethod]
    public void Recovery_CompletedSwap_CleansUp()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        Directory.CreateDirectory(Path.Combine(SavedataDir(), ".CUSA00900.previous"));
        Directory.CreateDirectory(Path.Combine(SavedataDir(), ".CUSA00900.restore"));

        string report = NewStore(new RealFileSystemOps()).RecoverInterruptedRestore(_userDir);

        Assert.IsTrue(report.Contains("Cleaned up"));
        Assert.IsTrue(Directory.Exists(Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900")));
        Assert.IsFalse(Directory.Exists(Path.Combine(SavedataDir(), ".CUSA00900.previous")));
        Assert.IsFalse(Directory.Exists(Path.Combine(SavedataDir(), ".CUSA00900.restore")));
    }

    [TestMethod]
    public void Recovery_NothingToRepair_ReportsNothing()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        Assert.AreEqual("", NewStore(new RealFileSystemOps()).RecoverInterruptedRestore(_userDir));
    }

    // ── reparse-point guard ────────────────────────────────────────────

    [TestMethod]
    public void Backup_DoesNotTraverseReparsePoints()
    {
        MakeSave("1000", "CUSA00900", "SPRJ0005", "v1");
        // A junction inside the save pointing outside - must not be traversed.
        string target = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "leak.txt"), "secret");
        string gamePath = Shadps4SaveStore.GameSavePath(_userDir, "1000", "CUSA00900");
        string junction = Path.Combine(gamePath, "link");
        System.Diagnostics.Process.Start("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"").WaitForExit();

        var store = NewStore(new RealFileSystemOps());
        var backup = store.BackupSave(_userDir, "1000", "CUSA00900", _backupRoot, false);

        Assert.IsNotNull(backup);
        Assert.IsFalse(File.Exists(Path.Combine(backup!.Path, "data", "link", "leak.txt")),
            "external reparse targets must never be copied");
    }
}
