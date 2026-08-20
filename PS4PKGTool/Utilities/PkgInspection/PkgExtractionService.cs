using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Utilities.PkgInspection
{
    /// <summary>
    /// Full-PKG extraction via orbis-pub-cmd img_extract against an
    /// ASCII-safe temp rename of the package (OrbisSafePkgOperation), with
    /// the result moved from an ASCII temp output dir to the real
    /// destination. This is the SAME pipeline the Mini PKG Viewer and the
    /// main app use - the shell integration is just another caller.
    /// </summary>
    public sealed class PkgExtractionService
    {
        private static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(10);

        private readonly string _orbisPubCmdPath;
        private readonly string _passcode;

        public PkgExtractionService(string? orbisPubCmdPath = null, string? passcode = null)
        {
            _orbisPubCmdPath = orbisPubCmdPath ?? Helper.OrbisPubCmd;
            // Null/empty keeps the standard all-zero default; the
            // PkgFileListingService.NoPasscode sentinel selects --no_passcode.
            _passcode = passcode == PkgFileListingService.NoPasscode
                ? PkgFileListingService.NoPasscode
                : string.IsNullOrWhiteSpace(passcode)
                    ? PkgFileListingService.DefaultPasscode
                    : passcode;
        }

        /// <summary>
        /// Extracts the whole package into destination. Returns (succeeded,
        /// message); "Cancelled" is reported when ct fires. The package is
        /// restored via OrbisSafePkgOperation on every exit path, and the
        /// ASCII staging output is removed.
        /// </summary>
        public async Task<(bool Succeeded, string Message)> ExtractFullAsync(
            string sourcePath, string destination,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            OrbisSafePkgOperation? safeOperation = null;
            string? tempOutputDir = null;
            try
            {
                progress?.Report("Preparing package...");
                Directory.CreateDirectory(destination);
                safeOperation = OrbisSafePkgOperation.Prepare(sourcePath);
                tempOutputDir = Helper.CreateOrbisTempDir("e");

                progress?.Report("Extracting game files...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = _orbisPubCmdPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("img_extract");
                PkgFileListingService.AddPasscodeArgument(startInfo, _passcode);
                OrbisCommandOptions.AddConfiguredTempPath(startInfo);
                startInfo.ArgumentList.Add(safeOperation.OrbisPath);
                startInfo.ArgumentList.Add(tempOutputDir);

                using var extract = new Process { StartInfo = startInfo };
                var stderrBuilder = new StringBuilder();
                extract.ErrorDataReceived += (_, ev) => { if (ev.Data != null) stderrBuilder.AppendLine(ev.Data); };
                extract.Start();
                extract.BeginErrorReadLine();
                Task<string> stdoutTask = extract.StandardOutput.ReadToEndAsync();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ExtractTimeout);
                try { await extract.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { if (!extract.HasExited) extract.Kill(entireProcessTree: true); extract.WaitForExit(); } catch { }
                    return (false, ct.IsCancellationRequested ? "Cancelled" : "Extraction timed out after 10 minutes.");
                }
                string stdout = await stdoutTask.ConfigureAwait(false);

                if (extract.ExitCode != 0)
                {
                    string detail = FormatOrbisError(stderrBuilder.Length > 0 ? stderrBuilder.ToString() : stdout);
                    return (false, "orbis-pub-cmd error:\n" + detail);
                }

                progress?.Report("Moving extracted files...");
                if (Directory.Exists(tempOutputDir))
                {
                    foreach (string entry in Directory.GetFileSystemEntries(tempOutputDir))
                    {
                        string dest = Path.Combine(destination, Path.GetFileName(entry));
                        try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch (Exception ex) { Logger.LogWarning("Failed to delete destination folder: " + ex.Message); }
                        if (Directory.Exists(entry))
                            SafeMoveDirectory(entry, dest);
                        else
                        {
                            try { if (File.Exists(dest)) File.Delete(dest); } catch (Exception ex) { Logger.LogWarning("Failed to delete destination file: " + ex.Message); }
                            File.Move(entry, dest);
                        }
                    }
                }

                Logger.LogInformation($"PkgExtractionService: {sourcePath} -> {destination}");
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, "Extraction failed: " + ex.Message);
            }
            finally
            {
                if (safeOperation != null)
                {
                    OrbisSafePkgRestoreResult restore = safeOperation.Restore();
                    if (!restore.Succeeded)
                        Logger.LogError("PkgExtractionService failed to restore PKG: " + restore.ErrorMessage +
                            " Recovery data remains in: " + restore.RecoveryDirectory);
                }
                if (tempOutputDir != null && Directory.Exists(tempOutputDir))
                {
                    try { Directory.Delete(tempOutputDir, true); } catch { }
                }
            }
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

        private static void SafeMoveDirectory(string src, string dst)
        {
            try { Directory.Move(src, dst); }
            catch (IOException)
            {
                foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length).TrimStart('\\', '/');
                    string target = Path.Combine(dst, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dst);
                    File.Copy(f, target, true);
                }
                Directory.Delete(src, true);
            }
        }

        private static string FormatOrbisError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "(no output from orbis-pub-cmd)";
            var errors = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(l => l.Trim())
                .ToList();
            return errors.Count > 0 ? string.Join("\n", errors) : raw.Trim();
        }
    }
}
