using DarkUI.Forms;
using System;
using System.IO;
using System.Windows.Forms;

namespace PS4PKGTool
{
    internal sealed record PkgMergePackage(string Path, string Title, string TitleId, string Version, string Category);

    internal sealed record PkgMergeOptions(string OutputPath, string WorkParentDirectory,
        bool ValidateAfterBuild, int WorkerCount);

    internal sealed partial class PkgMergeOptionsForm : DarkForm
    {
        public PkgMergeOptions? Options { get; private set; }

        public PkgMergeOptionsForm(PkgMergePackage basePkg, PkgMergePackage patchPkg)
        {
            InitializeComponent();

            string outputDirectory = Path.GetDirectoryName(basePkg.Path) ?? Environment.CurrentDirectory;
            txtBaseGame.Text = basePkg.Path;
            txtUpdate.Text = patchPkg.Path;
            txtTitleId.Text = basePkg.TitleId;
            txtVersions.Text = $"Base: {basePkg.Version}    Update: {patchPkg.Version}";
            _outputPath.Text = Path.Combine(outputDirectory,
                Path.GetFileNameWithoutExtension(basePkg.Path) + "_merged.pkg");
            string configuredTempDirectory = PS4PKGTool.Utilities.Settings.SettingsManager.appSettings_?.OrbisTempDirectory?.Trim() ?? "";
            _workParent.Text = string.IsNullOrWhiteSpace(configuredTempDirectory) ? outputDirectory : configuredTempDirectory;
        }

        private void btnBrowseOutput_Click(object sender, EventArgs e) => BrowseOutput();

        private void btnBrowseWork_Click(object sender, EventArgs e) => BrowseWorkLocation();

        private void btnMerge_Click(object sender, EventArgs e) => Confirm();

        private void BrowseOutput()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "PS4 Package (*.pkg)|*.pkg",
                FileName = Path.GetFileName(_outputPath.Text),
                InitialDirectory = Path.GetDirectoryName(_outputPath.Text)
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) _outputPath.Text = dialog.FileName;
        }

        private void BrowseWorkLocation()
        {
            using var dialog = new FolderBrowserDialog { InitialDirectory = _workParent.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) _workParent.Text = dialog.SelectedPath;
        }

        private void Confirm()
        {
            string output = _outputPath.Text.Trim();
            string workParent = _workParent.Text.Trim();
            if (string.IsNullOrWhiteSpace(output) || !output.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            {
                ShowWarning("Choose an output PKG filename.", false);
                return;
            }
            if (string.IsNullOrWhiteSpace(workParent))
            {
                ShowWarning("Choose a work location.", false);
                return;
            }

            try
            {
                workParent = Path.GetFullPath(workParent);
                Directory.CreateDirectory(workParent);
            }
            catch (Exception ex)
            {
                ShowWarning("The work location could not be created:\n" + ex.Message, false);
                return;
            }

            Options = new PkgMergeOptions(output, workParent, _validate.Checked, (int)_workers.Value);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

}
