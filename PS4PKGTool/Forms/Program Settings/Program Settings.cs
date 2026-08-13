using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using PS4PKGTool.Utilities.Settings;
using PS4PKGTool.Utilities.Shadps4;
using Color = System.Drawing.Color;
using static PS4PKGTool.Utilities.PS4PKGToolHelper.Helper;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System.Globalization;
using System.Threading;
using PS4PKGTool.Utilities.TrophyMetadata;
using DarkUI.Config;
using DarkUI.Controls;

namespace PS4PKGTool
{
    public partial class ProgramSetting : DarkUI.Forms.DarkForm
    {
        const string TITLE = "Atomic Heart";
        const string TITLE_ID = "CUSA17266";
        const string VERSION = "1.00";
        const string APP_VERSION = "1.00";
        const string CATEGORY = "Game";
        const string CONTENT_ID = "EP4133-CUSA37321_00-ATOMICHEARTGAME0";
        const string CONTENT_ID2 = "EP4133-CUSA37321_00-ATOMICHEARTGAME0-A0100-V0116";
        const string REGION = "EU";
        const string SYSTEM_VERSION = "9.50";
        private string HttpServerModulePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\npm\node_modules\http-server";
        private CancellationTokenSource trophyCacheCancellation;
        private string TrophyCachePath => Path.Combine(AppDataDirectory, "TrophyMetadata", "np-communication-ids.json");

        public bool Refresh = false;
        public ProgramSetting()
        {
            InitializeComponent();
            this.Icon = AppIcon;
            FormClosing += ProgramSetting_FormClosing;
        }

        private void btnOfficialUpdateDownloadFolder_Click(object sender, EventArgs e)
        {
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                tbOfficialUpdateDownloadFolder.Text = fbd.SelectedPath;
                Logger.LogInformation($"Official update PKG directory set to \"{fbd.SelectedPath}\"");
            }
        }

