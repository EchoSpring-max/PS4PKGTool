#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Utilities.TrophyMetadata
{
    public sealed class NpbindExtractionResult
    {
        public string? NpCommunicationId { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
        public bool Succeeded => NpCommunicationId != null;
    }

    /// <summary>Extracts only Sc0/npbind.dat and discards the temporary binary after reading its NPWR token.</summary>
    public sealed class NpbindExtractor
    {
        private const string DefaultPasscode = "00000000000000000000000000000000";
        private readonly NpCommunicationIdResolver _resolver = new();

        public async Task<NpbindExtractionResult> ExtractAsync(
            string orbisPubCmdPath,
            string pkgPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(orbisPubCmdPath))
                return Failure("orbis-pub-cmd.exe was not found.");
            if (!File.Exists(pkgPath))
                return Failure("The selected PKG was not found.");

            // Same safe-orbis pattern as every other extraction: the PKG is
            // staged only when orbis-pub-cmd cannot take the original path
            // (orbis-pub-cmd uses ANSI file APIs - non-ASCII directory
            // segments are just as fatal as a non-ASCII file name). The
            // staging is restored in a finally so no exit path skips it.
            OrbisSafePkgOperation? operation = null;
            string? outputDirectory = null;
            NpbindExtractionResult result;
            try
            {
                operation = OrbisSafePkgOperation.Prepare(pkgPath);
                outputDirectory = Helper.CreateOrbisTempDir("n");
                result = await RunOrbisAsync(
                    Path.GetFullPath(orbisPubCmdPath),
                    operation.OrbisPath,
                    outputDirectory,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = Failure(ex.Message);
            }
            finally
            {
                if (operation != null)
                {
                    OrbisSafePkgRestoreResult restore = operation.Restore();
                    if (!restore.Succeeded)
                    {
                        result = Failure(restore.ErrorMessage +
                            " Recovery data remains in: " + restore.RecoveryDirectory);
                    }
                }
                try
                {
                    if (outputDirectory != null && Directory.Exists(outputDirectory))
                        Directory.Delete(outputDirectory, true);
                }
                catch { /* best-effort scratch cleanup */ }
            }
            return result;
        }

        private async Task<NpbindExtractionResult> RunOrbisAsync(
            string orbisPubCmdPath,
            string safePkgPath,
            string workDirectory,
            CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = orbisPubCmdPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(orbisPubCmdPath) ?? workDirectory
                }
            };
            process.StartInfo.ArgumentList.Add("img_extract");
            process.StartInfo.ArgumentList.Add("--passcode");
            process.StartInfo.ArgumentList.Add(DefaultPasscode);
            OrbisCommandOptions.AddConfiguredTempPath(process.StartInfo);
            process.StartInfo.ArgumentList.Add(safePkgPath + ":Sc0/npbind.dat");
            process.StartInfo.ArgumentList.Add(workDirectory);

            process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
                catch { }
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                return Failure(cancellationToken.IsCancellationRequested
                    ? "NP Communication ID extraction was cancelled."
                    : "NP Communication ID extraction timed out.");
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            string? npbindPath = Directory.EnumerateFiles(workDirectory, "npbind.dat", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (npbindPath == null)
                return Failure(DescribeFailure(process.ExitCode, stdout, stderr));

            string? id = _resolver.ResolveFromFile(npbindPath);
            return id == null
                ? Failure("Sc0/npbind.dat was extracted, but no valid NPWRxxxxx_00 value was found.")
                : new NpbindExtractionResult { NpCommunicationId = id };
        }

        private static string DescribeFailure(int exitCode, string stdout, string stderr)
        {
            string detail = string.Join(" ", new[] { stderr, stdout }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().Replace('\r', ' ').Replace('\n', ' ')));
            if (detail.Length > 500) detail = detail[..500];
            return string.IsNullOrEmpty(detail)
                ? $"orbis-pub-cmd exited with code {exitCode} without producing npbind.dat."
                : $"orbis-pub-cmd exited with code {exitCode}: {detail}";
        }

        private static NpbindExtractionResult Failure(string message) =>
            new() { ErrorMessage = message };
    }
}
