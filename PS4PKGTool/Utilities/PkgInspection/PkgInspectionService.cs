using ByteSizeLib;
using OrbisPkgTool.Pkg;
using PS4PKGTool.Utilities.PkgMeta;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS4PKGTool.Utilities.PkgInspection
{
    internal sealed class RawPkgInspectionData
    {
        public string Title { get; init; }
        public string TitleId { get; init; }
        public string ContentId { get; init; }
        public string PackageCategory { get; init; }
        public string PackageState { get; init; }
        public string ApplicationVersion { get; init; }
        public IReadOnlyList<PkgSfoEntry> SfoEntries { get; init; } = Array.Empty<PkgSfoEntry>();
        public IReadOnlyList<PkgInspectionField> HeaderFields { get; init; } = Array.Empty<PkgInspectionField>();
        public byte[] Icon0Bytes { get; init; }
        public byte[] Pic0Bytes { get; init; }
        public byte[] Pic1Bytes { get; init; }
    }

    internal interface IPkgInspectionReader
    {
        RawPkgInspectionData Read(string packagePath);
    }

    internal sealed class Ps4ToolsPkgInspectionReader : IPkgInspectionReader
    {
        public RawPkgInspectionData Read(string packagePath)
        {
            // This remains the authoritative parser. Projecting immediately into
            // an immutable raw model prevents the viewer from touching Main's globals.
            var package = PkgMetadataReader.Read(packagePath);
            var entries = package.SfoTables == null
                ? Array.Empty<PkgSfoEntry>()
                : package.SfoTables
                    .Select(entry => new PkgSfoEntry(entry.Name, entry.Value))
                    .ToArray();
            var headerFields = ReadHeaderFields(packagePath, package);

            return new RawPkgInspectionData
            {
                Title = package.PS4_Title,
                TitleId = package.TITLEID,
                ContentId = package.SfoContentId != "" ? package.SfoContentId : package.Content_ID,
                PackageCategory = package.PKG_Type.ToString(),
                PackageState = package.PKGState.ToString(),
                ApplicationVersion = package.APP_VER,
                SfoEntries = entries,
                HeaderFields = headerFields,
                Icon0Bytes = CloneBytes(package.Icon),
                Pic0Bytes = CloneBytes(package.Pic0),
                Pic1Bytes = CloneBytes(package.Pic1)
            };
        }

        private static byte[] CloneBytes(byte[] bytes) => bytes == null ? null : (byte[])bytes.Clone();

        private static IReadOnlyList<PkgInspectionField> ReadHeaderFields(
            string packagePath,
            PkgMetadata package)
        {
            try
            {
                var rows = PkgHeaderDump.Rows(package.Header, ReadHeaderBytes(packagePath));
                return rows.Select(r => new PkgInspectionField(r.Type, r.Value)).ToArray();
            }
            catch
            {
                // Unknown or damaged header values are optional inspection data.
                return Array.Empty<PkgInspectionField>();
            }
        }

        private static byte[] ReadHeaderBytes(string packagePath)
        {
            using var file = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[PkgHeaderDump.HeaderBytes];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = file.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
            }
            Array.Resize(ref buffer, read);
            return buffer;
        }
    }

    internal sealed class PkgInspectionService
    {
        private readonly IPkgInspectionReader _reader;

        public PkgInspectionService()
            : this(new Ps4ToolsPkgInspectionReader())
        {
        }

        internal PkgInspectionService(IPkgInspectionReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public Task<PkgInspectionSnapshot> InspectAsync(string packagePath, CancellationToken cancellationToken)
        {
            return Task.Run(() => Inspect(packagePath, cancellationToken), cancellationToken);
        }

        internal PkgInspectionSnapshot Inspect(string packagePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("A package path is required.", nameof(packagePath));

            string fullPath = Path.GetFullPath(packagePath);
            cancellationToken.ThrowIfCancellationRequested();

            RawPkgInspectionData raw = _reader.Read(fullPath)
                ?? throw new InvalidDataException("The package reader returned no package metadata.");

            cancellationToken.ThrowIfCancellationRequested();
            return Map(fullPath, raw);
        }

        internal static PkgInspectionSnapshot Map(string packagePath, RawPkgInspectionData raw)
        {
            if (raw == null)
                throw new ArgumentNullException(nameof(raw));

            IReadOnlyList<PkgSfoEntry> entries = raw.SfoEntries ?? Array.Empty<PkgSfoEntry>();
            string packageVersion = FindSfoValue(entries, "VERSION");
            string requiredFirmware = FormatSystemVersion(FindSfoValue(entries, "SYSTEM_VER"));
            long size = File.Exists(packagePath) ? new FileInfo(packagePath).Length : 0;

            Bitmap icon = null;
            Bitmap pic0 = null;
            Bitmap pic1 = null;
            try
            {
                icon = CreateDetachedBitmap(raw.Icon0Bytes);
                pic0 = CreateDetachedBitmap(raw.Pic0Bytes);
                pic1 = CreateDetachedBitmap(raw.Pic1Bytes);

                return new PkgInspectionSnapshot
                {
                    PackagePath = packagePath,
                    FileName = Path.GetFileName(packagePath),
                    Title = raw.Title ?? string.Empty,
                    TitleId = raw.TitleId ?? string.Empty,
                    ContentId = raw.ContentId ?? string.Empty,
                    PackageCategory = raw.PackageCategory ?? string.Empty,
                    PackageState = raw.PackageState ?? string.Empty,
                    ApplicationVersion = raw.ApplicationVersion ?? string.Empty,
                    PackageVersion = packageVersion,
                    RequiredFirmware = requiredFirmware,
                    PackageSize = size > 0 ? ByteSize.FromBytes(size).ToString() : string.Empty,
                    SfoEntries = entries.ToArray(),
                    HeaderFields = (raw.HeaderFields ?? Array.Empty<PkgInspectionField>()).ToArray(),
                    BuildInfoFields = PkgBuildInfoParser.Parse(FindSfoValue(entries, "PUBTOOLINFO")),
                    Icon0 = icon,
                    Pic0 = pic0,
                    Pic1 = pic1
                };
            }
            catch
            {
                icon?.Dispose();
                pic0?.Dispose();
                pic1?.Dispose();
                throw;
            }
        }

        private static string FindSfoValue(IEnumerable<PkgSfoEntry> entries, string name)
        {
            return entries.FirstOrDefault(entry =>
                string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
        }

        private static string FormatSystemVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int encoded))
                return value;

            return encoded == 0
                ? value
                : $"{(encoded >> 8) & 0xFF}.{encoded & 0xFF:D2}";
        }

        internal static Bitmap CreateDetachedBitmap(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                return new Bitmap(source);
            }
            catch (ArgumentException)
            {
                // Damaged optional artwork must not prevent core metadata from loading.
                return null;
            }
            catch (OutOfMemoryException)
            {
                return null;
            }
        }
    }
}
