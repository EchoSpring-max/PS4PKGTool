using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.Shadps4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS4PKGTool.Tests
{
    [TestClass]
    public class Shadps4SetupTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_setup_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        private const string StableJson = """
        [
          {"tag_name":"v.0.16.0","published_at":"2026-06-01T00:00:00Z","prerelease":false,
           "assets":[{"name":"shadps4-win64-sdl-0.16.0.zip","size":32000000,
             "browser_download_url":"https://github.com/example/a.zip","digest":"sha256:aaaa"}]},
          {"tag_name":"v.0.17.0","published_at":"2026-07-30T00:00:00Z","prerelease":false,
           "assets":[{"name":"shadps4-win64-sdl-0.17.0.zip","size":32999497,
             "browser_download_url":"https://github.com/example/b.zip","digest":"sha256:bbbb"}]}
        ]
        """;

        private const string NightlyJson = """
        [
          {"tag_name":"Pre-release-shadPS4-2026-08-13-e81418b46ea2b2ad8822d5d98b2210cf118d159e",
           "published_at":"2026-08-13T17:15:48Z","prerelease":true,
           "assets":[{"name":"shadps4-win64-sdl-2026-08-13-e81418b.zip","size":32795394,
             "browser_download_url":"https://github.com/example/c.zip","digest":"sha256:cccc"}]},
          {"tag_name":"Pre-release-shadPS4-2026-08-12-1111111111111111111111111111111111111111",
           "published_at":"2026-08-12T00:00:00Z","prerelease":true,
           "assets":[{"name":"shadps4-win64-sdl-2026-08-12-1111111.zip","size":31000000,
             "browser_download_url":"https://github.com/example/d.zip","digest":"sha256:dddd"}]}
        ]
        """;

        private const string LauncherJson = """
        [
          {"tag_name":"shadPS4QtLauncher-2026-08-08-a12b988ef35d98f2222a614c05498b27fef87121",
           "published_at":"2026-08-08T05:01:08Z","prerelease":true,
           "assets":[{"name":"shadPS4QtLauncher-win64-qt-2026-08-08-a12b988.zip","size":27338491,
             "browser_download_url":"https://github.com/example/e.zip","digest":"sha256:eeee"}]}
        ]
        """;

        // ── feed parsing ──

        [TestMethod]
        public void Feed_ParsesStableReleases_WindowsAssetsOnly()
        {
            var releases = Shadps4ReleaseFeed.ParseReleases(StableJson, Shadps4FeedKind.CoreStable, "shadps4-emu/shadPS4");

            Assert.AreEqual(2, releases.Count);
            var v17 = releases.Single(r => r.Tag == "v.0.17.0");
            Assert.IsFalse(v17.IsPrerelease);
            Assert.AreEqual("shadps4-win64-sdl-0.17.0.zip", v17.AssetName);
            Assert.AreEqual("bbbb", v17.AssetDigestSha256, "GitHub digest is kept (sha256: prefix stripped)");
            Assert.AreEqual(32999497, v17.SizeBytes);
        }

        [TestMethod]
        public void Feed_NightlyTagCarriesTheRealEmulatorCommit()
        {
            var releases = Shadps4ReleaseFeed.ParseReleases(NightlyJson, Shadps4FeedKind.CoreNightly, "shadps4-emu/shadps4-binaries-Windows");

            Assert.AreEqual(2, releases.Count);
            Assert.AreEqual("e81418b46ea2b2ad8822d5d98b2210cf118d159e", releases[0].Commit,
                "the binaries-repo tag sha is the emulator commit, kept separately from any release-repo metadata");
            Assert.AreEqual("e81418b46ea2b2ad8822d5d98b2210cf118d159e", releases[0].BuildId);
            Assert.IsTrue(releases[0].IsPrerelease);
        }

        [TestMethod]
        public void Feed_QtLauncherParsesWithItsOwnCommit()
        {
            var releases = Shadps4ReleaseFeed.ParseReleases(LauncherJson, Shadps4FeedKind.QtLauncher, "shadps4-emu/shadps4-qtlauncher");

            Assert.AreEqual(1, releases.Count);
            Assert.AreEqual("a12b988ef35d98f2222a614c05498b27fef87121", releases[0].Commit);
            Assert.AreEqual("shadPS4QtLauncher-win64-qt-2026-08-08-a12b988.zip", releases[0].AssetName);
        }

        [TestMethod]
        public void Feed_StableTagsHaveNoCommit_UseTagAsBuildId()
        {
            var releases = Shadps4ReleaseFeed.ParseReleases(StableJson, Shadps4FeedKind.CoreStable, "x");
            var v17 = releases.Single(r => r.Tag == "v.0.17.0");
            Assert.AreEqual("", v17.Commit);
            Assert.AreEqual("v.0.17.0", v17.BuildId);
        }

        [TestMethod]
        public void Feed_CacheIsUsedWithinTtl_AndExpires()
        {
            var clock = new FakeClock(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
            string cachePath = Path.Combine(_tempRoot, "releases.json");

            // Seed the cache through a fake handler serving the fixture data.
            var feed = new Shadps4ReleaseFeed(cachePath, clock,
                http: new HttpClient(new FakeHandler(200, StableJson)), cacheTtl: TimeSpan.FromHours(1));
            var first = feed.GetReleasesAsync(Shadps4FeedKind.CoreStable).Result;
            Assert.IsTrue(first.Succeeded);
            Assert.AreEqual(2, first.Releases!.Count);

            // Same time -> cache hit (fresh instance, same file, no network).
            var feed2 = new Shadps4ReleaseFeed(cachePath, clock, cacheTtl: TimeSpan.FromHours(1));
            var cached = feed2.GetReleasesAsync(Shadps4FeedKind.CoreStable).Result;
            Assert.IsTrue(cached.Succeeded);
            Assert.AreEqual(2, cached.Releases!.Count);

            // 2 hours later -> expired -> tries the network and fails cleanly.
            clock.Now = clock.Now.AddHours(2);
            var feed3 = new Shadps4ReleaseFeed(cachePath, clock,
                http: new HttpClient(new FakeHandler(500)), cacheTtl: TimeSpan.FromHours(1));
            var expired = feed3.GetReleasesAsync(Shadps4FeedKind.CoreStable).Result;
            Assert.IsNull(expired.Releases);
            Assert.IsNotNull(expired.Error);
        }

        [TestMethod]
        public void Feed_GitHubRefusal_IsGeneric()
        {
            var feed = new Shadps4ReleaseFeed(
                Path.Combine(_tempRoot, "r.json"), new SystemClock(),
                http: new HttpClient(new FakeHandler(403)), cacheTtl: TimeSpan.FromHours(1));

            var result = feed.GetReleasesAsync(Shadps4FeedKind.CoreStable).Result;

            Assert.IsNull(result.Releases);
            StringAssert.Contains(result.Error, "GitHub temporarily refused the request");
        }

        // ── recommendation policy ──

        [TestMethod]
        public void Recommendation_StableCore_LauncherPrerelease_ConfidenceUnknown()
        {
            var feed = new FakeFeed(
                (Shadps4FeedKind.CoreStable, new Shadps4FeedResult(Parse(StableJson, Shadps4FeedKind.CoreStable), null)),
                (Shadps4FeedKind.QtLauncher, new Shadps4FeedResult(Parse(LauncherJson, Shadps4FeedKind.QtLauncher), null)));

            var rec = new Shadps4RecommendationService(feed).GetRecommendedAsync().Result;

            Assert.AreEqual("v.0.17.0", rec.Core!.Tag, "recommended core is the latest STABLE, never a prerelease");
            Assert.AreEqual("shadPS4QtLauncher-2026-08-08-a12b988ef35d98f2222a614c05498b27fef87121", rec.QtLauncher!.Tag);
            Assert.AreEqual(Shadps4CompatibilityConfidence.Unknown, rec.Compatibility,
                "no authoritative upstream pairing exists - never inferred");
        }

        [TestMethod]
        public void Recommendation_FeedError_SurfacesCoreSide()
        {
            var feed = new FakeFeed(
                (Shadps4FeedKind.CoreStable, new Shadps4FeedResult(null, "GitHub temporarily refused the request. Try again later.")),
                (Shadps4FeedKind.QtLauncher, new Shadps4FeedResult(Parse(LauncherJson, Shadps4FeedKind.QtLauncher), null)));

            var rec = new Shadps4RecommendationService(feed).GetRecommendedAsync().Result;

            Assert.IsNull(rec.Core);
            Assert.IsNotNull(rec.QtLauncher);
        }

        // ── managed build store ──

        [TestMethod]
        public void Store_CommitWritesVersionedDirWithManifest()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new FakeClock(new DateTime(2026, 8, 14, 1, 0, 0, DateTimeKind.Utc)));

            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "fake core");

            var release = Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0];
            var build = store.Commit(release, staging);

            Assert.AreEqual("e81418b46ea2b2ad8822d5d98b2210cf118d159e", build.BuildId);
            string dir = Path.Combine(root, "builds", "core-e81418b46ea2b2ad8822d5d98b2210cf118d159e");
            Assert.AreEqual(dir, build.DirectoryPath);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "ps4pkgtool-manifest.json")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "shadPS4.exe")));
            Assert.IsFalse(Directory.Exists(staging), "staging is moved, not copied");
        }

        [TestMethod]
        public void Store_ManifestCarriesEmulatorCommitSeparately()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "x");

            var build = store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], staging);

            Assert.AreEqual("e81418b46ea2b2ad8822d5d98b2210cf118d159e", build.Manifest.Commit);
            Assert.AreEqual("Pre-release-shadPS4-2026-08-13-e81418b46ea2b2ad8822d5d98b2210cf118d159e", build.Manifest.Release);
            Assert.AreEqual("PS4PKGTool", build.Manifest.ManagedBy);
            Assert.AreEqual("core", build.Manifest.Component);
        }

        [TestMethod]
        public void Store_ListFindsExe_AndToleratesOrphans()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "x");
            store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], staging);

            // Orphan dir (interrupted commit) with no manifest - must be tolerated.
            Directory.CreateDirectory(Path.Combine(root, "builds", "core-deadbeef"));
            File.WriteAllText(Path.Combine(root, "builds", "core-deadbeef", "shadPS4.exe"), "y");

            var builds = store.ListBuilds(Shadps4Component.Core);

            Assert.AreEqual(1, builds.Count, "orphan dirs are ignored");
            Assert.AreEqual(Path.Combine(root, "builds", "core-e81418b46ea2b2ad8822d5d98b2210cf118d159e", "shadPS4.exe"),
                store.FindExe(Shadps4Component.Core, "e81418b46ea2b2ad8822d5d98b2210cf118d159e"));
        }

        [TestMethod]
        public void Store_CommitRejectsMissingExpectedExe()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "somefile.txt"), "no exe");

            bool threw = false;
            try { store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], staging); }
            catch (InvalidOperationException) { threw = true; }
            Assert.IsTrue(threw, "a build without the expected executable must be rejected");
        }

        [TestMethod]
        public void Store_ResolveManaged_WiresIntoActiveCoreModel()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "x");
            var build = store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], staging);

            string? exe = Shadps4ActiveCore.ResolveExecutable(
                Shadps4ActiveCore.ForManaged(build.BuildId),
                store.ResolveManagedExecutable,
                out string? error);

            Assert.AreEqual(build.ExecutablePath, exe);
            Assert.IsNull(error);
        }

        // ── settings ──

        [TestMethod]
        public void Settings_Shadps4ManagedRoot_RoundTrips()
        {
            string file = Path.Combine(_tempRoot, "Settings.conf");
            var settings = new PS4PKGTool.Utilities.Settings.AppSettings
            {
                Shadps4ManagedRoot = @"D:\Emu Storage\managed",
            };
            PS4PKGTool.Utilities.Settings.SettingsManager.SaveSettings(settings, file);

            var loaded = PS4PKGTool.Utilities.Settings.SettingsManager.LoadSettings(file);

            Assert.AreEqual(@"D:\Emu Storage\managed", loaded.Shadps4ManagedRoot);
            Assert.AreEqual("", new PS4PKGTool.Utilities.Settings.AppSettings().Shadps4ManagedRoot,
                "default is empty = the LocalAppData default root");
        }

        // ── helpers ──

        private static IReadOnlyList<Shadps4ReleaseInfo> Parse(string json, Shadps4FeedKind feed)
            => Shadps4ReleaseFeed.ParseReleases(json, feed, "repo");

        private sealed class FakeClock : IClock
        {
            public FakeClock(DateTime now) { Now = now; }
            public DateTime Now { get; set; }
            public DateTime UtcNow => Now;
        }

        // NOTE: subclassing HttpClient does NOT intercept requests in .NET
        // Core+ - the working seam is a custom HttpMessageHandler.
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly int _status;
            private readonly string? _body;
            public FakeHandler(int status, string? body = null) { _status = status; _body = body; }

            protected override Task<HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage((System.Net.HttpStatusCode)_status);
                if (_body != null) response.Content = new StringContent(_body);
                return Task.FromResult(response);
            }
        }

        private sealed class FakeFeed : IShadps4FeedClient
        {
            private readonly Dictionary<Shadps4FeedKind, Shadps4FeedResult> _results;
            public FakeFeed(params (Shadps4FeedKind Feed, Shadps4FeedResult Result)[] entries)
            {
                _results = entries.ToDictionary(e => e.Feed, e => e.Result);
            }

            public Task<Shadps4FeedResult> GetReleasesAsync(Shadps4FeedKind feed, CancellationToken ct = default)
                => Task.FromResult(_results.TryGetValue(feed, out var r) ? r : new Shadps4FeedResult(null, "no feed"));
        }
    }
}
