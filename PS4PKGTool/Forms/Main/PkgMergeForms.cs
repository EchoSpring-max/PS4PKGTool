using DarkUI.Controls;
using DarkUI.Forms;
using OrbisPkgTool;
using OrbisPkgTool.Pkg;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PS4PKGTool
{
    internal sealed record PkgMergePackage(string Path, string Title, string TitleId, string Version, string Category);

    internal sealed record PkgMergeOptions(string OutputPath, string WorkParentDirectory,
        bool ValidateAfterBuild, int WorkerCount);

    internal sealed class PkgMergeOptionsForm : DarkForm
    {
        private readonly DarkTextBox _outputPath = new() { Dock = DockStyle.Fill };
        private readonly DarkTextBox _workParent = new() { Dock = DockStyle.Fill };
        private readonly DarkCheckBox _validate = new() { Text = "Validate merged PKG after build", Checked = true, AutoSize = true };
        private readonly DarkNumericUpDown _workers = new() { Minimum = 0, Maximum = 64, Value = 1, Width = 70 };

        public PkgMergeOptions? Options { get; private set; }

        public PkgMergeOptionsForm(PkgMergePackage basePkg, PkgMergePackage patchPkg)
        {
            Icon = Utilities.PS4PKGToolHelper.Helper.AppIcon;
            Text = "Merge Base and Update PKG";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 330);

            string outputDirectory = Path.GetDirectoryName(basePkg.Path) ?? Environment.CurrentDirectory;
            _outputPath.Text = Path.Combine(outputDirectory,
                Path.GetFileNameWithoutExtension(basePkg.Path) + "_merged.pkg");
            string configuredTempDirectory = PS4PKGTool.Utilities.Settings.SettingsManager.appSettings_?.OrbisTempDirectory?.Trim() ?? "";
            _workParent.Text = string.IsNullOrWhiteSpace(configuredTempDirectory)
                ? outputDirectory
                : configuredTempDirectory;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 3,
                RowCount = 8
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            for (int i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            AddReadOnlyRow(layout, 0, "Base Game", basePkg.Path);
            AddReadOnlyRow(layout, 1, "Update", patchPkg.Path);
            AddReadOnlyRow(layout, 2, "Title ID", basePkg.TitleId);
            AddReadOnlyRow(layout, 3, "Versions", $"Base: {basePkg.Version}    Update: {patchPkg.Version}");

            layout.Controls.Add(Label("Output PKG"), 0, 4);
            layout.Controls.Add(_outputPath, 1, 4);
            var browseOutput = new DarkButton { Text = "Browse...", Dock = DockStyle.Fill };
            browseOutput.Click += (_, _) => BrowseOutput();
            layout.Controls.Add(browseOutput, 2, 4);

            layout.Controls.Add(Label("Work location"), 0, 5);
            layout.Controls.Add(_workParent, 1, 5);
            var browseWork = new DarkButton { Text = "Browse...", Dock = DockStyle.Fill };
            browseWork.Click += (_, _) => BrowseWorkLocation();
            layout.Controls.Add(browseWork, 2, 5);

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            options.Controls.Add(_validate);
            options.Controls.Add(new DarkLabel { Text = "Workers:", AutoSize = true, Margin = new Padding(22, 6, 4, 0) });
            options.Controls.Add(_workers);
            layout.Controls.Add(options, 1, 6);
            layout.SetColumnSpan(options, 2);

            var note = new DarkLabel
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.Silver,
                Text = "The source PKGs are not changed. A rebuilt Game PKG is created with the update version. The work location is used only as a parent for a temporary merge folder.",
                AutoEllipsis = true
            };
            layout.Controls.Add(note, 0, 7);
            layout.SetColumnSpan(note, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 7, 12, 7) };
            var cancel = new DarkButton { Text = "Cancel", Width = 82, DialogResult = DialogResult.Cancel };
            var merge = new DarkButton { Text = "Merge", Width = 82 };
            merge.Click += (_, _) => Confirm();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(merge);
            Controls.Add(layout);
            Controls.Add(buttons);
            AcceptButton = merge;
            CancelButton = cancel;
        }

        private static DarkLabel Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 7, 0, 0) };

        private static void AddReadOnlyRow(TableLayoutPanel layout, int row, string label, string value)
        {
            layout.Controls.Add(Label(label), 0, row);
            var text = new DarkTextBox { Text = value, ReadOnly = true, Dock = DockStyle.Fill };
            layout.Controls.Add(text, 1, row);
            layout.SetColumnSpan(text, 2);
        }

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
                MessageBox.Show(this, "Choose an output PKG filename.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(workParent))
            {
                MessageBox.Show(this, "Choose a work location.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                workParent = Path.GetFullPath(workParent);
                Directory.CreateDirectory(workParent);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The work location could not be created:\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Options = new PkgMergeOptions(output, workParent, _validate.Checked, (int)_workers.Value);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class PkgMergeProgressForm : DarkForm
    {
        private readonly PkgMergeRequest _request;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly DarkLabel _stage = new() { Dock = DockStyle.Top, Height = 28, Padding = new Padding(12, 6, 12, 0) };
        private readonly DarkLabel _detail = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(12, 2, 12, 0), ForeColor = Color.Silver, AutoEllipsis = true };
        private readonly DarkProgressBar _progress = new() { Dock = DockStyle.Top, Height = 18, Marquee = true, MarqueeAnimationSpeed = 25 };
        private readonly DarkButton _cancel = new() { Text = "Cancel", Width = 84, Height = 30 };
        private bool _finished;

        public PkgMergeResult? Result { get; private set; }
        public Exception? Error { get; private set; }
        public bool WasCancelled { get; private set; }

        public PkgMergeProgressForm(PkgMergeRequest request)
        {
            _request = request;
            Icon = Utilities.PS4PKGToolHelper.Helper.AppIcon;
            Text = "Merging PKGs";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ClientSize = new Size(560, 145);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 7, 12, 7) };
            _cancel.Click += (_, _) => { _cancellation.Cancel(); _cancel.Enabled = false; _stage.Text = "Cancelling merge..."; };
            footer.Controls.Add(_cancel);
            Controls.Add(footer);
            Controls.Add(_progress);
            Controls.Add(_detail);
            Controls.Add(_stage);
            Shown += async (_, _) => await RunMergeAsync();
        }

        private async Task RunMergeAsync()
        {
            var progress = new Progress<PkgMergeProgress>(UpdateProgress);
            try
            {
                Result = await Task.Run(() => new PkgMergeService().Merge(_request with
                {
                    Progress = progress,
                    CancellationToken = _cancellation.Token
                }));
                _finished = true;
                DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                _finished = true;
                DialogResult = DialogResult.Cancel;
            }
            catch (Exception ex)
            {
                Error = ex;
                _finished = true;
                DialogResult = DialogResult.Abort;
            }
            finally
            {
                _finished = true;
                Close();
            }
        }

        private void UpdateProgress(PkgMergeProgress progress)
        {
            _stage.Text = progress.Stage;
            _detail.Text = progress.CurrentFile ?? "";
            if (progress.Percentage is not double percent) { _progress.Marquee = true; return; }
            _progress.Marquee = false;
            _progress.Value = Math.Clamp((int)Math.Round(percent), 0, 100);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_finished)
            {
                _cancellation.Cancel();
                e.Cancel = true;
                return;
            }
            _cancellation.Dispose();
            base.OnFormClosing(e);
        }
    }
}
