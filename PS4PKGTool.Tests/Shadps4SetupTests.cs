using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.Settings;
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
        public void Feed_MalformedCacheJson_FallsBackToFreshFetch()
        {
            var clock = new FakeClock(new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
            string cachePath = Path.Combine(_tempRoot, "releases.json");
            File.WriteAllText(cachePath, "this is { not valid json at all");

            var feed = new Shadps4ReleaseFeed(cachePath, clock,
                http: new HttpClient(new FakeHandler(200, StableJson)), cacheTtl: TimeSpan.FromHours(1));

            var result = feed.GetReleasesAsync(Shadps4FeedKind.CoreStable).Result;

            Assert.IsTrue(result.Succeeded, "malformed cache must never block a fresh fetch");
            Assert.AreEqual(2, result.Releases!.Count);
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
        public void Store_EmptyManagedRootFallsBackToDefaultRoot()
        {
            // Settings load an unset managed root as "" - a bare "" would
            // resolve the store to a relative "builds" folder and list
            // nothing. It must behave like null: default %LOCALAPPDATA% root.
            var store = new Shadps4ManagedBuilds("");

            Assert.AreEqual(Shadps4ManagedBuilds.DefaultRootPath(), store.RootPath);
        }

        [TestMethod]
        public void Store_DisplayName_StableTagUsedAsIs()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());
            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "x");

            var build = store.Commit(Parse(StableJson, Shadps4FeedKind.CoreStable)[1], staging);

            Assert.AreEqual("v.0.17.0", build.DisplayName);
        }

        [TestMethod]
        public void Store_DisplayName_NightlyShowsDateThenShortSha()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());
            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "x");

            var build = store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], staging);

            Assert.AreEqual("2026-08-13 (e81418b4)", build.DisplayName);
        }

        [TestMethod]
        public void Store_DisplayName_LauncherShowsDateThenShortSha()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());
            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4QtLauncher.exe"), "x");

            var build = store.Commit(Parse(LauncherJson, Shadps4FeedKind.QtLauncher)[0], staging);

            Assert.AreEqual("2026-08-08 (a12b988e)", build.DisplayName);
        }

        [TestMethod]
        public void Store_DeleteBuild_RemovesOnlyThatBuild()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());
            CommitCoreAt(store, index: 0);
            CommitCoreAt(store, index: 1);

            var ids = store.ListBuilds(Shadps4Component.Core).Select(b => b.BuildId).ToList();
            Assert.AreEqual(2, ids.Count);

            store.DeleteBuild(Shadps4Component.Core, ids[0]);

            var remaining = store.ListBuilds(Shadps4Component.Core).ToList();
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual(ids[1], remaining[0].BuildId);
            Assert.IsFalse(Directory.Exists(store.BuildDirectory(Shadps4Component.Core, ids[0])));
            Assert.IsTrue(Directory.Exists(store.BuildDirectory(Shadps4Component.Core, ids[1])));
        }

        [TestMethod]
        public void Store_DeleteBuild_MissingBuildIsNoOp()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());
            CommitCoreAt(store);

            store.DeleteBuild(Shadps4Component.Core, "0000000000000000000000000000000000000000");

            Assert.AreEqual(1, store.ListBuilds(Shadps4Component.Core).Count);
        }

        // ── display helpers ──

        [TestMethod]
        public void Display_DescribeActiveSetting_ManagedShowsBuildPrefix()
        {
            Assert.AreEqual("Build e81418b4", Shadps4SetupDisplay.DescribeActiveSetting("managed:e81418b4"));
        }

        [TestMethod]
        public void Display_DescribeActiveSetting_AdoptedShowsPath()
        {
            Assert.AreEqual(@"C:\emu\shadPS4.exe",
                Shadps4SetupDisplay.DescribeActiveSetting(@"adopted:C:\emu\shadPS4.exe"));
        }

        [TestMethod]
        public void Display_DescribeActiveSetting_EmptyShowsNotSet()
        {
            Assert.AreEqual("(not set)", Shadps4SetupDisplay.DescribeActiveSetting(""));
            Assert.AreEqual("(not set)", Shadps4SetupDisplay.DescribeActiveSetting(null));
        }

        [TestMethod]
        public void Display_ShortenPath_KeepsShortPathAndTruncatesLong()
        {
            string shortPath = @"C:\emu\shadPS4.exe";
            Assert.AreEqual(shortPath, Shadps4SetupDisplay.ShortenPath(shortPath));

            string longPath = @"C:\Users\VeryLongUserName\AppData\Roaming\shadPS4\custom-very-long-folder-name\config\settings-that-go-on-forever.json";
            string shortened = Shadps4SetupDisplay.ShortenPath(longPath);
            Assert.IsTrue(shortened.Length <= 70);
            Assert.IsTrue(shortened.EndsWith(longPath.Substring(longPath.Length - 20)));
        }

        [TestMethod]
        public void Display_ShortenPath_BlankShowsNotFound()
        {
            Assert.AreEqual("(not found)", Shadps4SetupDisplay.ShortenPath(""));
            Assert.AreEqual("(not found)", Shadps4SetupDisplay.ShortenPath(null));
        }

        // ── settings auto heal ──

        [TestMethod]
        public void Heal_EmptySettingsRestoresCoreLauncherRootAndInstallDirFromStore()
        {
            string root = Path.Combine(_tempRoot, "managed");
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            CommitCoreAt(new Shadps4ManagedBuilds(root, new FakeClock(new DateTime(2026, 8, 13, 1, 0, 0, DateTimeKind.Utc))));
            CommitLauncherAt(new Shadps4ManagedBuilds(root, new FakeClock(new DateTime(2026, 8, 13, 1, 0, 0, DateTimeKind.Utc))));

            var s = new AppSettings();
            bool changed = SettingsManager.AutoHealShadps4Settings(s, new Shadps4ManagedBuilds(root));

            Assert.IsTrue(changed);
            Assert.AreEqual(root, s.Shadps4ManagedRoot);
            Assert.AreEqual(Shadps4ActiveCore.ForManaged("e81418b46ea2b2ad8822d5d98b2210cf118d159e"), s.Shadps4ActiveCore);
            Assert.AreEqual(Shadps4ActiveCore.ForManaged("a12b988ef35d98f2222a614c05498b27fef87121"), s.Shadps4ActiveLauncher);
            Assert.AreEqual(Path.Combine(root, "Data"), s.Shadps4InstallDirectory);
        }

        [TestMethod]
        public void Heal_NewestInstalledBuildWins()
        {
            string root = Path.Combine(_tempRoot, "managed");
            // two cores installed at different times; NightlyJson[0] is the newer install
            CommitCoreAt(new Shadps4ManagedBuilds(root, new FakeClock(new DateTime(2026, 8, 12, 1, 0, 0, DateTimeKind.Utc))), index: 1);
            CommitCoreAt(new Shadps4ManagedBuilds(root, new FakeClock(new DateTime(2026, 8, 13, 1, 0, 0, DateTimeKind.Utc))), index: 0);

            var s = new AppSettings();
            SettingsManager.AutoHealShadps4Settings(s, new Shadps4ManagedBuilds(root));

            Assert.AreEqual("e81418b46ea2b2ad8822d5d98b2210cf118d159e",
                Shadps4ActiveCore.Parse(s.Shadps4ActiveCore!).Value);
        }

        [TestMethod]
        public void Heal_NeverOverridesValuesTheUserSet()
        {
            string root = Path.Combine(_tempRoot, "managed");
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            CommitCoreAt(new Shadps4ManagedBuilds(root, new SystemClock()));
            CommitLauncherAt(new Shadps4ManagedBuilds(root, new SystemClock()));

            var s = new AppSettings
            {
                Shadps4ManagedRoot = root,
                Shadps4ActiveCore = "adopted:C:\\tools\\shadPS4.exe",
                Shadps4ActiveLauncher = "adopted:C:\\tools\\shadPS4QtLauncher.exe",
                Shadps4InstallDirectory = "D:\\games",
            };
            bool changed = SettingsManager.AutoHealShadps4Settings(s, new Shadps4ManagedBuilds(root));

            Assert.IsFalse(changed, "values the user set are never touched");
            Assert.AreEqual("adopted:C:\\tools\\shadPS4.exe", s.Shadps4ActiveCore);
            Assert.AreEqual("D:\\games", s.Shadps4InstallDirectory);
        }

        [TestMethod]
        public void Heal_EmptyStoreDoesNothing()
        {
            string root = Path.Combine(_tempRoot, "managed"); // no builds, no Data dir

            var s = new AppSettings();
            bool changed = SettingsManager.AutoHealShadps4Settings(s, new Shadps4ManagedBuilds(root));

            Assert.IsFalse(changed);
            Assert.IsTrue(string.IsNullOrWhiteSpace(s.Shadps4ActiveCore));
            Assert.IsTrue(string.IsNullOrWhiteSpace(s.Shadps4InstallDirectory));
        }

        [TestMethod]
        public void Heal_DetectionOffer_TrueWhenSettingsEmptyAndStoreHasBuilds()
        {
            string root = Path.Combine(_tempRoot, "managed");
            CommitCoreAt(new Shadps4ManagedBuilds(root, new SystemClock()));
            CommitLauncherAt(new Shadps4ManagedBuilds(root, new SystemClock()));

            var s = new AppSettings();
            Assert.IsTrue(SettingsManager.CanHealShadps4Settings(s, new Shadps4ManagedBuilds(root)));
        }

        [TestMethod]
        public void Heal_DetectionOffer_FalseWhenDismissed()
        {
            string root = Path.Combine(_tempRoot, "managed");
            CommitCoreAt(new Shadps4ManagedBuilds(root, new SystemClock()));

            var s = new AppSettings { Shadps4ConfigDetectionDismissed = true };
            Assert.IsFalse(SettingsManager.CanHealShadps4Settings(s, new Shadps4ManagedBuilds(root)),
                "the user declined once - never ask again while settings persist");
        }

        [TestMethod]
        public void Heal_DetectionOffer_FalseWhenActiveCoreSet()
        {
            string root = Path.Combine(_tempRoot, "managed");
            CommitCoreAt(new Shadps4ManagedBuilds(root, new SystemClock()));

            var s = new AppSettings { Shadps4ActiveCore = "adopted:C:\\tools\\shadPS4.exe" };
            Assert.IsFalse(SettingsManager.CanHealShadps4Settings(s, new Shadps4ManagedBuilds(root)),
                "an already-configured setup is never offered");
        }

        [TestMethod]
        public void Heal_DetectionOffer_FalseWhenStoreEmpty()
        {
            string root = Path.Combine(_tempRoot, "managed");

            var s = new AppSettings();
            Assert.IsFalse(SettingsManager.CanHealShadps4Settings(s, new Shadps4ManagedBuilds(root)));
        }

        [TestMethod]
        public void Settings_ConfigDetectionDismissedFlag_RoundTrips()
        {
            string file = Path.Combine(_tempRoot, "Settings.conf");
            var settings = new AppSettings { Shadps4ConfigDetectionDismissed = true };
            SettingsManager.SaveSettings(settings, file);

            var loaded = SettingsManager.LoadSettings(file);

            Assert.IsTrue(loaded.Shadps4ConfigDetectionDismissed);
            Assert.IsFalse(new AppSettings().Shadps4ConfigDetectionDismissed, "default is not dismissed");
        }

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
        public void Store_InstallingNewVersion_KeepsOldBuildForRollback()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            string stagingA = Path.Combine(_tempRoot, "stagingA");
            Directory.CreateDirectory(stagingA);
            File.WriteAllText(Path.Combine(stagingA, "shadPS4.exe"), "old");
            store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], stagingA); // abc... e81418b

            string stagingB = Path.Combine(_tempRoot, "stagingB");
            Directory.CreateDirectory(stagingB);
            File.WriteAllText(Path.Combine(stagingB, "shadPS4.exe"), "new");
            var releaseB = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-b", "deadbeef", "b.zip", 100);
            store.Commit(releaseB, stagingB);

            var builds = store.ListBuilds(Shadps4Component.Core);

            Assert.AreEqual(2, builds.Count, "installing a newer build NEVER deletes the old one - rollback is switching back");
            Assert.IsNotNull(store.FindExe(Shadps4Component.Core, "e81418b46ea2b2ad8822d5d98b2210cf118d159e"));
            Assert.IsNotNull(store.FindExe(Shadps4Component.Core, "deadbeef"));
        }

        [TestMethod]
        public void SetupReset_ClearsStoreArtifactsAndToolSettings()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            // A committed build + a stray .work dir.
            string staging = Path.Combine(_tempRoot, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "shadPS4.exe"), "x");
            store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[0], staging);
            Directory.CreateDirectory(Path.Combine(root, ".work"));
            File.WriteAllText(Path.Combine(root, ".work", "shadps4_download.zip.part"), "partial");

            var settings = new PS4PKGTool.Utilities.Settings.AppSettings
            {
                Shadps4ActiveCore = "managed:abc1234",
                Shadps4ActiveLauncher = @"adopted:C:\emu\shadPS4QtLauncher.exe",
                Shadps4ManagedRoot = root,
                Shadps4InstallDirectory = @"C:\games",
                Shadps4ExecutablePath = @"C:\emu\old\shadPS4.exe",
            };

            Shadps4SetupReset.Reset(store, settings);

            Assert.IsFalse(Directory.Exists(Path.Combine(root, "builds")), "managed builds are removed");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, ".work")), "download artifacts are removed");
            Assert.IsTrue(Directory.Exists(root), "a CUSTOM managed root itself survives (it may hold other things)");
            Assert.AreEqual("", settings.Shadps4ActiveCore);
            Assert.AreEqual("", settings.Shadps4ActiveLauncher);
            Assert.AreEqual("", settings.Shadps4ManagedRoot);
            Assert.AreEqual("", settings.Shadps4InstallDirectory);
            Assert.AreEqual("", settings.Shadps4ExecutablePath, "legacy anchors are cleared so migration cannot resurrect them");
        }

        [TestMethod]
        public void SetupReset_DoesNotTouchAdoptedOrExternalContent()
        {
            string root = Path.Combine(_tempRoot, "managed");
            var store = new Shadps4ManagedBuilds(root, new SystemClock());

            // An unrelated folder that happens to sit inside the custom root.
            Directory.CreateDirectory(Path.Combine(root, "my own stuff"));
            File.WriteAllText(Path.Combine(root, "my own stuff", "notes.txt"), "keep me");

            Shadps4SetupReset.Reset(store, new PS4PKGTool.Utilities.Settings.AppSettings());

            Assert.IsTrue(File.Exists(Path.Combine(root, "my own stuff", "notes.txt")),
                "reset removes only PS4PKGTool artifacts, never unrelated content");
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

        // ── zip safety ──

        [TestMethod]
        public void Zip_ContainmentIsDirectoryBoundaryAware()
        {
            Assert.IsTrue(SafeZipExtractor.IsWithin(@"C:\a\stage\x", @"C:\a\stage"));
            Assert.IsFalse(SafeZipExtractor.IsWithin(@"C:\a\stage2\x", @"C:\a\stage"),
                "stage2 must never pass for stage + separator");
            Assert.IsFalse(SafeZipExtractor.IsWithin(@"C:\a\stage2", @"C:\a\stage"));
        }

        [TestMethod]
        public void Zip_TraversalAndAbsoluteEntriesAreRejected()
        {
            var evilZip = BuildZip(
                ("shadPS4.exe", new byte[] { 1 }),
                ("../evil.exe", new byte[] { 2 }),
                (@"C:\evil.exe", new byte[] { 3 }));

            bool threw = false;
            try
            {
                using var ms = new MemoryStream(evilZip);
                SafeZipExtractor.Extract(ms, Path.Combine(_tempRoot, "out"));
            }
            catch (InvalidDataException) { threw = true; }
            Assert.IsTrue(threw, "traversal and absolute entries must abort the extraction");
        }

        [TestMethod]
        public void Zip_SymlinkEntriesAreRejected()
        {
            var evilZip = BuildZip(
                ("shadPS4.exe", new byte[] { 1 }),
                ("link", new byte[] { 2 }));

            using var ms = new MemoryStream(evilZip);
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            var link = archive.GetEntry("link");
            link!.ExternalAttributes = unchecked((int)(0xA000u << 16)); // S_IFLNK
            Assert.IsTrue(SafeZipExtractor.IsSuspiciousEntry("link", link),
                "Unix symlink entries are rejected outright");
        }

        [TestMethod]
        public void Zip_CaseInsensitiveDuplicateOutputPaths_AreRejected()
        {
            // Windows is case-insensitive - File.dll and file.dll collide.
            var zip = BuildZip(
                ("File.dll", new byte[] { 1 }),
                ("file.dll", new byte[] { 2 }));

            bool threw = false;
            try
            {
                using var ms = new MemoryStream(zip);
                SafeZipExtractor.Extract(ms, Path.Combine(_tempRoot, "out"));
            }
            catch (InvalidDataException) { threw = true; }
            Assert.IsTrue(threw, "case-colliding entries must abort instead of silently overwriting");
        }

        [TestMethod]
        public void Zip_TotalUncompressedSize_SumsFileEntries()
        {
            var zip = BuildZip(
                ("a.bin", new byte[1000]),
                ("dir/b.bin", new byte[2000]),
                ("dir/", Array.Empty<byte>()));

            using var ms = new MemoryStream(zip);
            Assert.AreEqual(3000, SafeZipExtractor.TotalUncompressedSize(ms));
        }

        // ── downloader ──

        [TestMethod]
        public void Downloader_VerifiesSizeAndHash_ThenRenamesPart()
        {
            byte[] payload = new byte[1024];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7);
            string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();

            var dl = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(payload)) };
            string dest = Path.Combine(_tempRoot, "asset.zip");

            string? ok = dl.DownloadAsync("https://x/a.zip", dest, payload.Length, sha, null, CancellationToken.None).Result;
            Assert.IsNull(ok);
            Assert.IsTrue(File.Exists(dest));
            Assert.IsFalse(File.Exists(dest + ".part"));
            CollectionAssert.AreEqual(payload, File.ReadAllBytes(dest));

            // Wrong size and wrong hash both fail, .part is cleaned.
            string? badSize = dl.DownloadAsync("https://x/a.zip", dest, payload.Length + 1, sha, null, CancellationToken.None).Result;
            StringAssert.Contains(badSize, "Download incomplete");
            string? badHash = dl.DownloadAsync("https://x/a.zip", dest, payload.Length, "deadbeef", null, CancellationToken.None).Result;
            StringAssert.Contains(badHash, "Checksum mismatch");
            Assert.IsFalse(File.Exists(dest + ".part"));
        }

        [TestMethod]
        public void Downloader_Cancellation_RemovesPart()
        {
            var dl = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(new byte[1000])) };
            string dest = Path.Combine(_tempRoot, "asset.zip");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool cancelled = false;
            try
            {
                dl.DownloadAsync("https://x/a.zip", dest, 1000, null, null, cts.Token).Wait();
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { cancelled = true; }

            Assert.IsTrue(cancelled);
            Assert.IsFalse(File.Exists(dest + ".part"), "the .part file must be removed on cancellation");
        }

        // ── setup service ──

        [TestMethod]
        public void Setup_InstallsCoreBuild_EndToEnd()
        {
            var zip = BuildZip(("shadPS4.exe", new byte[] { 1, 2, 3 }), ("qtplugins/x.dll", new byte[] { 4 }));
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "shadps4-win64-sdl-abc1234.zip", zip.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(zip)) },
                FreeSpaceOverride = _ => 10L * 1024 * 1024 * 1024,
            };

            var result = svc.InstallBuildAsync(release, store, ct: CancellationToken.None).Result;

            Assert.AreEqual(Shadps4SetupStatus.Success, result.Status);
            Assert.IsTrue(File.Exists(Path.Combine(store.BuildDirectory(Shadps4Component.Core, "abc1234"), "shadPS4.exe")));
            Assert.IsTrue(File.Exists(Path.Combine(store.BuildDirectory(Shadps4Component.Core, "abc1234"), "qtplugins", "x.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(store.BuildDirectory(Shadps4Component.Core, "abc1234"), "ps4pkgtool-manifest.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRoot, "managed", ".work")), "work dir is cleaned up");
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRoot, "managed", "builds", ".staging-abc1234")), "staging is gone after commit");
        }

        [TestMethod]
        public void Setup_AlreadyInstalled_DoesNotReinstall()
        {
            var zip = BuildZip(("shadPS4.exe", new byte[] { 1 }));
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "a.zip", zip.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(zip)) },
                FreeSpaceOverride = _ => 10L * 1024 * 1024 * 1024,
            };

            var first = svc.InstallBuildAsync(release, store).Result;
            Assert.AreEqual(Shadps4SetupStatus.Success, first.Status);

            var second = svc.InstallBuildAsync(release, store).Result;
            Assert.AreEqual(Shadps4SetupStatus.AlreadyInstalled, second.Status, "the same build is never re-downloaded");
        }

        [TestMethod]
        public void Setup_TruncatedArchive_FailsVerification()
        {
            var zip = BuildZip(("shadPS4.exe", new byte[5000]));
            var truncated = zip.Take(zip.Length / 2).ToArray();
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "a.zip", truncated.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(truncated)) },
                FreeSpaceOverride = _ => 10L * 1024 * 1024 * 1024,
            };

            var result = svc.InstallBuildAsync(release, store).Result;

            Assert.AreEqual(Shadps4SetupStatus.VerificationFailed, result.Status);
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRoot, "managed", "builds", "core-abc1234")));
        }

        [TestMethod]
        public void Setup_NotEnoughSpace_BeforeDownload()
        {
            var zip = BuildZip(("shadPS4.exe", new byte[] { 1 }));
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "a.zip", zip.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(zip)) },
                FreeSpaceOverride = _ => 1L, // 1 byte free
            };

            var result = svc.InstallBuildAsync(release, store).Result;

            Assert.AreEqual(Shadps4SetupStatus.InsufficientSpace, result.Status);
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRoot, "managed", ".work")));
        }

        [TestMethod]
        public void Setup_NotEnoughSpace_ForExtraction()
        {
            var zip = BuildZip(("big.bin", new byte[20 * 1024 * 1024]), ("shadPS4.exe", new byte[] { 1 }));
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "a.zip", zip.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(zip)) },
                // Enough for the download, not enough for the 20 MB extraction + margin.
                FreeSpaceOverride = _ => release.SizeBytes + Shadps4SetupService.SpaceMarginBytes + 1,
            };

            var result = svc.InstallBuildAsync(release, store).Result;

            Assert.AreEqual(Shadps4SetupStatus.InsufficientSpace, result.Status,
                "the uncompressed-size estimate must be checked before extraction");
        }

        [TestMethod]
        public void Setup_Cancellation_CleansWorkAndStaging()
        {
            var zip = BuildZip(("shadPS4.exe", new byte[] { 1 }));
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "a.zip", zip.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(zip)) },
                FreeSpaceOverride = _ => 10L * 1024 * 1024 * 1024,
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = svc.InstallBuildAsync(release, store, ct: cts.Token).Result;

            Assert.AreEqual(Shadps4SetupStatus.Cancelled, result.Status);
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRoot, "managed", ".work")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_tempRoot, "managed", "builds", ".staging-abc1234")));
        }

        [TestMethod]
        public void Setup_MissingExpectedExe_ReportsQuarantineHint()
        {
            var zip = BuildZip(("onlydata.txt", new byte[] { 1 }));
            var release = MakeRelease(Shadps4FeedKind.CoreNightly, "tag-x", "abc1234", "a.zip", zip.Length);
            var store = new Shadps4ManagedBuilds(Path.Combine(_tempRoot, "managed"), new SystemClock());
            var svc = new Shadps4SetupService
            {
                Downloader = new Shadps4Downloader { StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(zip)) },
                FreeSpaceOverride = _ => 10L * 1024 * 1024 * 1024,
            };

            var result = svc.InstallBuildAsync(release, store).Result;

            Assert.AreEqual(Shadps4SetupStatus.VerificationFailed, result.Status);
            StringAssert.Contains(result.Message, "shadPS4.exe", "missing exe names the expected file");
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

        /// <summary>Commits a fake core build (NightlyJson, index selectable) to the store.</summary>
        private void CommitCoreAt(Shadps4ManagedBuilds store, int index = 0)
        {
            string staging = Path.Combine(_tempRoot, "stage_core_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, Shadps4EnvironmentResolver.CoreExeFileName), "fake core");
            store.Commit(Parse(NightlyJson, Shadps4FeedKind.CoreNightly)[index], staging);
        }

        /// <summary>Commits a fake QtLauncher build (LauncherJson) to the store.</summary>
        private void CommitLauncherAt(Shadps4ManagedBuilds store)
        {
            string staging = Path.Combine(_tempRoot, "stage_launcher_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, Shadps4EnvironmentResolver.QtLauncherFileName), "fake launcher");
            store.Commit(Parse(LauncherJson, Shadps4FeedKind.QtLauncher)[0], staging);
        }

        private static Shadps4ReleaseInfo MakeRelease(Shadps4FeedKind feed, string tag, string commit, string asset, long size, bool prerelease = true)
            => new(feed, "shadps4-emu/shadPS4", tag, new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc),
                commit, asset, "https://github.com/example/" + asset, size, null, prerelease);

        private static byte[] BuildZip(params (string Name, byte[] Content)[] files)
        {
            using var ms = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in files)
                {
                    var entry = archive.CreateEntry(name);
                    using var es = entry.Open();
                    es.Write(content, 0, content.Length);
                }
            }
            return ms.ToArray();
        }

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
