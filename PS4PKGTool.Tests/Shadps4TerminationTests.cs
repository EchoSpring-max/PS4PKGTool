using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Tests;

[TestClass]
public class Shadps4TerminationTests
{
    private string _tempRoot = null!;

    // Fixed clock reference point for all freshness tests.
    private static readonly DateTime T = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "p4t_termination_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        // The launcher glue test must not be blocked by a running emulator.
        Shadps4Launcher.CoreRunningCheck = () => false;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Shadps4Launcher.CoreRunningCheck = () => Process.GetProcessesByName("shadPS4").Length > 0;
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTime _now;
        public FixedClock(DateTime now) => _now = now;
        public DateTime UtcNow => _now;
    }

    private sealed class ImmediateDelay : IDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // ── exit-code classification ──

    [TestMethod]
    public void Classify_MapsKnownStatuses()
    {
        Assert.AreEqual(Shadps4ExitCategory.NormalExit, Shadps4ExitStatus.Classify(0x00000000));
        Assert.AreEqual(Shadps4ExitCategory.UserTermination, Shadps4ExitStatus.Classify(0xC000013A));
        Assert.AreEqual(Shadps4ExitCategory.LoaderError, Shadps4ExitStatus.Classify(0xC0000135));
        Assert.AreEqual(Shadps4ExitCategory.EntryPointError, Shadps4ExitStatus.Classify(0xC0000139));
        Assert.AreEqual(Shadps4ExitCategory.InitializationError, Shadps4ExitStatus.Classify(0xC0000142));
        Assert.AreEqual(Shadps4ExitCategory.AccessViolation, Shadps4ExitStatus.Classify(0xC0000005));
        Assert.AreEqual(Shadps4ExitCategory.StackOverflow, Shadps4ExitStatus.Classify(0xC00000FD));
        Assert.AreEqual(Shadps4ExitCategory.Breakpoint, Shadps4ExitStatus.Classify(0x80000003));
        Assert.AreEqual(Shadps4ExitCategory.FailFast, Shadps4ExitStatus.Classify(0xC0000409));
        Assert.AreEqual(Shadps4ExitCategory.OtherErrorStatus, Shadps4ExitStatus.Classify(0xC0001234));
        Assert.AreEqual(Shadps4ExitCategory.Unknown, Shadps4ExitStatus.Classify(5));
        Assert.AreEqual(Shadps4ExitCategory.Unknown, Shadps4ExitStatus.Classify(0x80000000));
    }

    [TestMethod]
    public void Classify_SignedExitCode_IsInterpretedAsUnsigned()
    {
        // Process.ExitCode is a signed int; the negative value must not
        // fall into the Unknown bucket and must format without sign extension.
        int signed = unchecked((int)0xC0000005); // -1073741819
        uint code = unchecked((uint)signed);
        Assert.AreEqual(Shadps4ExitCategory.AccessViolation, Shadps4ExitStatus.Classify(code));
        Assert.AreEqual("0xC0000005", $"0x{code:X8}");
        Assert.AreEqual("STATUS_ACCESS_VIOLATION", Shadps4ExitStatus.StatusName(code));
    }

    [TestMethod]
    public void IsErrorStatus_GatesTheDialogPolicy()
    {
        Assert.IsTrue(Shadps4ExitStatus.IsErrorStatus(Shadps4ExitCategory.AccessViolation));
        Assert.IsTrue(Shadps4ExitStatus.IsErrorStatus(Shadps4ExitCategory.EntryPointError));
        Assert.IsTrue(Shadps4ExitStatus.IsErrorStatus(Shadps4ExitCategory.OtherErrorStatus));
        Assert.IsFalse(Shadps4ExitStatus.IsErrorStatus(Shadps4ExitCategory.NormalExit));
        Assert.IsFalse(Shadps4ExitStatus.IsErrorStatus(Shadps4ExitCategory.UserTermination));
        Assert.IsFalse(Shadps4ExitStatus.IsErrorStatus(Shadps4ExitCategory.Unknown));
    }

    // ── log tail ──

    [TestMethod]
    public void LogTail_ReadsFromAFile()
    {
        string log = Path.Combine(_tempRoot, "shad_log.txt");
        File.WriteAllText(log, "line one\nline two\n");
        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.LogPathResolver = _ => log;

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T, T.AddSeconds(5), 0).GetAwaiter().GetResult();

        Assert.AreEqual("line one\nline two", report.LogTail);
    }

    [TestMethod]
    public void LogTail_CapsAt200Lines()
    {
        string log = Path.Combine(_tempRoot, "shad_log.txt");
        var lines = new string[500];
        for (int i = 0; i < 500; i++) lines[i] = $"line {i:D4}";
        File.WriteAllText(log, string.Join("\n", lines));
        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.LogPathResolver = _ => log;

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T, T.AddSeconds(5), 0).GetAwaiter().GetResult();

        Assert.IsNotNull(report.LogTail);
        string[] tail = report.LogTail!.Split('\n');
        Assert.AreEqual(200, tail.Length);
        Assert.AreEqual("line 0300", tail[0]);
        Assert.AreEqual("line 0499", tail[^1]);
    }

    [TestMethod]
    public void LogTail_LargeFile_StartsAtLineBoundary()
    {
        // The 64 KB window can begin mid-line; the tail must never start
        // with a partial line (a real shadPS4 log is 100+ KB).
        string log = Path.Combine(_tempRoot, "shad_log.txt");
        var lines = new string[700]; // ~150 bytes each = ~105 KB, larger than the window
        for (int i = 0; i < 700; i++)
            lines[i] = $"[Core.Linker] <Warning> (Game:Main) linker.cpp:449 Resolve: Stub resolved Stub{i:D4} as sceKernelName{i:D4} (lib: libkernel, mod: libkernel)";
        File.WriteAllText(log, string.Join("\r\n", lines));
        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.LogPathResolver = _ => log;

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T, T.AddSeconds(5), 0).GetAwaiter().GetResult();

        Assert.IsNotNull(report.LogTail);
        string[] tail = report.LogTail!.Split('\n');
        Assert.AreEqual(200, tail.Length);
        // First line must be complete, not a fragment.
        Assert.IsTrue(tail[0].StartsWith("[Core.Linker]"), "tail must start at a line boundary, got: " + tail[0]);
        Assert.AreEqual(200, tail.Length);
    }

    [TestMethod]
    public void LogTail_MissingFile_IsNull()
    {
        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.LogPathResolver = _ => Path.Combine(_tempRoot, "does not exist.txt");

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T, T.AddSeconds(5), 0).GetAwaiter().GetResult();

        Assert.IsNull(report.LogTail);
    }

    [TestMethod]
    public void LogTail_LockedFile_ReturnsNullAfterRetries()
    {
        string log = Path.Combine(_tempRoot, "locked.txt");
        File.WriteAllText(log, "held");
        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.LogPathResolver = _ => log;

        using (new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
                T, T.AddSeconds(5), 0).GetAwaiter().GetResult();
            Assert.IsNull(report.LogTail);
        }
    }

    // ── WER evidence ──

    [TestMethod]
    public void WerDiscovery_FindsFreshReportWithDump()
    {
        string root = Path.Combine(_tempRoot, "wer");
        string dir = Path.Combine(root, "AppCrash_shadPS4.exe_abc_123");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Report.wer"), "metadata");
        File.WriteAllText(Path.Combine(dir, "memory.hdmp"), "dump");
        Directory.SetLastWriteTimeUtc(dir, T.AddSeconds(-4));

        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.WerRootsResolver = () => new[] { root };

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T.AddSeconds(-30), T.AddSeconds(-5), unchecked((int)0xC0000005)).GetAwaiter().GetResult();

        Assert.IsNotNull(report.WerReportFolder);
        Assert.AreEqual(dir, report.WerReportFolder);
        Assert.IsNotNull(report.DumpFile);
        Assert.IsTrue(report.DumpFile!.EndsWith("memory.hdmp"));
    }

    [TestMethod]
    public void WerDiscovery_IgnoresStaleReport()
    {
        string root = Path.Combine(_tempRoot, "wer");
        string dir = Path.Combine(root, "AppCrash_shadPS4.exe_old_1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Report.wer"), "old");
        Directory.SetLastWriteTimeUtc(dir, T.AddMinutes(-10)); // long before the exit

        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.WerRootsResolver = () => new[] { root };

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T.AddSeconds(-30), T.AddSeconds(-5), 1).GetAwaiter().GetResult();

        Assert.IsNull(report.WerReportFolder);
    }

    [TestMethod]
    public void WerDiscovery_LocalDumps_OnlyWhenConfigured()
    {
        // No LocalDumps configuration - resolver returns null - nothing found.
        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.LocalDumpsFolderResolver = () => null;
        analyzer.WerRootsResolver = () => Array.Empty<string>();

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T.AddSeconds(-30), T.AddSeconds(-5), 1).GetAwaiter().GetResult();
        Assert.IsNull(report.WerReportFolder);

        // LocalDumps configured to a folder with a fresh shadPS4 dump - found.
        string dumps = Path.Combine(_tempRoot, "crashdumps");
        Directory.CreateDirectory(dumps);
        string dump = Path.Combine(dumps, "shadPS4.exe.1234.dmp");
        File.WriteAllText(dump, "dump");
        File.SetLastWriteTimeUtc(dump, T.AddSeconds(-3));

        analyzer.LocalDumpsFolderResolver = () => dumps;
        var found = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T.AddSeconds(-30), T.AddSeconds(-5), 1).GetAwaiter().GetResult();
        Assert.IsNotNull(found.WerReportFolder);
        Assert.AreEqual(dump, found.DumpFile);
    }

    [TestMethod]
    public void WerDiscovery_ReportWithoutDump_HasFolderOnly()
    {
        string root = Path.Combine(_tempRoot, "wer");
        string dir = Path.Combine(root, "AppCrash_shadPS4.exe_meta_9");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Report.wer"), "metadata only");
        Directory.SetLastWriteTimeUtc(dir, T.AddSeconds(-4));

        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.WerRootsResolver = () => new[] { root };

        var report = analyzer.AnalyzeAsync(null, "exe", Array.Empty<string>(), null, null,
            T.AddSeconds(-30), T.AddSeconds(-5), 1).GetAwaiter().GetResult();

        Assert.IsNotNull(report.WerReportFolder);
        Assert.IsNull(report.DumpFile);
    }

    // ── full report ──

    [TestMethod]
    public void Analyze_CombinesAllEvidence()
    {
        string werRoot = Path.Combine(_tempRoot, "wer");
        string reportDir = Path.Combine(werRoot, "AppCrash_shadPS4.exe_abc_123");
        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, "Report.wer"), "x");
        Directory.SetLastWriteTimeUtc(reportDir, T.AddSeconds(-4));
        string logPath = Path.Combine(_tempRoot, "shad_log.txt");
        File.WriteAllText(logPath, "gpu line\ncrash line\n");

        var analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay());
        analyzer.WerRootsResolver = () => new[] { werRoot };
        analyzer.LogPathResolver = _ => logPath;

        var report = analyzer.AnalyzeAsync("CUSA12345", @"C:\x\shadPS4.exe",
            new[] { "CUSA12345" }, "0.17.0", _tempRoot,
            T.AddSeconds(-30), T.AddSeconds(-5), unchecked((int)0xC0000139)).GetAwaiter().GetResult();

        Assert.AreEqual(Shadps4ExitCategory.EntryPointError, report.Category);
        Assert.AreEqual("STATUS_ENTRYPOINT_NOT_FOUND", report.StatusName);
        Assert.AreEqual("0xC0000139 (STATUS_ENTRYPOINT_NOT_FOUND)", report.ExitCodeText);
        Assert.AreEqual(TimeSpan.FromSeconds(25), report.Runtime);
        Assert.AreEqual("00:00:25.0", report.RuntimeText);
        Assert.AreEqual("CUSA12345", report.Target);
        Assert.AreEqual("0.17.0", report.CoreVersion);
        Assert.AreEqual("gpu line\ncrash line", report.LogTail);
        Assert.AreEqual(reportDir, report.WerReportFolder);
    }

    // ── dialog policy (closed vs crash matrix) ──

    [TestMethod]
    public void DialogPolicy_NormalExit_IsInfoClosed()
    {
        var report = new Shadps4TerminationReport("CUSA12345", "core", Array.Empty<string>(), null,
            T, TimeSpan.FromSeconds(12), 0, Shadps4ExitCategory.NormalExit, "STATUS_SUCCESS",
            null, null, null, null);

        var (type, message, offerWer) = Shadps4TerminationUi.BuildDialog(report);

        Assert.AreEqual(AppMessageType.Info, type);
        Assert.IsFalse(offerWer);
        Assert.IsTrue(message.Contains("shadPS4 closed after 00:00:12"));
        Assert.IsTrue(message.Contains("Target: CUSA12345"));
        Assert.IsFalse(message.Contains("Exit code:"));
        Assert.IsFalse(message.Contains("error status"));
    }

    [TestMethod]
    public void DialogPolicy_UserTermination_IsInfoClosed()
    {
        var report = new Shadps4TerminationReport(null, "core", Array.Empty<string>(), null,
            T, TimeSpan.FromSeconds(5), 0xC000013A, Shadps4ExitCategory.UserTermination,
            "STATUS_CONTROL_C_EXIT", null, null, null, null);

        var (type, message, _) = Shadps4TerminationUi.BuildDialog(report);

        Assert.AreEqual(AppMessageType.Info, type);
        Assert.IsTrue(message.Contains("shadPS4 closed after 00:00:05"));
    }

    [TestMethod]
    public void DialogPolicy_UnknownCode_ShowsExitCode()
    {
        var report = new Shadps4TerminationReport(null, "core", Array.Empty<string>(), null,
            T, TimeSpan.FromSeconds(30), 1, Shadps4ExitCategory.Unknown, null, null, null, null, null);

        var (type, message, _) = Shadps4TerminationUi.BuildDialog(report);

        Assert.AreEqual(AppMessageType.Info, type);
        Assert.IsTrue(message.Contains("Exit code: 0x00000001"));
    }

    [TestMethod]
    public void DialogPolicy_Crash_IsWarningWithStatus()
    {
        var report = new Shadps4TerminationReport("CUSA12345", @"C:\x\shadPS4.exe",
            new[] { "CUSA12345" }, "0.17.0", T, TimeSpan.FromSeconds(25),
            unchecked((uint)0xC0000005), Shadps4ExitCategory.AccessViolation,
            "STATUS_ACCESS_VIOLATION", "gpu line\ncrash line", null, null, null);

        var (type, message, offerWer) = Shadps4TerminationUi.BuildDialog(report);

        Assert.AreEqual(AppMessageType.Warning, type);
        Assert.IsFalse(offerWer);
        Assert.IsTrue(message.Contains("terminated with error status 0xC0000005 (STATUS_ACCESS_VIOLATION)"));
        Assert.IsTrue(message.Contains("Last emulator log lines"));
        Assert.IsTrue(message.Contains("crash line"));
    }

    [TestMethod]
    public void DialogPolicy_CrashWithWerReport_OffersFolder()
    {
        var report = new Shadps4TerminationReport(null, "core", Array.Empty<string>(), null,
            T, TimeSpan.FromSeconds(4), unchecked((uint)0xC0000139),
            Shadps4ExitCategory.EntryPointError, "STATUS_ENTRYPOINT_NOT_FOUND",
            null, @"C:\wer\AppCrash_shadPS4.exe_abc", @"C:\wer\AppCrash_shadPS4.exe_abc\memory.hdmp", null);

        var (type, message, offerWer) = Shadps4TerminationUi.BuildDialog(report);

        Assert.AreEqual(AppMessageType.Warning, type);
        Assert.IsTrue(offerWer);
        Assert.IsTrue(message.Contains("Open the WER report folder?"));
        Assert.IsTrue(message.Contains("memory.hdmp"));
    }

    // ── launcher glue (real short-lived process via the seam) ──

    [TestMethod]
    public async Task Launcher_WatchesExitAndRaisesTerminated()
    {
        string lib = Path.Combine(_tempRoot, "lib");
        string game = Path.Combine(lib, "CUSA12345");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "eboot.bin"), "fake eboot");
        string fakeExe = Path.Combine(_tempRoot, "shadPS4.exe");
        File.WriteAllText(fakeExe, "fake exe");

        var launcher = new Shadps4Launcher
        {
            ProcessStartOverride = _ => StartCmdExit(7),
            Analyzer = new Shadps4TerminationAnalyzer(new FixedClock(T), new ImmediateDelay()),
        };
        var tcs = new TaskCompletionSource<Shadps4TerminationReport>();
        launcher.Terminated += (_, r) => tcs.TrySetResult(r);

        var env = new Shadps4Environment
        {
            CoreExePath = fakeExe,
            InstallDirectories = new[] { lib },
        };
        var (status, _) = launcher.LaunchInstalledTitle(env, "CUSA12345");
        Assert.AreEqual(Shadps4LaunchStatus.Started, status);

        Shadps4TerminationReport report = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(7u, report.ExitCode);
        Assert.AreEqual(Shadps4ExitCategory.Unknown, report.Category);
        Assert.AreEqual("CUSA12345", report.Target);
        Assert.AreEqual(fakeExe, report.Executable);
    }

    private static Process StartCmdExit(int code)
    {
        string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var psi = new ProcessStartInfo(cmd)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("exit");
        psi.ArgumentList.Add(code.ToString());
        return Process.Start(psi)!;
    }
}
