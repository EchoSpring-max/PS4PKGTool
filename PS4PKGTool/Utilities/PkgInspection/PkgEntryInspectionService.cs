using ByteSizeLib;
using PS4_Tools.LibOrbis.PKG;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS4PKGTool.Utilities.PkgInspection
{
    internal sealed class PkgEntryInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Offset { get; init; } = string.Empty;
        public string Size { get; init; } = string.Empty;
        public string Flags1 { get; init; } = string.Empty;
        public string Flags2 { get; init; } = string.Empty;
        public string Encrypted { get; init; } = string.Empty;
    }

    internal sealed class PkgEntryLoadResult
    {
        private PkgEntryLoadResult(IReadOnlyList<PkgEntryInfo> entries, string errorMessage)
        {
            Entries = entries ?? Array.Empty<PkgEntryInfo>();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public IReadOnlyList<PkgEntryInfo> Entries { get; }
        public string ErrorMessage { get; }
        public bool Succeeded => ErrorMessage.Length == 0;

        public static PkgEntryLoadResult Success(IReadOnlyList<PkgEntryInfo> entries) =>
            new PkgEntryLoadResult(entries, null);

        public static PkgEntryLoadResult Failure(string errorMessage) =>
            new PkgEntryLoadResult(Array.Empty<PkgEntryInfo>(), errorMessage);
    }

    internal interface IPkgEntryReader
    {
        IReadOnlyList<PkgEntryInfo> Read(string packagePath, CancellationToken cancellationToken);
    }

    internal sealed class LibOrbisPkgEntryReader : IPkgEntryReader
    {
        public IReadOnlyList<PkgEntryInfo> Read(string packagePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(
                packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(stream.Dispose);

            try
            {
                var package = new PkgReader(stream).ReadPkg();
                cancellationToken.ThrowIfCancellationRequested();

                var entries = new List<PkgEntryInfo>();
                foreach (var meta in package.Metas.Metas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Add(new PkgEntryInfo
                    {
                        Name = meta.id.ToString(),
                        Offset = $"0x{meta.DataOffset:X}",
                        Size = ByteSize.FromBytes(Convert.ToDouble(meta.DataSize)).ToString(),
                        Flags1 = $"0x{meta.Flags1:X}",
                        Flags2 = $"0x{meta.Flags2:X}",
                        Encrypted = $"{meta.Encrypted:X}"
                    });
                }

                return entries;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    internal interface IPkgEntryLoader
    {
        Task<PkgEntryLoadResult> LoadAsync(string packagePath, CancellationToken cancellationToken);
    }

    internal sealed class PkgEntryInspectionService : IPkgEntryLoader
    {
        private readonly IPkgEntryReader _reader;

        public PkgEntryInspectionService()
            : this(new LibOrbisPkgEntryReader())
        {
        }

        internal PkgEntryInspectionService(IPkgEntryReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<PkgEntryLoadResult> LoadAsync(
            string packagePath, CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<PkgEntryInfo> entries = await Task.Run(
                    () => _reader.Read(packagePath, cancellationToken), cancellationToken);
                return PkgEntryLoadResult.Success(entries.ToArray());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return PkgEntryLoadResult.Failure(ex.Message);
            }
        }
    }

    internal sealed class PkgEntryInspectionSession : IDisposable
    {
        private readonly string _packagePath;
        private readonly IPkgEntryLoader _loader;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly object _sync = new object();
        private Task<PkgEntryLoadResult> _loadTask;
        private bool _disposed;

        public PkgEntryInspectionSession(string packagePath, IPkgEntryLoader loader)
        {
            _packagePath = packagePath ?? throw new ArgumentNullException(nameof(packagePath));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public Task<PkgEntryLoadResult> LoadAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_loadTask == null)
                    _loadTask = LoadCoreAsync(cancellationToken);
                return _loadTask;
            }
        }

        private async Task<PkgEntryLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token, cancellationToken);
            return await _loader.LoadAsync(_packagePath, linkedCancellation.Token);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _lifetimeCancellation.Cancel();
            }

            _lifetimeCancellation.Dispose();
        }
    }
}
