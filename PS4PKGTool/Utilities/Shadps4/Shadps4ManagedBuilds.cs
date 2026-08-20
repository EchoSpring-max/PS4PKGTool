using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>PS4PKGTool-owned metadata stored next to each managed build.</summary>
    public sealed class Shadps4BuildManifest
    {
        public string ManagedBy { get; set; } = "PS4PKGTool";
        public string Component { get; set; } = "";      // "core" | "qtlauncher"
        public string Repository { get; set; } = "";
        public string Release { get; set; } = "";         // upstream release tag
        public string Commit { get; set; } = "";          // emulator/launcher commit (NOT a release-repo automation sha)
        public string Asset { get; set; } = "";
        public string InstalledAtUtc { get; set; } = "";
    }

    /// <summary>One installed managed build on disk.</summary>
    public sealed record Shadps4InstalledBuild(
        string BuildId,
        Shadps4Component Component,
        string DirectoryPath,
        Shadps4BuildManifest Manifest,
        string ExecutablePath)
    {
        private static readonly Regex DateInTag = new(@"\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);

        /// <summary>
        /// Human-readable name for lists and dialogs: stable tags (v.0.17.0)
        /// as they are; nightly/launcher build ids (commit shas) become
        /// "date (short sha)" parsed from the release tag, so users can tell
        /// versions apart without reading checksums.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!LooksLikeSha(BuildId)) return BuildId;
                string date = DateInTag.Match(Manifest.Release ?? "").Value;
                string shortSha = BuildId.Length > 8 ? BuildId.Substring(0, 8) : BuildId;
                return string.IsNullOrEmpty(date) ? BuildId : $"{date} ({shortSha})";
            }
        }

        private static bool LooksLikeSha(string id)
            => id.Length == 40 && id.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// Versioned storage for PS4PKGTool-managed shadPS4 builds:
    ///   &lt;managed root&gt;\builds\core-&lt;buildId&gt;\
    ///   &lt;managed root&gt;\builds\launcher-&lt;buildId&gt;\
    /// Every build dir carries a ps4pkgtool-manifest.json. Old builds are
    /// NEVER overwritten or deleted - rollback is just switching the active
    /// build. Adopted installations live OUTSIDE this store and are never
    /// written to: Commit only moves staging dirs under the builds root.
    /// </summary>
    public sealed class Shadps4ManagedBuilds
    {
        public const string ManifestFileName = "ps4pkgtool-manifest.json";

        /// <summary>Default managed root: %LOCALAPPDATA%\PS4PKGTool\shadPS4.</summary>
        public static string DefaultRootPath()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS4PKGTool", "shadPS4");

        private readonly string _root;

        public Shadps4ManagedBuilds(string? rootPath = null, IClock? clock = null)
        {
            // Empty string must fall back to the default root too: settings
            // load an unset managed root as "" and a bare "" would point the
            // store at a relative "builds" folder (no builds found).
            _root = string.IsNullOrWhiteSpace(rootPath) ? DefaultRootPath() : rootPath;
            Clock = clock ?? new SystemClock();
        }

        public IClock Clock { get; }

        public string RootPath => _root;

        private string BuildsRoot => Path.Combine(_root, "builds");

        public string BuildDirectory(Shadps4Component component, string buildId)
            => Path.Combine(BuildsRoot, ComponentPrefix(component) + buildId);

        /// <summary>
        /// Moves a fully-extracted and validated staging directory into the
        /// store as a versioned build and writes its manifest (manifest is
        /// written into staging first, then a single atomic Directory.Move -
        /// a crash mid-commit leaves an orphan dir that ListBuilds tolerates).
        /// </summary>
        public Shadps4InstalledBuild Commit(Shadps4ReleaseInfo release, string stagingDir)
        {
            Shadps4Component component = release.Feed == Shadps4FeedKind.QtLauncher
                ? Shadps4Component.QtLauncher
                : Shadps4Component.Core;

            string buildId = release.BuildId;
            string finalDir = BuildDirectory(component, buildId);
            if (Directory.Exists(finalDir))
                throw new InvalidOperationException($"Managed build already exists: {finalDir}");

            string exeName = component == Shadps4Component.Core
                ? Shadps4EnvironmentResolver.CoreExeFileName
                : Shadps4EnvironmentResolver.QtLauncherFileName;
            string expectedExe = Path.Combine(stagingDir, exeName);
            if (!File.Exists(expectedExe))
                throw new InvalidOperationException(
                    $"Extracted build does not contain {exeName} - security software may have quarantined it.");

            var manifest = new Shadps4BuildManifest
            {
                Component = component == Shadps4Component.Core ? "core" : "qtlauncher",
                Repository = release.Repository,
                Release = release.Tag,
                Commit = release.Commit,
                Asset = release.AssetName,
                InstalledAtUtc = Clock.UtcNow.ToString("O"),
            };
            File.WriteAllText(Path.Combine(stagingDir, ManifestFileName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            Directory.CreateDirectory(BuildsRoot);
            Directory.Move(stagingDir, finalDir);

            return new Shadps4InstalledBuild(buildId, component, finalDir, manifest,
                Path.Combine(finalDir, exeName));
        }

        /// <summary>All installed builds of a component, newest install first. Orphans (no manifest) are ignored.</summary>
        public IReadOnlyList<Shadps4InstalledBuild> ListBuilds(Shadps4Component component)
        {
            var result = new List<Shadps4InstalledBuild>();
            if (!Directory.Exists(BuildsRoot)) return result;

            string prefix = ComponentPrefix(component);
            foreach (string dir in Directory.GetDirectories(BuildsRoot))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                var manifest = ReadManifest(dir);
                if (manifest == null) continue; // orphan / interrupted commit - tolerated

                string exeName = component == Shadps4Component.Core
                    ? Shadps4EnvironmentResolver.CoreExeFileName
                    : Shadps4EnvironmentResolver.QtLauncherFileName;
                string exe = Path.Combine(dir, exeName);
                if (!File.Exists(exe)) continue;

                result.Add(new Shadps4InstalledBuild(
                    name[prefix.Length..], component, dir, manifest, exe));
            }
            return result.OrderByDescending(b => b.Manifest.InstalledAtUtc).ToList();
        }

        /// <summary>Resolves a build id to its executable, or null.</summary>
        public string? FindExe(Shadps4Component component, string buildId)
        {
            var build = ListBuilds(component).FirstOrDefault(b =>
                string.Equals(b.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
            return build?.ExecutablePath;
        }

        /// <summary>
        /// Resolves a bare build id against the store - the hook wired into
        /// Shadps4ActiveCore (which passes the id, not the full "managed:&lt;id&gt;"
        /// setting). Tries core first, then launcher.
        /// </summary>
        public string? ResolveManagedExecutable(string buildId)
            => FindExe(Shadps4Component.Core, buildId)
                ?? FindExe(Shadps4Component.QtLauncher, buildId);

        /// <summary>
        /// Permanently removes ONE managed build folder. Deliberate deletion is
        /// a user action (the Builds UI protects the active build and confirms
        /// first) - rollback stays the default mechanism, so this only removes
        /// the exact versioned directory, never the store or other builds. A
        /// missing build id is a no-op.
        /// </summary>
        public void DeleteBuild(Shadps4Component component, string buildId)
        {
            string dir = BuildDirectory(component, buildId);
            if (!Directory.Exists(dir)) return;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                throw new IOException("Could not remove the build folder:\n" + dir + "\n\n" + ex.Message);
            }
        }

        private static Shadps4BuildManifest? ReadManifest(string dir)
        {
            try
            {
                string path = Path.Combine(dir, ManifestFileName);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<Shadps4BuildManifest>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static string ComponentPrefix(Shadps4Component component)
            => component == Shadps4Component.Core ? "core-" : "launcher-";
    }
}
