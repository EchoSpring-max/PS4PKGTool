namespace PS4PKGTool
{
    internal sealed partial class PkgMergeOptionsForm
    {
        private System.ComponentModel.IContainer components;
        private System.Windows.Forms.TableLayoutPanel layout;
        private System.Windows.Forms.FlowLayoutPanel optionsPanel;
        private System.Windows.Forms.FlowLayoutPanel buttonsPanel;
        private DarkUI.Controls.DarkLabel lblBaseGame;
        private DarkUI.Controls.DarkLabel lblUpdate;
        private DarkUI.Controls.DarkLabel lblTitleId;
        private DarkUI.Controls.DarkLabel lblVersions;
        private DarkUI.Controls.DarkLabel lblOutputPkg;
        private DarkUI.Controls.DarkLabel lblWorkLocation;
        private DarkUI.Controls.DarkLabel lblWorkers;
        private DarkUI.Controls.DarkLabel lblNote;
        private DarkUI.Controls.DarkTextBox txtBaseGame;
        private DarkUI.Controls.DarkTextBox txtUpdate;
        private DarkUI.Controls.DarkTextBox txtTitleId;
        private DarkUI.Controls.DarkTextBox txtVersions;
        private DarkUI.Controls.DarkTextBox _outputPath;
        private DarkUI.Controls.DarkTextBox _workParent;
        private DarkUI.Controls.DarkCheckBox _validate;
        private DarkUI.Controls.DarkNumericUpDown _workers;
        private DarkUI.Controls.DarkButton btnBrowseOutput;
        private DarkUI.Controls.DarkButton btnBrowseWork;
        private DarkUI.Controls.DarkButton btnCancel;
        private DarkUI.Controls.DarkButton btnMerge;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            layout = new System.Windows.Forms.TableLayoutPanel();
            lblBaseGame = new DarkUI.Controls.DarkLabel();
            txtBaseGame = new DarkUI.Controls.DarkTextBox();
            lblUpdate = new DarkUI.Controls.DarkLabel();
            txtUpdate = new DarkUI.Controls.DarkTextBox();
            lblTitleId = new DarkUI.Controls.DarkLabel();
            txtTitleId = new DarkUI.Controls.DarkTextBox();
            lblVersions = new DarkUI.Controls.DarkLabel();
            txtVersions = new DarkUI.Controls.DarkTextBox();
            lblOutputPkg = new DarkUI.Controls.DarkLabel();
            _outputPath = new DarkUI.Controls.DarkTextBox();
            btnBrowseOutput = new DarkUI.Controls.DarkButton();
            lblWorkLocation = new DarkUI.Controls.DarkLabel();
            _workParent = new DarkUI.Controls.DarkTextBox();
            btnBrowseWork = new DarkUI.Controls.DarkButton();
            optionsPanel = new System.Windows.Forms.FlowLayoutPanel();
            _validate = new DarkUI.Controls.DarkCheckBox();
            lblWorkers = new DarkUI.Controls.DarkLabel();
            _workers = new DarkUI.Controls.DarkNumericUpDown();
            lblNote = new DarkUI.Controls.DarkLabel();
            buttonsPanel = new System.Windows.Forms.FlowLayoutPanel();
            btnCancel = new DarkUI.Controls.DarkButton();
            btnMerge = new DarkUI.Controls.DarkButton();
            layout.SuspendLayout();
            optionsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_workers).BeginInit();
            buttonsPanel.SuspendLayout();
            SuspendLayout();

            layout.ColumnCount = 3;
            layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 105F));
            layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 88F));
            layout.Controls.Add(lblBaseGame, 0, 0);
            layout.Controls.Add(txtBaseGame, 1, 0);
            layout.Controls.Add(lblUpdate, 0, 1);
            layout.Controls.Add(txtUpdate, 1, 1);
            layout.Controls.Add(lblTitleId, 0, 2);
            layout.Controls.Add(txtTitleId, 1, 2);
            layout.Controls.Add(lblVersions, 0, 3);
            layout.Controls.Add(txtVersions, 1, 3);
            layout.Controls.Add(lblOutputPkg, 0, 4);
            layout.Controls.Add(_outputPath, 1, 4);
            layout.Controls.Add(btnBrowseOutput, 2, 4);
            layout.Controls.Add(lblWorkLocation, 0, 5);
            layout.Controls.Add(_workParent, 1, 5);
            layout.Controls.Add(btnBrowseWork, 2, 5);
            layout.Controls.Add(optionsPanel, 1, 6);
            layout.Controls.Add(lblNote, 0, 7);
            layout.Dock = System.Windows.Forms.DockStyle.Fill;
            layout.Name = "layout";
            layout.Padding = new System.Windows.Forms.Padding(12);
            layout.RowCount = 8;
            for (int i = 0; i < 7; i++)
                layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layout.SetColumnSpan(txtBaseGame, 2);
            layout.SetColumnSpan(txtUpdate, 2);
            layout.SetColumnSpan(txtTitleId, 2);
            layout.SetColumnSpan(txtVersions, 2);
            layout.SetColumnSpan(optionsPanel, 2);
            layout.SetColumnSpan(lblNote, 3);
            layout.Size = new System.Drawing.Size(760, 286);
            layout.TabIndex = 0;

            ConfigureRowLabel(lblBaseGame, "Base Game");
            ConfigureRowLabel(lblUpdate, "Update");
            ConfigureRowLabel(lblTitleId, "Title ID");
            ConfigureRowLabel(lblVersions, "Versions");
            ConfigureRowLabel(lblOutputPkg, "Output PKG");
            ConfigureRowLabel(lblWorkLocation, "Work location");
            ConfigureReadOnlyValue(txtBaseGame, "txtBaseGame");
            ConfigureReadOnlyValue(txtUpdate, "txtUpdate");
            ConfigureReadOnlyValue(txtTitleId, "txtTitleId");
            ConfigureReadOnlyValue(txtVersions, "txtVersions");

            _outputPath.Dock = System.Windows.Forms.DockStyle.Fill;
            _outputPath.Name = "_outputPath";
            _outputPath.TabIndex = 0;
            btnBrowseOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            btnBrowseOutput.Name = "btnBrowseOutput";
            btnBrowseOutput.TabIndex = 1;
            btnBrowseOutput.Text = "Browse...";
            btnBrowseOutput.Click += btnBrowseOutput_Click;

            _workParent.Dock = System.Windows.Forms.DockStyle.Fill;
            _workParent.Name = "_workParent";
            _workParent.TabIndex = 2;
            btnBrowseWork.Dock = System.Windows.Forms.DockStyle.Fill;
            btnBrowseWork.Name = "btnBrowseWork";
            btnBrowseWork.TabIndex = 3;
            btnBrowseWork.Text = "Browse...";
            btnBrowseWork.Click += btnBrowseWork_Click;

            optionsPanel.Controls.Add(_validate);
            optionsPanel.Controls.Add(lblWorkers);
            optionsPanel.Controls.Add(_workers);
            optionsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            optionsPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            optionsPanel.Name = "optionsPanel";
            optionsPanel.WrapContents = false;
            _validate.AutoSize = true;
            _validate.Checked = true;
            _validate.CheckState = System.Windows.Forms.CheckState.Checked;
            _validate.Name = "_validate";
            _validate.Text = "Validate merged PKG after build";
            lblWorkers.AutoSize = true;
            lblWorkers.Margin = new System.Windows.Forms.Padding(22, 6, 4, 0);
            lblWorkers.Name = "lblWorkers";
            lblWorkers.Text = "Workers:";
            _workers.Maximum = 64;
            _workers.Name = "_workers";
            _workers.Size = new System.Drawing.Size(70, 23);
            _workers.Value = 1;

            lblNote.AutoEllipsis = true;
            lblNote.Dock = System.Windows.Forms.DockStyle.Fill;
            lblNote.ForeColor = System.Drawing.Color.Silver;
            lblNote.Name = "lblNote";
            lblNote.Text = "The source PKGs are not changed. A rebuilt Game PKG is created with the update version. The work location is used only as a parent for a temporary merge folder.";

            buttonsPanel.Controls.Add(btnCancel);
            buttonsPanel.Controls.Add(btnMerge);
            buttonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            buttonsPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            buttonsPanel.Location = new System.Drawing.Point(0, 286);
            buttonsPanel.Name = "buttonsPanel";
            buttonsPanel.Padding = new System.Windows.Forms.Padding(12, 7, 12, 7);
            buttonsPanel.Size = new System.Drawing.Size(760, 44);
            buttonsPanel.TabIndex = 1;
            btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new System.Drawing.Size(82, 30);
            btnCancel.Text = "Cancel";
            btnMerge.Name = "btnMerge";
            btnMerge.Size = new System.Drawing.Size(82, 30);
            btnMerge.Text = "Merge";
            btnMerge.Click += btnMerge_Click;

            AcceptButton = btnMerge;
            CancelButton = btnCancel;
            ClientSize = new System.Drawing.Size(760, 330);
            Controls.Add(layout);
            Controls.Add(buttonsPanel);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Icon = global::PS4PKGTool.Properties.Resources.PackageIcon;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "PkgMergeOptionsForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Merge Base and Update PKG";
            layout.ResumeLayout(false);
            layout.PerformLayout();
            optionsPanel.ResumeLayout(false);
            optionsPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_workers).EndInit();
            buttonsPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        private static void ConfigureRowLabel(DarkUI.Controls.DarkLabel label, string text)
        {
            label.AutoSize = true;
            label.Margin = new System.Windows.Forms.Padding(0, 7, 0, 0);
            label.Text = text;
        }

        private static void ConfigureReadOnlyValue(DarkUI.Controls.DarkTextBox textBox, string name)
        {
            textBox.Dock = System.Windows.Forms.DockStyle.Fill;
            textBox.Name = name;
            textBox.ReadOnly = true;
        }
    }

}
