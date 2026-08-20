using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>
    /// Diagnostics enrichment for a terminated shadPS4 process: exit-code
    /// classification, an optional tail of the emulator's own log, and
    /// discovery of Windows Error Reporting evidence. Everything is READ-ONLY:
    /// no registry writes, no WER configuration, no modification of any
    /// shadPS4-owned file. Absent evidence is reported as absent - nothing is
    /// invented.
    ///
    /// WER discovery sources, in order:
    ///  1. LocalDumps - only when the machine has already configured it
    ///     (HKLM/HKCU SOFTWARE\...\Windows Error Reporting\LocalDumps; we
    ///     never create or change these keys). Default dump folder is
    ///     %LOCALAPPDATA%\CrashDumps only in that case.
    ///  2. WER report folders - %LOCALAPPDATA%\Microsoft\Windows\WER\
    ///     ReportQueue and ReportArchive, matching AppCrash_shadPS4.exe_*.
    ///     A report folder is fresh when written within a short window of
    ///     the process exit; werfault can lag the exit by seconds, so the
    ///     caller polls briefly.
    ///  3. Nothing - only exit status and log context are reported.
    ///
    /// The shadPS4 log path is deliberately treated as unverified: the log
    /// location differs between builds, so LogPathResolver probes candidates
    /// read-only and returns null when none exists.
    /// </summary>
    public sealed class Shadps4TerminationAnalyzer
    {
        private const int MaxLogBytes = 64 * 1024;
        private const int MaxLogLines = 200;
        private const int PollAttempts = 3;
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

        // Freshness window: a WER artifact written shortly BEFORE the exit
        // (pre-existing evidence) or shortly AFTER it (werfault lag) counts.
        private static readonly TimeSpan FreshBeforeExit = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan FreshAfterExit = TimeSpan.FromSeconds(30);

        private readonly IClock _clock;
        private readonly IDelay _delay;

        public Shadps4TerminationAnalyzer(IClock clock, IDelay delay)
        {
            _clock = clock;
            _delay = delay;
        }

        /// <summary>Test seam: configured LocalDumps dump folder, or null when LocalDumps is not configured.</summary>
        public Func<string?> LocalDumpsFolderResolver { get; set; } = ReadConfiguredLocalDumpsFolder;

        /// <summary>Test seam: WER ReportQueue/ReportArchive roots (both may not exist).</summary>
        public Func<IReadOnlyList<string>> WerRootsResolver { get; set; } = DefaultWerRoots;

        /// <summary>
        /// Test seam: resolves the shadPS4 log path from the user directory.
        /// The log location is build-dependent and unverified, so the default
        /// probes candidates read-only and returns null when none exists.
        /// </summary>
        public Func<string?, string?> LogPathResolver { get; set; } = ResolveLogPathBestEffort;

        /// <summary>
        /// Builds the termination report. exitTimeUtc is when the watcher saw
        /// the process exit; freshness windows are computed around it.
        /// </summary>
        public async Task<Shadps4TerminationReport> AnalyzeAsync(
            string? target, string executable, string[] arguments, string? coreVersion,
            string? userDirectory, DateTime startTimeUtc, DateTime exitTimeUtc,
            int exitCode, CancellationToken cancellationToken = default)
        {
            uint code = unchecked((uint)exitCode);
            var category = Shadps4ExitStatus.Classify(code);

            // Resolve once - the path is the session's own log and is
            // retained (reports and log snapshots must never substitute a
            // later run's log for this one).
            string? logPath = LogPathResolver(userDirectory);
            string? logTail = ReadLogTail(logPath);
            var wer = await FindWerEvidenceAsync(exitTimeUtc, cancellationToken).ConfigureAwait(false);

            return new Shadps4TerminationReport(
                target, executable, arguments, coreVersion,
                startTimeUtc, exitTimeUtc - startTimeUtc,
                code, category, Shadps4ExitStatus.StatusName(code),
                logTail, wer?.Folder, wer?.DumpFile, logPath);
        }

        // ── WER evidence ─────────────────────────────────────────────────

        /// <summary>werfault may still be writing the report when the process exits - poll briefly.</summary>
        private async Task<(string Folder, string? DumpFile)?> FindWerEvidenceAsync(
            DateTime exitTimeUtc, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < PollAttempts; attempt++)
            {
                if (attempt > 0)
                    await _delay.DelayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
                var found = FindWerEvidenceOnce(exitTimeUtc);
                if (found != null) return found;
            }
            return null;
        }

        private (string Folder, string? DumpFile)? FindWerEvidenceOnce(DateTime exitTimeUtc)
        {
            DateTime now = _clock.UtcNow;

            // 1) LocalDumps, only when the machine has already configured it.
            string? localDumps = null;
            try { localDumps = LocalDumpsFolderResolver(); } catch { }
            if (!string.IsNullOrWhiteSpace(localDumps) && Directory.Exists(localDumps))
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(localDumps, "shadPS4.exe*.dmp"))
                    {
                        if (IsFresh(LastWriteUtc(file), exitTimeUtc, now))
                            return (localDumps, file);
                    }
                }
                catch { /* unreadable dump folder - ignore */ }
            }

            // 2) WER report queue and archive (report folders, not guaranteed dumps).
            foreach (string root in WerRootsResolver())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (string dir in Directory.EnumerateDirectories(root, "AppCrash_shadPS4.exe_*"))
                    {
                        if (!IsFresh(LastWriteUtc(dir), exitTimeUtc, now)) continue;
                        string? dump = FindDumpArtifact(dir);
                        return (dir, dump);
                    }
                }
                catch { /* unreadable WER root - ignore */ }
            }

            return null;
        }

        /// <summary>Written around the exit time: not stale pre-existing evidence, not from a future run.</summary>
        private static bool IsFresh(DateTime lastWriteUtc, DateTime exitTimeUtc, DateTime now)
            => lastWriteUtc >= exitTimeUtc - FreshBeforeExit && lastWriteUtc <= now + FreshAfterExit;

        /// <summary>A report folder may hold only metadata (Report.wer) - dumps are not guaranteed.</summary>
        private static string? FindDumpArtifact(string reportFolder)
        {
            try
            {
                foreach (string pattern in new[] { "*.hdmp", "*.dmp" })
                {
                    string? first = Directory.EnumerateFiles(reportFolder, pattern)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (first != null) return first;
                }
            }
            catch { }
            return null;
        }

        private static DateTime LastWriteUtc(string path)
        {
            try { return Directory.GetLastWriteTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }

        /// <summary>
        /// Read-only probe of the LocalDumps WER configuration. Returns the
        /// configured DumpFolder, or the documented default folder only when
        /// LocalDumps itself is configured (without it, %LOCALAPPDATA%\
        /// CrashDumps does not exist on stock Windows). Never writes anything.
        /// </summary>
        internal static string? ReadConfiguredLocalDumpsFolder()
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps");
                    if (key == null) continue;
                    string? folder = key.GetValue("DumpFolder") as string;
                    if (!string.IsNullOrWhiteSpace(folder))
                        return folder;
                    // LocalDumps is configured but uses the documented default.
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CrashDumps");
                }
                catch { }
            }
            return null;
        }

        private static IReadOnlyList<string> DefaultWerRoots()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new[]
            {
                Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"),
                Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
            };
        }

        // ── shadPS4 log tail ─────────────────────────────────────────────

        /// <summary>
        /// Probes known shadPS4 log locations read-only and returns the first
        /// that exists. The log location is build-dependent (currently
        /// unverified for the supported builds), so this is best-effort: no
        /// match returns null and the report simply has no log context.
        /// </summary>
        internal static string? ResolveLogPathBestEffort(string? userDirectory)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(userDirectory))
            {
                candidates.Add(Path.Combine(userDirectory, "log", "shad_log.txt"));
                candidates.Add(Path.Combine(userDirectory, "shad_log.txt"));
            }
            // Portable layouts keep the user folder beside the executable;
            // the resolver may not have known it, so probe AppData too.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            candidates.Add(Path.Combine(appData, "shadPS4", "log", "shad_log.txt"));
            candidates.Add(Path.Combine(appData, "shadPS4", "shad_log.txt"));

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Last 200 lines / 64 KB of the log, UTF-8 tolerant, or null when unreadable.</summary>
        private static string? ReadLogTail(string? logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath)) return null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // The emulator is dead by the time we read, but a watcher
                    // or scanner may briefly hold the file - share anyway.
                    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    long length = fs.Length;
                    long start = Math.Max(0, length - MaxLogBytes);
                    fs.Position = start;
                    // BOM detection strips a leading UTF-8 BOM when the read
                    // starts at offset 0 (small files).
                    using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    if (start > 0)
                    {
                        // The 64 KB window can begin mid-line - skip the
                        // partial first line so the tail starts at a real
                        // line boundary (never a fragment like "nk:449 ...").
                        int c = sr.Read();
                        while (c >= 0 && c != '\n') c = sr.Read();
                    }
                    string tail = sr.ReadToEnd();
                    if (tail.Length == 0) return "";
                    var lines = tail.Split('\n')
                        .Select(l => l.TrimEnd('\r'))
                        .Where(l => l.Length > 0)
                        .ToList();
                    if (lines.Count > MaxLogLines)
                        lines = lines.GetRange(lines.Count - MaxLogLines, MaxLogLines);
                    return string.Join("\n", lines);
                }
                catch
                {
                    // briefly locked or unreadable - retry, then give up silently
                }
            }
            return null;
        }
    }
}
