using System;
using System.Collections.Generic;

namespace PS4PKGTool.Utilities.Shadps4
{
    public enum Shadps4UserDirectoryMode
    {
        /// <summary>No shadPS4 user directory found.</summary>
        Unknown,
        /// <summary>Portable layout: user\ next to the emulator executable.</summary>
        Portable,
        /// <summary>Roaming AppData layout: %APPDATA%\shadPS4.</summary>
        AppData,
        /// <summary>A user folder exists but its config is missing or unreadable - layout defaults only.</summary>
        Custom,
        /// <summary>Multiple plausible user directories exist and none was selected silently.</summary>
        Ambiguous,
    }

    public enum Shadps4DetectionConfidence
    {
        None,
        Low,
        High,
    }

    /// <summary>
    /// Resolved view of a shadPS4 installation. Nothing here is persisted -
    /// it is re-derived from the configured exe path and the filesystem every
    /// time it is needed (shadPS4's own layout has changed before, so cached
    /// stale paths are deliberately avoided). PS4PKGTool reads shadPS4's
    /// config; it never rewrites it.
    /// </summary>
    public sealed class Shadps4Environment
    {
        public string? CoreExePath { get; init; }
        public string? LauncherExePath { get; init; }

        /// <summary>The user directory that was selected as authoritative, or null.</summary>
        public string? UserDirectory { get; init; }

        /// <summary>The config file that produced the values (config.json or config.toml), or null.</summary>
        public string? ConfigPath { get; init; }

        public Shadps4UserDirectoryMode UserDirectoryMode { get; init; } = Shadps4UserDirectoryMode.Unknown;

        /// <summary>Enabled game libraries from the config (possibly empty).</summary>
        public IReadOnlyList<string> InstallDirectories { get; init; } = Array.Empty<string>();

        /// <summary>Save-data root (general.home_dir, default &lt;user&gt;\home).</summary>
        public string? SaveDataDirectory { get; init; }

        /// <summary>Add-on/DLC directory (general.addon_install_dir, default &lt;user&gt;\addcont).</summary>
        public string? AddonInstallDirectory { get; init; }

        public Shadps4DetectionConfidence DetectionConfidence { get; init; } = Shadps4DetectionConfidence.None;

        /// <summary>All candidate config files that were considered (for the Ambiguous display).</summary>
        public IReadOnlyList<string> CandidateConfigPaths { get; init; } = Array.Empty<string>();

        /// <summary>Human-readable detection notes (stale folders, missing dirs, malformed configs...).</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        /// <summary>True when a known configuration was resolved (mode is Portable/AppData/Custom).</summary>
        public bool IsValid =>
            UserDirectoryMode is Shadps4UserDirectoryMode.Portable
                or Shadps4UserDirectoryMode.AppData
                or Shadps4UserDirectoryMode.Custom;

        /// <summary>True when the environment is usable for launch/install operations.</summary>
        public bool IsUsable =>
            IsValid
            && !string.IsNullOrEmpty(CoreExePath)
            && System.IO.File.Exists(CoreExePath);
    }
}
