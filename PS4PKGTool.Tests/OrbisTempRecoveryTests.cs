using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.IO;
using System.Linq;

namespace PS4PKGTool.Tests
{
    [TestClass]
    public class OrbisTempRecoveryTests
    {
        private string _tempRoot = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_recovery_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        private static string MakeTempDir(string root, string suffix = "abc123")
        {
            string dir = Path.Combine(root, OrbisTempRecovery.TempDirPrefix + suffix);
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void Recover_RestoresRenamedPkgViaSidecar()
        {
            // Simulate a crash: pkg moved into temp dir with sidecar, never restored.
            string originalDir = Path.Combine(_tempRoot, "orig", "游戏", "sub");
            Directory.CreateDirectory(originalDir);
            string original = Path.Combine(originalDir, "game.pkg");
            File.WriteAllText(original, "data");
            string tempDir = MakeTempDir(_tempRoot);
            string tempPkg = Path.Combine(tempDir, "ps4pkgtool_orbis_123456.pkg");
            OrbisTempRecovery.MoveIntoOrbisTemp(original, tempDir, tempPkg);
            // crash - no restore.

            var result = OrbisTempRecovery.Recover(new[] { _tempRoot });

            Assert.AreEqual(1, result.Restored);
            Assert.IsTrue(File.Exists(original), "the PKG is back at its original path");
            Assert.IsFalse(Directory.Exists(tempDir), "the temp dir is gone");
            Assert.IsFalse(File.Exists(tempPkg));
        }

        [TestMethod]
        public void Recover_RemovesEmptyLeftoverTempDirs()
        {
            string dir = MakeTempDir(_tempRoot, "empty1");
            File.WriteAllText(Path.Combine(dir, "original_path.txt"), "C:\\gone.pkg"); // failed move left only the sidecar

            var result = OrbisTempRecovery.Recover(new[] { _tempRoot });

            Assert.AreEqual(1, result.EmptyDirsRemoved);
            Assert.AreEqual(0, result.Restored);
            Assert.IsFalse(Directory.Exists(dir));
        }

        [TestMethod]
        public void Recover_LeavesUnresolvable_WhenNoSidecar()
        {
            string dir = MakeTempDir(_tempRoot);
            File.WriteAllText(Path.Combine(dir, "ps4pkgtool_orbis_999.pkg"), "data");

            var result = OrbisTempRecovery.Recover(new[] { _tempRoot });

            Assert.AreEqual(0, result.Restored);
            Assert.AreEqual(1, result.Unresolvable.Count);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "ps4pkgtool_orbis_999.pkg")), "unresolvable dirs are never touched");
        }

        [TestMethod]
        public void Recover_LeavesWhenOriginalAlreadyExists()
        {
            string originalDir = Path.Combine(_tempRoot, "orig");
            Directory.CreateDirectory(originalDir);
            string original = Path.Combine(originalDir, "game.pkg");
            File.WriteAllText(original, "newer");
            string dir = MakeTempDir(_tempRoot);
            string tempPkg = Path.Combine(dir, "ps4pkgtool_orbis_123456.pkg");
            OrbisTempRecovery.MoveIntoOrbisTemp(original, dir, tempPkg);
            File.WriteAllText(original, "newer"); // original recreated while the temp pkg was orphaned

            var result = OrbisTempRecovery.Recover(new[] { _tempRoot });

            Assert.AreEqual(0, result.Restored, "an occupied original is never overwritten");
            Assert.AreEqual(1, result.Unresolvable.Count);
        }

        [TestMethod]
        public void Recover_IgnoresUnrelatedDirs()
        {
            Directory.CreateDirectory(Path.Combine(_tempRoot, "normal folder"));
            File.WriteAllText(Path.Combine(_tempRoot, "normal folder", "game.pkg"), "data");
            Directory.CreateDirectory(Path.Combine(_tempRoot, "p4t_v_notours"));
            File.WriteAllText(Path.Combine(_tempRoot, "p4t_v_notours", "notes.txt"), "keep");

            var result = OrbisTempRecovery.Recover(new[] { _tempRoot });

            Assert.AreEqual(0, result.Restored);
            Assert.AreEqual(0, result.EmptyDirsRemoved);
            Assert.AreEqual(0, result.Unresolvable.Count);
            Assert.IsTrue(File.Exists(Path.Combine(_tempRoot, "p4t_v_notours", "notes.txt")), "unrelated content under a p4t_v_ name is left alone");
        }

        [TestMethod]
        public void DeleteOrbisTempDirSafe_NeverDeletesDirHoldingAPkg()
        {
            string dir = MakeTempDir(_tempRoot);
            string tempPkg = Path.Combine(dir, "ps4pkgtool_orbis_123456.pkg");
            File.WriteAllText(tempPkg, "data");

            OrbisTempRecovery.DeleteOrbisTempDirSafe(tempPkg);

            Assert.IsTrue(Directory.Exists(dir), "a temp dir still holding a PKG is NEVER deleted (data-loss guard)");
            Assert.IsTrue(File.Exists(tempPkg));
        }

        [TestMethod]
        public void DeleteOrbisTempDirSafe_DeletesEmptyOrSidecarOnlyDirs()
        {
            string dir1 = MakeTempDir(_tempRoot, "a");
            string dir2 = MakeTempDir(_tempRoot, "b");
            File.WriteAllText(Path.Combine(dir2, OrbisTempRecovery.SidecarName), "C:\\x.pkg");

            OrbisTempRecovery.DeleteOrbisTempDirSafe(Path.Combine(dir1, "ps4pkgtool_orbis_1.pkg"));
            OrbisTempRecovery.DeleteOrbisTempDirSafe(Path.Combine(dir2, "ps4pkgtool_orbis_2.pkg"));

            Assert.IsFalse(Directory.Exists(dir1));
            Assert.IsFalse(Directory.Exists(dir2), "a dir with only the sidecar (failed move) is safe to remove");
        }
    }
}
