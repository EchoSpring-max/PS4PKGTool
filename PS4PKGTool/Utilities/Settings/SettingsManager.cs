using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PS4PKGTool.Utilities.Shadps4;

namespace PS4PKGTool.Utilities.Settings
{
    public static class SettingsManager
    {
        public static AppSettings appSettings_ = new AppSettings();
        public static string SettingFilePath = Path.Combine(PS4PKGToolHelper.Helper.AppDataDirectory, @"Settings.conf");
        public static void SaveSettings(AppSettings settings, string filePath)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    string directories = string.Join(",", settings.PkgDirectories);
                    writer.WriteLine($"pkg_directories={directories}");
                    writer.WriteLine($"scan_recursive={settings.ScanRecursive}");
                    writer.WriteLine($"play_bgm={settings.PlayBgm}");
                    writer.WriteLine($"auto_sort_row={settings.AutoSortRow}");
                    writer.WriteLine($"local_server_ip={settings.LocalServerIp}");
                    writer.WriteLine($"ps4_ip={settings.Ps4Ip}");
                    writer.WriteLine($"nodeJs_installed={settings.NodeJsInstalled}");
                    writer.WriteLine($"httpServer_installed={settings.HttpServerInstalled}");
                    writer.WriteLine($"official_update_download_directory={settings.OfficialUpdateDownloadDirectory}");
                    writer.WriteLine($"pkg_color_label={settings.PkgColorLabel}");
                    writer.WriteLine($"game_pkg_forecolor={settings.GamePkgForeColor.ToArgb()}");
                    writer.WriteLine($"patch_pkg_forecolor={settings.PatchPkgForeColor.ToArgb()}");
                    writer.WriteLine($"addon_pkg_forecolor={settings.AddonPkgForeColor.ToArgb()}");
                    writer.WriteLine($"app_pkg_forecolor={settings.AppPkgForeColor.ToArgb()}");
                    writer.WriteLine($"game_pkg_backcolor={settings.GamePkgBackColor.ToArgb()}");
                    writer.WriteLine($"patch_pkg_backcolor={settings.PatchPkgBackColor.ToArgb()}");
                    writer.WriteLine($"addon_pkg_backcolor={settings.AddonPkgBackColor.ToArgb()}");
                    writer.WriteLine($"app_pkg_backcolor={settings.AppPkgBackColor.ToArgb()}");
                    writer.WriteLine($"rename_custom_format={settings.RenameCustomName}");
                    string formattedDate = settings.Ps5BcJsonLastDownloadDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
                    writer.WriteLine($"ps5bc_json_download_date={formattedDate}");
                    writer.WriteLine($"psvr_neo_ps5bc_check={settings.psvr_neo_ps5bc_check}");

