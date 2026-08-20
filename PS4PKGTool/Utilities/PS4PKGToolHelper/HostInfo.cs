using System;
using System.Management;
using Microsoft.Win32;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    /// <summary>
    /// Host CPU and GPU names for the feedback form's Processor and Graphics
    /// Card fields - detected once (cached) so the user only edits when the
    /// defaults are wrong. Never throws; empty string when undetectable.
    /// </summary>
    public static class HostInfo
    {
        public static string CpuName { get; } = DetectCpu();

        public static string GpuName { get; } = DetectGpu();

        private static string DetectCpu()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string DetectGpu()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, CurrentBitsPerPixel FROM Win32_VideoController");
                string fallback = "";
                foreach (var obj in searcher.Get())
                {
                    string name = (obj["Name"] as string ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    // Software/remote adapters are never the real GPU.
                    if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // The active display adapter usually reports a color depth;
                    // prefer it so laptops report the discrete GPU when in use.
                    bool active = (obj["CurrentBitsPerPixel"] as int?) is > 0;
                    if (active) return name;
                    if (fallback.Length == 0) fallback = name;
                }
                return fallback;
            }
            catch
            {
                return "";
            }
        }
    }
}
