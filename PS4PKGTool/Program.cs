using DarkUI.Config;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static PS4PKGTool.Utilities.PS4PKGToolHelper.Helper;

namespace PS4PKGTool
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            EnsureSettingsFileExists();

            appSettings_ = LoadSettings(SettingFilePath);

            // Apply saved theme before showing any form (avoids flash of default)
            int themeIdx = appSettings_.ThemeIndex;
            if (themeIdx >= 0 && themeIdx < ThemeManager.Presets.Count)
                ThemeManager.Apply(ThemeManager.Presets[themeIdx]);

            ChooseStartupForm();
        }

        private static void EnsureSettingsFileExists()
        {
            if (!Directory.Exists(Helper.AppDataDirectory))
            {
                Directory.CreateDirectory(Helper.AppDataDirectory);
                Logger.LogInformation("Creating AppData directory...");
            }

            if (!File.Exists(SettingFilePath) || new FileInfo(SettingFilePath).Length == 0)
            {
                CreateDefaultSettings();
            }
        }

        private static void CreateDefaultSettings()
        {
            string defaultSettings =
    @"pkg_directories=
scan_recursive=False
play_bgm=False
auto_sort_row=False
local_server_ip=
ps4_ip=
nodeJs_installed=
httpServer_installed=
official_update_download_directory=
pkg_color_label=False
game_pkg_forecolor=-2302756
patch_pkg_forecolor=-2302756
addon_pkg_forecolor=-2302756
app_pkg_forecolor=-2302756
game_pkg_backcolor=-12828863
patch_pkg_backcolor=-12828863
addon_pkg_backcolor=-12828863
app_pkg_backcolor=-12828863
rename_custom_format=
ps5bc_json_download_date=
psvr_neo_ps5bc_check=
pkg_titleId_column=True
pkg_contentId_column=True
pkg_region_column=True
pkg_minimum_firmware_column=True
pkg_version_column=True
pkg_type_column=True
pkg_category_column=True
pkg_size_column=True
pkg_location_column=True
pkg_backport_column=True
auto_fetch_update=False
theme_index=0
shadps4_check=False";
            File.WriteAllText(SettingFilePath, defaultSettings);
        }

        private static void ChooseStartupForm()
        {
            // Determine what's available
            bool manifestAvailable = false;
            int manifestEntryCount = 0;

            if (ManifestHelper.ManifestExists()
                && appSettings_.PkgDirectories.Count > 0
                && appSettings_.PkgDirectories.Any(d => !string.IsNullOrEmpty(d)))
            {
                var manifest = ManifestHelper.LoadManifest();
                if (manifest != null)
                {
                    var (isValid, reason) = ManifestHelper.ValidateManifest(manifest, appSettings_);
                    if (isValid)
                    {
                        manifestAvailable = true;
                        manifestEntryCount = manifest.Entries?.Count ?? 0;
                    }
                    else
                    {
                        Logger.LogInformation($"Manifest invalid: {reason}.");
                    }
                }
            }

            bool directoriesAvailable = appSettings_.PkgDirectories.Count > 0
                && appSettings_.PkgDirectories.Any(d => !string.IsNullOrEmpty(d));
            int directoryCount = appSettings_.PkgDirectories.Count(d => !string.IsNullOrEmpty(d));

            // Show 3-option startup dialog
            using (var prompt = new ManifestLoaderPrompt(
                manifestAvailable, manifestEntryCount,
                directoriesAvailable, directoryCount))
            {
                if (prompt.ShowDialog() != DialogResult.OK)
                    return; // User clicked X — exit

                switch (prompt.Choice)
                {
                    case StartupChoice.Manifest:
                        Helper.LoadFromManifest = true;
                        break;
                    case StartupChoice.Directory:
                        Helper.LoadFromManifest = false;
                        break;
                    case StartupChoice.Empty:
                        Helper.LoadFromManifest = false;
                        Helper.LaunchEmpty = true;
                        break;
                }
            }

            Application.Run(new Main());
        }
    }
}
