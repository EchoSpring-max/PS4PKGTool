using PS4PKGTool.Utilities.Settings;
using System;
using System.Diagnostics;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    /// <summary>Shared optional arguments for every orbis-pub-cmd extraction.</summary>
    internal static class OrbisCommandOptions
    {
        public static void AddConfiguredTempPath(ProcessStartInfo startInfo)
            => AddTempPath(startInfo, SettingsManager.appSettings_?.OrbisTempDirectory);

        public static void AddTempPath(ProcessStartInfo startInfo, string tempPath)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));
            if (string.IsNullOrWhiteSpace(tempPath))
                return;

            startInfo.ArgumentList.Add("--tmp_path");
            startInfo.ArgumentList.Add(tempPath.Trim());
        }
    }
}