                    writer.WriteLine($"pkg_titleId_column={settings.pkgtitleIdColumn}");
                    writer.WriteLine($"pkg_contentId_column={settings.pkgcontentIdColumn}");
                    writer.WriteLine($"pkg_region_column={settings.pkgregionColumn}");
                    writer.WriteLine($"pkg_minimum_firmware_column={settings.pkgminimumFirmwareColumn}");
                    writer.WriteLine($"pkg_version_column={settings.pkgversionColumn}");
                    writer.WriteLine($"pkg_type_column={settings.pkgTypeColumn}");
                    writer.WriteLine($"pkg_category_column={settings.pkgcategoryColumn}");
                    writer.WriteLine($"pkg_size_column={settings.pkgsizeColumn}");
                    writer.WriteLine($"pkg_location_column={settings.pkgDirectoryColumn}");
                    writer.WriteLine($"pkg_backport_column={settings.pkgBackportColumn}");
                    writer.WriteLine($"auto_fetch_update={settings.AutoFetchUpdate}");
                    writer.WriteLine($"theme_index={settings.ThemeIndex}");
                    writer.WriteLine($"shadps4_check={settings.Shadps4Check}");
                    writer.WriteLine($"shadps4_os={settings.Shadps4Os}");
                    writer.WriteLine($"shadps4_executable={settings.Shadps4ExecutablePath}");
                    writer.WriteLine($"shadps4_active_core={settings.Shadps4ActiveCore}");
                    writer.WriteLine($"shadps4_active_launcher={settings.Shadps4ActiveLauncher}");
                    writer.WriteLine($"shadps4_install_directory={settings.Shadps4InstallDirectory}");

                }
            }
            catch (Exception ex)
            {
                ShowError("Error saving settings: " + ex.Message, true);
            }
        }

        public static AppSettings LoadSettings(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    using (StreamReader reader = new StreamReader(filePath))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("pkg_directories="))
                            {
                                string directories = line.Substring("pkg_directories=".Length);
                                appSettings_.PkgDirectories.Clear();
                                appSettings_.PkgDirectories.AddRange(directories.Split(',').Where(d => !string.IsNullOrEmpty(d)));
                            }
                            else if (line.StartsWith("scan_recursive="))
                            {
                                bool.TryParse(line.Substring("scan_recursive=".Length), out bool scan_recursive);
                                appSettings_.ScanRecursive = scan_recursive;
                            }
                            else if (line.StartsWith("play_bgm="))
                            {
                                bool.TryParse(line.Substring("play_bgm=".Length), out bool PlayBgm);
                                appSettings_.PlayBgm = PlayBgm;
                            }
                            else if (line.StartsWith("auto_sort_row"))
                            {
                                bool.TryParse(line.Substring("auto_sort_row=".Length), out bool auto_sort_row);
                                appSettings_.AutoSortRow = auto_sort_row;
                            }
                            else if (line.StartsWith("local_server_ip="))
                            {
                                appSettings_.LocalServerIp = line.Substring("local_server_ip=".Length);
                            }
                            else if (line.StartsWith("ps4_ip="))
                            {
                                appSettings_.Ps4Ip = line.Substring("ps4_ip=".Length);
                            }
                            else if (line.StartsWith("nodeJs_installed="))
                            {
                                bool.TryParse(line.Substring("nodeJs_installed=".Length), out bool nodeJs_installed);
                                appSettings_.NodeJsInstalled = nodeJs_installed;
                            }
                            else if (line.StartsWith("httpServer_installed="))
                            {
                                bool.TryParse(line.Substring("httpServer_installed=".Length), out bool httpServer_installed);
                                appSettings_.HttpServerInstalled = httpServer_installed;
                            }
                            else if (line.StartsWith("official_update_download_directory="))
                            {
                                appSettings_.OfficialUpdateDownloadDirectory = line.Substring("official_update_download_directory=".Length);
                            }
                            else if (line.StartsWith("pkg_color_label="))
                            {
                                bool.TryParse(line.Substring("pkg_color_label=".Length), out bool pkg_color_label);
                                appSettings_.PkgColorLabel = pkg_color_label;
                            }
                            else if (line.StartsWith("game_pkg_forecolor="))
                            {
                                if (int.TryParse(line.Substring("game_pkg_forecolor=".Length), out int v))
                                    appSettings_.GamePkgForeColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("patch_pkg_forecolor="))
                            {
                                if (int.TryParse(line.Substring("patch_pkg_forecolor=".Length), out int v))
                                    appSettings_.PatchPkgForeColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("addon_pkg_forecolor="))
                            {
                                if (int.TryParse(line.Substring("addon_pkg_forecolor=".Length), out int v))
                                    appSettings_.AddonPkgForeColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("app_pkg_forecolor="))
                            {
                                if (int.TryParse(line.Substring("app_pkg_forecolor=".Length), out int v))
                                    appSettings_.AppPkgForeColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("game_pkg_backcolor="))
                            {
                                if (int.TryParse(line.Substring("game_pkg_backcolor=".Length), out int v))
                                    appSettings_.GamePkgBackColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("patch_pkg_backcolor="))
                            {
                                if (int.TryParse(line.Substring("patch_pkg_backcolor=".Length), out int v))
                                    appSettings_.PatchPkgBackColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("addon_pkg_backcolor="))
                            {
                                if (int.TryParse(line.Substring("addon_pkg_backcolor=".Length), out int v))
                                    appSettings_.AddonPkgBackColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("app_pkg_backcolor="))
                            {
                                if (int.TryParse(line.Substring("app_pkg_backcolor=".Length), out int v))
                                    appSettings_.AppPkgBackColor = Color.FromArgb(v);
                            }
                            else if (line.StartsWith("rename_custom_format="))
                            {
                                appSettings_.RenameCustomName = line.Substring("rename_custom_format=".Length);
                            }
                            else if (line.StartsWith("ps5bc_json_download_date="))
                            {
                                string[] formats = { "d MMMM yyyy" }; // Example: "3 June 2023", "03 June 2023"
                                string dateString = line.Substring("ps5bc_json_download_date=".Length);

                                if (!string.IsNullOrEmpty(dateString))
                                {
                                    DateTime convertedDate;
                                    if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out convertedDate))
                                    {
                                        appSettings_.Ps5BcJsonLastDownloadDate = convertedDate;
                                    }
                                }
                            }
                            else if (line.StartsWith("psvr_neo_ps5bc_check="))
                            {
                                bool.TryParse(line.Substring("psvr_neo_ps5bc_check=".Length), out bool psvr_neo_ps5bc_check);
                                appSettings_.psvr_neo_ps5bc_check = psvr_neo_ps5bc_check;
                            }
                            else if (line.StartsWith("pkg_titleId_column="))
                            {
                                bool.TryParse(line.Substring("pkg_titleId_column=".Length), out bool pkg_titleId_column);
                                appSettings_.pkgtitleIdColumn = pkg_titleId_column;
                            }
                            else if (line.StartsWith("pkg_contentId_column="))
                            {
                                bool.TryParse(line.Substring("pkg_contentId_column=".Length), out bool pkg_contentId_column);
                                appSettings_.pkgcontentIdColumn = pkg_contentId_column;
                            }
                            else if (line.StartsWith("pkg_region_column="))
                            {
                                bool.TryParse(line.Substring("pkg_region_column=".Length), out bool pkg_region_column);
                                appSettings_.pkgregionColumn = pkg_region_column;
                            }
                            else if (line.StartsWith("pkg_minimum_firmware_column="))
                            {
                                bool.TryParse(line.Substring("pkg_minimum_firmware_column=".Length), out bool pkg_minimum_firmware_column);
                                appSettings_.pkgminimumFirmwareColumn = pkg_minimum_firmware_column;
                            }
                            else if (line.StartsWith("pkg_version_column="))
                            {
                                bool.TryParse(line.Substring("pkg_version_column=".Length), out bool pkg_version_column);
                                appSettings_.pkgversionColumn = pkg_version_column;
                            }
                            else if (line.StartsWith("pkg_type_column="))
                            {
                                bool.TryParse(line.Substring("pkg_type_column=".Length), out bool pkg_type_column);
                                appSettings_.pkgTypeColumn = pkg_type_column;
                            }
                            else if (line.StartsWith("pkg_category_column="))
                            {
                                bool.TryParse(line.Substring("pkg_category_column=".Length), out bool pkg_category_column);
                                appSettings_.pkgcategoryColumn = pkg_category_column;
                            }
                            else if (line.StartsWith("pkg_size_column="))
                            {
                                bool.TryParse(line.Substring("pkg_size_column=".Length), out bool pkg_size_column);
                                appSettings_.pkgsizeColumn = pkg_size_column;
                            }
                            else if (line.StartsWith("pkg_location_column="))
                            {
                                bool.TryParse(line.Substring("pkg_location_column=".Length), out bool pkg_location_column);
                                appSettings_.pkgDirectoryColumn = pkg_location_column;
                            }
                            else if (line.StartsWith("pkg_backport_column="))
                            {
                                bool.TryParse(line.Substring("pkg_backport_column=".Length), out bool pkg_backport_column);
                                appSettings_.pkgBackportColumn = pkg_backport_column;
                            }
                            else if (line.StartsWith("auto_fetch_update="))
                            {
                                bool.TryParse(line.Substring("auto_fetch_update=".Length), out bool auto_fetch_update);
                                appSettings_.AutoFetchUpdate = auto_fetch_update;
                            }
                            else if (line.StartsWith("theme_index="))
                            {
                                int.TryParse(line.Substring("theme_index=".Length), out int theme_index);
                                appSettings_.ThemeIndex = theme_index;
                            }
                            else if (line.StartsWith("shadps4_check="))
                            {
                                bool.TryParse(line.Substring("shadps4_check=".Length), out bool shadps4_check);
                                appSettings_.Shadps4Check = shadps4_check;
                            }
                            else if (line.StartsWith("shadps4_os="))
                            {
                                string os = line.Substring("shadps4_os=".Length).Trim().ToLowerInvariant();
                                appSettings_.Shadps4Os = os is "linux" or "macos" ? os : "windows";
                            }
                            else if (line.StartsWith("shadps4_executable="))
                            {
                                appSettings_.Shadps4ExecutablePath = line.Substring("shadps4_executable=".Length).Trim();
                            }
                            else if (line.StartsWith("shadps4_active_core="))
                            {
                                appSettings_.Shadps4ActiveCore = line.Substring("shadps4_active_core=".Length).Trim();
                            }
                            else if (line.StartsWith("shadps4_active_launcher="))
                            {
                                appSettings_.Shadps4ActiveLauncher = line.Substring("shadps4_active_launcher=".Length).Trim();
                            }
                            else if (line.StartsWith("shadps4_install_directory="))
                            {
                                appSettings_.Shadps4InstallDirectory = line.Substring("shadps4_install_directory=".Length).Trim();
                            }
                            else if (line.StartsWith("shadps4_core_exe="))
                            {
                                appSettings_.Shadps4CoreExePath = line.Substring("shadps4_core_exe=".Length).Trim();
                            }
                            else if (line.StartsWith("shadps4_launcher_exe="))
                            {
                                appSettings_.Shadps4LauncherExePath = line.Substring("shadps4_launcher_exe=".Length).Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions appropriately
                ShowError("Error loading settings: " + ex.Message, true);
            }

            NormalizePkgDirectories(appSettings_.PkgDirectories);
            MigrateShadps4ActiveKeys(appSettings_);
            return appSettings_;
        }

        /// <summary>
        /// One-time migration to the explicit active-core/launcher model.
        /// Runs only when the new keys are empty, so it never overrides a
        /// value the user set, and the migrated value is persisted on the
        /// next settings save (legacy keys are then never consulted again).
        /// Rules: legacy core/launcher paths become adopted references; the
        /// legacy single executable becomes the adopted core or launcher
        /// depending on its filename.
        /// </summary>
        private static void MigrateShadps4ActiveKeys(AppSettings s)
        {
            if (string.IsNullOrWhiteSpace(s.Shadps4ActiveCore))
            {
                string core = s.Shadps4CoreExePath ?? "";
                if (string.IsNullOrWhiteSpace(core) && IsCoreFileName(s.Shadps4ExecutablePath))
                    core = s.Shadps4ExecutablePath;
                if (!string.IsNullOrWhiteSpace(core))
                    s.Shadps4ActiveCore = Shadps4ActiveCore.ForAdopted(core);
            }

            if (string.IsNullOrWhiteSpace(s.Shadps4ActiveLauncher))
            {
                string launcher = s.Shadps4LauncherExePath ?? "";
                if (string.IsNullOrWhiteSpace(launcher) && IsLauncherFileName(s.Shadps4ExecutablePath))
                    launcher = s.Shadps4ExecutablePath;
                if (!string.IsNullOrWhiteSpace(launcher))
                    s.Shadps4ActiveLauncher = Shadps4ActiveCore.ForAdopted(launcher);
            }
        }

        private static bool IsCoreFileName(string? path)
        {
            string name = Path.GetFileName(path ?? "");
            return name.Equals(Shadps4EnvironmentResolver.CoreExeFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLauncherFileName(string? path)
        {
            string name = Path.GetFileName(path ?? "");
            return name.Equals(Shadps4EnvironmentResolver.QtLauncherFileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Keeps the configured directory list clean. The move-PKG feature
        /// once added one entry per destination folder, leaving hundreds of
        /// redundant subfolders (226 entries from 2 real roots).
        /// Rules:
        ///  - empty and exact-duplicate entries are dropped;
        ///  - sibling groups (2+) sharing the same parent collapse to that
        ///    parent (&lt;lib&gt;\Base + Update\Title1, Title2, ... and even
        ///    &lt;lib&gt;\Game &amp; Patch + &lt;lib&gt;\New folder become &lt;lib&gt;);
        ///  - entries still nested inside a kept entry are dropped.
        /// Scanning a configured directory covers its children (recursive, or
        /// via the immediate-subfolder scan), so the result - top-level roots
        /// only - loses no coverage.
        /// </summary>
        private static void NormalizePkgDirectories(List<string> dirs)
        {
            var unique = new List<string>();
            foreach (string raw in dirs)
            {
                string d = raw?.Trim() ?? "";
                if (d.Length == 0) continue;
                if (!unique.Any(c => string.Equals(c, d, StringComparison.OrdinalIgnoreCase)))
                    unique.Add(d);
            }

            // Collapse sibling groups to their common parent; lone entries stay.
            var collapsed = new List<string>();
            foreach (var group in unique.GroupBy(ParentPath, StringComparer.OrdinalIgnoreCase))
            {
                string parent = group.Key ?? "";
                if (group.Count() >= 2 && !string.IsNullOrEmpty(parent))
                {
                    if (!collapsed.Any(c => string.Equals(c, parent, StringComparison.OrdinalIgnoreCase)))
                        collapsed.Add(parent);
                }
                else
                {
                    foreach (string d in group)
                        if (!collapsed.Any(c => string.Equals(c, d, StringComparison.OrdinalIgnoreCase)))
                            collapsed.Add(d);
                }
            }

            // Drop entries nested inside another entry (a collapsed parent may
            // itself nest under a kept root).
            var final = collapsed.Where(d => !collapsed.Any(other =>
                !string.Equals(other, d, StringComparison.OrdinalIgnoreCase)
                && IsStrictChildOf(d, other))).ToList();

            dirs.Clear();
            dirs.AddRange(final);
        }

        /// <summary>Parent directory of a path (no trailing separator), or null when none.</summary>
        private static string? ParentPath(string path)
        {
            string trimmed = path.TrimEnd('\\', '/');
            int idx = trimmed.LastIndexOf('\\');
            if (idx <= 0) return null;
            return trimmed.Substring(0, idx);
        }

        private static bool IsStrictChildOf(string child, string parent)
        {
            string p = parent.TrimEnd('\\', '/');
            return child.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || child.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
