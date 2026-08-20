using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.TrophyMetadata;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PS4PKGTool.Tests;

[TestClass]
public sealed class TrophyMetadataTests
{
    private const string NpCommunicationId = "NPWR11036_00";
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    [TestMethod]
    public void SampleTrp_HeaderAndEntryTableAreReadBigEndian()
    {
        string path = WriteVerifiedSampleTrp();
        try
        {
            TrpArchive archive = new TrpReader().Read(path);
            Assert.AreEqual(3u, archive.Version);
            Assert.AreEqual(new FileInfo(path).Length, archive.DeclaredFileSize);
            Assert.AreEqual(3, archive.Entries.Count);
            Assert.AreEqual(64u, archive.EntrySize);
            Assert.AreEqual("TROPCONF.ESFM", archive.Entries[0].Name);
            Assert.AreEqual(96 + 3 * 64, archive.Entries[0].Offset);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SampleTrp_AllOffsetsAreBoundedAndExtractionLengthsAreExact()
    {
        string path = WriteVerifiedSampleTrp();
        try
        {
            var reader = new TrpReader();
            TrpArchive archive = reader.Read(path);
            foreach (TrpEntry entry in archive.Entries)
            {
                Assert.IsTrue(entry.Offset >= 0, entry.Name);
                Assert.IsTrue(entry.Size <= archive.DeclaredFileSize - entry.Offset, entry.Name);
                Assert.AreEqual(entry.Size, reader.ReadEntry(archive, entry).LongLength, entry.Name);
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ResourceDetector_RecognizesEightByteEsfmMarker()
    {
        Assert.AreEqual(
            TrophyResourceKind.EsfmMarker,
            new TrophyResourceDetector().Detect(Encoding.ASCII.GetBytes("ESFM\0\0\0\0")));
    }

    [TestMethod]
    public void ResourceDetector_RecognizesPngBySignatureNotExtension()
    {
        Assert.AreEqual(
            TrophyResourceKind.Png,
            new TrophyResourceDetector().Detect(Png, new TrpEntry { Name = "trophy.ucp" }));
    }

    [TestMethod]
    public void ResourceDetector_LeavesUnknownUcpDataUnknown()
    {
        var detector = new TrophyResourceDetector();
        Assert.AreEqual(TrophyResourceKind.EmptyMarker, detector.Detect(new byte[8], new TrpEntry { Name = "trophy.ucp" }));
        Assert.AreEqual(TrophyResourceKind.Unknown, detector.Detect(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new TrpEntry { Name = "trophy.ucp" }));
    }

    [TestMethod]
    public void NpCommunicationIdValidationIsStrict()
    {
        var resolver = new NpCommunicationIdResolver();
        Assert.IsTrue(resolver.IsValid(NpCommunicationId));
        Assert.IsFalse(resolver.IsValid("CUSA07202"));
        Assert.IsFalse(resolver.IsValid("NPWR11036"));
        Assert.ThrowsExactly<ArgumentException>(() => resolver.Resolve("CUSA07202", null));
    }

    [TestMethod]
    public void NpCommunicationIdCachePersistsValidatedContentIdMapping()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ps4pkgtool-cache-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "np-communication-ids.json");
        try
        {
            var first = new NpCommunicationIdCache(path);
            first.Set("EP0700-CUSA07202_00-ACECOMBATCLOUD07", "npwr11036_00");

            var second = new NpCommunicationIdCache(path);
            Assert.IsTrue(second.TryGet("ep0700-cusa07202_00-acecombatcloud07", out string? value));
            Assert.AreEqual(NpCommunicationId, value);
            Assert.AreEqual(1, second.Count);
            Assert.ThrowsExactly<ArgumentException>(() => first.Set("content", "CUSA07202"));
            second.Clear();
            Assert.AreEqual(0, second.Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TrophyMetadataCacheBuilderHandlesEmptyLibrary()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ps4pkgtool-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            TrophyCacheBuildResult result = await new TrophyMetadataCacheBuilder().BuildAsync(
                new[] { directory }, recursive: true,
                Path.Combine(directory, "cache.json"));
            Assert.AreEqual(0, result.TotalPackages);
            Assert.AreEqual(0, result.Added);
            Assert.AreEqual(0, result.Failed);
            Assert.IsFalse(result.Cancelled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task NpbindExtractorReportsFailureForInvalidPkg()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ps4pkgtool-npbind-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string pkgPath = Path.Combine(directory, "Game™：日本語.pkg");
        File.WriteAllBytes(pkgPath, new byte[] { 1, 2, 3, 4 });
        try
        {
            NpbindExtractionResult result = await new NpbindExtractor().ExtractAsync(pkgPath);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage));
            // The package is opened read-only - never renamed or moved.
            Assert.IsTrue(File.Exists(pkgPath), "The original special-character filename must be untouched.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void SampleTrp_DecryptsAndRecoversAllFieldsAndIcons()
    {
        string path = WriteVerifiedSampleTrp();
        try
        {
            TrophyMetadataResult result = new TrophyMetadataService().Read(path, NpCommunicationId);
            Assert.IsTrue(result.MetadataPayloadFound, result.StatusMessage);
            Assert.IsTrue(result.DecryptionAttempted, result.StatusMessage);
            Assert.IsTrue(result.DecryptionSucceeded, result.StatusMessage);
            Assert.AreEqual(NpCommunicationId, result.NpCommunicationId);
            Assert.AreEqual(2, result.Trophies.Count);

            TrophyInfo platinum = result.Trophies.Single(t => t.Id == 0);
            Assert.AreEqual("Test Platinum", platinum.Name);
            Assert.AreEqual("Obtained all trophies.", platinum.Description);
            Assert.AreEqual(TrophyGrade.Platinum, platinum.Grade);
            Assert.IsFalse(platinum.IsHidden);
            Assert.AreEqual("Base Game", platinum.GroupName);
            Assert.IsNotNull(platinum.IconData);
            CollectionAssert.AreEqual(Png[..8], platinum.IconData[..8]);

            TrophyInfo hidden = result.Trophies.Single(t => t.Id == 1);
            Assert.IsTrue(hidden.IsHidden);
            Assert.AreEqual(0, hidden.PlatinumId);
            Assert.AreEqual("TROP001.PNG", hidden.IconEntryName);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void IncorrectNpCommunicationIdFailsClosed()
    {
        string path = WriteVerifiedSampleTrp();
        try
        {
            TrophyMetadataResult result = new TrophyMetadataService().Read(path, "NPWR00001_00");
            Assert.IsTrue(result.DecryptionAttempted);
            Assert.IsFalse(result.DecryptionSucceeded);
            Assert.IsEmpty(result.Trophies);
            StringAssert.Contains(result.StatusMessage, "Metadata unavailable");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TruncatedInputIsRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => new TrpReader().Read(new MemoryStream(new byte[20])));
    }

    [TestMethod]
    public void InvalidXmlIsRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => new TrophyMetadataParser().Parse("<trophyconf><trophy"u8));
    }

    [TestMethod]
    public void GroupAndPlatinumRelationshipsAreParsed()
    {
        const string xml = "<trophyconf><npcommid>NPWR00001_00</npcommid><group id='001'><name>DLC One</name></group><trophy id='007' hidden='yes' ttype='G' pid='000' gid='001'><name>N</name><detail>D</detail></trophy></trophyconf>";
        TrophyInfo trophy = new TrophyMetadataParser().Parse(Encoding.UTF8.GetBytes(xml), "NPWR00001_00").Single();
        Assert.AreEqual(TrophyGrade.Gold, trophy.Grade);
        Assert.AreEqual(1, trophy.GroupId);
        Assert.AreEqual("DLC One", trophy.GroupName);
        Assert.AreEqual(0, trophy.PlatinumId);
    }

    [TestMethod]
    public void IconResolverMatchesExactThreeDigitId()
    {
        string path = WriteTemporaryTrp(("TROP007.PNG", Png, 0u), ("TROP070.PNG", Png, 0u));
        try
        {
            var reader = new TrpReader();
            TrpArchive archive = reader.Read(path);
            TrophyInfo trophy = new TrophyIconResolver(reader, new TrophyResourceDetector())
                .AttachIcons(archive, new[] { new TrophyInfo { Id = 7 } }).Single();
            Assert.AreEqual("TROP007.PNG", trophy.IconEntryName);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void NoMetadataStillReturnsArchiveWithPng()
    {
        string path = WriteTemporaryTrp(("TROP000.PNG", Png, 0u));
        try
        {
            TrophyMetadataResult result = new TrophyMetadataService().Read(path);
            Assert.IsFalse(result.MetadataPayloadFound);
            Assert.IsFalse(result.DecryptionAttempted);
            Assert.IsEmpty(result.Trophies);
            Assert.AreEqual(1, result.Archive.Entries.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ImpossibleEntryOffsetIsRejected()
    {
        byte[] trp = BuildTrp(("BAD.BIN", new byte[] { 1 }, 0u));
        BinaryPrimitives.WriteUInt32BigEndian(trp.AsSpan(96 + 36, 4), uint.MaxValue);
        Assert.ThrowsExactly<InvalidDataException>(() => new TrpReader().Read(new MemoryStream(trp)));
    }

    private static string WriteVerifiedSampleTrp()
    {
        const string xml = "<trophyconf><npcommid>NPWR11036_00</npcommid>" +
            "<trophy id='000' hidden='no' ttype='P' pid='-1' gid='0'><name>Test Platinum</name><detail>Obtained all trophies.</detail></trophy>" +
            "<trophy id='001' hidden='yes' ttype='B' pid='000' gid='0'><name>Hidden Trophy</name><detail>Hidden detail.</detail></trophy>" +
            "</trophyconf>";
        byte[] encrypted = EncryptEsfm(Encoding.UTF8.GetBytes(xml), NpCommunicationId);
        return WriteTemporaryTrp(
            ("TROPCONF.ESFM", encrypted, 3u),
            ("TROP000.PNG", Png, 0u),
            ("TROP001.PNG", Png, 0u));
    }

    private static byte[] EncryptEsfm(byte[] xml, string npCommunicationId)
    {
        byte[] plaintext = new byte[16 + xml.Length];
        xml.CopyTo(plaintext, 16);
        byte[] titleKey = new EsfmDecryptor().DeriveTitleKey(npCommunicationId);

        using Aes aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = titleKey;
        aes.IV = new byte[16];
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    private static string WriteTemporaryTrp(params (string Name, byte[] Data, uint Flags)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ps4pkgtool-{Guid.NewGuid():N}.trp");
        File.WriteAllBytes(path, BuildTrp(entries));
        return path;
    }

    private static byte[] BuildTrp(params (string Name, byte[] Data, uint Flags)[] entries)
    {
        const int headerSize = 96;
        const int entrySize = 64;
        int cursor = headerSize + entries.Length * entrySize;
        byte[] bytes = new byte[cursor + entries.Sum(entry => entry.Data.Length)];
        new byte[] { 0xDC, 0xA2, 0x4D, 0 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), 3);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), (ulong)bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), entrySize);

        for (int index = 0; index < entries.Length; index++)
        {
            (string name, byte[] data, uint flags) = entries[index];
            int tableOffset = headerSize + index * entrySize;
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length >= 36) throw new ArgumentException("Synthetic TRP entry name is too long.");
            nameBytes.CopyTo(bytes, tableOffset);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tableOffset + 36, 4), (uint)cursor);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(tableOffset + 40, 8), (ulong)data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tableOffset + 48, 4), flags);
            data.CopyTo(bytes, cursor);
            cursor += data.Length;
        }

        return bytes;
    }
}
