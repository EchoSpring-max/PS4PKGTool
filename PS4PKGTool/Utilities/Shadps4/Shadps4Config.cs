using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Tomlyn;
using Tomlyn.Model;

namespace PS4PKGTool.Utilities.Shadps4
{
    /// <summary>The subset of shadPS4's configuration that PS4PKGTool needs.</summary>
    public sealed class Shadps4Config
    {
        public IReadOnlyList<Shadps4InstallDir> InstallDirs { get; init; } = Array.Empty<Shadps4InstallDir>();
        public string? AddonInstallDir { get; init; }
        public string? HomeDir { get; init; }
    }

    /// <summary>One configured game library entry (shadPS4 general.install_dirs).</summary>
    public sealed record Shadps4InstallDir(string Path, bool Enabled);

    /// <summary>
    /// Reads the values PS4PKGTool needs from a shadPS4 config file.
    /// Isolated behind an interface so the parser can be swapped.
    /// </summary>
    public interface IShadps4ConfigReader
    {
        /// <summary>True when this reader handles the given file (by name).</summary>
        bool Handles(string filePath);

        /// <summary>Parses the config, or null when it is missing/unreadable/malformed.</summary>
        Shadps4Config? Read(string filePath);
    }

    /// <summary>
    /// Current shadPS4 config backend (verified against shadPS4 main,
    /// src/core/emulator_settings.h): config.json is a nested object with
    /// flat snake_case keys per group:
    ///   general.install_dirs    = [{ "path": "...", "enabled": true }, ...]
    ///   general.addon_install_dir = "..."   (path string)
    ///   general.home_dir        = "..."     (path string, defaults to &lt;user&gt;\home)
    /// </summary>
    public sealed class Shadps4JsonConfigReader : IShadps4ConfigReader
    {
        public bool Handles(string filePath)
            => Path.GetFileName(filePath).Equals("config.json", StringComparison.OrdinalIgnoreCase);

        public Shadps4Config? Read(string filePath)
        {
            try
            {
                var root = JObject.Parse(File.ReadAllText(filePath));
                var general = root["general"] as JObject;
                if (general == null) return new Shadps4Config();

                var dirs = new List<Shadps4InstallDir>();
                if (general["install_dirs"] is JArray dirArray)
                {
                    foreach (var item in dirArray)
                    {
                        if (item is not JObject o) continue;
                        string? path = o["path"]?.ToString();
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        bool enabled = o["enabled"]?.ToObject<bool>() ?? true;
                        dirs.Add(new Shadps4InstallDir(path, enabled));
                    }
                }

                string? addonDir = general["addon_install_dir"]?.ToString();
                if (string.IsNullOrWhiteSpace(addonDir)) addonDir = null;

                string? homeDir = general["home_dir"]?.ToString();
                if (string.IsNullOrWhiteSpace(homeDir)) homeDir = null;

                return new Shadps4Config { InstallDirs = dirs, AddonInstallDir = addonDir, HomeDir = homeDir };
            }
            catch
            {
                return null; // missing / unreadable / malformed
            }
        }
    }

    /// <summary>
    /// Legacy shadPS4 config.toml (verified against shadPS4 main,
    /// src/core/emulator_settings.cpp, TransferSettings): the current
    /// emulator migrates this to config.json on launch, but a config.toml
    /// can still be the only file present when the emulator has not been
    /// started since the backend change.
    ///   [GUI]
    ///   installDirs         = ["...", ...]
    ///   installDirsEnabled  = [true, ...]   (parallel, optional)
    ///   addonInstallDir     = "..."
    /// </summary>
    public sealed class Shadps4TomlConfigReader : IShadps4ConfigReader
    {
        public bool Handles(string filePath)
            => Path.GetFileName(filePath).Equals("config.toml", StringComparison.OrdinalIgnoreCase);

        public Shadps4Config? Read(string filePath)
        {
            try
            {
                var model = Toml.ToModel(File.ReadAllText(filePath));
                if (model is not TomlTable root) return null;
                if (root["GUI"] is not TomlTable gui) return new Shadps4Config();

                var dirs = new List<Shadps4InstallDir>();
                var enabled = new List<bool>();
                if (gui["installDirs"] is TomlArray dirArray)
                {
                    foreach (var item in dirArray)
                        if (item is string s && !string.IsNullOrWhiteSpace(s))
                            dirs.Add(new Shadps4InstallDir(s, true));
                }
                if (gui["installDirsEnabled"] is TomlArray enabledArray)
                {
                    foreach (var item in enabledArray)
                        enabled.Add(item is bool b && b);
                }
                for (int i = 0; i < dirs.Count; i++)
                {
                    bool isEnabled = i < enabled.Count ? enabled[i] : true;
                    if (dirs[i].Enabled != isEnabled)
                        dirs[i] = new Shadps4InstallDir(dirs[i].Path, isEnabled);
                }

                string? addonDir = gui["addonInstallDir"] as string;
                if (string.IsNullOrWhiteSpace(addonDir)) addonDir = null;

                return new Shadps4Config { InstallDirs = dirs, AddonInstallDir = addonDir, HomeDir = null };
            }
            catch
            {
                return null; // missing / unreadable / malformed
            }
        }
    }
}
