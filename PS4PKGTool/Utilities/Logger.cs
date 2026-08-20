using Microsoft.Extensions.Logging;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS4PKGTool.Utilities
{
    class Logger
    {
        public static string LogFilename { get; set; }
        public static Action<string> OnLog;

        /// <summary>
        /// Normalizes mixed line endings to CRLF: multi-line messages (e.g.
        /// the emulator log tail) arrive with LF-only breaks, exception text
        /// with CRLF. The log file and the Log tab must render every break.
        /// </summary>
        public static string NormalizeNewlines(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

        public static void FlushLog()
        {
            File.WriteAllText(Helper.PS4PKGToolLogFile, string.Empty);
        }

        private static object lockObject = new object(); // For thread safety

        public static void LogInformation(string msg)
        {
            Log(LogLevel.Information, msg);
        }

        public static void LogWarning(string msg)
        {
            Log(LogLevel.Warning, msg);
        }

        public static void LogError(string msg, Exception ex = null)
        {
            Log(LogLevel.Error, msg, ex);
        }

        private static void Log(LogLevel level, string msg, Exception ex = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(msg))
                {
                    DateTime now = DateTime.Now;
                    string shortLabel = level.ToString().Replace("Information", "INFO").Replace("Warning","WARN").Replace("Error","ERR");
                    string logMessage = $"{now:G} : [{shortLabel}] {msg}";
                    string displayMessage = $"{now:HH:mm:ss}  [{shortLabel}] {msg}";

                    if (ex != null)
                    {
                        logMessage += Environment.NewLine + ex.ToString();
                        displayMessage += " " + ex.Message;
                    }

                    lock (lockObject)
                    {
                        using (var sw = new StreamWriter(Helper.PS4PKGToolLogFile, true))
                        {
                            sw.WriteLine(NormalizeNewlines(logMessage));
                        }
                    }

                    OnLog?.Invoke(displayMessage);
                }
            }
            catch (Exception)
            {
                // Handle any exceptions during logging
            }
        }
    }
}
