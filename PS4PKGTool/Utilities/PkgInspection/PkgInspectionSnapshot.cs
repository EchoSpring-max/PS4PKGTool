using System;
using System.Collections.Generic;
using System.Drawing;

namespace PS4PKGTool.Utilities.PkgInspection
{
    internal sealed class PkgSfoEntry
    {
        public PkgSfoEntry(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Name { get; }
        public string Value { get; }
    }

    internal sealed class PkgInspectionField
    {
        public PkgInspectionField(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Name { get; }
        public string Value { get; }
    }

    internal sealed class PkgInspectionSnapshot : IDisposable
    {
        private bool _disposed;

        public string PackagePath { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string TitleId { get; init; } = string.Empty;
        public string ContentId { get; init; } = string.Empty;
        public string PackageCategory { get; init; } = string.Empty;
        public string PackageState { get; init; } = string.Empty;
        public string ApplicationVersion { get; init; } = string.Empty;
        public string PackageVersion { get; init; } = string.Empty;
        public string RequiredFirmware { get; init; } = string.Empty;
        public string PackageSize { get; init; } = string.Empty;
        public IReadOnlyList<PkgSfoEntry> SfoEntries { get; init; } = Array.Empty<PkgSfoEntry>();
        public IReadOnlyList<PkgInspectionField> HeaderFields { get; init; } = Array.Empty<PkgInspectionField>();
        public IReadOnlyList<PkgInspectionField> BuildInfoFields { get; init; } = Array.Empty<PkgInspectionField>();
        public Bitmap Icon0 { get; init; }
        public Bitmap Pic0 { get; init; }
        public Bitmap Pic1 { get; init; }

        internal bool IsDisposed => _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Icon0?.Dispose();
            Pic0?.Dispose();
            Pic1?.Dispose();
        }
    }
}
