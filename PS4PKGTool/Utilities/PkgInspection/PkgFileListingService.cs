using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS4PKGTool.Utilities.PkgInspection
{
    internal sealed class PkgFileEntry
    {
        public string FullPath { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ParentPath { get; init; } = string.Empty;
        public bool IsDirectory { get; init; }
        public long Size { get; init; }
        public string Type => IsDirectory
            ? "Directory"
            : string.IsNullOrEmpty(Path.GetExtension(Name))
                ? "File"
                : Path.GetExtension(Name).TrimStart('.');
    }

    internal sealed class PkgFileNode
    {
        private readonly List<PkgFileNode> _children = new List<PkgFileNode>();

        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public bool IsDirectory { get; internal set; }
        public long Size { get; internal set; }
        public IReadOnlyList<PkgFileNode> Children => _children;

        internal void Add(PkgFileNode child) => _children.Add(child);
        internal void SortChildren()
        {
            _children.Sort((left, right) =>
            {
                int directoryOrder = right.IsDirectory.CompareTo(left.IsDirectory);
                return directoryOrder != 0
                    ? directoryOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });
            foreach (PkgFileNode child in _children)
                child.SortChildren();
        }
    }

    internal sealed class PkgFileListingResult
    {
        public bool Succeeded { get; init; }
        public IReadOnlyList<PkgFileEntry> Entries { get; init; } = Array.Empty<PkgFileEntry>();
        public IReadOnlyList<PkgFileNode> Roots { get; init; } = Array.Empty<PkgFileNode>();
        public string ErrorMessage { get; init; } = string.Empty;

        /// <summary>Vestigial: kept for callers written against the old
        /// orbis-pub-cmd pipeline. Always false because the in-process reader
        /// never stages or moves the package.</summary>
        public bool RestoreFailed { get; init; }

        /// <summary>Vestigial: kept for callers written against the old
        /// pipeline. Always empty.</summary>
        public string RecoveryDirectory { get; init; } = string.Empty;

        /// <summary>Vestigial: kept for callers written against the old
        /// text-output parser. Always zero.</summary>
        public int MalformedLineCount { get; init; }
    }

    internal sealed class PkgFileTreeBuilder
    {
        public IReadOnlyList<PkgFileNode> Build(IReadOnlyList<PkgFileEntry> entries)
        {
            var roots = new List<PkgFileNode>();
            var index = new Dictionary<string, PkgFileNode>(StringComparer.OrdinalIgnoreCase);

            foreach (PkgFileEntry entry in entries)
            {
                string[] segments = entry.FullPath.Split(
                    '/', StringSplitOptions.RemoveEmptyEntries);
                PkgFileNode parent = null;
                string path = string.Empty;
                for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                {
                    path = path.Length == 0 ? segments[segmentIndex] : path + "/" + segments[segmentIndex];
                    bool isLeaf = segmentIndex == segments.Length - 1;
                    if (!index.TryGetValue(path, out PkgFileNode node))
                    {
                        node = new PkgFileNode
                        {
                            Name = segments[segmentIndex],
                            FullPath = path,
                            IsDirectory = !isLeaf || entry.IsDirectory,
                            Size = isLeaf ? entry.Size : 0
                        };
                        index.Add(path, node);
                        if (parent == null)
                            roots.Add(node);
                        else
                            parent.Add(node);
                    }
                    else if (isLeaf)
                    {
                        node.IsDirectory |= entry.IsDirectory;
                        node.Size = entry.Size;
                    }
                    parent = node;
                }
            }

            roots.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            foreach (PkgFileNode root in roots)
                root.SortChildren();
            return roots;
        }
    }

    internal interface IPkgFileListingLoader
    {
        Task<PkgFileListingResult> ListAsync(
            string packagePath, string passcode, CancellationToken cancellationToken);
    }

    /// <summary>
    /// In-process replacement for the orbis-pub-cmd img_file_list spawn:
    /// OrbisPkgTool.PkgReader reads the PKG entry table (Sc0) and the inner
    /// PFS (Image0) directly. No external process, no output parsing, no
    /// ASCII-safe temp staging. The package file is opened read-only at
    /// its original path, so Unicode paths just work.
    /// </summary>
    internal sealed class PkgFileListingService : IPkgFileListingLoader
    {
        public const string DefaultPasscode = "00000000000000000000000000000000";

        /// <summary>Sentinel passcode value meaning "attempt reading
        /// without a passcode" (official packages whose key is unknown).
        /// Callers pass this in when the user selects the no-passcode
        /// option in the prompt. PkgReader maps this to the default
        /// passcode and falls back to RSA dk3 recovery when the digest
        /// check fails, matching orbis-pub-cmd's --no_passcode behavior
        /// produced.</summary>
        public const string NoPasscode = "\x1";

        public async Task<PkgFileListingResult> ListAsync(
            string packagePath, string passcode, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string effectivePasscode =
                    passcode == NoPasscode || string.IsNullOrWhiteSpace(passcode)
                        ? OrbisPkgTool.PkgReader.DefaultPasscode
                        : passcode;

                // Offload the (potentially long) Image0 tree walk to the
                // thread pool; callers on UI/background threads block on
                // this task, so keep the read off their continuation.
                List<OrbisPkgTool.PkgFileEntry> files = await Task.Run(
                    () =>
                    {
                        using var reader = new OrbisPkgTool.PkgReader(
                            packagePath, effectivePasscode);
                        return reader.ListFiles();
                    }, cancellationToken).ConfigureAwait(false);

                var entries = new List<PkgFileEntry>(files.Count);
                foreach (OrbisPkgTool.PkgFileEntry file in files)
                {
                    string fullPath = file.Path.Replace('\\', '/');
                    int separator = fullPath.LastIndexOf('/');
                    entries.Add(new PkgFileEntry
                    {
                        FullPath = fullPath,
                        Name = file.Name,
                        ParentPath = separator < 0 ? string.Empty : fullPath[..separator],
                        IsDirectory = file.IsDirectory,
                        Size = file.Size
                    });
                }

                return new PkgFileListingResult
                {
                    Succeeded = true,
                    Entries = entries,
                    Roots = new PkgFileTreeBuilder().Build(entries)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Passcode failures surface here as
                // InvalidDataException("Passcode mismatch."); callers
                // (ShellCommands, Mini Viewer) match "passcode" in the
                // message to offer the retry prompt.
                return Failure("Package files could not be listed. " + ex.Message);
            }
        }

        private static PkgFileListingResult Failure(string message) => new()
        {
            Succeeded = false,
            ErrorMessage = message
        };
    }

    internal sealed class PkgFileListingSession : IDisposable
    {
        private readonly string _packagePath;
        private readonly string _passcode;
        private readonly IPkgFileListingLoader _loader;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly object _sync = new object();
        private Task<PkgFileListingResult> _loadTask;
        private bool _disposed;

        public PkgFileListingSession(
            string packagePath, string passcode, IPkgFileListingLoader loader)
        {
            _packagePath = packagePath ?? throw new ArgumentNullException(nameof(packagePath));
            _passcode = passcode ?? string.Empty;
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public bool HasStarted
        {
            get { lock (_sync) return _loadTask != null; }
        }

        public Task<PkgFileListingResult> LoadAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_loadTask == null)
                    _loadTask = LoadCoreAsync(cancellationToken);
                return _loadTask;
            }
        }

        private async Task<PkgFileListingResult> LoadCoreAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token, cancellationToken);
            return await _loader.ListAsync(_packagePath, _passcode, linked.Token);
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
