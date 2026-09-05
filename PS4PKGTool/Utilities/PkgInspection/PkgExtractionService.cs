using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Utilities.PkgInspection
{
    /// <summary>
    /// Full-PKG extraction via OrbisPkgTool.PkgReader in process: the PKG is
    /// opened read-only and every Sc0 + Image0 entry is decrypted and written
    /// straight into the destination. No orbis-pub-cmd spawn, no ASCII-safe
    /// temp staging (Unicode package and destination paths just work), no
    /// cross-volume move step. The Explorer shell integration and the Mini
    /// PKG Viewer are callers of this same pipeline.
    /// </summary>
    public sealed class PkgExtractionService
    {
        private readonly string _passcode;

        public PkgExtractionService(string? passcode = null)
        {
            // Null/empty keeps the standard all-zero default; the
            // PkgFileListingService.NoPasscode sentinel maps to the same
            // default (PkgReader itself falls back to RSA dk3 recovery for
            // official PKGs - the equivalent of --no_passcode).
            _passcode = string.IsNullOrWhiteSpace(passcode) || passcode == PkgFileListingService.NoPasscode
                ? PkgFileListingService.DefaultPasscode
                : passcode;
        }

        /// <summary>
        /// Extracts the whole package into destination. Returns (succeeded,
        /// message); "Cancelled" is reported when ct fires. The package file
        /// itself is only ever opened read-only - no rename, no move, no
        /// recovery sidecars. A wrong or malformed passcode fails before any
        /// file is written, so the shell's prompt-and-retry loop restarts
        /// clean.
        /// </summary>
        public async Task<(bool Succeeded, string Message)> ExtractFullAsync(
            string sourcePath, string destination,
            IProgress<string>? progress = null, CancellationToken ct = default,
            IProgress<(int Current, int Total, string CurrentFile)>? fileProgress = null,
            bool logCompletion = true)
        {
            try
            {
                progress?.Report("Preparing package...");
                Directory.CreateDirectory(destination);

                progress?.Report("Extracting game files...");
                // Progress<T> built here captures the caller's context, so
                // per-file reports keep marshaling to the UI thread.
                var textProgress = progress == null
                    ? null
                    : new Progress<(int Current, int Total, string File)>(p =>
                        progress.Report(p.Total > 0
                            ? $"Extracting {p.Current + 1}/{p.Total}: {p.File}"
                            : $"Extracting {p.File}"));

                var extractionProgress = fileProgress ?? textProgress;

                var failures = await Task.Run(() =>
                {
                    using var reader = new OrbisPkgTool.PkgReader(sourcePath, _passcode);
                    return reader.ExtractAll(destination, extractionProgress,
                        new OrbisPkgTool.ExtractAllOptions { CancellationToken = ct });
                }, ct).ConfigureAwait(false);

                if (failures.Count > 0)
                {
                    Logger.LogWarning($"PkgExtractionService: {failures.Count} entries failed for {sourcePath}");
                    return (false, FormatFailures(failures));
                }

                // Internal pipeline callers (e.g. the FFPFSC converter) extract
                // into a temporary GUID work folder that is deleted afterwards;
                // they log their own clean completion line instead.
                if (logCompletion)
                    Logger.LogInformation($"PkgExtractionService: {sourcePath} -> {destination}");
                return (true, "");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (false, "Cancelled");
            }
            catch (Exception ex)
            {
                // "Passcode mismatch." and wrong-length passcodes both land
                // here with "passcode" in the message - ShellCommands keys
                // its 5-attempt retry prompt on exactly that.
                return (false, "Extraction failed: " + ex.Message);
            }
        }

        private static string FormatFailures(System.Collections.Generic.IReadOnlyList<OrbisPkgTool.ExtractionFailure> failures)
        {
            string summary = string.Join("\n",
                failures.Take(5).Select(f => $"{f.Path}: {f.Exception.Message}"));
            if (failures.Count > 5)
                summary += $"\n... and {failures.Count - 5} more";
            return $"{failures.Count} {(failures.Count == 1 ? "entry" : "entries")} failed to extract:\n{summary}";
        }

        /// <summary>
        /// Same folder-name rules as PS4_Tools' SanitizeFileName (used by the
        /// Mini Viewer and the main app): full-width substitutes for the
        /// forbidden characters, control and invalid characters become
        /// underscores, reserved device names get prefixed.
        /// </summary>
        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (SafeSubstitutes.TryGetValue(c, out char sub))
                    sb.Append(sub);
                else if (char.IsControl(c) || Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            string result = sb.ToString().TrimEnd('.', ' ');

            string upper = result.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL" ||
                (upper.Length == 4 && upper.StartsWith("COM") && upper[3] >= '1' && upper[3] <= '9') ||
                (upper.Length == 4 && upper.StartsWith("LPT") && upper[3] >= '1' && upper[3] <= '9'))
                result = "_" + result;

            return string.IsNullOrEmpty(result) ? "_" : result;
        }

        private static readonly System.Collections.Generic.Dictionary<char, char> SafeSubstitutes =
            new System.Collections.Generic.Dictionary<char, char>
        {
            ['<'] = '＜',
            ['>'] = '＞',
            [':'] = '：',
            ['"'] = '＂',
            ['/'] = '／',
            ['\\'] = '＼',
            ['|'] = '｜',
            ['?'] = '？',
            ['*'] = '＊',
        };
    }
}
