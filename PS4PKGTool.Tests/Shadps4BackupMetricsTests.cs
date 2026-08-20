using System;
using System.IO;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Tests;

[TestClass]
public class Shadps4BackupMetricsTests
{
    private string _tempRoot = null!;
    private string _backupPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_backup_metrics_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        // A managed backup: <snapshotId>\data\<slot>\...
        _backupPath = Path.Combine(_tempRoot, "20260815T100000Z_abcdef");
        Directory.CreateDirectory(_backupPath);
        Directory.CreateDirectory(Path.Combine(_backupPath, "data"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_backupPath, "data", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [TestMethod]
    public void Measure_SumsSizeAndCountsSlots()
    {
        WriteFile(Path.Combine("slot1", "sce_sys", "param.sfo"), "sfo");
        WriteFile(Path.Combine("slot1", "userdata0000"), "AAAAAAAA");   // 8 bytes
        WriteFile(Path.Combine("slot2", "userdata0000"), "BBBB");       // 4 bytes
        WriteFile("stray.bin", "CC");                                   // 2 bytes, loose file still a slot

        var m = Shadps4BackupMetrics.Measure(_backupPath);

        Assert.AreEqual(3, m.SlotCount);
        // slot1 = sfo(3) + userdata(8), slot2 = userdata(4), stray.bin = 2
        Assert.AreEqual(3 + 8 + 4 + 2, m.TotalSize);
    }

    [TestMethod]
    public void Measure_ReturnsZeros_WhenDataFolderMissing()
    {
        Directory.Delete(Path.Combine(_backupPath, "data"));

        var m = Shadps4BackupMetrics.Measure(_backupPath);

        Assert.AreEqual(0, m.SlotCount);
        Assert.AreEqual(0, m.TotalSize);
    }

    [TestMethod]
    public void Measure_ReturnsZeros_WhenBackupMissing()
    {
        var m = Shadps4BackupMetrics.Measure(Path.Combine(_tempRoot, "does-not-exist"));

        Assert.AreEqual(0, m.SlotCount);
        Assert.AreEqual(0, m.TotalSize);
    }

    [TestMethod]
    public void Measure_ReturnsZeros_WhenPathNullOrBlank()
    {
        Assert.AreEqual(0, Shadps4BackupMetrics.Measure("").SlotCount);
        Assert.AreEqual(0, Shadps4BackupMetrics.Measure(null!).TotalSize);
    }

    [TestMethod]
    public void Measure_CountsEmptyDataAsZero()
    {
        var m = Shadps4BackupMetrics.Measure(_backupPath);

        Assert.AreEqual(0, m.SlotCount);
        Assert.AreEqual(0, m.TotalSize);
    }
}
