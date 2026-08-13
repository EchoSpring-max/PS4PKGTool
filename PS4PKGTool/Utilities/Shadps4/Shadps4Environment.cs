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
    /// it is re-derived from the configured exe paths and the filesystem
    /// every time it is needed (shadPS4's own layout has changed before).
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
        public string? AddonDirectory { get; init; }

        public Shadps4DetectionConfidence DetectionConfidence { get; init; } = Shadps4DetectionConfidence.None;

        /// <summary>All candidate config files that were considered (for the Ambiguous display).</summary>
        public IReadOnlyList<string> CandidateConfigPaths { get; init; } = Array.Empty<string>();

        /// <summary>True when the environment is usable for launch/install operations.</summary>
        public bool IsUsable =>
            !string.IsNullOrEmpty(CoreExePath)
            && UserDirectoryMode is not (Shadps4UserDirectoryMode.Unknown or Shadps4UserDirectoryMode.Ambiguous)
            && System.IO.File.Exists(CoreExePath);
    }
}
