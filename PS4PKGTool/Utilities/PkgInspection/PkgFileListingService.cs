using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    internal sealed class PkgFileParseResult
    {
        public IReadOnlyList<PkgFileEntry> Entries { get; init; } = Array.Empty<PkgFileEntry>();
        public int MalformedLineCount { get; init; }
    }

    internal sealed class PkgFileListingResult
    {
        public bool Succeeded { get; init; }
        public IReadOnlyList<PkgFileEntry> Entries { get; init; } = Array.Empty<PkgFileEntry>();
        public IReadOnlyList<PkgFileNode> Roots { get; init; } = Array.Empty<PkgFileNode>();
        public string ErrorMessage { get; init; } = string.Empty;
        public bool RestoreFailed { get; init; }
        public string RecoveryDirectory { get; init; } = string.Empty;
        public int MalformedLineCount { get; init; }
    }

    internal sealed class PkgFileListingParser
    {
        public PkgFileParseResult Parse(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return new PkgFileParseResult();

            var entries = new List<PkgFileEntry>();
            int malformed = 0;
            foreach (string rawLine in output.Split(
                new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.Contains("[Error]", StringComparison.OrdinalIgnoreCase))
                    continue;

                char kind = line[0];
                if (kind is not ('F' or 'f' or 'D' or 'd'))
                    continue;

                int pathIndex = RootIndex(line);
                if (pathIndex < 0)
                {
                    malformed++;
                    continue;
                }

                string prefix = line[..pathIndex].Trim();
                string[] fields = prefix.Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !long.TryParse(fields[1], out long size) || size < 0)
                {
                    malformed++;
                    continue;
                }

                string fullPath = NormalizePath(line[pathIndex..]);
                if (string.IsNullOrWhiteSpace(fullPath))
                {
                    malformed++;
                    continue;
                }

                int separator = fullPath.LastIndexOf('/');
                string name = separator < 0 ? fullPath : fullPath[(separator + 1)..];
                if (name.Length == 0)
                {
                    malformed++;
                    continue;
                }

                entries.Add(new PkgFileEntry
                {
                    FullPath = fullPath,
                    Name = name,
                    ParentPath = separator < 0 ? string.Empty : fullPath[..separator],
                    IsDirectory = kind is 'D' or 'd',
                    Size = size
                });
            }

            return new PkgFileParseResult
            {
                Entries = entries,
                MalformedLineCount = malformed
            };
        }

        private static int RootIndex(string line)
        {
            int image = line.IndexOf("Image0", StringComparison.OrdinalIgnoreCase);
            int sc = line.IndexOf("Sc0", StringComparison.OrdinalIgnoreCase);
            if (image < 0) return sc;
            if (sc < 0) return image;
            return Math.Min(image, sc);
        }

        private static string NormalizePath(string path) =>
            path.Trim().Trim('"').Replace('\\', '/').Trim('/');
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

    internal sealed class OrbisProcessResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
        public bool TimedOut { get; init; }
    }

    internal interface IOrbisProcessRunner
    {
        Task<OrbisProcessResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken);
    }

    internal sealed class OrbisProcessRunner : IOrbisProcessRunner
    {
        public async Task<OrbisProcessResult> RunAsync(
            ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutCancellation.CancelAfter(timeout);

            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch { }

                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                if (!timedOut)
                    throw new OperationCanceledException(cancellationToken);
            }

            return new OrbisProcessResult
            {
                ExitCode = process.HasExited ? process.ExitCode : -1,
                StandardOutput = await stdout.ConfigureAwait(false),
                StandardError = await stderr.ConfigureAwait(false),
                TimedOut = timedOut
            };
        }
    }

    internal interface IPkgFileListingLoader
    {
        Task<PkgFileListingResult> ListAsync(
            string packagePath, string passcode, CancellationToken cancellationToken);
    }

    internal sealed class PkgFileListingService : IPkgFileListingLoader
    {
        public const string DefaultPasscode = "00000000000000000000000000000000";

        /// <summary>Sentinel passcode value meaning "run with --no_passcode"
        /// (official packages whose key is unknown). Callers pass this in
        /// when the user selects the no-passcode option in the prompt.</summary>
        public const string NoPasscode = "\x1";
        private readonly string _orbisPubCmdPath;
        private readonly IOrbisProcessRunner _processRunner;
        private readonly PkgFileListingParser _parser;
        private readonly PkgFileTreeBuilder _treeBuilder;
        private readonly TimeSpan _timeout;

        public PkgFileListingService()
            : this(
                Helper.OrbisPubCmd,
                new OrbisProcessRunner(),
                new PkgFileListingParser(),
                new PkgFileTreeBuilder(),
                TimeSpan.FromSeconds(30))
        {
        }

        internal PkgFileListingService(
            string orbisPubCmdPath,
            IOrbisProcessRunner processRunner,
            PkgFileListingParser parser,
            PkgFileTreeBuilder treeBuilder,
            TimeSpan timeout)
        {
            _orbisPubCmdPath = Path.GetFullPath(orbisPubCmdPath);
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _treeBuilder = treeBuilder ?? throw new ArgumentNullException(nameof(treeBuilder));
            _timeout = timeout;
        }

        public async Task<PkgFileListingResult> ListAsync(
            string packagePath, string passcode, CancellationToken cancellationToken)
        {
            if (!File.Exists(_orbisPubCmdPath))
                return Failure("orbis-pub-cmd.exe was not found.");

            OrbisSafePkgOperation operation = null;
            PkgFileListingResult result;
            OperationCanceledException cancellation = null;
            OrbisSafePkgRestoreResult? restoreFailure = null;
            try
            {
                try
                {
                    operation = OrbisSafePkgOperation.Prepare(packagePath);
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = _orbisPubCmdPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.Add("img_file_list");
                    AddPasscodeArgument(startInfo, passcode);
                    startInfo.ArgumentList.Add("--oformat");
                    startInfo.ArgumentList.Add("long+original_size");
                    startInfo.ArgumentList.Add(operation.OrbisPath);

                    OrbisProcessResult process = await _processRunner.RunAsync(
                        startInfo, _timeout, cancellationToken).ConfigureAwait(false);
                    if (process.TimedOut)
                    {
                        result = Failure("orbis-pub-cmd timed out while listing package files.");
                    }
                    else if (process.ExitCode != 0 ||
                        process.StandardOutput.Contains("[Error]", StringComparison.OrdinalIgnoreCase) ||
                        process.StandardError.Contains("[Error]", StringComparison.OrdinalIgnoreCase))
                    {
                        result = Failure(DescribeToolFailure(process));
                    }
                    else
                    {
                        PkgFileParseResult parsed = _parser.Parse(process.StandardOutput);
                        if (parsed.Entries.Count == 0)
                        {
                            result = Failure(parsed.MalformedLineCount > 0
                                ? "orbis-pub-cmd returned an invalid file listing."
                                : "orbis-pub-cmd returned no package files.");
                        }
                        else
                        {
                            result = new PkgFileListingResult
                            {
                                Succeeded = true,
                                Entries = parsed.Entries,
                                Roots = _treeBuilder.Build(parsed.Entries),
                                MalformedLineCount = parsed.MalformedLineCount
                            };
                        }
                    }
                }
                catch (OperationCanceledException ex)
                {
                    cancellation = ex;
                    result = null;
                }
                catch (Exception ex)
                {
                    result = Failure("Package files could not be listed. " + ex.Message);
                }
            }
            finally
            {
                // Staging restoration must run on every exit path - success,
                // tool failure, parse failure, timeout and cancellation.
                if (operation != null)
                {
                    OrbisSafePkgRestoreResult restore = operation.Restore();
                    if (!restore.Succeeded)
                        restoreFailure = restore;
                }
            }

            if (restoreFailure != null)
            {
                return new PkgFileListingResult
                {
                    Succeeded = false,
                    RestoreFailed = true,
                    RecoveryDirectory = restoreFailure.RecoveryDirectory,
                    ErrorMessage = restoreFailure.ErrorMessage +
                        " Recovery data remains in: " + restoreFailure.RecoveryDirectory
                };
            }

            if (cancellation != null)
                throw cancellation;
            return result;
        }

        /// <summary>
        /// Adds the passcode argument(s) shared by every orbis-pub-cmd
        /// invocation: "--no_passcode" for the sentinel, otherwise
        /// "--passcode &lt;code&gt;" with the default when none given.
        /// </summary>
        internal static void AddPasscodeArgument(ProcessStartInfo startInfo, string? passcode)
        {
            if (passcode == NoPasscode)
                startInfo.ArgumentList.Add("--no_passcode");
            else
            {
                startInfo.ArgumentList.Add("--passcode");
                startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(passcode)
                    ? DefaultPasscode
                    : passcode);
            }
        }

        private static string DescribeToolFailure(OrbisProcessResult process)
        {
            string detail = string.Join(" ", new[]
            {
                process.StandardError,
                process.StandardOutput
            }.Where(value => !string.IsNullOrWhiteSpace(value))
             .Select(value => value.Trim().Replace('\r', ' ').Replace('\n', ' ')));
            if (detail.Length > 500)
                detail = detail[..500];
            return detail.Length == 0
                ? $"orbis-pub-cmd exited with code {process.ExitCode}."
                : $"orbis-pub-cmd exited with code {process.ExitCode}: {detail}";
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
