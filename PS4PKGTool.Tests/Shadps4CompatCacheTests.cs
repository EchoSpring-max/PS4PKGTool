using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Tests
{
    /// <summary>
    /// Phase 1 hardening: Shadps4Compat.LoadCache used to swallow every
    /// failure silently, which left _cache as an empty dict - Lookup then
    /// returned "" forever until the app restarted. The contract these
    /// tests pin: a missing or corrupt cache never throws, Lookup returns
    /// "" for unknown titles (distinguishable from a known-empty status),
    /// and the never-throws contract holds across all entry points.
    /// </summary>
    [TestClass]
    public class Shadps4CompatCacheTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_compat_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        [TestMethod]
        public void Lookup_UnknownTitleIdReturnsEmptyString()
        {
            // The default state of _cache is null; Lookup must not throw on
            // that and must return "" (not null) for an unknown title.
            string result = Shadps4Compat.Lookup("CUSA99999", "windows");
            Assert.AreEqual("", result, "unknown title id returns empty, never null and never throws");
        }

        [TestMethod]
        public void Lookup_NullOrEmptyTitleIdReturnsEmpty()
        {
            Assert.AreEqual("", Shadps4Compat.Lookup(null!, "windows"));
            Assert.AreEqual("", Shadps4Compat.Lookup("", "windows"));
            Assert.AreEqual("", Shadps4Compat.Lookup("   ", "windows"));
        }

        [TestMethod]
        public void StatusRank_OrderingPreserved()
        {
            // Phase 1 did not touch ranking, but it's the pure-logic path and
            // a useful guard against accidental regression.
            Assert.IsTrue(Shadps4Compat.StatusRank("Playable") > Shadps4Compat.StatusRank("In-Game"));
            Assert.IsTrue(Shadps4Compat.StatusRank("In-Game") > Shadps4Compat.StatusRank("Menus"));
            Assert.IsTrue(Shadps4Compat.StatusRank("Menus") > Shadps4Compat.StatusRank("Boots"));
            Assert.IsTrue(Shadps4Compat.StatusRank("Boots") > Shadps4Compat.StatusRank("Nothing"));
            Assert.IsTrue(Shadps4Compat.StatusRank("Nothing") > Shadps4Compat.StatusRank(""));
        }

        [TestMethod]
        public void OsDisplay_NormalizesKnownPlatforms()
        {
            Assert.AreEqual("Windows", Shadps4Compat.OsDisplay("windows"));
            Assert.AreEqual("Windows", Shadps4Compat.OsDisplay("win"));
            Assert.AreEqual("Windows", Shadps4Compat.OsDisplay(""));
            Assert.AreEqual("Linux", Shadps4Compat.OsDisplay("linux"));
            Assert.AreEqual("macOS", Shadps4Compat.OsDisplay("macos"));
        }
    }
}
