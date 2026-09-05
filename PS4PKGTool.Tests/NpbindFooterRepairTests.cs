using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.Ffpfsc;
using System;
using System.IO;
using System.Security.Cryptography;

namespace PS4PKGTool.Tests;

/// <summary>
/// Tests for <see cref="PS4PKGTool.Utilities.Ffpfsc.NpbindFooterRepair"/> â€”
/// the port of the SadykovIV ps4ffpsc <c>npbind.py</c> reference.
/// </summary>
[TestClass]
public sealed class NpbindFooterRepairTests
{
    private static byte[] MakeNpbind(int entryCount = 1)
    {
        int size = NpbindFooterRepair.HeaderSize + NpbindFooterRepair.EntrySize * entryCount
            + NpbindFooterRepair.DigestSize;
        byte[] data = new byte[size];
        WriteBigEndian32(data, 0, NpbindFooterRepair.Magic);        // magic
        WriteBigEndian32(data, 4, 1);                                // version
        WriteBigEndian64(data, 8, size);                            // declared_size
        WriteBigEndian64(data, 16, NpbindFooterRepair.EntrySize);   // entry_size
        WriteBigEndian64(data, 24, entryCount);                    // entry_count
        byte[] hash = SHA1.HashData(data.AsSpan(0, data.Length - NpbindFooterRepair.DigestSize));
        Buffer.BlockCopy(hash, 0, data, data.Length - NpbindFooterRepair.DigestSize, NpbindFooterRepair.DigestSize);
        return data;
    }

    [DataTestMethod]
    [DataRow(1, 532)]
    [DataRow(3, 1300)]
    public void Inspect_AcceptsValidLayout(int entryCount, int expectedSize)
    {
        byte[] data = MakeNpbind(entryCount);

        var report = NpbindFooterRepair.Inspect(data);

        Assert.AreEqual("valid", report.Status);
        Assert.AreEqual(entryCount, report.EntryCount);
        Assert.AreEqual(expectedSize, report.DeclaredSize);
        Assert.IsTrue(report.FooterValid);
    }

    [TestMethod]
    public void Inspect_DetectsFooterCorruption()
    {
        byte[] data = MakeNpbind();
        data[^1] ^= 0xFF; // corrupt one byte of the footer

        var report = NpbindFooterRepair.Inspect(data);

        Assert.AreEqual("repairable_footer", report.Status);
        Assert.IsFalse(report.FooterValid);
    }

    [TestMethod]
    public void Inspect_RejectsTooSmall()
    {
        var report = NpbindFooterRepair.Inspect(new byte[10]);
        Assert.AreEqual("invalid", report.Status);
        Assert.AreEqual("too small", report.StatusDetail);
    }

    [TestMethod]
    public void Inspect_RejectsBadMagic()
    {
        byte[] data = MakeNpbind();
        data[0] = 0xFF; // bad magic
        var report = NpbindFooterRepair.Inspect(data);
        Assert.AreEqual("invalid", report.Status);
        StringAssert.Contains(report.StatusDetail, "magic mismatch");
    }

    [TestMethod]
    public void Inspect_RejectsWrongVersion()
    {
        byte[] data = MakeNpbind();
        WriteBigEndian32(data, 4, 0); // version 0 — whole 32-bit field, not just data[4]
        var report = NpbindFooterRepair.Inspect(data);
        Assert.AreEqual("invalid", report.Status);
        StringAssert.Contains(report.StatusDetail, "version");
    }

    [TestMethod]
    public void Inspect_RejectsDeclaredSizeMismatch()
    {
        byte[] data = MakeNpbind();
        WriteBigEndian64(data, 8, 999); // wrong declared_size
        var report = NpbindFooterRepair.Inspect(data);
        Assert.AreEqual("invalid", report.Status);
        StringAssert.Contains(report.StatusDetail, "declared size");
    }

