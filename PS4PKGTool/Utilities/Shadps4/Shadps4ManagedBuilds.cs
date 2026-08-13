using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        string ExecutablePath);

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
            _root = rootPath ?? DefaultRootPath();
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
