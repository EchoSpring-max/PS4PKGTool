using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities;

namespace PS4PKGTool.Tests;

/// <summary>
/// Suite-wide setup. The test process runs from its bin directory, which has
/// no AppData\ folder - so the primary log write fails and Logger engages the
/// %TEMP% fallback on the very first logged line. Without this redirect the
/// entire suite would dump its log output into the real %TEMP% on the
/// developer's machine. The override lives for the whole assembly run and is
/// cleaned up after (the directory is per-run, GUID-named).
/// </summary>
[TestClass]
public class AssemblyHooks
{
    public static string? FallbackDir { get; private set; }

    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        FallbackDir = Path.Combine(Path.GetTempPath(),
            "p4t_test_logs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FallbackDir);
        Logger.Test_FallbackDirectory = FallbackDir;
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        Logger.Test_FallbackDirectory = null;
        if (FallbackDir != null)
        {
            try { Directory.Delete(FallbackDir, recursive: true); } catch { }
        }
    }
}
