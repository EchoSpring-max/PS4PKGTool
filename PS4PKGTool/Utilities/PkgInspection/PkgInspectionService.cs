using ByteSizeLib;
using PS4_Tools.LibOrbis.Util;
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
            var package = PS4_Tools.PKG.SceneRelated.Read_PKG(packagePath);
            var entries = package.Param?.Tables == null
                ? Array.Empty<PkgSfoEntry>()
                : package.Param.Tables
                    .Select(entry => new PkgSfoEntry(entry.Name, entry.Value))
                    .ToArray();
            var headerFields = ReadHeaderFields(package);

            return new RawPkgInspectionData
            {
                Title = package.PS4_Title,
                TitleId = package.Param?.TITLEID,
                ContentId = package.Param?.ContentID ?? package.Content_ID,
                PackageCategory = package.PKG_Type.ToString(),
                PackageState = package.PKGState.ToString(),
                ApplicationVersion = package.Param?.APP_VER,
                SfoEntries = entries,
                HeaderFields = headerFields,
                Icon0Bytes = CloneBytes(package.Icon),
                Pic0Bytes = CloneBytes(package.Image),
                Pic1Bytes = CloneBytes(package.Image2)
            };
        }

        private static byte[] CloneBytes(byte[] bytes) => bytes == null ? null : (byte[])bytes.Clone();

        private static IReadOnlyList<PkgInspectionField> ReadHeaderFields(
            PS4_Tools.PKG.SceneRelated.Unprotected_PKG package)
        {
            try
            {
                string[] names = package.Header.DisplayType()?.ToArray() ?? Array.Empty<string>();
                string[] values = package.Header.DisplayValue()?.ToArray() ?? Array.Empty<string>();
                return names.Zip(values, (name, value) => new PkgInspectionField(name, value)).ToArray();
            }
            catch
            {
                // Unknown or damaged header values are optional inspection data.
                return Array.Empty<PkgInspectionField>();
            }
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
