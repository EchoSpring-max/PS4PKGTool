using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Utilities
{
    internal sealed record PkgDirectoryScanIssue(string Path, string Message);

    internal sealed record PkgDirectoryScanResult(
        IReadOnlyList<string> Files,
        IReadOnlyList<PkgDirectoryScanIssue> Issues);

    /// <summary>
    /// Enumerates PKGs one directory at a time so an inaccessible child does
    /// not abort the rest of a library. A non-recursive scan includes the root
    /// and its immediate children, matching the application's historical UI.
    /// </summary>
    internal static class PkgDirectoryScanner
    {
        internal static PkgDirectoryScanResult Scan(
            string root, bool recursive, IReadOnlyCollection<string>? excludedDirectoryNames = null,
            bool includeImmediateChildrenWhenNonRecursive = true)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var issues = new List<PkgDirectoryScanIssue>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<(string Path, int Depth)>();
            var excluded = new HashSet<string>(
                excludedDirectoryNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            try
            {
                pending.Push((Path.GetFullPath(root), 0));
            }
            catch (Exception ex)
            {
                issues.Add(new PkgDirectoryScanIssue(root, ex.Message));
                return new PkgDirectoryScanResult(files.ToList(), issues);
            }

            while (pending.Count > 0)
            {
                var (directory, depth) = pending.Pop();
                if (!visited.Add(directory)) continue;

                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.PKG", SearchOption.TopDirectoryOnly))
                        files.Add(file);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    issues.Add(new PkgDirectoryScanIssue(directory, ex.Message));
                }

                if (!recursive && (depth >= 1 || !includeImmediateChildrenWhenNonRecursive)) continue;

                try
                {
                    foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (excluded.Contains(Path.GetFileName(child))) continue;
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                                continue;
                            pending.Push((Path.GetFullPath(child), depth + 1));
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                        {
                            issues.Add(new PkgDirectoryScanIssue(child, ex.Message));
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    issues.Add(new PkgDirectoryScanIssue(directory, ex.Message));
                }
            }

            return new PkgDirectoryScanResult(
                files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), issues);
        }
    }
}
