using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities;
using System.Security.Cryptography;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class WorkflowRegressionTests
{
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
