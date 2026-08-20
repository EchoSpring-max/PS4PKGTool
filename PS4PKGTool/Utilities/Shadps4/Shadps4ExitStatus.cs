using System;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>
    /// Neutral classification of a shadPS4 process exit code. Categories are
    /// descriptive, not judgemental: an error status does not necessarily
    /// mean the emulator crashed (0xC0000139 is a loader failure), and exit
    /// code 0 does not prove the game booted (boot failures also exit 0).
    /// </summary>
    public enum Shadps4ExitCategory
    {
        /// <summary>Exit code 0. Not proof of a clean run - boot failures also exit 0.</summary>
        NormalExit,
        /// <summary>Deliberate termination (console Ctrl+C / close).</summary>
        UserTermination,
        /// <summary>STATUS_DLL_NOT_FOUND - a DLL dependency could not be loaded.</summary>
        LoaderError,
        /// <summary>STATUS_ENTRYPOINT_NOT_FOUND - a missing export; the classic mismatched-core signature.</summary>
        EntryPointError,
        /// <summary>STATUS_DLL_INIT_FAILED - a DLL could not initialize.</summary>
        InitializationError,
        /// <summary>STATUS_ACCESS_VIOLATION.</summary>
        AccessViolation,
        /// <summary>STATUS_STACK_OVERFLOW.</summary>
        StackOverflow,
        /// <summary>STATUS_BREAKPOINT.</summary>
        Breakpoint,
        /// <summary>STATUS_STACK_BUFFER_OVERRUN (fail fast).</summary>
        FailFast,
        /// <summary>Another NTSTATUS failure code (&gt;= 0xC0000000) not individually mapped.</summary>
        OtherErrorStatus,
        /// <summary>Any other code (including the success sub-range).</summary>
        Unknown,
    }

    /// <summary>Maps a process exit code to a category and its Windows STATUS_ name.</summary>
    public static class Shadps4ExitStatus
    {
        public static Shadps4ExitCategory Classify(uint code) => code switch
        {
            0x00000000 => Shadps4ExitCategory.NormalExit,
            0xC000013A => Shadps4ExitCategory.UserTermination,
            0xC0000135 => Shadps4ExitCategory.LoaderError,
            0xC0000139 => Shadps4ExitCategory.EntryPointError,
            0xC0000142 => Shadps4ExitCategory.InitializationError,
            0xC0000005 => Shadps4ExitCategory.AccessViolation,
            0xC00000FD => Shadps4ExitCategory.StackOverflow,
            0x80000003 => Shadps4ExitCategory.Breakpoint,
            0xC0000409 => Shadps4ExitCategory.FailFast,
            _ when code >= 0xC0000000 => Shadps4ExitCategory.OtherErrorStatus,
            _ => Shadps4ExitCategory.Unknown,
        };

        /// <summary>The Windows STATUS_ name for a code, or "" when unmapped.</summary>
        public static string StatusName(uint code) => code switch
        {
            0x00000000 => "STATUS_SUCCESS",
            0xC000013A => "STATUS_CONTROL_C_EXIT",
            0xC0000135 => "STATUS_DLL_NOT_FOUND",
            0xC0000139 => "STATUS_ENTRYPOINT_NOT_FOUND",
            0xC0000142 => "STATUS_DLL_INIT_FAILED",
            0xC0000005 => "STATUS_ACCESS_VIOLATION",
            0xC00000FD => "STATUS_STACK_OVERFLOW",
            0x80000003 => "STATUS_BREAKPOINT",
            0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN",
            _ => "",
        };

        /// <summary>
        /// UI policy: which categories warrant an error-status dialog.
        /// Normal exits, user terminations and unmapped codes are logged only.
        /// </summary>
        public static bool IsErrorStatus(Shadps4ExitCategory category)
            => category is Shadps4ExitCategory.LoaderError
                or Shadps4ExitCategory.EntryPointError
                or Shadps4ExitCategory.InitializationError
                or Shadps4ExitCategory.AccessViolation
                or Shadps4ExitCategory.StackOverflow
                or Shadps4ExitCategory.Breakpoint
                or Shadps4ExitCategory.FailFast
                or Shadps4ExitCategory.OtherErrorStatus;
    }
}