    [TestMethod]
    public void Validate_ThrowsOnCorruptFooter()
    {
        byte[] data = MakeNpbind();
        data[^1] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => NpbindFooterRepair.Validate(data));
    }

    [TestMethod]
    public void Repair_RewritesOnlyTheFooter()
    {
        byte[] original = MakeNpbind();
        byte[] damaged = (byte[])original.Clone();
        damaged[^1] = 0x42;

        byte[] repaired = NpbindFooterRepair.Repair(damaged);

        // Body unchanged
        CollectionAssert.AreEqual(
            original.AsSpan(0, original.Length - NpbindFooterRepair.DigestSize).ToArray(),
            repaired.AsSpan(0, repaired.Length - NpbindFooterRepair.DigestSize).ToArray());
        // Footer matches recomputed SHA-1
        byte[] expectedHash = SHA1.HashData(repaired.AsSpan(0, repaired.Length - NpbindFooterRepair.DigestSize));
        CollectionAssert.AreEqual(
            expectedHash,
            repaired.AsSpan(repaired.Length - NpbindFooterRepair.DigestSize, NpbindFooterRepair.DigestSize).ToArray());
        // Now valid
        Assert.IsTrue(NpbindFooterRepair.Verify(repaired));
    }

    [TestMethod]
    public void Repair_DoesNotMutateInput()
    {
        byte[] original = MakeNpbind();
        byte[] damaged = (byte[])original.Clone();
        damaged[^1] = 0x00;

        byte[] repaired = NpbindFooterRepair.Repair(damaged);

        Assert.AreEqual(0x00, damaged[^1]); // input unchanged
        Assert.AreNotEqual(damaged[^1], repaired[^1]);
    }

    [TestMethod]
    public void Repair_NoCopyWhenAlreadyValid()
    {
        byte[] data = MakeNpbind();

        byte[] result = NpbindFooterRepair.Repair(data);

        Assert.IsTrue(ReferenceEquals(data, result));
    }

    [TestMethod]
    public void Repair_ThrowsOnStructurallyInvalid()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => NpbindFooterRepair.Repair(new byte[10]));
    }

    [TestMethod]
    public void Repair_FileOverload_WritesRepairedCopy()
    {
        using var temp = new TempFile();
        byte[] damaged = MakeNpbind();
        damaged[^1] = 0x42;
        File.WriteAllBytes(temp.SourcePath, damaged);

        NpbindFooterRepair.Repair(temp.SourcePath, temp.DestinationPath);

        byte[] repaired = File.ReadAllBytes(temp.DestinationPath);
        Assert.IsTrue(NpbindFooterRepair.Verify(repaired));
    }

    [TestMethod]
    public void TryRepairInPlace_RepairsOnlyWhenFooterIsWrong()
    {
        string root = Path.Combine(Path.GetTempPath(), "npbind-test-" + Guid.NewGuid().ToString("N"));
        string sceSys = Path.Combine(root, "sce_sys");
        Directory.CreateDirectory(sceSys);
        try
        {
            // Valid file â€” no repair needed
            byte[] valid = MakeNpbind();
            string validPath = Path.Combine(sceSys, "npbind.dat");
            File.WriteAllBytes(validPath, valid);
            Assert.IsFalse(NpbindFooterRepair.TryRepairInPlace(root));

            // Corrupted file â€” repair runs
            byte[] damaged = (byte[])valid.Clone();
            damaged[^1] = 0x00;
            File.WriteAllBytes(validPath, damaged);
            Assert.IsTrue(NpbindFooterRepair.TryRepairInPlace(root));
            Assert.IsTrue(NpbindFooterRepair.Verify(File.ReadAllBytes(validPath)));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [TestMethod]
    public void TryRepairInPlace_ReturnsFalseWhenNoNpbind()
    {
        using var temp = new TempDir();
        Assert.IsFalse(NpbindFooterRepair.TryRepairInPlace(temp.Root));
    }

    private static void WriteBigEndian32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static void WriteBigEndian64(byte[] data, int offset, long value)
    {
        for (int i = 0; i < 8; i++)
            data[offset + i] = (byte)((value >> ((7 - i) * 8)) & 0xFF);
    }

    private sealed class TempFile : IDisposable
    {
        public string SourcePath { get; } = Path.GetTempFileName();
        public string DestinationPath { get; } = Path.GetTempFileName();
        public void Dispose()
        {
            try { File.Delete(SourcePath); } catch { }
            try { File.Delete(DestinationPath); } catch { }
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(System.IO.Path.GetTempPath(), "npbind-test-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
