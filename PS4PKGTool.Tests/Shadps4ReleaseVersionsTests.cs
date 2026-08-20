using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.Shadps4;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PS4PKGTool.Tests
{
    [TestClass]
    public class Shadps4ReleaseVersionsTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_release_versions_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        [TestMethod]
        public void Collect_FromFeedCache_ReturnsReleaseTagsNewestFirst()
        {
            string cache = Path.Combine(_tempRoot, "shadps4_releases.json");
            File.WriteAllText(cache, BuildCacheJson(
                // The REAL upstream tag shape - "v.0.17.0" with a dot after
                // the v. Older releases (down to shadPS4's first, v.0.0.3)
                // are all present in the feed.
                ReleaseJson("v.0.17.0", "2026-08-01T00:00:00Z"),
                ReleaseJson("v.0.16.0", "2026-06-01T00:00:00Z"),
                ReleaseJson("v.0.0.3", "2024-01-01T00:00:00Z"),
                ReleaseJson("not-a-version", "2026-07-01T00:00:00Z")));

            // Hermetic: an explicit empty managed root so builds installed
            // on this machine (e.g. a newer v.0.18.0) never leak into the
            // assertion.
            var versions = Shadps4ReleaseVersions.Collect(managedRoot: _tempRoot, cachePath: cache);

            // Regression: the v-dot tags must not surface as ".0.17.0" -
            // that malformed shape used to fill the report combobox (and
            // made the "0.0.3" entry show up wrong).
            CollectionAssert.AreEqual(new[] { "0.17.0", "0.16.0", "0.0.3" }, versions,
                "official release tags only, newest first, v and v-dot stripped");
        }

        [TestMethod]
        public void Collect_FromInstalledBuilds_IncludesReleaseTaggedOnly()
        {
            // Release-tagged managed build.
            CommitBuild("core-v0.15.0", "v0.15.0");
            // Nightly managed build - its release tag is a date+sha id, never a version.
            CommitBuild("core-e81418b4aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Pre-release-shadPS4-2026-08-13-e81418b4aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            var versions = Shadps4ReleaseVersions.Collect(managedRoot: _tempRoot);

            CollectionAssert.AreEqual(new[] { "0.15.0" }, versions,
                "nightly builds never appear as release versions");
        }

        [TestMethod]
        public void Collect_EmptyStoreAndMissingCache_ReturnsEmpty()
        {
            var versions = Shadps4ReleaseVersions.Collect(_tempRoot, Path.Combine(_tempRoot, "missing.json"));

            Assert.AreEqual(0, versions.Count, "no data anywhere - nothing is invented");
        }

        [TestMethod]
        public void ShapeHelpers_TagAndVersionChecksNormalize()
        {
            Assert.IsTrue(Shadps4ReleaseVersions.IsReleaseTag("v0.17.0"));
            // The REAL upstream tag shape - "v.0.17.0" with a dot after the v.
            Assert.IsTrue(Shadps4ReleaseVersions.IsReleaseTag("v.0.17.0"));
            Assert.IsFalse(Shadps4ReleaseVersions.IsReleaseTag("Pre-release-shadPS4-2026-08-13-abc"));
            Assert.IsFalse(Shadps4ReleaseVersions.IsReleaseTag(null));
            Assert.IsTrue(Shadps4ReleaseVersions.IsReleaseVersion("0.17.0"));
            Assert.IsFalse(Shadps4ReleaseVersions.IsReleaseVersion("2026-08-14 (e81418b4)"));
            Assert.AreEqual("0.17.0", Shadps4ReleaseVersions.Normalize("v0.17.0"));
            // Regression: "v.0.17.0" must not normalize to ".0.17.0" - the
            // feed tags are v-dot shaped, and the leading dot used to leak
            // into the report combobox (and the oldest release showed up
            // as ".0.0.3").
            Assert.AreEqual("0.17.0", Shadps4ReleaseVersions.Normalize("v.0.17.0"));
            Assert.AreEqual("0.0.3", Shadps4ReleaseVersions.Normalize("v.0.0.3"));
            Assert.AreEqual("0.17.0", Shadps4ReleaseVersions.Normalize("0.17.0"));
        }

        // ── fixtures ─────────────────────────────────────────────────────

        private void CommitBuild(string buildId, string releaseTag)
        {
            var store = new Shadps4ManagedBuilds(_tempRoot);
            string dir = store.BuildDirectory(Shadps4Component.Core, buildId);
            Directory.CreateDirectory(dir);
            string manifest =
                "{\"Component\":\"core\",\"Release\":\"" + releaseTag + "\"," +
                "\"Commit\":\"\",\"InstalledAtUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"}";
            File.WriteAllText(Path.Combine(dir, Shadps4ManagedBuilds.ManifestFileName), manifest);
            File.WriteAllText(Path.Combine(dir, Shadps4EnvironmentResolver.CoreExeFileName), "");
        }

        private static string BuildCacheJson(params string[] releaseObjects)
            => "{\"fetchedUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"," +
               "\"CoreStable\":[" + string.Join(",", releaseObjects) + "]}";

        private static string ReleaseJson(string tag, string published)
            => "{\"tag_name\":\"" + tag + "\",\"prerelease\":false,\"published_at\":\"" + published + "\"," +
               "\"assets\":[{\"name\":\"shadps4-win64.zip\",\"browser_download_url\":\"https://example.com/x.zip\",\"size\":100}]}";
    }
}
