using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities;
using System.Security.Cryptography;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class WorkflowRegressionTests
{
    [TestMethod]
    public void PkgDirectoryScanner_RespectsDepthAndExcludedDirectoriesWithoutDuplicates()
    {
        string root = Path.Combine(Path.GetTempPath(), "pkg_scan_" + Guid.NewGuid().ToString("N"));
        try
        {
            string child = Directory.CreateDirectory(Path.Combine(root, "Child")).FullName;
            string deep = Directory.CreateDirectory(Path.Combine(child, "Deep")).FullName;
            string excluded = Directory.CreateDirectory(Path.Combine(root, "Excluded")).FullName;
            string rootPkg = Path.Combine(root, "root.pkg");
            string childPkg = Path.Combine(child, "child.pkg");
            string deepPkg = Path.Combine(deep, "deep.pkg");
            string excludedPkg = Path.Combine(excluded, "excluded.pkg");
            foreach (string path in new[] { rootPkg, childPkg, deepPkg, excludedPkg })
                File.WriteAllBytes(path, Array.Empty<byte>());

            var savedNonRecursive = PkgDirectoryScanner.Scan(root, recursive: false, new[] { "Excluded" });
            CollectionAssert.AreEquivalent(new[] { rootPkg, childPkg }, savedNonRecursive.Files.ToArray());

            var droppedNonRecursive = PkgDirectoryScanner.Scan(root, recursive: false, new[] { "Excluded" },
                includeImmediateChildrenWhenNonRecursive: false);
            CollectionAssert.AreEquivalent(new[] { rootPkg }, droppedNonRecursive.Files.ToArray());

            var recursive = PkgDirectoryScanner.Scan(root, recursive: true, new[] { "Excluded" });
            CollectionAssert.AreEquivalent(new[] { rootPkg, childPkg, deepPkg }, recursive.Files.ToArray());
            Assert.HasCount(3, recursive.Files.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public void GlvAppVersionRank_UsesBracketedApplicationVersion()
    {
        Assert.AreEqual(1.23m, WorkflowGuards.ParseCombinedAppVersion("01.00 [01.23]"));
        Assert.AreEqual(10.50m, WorkflowGuards.ParseCombinedAppVersion("01.00 [10.50]"));
        Assert.AreEqual(2.00m, WorkflowGuards.ParseCombinedAppVersion("02.00"));
    }

    [TestMethod]
    public void OfficialUpdateIntegrity_AcceptsMatchingSizeAndHash()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] data = "verified update piece"u8.ToArray();
            File.WriteAllBytes(path, data);
            string hash = Convert.ToHexString(SHA256.HashData(data));

            WorkflowGuards.VerifyDownloadedPiece(path, data.Length, data.Length, hash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void OfficialUpdateIntegrity_RejectsTruncationAndWrongHash()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] data = "truncated"u8.ToArray();
            File.WriteAllBytes(path, data);
            string hash = Convert.ToHexString(SHA256.HashData(data));

            Assert.ThrowsExactly<InvalidDataException>(() =>
                WorkflowGuards.VerifyDownloadedPiece(path, data.Length, data.Length + 1, hash));
            Assert.ThrowsExactly<InvalidDataException>(() =>
                WorkflowGuards.VerifyDownloadedPiece(path, data.Length, data.Length, new string('0', 64)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
