using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    /// <summary>
    /// UI policy for a shadPS4 termination report. Callers marshal to the UI
    /// thread first (the report arrives from the watcher thread).
    ///   - routine exits (exit 0 / user termination)  - NO dialog: the flow
    ///     goes straight to the Add Test Result form; the form itself shows
    ///     the session context (runtime, core)
    ///   - error status (crash)  - Warning dialog with the STATUS_ code,
    ///     runtime, launch context and the last emulator log lines
    ///   - unmapped exit code  - Info dialog with the exit code (informative,
    ///     not routine)
    ///   - fresh WER report on a crash  - the dialog offers to open the report
    ///     folder; an actual dump artifact inside is mentioned as such
    ///   - no report, no dump  - nothing is invented or offered
    /// Exit 0 is never called a crash (boot failures also exit 0). Dialog
    /// text never contains hyphen characters.
    /// </summary>
    public static class Shadps4TerminationUi
    {
        private const int DialogLogTailLines = 12;

        public static void ShowIfNeeded(Shadps4TerminationReport report, GameFeedbackContext? feedback = null)
        {
            bool isRoutine = report.Category is Shadps4ExitCategory.NormalExit
                or Shadps4ExitCategory.UserTermination;
            if (!isRoutine)
            {
                var (type, message, offerWerFolder) = BuildDialog(report);
                if (!offerWerFolder)
                {
                    AppMessageBox.Show("shadPS4", message, type, AppMessageButtons.OK);
                }
                else
                {
                    var choice = AppMessageBox.Show("shadPS4", message, AppMessageType.Warning, AppMessageButtons.YesNo);
                    if (choice == DialogResult.Yes)
                    {
                        try { Process.Start("explorer.exe", report.WerReportFolder); } catch { /* best-effort: Explorer refused to open */ }
                    }
                }
            }

            // Post-session feedback: the caller supplies context (game title,
            // version, active builds) when the game is known; Skip saves nothing.
            if (feedback != null)
            {
                using (var form = new GameFeedbackForm(feedback))
                    form.ShowDialog();
            }
        }

        /// <summary>
        /// Pure policy: what to show for a given report. Testable without UI.
        /// </summary>
        public static (AppMessageType Type, string Message, bool OfferWerFolder) BuildDialog(Shadps4TerminationReport report)
        {
            if (Shadps4ExitStatus.IsErrorStatus(report.Category))
            {
                string msg = $"shadPS4 terminated with error status {report.ExitCodeText} after {report.RuntimeText}.\n\n";
                if (!string.IsNullOrEmpty(report.Target))
                    msg += $"Target: {report.Target}\n";
                msg += $"Core: {report.Executable}\n";
                if (!string.IsNullOrEmpty(report.CoreVersion))
                    msg += $"Core version: {report.CoreVersion}\n";

                string? logTail = TrimTailForDialog(report.LogTail);
                if (logTail != null)
                    msg += $"\nLast emulator log lines:\n{logTail}\n";

                if (string.IsNullOrWhiteSpace(report.WerReportFolder))
                    return (AppMessageType.Warning, msg, false);
                msg += "\nA WER report was found for this termination.";
                if (!string.IsNullOrEmpty(report.DumpFile))
                    msg += $"\nIt contains a crash dump ({Path.GetFileName(report.DumpFile)}).";
                msg += "\n\nOpen the WER report folder?";
                return (AppMessageType.Warning, msg, true);
            }

            // Closed normally (exit 0, user termination) or an unmapped code.
            string closed = $"shadPS4 closed after {report.RuntimeText}.\n";
            if (!string.IsNullOrEmpty(report.Target))
                closed += $"Target: {report.Target}\n";
            if (report.Category == Shadps4ExitCategory.Unknown)
                closed += $"Exit code: {report.ExitCodeText}\n";
            return (AppMessageType.Info, closed, false);
        }

        /// <summary>Dialog size is capped - show only the last lines of the log tail.</summary>
        private static string? TrimTailForDialog(string? logTail)
        {
            if (string.IsNullOrWhiteSpace(logTail)) return null;
            var lines = logTail.Split('\n');
            if (lines.Length <= DialogLogTailLines) return logTail;
            return string.Join("\n", lines, lines.Length - DialogLogTailLines, DialogLogTailLines)
                + "\n...";
        }
    }
}
