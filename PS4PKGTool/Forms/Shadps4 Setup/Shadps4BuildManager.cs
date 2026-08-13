using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4PKGTool.Utilities.PS4PKGToolHelper;
using PS4PKGTool.Utilities.Settings;
using PS4PKGTool.Utilities.Shadps4;
using DarkUI.Forms;

namespace PS4PKGTool
{
    /// <summary>
    /// Managed-build management: list installed core/launcher builds, switch
    /// the active build (rollback = switch back - old builds are never
    /// deleted), open build folders, install the wizard-recommended setup or
    /// a specific core version. All layout lives in the Designer file.
    /// </summary>
    public partial class Shadps4BuildManager : DarkForm
    {
        private readonly AppSettings _settings;
        private Shadps4ManagedBuilds _store;

        public Shadps4BuildManager(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _store = new Shadps4ManagedBuilds(_settings.Shadps4ManagedRoot);
            this.Icon = Helper.AppIcon;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshLists();
        }

        private void RefreshLists()
        {
            _store = new Shadps4ManagedBuilds(_settings.Shadps4ManagedRoot);

            var activeCore = Shadps4ActiveCore.Parse(_settings.Shadps4ActiveCore);
            var activeLauncher = Shadps4ActiveCore.Parse(_settings.Shadps4ActiveLauncher);

            FillCoreList(_store.ListBuilds(Shadps4Component.Core), activeCore);
            FillList(lstLauncherBuilds, _store.ListBuilds(Shadps4Component.QtLauncher), activeLauncher, NoLauncherBuildsHint);
        }

        // DarkUI's DarkListBox crashes (OnDrawItem, index -1) when drawing an
        // EMPTY list - always keep at least one placeholder item.
        private const string NoCoreBuildsHint = "(no managed core builds yet - use Install shadPS4 Setup...)";
        private const string NoLauncherBuildsHint = "(no managed launcher builds yet - use Install shadPS4 Setup...)";

        private void FillCoreList(IReadOnlyList<Shadps4InstalledBuild> builds, Shadps4ComponentRef active)
        {
            lstCoreBuilds.Items.Clear();
            foreach (var b in builds)
            {
                bool isActive = active.Source == Shadps4ComponentSource.Managed
                    && string.Equals(active.Value, b.BuildId, StringComparison.OrdinalIgnoreCase);
                lstCoreBuilds.Items.Add(isActive ? $"✓ {b.BuildId}  Active" : $"  {b.BuildId}");
            }
            if (lstCoreBuilds.Items.Count == 0) lstCoreBuilds.Items.Add(NoCoreBuildsHint);
        }

        private void FillList(DarkUI.Controls.DarkListBox list, IReadOnlyList<Shadps4InstalledBuild> builds, Shadps4ComponentRef active, string emptyHint)
        {
            list.Items.Clear();
            foreach (var b in builds)
            {
                bool isActive = active.Source == Shadps4ComponentSource.Managed
                    && string.Equals(active.Value, b.BuildId, StringComparison.OrdinalIgnoreCase);
                list.Items.Add(isActive ? $"✓ {b.BuildId}  Active" : $"  {b.BuildId}");
            }
            if (list.Items.Count == 0) list.Items.Add(emptyHint);
        }

        private Shadps4InstalledBuild? SelectedCoreBuild()
        {
            var builds = _store.ListBuilds(Shadps4Component.Core);
            int idx = lstCoreBuilds.SelectedIndex;
            return idx >= 0 && idx < builds.Count ? builds[idx] : null;
        }

        private Shadps4InstalledBuild? SelectedLauncherBuild()
        {
            var builds = _store.ListBuilds(Shadps4Component.QtLauncher);
            int idx = lstLauncherBuilds.SelectedIndex;
            return idx >= 0 && idx < builds.Count ? builds[idx] : null;
        }

        private void btnCoreMakeActive_Click(object sender, EventArgs e)
        {
            var build = SelectedCoreBuild();
            if (build == null) { MessageBoxHelper.ShowWarning("Select a core build first.", false); return; }
            _settings.Shadps4ActiveCore = Shadps4ActiveCore.ForManaged(build.BuildId);
            SettingsManager.SaveSettings(_settings, SettingsManager.SettingFilePath);
            RefreshLists();
        }

        private void btnLauncherMakeActive_Click(object sender, EventArgs e)
        {
            var build = SelectedLauncherBuild();
            if (build == null) { MessageBoxHelper.ShowWarning("Select a launcher build first.", false); return; }
            _settings.Shadps4ActiveLauncher = Shadps4ActiveCore.ForManaged(build.BuildId);
            SettingsManager.SaveSettings(_settings, SettingsManager.SettingFilePath);
            RefreshLists();
        }

        private void btnCoreOpenFolder_Click(object sender, EventArgs e)
        {
            var build = SelectedCoreBuild();
            if (build == null) { MessageBoxHelper.ShowWarning("Select a core build first.", false); return; }
            Process.Start("explorer.exe", build.DirectoryPath);
        }

