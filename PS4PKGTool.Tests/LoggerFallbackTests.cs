using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PS4PKGTool.Tests
{
    /// <summary>
    /// Phase 1 hardening: the logger used to swallow its own write failures
    /// silently, and OnLog lived inside the same try as the file write - so a
    /// broken log path killed both the file log AND the Log tab. These tests
    /// pin the new contract: file-write failure falls back to %TEMP%, the
    /// Log tab still receives every line, and the fallback is announced once.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // Logger is process-static; these tests must not interleave
    public class LoggerFallbackTests
    {
        private string _tempRoot = null!;
        private string _savedLogFile = null!;
        private Action<string> _savedOnLog;
        private int _onLogCalls;
        private List<string> _onLogLines = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_logger_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            // Logger reads Helper.PS4PKGToolLogFile (static, rooted at
            // AppContext.BaseDirectory). Redirect it to a writable path under
            // our temp root so the test never pokes the real log file.
            _savedLogFile = Helper.PS4PKGToolLogFile;
            _savedOnLog = Logger.OnLog;

            Logger.Test_ResetFallback();
            _onLogCalls = 0;
            _onLogLines = new List<string>();
            // Capture OnLog on a single thread; no Invoke needed here.
            Logger.OnLog = line => { _onLogCalls++; _onLogLines.Add(line); };
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Restore the statics we touched so other test classes see the
            // original process-wide state.
            Logger.OnLog = _savedOnLog;
            Logger.Test_ResetFallback();
            Helper.PS4PKGToolLogFile = _savedLogFile;

            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        [TestMethod]
        public void Log_WritesToPrimaryLogFileWhenWritable()
        {
            string logFile = Path.Combine(_tempRoot, "PS4PKGToolLog.txt");
            Helper.PS4PKGToolLogFile = logFile;

            Logger.LogInformation("hello world");

            Assert.IsTrue(File.Exists(logFile), "log file created on the writable path");
            string content = File.ReadAllText(logFile);
            Assert.IsTrue(content.Contains("hello world"), "message written to the log file");
            Assert.IsFalse(content.Contains("fallback"), "no fallback marker on the happy path");
            Assert.IsNull(Logger.Test_FallbackLogPath, "fallback not engaged on the happy path");
            Assert.AreEqual(1, _onLogCalls, "OnLog invoked exactly once");
            Assert.IsTrue(_onLogLines[0].Contains("hello world"), "OnLog received the display message");
        }

        [TestMethod]
        public void Log_FallsBackToTempWhenPrimaryUnwritable()
        {
            // Point the log path at a path whose parent does not exist and
            // cannot be created (use an invalid-path char so CreateDirectory
            // would fail). A path under a missing parent forces the StreamWriter
            // to throw DirectoryNotFoundException, which is exactly what we want.
            string badPath = Path.Combine(_tempRoot, "missing|dir", "PS4PKGToolLog.txt");
            Helper.PS4PKGToolLogFile = badPath;

            Logger.LogWarning("first message after breakage");
            Logger.LogWarning("second message after breakage");

            string fallback = Logger.Test_FallbackLogPath;
            Assert.IsNotNull(fallback, "fallback path engaged after primary write failed");
            Assert.IsTrue(fallback.Contains("PS4PKGTool-fallback"), "fallback path is the documented %TEMP% file");

            // The fallback file must exist and contain both messages.
            Assert.IsTrue(File.Exists(fallback), "fallback log file actually written");
            string content = File.ReadAllText(fallback);
            Assert.IsTrue(content.Contains("first message after breakage"), "first warning in fallback");
            Assert.IsTrue(content.Contains("second message after breakage"), "second warning in fallback");
        }

        [TestMethod]
        public void Log_AnnouncesFallbackOnLogTabExactlyOnce()
        {
            string badPath = Path.Combine(_tempRoot, "missing|dir", "PS4PKGToolLog.txt");
            Helper.PS4PKGToolLogFile = badPath;

            // First call after breakage: should announce + the real line.
            Logger.LogInformation("call one");
            int callsAfterFirst = _onLogCalls;
            Assert.IsTrue(Logger.Test_FallbackAnnounced, "fallback announced after the first failed write");

            // The announcement line should be in the captured OnLog stream,
            // before the real display message.
            Assert.IsTrue(_onLogLines.Any(l => l.Contains("Log file unavailable") && l.Contains("fallback")),
                "announcement line delivered to OnLog");
            Assert.IsTrue(_onLogLines.Any(l => l.Contains("call one")),
                "real display message also delivered to OnLog");

            // Second call: no second announcement, just the real line.
            Logger.LogInformation("call two");
            int announceCount = _onLogLines.Count(l => l.Contains("Log file unavailable"));
            Assert.AreEqual(1, announceCount, "announcement fires exactly once, never again");
            // Exactly 3 OnLog invocations: announce + call-one + call-two.
            Assert.AreEqual(3, _onLogCalls, "no extra announcement on subsequent calls");
            Assert.IsTrue(_onLogLines.Any(l => l.Contains("call two")), "second display message delivered");
        }

        [TestMethod]
        public void Log_EmptyMessageNeverInvokesOnLog()
        {
            string logFile = Path.Combine(_tempRoot, "PS4PKGToolLog.txt");
            Helper.PS4PKGToolLogFile = logFile;

            Logger.LogInformation("");
            Logger.LogInformation(null!);

            Assert.AreEqual(0, _onLogCalls, "empty/null messages short-circuit before OnLog");
            // The log file may exist (an earlier test could have created it
            // in this temp root), but it must not contain an empty line from
            // these calls. We only assert OnLog was never invoked.
        }

        [TestMethod]
        public void Log_ExceptionMessageAppendedToDisplay()
        {
            string logFile = Path.Combine(_tempRoot, "PS4PKGToolLog.txt");
            Helper.PS4PKGToolLogFile = logFile;

            var ex = new InvalidOperationException("boom");
            Logger.LogError("outer context", ex);

            Assert.AreEqual(1, _onLogCalls);
            Assert.IsTrue(_onLogLines[0].Contains("outer context"), "context preserved");
            Assert.IsTrue(_onLogLines[0].Contains("boom"), "exception message appended to the display line");

            string content = File.ReadAllText(logFile);
            Assert.IsTrue(content.Contains("boom"), "exception message written to the file");
            Assert.IsTrue(content.Contains("InvalidOperationException"), "full exception type in the file log");
        }
    }
}