        private void ProgramSetting_Load(object sender, EventArgs e)
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    darkComboBoxServerIP.Items.Add(ip.ToString());
                }
            }

            cmbTheme.Items.AddRange(ThemeManager.Presets.Select(p => p.Name).ToArray());
            cmbTheme.SelectedIndexChanged += (_, _) =>
            {
                ThemeManager.Apply(ThemeManager.Presets[cmbTheme.SelectedIndex]);
            };

            #region LoadSetting
            // directory settings
            darkCheckBoxRecursive.Checked = appSettings_.ScanRecursive;
            lbPkgDirectoryList.Items.AddRange(appSettings_.PkgDirectories?.Cast<string>().ToArray() ?? Array.Empty<string>());

            AutoSortRow.Checked = appSettings_.AutoSortRow;
            PKGColorLabeling.Checked = appSettings_.PkgColorLabel;
            darkLabelGamePkgColorLabel.ForeColor = (appSettings_.GamePkgForeColor == null) ? Color.FromArgb(220, 220, 220) : appSettings_.GamePkgForeColor;
            darkLabelPatchPkgColorLabel.ForeColor = (appSettings_.PatchPkgForeColor == null) ? Color.FromArgb(220, 220, 220) : appSettings_.PatchPkgForeColor;
            darkLabelAddonPkgColorLabel.ForeColor = (appSettings_.AddonPkgForeColor == null) ? Color.FromArgb(220, 220, 220) : appSettings_.AddonPkgForeColor;
            darkLabelAppPkgColorLabel.ForeColor = (appSettings_.AppPkgForeColor == null) ? Color.FromArgb(220, 220, 220) : appSettings_.AppPkgForeColor;
            darkLabelGamePkgColorLabel.BackColor = (appSettings_.GamePkgBackColor == null) ? Color.FromArgb(60, 63, 65) : appSettings_.GamePkgBackColor;
            darkLabelPatchPkgColorLabel.BackColor = (appSettings_.PatchPkgBackColor == null) ? Color.FromArgb(60, 63, 65) : appSettings_.PatchPkgBackColor;
            darkLabelAddonPkgColorLabel.BackColor = (appSettings_.AddonPkgBackColor == null) ? Color.FromArgb(60, 63, 65) : appSettings_.AddonPkgBackColor;
            darkLabelAppPkgColorLabel.BackColor = (appSettings_.AppPkgBackColor == null) ? Color.FromArgb(60, 63, 65) : appSettings_.AppPkgBackColor;
            tbCustomNamePattern.Text = appSettings_.RenameCustomName;

            tbOfficialUpdateDownloadFolder.Text = appSettings_.OfficialUpdateDownloadDirectory;
            tbPS4IP.Text = appSettings_.Ps4Ip;
            darkComboBoxServerIP.Text = appSettings_.LocalServerIp;
            labelPs5BcJsonDownloadDate.Text = (appSettings_.Ps5BcJsonLastDownloadDate == DateTime.MinValue || !File.Exists(Ps5BcJsonFile))
                ? ""
                : appSettings_.Ps5BcJsonLastDownloadDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

            cbPs5BcCheck.Checked = appSettings_.psvr_neo_ps5bc_check;
            cbShadps4Check.Checked = appSettings_.Shadps4Check;
            cmbShadps4Os.SelectedIndex = appSettings_.Shadps4Os switch
            {
                "linux" => 1,
                "macos" => 2,
                _ => 0, // windows (default)
            };
            tbShadps4ActiveCore.Text = DescribeActiveComponent(appSettings_.Shadps4ActiveCore);
            tbShadps4ActiveLauncher.Text = DescribeActiveComponent(appSettings_.Shadps4ActiveLauncher);
            tbShadps4InstallDirectory.Text = appSettings_.Shadps4InstallDirectory ?? "";
            RefreshShadps4Detection();
            labelShadps4JsonDate.Text = Shadps4Compat.LastDownload?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? "Not downloaded";
            Location.Checked = appSettings_.pkgDirectoryColumn;
            Size.Checked = appSettings_.pkgsizeColumn;
            Category.Checked = appSettings_.pkgcategoryColumn;
            PkgType.Checked = appSettings_.pkgTypeColumn;
            SystemFirmware.Checked = appSettings_.pkgminimumFirmwareColumn;
            Version.Checked = appSettings_.pkgversionColumn;
            Region.Checked = appSettings_.pkgregionColumn;
            ContentId.Checked = appSettings_.pkgcontentIdColumn;
            TitleId.Checked = appSettings_.pkgtitleIdColumn;
            cbBackported.Checked = appSettings_.pkgBackportColumn;
            cbAutoFetchUpdate.Checked = appSettings_.AutoFetchUpdate;
            BGM.Checked = appSettings_.PlayBgm;
            cmbTheme.SelectedIndex = appSettings_.ThemeIndex >= 0 && appSettings_.ThemeIndex < ThemeManager.Presets.Count
                ? appSettings_.ThemeIndex : 0;
            #endregion LoadSetting

            #region nodejs&serve
            if (Tool.IsAppInstalled("Node.js") == true)
            {
                darkLabelNodejsInstalled.Text = "✔";
                darkLabelNodejsInstalled.ForeColor = Color.Green;
                btnInstallNodejs.Enabled = false;
                appSettings_.NodeJsInstalled = true;
            }
            else
            {
                darkLabelNodejsInstalled.Text = "✘";
                darkLabelNodejsInstalled.ForeColor = Color.Red;
                btnInstallNodejs.Enabled = true;
                appSettings_.NodeJsInstalled = false;
            }

            if (Directory.Exists(HttpServerModulePath))
            {
                darkLabelserveModuleInstalled.Text = "✔";
                darkLabelserveModuleInstalled.ForeColor = Color.Green;
                btnInstalleServerModule.Enabled = false;
                appSettings_.HttpServerInstalled = true;

            }
            else
            {
                darkLabelserveModuleInstalled.Text = "✘";
                darkLabelserveModuleInstalled.ForeColor = Color.Red;
                btnInstalleServerModule.Enabled = true;
                appSettings_.HttpServerInstalled = false;
            }

            #endregion nodejs&serve

            cbPs5BcCheck.CheckedChanged += cbPs5BcCheck_CheckedChanged;
            UpdateTrophyCacheStatus();
        }

        private void btnSaveClose_Click(object sender, EventArgs e)
        {
            SaveSettings();
            this.Hide();
        }

        private void SaveSettings()
        {
            Logger.LogInformation("Saving program settings..");

            appSettings_.OfficialUpdateDownloadDirectory = tbOfficialUpdateDownloadFolder.Text;
            appSettings_.PlayBgm = BGM.Checked;
            appSettings_.AutoSortRow = AutoSortRow.Checked;
            appSettings_.PkgColorLabel =PKGColorLabeling.Checked;

            // fore color
            appSettings_.GamePkgForeColor = darkLabelGamePkgColorLabel.ForeColor;
            appSettings_.PatchPkgForeColor = darkLabelPatchPkgColorLabel.ForeColor;
            appSettings_.AddonPkgForeColor = darkLabelAddonPkgColorLabel.ForeColor;
            appSettings_.AppPkgForeColor = darkLabelAppPkgColorLabel.ForeColor;

            // back color
            appSettings_.GamePkgBackColor = darkLabelGamePkgColorLabel.BackColor;
            appSettings_.PatchPkgBackColor = darkLabelPatchPkgColorLabel.BackColor;
            appSettings_.AddonPkgBackColor = darkLabelAddonPkgColorLabel.BackColor;
            appSettings_.AppPkgBackColor = darkLabelAppPkgColorLabel.BackColor;

            appSettings_.RenameCustomName = tbCustomNamePattern.Text;
            appSettings_.pkgtitleIdColumn = TitleId.Checked;
            appSettings_.pkgcontentIdColumn = ContentId.Checked;
            appSettings_.pkgregionColumn = Region.Checked;
            appSettings_.pkgversionColumn = Version.Checked;
            appSettings_.pkgminimumFirmwareColumn = SystemFirmware.Checked;
            appSettings_.pkgTypeColumn = PkgType.Checked;
            appSettings_.pkgcategoryColumn = Category.Checked;
            appSettings_.pkgsizeColumn = Size.Checked;
            appSettings_.pkgDirectoryColumn = Location.Checked;
            appSettings_.pkgBackportColumn = cbBackported.Checked;
            appSettings_.AutoFetchUpdate = cbAutoFetchUpdate.Checked;
            appSettings_.psvr_neo_ps5bc_check = cbPs5BcCheck.Checked;
            appSettings_.Shadps4Check = cbShadps4Check.Checked;
            appSettings_.Shadps4Os = cmbShadps4Os.SelectedIndex switch
            {
                1 => "linux",
                2 => "macos",
                _ => "windows",
            };
            appSettings_.Shadps4InstallDirectory = tbShadps4InstallDirectory.Text.Trim();
            appSettings_.ThemeIndex = cmbTheme.SelectedIndex;

            appSettings_.LocalServerIp = darkComboBoxServerIP.Text;
            appSettings_.Ps4Ip = tbPS4IP.Text;
            appSettings_.NodeJsInstalled = Tool.IsAppInstalled("Node.js");
            appSettings_.HttpServerInstalled = (Directory.Exists(HttpServerModulePath)) ? true : false;

            var PkgDirectoryList = lbPkgDirectoryList.Items.Cast<string>().ToList();
            appSettings_.PkgDirectories = PkgDirectoryList;
            appSettings_.ScanRecursive = darkCheckBoxRecursive.Checked;

            if (labelPs5BcJsonDownloadDate.Text != "" || labelPs5BcJsonDownloadDate.Text.Length != 0)
                appSettings_.Ps5BcJsonLastDownloadDate = DateTime.Parse(labelPs5BcJsonDownloadDate.Text);

            SettingsManager.SaveSettings(appSettings_, SettingFilePath);
        }

        private void btnPingPs4_Click(object sender, EventArgs e)
        {
            if (tbPS4IP.Text == string.Empty)
                return;

            Logger.LogInformation("Checking PS4 connectivity..");
            bool isPS4Connected = Tool.CheckForPS4Connection(tbPS4IP.Text);

            if (isPS4Connected)
            {
                ShowInformation("PS4 detected.", true);
            }
            else
            {
                ShowError("PS4 not detected.", true);
            }
        }

        private void btnInstallServerModule_Click(object sender, EventArgs e)
        {
            Logger.LogInformation("Installing http-server module..");
            bool nodejsInstalled = Tool.IsAppInstalled("Node.js");

            if (!nodejsInstalled)
            {
                ShowInformation("Please install Node.js before installing the serve module.", true);
                Tool.OpenWebLink("https://nodejs.org/en/download/");
                return;
            }

            this.Enabled = false;
            InstallServerModule();
            UpdateServerModuleStatus();
            this.Enabled = true;
        }

        private void InstallServerModule()
        {
            try
            {
                Process server = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = "cmd.exe",
                    Arguments = "/C npm install http-server -g"
                };
                server.StartInfo = startInfo;

                if (server.Start())
                {
                    server.WaitForExit();
                    int exitCode = server.ExitCode;

                    if (exitCode == 0)
                    {
                        Logger.LogInformation("http-server package installed.");
                        appSettings_.HttpServerInstalled = (Directory.Exists(HttpServerModulePath)) ? true : false;
                    }
                    else
                    {
                        ShowError($"An error occurred while installing http-server. Exit code: {exitCode}. Try install http-server manually in command prompt.", true);
                    }
                }
                else
                {
                    Logger.LogError("Failed to start the process.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("An error occurred: " + ex.Message);
            }
        }

        private void UpdateServerModuleStatus()
        {
            btnInstalleServerModule.Enabled = !appSettings_.HttpServerInstalled;
        }

        private void btnInstallNodejs_Click(object sender, EventArgs e)
        {
            Logger.LogInformation("Installing Node.js..");
            Tool.OpenWebLink("https://nodejs.org/en/download/");
        }

        private void SetPKGLabelColor_Click(object sender, EventArgs args)
        {
            if (!(sender is Button clickedButton))
                return;


            if (clickedButton == btnResetPkgLabelColor)
            {
                darkLabelGamePkgColorLabel.ForeColor = Color.FromArgb(220, 220, 220);
                darkLabelGamePkgColorLabel.BackColor = Color.FromArgb(60, 63, 65);

                darkLabelPatchPkgColorLabel.ForeColor = Color.FromArgb(220, 220, 220);
                darkLabelPatchPkgColorLabel.BackColor = Color.FromArgb(60, 63, 65);

                darkLabelAddonPkgColorLabel.ForeColor = Color.FromArgb(220, 220, 220);
                darkLabelAddonPkgColorLabel.BackColor = Color.FromArgb(60, 63, 65);

                darkLabelAppPkgColorLabel.ForeColor = Color.FromArgb(220, 220, 220);
                darkLabelAppPkgColorLabel.BackColor = Color.FromArgb(60, 63, 65);
            }
            else
            {
                ColorDialog colorDialog = new ColorDialog();
                DialogResult result = colorDialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    Color selectedColor = colorDialog.Color;
                    string colorValue = ColorTranslator.ToHtml(selectedColor);

                    // fore
                    if (clickedButton == btnGamePkgForeColor)
                        darkLabelGamePkgColorLabel.ForeColor = selectedColor;
                    if (clickedButton == btnPatchPkgForeColor)
                        darkLabelPatchPkgColorLabel.ForeColor = selectedColor;
                    if (clickedButton == btnAddonPkgForeColor)
                        darkLabelAddonPkgColorLabel.ForeColor = selectedColor;
                    if (clickedButton == btnAppPkgForeColor)
                        darkLabelAppPkgColorLabel.ForeColor = selectedColor;

                    // back
                    if (clickedButton == btnGamePkgBackColor)
                        darkLabelGamePkgColorLabel.BackColor = selectedColor;
                    if (clickedButton == btnPatchPkgBackColor)
                        darkLabelPatchPkgColorLabel.BackColor = selectedColor;
                    if (clickedButton == btnAddonPkgBackColor)
                        darkLabelAddonPkgColorLabel.BackColor = selectedColor;
                    if (clickedButton == btnAppPkgBackColor)
                        darkLabelAppPkgColorLabel.BackColor = selectedColor;

                    Logger.LogInformation($"Selected {colorValue}.");
                }
            }
        }

        private async void btnDownloadPS5BCJson_Click(object sender, EventArgs e)
        {
            Logger.LogInformation("Downloading PS5 Backward Compatibility json from github..");

            if (!Tool.CheckForInternetConnection("github.com"))
            {
                ShowError("Problem occured when try connecting to Github", true);
                return;
            }
            try
            {
                ShowTaskbarNotification("PS5 Backward Compatibility Status", "PS5 Backward Compatibility Status is being downloaded..");
                await Tool.DownloadFileFromUrlAsync("https://raw.githubusercontent.com/andshrew/supreme-enigma/master/docs/PS5-BC-Status.json", Ps5BcJsonFile);
                ShowInformation("PS5 Backward Compatibility Status file downloaded to AppData", true);
                appSettings_.Ps5BcJsonLastDownloadDate = DateTime.Now;
                labelPs5BcJsonDownloadDate.Text = appSettings_.Ps5BcJsonLastDownloadDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                ShowError("Failed to download PS5 Backward Compatibility Status file : " + ex.Message, true);
            }
        }

        private async void btnDownloadShadps4Json_Click(object sender, EventArgs e)
        {
            Logger.LogInformation("Downloading shadPS4 compatibility data from GitHub..");

            if (!Tool.CheckForInternetConnection("github.com"))
            {
                ShowError("Problem occured when try connecting to Github", true);
                return;
            }

            btnDownloadShadps4Json.Enabled = false;
            btnDownloadShadps4Json.Text = "Downloading...";
            try
            {
                var progress = new Progress<string>(s => Logger.LogInformation("shadPS4 compat: " + s));
                var (count, error) = await Shadps4Compat.DownloadAsync(progress);
                if (error != null)
                {
                    ShowError("Failed to download shadPS4 compatibility data: " + error, true);
                    return;
                }
                ShowInformation($"shadPS4 compatibility data downloaded ({count} games).", true);
                labelShadps4JsonDate.Text = Shadps4Compat.LastDownload?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? "..";
            }
            catch (Exception ex)
            {
                ShowError("Failed to download shadPS4 compatibility data: " + ex.Message, true);
            }
            finally
            {
                btnDownloadShadps4Json.Enabled = true;
                btnDownloadShadps4Json.Text = "Download compat data";
            }
        }

        private void btnBrowseShadps4Core_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Select the shadPS4 core (shadPS4.exe)",
                Filter = "shadPS4 core (shadPS4.exe)|shadPS4.exe|Executables (*.exe)|*.exe",
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            // Adopt the chosen installation - reference only, never modified.
            appSettings_.Shadps4ActiveCore = Shadps4ActiveCore.ForAdopted(ofd.FileName);
            SettingsManager.SaveSettings(appSettings_, SettingFilePath);
            RefreshShadps4Detection();
        }

        private void btnInstallShadps4Launcher_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Select the shadPS4 QtLauncher (shadPS4QtLauncher.exe)",
                Filter = "shadPS4 QtLauncher (shadPS4QtLauncher.exe)|shadPS4QtLauncher.exe|Executables (*.exe)|*.exe",
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            appSettings_.Shadps4ActiveLauncher = Shadps4ActiveCore.ForAdopted(ofd.FileName);
            SettingsManager.SaveSettings(appSettings_, SettingFilePath);
            RefreshShadps4Detection();
        }

        private void btnOpenShadps4Launcher_Click(object sender, EventArgs e)
        {
            var store = new Shadps4ManagedBuilds(appSettings_.Shadps4ManagedRoot);
            string? path = Shadps4ActiveCore.ResolveExecutable(
                appSettings_.Shadps4ActiveLauncher, store.ResolveManagedExecutable, out string? error);
            if (path == null)
            {
                MessageBoxHelper.ShowWarning((error ?? "No QtLauncher configured.") + "\n\nUse Install/Change to select or install one.", false);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                });
            }
            catch (Exception ex)
            {
                ShowError($"Failed to open the shadPS4 QtLauncher:\n{ex.Message}", false);
            }
        }

        private void btnManageShadps4Builds_Click(object sender, EventArgs e)
        {
            using var dlg = new Shadps4BuildManager(appSettings_);
            dlg.ShowDialog(this);
            tbShadps4ActiveCore.Text = DescribeActiveComponent(appSettings_.Shadps4ActiveCore);
            tbShadps4ActiveLauncher.Text = DescribeActiveComponent(appSettings_.Shadps4ActiveLauncher);
            RefreshShadps4Detection();
        }

        private void btnOpenShadps4ConfigFolder_Click(object sender, EventArgs e)
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4");
            if (!Directory.Exists(configDir))
            {
                ShowWarning("The shadPS4 config folder does not exist yet.\n\nStart shadPS4 once and it will be created.", false);
                return;
            }
            Process.Start("explorer.exe", configDir);
        }

        private async void btnCheckShadps4Updates_Click(object sender, EventArgs e)
        {
            btnCheckShadps4Updates.Enabled = false;
            btnCheckShadps4Updates.Text = "Checking...";
            try
            {
                var feed = new Shadps4ReleaseFeed();
                var stable = await feed.GetReleasesAsync(Shadps4FeedKind.CoreStable);
                var nightly = await feed.GetReleasesAsync(Shadps4FeedKind.CoreNightly);
                var launcher = await feed.GetReleasesAsync(Shadps4FeedKind.QtLauncher);

                string? latestStable = stable.Releases?.OrderByDescending(r => r.PublishedUtc).FirstOrDefault()?.Tag;
                string? latestNightly = nightly.Releases?.OrderByDescending(r => r.PublishedUtc).FirstOrDefault()?.BuildId;
                string? latestLauncher = launcher.Releases?.OrderByDescending(r => r.PublishedUtc).FirstOrDefault()?.BuildId;

                string coreError = stable.Error ?? nightly.Error;
                if (coreError != null)
                {
                    ShowWarning("shadPS4 update check failed:\n" + coreError, false);
                    return;
                }

                var message =
                    "Core\n" +
                    $"  Installed: {DescribeActiveComponent(appSettings_.Shadps4ActiveCore)}\n" +
                    $"  Latest stable: {latestStable ?? "unknown"}\n" +
                    $"  Latest nightly: {latestNightly ?? "unknown"}\n\n" +
                    "QtLauncher\n" +
                    $"  Installed: {DescribeActiveComponent(appSettings_.Shadps4ActiveLauncher)}\n" +
                    $"  Latest: {latestLauncher ?? "unknown"}\n\n" +
                    "Updates are never installed or activated automatically. Use Manage Builds to install a specific version.";

                var choice = AppMessageBox.Show("shadPS4 Updates", message,
                    AppMessageType.Info, AppMessageButtons.YesNo);
                if (choice == DialogResult.Yes)
                    Tool.OpenWebLink("https://github.com/shadps4-emu/shadPS4/releases");
            }
            catch (Exception ex)
            {
                ShowWarning("shadPS4 update check failed: " + ex.Message, false);
            }
            finally
            {
                btnCheckShadps4Updates.Enabled = true;
                btnCheckShadps4Updates.Text = "Check for Updates";
            }
        }

        /// <summary>Human-readable form of an active-component setting for the settings textboxes.</summary>
        private static string DescribeActiveComponent(string? setting)
        {
            var r = Shadps4ActiveCore.Parse(setting);
            return r.Source switch
            {
                Shadps4ComponentSource.Managed => "Build " + r.Value,
                Shadps4ComponentSource.Adopted => r.Value,
                _ => "(not set)",
            };
        }

        private void btnBrowseShadps4InstallDirectory_Click(object sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select the shadPS4 game install directory",
                ShowNewFolderButton = true,
            };
            string current = tbShadps4InstallDirectory.Text.Trim();
            if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                fbd.SelectedPath = current;
            if (fbd.ShowDialog() != DialogResult.OK) return;
            tbShadps4InstallDirectory.Text = fbd.SelectedPath;
            Logger.LogInformation($"shadPS4 install directory set to \"{fbd.SelectedPath}\"");
        }

        /// <summary>
        /// Re-runs environment detection and shows the resolved paths plus any
        /// detection warnings. Never throws - detection problems must not
        /// break the settings form.
        /// </summary>
        private void RefreshShadps4Detection()
        {
            try
            {
                var env = Shadps4EnvironmentResolver.Resolve(appSettings_.Shadps4ExecutablePath);

                // Auto-fill the install directory from shadPS4's own config
                // (first enabled library) - only when the user has not set
                // their own directory. No config -> stays empty.
                if (string.IsNullOrWhiteSpace(tbShadps4InstallDirectory.Text)
                    && env.InstallDirectories.Count > 0)
                {
                    tbShadps4InstallDirectory.Text = env.InstallDirectories[0];
                }

                var lines = new List<string>
                {
                    $"Active core: {DescribeActiveComponent(appSettings_.Shadps4ActiveCore)}",
                    $"QtLauncher: {DescribeActiveComponent(appSettings_.Shadps4ActiveLauncher)}",
                    $"Status: {(env.IsValid ? "Configuration detected successfully" : "shadPS4 not configured or not found")}",
                    $"Mode: {env.UserDirectoryMode}{(env.DetectionConfidence == Shadps4DetectionConfidence.Low ? " (low confidence)" : "")}",
                    $"Distribution: {env.DistributionType}",
                    $"Config: {Shorten(env.ConfigPath)} ({env.ConfigFormat})",
                    $"Libraries: {(env.InstallDirectories.Count == 0 ? "(none enabled)" : string.Join(" | ", env.InstallDirectories.Select(Shorten)))}",
                    $"Addon/DLC: {Shorten(env.AddonInstallDirectory)}",
                    $"Home: {Shorten(env.HomeDirectory)}",
                    $"Fonts: {Shorten(env.FontDirectory)}",
                    $"Sys modules: {Shorten(env.SysModulesDirectory)}",
                };
                foreach (var warning in env.Warnings)
                    lines.Add("⚠ " + warning);

                darkLabelShadps4Detect.Text = string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                darkLabelShadps4Detect.Text = "Detection failed: " + ex.Message;
            }
        }

        private static string Shorten(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "(not found)";
            return path.Length <= 70 ? path : "..." + path.Substring(path.Length - 67);
        }

        private static void ShowTaskbarNotification(string title, string text)
        {
            NotifyIcon notifyIcon = new NotifyIcon();
            notifyIcon.Icon = AppIcon;
            notifyIcon.Visible = true;
            notifyIcon.BalloonTipTitle = title;
            notifyIcon.BalloonTipText = text;
            notifyIcon.ShowBalloonTip(3000); // Display the balloon tip for 3 seconds

            // Clean up when done
            notifyIcon.Dispose();
        }

        private void cbPs5BcCheck_CheckedChanged(object sender, EventArgs e)
        {
            var ps5BcFile = File.Exists(Ps5BcJsonFile);
            if (cbPs5BcCheck.Checked && !ps5BcFile)
            {
                ShowWarning("Download PS5 Backward Compatibility Status json to use this feature", false);
                cbPs5BcCheck.Checked = false;
            }
            else if (cbPs5BcCheck.Checked)
            {
                Refresh = true;
            }
        }

        private void PlaceholderButton_Click(object sender, EventArgs e)
        {
            if (sender is DarkUI.Controls.DarkButton btn && btn.Tag != null)
            {
                string tag = btn.Tag.ToString();
                int pos = tbCustomNamePattern.SelectionStart;
                tbCustomNamePattern.Text = tbCustomNamePattern.Text.Insert(pos, tag);
                tbCustomNamePattern.SelectionStart = pos + tag.Length;
                tbCustomNamePattern.Focus();
            }
        }

        private void tbCustomNamePattern_TextChanged(object sender, EventArgs e)
        {
            darkLabelNamingPatternExample.Text = tbCustomNamePattern.Text
                    .Replace("{TITLE}", TITLE)
                    .Replace("{TITLE_ID}", TITLE_ID)
                    .Replace("{VERSION}", VERSION)
                    .Replace("{APP_VERSION}", APP_VERSION)
                    .Replace("{CATEGORY}", CATEGORY)
                    .Replace("{CONTENT_ID}", CONTENT_ID)
                    .Replace("{CONTENT_ID2}", CONTENT_ID2)
                    .Replace("{REGION}", REGION)
                    .Replace("{SYSTEM_VERSION}", SYSTEM_VERSION);
        }

        private void darkButton1_Click(object sender, EventArgs e)
        {
            tbCustomNamePattern.Text = "";
        }

        private void btnClearAllPkgDirectory_Click(object sender, EventArgs e)
        {
            lbPkgDirectoryList.Items.Clear();
        }

        private void btnDeletePkgDirectory_Click(object sender, EventArgs e)
        {
            if (lbPkgDirectoryList.SelectedItems.Count > 0)
            {
                int selectedIndex = lbPkgDirectoryList.SelectedIndex;
                lbPkgDirectoryList.Items.RemoveAt(selectedIndex);
            }
        }

        private void btnAddPkgDirectory_Click(object sender, EventArgs e)
        {
            if (ShowFolderBrowserDialog(out FolderBrowserDialog fbd))
            {
                string selectedFolder = fbd.SelectedPath;

                if (lbPkgDirectoryList.Items.Contains(selectedFolder))
                {
                    ShowError("Path already added.", false);
                    return;
                }

                if (Tool.IsRootDrive(selectedFolder))
                {
                    DialogResult dialogResult = DialogResultYesNo("Scanning the whole drive may take some time. Are you sure you want to proceed?");
                    if (dialogResult == DialogResult.No)
                    {
                        return;
                    }
                }

                lbPkgDirectoryList.Items.Add(selectedFolder);
            }
        }

        private void btnOpenAppData_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(AppDataDirectory))
                Process.Start("explorer.exe", AppDataDirectory);
        }

        private void ProgramSetting_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (btnBuildTrophyCache != null && !btnBuildTrophyCache.Enabled)
            {
                trophyCacheCancellation?.Cancel();
                lblTrophyCacheStatus.Text = "Cancelling trophy metadata cache build...";
                e.Cancel = true;
            }
        }

        private async void btnBuildTrophyCache_Click(object sender, EventArgs e)
        {
            List<string> directories = lbPkgDirectoryList.Items.Cast<string>().ToList();
            if (directories.Count == 0)
            {
                ShowWarning("Add at least one PKG directory before building the trophy metadata cache.", false);
                return;
            }
            if (!File.Exists(OrbisPubCmd))
            {
                ShowError("Missing orbis-pub-cmd.exe in AppData.", true);
                return;
            }

            trophyCacheCancellation?.Dispose();
            trophyCacheCancellation = new CancellationTokenSource();
            btnBuildTrophyCache.Enabled = false;
            btnClearTrophyCache.Enabled = false;
            btnCancelTrophyCache.Enabled = true;
            btnSaveClose.Enabled = false;
            lblTrophyCacheStatus.Text = "Scanning configured PKG directories...";
            pbTrophyCacheProgress.Value = 0;

            var progress = new Progress<TrophyCacheProgress>(value =>
            {
                pbTrophyCacheProgress.Maximum = Math.Max(1, value.Total);
                pbTrophyCacheProgress.Value = Math.Min(pbTrophyCacheProgress.Maximum, Math.Max(0, value.Processed));
                lblTrophyCacheStatus.Text = value.Total == 0
                    ? value.CurrentFile
                    : $"{value.Processed}/{value.Total}  {value.CurrentFile}";
            });

            try
            {
                Logger.LogInformation("Building trophy metadata cache from configured PKG directories...");
                var builder = new TrophyMetadataCacheBuilder();
                TrophyCacheBuildResult result = await builder.BuildAsync(
                    directories,
                    darkCheckBoxRecursive.Checked,
                    OrbisPubCmd,
                    TrophyCachePath,
                    Path.Combine(AppDataDirectory, "TrophyMetadata", "Temp"),
                    progress,
                    trophyCacheCancellation.Token);

                string summary = $"PKGs: {result.TotalPackages} | Added: {result.Added} | Cached: {result.AlreadyCached} | " +
                    $"No trophies: {result.WithoutTrophies} | Duplicates: {result.DuplicateContentIds} | Failed: {result.Failed}";
                lblTrophyCacheStatus.Text = (result.Cancelled ? "Cancelled. " : "Complete. ") + summary;
                Logger.LogInformation("Trophy metadata cache: " + lblTrophyCacheStatus.Text);
                foreach (string error in result.Errors)
                    Logger.LogWarning("Trophy cache: " + error);

                if (!result.Cancelled)
                    ShowInformation(summary, true);
            }
            catch (Exception ex)
            {
                lblTrophyCacheStatus.Text = "Cache build failed: " + ex.Message;
                Logger.LogError("Failed to build trophy metadata cache", ex);
                ShowError(lblTrophyCacheStatus.Text, true);
            }
            finally
            {
                btnBuildTrophyCache.Enabled = true;
                btnClearTrophyCache.Enabled = true;
                btnCancelTrophyCache.Enabled = false;
                btnSaveClose.Enabled = true;
            }
        }

        private void btnCancelTrophyCache_Click(object sender, EventArgs e)
        {
            trophyCacheCancellation?.Cancel();
        }

        private void btnClearTrophyCache_Click(object sender, EventArgs e)
        {
            if (DialogResultYesNo("Clear every cached NP Communication ID?") != DialogResult.Yes)
                return;
            try
            {
                new NpCommunicationIdCache(TrophyCachePath).Clear();
                UpdateTrophyCacheStatus();
                Logger.LogInformation("Trophy metadata cache cleared.");
            }
            catch (Exception ex)
            {
                ShowError("Failed to clear trophy metadata cache: " + ex.Message, true);
            }
        }

        private void UpdateTrophyCacheStatus()
        {
            try
            {
                int count = new NpCommunicationIdCache(TrophyCachePath).Count;
                lblTrophyCacheStatus.Text = $"Cached NP Communication IDs: {count}";
            }
            catch (Exception ex)
            {
                lblTrophyCacheStatus.Text = "Cache unavailable: " + ex.Message;
            }
        }
    }
}