        private void btnLauncherOpenFolder_Click(object sender, EventArgs e)
        {
            var build = SelectedLauncherBuild();
            if (build == null) { MessageBoxHelper.ShowWarning("Select a launcher build first.", false); return; }
            Process.Start("explorer.exe", build.DirectoryPath);
        }

        private void btnInstallShadps4_Click(object sender, EventArgs e)
        {
            using var wizard = new Shadps4SetupWizard(_settings);
            wizard.ShowDialog(this);
            RefreshLists();
        }

        private async void btnChooseCoreVersion_Click(object sender, EventArgs e)
        {
            btnChooseCoreVersion.Enabled = false;
            lblStatus.Text = "Loading available core versions from GitHub...";
            try
            {
                var feed = new Shadps4ReleaseFeed();
                var stableResult = await feed.GetReleasesAsync(Shadps4FeedKind.CoreStable);
                var nightlyResult = await feed.GetReleasesAsync(Shadps4FeedKind.CoreNightly);

                var releases = new List<Shadps4ReleaseInfo>();
                if (stableResult.Releases != null) releases.AddRange(stableResult.Releases);
                if (nightlyResult.Releases != null) releases.AddRange(nightlyResult.Releases);
                releases = releases.OrderByDescending(r => r.PublishedUtc).ToList();

                if (releases.Count == 0)
                {
                    MessageBoxHelper.ShowWarning("No core versions could be loaded.\n\n" + (stableResult.Error ?? nightlyResult.Error ?? "Unknown error."), false);
                    return;
                }

                using var picker = new Shadps4VersionPicker(releases);
                if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null) return;

                var selected = picker.Selected;
                if (_store.FindExe(Shadps4Component.Core, selected.BuildId) != null)
                {
                    MessageBoxHelper.ShowWarning($"Build {selected.BuildId} is already installed. Use Make Active to switch to it.", false);
                    RefreshLists();
                    return;
                }

                await RunInstallAsync(selected);
            }
            catch (Exception ex)
            {
                MessageBoxHelper.ShowWarning("Failed to load core versions: " + ex.Message, false);
            }
            finally
            {
                btnChooseCoreVersion.Enabled = true;
            }
        }

        private async Task RunInstallAsync(Shadps4ReleaseInfo release)
        {
            btnChooseCoreVersion.Enabled = false;
            btnInstallShadps4.Enabled = false;
            btnClose.Enabled = false;
            try
            {
                var svc = new Shadps4SetupService();
                var progress = new Progress<string>(s => lblStatus.Text = s);
                using var cts = new CancellationTokenSource();

                var result = await svc.InstallBuildAsync(release, _store, progress, cts.Token);
                lblStatus.Text = result.Message;
                switch (result.Status)
                {
                    case Shadps4SetupStatus.Success:
                        var askActive = AppMessageBox.Show("shadPS4 Builds",
                            $"{result.Message}\n\nMake this build the active core now?",
                            AppMessageType.Info, AppMessageButtons.YesNo);
                        if (askActive == DialogResult.Yes && result.Build != null)
                        {
                            _settings.Shadps4ActiveCore = Shadps4ActiveCore.ForManaged(result.Build.BuildId);
                            SettingsManager.SaveSettings(_settings, SettingsManager.SettingFilePath);
                        }
                        RefreshLists();
                        break;
                    case Shadps4SetupStatus.Cancelled:
                        break;
                    default:
                        MessageBoxHelper.ShowWarning(result.Message, false);
                        break;
                }
            }
            finally
            {
                btnChooseCoreVersion.Enabled = true;
                btnInstallShadps4.Enabled = true;
                btnClose.Enabled = true;
            }
        }

        private void btnResetSetup_Click(object sender, EventArgs e)
        {
            var choice = AppMessageBox.Show("Reset shadPS4 Setup",
                "Reset the shadPS4 setup?\n\n" +
                "This removes:\n" +
                "  - All managed shadPS4 builds downloaded by PS4 PKG Tool\n" +
                "  - The shadPS4 settings in PS4 PKG Tool (active core, launcher, install directory)\n\n" +
                "This does NOT touch:\n" +
                "  - shadPS4's own configuration (%APPDATA%\\shadPS4)\n" +
                "  - Your saves, games or installed libraries\n\n" +
                "You can redo the setup afterwards with Install shadPS4 Setup...\n\n" +
                "Reset?",
                AppMessageType.Warning, AppMessageButtons.YesNo);
            if (choice != DialogResult.Yes) return;

            Shadps4SetupReset.Reset(_store, _settings);
            SettingsManager.SaveSettings(_settings, SettingsManager.SettingFilePath);
            RefreshLists();
            MessageBoxHelper.ShowWarning(
                "shadPS4 setup has been reset.\n\nUse Install shadPS4 Setup... to start fresh.", false);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

    }
}
