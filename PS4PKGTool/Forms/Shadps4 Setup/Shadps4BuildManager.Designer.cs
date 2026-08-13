namespace PS4PKGTool
{
    partial class Shadps4BuildManager
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            grpCoreBuilds = new DarkUI.Controls.DarkSectionPanel();
            lstCoreBuilds = new DarkUI.Controls.DarkListBox();
            btnCoreMakeActive = new DarkUI.Controls.DarkButton();
            btnCoreOpenFolder = new DarkUI.Controls.DarkButton();
            grpLauncherBuilds = new DarkUI.Controls.DarkSectionPanel();
            lstLauncherBuilds = new DarkUI.Controls.DarkListBox();
            btnLauncherMakeActive = new DarkUI.Controls.DarkButton();
            btnLauncherOpenFolder = new DarkUI.Controls.DarkButton();
            btnInstallShadps4 = new DarkUI.Controls.DarkButton();
            btnChooseCoreVersion = new DarkUI.Controls.DarkButton();
            btnResetSetup = new DarkUI.Controls.DarkButton();
            btnClose = new DarkUI.Controls.DarkButton();
            lblStatus = new DarkUI.Controls.DarkLabel();
            grpCoreBuilds.SuspendLayout();
            grpLauncherBuilds.SuspendLayout();
            this.SuspendLayout();
            //
            // grpCoreBuilds
            //
            grpCoreBuilds.Controls.Add(lstCoreBuilds);
            grpCoreBuilds.Controls.Add(btnCoreMakeActive);
            grpCoreBuilds.Controls.Add(btnCoreOpenFolder);
            grpCoreBuilds.Location = new System.Drawing.Point(12, 12);
            grpCoreBuilds.Name = "grpCoreBuilds";
            grpCoreBuilds.SectionHeader = "Managed shadPS4 Cores";
            grpCoreBuilds.Size = new System.Drawing.Size(300, 310);
            grpCoreBuilds.TabIndex = 0;
            //
            // lstCoreBuilds
            //
            lstCoreBuilds.Location = new System.Drawing.Point(14, 32);
            lstCoreBuilds.Name = "lstCoreBuilds";
            lstCoreBuilds.Size = new System.Drawing.Size(272, 230);
            lstCoreBuilds.TabIndex = 0;
            //
            // btnCoreMakeActive
            //
            btnCoreMakeActive.Location = new System.Drawing.Point(14, 270);
            btnCoreMakeActive.Name = "btnCoreMakeActive";
            btnCoreMakeActive.Size = new System.Drawing.Size(130, 26);
            btnCoreMakeActive.TabIndex = 1;
            btnCoreMakeActive.Text = "Make Active";
            btnCoreMakeActive.Click += btnCoreMakeActive_Click;
            //
            // btnCoreOpenFolder
            //
            btnCoreOpenFolder.Location = new System.Drawing.Point(156, 270);
            btnCoreOpenFolder.Name = "btnCoreOpenFolder";
            btnCoreOpenFolder.Size = new System.Drawing.Size(130, 26);
            btnCoreOpenFolder.TabIndex = 2;
            btnCoreOpenFolder.Text = "Open Folder";
            btnCoreOpenFolder.Click += btnCoreOpenFolder_Click;
            //
            // grpLauncherBuilds
            //
            grpLauncherBuilds.Controls.Add(lstLauncherBuilds);
            grpLauncherBuilds.Controls.Add(btnLauncherMakeActive);
            grpLauncherBuilds.Controls.Add(btnLauncherOpenFolder);
            grpLauncherBuilds.Location = new System.Drawing.Point(324, 12);
            grpLauncherBuilds.Name = "grpLauncherBuilds";
            grpLauncherBuilds.SectionHeader = "Managed QtLauncher Builds";
            grpLauncherBuilds.Size = new System.Drawing.Size(300, 310);
            grpLauncherBuilds.TabIndex = 1;
            //
            // lstLauncherBuilds
            //
            lstLauncherBuilds.Location = new System.Drawing.Point(14, 32);
            lstLauncherBuilds.Name = "lstLauncherBuilds";
            lstLauncherBuilds.Size = new System.Drawing.Size(272, 230);
            lstLauncherBuilds.TabIndex = 0;
            //
            // btnLauncherMakeActive
            //
            btnLauncherMakeActive.Location = new System.Drawing.Point(14, 270);
            btnLauncherMakeActive.Name = "btnLauncherMakeActive";
            btnLauncherMakeActive.Size = new System.Drawing.Size(130, 26);
            btnLauncherMakeActive.TabIndex = 1;
            btnLauncherMakeActive.Text = "Make Active";
            btnLauncherMakeActive.Click += btnLauncherMakeActive_Click;
            //
            // btnLauncherOpenFolder
            //
            btnLauncherOpenFolder.Location = new System.Drawing.Point(156, 270);
            btnLauncherOpenFolder.Name = "btnLauncherOpenFolder";
            btnLauncherOpenFolder.Size = new System.Drawing.Size(130, 26);
            btnLauncherOpenFolder.TabIndex = 2;
            btnLauncherOpenFolder.Text = "Open Folder";
            btnLauncherOpenFolder.Click += btnLauncherOpenFolder_Click;
            //
            // btnInstallShadps4
            //
            btnInstallShadps4.Location = new System.Drawing.Point(12, 334);
            btnInstallShadps4.Name = "btnInstallShadps4";
            btnInstallShadps4.Size = new System.Drawing.Size(150, 26);
            btnInstallShadps4.TabIndex = 2;
            btnInstallShadps4.Text = "Install shadPS4...";
            btnInstallShadps4.Click += btnInstallShadps4_Click;
            //
            // btnChooseCoreVersion
            //
            btnChooseCoreVersion.Location = new System.Drawing.Point(170, 334);
            btnChooseCoreVersion.Name = "btnChooseCoreVersion";
            btnChooseCoreVersion.Size = new System.Drawing.Size(160, 26);
            btnChooseCoreVersion.TabIndex = 3;
            btnChooseCoreVersion.Text = "Choose Core Version...";
            btnChooseCoreVersion.Click += btnChooseCoreVersion_Click;
            //
            // btnResetSetup
            //
            btnResetSetup.Location = new System.Drawing.Point(340, 334);
            btnResetSetup.Name = "btnResetSetup";
            btnResetSetup.Size = new System.Drawing.Size(170, 26);
            btnResetSetup.TabIndex = 5;
            btnResetSetup.Text = "Reset shadPS4 Setup...";
            btnResetSetup.Click += btnResetSetup_Click;
            //
            // btnClose
            //
            btnClose.Location = new System.Drawing.Point(524, 334);
            btnClose.Name = "btnClose";
            btnClose.Size = new System.Drawing.Size(100, 26);
            btnClose.TabIndex = 4;
            btnClose.Text = "Close";
            btnClose.Click += btnClose_Click;
            //
            // lblStatus
            //
            lblStatus.AutoEllipsis = true;
            lblStatus.Font = new System.Drawing.Font("Segoe UI", 8F);
            lblStatus.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            lblStatus.Location = new System.Drawing.Point(12, 372);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(612, 60);
            lblStatus.TabIndex = 5;
            lblStatus.Text = "Managed builds live under %LOCALAPPDATA%\\PS4PKGTool\\shadPS4\\builds. Old builds are never deleted - switching the active build provides rollback.";
            //
            // Shadps4BuildManager
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(636, 440);
            this.Controls.Add(grpCoreBuilds);
            this.Controls.Add(grpLauncherBuilds);
            this.Controls.Add(btnInstallShadps4);
            this.Controls.Add(btnChooseCoreVersion);
            this.Controls.Add(btnResetSetup);
            this.Controls.Add(btnClose);
            this.Controls.Add(lblStatus);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Shadps4BuildManager";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Manage shadPS4 Builds";
            grpCoreBuilds.ResumeLayout(false);
            grpLauncherBuilds.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private DarkUI.Controls.DarkSectionPanel grpCoreBuilds;
        private DarkUI.Controls.DarkListBox lstCoreBuilds;
        private DarkUI.Controls.DarkButton btnCoreMakeActive;
        private DarkUI.Controls.DarkButton btnCoreOpenFolder;
        private DarkUI.Controls.DarkSectionPanel grpLauncherBuilds;
        private DarkUI.Controls.DarkListBox lstLauncherBuilds;
        private DarkUI.Controls.DarkButton btnLauncherMakeActive;
        private DarkUI.Controls.DarkButton btnLauncherOpenFolder;
        private DarkUI.Controls.DarkButton btnInstallShadps4;
        private DarkUI.Controls.DarkButton btnChooseCoreVersion;
        private DarkUI.Controls.DarkButton btnResetSetup;
        private DarkUI.Controls.DarkButton btnClose;
        private DarkUI.Controls.DarkLabel lblStatus;
    }
}
