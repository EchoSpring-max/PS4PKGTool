using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>
    /// ZIP extraction hardened against archive-based path traversal:
    ///  - entries with ".." segments, rooted/absolute paths, drive letters
    ///    and UNC paths are rejected;
    ///  - containment is checked on canonical (GetFullPath) paths with a
    ///    directory-boundary-aware comparison - "C:\builds\stage2" never
    ///    passes for "C:\builds\stage" + separator;
    ///  - non-regular entries (Unix symlink/socket/fifo/char device mode
    ///    bits) are rejected outright - v1 rejects what cannot be
    ///    confidently classified as a regular file.
    /// Corrupt/truncated archives surface as InvalidDataException from
    /// ZipArchive and fail cleanly.
    /// </summary>
    public static class SafeZipExtractor
    {
        /// <summary>Unix file-type mode bits in a ZIP entry's external attributes.</summary>
        private const uint FileTypeMask = 0xF000;
        private const uint SymlinkType = 0xA000;
        private const uint SocketType = 0xC000;
        private const uint CharDeviceType = 0x2000;
        private const uint BlockDeviceType = 0x6000;
        private const uint FifoType = 0x1000;

        /// <summary>Extracts every entry into stagingRoot. Throws on malicious/corrupt archives.</summary>
        public static void Extract(Stream zipStream, string stagingRoot)
        {
            string root = Path.GetFullPath(stagingRoot);
            Directory.CreateDirectory(root);

            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            // Windows is case-insensitive: "File.dll" and "file.dll" would
            // silently overwrite each other. Case-insensitive dedupe catches
            // any output-path collision.
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                string entryName = entry.FullName.Replace('\\', '/');
                if (IsSuspiciousEntry(entryName, entry))
                    throw new InvalidDataException(
                        $"Archive entry rejected as unsafe: {entry.FullName}");

                string target = Path.GetFullPath(Path.Combine(root, entryName));
                if (!IsWithin(target, root))
                    throw new InvalidDataException(
                        $"Archive entry escapes the extraction folder: {entry.FullName}");
                if (!seenTargets.Add(target))
                    throw new InvalidDataException(
                        $"Archive contains duplicate output path: {entry.FullName}");

                if (entryName.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = File.Create(target);
                input.CopyTo(output);
            }
        }

        /// <summary>
        /// True when the entry must be rejected: traversal, rooted paths,
        /// or a Unix file type that is not a regular file or directory.
        /// </summary>
        public static bool IsSuspiciousEntry(string entryName, ZipArchiveEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return false; // root entry - harmless

            // Path traversal / absolute / drive / UNC.
            if (entryName.Contains("..")) return true;
            if (entryName.Length >= 2 && entryName[1] == ':') return true;      // drive letter
            if (entryName.StartsWith("//") || entryName.StartsWith(@"\\")) return true; // UNC
            if (entryName.StartsWith("/")) return true;

            // Unix file-type bits (only meaningful on Unix-derived zips; any
            // set non-regular type is rejected - v1 does not guess).
            uint mode = (uint)(entry.ExternalAttributes >> 16);
            uint type = mode & FileTypeMask;
            if (type != 0)
            {
                if (type == SymlinkType || type == SocketType
                    || type == CharDeviceType || type == BlockDeviceType || type == FifoType)
                    return true;
            }
            return false;
        }

        /// <summary>Directory-boundary-aware containment (never a naive StartsWith).</summary>
        public static bool IsWithin(string child, string root)
        {
            string c = Path.GetFullPath(child);
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(c, r, StringComparison.OrdinalIgnoreCase)
                || c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Total uncompressed size of every file entry (free-space estimate).</summary>
        public static long TotalUncompressedSize(Stream zipStream)
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                total += entry.Length;
            }
            return total;
        }
    }
}
