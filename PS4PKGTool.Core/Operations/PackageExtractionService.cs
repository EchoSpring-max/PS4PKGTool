using System.Text;

namespace PS4PKGTool.Utilities.PkgInspection;

/// <summary>Platform-neutral, in-process full PKG extraction service.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Preserves the established production extraction API.")]
public interface IPackageExtractionService
{
    Task<(bool Succeeded, string Message)> ExtractFullAsync(string sourcePath, string destination,
        IProgress<string>? progress = null, CancellationToken ct = default,
        IProgress<(int Current, int Total, string CurrentFile)>? fileProgress = null,
        bool logCompletion = true);
}

public sealed class PkgExtractionService : IPackageExtractionService
{
    public const string DefaultPasscode = "00000000000000000000000000000000";
    public const string NoPasscode = "\x1";
    private readonly string _passcode;

    public PkgExtractionService(string? passcode = null) => _passcode = string.IsNullOrWhiteSpace(passcode) || passcode == NoPasscode
        ? DefaultPasscode : passcode;

    public async Task<(bool Succeeded, string Message)> ExtractFullAsync(string sourcePath, string destination,
        IProgress<string>? progress = null, CancellationToken ct = default,
        IProgress<(int Current, int Total, string CurrentFile)>? fileProgress = null, bool logCompletion = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        try
        {
            progress?.Report("Preparing package...");
            Directory.CreateDirectory(destination);
            progress?.Report("Extracting game files...");
            var textProgress = progress is null ? null : new Progress<(int Current, int Total, string File)>(p =>
                progress.Report(p.Total > 0 ? $"Extracting {p.Current + 1}/{p.Total}: {p.File}" : $"Extracting {p.File}"));
            var extractionProgress = fileProgress ?? textProgress;
            var failures = await Task.Run(() =>
            {
                using var reader = new OrbisPkgTool.PkgReader(sourcePath, _passcode);
                return reader.ExtractAll(destination, extractionProgress,
                    new OrbisPkgTool.ExtractAllOptions { CancellationToken = ct });
            }, ct).ConfigureAwait(false);
            return failures.Count == 0 ? (true, string.Empty) : (false, FormatFailures(failures));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return (false, "Cancelled"); }
        catch (Exception ex) { return (false, "Extraction failed: " + ex.Message); }
    }

    private static string FormatFailures(IReadOnlyList<OrbisPkgTool.ExtractionFailure> failures)
    {
        string summary = string.Join("\n", failures.Take(5).Select(f => $"{f.Path}: {f.Exception.Message}"));
        if (failures.Count > 5) summary += $"\n... and {failures.Count - 5} more";
        return $"{failures.Count} {(failures.Count == 1 ? "entry" : "entries")} failed to extract:\n{summary}";
    }

    public static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_";
        char[] invalid = Path.GetInvalidFileNameChars();
        var result = new StringBuilder(name.Length);
        foreach (char c in name)
            result.Append(SafeSubstitutes.TryGetValue(c, out char substitute) ? substitute : char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        string sanitized = result.ToString().TrimEnd('.', ' ');
        string upper = sanitized.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL" || (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) && upper[3] is >= '1' and <= '9')) sanitized = "_" + sanitized;
        return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
    }

    private static readonly Dictionary<char, char> SafeSubstitutes = new()
    {
        ['<'] = '＜', ['>'] = '＞', [':'] = '：', ['\"'] = '＂', ['/'] = '／', ['\\'] = '＼', ['|'] = '｜', ['?'] = '？', ['*'] = '＊'
    };
}
