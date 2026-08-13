using Newtonsoft.Json;
using PS4PKGTool.Util.Constants;
using PS4PKGTool.Utilities.Settings;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    public class ManifestEntry
    {
        public string FilePath { get; set; }
        public string Filename { get; set; }
        public string Title { get; set; }
        public string TitleId { get; set; }
        public string ContentId { get; set; }
        public string Region { get; set; }
        public string SystemVersion { get; set; }
        public string Version { get; set; }
        public string PkgType { get; set; }
        public string Category { get; set; }
        public string Size { get; set; }
        public string Psvr { get; set; }
        public string Ps4ProEnhanced { get; set; }
        public string Ps5Bc { get; set; }
        public string Directory { get; set; }
        public string Backported { get; set; }
        public string LatestUpdate { get; set; }
        public DateTime FileLastWriteTimeUtc { get; set; }
    }

    public class ManifestData
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTime CreatedAt { get; set; }
        public List<string> PkgDirectories { get; set; }
        public bool ScanRecursive { get; set; }
        public List<ManifestEntry> Entries { get; set; }
    }

    public static class ManifestHelper
    {
        public static string ManifestFilePath =>
            Helper.AppDataDirectory + "manifest.json";

        public static bool ManifestExists() => File.Exists(ManifestFilePath);

        public static ManifestData LoadManifest()
        {
            try
            {
                string json = File.ReadAllText(ManifestFilePath);
                var manifest = JsonConvert.DeserializeObject<ManifestData>(json);
                return manifest;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load manifest: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates the manifest against current app settings.
        /// Returns (isValid, reason).
        /// </summary>
        public static (bool IsValid, string Reason) ValidateManifest(ManifestData manifest, AppSettings currentSettings)
        {
            if (manifest == null)
                return (false, "Manifest is null.");

            if (manifest.SchemaVersion != 1)
                return (false, $"Unsupported manifest schema version: {manifest.SchemaVersion}.");

            if (manifest.PkgDirectories == null || manifest.PkgDirectories.Count == 0)
                return (false, "Manifest contains no PKG directories.");

            if (currentSettings.PkgDirectories == null || currentSettings.PkgDirectories.Count == 0)
                return (false, "No PKG directories configured in settings.");

            // Compare directory sets (order-independent)
            var manifestDirs = new HashSet<string>(
                manifest.PkgDirectories.Where(d => !string.IsNullOrEmpty(d)),
                StringComparer.OrdinalIgnoreCase);
            var settingsDirs = new HashSet<string>(
                currentSettings.PkgDirectories.Where(d => !string.IsNullOrEmpty(d)),
                StringComparer.OrdinalIgnoreCase);

            if (!manifestDirs.SetEquals(settingsDirs))
                return (false, "PKG directories have changed since the manifest was created.");

            if (manifest.ScanRecursive != currentSettings.ScanRecursive)
                return (false, "Scan recursive setting has changed.");

            return (true, string.Empty);
        }

        /// <summary>
        /// Removes entries whose files no longer exist or have been modified since the manifest was created.
        /// Returns the valid entries and the count of removed entries.
        /// </summary>
        public static (List<ManifestEntry> Valid, int Removed) FilterValidEntries(List<ManifestEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return (new List<ManifestEntry>(), 0);

            var valid = new List<ManifestEntry>();
            int removed = 0;

            foreach (var entry in entries)
            {
                string fullPath = string.IsNullOrEmpty(entry.FilePath)
                    ? Path.Combine(entry.Directory ?? "", entry.Filename ?? "")
                    : entry.FilePath;

                if (!File.Exists(fullPath))
                {
                    removed++;
                    continue;
                }

                // If the file has been modified since the manifest was created, skip it
                if (File.GetLastWriteTimeUtc(fullPath) > entry.FileLastWriteTimeUtc)
                {
                    removed++;
                    continue;
                }

                valid.Add(entry);
            }

            if (removed > 0)
                Logger.LogWarning($"{removed} PKG entries removed from manifest (file missing or modified).");

            return (valid, removed);
        }

        /// <summary>
        /// Reconstructs the 16-column DataTable from manifest entries.
        /// </summary>
        public static DataTable BuildDataTableFromManifest(List<ManifestEntry> entries)
        {
            var dt = PkgColumns.CreateSchema();

            if (entries == null || entries.Count == 0)
                return dt;

            var imageConverter = new ImageConverter();

            foreach (var entry in entries)
            {
                byte[] regionIcon = GetRegionIconBytes(entry.Region, imageConverter);

                dt.Rows.Add(
                    entry.Filename ?? "",
                    entry.Title ?? "",
                    entry.TitleId ?? "",
                    entry.ContentId ?? "",
                    regionIcon,
                    FormatSystemVersion(entry.SystemVersion),
                    entry.Version ?? "",
                    entry.PkgType ?? "",
                    entry.Category ?? "",
                    entry.Size ?? "",
                    entry.Psvr ?? "",
                    entry.Ps4ProEnhanced ?? "",
                    entry.Ps5Bc ?? "",
                    entry.Directory ?? "",
                    entry.Backported ?? "",
                    entry.LatestUpdate ?? ""
                );
            }

            return dt;
        }

        /// <summary>
        /// Reconstructs the list of PKG paths from manifest entries.
        /// </summary>
        public static List<string> BuildPkgPathList(List<ManifestEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<string>();

            return entries
                .Select(e => string.IsNullOrEmpty(e.FilePath)
                    ? Path.Combine(e.Directory ?? "", e.Filename ?? "")
                    : e.FilePath)
                .ToList();
        }

        /// <summary>
        /// Saves the current PKGGridView data to a manifest file.
        /// </summary>
        public static void SaveManifest(DataTable gridViewData, List<string> verifiedPkgList)
        {
            if (gridViewData == null || gridViewData.Rows.Count == 0)
                return;

            var entries = new List<ManifestEntry>();
            var imageConverter = new ImageConverter();

            foreach (DataRow row in gridViewData.Rows)
            {
                string filename = row["Filename"]?.ToString() ?? "";
                string directory = row["Directory"]?.ToString() ?? "";
                string fullPath = Path.Combine(directory, filename);

                // Get region string from byte[] column
                string region = GetRegionStringFromIcon(row["Region"] as byte[]);

                // Get file last write time
                DateTime lastWriteTime = File.Exists(fullPath)
                    ? File.GetLastWriteTimeUtc(fullPath)
                    : DateTime.UtcNow;

                entries.Add(new ManifestEntry
                {
                    FilePath = fullPath,
                    Filename = filename,
                    Title = row["Title"]?.ToString() ?? "",
                    TitleId = row["Title ID"]?.ToString() ?? "",
                    ContentId = row["Content ID"]?.ToString() ?? "",
                    Region = region,
                    SystemVersion = row["System Version"]?.ToString() ?? "",
                    Version = row["Version [App Version]"]?.ToString() ?? "",
                    PkgType = row["PKG Type"]?.ToString() ?? "",
                    Category = row["Category"]?.ToString() ?? "",
                    Size = row["Size"]?.ToString() ?? "",
                    Psvr = row["PSVR"]?.ToString() ?? "",
                    Ps4ProEnhanced = row["PS4 Pro Enhanced"]?.ToString() ?? "",
                    Ps5Bc = row["PS5 BC"]?.ToString() ?? "",
                    Directory = directory,
                    Backported = row["Backported"]?.ToString() ?? "",
                    LatestUpdate = SafeCell(row, "Latest Update"),
                    FileLastWriteTimeUtc = lastWriteTime
                });
            }

            var manifest = new ManifestData
            {
                SchemaVersion = 1,
                CreatedAt = DateTime.Now,
                PkgDirectories = SettingsManager.appSettings_?.PkgDirectories
                    ?.Where(d => !string.IsNullOrEmpty(d)).ToList() ?? new List<string>(),
                ScanRecursive = SettingsManager.appSettings_?.ScanRecursive ?? false,
                Entries = entries
            };

            string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(ManifestFilePath, json);
            Logger.LogInformation($"Manifest saved: {entries.Count} entries to {ManifestFilePath}");
        }

        /// <summary>
        /// Deletes the manifest file if it exists.
        /// </summary>
        public static void DeleteManifest()
        {
            if (File.Exists(ManifestFilePath))
            {
                File.Delete(ManifestFilePath);
                Logger.LogInformation("Manifest deleted.");
            }
        }

        /// <summary>
        /// Safely reads a cell value from a DataRow, returning "" if the column doesn't exist.
        /// </summary>
        private static string SafeCell(DataRow row, string columnName)
        {
            return row.Table.Columns.Contains(columnName)
                ? (row[columnName]?.ToString() ?? "")
                : "";
        }

        /// <summary>
        /// Formats a PS4 system version value to "X.XX" display format.
        /// Handles raw integers, hex-derived strings, and already-formatted values.
        /// </summary>
        private static string FormatSystemVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw == "NA" || raw == "0")
                return raw ?? "";

            // Already formatted (e.g. "4.50", "11.00") - short string with one dot
            if (raw.Length <= 5 && raw.Contains("."))
                return raw;

            // Broken format from old code (e.g. "45000.00") - remove dot, take first 3 hex chars
            string hex;
            if (raw.Contains("."))
            {
                hex = raw.Replace(".", "");
            }
            else if (long.TryParse(raw, out long num))
            {
                hex = string.Format("{0:X}", num);
            }
            else
            {
                return raw; // Unknown format, return as-is
            }

            if (hex.Length >= 3)
            {
                string first_three = hex.Substring(0, 3);
                return first_three.Insert(1, ".");
            }

            return raw;
        }

        /// <summary>
        /// Converts a region string to the corresponding flag icon byte array.
        /// Matches the logic in Main.cs LoadPKGGridView region icon loading.
        /// </summary>
        private static byte[] GetRegionIconBytes(string region, ImageConverter converter)
        {
            if (string.IsNullOrEmpty(region))
                return null;

            switch (region)
            {
                case PKGRegion.EU: return (byte[])converter.ConvertTo(Properties.Resources.eu, typeof(byte[]));
                case PKGRegion.US: return (byte[])converter.ConvertTo(Properties.Resources.us, typeof(byte[]));
                case PKGRegion.UK: return (byte[])converter.ConvertTo(Properties.Resources.us, typeof(byte[]));
                case PKGRegion.JAPAN: return (byte[])converter.ConvertTo(Properties.Resources.jp, typeof(byte[]));
                case PKGRegion.HONG_KONG: return (byte[])converter.ConvertTo(Properties.Resources.hk, typeof(byte[]));
                case PKGRegion.ASIA: return (byte[])converter.ConvertTo(Properties.Resources.asia, typeof(byte[]));
                case PKGRegion.KOREA: return (byte[])converter.ConvertTo(Properties.Resources.kr, typeof(byte[]));
                default: return null;
            }
        }

        /// <summary>
        /// Guesses the region string from the region icon byte array.
        /// Compares against known resource images.
        /// </summary>
        private static string GetRegionStringFromIcon(byte[] iconBytes)
        {
            if (iconBytes == null || iconBytes.Length == 0)
                return "";

            var imageConverter = new ImageConverter();

            // Compare against known region icons
            var knownRegions = new (string RegionName, byte[] Icon)[]
            {
                (PKGRegion.EU, (byte[])imageConverter.ConvertTo(Properties.Resources.eu, typeof(byte[]))),
                (PKGRegion.US, (byte[])imageConverter.ConvertTo(Properties.Resources.us, typeof(byte[]))),
                (PKGRegion.JAPAN, (byte[])imageConverter.ConvertTo(Properties.Resources.jp, typeof(byte[]))),
                (PKGRegion.HONG_KONG, (byte[])imageConverter.ConvertTo(Properties.Resources.hk, typeof(byte[]))),
                (PKGRegion.ASIA, (byte[])imageConverter.ConvertTo(Properties.Resources.asia, typeof(byte[]))),
                (PKGRegion.KOREA, (byte[])imageConverter.ConvertTo(Properties.Resources.kr, typeof(byte[]))),
            };

            foreach (var (regionName, knownIcon) in knownRegions)
            {
                if (ByteArraysEqual(iconBytes, knownIcon))
                    return regionName;
            }

            return "";
        }

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
