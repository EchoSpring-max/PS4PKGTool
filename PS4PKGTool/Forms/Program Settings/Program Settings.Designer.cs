namespace PS4PKGTool
{
    partial class ProgramSetting
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing) { if (disposing && (components != null)) components.Dispose(); base.Dispose(disposing); }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            settingsTab = new DarkUI.Controls.DarkTabControl();
            tabGeneral = new System.Windows.Forms.TabPage();
            grpDirectories = new DarkUI.Controls.DarkSectionPanel();
            grpDirList = new DarkUI.Controls.DarkSectionPanel();
            lbPkgDirectoryList = new DarkUI.Controls.DarkListBox(components);
            darkCheckBoxRecursive = new DarkUI.Controls.DarkCheckBox();
            btnAddPkgDirectory = new DarkUI.Controls.DarkButton();
            btnDeletePkgDirectory = new DarkUI.Controls.DarkButton();
            btnClearAllPkgDirectory = new DarkUI.Controls.DarkButton();
            grpDownloads = new DarkUI.Controls.DarkSectionPanel();
            darkLabel5 = new DarkUI.Controls.DarkLabel();
            tbOfficialUpdateDownloadFolder = new DarkUI.Controls.DarkTextBox();
            btnOfficialUpdateDownloadFolder = new DarkUI.Controls.DarkButton();
            grpStartup = new DarkUI.Controls.DarkSectionPanel();
            BGM = new DarkUI.Controls.DarkCheckBox();
            AutoSortRow = new DarkUI.Controls.DarkCheckBox();
            cbAutoFetchUpdate = new DarkUI.Controls.DarkCheckBox();
            lblTheme = new DarkUI.Controls.DarkLabel();
            cmbTheme = new DarkUI.Controls.DarkComboBox();
            btnOpenAppData = new DarkUI.Controls.DarkButton();
            tabAppearance = new System.Windows.Forms.TabPage();
            grpColumns = new DarkUI.Controls.DarkSectionPanel();
            PKGname = new DarkUI.Controls.DarkCheckBox();
            TitleId = new DarkUI.Controls.DarkCheckBox();
            ContentId = new DarkUI.Controls.DarkCheckBox();
            Region = new DarkUI.Controls.DarkCheckBox();
            SystemFirmware = new DarkUI.Controls.DarkCheckBox();
            Version = new DarkUI.Controls.DarkCheckBox();
            PkgType = new DarkUI.Controls.DarkCheckBox();
            Category = new DarkUI.Controls.DarkCheckBox();
            Size = new DarkUI.Controls.DarkCheckBox();
            Location = new DarkUI.Controls.DarkCheckBox();
            cbBackported = new DarkUI.Controls.DarkCheckBox();
            grpColors = new DarkUI.Controls.DarkSectionPanel();
            PKGColorLabeling = new DarkUI.Controls.DarkCheckBox();
            darkLabelGamePkgColorLabel = new DarkUI.Controls.DarkLabel();
            btnGamePkgForeColor = new DarkUI.Controls.DarkButton();
            btnGamePkgBackColor = new DarkUI.Controls.DarkButton();
            darkLabelPatchPkgColorLabel = new DarkUI.Controls.DarkLabel();
            btnPatchPkgForeColor = new DarkUI.Controls.DarkButton();
            btnPatchPkgBackColor = new DarkUI.Controls.DarkButton();
            darkLabelAddonPkgColorLabel = new DarkUI.Controls.DarkLabel();
            btnAddonPkgForeColor = new DarkUI.Controls.DarkButton();
            btnAddonPkgBackColor = new DarkUI.Controls.DarkButton();
            darkLabelAppPkgColorLabel = new DarkUI.Controls.DarkLabel();
            btnAppPkgForeColor = new DarkUI.Controls.DarkButton();
            btnAppPkgBackColor = new DarkUI.Controls.DarkButton();
            btnResetPkgLabelColor = new DarkUI.Controls.DarkButton();
            grpPS5BC = new DarkUI.Controls.DarkSectionPanel();
            cbPs5BcCheck = new DarkUI.Controls.DarkCheckBox();
            btnDownloadPS5BCJson = new DarkUI.Controls.DarkButton();
            darkLabel9 = new DarkUI.Controls.DarkLabel();
            labelPs5BcJsonDownloadDate = new DarkUI.Controls.DarkLabel();
            grpShadps4 = new DarkUI.Controls.DarkSectionPanel();
            cbShadps4Check = new DarkUI.Controls.DarkCheckBox();
            btnDownloadShadps4Json = new DarkUI.Controls.DarkButton();
            darkLabelShadps4 = new DarkUI.Controls.DarkLabel();
            labelShadps4JsonDate = new DarkUI.Controls.DarkLabel();
            darkLabelShadps4Os = new DarkUI.Controls.DarkLabel();
            cmbShadps4Os = new DarkUI.Controls.DarkComboBox();
            tabTrophies = new System.Windows.Forms.TabPage();
            grpTrophyCache = new DarkUI.Controls.DarkSectionPanel();
            lblTrophyCacheDesc = new DarkUI.Controls.DarkLabel();
            btnBuildTrophyCache = new DarkUI.Controls.DarkButton();
            btnCancelTrophyCache = new DarkUI.Controls.DarkButton();
            btnClearTrophyCache = new DarkUI.Controls.DarkButton();
            pbTrophyCacheProgress = new System.Windows.Forms.ProgressBar();
            lblTrophyCacheStatus = new DarkUI.Controls.DarkLabel();
            tabRename = new System.Windows.Forms.TabPage();
            grpRename = new DarkUI.Controls.DarkSectionPanel();
            darkLabel12 = new DarkUI.Controls.DarkLabel();
            tbCustomNamePattern = new DarkUI.Controls.DarkTextBox();
            darkLabelPlaceholderHint = new DarkUI.Controls.DarkLabel();
            btnPlaceTitle = new DarkUI.Controls.DarkButton();
            btnPlaceTitleId = new DarkUI.Controls.DarkButton();
            btnPlaceVersion = new DarkUI.Controls.DarkButton();
            btnPlaceAppVer = new DarkUI.Controls.DarkButton();
            btnPlaceCategory = new DarkUI.Controls.DarkButton();
            btnPlaceContentId = new DarkUI.Controls.DarkButton();
            btnPlaceRegion = new DarkUI.Controls.DarkButton();
            btnPlaceSysVer = new DarkUI.Controls.DarkButton();
            darkButton1 = new DarkUI.Controls.DarkButton();
            darkLabelNamingPatternExample = new DarkUI.Controls.DarkLabel();
            tabRPI = new System.Windows.Forms.TabPage();
            grpNetwork = new DarkUI.Controls.DarkSectionPanel();
            darkLabel1 = new DarkUI.Controls.DarkLabel();
            darkComboBoxServerIP = new DarkUI.Controls.DarkComboBox();
            darkLabel2 = new DarkUI.Controls.DarkLabel();
            tbPS4IP = new DarkUI.Controls.DarkTextBox();
            btnPingPs4 = new DarkUI.Controls.DarkButton();
            grpTools = new DarkUI.Controls.DarkSectionPanel();
            darkLabel3 = new DarkUI.Controls.DarkLabel();
            darkLabelNodejsInstalled = new DarkUI.Controls.DarkLabel();
            btnInstallNodejs = new DarkUI.Controls.DarkButton();
            darkLabel4 = new DarkUI.Controls.DarkLabel();
            darkLabelserveModuleInstalled = new DarkUI.Controls.DarkLabel();
            btnInstalleServerModule = new DarkUI.Controls.DarkButton();
            btnSaveClose = new DarkUI.Controls.DarkButton();
            flatTabControl1 = new DarkUI.Controls.DarkTabControl();
            darkLabel6 = new DarkUI.Controls.DarkLabel();
            darkLabel8 = new DarkUI.Controls.DarkLabel();
            darkLabel10 = new DarkUI.Controls.DarkLabel();
            darkLabel11 = new DarkUI.Controls.DarkLabel();
            darkSectionPanel9 = new DarkUI.Controls.DarkSectionPanel();
            settingsTab.SuspendLayout();
            tabGeneral.SuspendLayout();
            grpDirectories.SuspendLayout();
            grpDirList.SuspendLayout();
            grpDownloads.SuspendLayout();
            grpStartup.SuspendLayout();
            tabAppearance.SuspendLayout();
            grpColumns.SuspendLayout();
            grpColors.SuspendLayout();
            grpPS5BC.SuspendLayout();
            grpShadps4.SuspendLayout();
            tabTrophies.SuspendLayout();
            grpTrophyCache.SuspendLayout();
            tabRename.SuspendLayout();
            grpRename.SuspendLayout();
            tabRPI.SuspendLayout();
            grpNetwork.SuspendLayout();
            grpTools.SuspendLayout();
            SuspendLayout();
            // 
            // settingsTab
            // 
            settingsTab.AllowDrop = true;
            settingsTab.Controls.Add(tabGeneral);
            settingsTab.Controls.Add(tabAppearance);
            settingsTab.Controls.Add(tabTrophies);
            settingsTab.Controls.Add(tabRename);
            settingsTab.Controls.Add(tabRPI);
            settingsTab.Font = new System.Drawing.Font("Segoe UI", 9F);
            settingsTab.ItemSize = new System.Drawing.Size(150, 28);
            settingsTab.Location = new System.Drawing.Point(12, 11);
            settingsTab.Name = "settingsTab";
            settingsTab.Padding = new System.Drawing.Point(0, 0);
            settingsTab.SelectedIndex = 0;
            settingsTab.Size = new System.Drawing.Size(640, 552);
            settingsTab.TabIndex = 0;
            // 
            // tabGeneral
            // 
            tabGeneral.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabGeneral.Controls.Add(grpDirectories);
            tabGeneral.Controls.Add(grpDownloads);
            tabGeneral.Controls.Add(grpStartup);
            tabGeneral.Location = new System.Drawing.Point(4, 32);
            tabGeneral.Name = "tabGeneral";
            tabGeneral.Size = new System.Drawing.Size(632, 452);
            tabGeneral.TabIndex = 0;
            tabGeneral.Text = "General";
            // 
            // grpDirectories
            // 
            grpDirectories.Controls.Add(grpDirList);
            grpDirectories.Controls.Add(darkCheckBoxRecursive);
            grpDirectories.Controls.Add(btnAddPkgDirectory);
            grpDirectories.Controls.Add(btnDeletePkgDirectory);
            grpDirectories.Controls.Add(btnClearAllPkgDirectory);
            grpDirectories.Location = new System.Drawing.Point(12, 219);
            grpDirectories.Name = "grpDirectories";
            grpDirectories.SectionHeader = "PKG Directory Settings";
            grpDirectories.Size = new System.Drawing.Size(606, 218);
            grpDirectories.TabIndex = 0;
            // 
            // grpDirList
            // 
            grpDirList.Controls.Add(lbPkgDirectoryList);
            grpDirList.Location = new System.Drawing.Point(15, 32);
            grpDirList.Name = "grpDirList";
            grpDirList.SectionHeader = "PKG Directory List";
            grpDirList.Size = new System.Drawing.Size(572, 130);
            grpDirList.TabIndex = 0;
            // 
            // lbPkgDirectoryList
            // 
            lbPkgDirectoryList.BackColor = System.Drawing.Color.FromArgb(69, 73, 74);
            lbPkgDirectoryList.BorderStyle = System.Windows.Forms.BorderStyle.None;
            lbPkgDirectoryList.Dock = System.Windows.Forms.DockStyle.Fill;
            lbPkgDirectoryList.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            lbPkgDirectoryList.Font = new System.Drawing.Font("Segoe UI", 9F);
            lbPkgDirectoryList.ForeColor = System.Drawing.Color.Gainsboro;
            lbPkgDirectoryList.FormattingEnabled = true;
            lbPkgDirectoryList.ItemHeight = 15;
            lbPkgDirectoryList.Location = new System.Drawing.Point(1, 25);
            lbPkgDirectoryList.Name = "lbPkgDirectoryList";
            lbPkgDirectoryList.Size = new System.Drawing.Size(570, 104);
            lbPkgDirectoryList.TabIndex = 0;
            lbPkgDirectoryList.TabStop = false;
            lbPkgDirectoryList.UseTabStops = false;
            // 
            // darkCheckBoxRecursive
            // 
            darkCheckBoxRecursive.AutoSize = true;
            darkCheckBoxRecursive.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkCheckBoxRecursive.Location = new System.Drawing.Point(474, 181);
            darkCheckBoxRecursive.Name = "darkCheckBoxRecursive";
            darkCheckBoxRecursive.Size = new System.Drawing.Size(110, 19);
            darkCheckBoxRecursive.TabIndex = 1;
            darkCheckBoxRecursive.Text = "Scan recursively";
            // 
            // btnAddPkgDirectory
            // 
            btnAddPkgDirectory.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnAddPkgDirectory.Location = new System.Drawing.Point(15, 176);
            btnAddPkgDirectory.Name = "btnAddPkgDirectory";
            btnAddPkgDirectory.Size = new System.Drawing.Size(132, 28);
            btnAddPkgDirectory.TabIndex = 2;
            btnAddPkgDirectory.Text = "Add directory";
            btnAddPkgDirectory.Click += btnAddPkgDirectory_Click;
            // 
            // btnDeletePkgDirectory
            // 
            btnDeletePkgDirectory.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnDeletePkgDirectory.Location = new System.Drawing.Point(168, 176);
            btnDeletePkgDirectory.Name = "btnDeletePkgDirectory";
            btnDeletePkgDirectory.Size = new System.Drawing.Size(132, 28);
            btnDeletePkgDirectory.TabIndex = 3;
            btnDeletePkgDirectory.Text = "Delete directory";
            btnDeletePkgDirectory.Click += btnDeletePkgDirectory_Click;
            // 
            // btnClearAllPkgDirectory
            // 
            btnClearAllPkgDirectory.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnClearAllPkgDirectory.Location = new System.Drawing.Point(321, 176);
            btnClearAllPkgDirectory.Name = "btnClearAllPkgDirectory";
            btnClearAllPkgDirectory.Size = new System.Drawing.Size(132, 28);
            btnClearAllPkgDirectory.TabIndex = 4;
            btnClearAllPkgDirectory.Text = "Clear all";
            btnClearAllPkgDirectory.Click += btnClearAllPkgDirectory_Click;
            // 
            // grpDownloads
            // 
            grpDownloads.Controls.Add(darkLabel5);
            grpDownloads.Controls.Add(tbOfficialUpdateDownloadFolder);
            grpDownloads.Controls.Add(btnOfficialUpdateDownloadFolder);
            grpDownloads.Location = new System.Drawing.Point(12, 16);
            grpDownloads.Name = "grpDownloads";
            grpDownloads.SectionHeader = "Official Update Download Directory";
            grpDownloads.Size = new System.Drawing.Size(606, 80);
            grpDownloads.TabIndex = 1;
            // 
            // darkLabel5
            // 
            darkLabel5.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabel5.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel5.Location = new System.Drawing.Point(15, 29);
            darkLabel5.Name = "darkLabel5";
            darkLabel5.Size = new System.Drawing.Size(115, 18);
            darkLabel5.TabIndex = 0;
            darkLabel5.Text = "Download location:";
            // 
            // tbOfficialUpdateDownloadFolder
            // 
            tbOfficialUpdateDownloadFolder.Font = new System.Drawing.Font("Segoe UI", 9F);
            tbOfficialUpdateDownloadFolder.Location = new System.Drawing.Point(15, 47);
            tbOfficialUpdateDownloadFolder.Name = "tbOfficialUpdateDownloadFolder";
            tbOfficialUpdateDownloadFolder.ReadOnly = true;
            tbOfficialUpdateDownloadFolder.Size = new System.Drawing.Size(545, 23);
            tbOfficialUpdateDownloadFolder.TabIndex = 1;
            // 
            // btnOfficialUpdateDownloadFolder
            // 
            btnOfficialUpdateDownloadFolder.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnOfficialUpdateDownloadFolder.Location = new System.Drawing.Point(565, 47);
            btnOfficialUpdateDownloadFolder.Name = "btnOfficialUpdateDownloadFolder";
            btnOfficialUpdateDownloadFolder.Size = new System.Drawing.Size(26, 23);
            btnOfficialUpdateDownloadFolder.TabIndex = 2;
            btnOfficialUpdateDownloadFolder.Text = "…";
            btnOfficialUpdateDownloadFolder.Click += btnOfficialUpdateDownloadFolder_Click;
            // 
            // grpStartup
            // 
            grpStartup.Controls.Add(BGM);
            grpStartup.Controls.Add(AutoSortRow);
            grpStartup.Controls.Add(cbAutoFetchUpdate);
            grpStartup.Controls.Add(lblTheme);
            grpStartup.Controls.Add(cmbTheme);
            grpStartup.Controls.Add(btnOpenAppData);
            grpStartup.Location = new System.Drawing.Point(12, 104);
            grpStartup.Name = "grpStartup";
            grpStartup.SectionHeader = "Startup & Behavior";
            grpStartup.Size = new System.Drawing.Size(606, 107);
            grpStartup.TabIndex = 2;
            // 
            // BGM
            // 
            BGM.AutoSize = true;
            BGM.Font = new System.Drawing.Font("Segoe UI", 9F);
            BGM.ForeColor = System.Drawing.Color.Gainsboro;
            BGM.Location = new System.Drawing.Point(15, 32);
            BGM.Name = "BGM";
            BGM.Size = new System.Drawing.Size(220, 19);
            BGM.TabIndex = 1;
            BGM.Text = "Play selected PKG background music";
            // 
            // AutoSortRow
            // 
            AutoSortRow.AutoSize = true;
            AutoSortRow.Font = new System.Drawing.Font("Segoe UI", 9F);
            AutoSortRow.ForeColor = System.Drawing.Color.Gainsboro;
            AutoSortRow.Location = new System.Drawing.Point(15, 54);
            AutoSortRow.Name = "AutoSortRow";
            AutoSortRow.Size = new System.Drawing.Size(220, 19);
            AutoSortRow.TabIndex = 2;
            AutoSortRow.Text = "Auto-sort PKG list in ascending order";
            // 
            // cbAutoFetchUpdate
            // 
            cbAutoFetchUpdate.AutoSize = true;
            cbAutoFetchUpdate.Font = new System.Drawing.Font("Segoe UI", 9F);
            cbAutoFetchUpdate.ForeColor = System.Drawing.Color.Gainsboro;
            cbAutoFetchUpdate.Location = new System.Drawing.Point(15, 76);
            cbAutoFetchUpdate.Name = "cbAutoFetchUpdate";
            cbAutoFetchUpdate.Size = new System.Drawing.Size(293, 19);
            cbAutoFetchUpdate.TabIndex = 3;
            cbAutoFetchUpdate.Text = "Show Latest Update column & auto-fetch on startup";
            // 
            // lblTheme
            // 
            lblTheme.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTheme.ForeColor = System.Drawing.Color.Gainsboro;
            lblTheme.Location = new System.Drawing.Point(346, 31);
            lblTheme.Name = "lblTheme";
            lblTheme.Size = new System.Drawing.Size(60, 20);
            lblTheme.TabIndex = 4;
            lblTheme.Text = "Theme:";
            lblTheme.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // cmbTheme
            // 
            cmbTheme.Font = new System.Drawing.Font("Segoe UI", 9F);
            cmbTheme.Location = new System.Drawing.Point(411, 29);
            cmbTheme.Name = "cmbTheme";
            cmbTheme.Size = new System.Drawing.Size(180, 24);
            cmbTheme.TabIndex = 5;
            // 
            // btnOpenAppData
            // 
            btnOpenAppData.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnOpenAppData.Location = new System.Drawing.Point(470, 73);
            btnOpenAppData.Name = "btnOpenAppData";
            btnOpenAppData.Size = new System.Drawing.Size(120, 24);
            btnOpenAppData.TabIndex = 4;
            btnOpenAppData.Text = "Open App Data";
            btnOpenAppData.Click += btnOpenAppData_Click;
            // 
            // tabAppearance
            // 
            tabAppearance.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabAppearance.Controls.Add(grpColumns);
            tabAppearance.Controls.Add(grpColors);
            tabAppearance.Controls.Add(grpPS5BC);
            tabAppearance.Controls.Add(grpShadps4);
            tabAppearance.Location = new System.Drawing.Point(4, 32);
            tabAppearance.Name = "tabAppearance";
            tabAppearance.Size = new System.Drawing.Size(632, 516);
            tabAppearance.TabIndex = 1;
            tabAppearance.Text = "Appearance";
            // 
            // grpColumns
            // 
            grpColumns.Controls.Add(PKGname);
            grpColumns.Controls.Add(TitleId);
            grpColumns.Controls.Add(ContentId);
            grpColumns.Controls.Add(Region);
            grpColumns.Controls.Add(SystemFirmware);
            grpColumns.Controls.Add(Version);
            grpColumns.Controls.Add(PkgType);
            grpColumns.Controls.Add(Category);
            grpColumns.Controls.Add(Size);
            grpColumns.Controls.Add(Location);
            grpColumns.Controls.Add(cbBackported);
            grpColumns.Location = new System.Drawing.Point(12, 16);
            grpColumns.Name = "grpColumns";
            grpColumns.SectionHeader = "Column Visibility";
            grpColumns.Size = new System.Drawing.Size(605, 110);
            grpColumns.TabIndex = 0;
            // 
            // PKGname
            // 
            PKGname.AutoSize = true;
            PKGname.Checked = true;
            PKGname.CheckState = System.Windows.Forms.CheckState.Checked;
            PKGname.Enabled = false;
            PKGname.Font = new System.Drawing.Font("Segoe UI", 9F);
            PKGname.Location = new System.Drawing.Point(22, 33);
            PKGname.Name = "PKGname";
            PKGname.Size = new System.Drawing.Size(74, 19);
            PKGname.TabIndex = 0;
            PKGname.Text = "Filename";
            // 
            // TitleId
            // 
            TitleId.AutoSize = true;
            TitleId.Checked = true;
            TitleId.CheckState = System.Windows.Forms.CheckState.Checked;
            TitleId.Font = new System.Drawing.Font("Segoe UI", 9F);
            TitleId.Location = new System.Drawing.Point(176, 33);
            TitleId.Name = "TitleId";
            TitleId.Size = new System.Drawing.Size(63, 19);
            TitleId.TabIndex = 1;
            TitleId.Text = "Title ID";
            // 
            // ContentId
            // 
            ContentId.AutoSize = true;
            ContentId.Checked = true;
            ContentId.CheckState = System.Windows.Forms.CheckState.Checked;
            ContentId.Font = new System.Drawing.Font("Segoe UI", 9F);
            ContentId.Location = new System.Drawing.Point(319, 33);
            ContentId.Name = "ContentId";
            ContentId.Size = new System.Drawing.Size(83, 19);
            ContentId.TabIndex = 2;
            ContentId.Text = "Content ID";
            // 
            // Region
            // 
            Region.AutoSize = true;
            Region.Checked = true;
            Region.CheckState = System.Windows.Forms.CheckState.Checked;
            Region.Font = new System.Drawing.Font("Segoe UI", 9F);
            Region.Location = new System.Drawing.Point(482, 33);
            Region.Name = "Region";
            Region.Size = new System.Drawing.Size(63, 19);
            Region.TabIndex = 3;
            Region.Text = "Region";
            // 
            // SystemFirmware
            // 
            SystemFirmware.AutoSize = true;
            SystemFirmware.Checked = true;
            SystemFirmware.CheckState = System.Windows.Forms.CheckState.Checked;
            SystemFirmware.Font = new System.Drawing.Font("Segoe UI", 9F);
            SystemFirmware.Location = new System.Drawing.Point(22, 57);
            SystemFirmware.Name = "SystemFirmware";
            SystemFirmware.Size = new System.Drawing.Size(105, 19);
            SystemFirmware.TabIndex = 4;
            SystemFirmware.Text = "System Version";
            // 
            // Version
            // 
            Version.AutoSize = true;
            Version.Checked = true;
            Version.CheckState = System.Windows.Forms.CheckState.Checked;
            Version.Font = new System.Drawing.Font("Segoe UI", 9F);
            Version.Location = new System.Drawing.Point(176, 57);
            Version.Name = "Version";
            Version.Size = new System.Drawing.Size(116, 19);
            Version.TabIndex = 5;
            Version.Text = "Version [App Ver]";
            // 
            // PkgType
            // 
            PkgType.AutoSize = true;
            PkgType.Checked = true;
            PkgType.CheckState = System.Windows.Forms.CheckState.Checked;
            PkgType.Font = new System.Drawing.Font("Segoe UI", 9F);
            PkgType.Location = new System.Drawing.Point(319, 57);
            PkgType.Name = "PkgType";
            PkgType.Size = new System.Drawing.Size(75, 19);
            PkgType.TabIndex = 6;
            PkgType.Text = "PKG Type";
            // 
            // Category
            // 
            Category.AutoSize = true;
            Category.Checked = true;
            Category.CheckState = System.Windows.Forms.CheckState.Checked;
            Category.Font = new System.Drawing.Font("Segoe UI", 9F);
            Category.Location = new System.Drawing.Point(484, 57);
            Category.Name = "Category";
            Category.Size = new System.Drawing.Size(74, 19);
            Category.TabIndex = 7;
            Category.Text = "Category";
            // 
            // Size
            // 
            Size.AutoSize = true;
            Size.Checked = true;
            Size.CheckState = System.Windows.Forms.CheckState.Checked;
            Size.Font = new System.Drawing.Font("Segoe UI", 9F);
            Size.Location = new System.Drawing.Point(22, 81);
            Size.Name = "Size";
            Size.Size = new System.Drawing.Size(46, 19);
            Size.TabIndex = 8;
            Size.Text = "Size";
            // 
            // Location
            // 
            Location.AutoSize = true;
            Location.Checked = true;
            Location.CheckState = System.Windows.Forms.CheckState.Checked;
            Location.Font = new System.Drawing.Font("Segoe UI", 9F);
            Location.Location = new System.Drawing.Point(176, 81);
            Location.Name = "Location";
            Location.Size = new System.Drawing.Size(74, 19);
            Location.TabIndex = 9;
            Location.Text = "Directory";
            // 
            // cbBackported
            // 
            cbBackported.AutoSize = true;
            cbBackported.Checked = true;
            cbBackported.CheckState = System.Windows.Forms.CheckState.Checked;
            cbBackported.Font = new System.Drawing.Font("Segoe UI", 9F);
            cbBackported.Location = new System.Drawing.Point(319, 81);
            cbBackported.Name = "cbBackported";
            cbBackported.Size = new System.Drawing.Size(86, 19);
            cbBackported.TabIndex = 10;
            cbBackported.Text = "Backported";
            // 
            // grpColors
            // 
            grpColors.Controls.Add(PKGColorLabeling);
            grpColors.Controls.Add(darkLabelGamePkgColorLabel);
            grpColors.Controls.Add(btnGamePkgForeColor);
            grpColors.Controls.Add(btnGamePkgBackColor);
            grpColors.Controls.Add(darkLabelPatchPkgColorLabel);
            grpColors.Controls.Add(btnPatchPkgForeColor);
            grpColors.Controls.Add(btnPatchPkgBackColor);
            grpColors.Controls.Add(darkLabelAddonPkgColorLabel);
            grpColors.Controls.Add(btnAddonPkgForeColor);
            grpColors.Controls.Add(btnAddonPkgBackColor);
            grpColors.Controls.Add(darkLabelAppPkgColorLabel);
            grpColors.Controls.Add(btnAppPkgForeColor);
            grpColors.Controls.Add(btnAppPkgBackColor);
            grpColors.Controls.Add(btnResetPkgLabelColor);
            grpColors.Location = new System.Drawing.Point(12, 132);
            grpColors.Name = "grpColors";
            grpColors.SectionHeader = "PKG Color Labeling";
            grpColors.Size = new System.Drawing.Size(605, 169);
            grpColors.TabIndex = 1;
            // 
            // PKGColorLabeling
            // 
            PKGColorLabeling.AutoSize = true;
            PKGColorLabeling.Font = new System.Drawing.Font("Segoe UI", 9F);
            PKGColorLabeling.ForeColor = System.Drawing.Color.Gainsboro;
            PKGColorLabeling.Location = new System.Drawing.Point(22, 35);
            PKGColorLabeling.Name = "PKGColorLabeling";
            PKGColorLabeling.Size = new System.Drawing.Size(136, 19);
            PKGColorLabeling.TabIndex = 0;
            PKGColorLabeling.Text = "Enable color labeling";
            // 
            // darkLabelGamePkgColorLabel
            // 
            darkLabelGamePkgColorLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelGamePkgColorLabel.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabelGamePkgColorLabel.Location = new System.Drawing.Point(22, 63);
            darkLabelGamePkgColorLabel.Name = "darkLabelGamePkgColorLabel";
            darkLabelGamePkgColorLabel.Size = new System.Drawing.Size(55, 24);
            darkLabelGamePkgColorLabel.TabIndex = 1;
            darkLabelGamePkgColorLabel.Text = "Game";
            darkLabelGamePkgColorLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnGamePkgForeColor
            // 
            btnGamePkgForeColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnGamePkgForeColor.Location = new System.Drawing.Point(82, 63);
            btnGamePkgForeColor.Name = "btnGamePkgForeColor";
            btnGamePkgForeColor.Size = new System.Drawing.Size(80, 24);
            btnGamePkgForeColor.TabIndex = 2;
            btnGamePkgForeColor.Text = "ForeColor";
            btnGamePkgForeColor.Click += SetPKGLabelColor_Click;
            // 
            // btnGamePkgBackColor
            // 
            btnGamePkgBackColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnGamePkgBackColor.Location = new System.Drawing.Point(167, 63);
            btnGamePkgBackColor.Name = "btnGamePkgBackColor";
            btnGamePkgBackColor.Size = new System.Drawing.Size(80, 24);
            btnGamePkgBackColor.TabIndex = 3;
            btnGamePkgBackColor.Text = "BackColor";
            btnGamePkgBackColor.Click += SetPKGLabelColor_Click;
            // 
            // darkLabelPatchPkgColorLabel
            // 
            darkLabelPatchPkgColorLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelPatchPkgColorLabel.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabelPatchPkgColorLabel.Location = new System.Drawing.Point(22, 90);
            darkLabelPatchPkgColorLabel.Name = "darkLabelPatchPkgColorLabel";
            darkLabelPatchPkgColorLabel.Size = new System.Drawing.Size(55, 24);
            darkLabelPatchPkgColorLabel.TabIndex = 4;
            darkLabelPatchPkgColorLabel.Text = "Patch";
            darkLabelPatchPkgColorLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnPatchPkgForeColor
            // 
            btnPatchPkgForeColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPatchPkgForeColor.Location = new System.Drawing.Point(82, 90);
            btnPatchPkgForeColor.Name = "btnPatchPkgForeColor";
            btnPatchPkgForeColor.Size = new System.Drawing.Size(80, 24);
            btnPatchPkgForeColor.TabIndex = 5;
            btnPatchPkgForeColor.Text = "ForeColor";
            btnPatchPkgForeColor.Click += SetPKGLabelColor_Click;
            // 
            // btnPatchPkgBackColor
            // 
            btnPatchPkgBackColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPatchPkgBackColor.Location = new System.Drawing.Point(167, 90);
            btnPatchPkgBackColor.Name = "btnPatchPkgBackColor";
            btnPatchPkgBackColor.Size = new System.Drawing.Size(80, 24);
            btnPatchPkgBackColor.TabIndex = 6;
            btnPatchPkgBackColor.Text = "BackColor";
            btnPatchPkgBackColor.Click += SetPKGLabelColor_Click;
            // 
            // darkLabelAddonPkgColorLabel
            // 
            darkLabelAddonPkgColorLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelAddonPkgColorLabel.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabelAddonPkgColorLabel.Location = new System.Drawing.Point(287, 63);
            darkLabelAddonPkgColorLabel.Name = "darkLabelAddonPkgColorLabel";
            darkLabelAddonPkgColorLabel.Size = new System.Drawing.Size(55, 24);
            darkLabelAddonPkgColorLabel.TabIndex = 7;
            darkLabelAddonPkgColorLabel.Text = "Addon";
            darkLabelAddonPkgColorLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnAddonPkgForeColor
            // 
            btnAddonPkgForeColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnAddonPkgForeColor.Location = new System.Drawing.Point(347, 63);
            btnAddonPkgForeColor.Name = "btnAddonPkgForeColor";
            btnAddonPkgForeColor.Size = new System.Drawing.Size(80, 24);
            btnAddonPkgForeColor.TabIndex = 8;
            btnAddonPkgForeColor.Text = "ForeColor";
            btnAddonPkgForeColor.Click += SetPKGLabelColor_Click;
            // 
            // btnAddonPkgBackColor
            // 
            btnAddonPkgBackColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnAddonPkgBackColor.Location = new System.Drawing.Point(432, 63);
            btnAddonPkgBackColor.Name = "btnAddonPkgBackColor";
            btnAddonPkgBackColor.Size = new System.Drawing.Size(80, 24);
            btnAddonPkgBackColor.TabIndex = 9;
            btnAddonPkgBackColor.Text = "BackColor";
            btnAddonPkgBackColor.Click += SetPKGLabelColor_Click;
            // 
            // darkLabelAppPkgColorLabel
            // 
            darkLabelAppPkgColorLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelAppPkgColorLabel.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabelAppPkgColorLabel.Location = new System.Drawing.Point(287, 90);
            darkLabelAppPkgColorLabel.Name = "darkLabelAppPkgColorLabel";
            darkLabelAppPkgColorLabel.Size = new System.Drawing.Size(55, 24);
            darkLabelAppPkgColorLabel.TabIndex = 10;
            darkLabelAppPkgColorLabel.Text = "App";
            darkLabelAppPkgColorLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnAppPkgForeColor
            // 
            btnAppPkgForeColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnAppPkgForeColor.Location = new System.Drawing.Point(347, 90);
            btnAppPkgForeColor.Name = "btnAppPkgForeColor";
            btnAppPkgForeColor.Size = new System.Drawing.Size(80, 24);
            btnAppPkgForeColor.TabIndex = 11;
            btnAppPkgForeColor.Text = "ForeColor";
            btnAppPkgForeColor.Click += SetPKGLabelColor_Click;
            // 
            // btnAppPkgBackColor
            // 
            btnAppPkgBackColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnAppPkgBackColor.Location = new System.Drawing.Point(432, 90);
            btnAppPkgBackColor.Name = "btnAppPkgBackColor";
            btnAppPkgBackColor.Size = new System.Drawing.Size(80, 24);
            btnAppPkgBackColor.TabIndex = 12;
            btnAppPkgBackColor.Text = "BackColor";
            btnAppPkgBackColor.Click += SetPKGLabelColor_Click;
            // 
            // btnResetPkgLabelColor
            // 
            btnResetPkgLabelColor.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnResetPkgLabelColor.Location = new System.Drawing.Point(22, 122);
            btnResetPkgLabelColor.Name = "btnResetPkgLabelColor";
            btnResetPkgLabelColor.Size = new System.Drawing.Size(140, 24);
            btnResetPkgLabelColor.TabIndex = 13;
            btnResetPkgLabelColor.Text = "Reset all";
            btnResetPkgLabelColor.Click += SetPKGLabelColor_Click;
            // 
            // grpPS5BC
            // 
            grpPS5BC.Controls.Add(cbPs5BcCheck);
            grpPS5BC.Controls.Add(btnDownloadPS5BCJson);
            grpPS5BC.Controls.Add(darkLabel9);
            grpPS5BC.Controls.Add(labelPs5BcJsonDownloadDate);
            grpPS5BC.Location = new System.Drawing.Point(12, 307);
            grpPS5BC.Name = "grpPS5BC";
            grpPS5BC.SectionHeader = "PS5 Backward Compatibility";
            grpPS5BC.Size = new System.Drawing.Size(605, 93);
            grpPS5BC.TabIndex = 2;
            // 
            // cbPs5BcCheck
            // 
            cbPs5BcCheck.AutoSize = true;
            cbPs5BcCheck.Font = new System.Drawing.Font("Segoe UI", 9F);
            cbPs5BcCheck.ForeColor = System.Drawing.Color.Gainsboro;
            cbPs5BcCheck.Location = new System.Drawing.Point(15, 31);
            cbPs5BcCheck.Name = "cbPs5BcCheck";
            cbPs5BcCheck.Size = new System.Drawing.Size(224, 19);
            cbPs5BcCheck.TabIndex = 0;
            cbPs5BcCheck.Text = "Enable PS5 BC / PSVR / PS4 Pro check";
            // 
            // btnDownloadPS5BCJson
            // 
            btnDownloadPS5BCJson.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnDownloadPS5BCJson.Location = new System.Drawing.Point(15, 56);
            btnDownloadPS5BCJson.Name = "btnDownloadPS5BCJson";
            btnDownloadPS5BCJson.Size = new System.Drawing.Size(170, 26);
            btnDownloadPS5BCJson.TabIndex = 1;
            btnDownloadPS5BCJson.Text = "Download BC status data";
            btnDownloadPS5BCJson.Click += btnDownloadPS5BCJson_Click;
            // 
            // darkLabel9
            // 
            darkLabel9.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            darkLabel9.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel9.Location = new System.Drawing.Point(190, 61);
            darkLabel9.Name = "darkLabel9";
            darkLabel9.Size = new System.Drawing.Size(90, 18);
            darkLabel9.TabIndex = 2;
            darkLabel9.Text = "Last download:";
            // 
            // labelPs5BcJsonDownloadDate
            // 
            labelPs5BcJsonDownloadDate.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            labelPs5BcJsonDownloadDate.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            labelPs5BcJsonDownloadDate.Location = new System.Drawing.Point(285, 61);
            labelPs5BcJsonDownloadDate.Name = "labelPs5BcJsonDownloadDate";
            labelPs5BcJsonDownloadDate.Size = new System.Drawing.Size(220, 18);
            labelPs5BcJsonDownloadDate.TabIndex = 3;
            labelPs5BcJsonDownloadDate.Text = "..";
            // 
            // grpShadps4
            // 
            grpShadps4.Controls.Add(cbShadps4Check);
            grpShadps4.Controls.Add(btnDownloadShadps4Json);
            grpShadps4.Controls.Add(darkLabelShadps4);
            grpShadps4.Controls.Add(labelShadps4JsonDate);
            grpShadps4.Controls.Add(darkLabelShadps4Os);
            grpShadps4.Controls.Add(cmbShadps4Os);
            grpShadps4.Location = new System.Drawing.Point(12, 406);
            grpShadps4.Name = "grpShadps4";
            grpShadps4.SectionHeader = "shadPS4 Compatibility";
            grpShadps4.Size = new System.Drawing.Size(605, 93);
            grpShadps4.TabIndex = 3;
            // 
            // cbShadps4Check
            // 
            cbShadps4Check.AutoSize = true;
            cbShadps4Check.Font = new System.Drawing.Font("Segoe UI", 9F);
            cbShadps4Check.ForeColor = System.Drawing.Color.Gainsboro;
            cbShadps4Check.Location = new System.Drawing.Point(15, 31);
            cbShadps4Check.Name = "cbShadps4Check";
            cbShadps4Check.Size = new System.Drawing.Size(215, 19);
            cbShadps4Check.TabIndex = 0;
            cbShadps4Check.Text = "Enable shadPS4 compatibility check";
            // 
            // btnDownloadShadps4Json
            // 
            btnDownloadShadps4Json.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnDownloadShadps4Json.Location = new System.Drawing.Point(15, 56);
            btnDownloadShadps4Json.Name = "btnDownloadShadps4Json";
            btnDownloadShadps4Json.Size = new System.Drawing.Size(170, 26);
            btnDownloadShadps4Json.TabIndex = 1;
            btnDownloadShadps4Json.Text = "Download compat data";
            btnDownloadShadps4Json.Click += btnDownloadShadps4Json_Click;
            // 
            // darkLabelShadps4
            // 
            darkLabelShadps4.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            darkLabelShadps4.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabelShadps4.Location = new System.Drawing.Point(190, 61);
            darkLabelShadps4.Name = "darkLabelShadps4";
            darkLabelShadps4.Size = new System.Drawing.Size(90, 18);
            darkLabelShadps4.TabIndex = 2;
            darkLabelShadps4.Text = "Last download:";
            // 
            // labelShadps4JsonDate
            // 
            labelShadps4JsonDate.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            labelShadps4JsonDate.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            labelShadps4JsonDate.Location = new System.Drawing.Point(285, 61);
            labelShadps4JsonDate.Name = "labelShadps4JsonDate";
            labelShadps4JsonDate.Size = new System.Drawing.Size(220, 18);
            labelShadps4JsonDate.TabIndex = 3;
            labelShadps4JsonDate.Text = "..";
            //
            // darkLabelShadps4Os
            //
            darkLabelShadps4Os.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelShadps4Os.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabelShadps4Os.Location = new System.Drawing.Point(255, 33);
            darkLabelShadps4Os.Name = "darkLabelShadps4Os";
            darkLabelShadps4Os.Size = new System.Drawing.Size(28, 15);
            darkLabelShadps4Os.TabIndex = 4;
            darkLabelShadps4Os.Text = "OS:";
            //
            // cmbShadps4Os
            //
            cmbShadps4Os.Font = new System.Drawing.Font("Segoe UI", 9F);
            cmbShadps4Os.Items.AddRange(new object[] { "Windows", "Linux", "macOS" });
            cmbShadps4Os.Location = new System.Drawing.Point(290, 30);
            cmbShadps4Os.Name = "cmbShadps4Os";
            cmbShadps4Os.Size = new System.Drawing.Size(120, 24);
            cmbShadps4Os.TabIndex = 5;
            // 
            // tabTrophies
            // 
            tabTrophies.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabTrophies.Controls.Add(grpTrophyCache);
            tabTrophies.Location = new System.Drawing.Point(4, 32);
            tabTrophies.Name = "tabTrophies";
            tabTrophies.Size = new System.Drawing.Size(632, 452);
            tabTrophies.TabIndex = 4;
            tabTrophies.Text = "Trophies";
            // 
            // grpTrophyCache
            // 
            grpTrophyCache.Controls.Add(lblTrophyCacheDesc);
            grpTrophyCache.Controls.Add(btnBuildTrophyCache);
            grpTrophyCache.Controls.Add(btnCancelTrophyCache);
            grpTrophyCache.Controls.Add(btnClearTrophyCache);
            grpTrophyCache.Controls.Add(pbTrophyCacheProgress);
            grpTrophyCache.Controls.Add(lblTrophyCacheStatus);
            grpTrophyCache.Location = new System.Drawing.Point(12, 16);
            grpTrophyCache.Name = "grpTrophyCache";
            grpTrophyCache.SectionHeader = "Trophy Metadata Cache";
            grpTrophyCache.Size = new System.Drawing.Size(606, 205);
            grpTrophyCache.TabIndex = 0;
            // 
            // lblTrophyCacheDesc
            // 
            lblTrophyCacheDesc.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTrophyCacheDesc.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            lblTrophyCacheDesc.Location = new System.Drawing.Point(15, 32);
            lblTrophyCacheDesc.Name = "lblTrophyCacheDesc";
            lblTrophyCacheDesc.Size = new System.Drawing.Size(575, 38);
            lblTrophyCacheDesc.TabIndex = 0;
            lblTrophyCacheDesc.Text = "Extract NP Communication IDs from PKGs in the configured directories.\r\nCached IDs enable full trophy names and descriptions when a PKG is selected.";
            // 
            // btnBuildTrophyCache
            // 
            btnBuildTrophyCache.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnBuildTrophyCache.Location = new System.Drawing.Point(15, 78);
            btnBuildTrophyCache.Name = "btnBuildTrophyCache";
            btnBuildTrophyCache.Size = new System.Drawing.Size(210, 30);
            btnBuildTrophyCache.TabIndex = 1;
            btnBuildTrophyCache.Text = "Build Trophy Metadata Cache";
            btnBuildTrophyCache.Click += btnBuildTrophyCache_Click;
            // 
            // btnCancelTrophyCache
            // 
            btnCancelTrophyCache.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnCancelTrophyCache.Location = new System.Drawing.Point(235, 78);
            btnCancelTrophyCache.Name = "btnCancelTrophyCache";
            btnCancelTrophyCache.Size = new System.Drawing.Size(105, 30);
            btnCancelTrophyCache.TabIndex = 2;
            btnCancelTrophyCache.Text = "Cancel";
            btnCancelTrophyCache.Click += btnCancelTrophyCache_Click;
            // 
            // btnClearTrophyCache
            // 
            btnClearTrophyCache.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnClearTrophyCache.Location = new System.Drawing.Point(350, 78);
            btnClearTrophyCache.Name = "btnClearTrophyCache";
            btnClearTrophyCache.Size = new System.Drawing.Size(120, 30);
            btnClearTrophyCache.TabIndex = 3;
            btnClearTrophyCache.Text = "Clear Cache";
            btnClearTrophyCache.Click += btnClearTrophyCache_Click;
            // 
            // pbTrophyCacheProgress
            // 
            pbTrophyCacheProgress.Location = new System.Drawing.Point(15, 120);
            pbTrophyCacheProgress.Maximum = 1;
            pbTrophyCacheProgress.Name = "pbTrophyCacheProgress";
            pbTrophyCacheProgress.Size = new System.Drawing.Size(575, 20);
            pbTrophyCacheProgress.TabIndex = 4;
            // 
            // lblTrophyCacheStatus
            // 
            lblTrophyCacheStatus.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTrophyCacheStatus.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            lblTrophyCacheStatus.Location = new System.Drawing.Point(15, 150);
            lblTrophyCacheStatus.Name = "lblTrophyCacheStatus";
            lblTrophyCacheStatus.Size = new System.Drawing.Size(575, 42);
            lblTrophyCacheStatus.TabIndex = 5;
            lblTrophyCacheStatus.Text = "Cache not checked.";
            // 
            // tabRename
            // 
            tabRename.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabRename.Controls.Add(grpRename);
            tabRename.Location = new System.Drawing.Point(4, 32);
            tabRename.Name = "tabRename";
            tabRename.Size = new System.Drawing.Size(632, 452);
            tabRename.TabIndex = 3;
            tabRename.Text = "PKG Rename";
            // 
            // grpRename
            // 
            grpRename.Controls.Add(darkLabel12);
            grpRename.Controls.Add(tbCustomNamePattern);
            grpRename.Controls.Add(darkLabelPlaceholderHint);
            grpRename.Controls.Add(btnPlaceTitle);
            grpRename.Controls.Add(btnPlaceTitleId);
            grpRename.Controls.Add(btnPlaceVersion);
            grpRename.Controls.Add(btnPlaceAppVer);
            grpRename.Controls.Add(btnPlaceCategory);
            grpRename.Controls.Add(btnPlaceContentId);
            grpRename.Controls.Add(btnPlaceRegion);
            grpRename.Controls.Add(btnPlaceSysVer);
            grpRename.Controls.Add(darkButton1);
            grpRename.Controls.Add(darkLabelNamingPatternExample);
            grpRename.Location = new System.Drawing.Point(12, 16);
            grpRename.Name = "grpRename";
            grpRename.SectionHeader = "Custom Rename Format";
            grpRename.Size = new System.Drawing.Size(600, 285);
            grpRename.TabIndex = 0;
            // 
            // darkLabel12
            // 
            darkLabel12.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            darkLabel12.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel12.Location = new System.Drawing.Point(15, 32);
            darkLabel12.Name = "darkLabel12";
            darkLabel12.Size = new System.Drawing.Size(570, 20);
            darkLabel12.TabIndex = 0;
            darkLabel12.Text = "Format:  (files are renamed using this pattern when you Rename PKG)";
            // 
            // tbCustomNamePattern
            // 
            tbCustomNamePattern.Font = new System.Drawing.Font("Consolas", 10F);
            tbCustomNamePattern.Location = new System.Drawing.Point(15, 55);
            tbCustomNamePattern.Name = "tbCustomNamePattern";
            tbCustomNamePattern.Size = new System.Drawing.Size(570, 23);
            tbCustomNamePattern.TabIndex = 1;
            tbCustomNamePattern.TextChanged += tbCustomNamePattern_TextChanged;
            // 
            // darkLabelPlaceholderHint
            // 
            darkLabelPlaceholderHint.Font = new System.Drawing.Font("Segoe UI", 8F);
            darkLabelPlaceholderHint.ForeColor = System.Drawing.Color.FromArgb(160, 160, 160);
            darkLabelPlaceholderHint.Location = new System.Drawing.Point(15, 88);
            darkLabelPlaceholderHint.Name = "darkLabelPlaceholderHint";
            darkLabelPlaceholderHint.Size = new System.Drawing.Size(570, 16);
            darkLabelPlaceholderHint.TabIndex = 2;
            darkLabelPlaceholderHint.Text = "Click a button below to insert that value into the format:";
            // 
            // btnPlaceTitle
            // 
            btnPlaceTitle.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceTitle.Location = new System.Drawing.Point(15, 108);
            btnPlaceTitle.Name = "btnPlaceTitle";
            btnPlaceTitle.Size = new System.Drawing.Size(135, 26);
            btnPlaceTitle.TabIndex = 10;
            btnPlaceTitle.Tag = "{TITLE}";
            btnPlaceTitle.Text = "{TITLE}";
            btnPlaceTitle.Click += PlaceholderButton_Click;
            // 
            // btnPlaceTitleId
            // 
            btnPlaceTitleId.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceTitleId.Location = new System.Drawing.Point(160, 108);
            btnPlaceTitleId.Name = "btnPlaceTitleId";
            btnPlaceTitleId.Size = new System.Drawing.Size(135, 26);
            btnPlaceTitleId.TabIndex = 11;
            btnPlaceTitleId.Tag = "{TITLE_ID}";
            btnPlaceTitleId.Text = "{TITLE_ID}";
            btnPlaceTitleId.Click += PlaceholderButton_Click;
            // 
            // btnPlaceVersion
            // 
            btnPlaceVersion.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceVersion.Location = new System.Drawing.Point(305, 108);
            btnPlaceVersion.Name = "btnPlaceVersion";
            btnPlaceVersion.Size = new System.Drawing.Size(135, 26);
            btnPlaceVersion.TabIndex = 12;
            btnPlaceVersion.Tag = "{VERSION}";
            btnPlaceVersion.Text = "{VERSION}";
            btnPlaceVersion.Click += PlaceholderButton_Click;
            // 
            // btnPlaceAppVer
            // 
            btnPlaceAppVer.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceAppVer.Location = new System.Drawing.Point(450, 108);
            btnPlaceAppVer.Name = "btnPlaceAppVer";
            btnPlaceAppVer.Size = new System.Drawing.Size(135, 26);
            btnPlaceAppVer.TabIndex = 13;
            btnPlaceAppVer.Tag = "{APP_VERSION}";
            btnPlaceAppVer.Text = "{APP_VERSION}";
            btnPlaceAppVer.Click += PlaceholderButton_Click;
            // 
            // btnPlaceCategory
            // 
            btnPlaceCategory.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceCategory.Location = new System.Drawing.Point(15, 140);
            btnPlaceCategory.Name = "btnPlaceCategory";
            btnPlaceCategory.Size = new System.Drawing.Size(135, 26);
            btnPlaceCategory.TabIndex = 14;
            btnPlaceCategory.Tag = "{CATEGORY}";
            btnPlaceCategory.Text = "{CATEGORY}";
            btnPlaceCategory.Click += PlaceholderButton_Click;
            // 
            // btnPlaceContentId
            // 
            btnPlaceContentId.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceContentId.Location = new System.Drawing.Point(160, 140);
            btnPlaceContentId.Name = "btnPlaceContentId";
            btnPlaceContentId.Size = new System.Drawing.Size(135, 26);
            btnPlaceContentId.TabIndex = 15;
            btnPlaceContentId.Tag = "{CONTENT_ID}";
            btnPlaceContentId.Text = "{CONTENT_ID}";
            btnPlaceContentId.Click += PlaceholderButton_Click;
            // 
            // btnPlaceRegion
            // 
            btnPlaceRegion.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceRegion.Location = new System.Drawing.Point(305, 140);
            btnPlaceRegion.Name = "btnPlaceRegion";
            btnPlaceRegion.Size = new System.Drawing.Size(135, 26);
            btnPlaceRegion.TabIndex = 16;
            btnPlaceRegion.Tag = "{REGION}";
            btnPlaceRegion.Text = "{REGION}";
            btnPlaceRegion.Click += PlaceholderButton_Click;
            // 
            // btnPlaceSysVer
            // 
            btnPlaceSysVer.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPlaceSysVer.Location = new System.Drawing.Point(450, 140);
            btnPlaceSysVer.Name = "btnPlaceSysVer";
            btnPlaceSysVer.Size = new System.Drawing.Size(135, 26);
            btnPlaceSysVer.TabIndex = 17;
            btnPlaceSysVer.Tag = "{SYSTEM_VERSION}";
            btnPlaceSysVer.Text = "{SYSTEM_VERSION}";
            btnPlaceSysVer.Click += PlaceholderButton_Click;
            // 
            // darkButton1
            // 
            darkButton1.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkButton1.Location = new System.Drawing.Point(15, 176);
            darkButton1.Name = "darkButton1";
            darkButton1.Size = new System.Drawing.Size(80, 26);
            darkButton1.TabIndex = 18;
            darkButton1.Text = "Clear";
            darkButton1.Click += darkButton1_Click;
            // 
            // darkLabelNamingPatternExample
            // 
            darkLabelNamingPatternExample.Font = new System.Drawing.Font("Segoe UI", 10F);
            darkLabelNamingPatternExample.ForeColor = System.Drawing.Color.FromArgb(180, 220, 180);
            darkLabelNamingPatternExample.Location = new System.Drawing.Point(15, 213);
            darkLabelNamingPatternExample.Name = "darkLabelNamingPatternExample";
            darkLabelNamingPatternExample.Size = new System.Drawing.Size(570, 50);
            darkLabelNamingPatternExample.TabIndex = 5;
            // 
            // tabRPI
            // 
            tabRPI.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
            tabRPI.Controls.Add(grpNetwork);
            tabRPI.Controls.Add(grpTools);
            tabRPI.Location = new System.Drawing.Point(4, 32);
            tabRPI.Name = "tabRPI";
            tabRPI.Size = new System.Drawing.Size(632, 452);
            tabRPI.TabIndex = 2;
            tabRPI.Text = "Remote PKG Installer";
            // 
            // grpNetwork
            // 
            grpNetwork.Controls.Add(darkLabel1);
            grpNetwork.Controls.Add(darkComboBoxServerIP);
            grpNetwork.Controls.Add(darkLabel2);
            grpNetwork.Controls.Add(tbPS4IP);
            grpNetwork.Controls.Add(btnPingPs4);
            grpNetwork.Location = new System.Drawing.Point(12, 16);
            grpNetwork.Name = "grpNetwork";
            grpNetwork.SectionHeader = "Network";
            grpNetwork.Size = new System.Drawing.Size(601, 85);
            grpNetwork.TabIndex = 0;
            // 
            // darkLabel1
            // 
            darkLabel1.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabel1.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel1.Location = new System.Drawing.Point(35, 31);
            darkLabel1.Name = "darkLabel1";
            darkLabel1.Size = new System.Drawing.Size(100, 20);
            darkLabel1.TabIndex = 0;
            darkLabel1.Text = "PC IP address:";
            // 
            // darkComboBoxServerIP
            // 
            darkComboBoxServerIP.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkComboBoxServerIP.FormattingEnabled = true;
            darkComboBoxServerIP.Location = new System.Drawing.Point(35, 51);
            darkComboBoxServerIP.Name = "darkComboBoxServerIP";
            darkComboBoxServerIP.Size = new System.Drawing.Size(200, 24);
            darkComboBoxServerIP.TabIndex = 1;
            // 
            // darkLabel2
            // 
            darkLabel2.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabel2.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel2.Location = new System.Drawing.Point(341, 32);
            darkLabel2.Name = "darkLabel2";
            darkLabel2.Size = new System.Drawing.Size(100, 20);
            darkLabel2.TabIndex = 2;
            darkLabel2.Text = "PS4 IP address:";
            // 
            // tbPS4IP
            // 
            tbPS4IP.Font = new System.Drawing.Font("Segoe UI", 9F);
            tbPS4IP.Location = new System.Drawing.Point(341, 52);
            tbPS4IP.Name = "tbPS4IP";
            tbPS4IP.Size = new System.Drawing.Size(160, 23);
            tbPS4IP.TabIndex = 3;
            tbPS4IP.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // btnPingPs4
            // 
            btnPingPs4.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnPingPs4.Location = new System.Drawing.Point(511, 51);
            btnPingPs4.Name = "btnPingPs4";
            btnPingPs4.Size = new System.Drawing.Size(55, 24);
            btnPingPs4.TabIndex = 4;
            btnPingPs4.Text = "Ping";
            btnPingPs4.Click += btnPingPs4_Click;
            // 
            // grpTools
            // 
            grpTools.Controls.Add(darkLabel3);
            grpTools.Controls.Add(darkLabelNodejsInstalled);
            grpTools.Controls.Add(btnInstallNodejs);
            grpTools.Controls.Add(darkLabel4);
            grpTools.Controls.Add(darkLabelserveModuleInstalled);
            grpTools.Controls.Add(btnInstalleServerModule);
            grpTools.Location = new System.Drawing.Point(12, 107);
            grpTools.Name = "grpTools";
            grpTools.SectionHeader = "Tools";
            grpTools.Size = new System.Drawing.Size(601, 72);
            grpTools.TabIndex = 1;
            // 
            // darkLabel3
            // 
            darkLabel3.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabel3.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel3.Location = new System.Drawing.Point(35, 35);
            darkLabel3.Name = "darkLabel3";
            darkLabel3.Size = new System.Drawing.Size(70, 24);
            darkLabel3.TabIndex = 0;
            darkLabel3.Text = "Node.js";
            // 
            // darkLabelNodejsInstalled
            // 
            darkLabelNodejsInstalled.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelNodejsInstalled.ForeColor = System.Drawing.Color.Red;
            darkLabelNodejsInstalled.Location = new System.Drawing.Point(105, 35);
            darkLabelNodejsInstalled.Name = "darkLabelNodejsInstalled";
            darkLabelNodejsInstalled.Size = new System.Drawing.Size(30, 24);
            darkLabelNodejsInstalled.TabIndex = 1;
            darkLabelNodejsInstalled.Text = "✘";
            darkLabelNodejsInstalled.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnInstallNodejs
            // 
            btnInstallNodejs.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnInstallNodejs.Location = new System.Drawing.Point(140, 34);
            btnInstallNodejs.Name = "btnInstallNodejs";
            btnInstallNodejs.Size = new System.Drawing.Size(55, 26);
            btnInstallNodejs.TabIndex = 2;
            btnInstallNodejs.Text = "Install";
            btnInstallNodejs.Click += btnInstallNodejs_Click;
            // 
            // darkLabel4
            // 
            darkLabel4.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabel4.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel4.Location = new System.Drawing.Point(341, 34);
            darkLabel4.Name = "darkLabel4";
            darkLabel4.Size = new System.Drawing.Size(100, 24);
            darkLabel4.TabIndex = 3;
            darkLabel4.Text = "http-server";
            // 
            // darkLabelserveModuleInstalled
            // 
            darkLabelserveModuleInstalled.Font = new System.Drawing.Font("Segoe UI", 9F);
            darkLabelserveModuleInstalled.ForeColor = System.Drawing.Color.Red;
            darkLabelserveModuleInstalled.Location = new System.Drawing.Point(461, 34);
            darkLabelserveModuleInstalled.Name = "darkLabelserveModuleInstalled";
            darkLabelserveModuleInstalled.Size = new System.Drawing.Size(30, 24);
            darkLabelserveModuleInstalled.TabIndex = 4;
            darkLabelserveModuleInstalled.Text = "✘";
            darkLabelserveModuleInstalled.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnInstalleServerModule
            // 
            btnInstalleServerModule.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnInstalleServerModule.Location = new System.Drawing.Point(511, 33);
            btnInstalleServerModule.Name = "btnInstalleServerModule";
            btnInstalleServerModule.Size = new System.Drawing.Size(55, 26);
            btnInstalleServerModule.TabIndex = 5;
            btnInstalleServerModule.Text = "Install";
            btnInstalleServerModule.Click += btnInstallServerModule_Click;
            // 
            // btnSaveClose
            // 
            btnSaveClose.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnSaveClose.Location = new System.Drawing.Point(12, 569);
            btnSaveClose.Name = "btnSaveClose";
            btnSaveClose.Size = new System.Drawing.Size(640, 49);
            btnSaveClose.TabIndex = 1;
            btnSaveClose.Text = "Save & Close";
            btnSaveClose.Click += btnSaveClose_Click;
            // 
            // flatTabControl1
            // 
            flatTabControl1.AllowDrop = true;
            flatTabControl1.ItemSize = new System.Drawing.Size(120, 28);
            flatTabControl1.Location = new System.Drawing.Point(0, 0);
            flatTabControl1.Name = "flatTabControl1";
            flatTabControl1.Padding = new System.Drawing.Point(0, 0);
            flatTabControl1.SelectedIndex = 0;
            flatTabControl1.Size = new System.Drawing.Size(200, 100);
            flatTabControl1.TabIndex = 0;
            flatTabControl1.Visible = false;
            // 
            // darkLabel6
            // 
            darkLabel6.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel6.Location = new System.Drawing.Point(0, 0);
            darkLabel6.Name = "darkLabel6";
            darkLabel6.Size = new System.Drawing.Size(100, 23);
            darkLabel6.TabIndex = 0;
            darkLabel6.Visible = false;
            // 
            // darkLabel8
            // 
            darkLabel8.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel8.Location = new System.Drawing.Point(0, 0);
            darkLabel8.Name = "darkLabel8";
            darkLabel8.Size = new System.Drawing.Size(100, 23);
            darkLabel8.TabIndex = 0;
            darkLabel8.Visible = false;
            // 
            // darkLabel10
            // 
            darkLabel10.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel10.Location = new System.Drawing.Point(0, 0);
            darkLabel10.Name = "darkLabel10";
            darkLabel10.Size = new System.Drawing.Size(100, 23);
            darkLabel10.TabIndex = 0;
            darkLabel10.Visible = false;
            // 
            // darkLabel11
            // 
            darkLabel11.ForeColor = System.Drawing.Color.FromArgb(220, 220, 220);
            darkLabel11.Location = new System.Drawing.Point(0, 0);
            darkLabel11.Name = "darkLabel11";
            darkLabel11.Size = new System.Drawing.Size(100, 23);
            darkLabel11.TabIndex = 0;
            darkLabel11.Visible = false;
            // 
            // darkSectionPanel9
            // 
            darkSectionPanel9.Location = new System.Drawing.Point(0, 0);
            darkSectionPanel9.Name = "darkSectionPanel9";
            darkSectionPanel9.SectionHeader = null;
            darkSectionPanel9.Size = new System.Drawing.Size(200, 100);
            darkSectionPanel9.TabIndex = 0;
            darkSectionPanel9.Visible = false;
            // 
            // ProgramSetting
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(664, 629);
            Controls.Add(settingsTab);
            Controls.Add(btnSaveClose);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "ProgramSetting";
            SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Program Settings";
            Load += ProgramSetting_Load;
            settingsTab.ResumeLayout(false);
            tabGeneral.ResumeLayout(false);
            grpDirectories.ResumeLayout(false);
            grpDirectories.PerformLayout();
            grpDirList.ResumeLayout(false);
            grpDownloads.ResumeLayout(false);
            grpDownloads.PerformLayout();
            grpStartup.ResumeLayout(false);
            grpStartup.PerformLayout();
            tabAppearance.ResumeLayout(false);
            grpColumns.ResumeLayout(false);
            grpColumns.PerformLayout();
            grpColors.ResumeLayout(false);
            grpColors.PerformLayout();
            grpPS5BC.ResumeLayout(false);
            grpPS5BC.PerformLayout();
            grpShadps4.ResumeLayout(false);
            grpShadps4.PerformLayout();
            tabTrophies.ResumeLayout(false);
            grpTrophyCache.ResumeLayout(false);
            tabRename.ResumeLayout(false);
            grpRename.ResumeLayout(false);
            grpRename.PerformLayout();
            tabRPI.ResumeLayout(false);
            grpNetwork.ResumeLayout(false);
            grpNetwork.PerformLayout();
            grpTools.ResumeLayout(false);
            ResumeLayout(false);
        }

        private DarkUI.Controls.DarkTabControl settingsTab;
        private System.Windows.Forms.TabPage tabGeneral, tabAppearance, tabRPI, tabRename, tabTrophies;
        private DarkUI.Controls.DarkSectionPanel grpDirectories, grpDirList, grpDownloads, grpStartup;
        private DarkUI.Controls.DarkListBox lbPkgDirectoryList;
        private DarkUI.Controls.DarkCheckBox darkCheckBoxRecursive;
        private DarkUI.Controls.DarkButton btnAddPkgDirectory, btnDeletePkgDirectory, btnClearAllPkgDirectory;
        private DarkUI.Controls.DarkLabel darkLabel5;
        private DarkUI.Controls.DarkTextBox tbOfficialUpdateDownloadFolder;
        private DarkUI.Controls.DarkButton btnOfficialUpdateDownloadFolder;
        private DarkUI.Controls.DarkCheckBox BGM, AutoSortRow, cbAutoFetchUpdate;
        private DarkUI.Controls.DarkLabel lblTheme;
        private DarkUI.Controls.DarkComboBox cmbTheme;
        private DarkUI.Controls.DarkButton btnOpenAppData;
        private DarkUI.Controls.DarkSectionPanel grpColumns, grpColors, grpPS5BC, grpShadps4;
        private DarkUI.Controls.DarkCheckBox PKGname, TitleId, ContentId, Region, SystemFirmware, Version, PkgType, Category, Size, Location, cbBackported;
        private DarkUI.Controls.DarkCheckBox PKGColorLabeling;
        private DarkUI.Controls.DarkLabel darkLabelGamePkgColorLabel, darkLabelPatchPkgColorLabel, darkLabelAddonPkgColorLabel, darkLabelAppPkgColorLabel;
        private DarkUI.Controls.DarkButton btnGamePkgForeColor, btnGamePkgBackColor, btnPatchPkgForeColor, btnPatchPkgBackColor, btnAddonPkgForeColor, btnAddonPkgBackColor, btnAppPkgForeColor, btnAppPkgBackColor, btnResetPkgLabelColor;
        private DarkUI.Controls.DarkCheckBox cbPs5BcCheck;
        private DarkUI.Controls.DarkButton btnDownloadPS5BCJson;
        private DarkUI.Controls.DarkLabel darkLabel9, labelPs5BcJsonDownloadDate;
        private DarkUI.Controls.DarkCheckBox cbShadps4Check;
        private DarkUI.Controls.DarkLabel darkLabelShadps4Os;
        private DarkUI.Controls.DarkComboBox cmbShadps4Os;
        private DarkUI.Controls.DarkButton btnDownloadShadps4Json;
        private DarkUI.Controls.DarkLabel darkLabelShadps4, labelShadps4JsonDate;
        private DarkUI.Controls.DarkSectionPanel grpNetwork, grpTools;
        private DarkUI.Controls.DarkLabel darkLabel1, darkLabel2, darkLabel3, darkLabelNodejsInstalled, darkLabel4, darkLabelserveModuleInstalled;
        private DarkUI.Controls.DarkComboBox darkComboBoxServerIP;
        private DarkUI.Controls.DarkTextBox tbPS4IP;
        private DarkUI.Controls.DarkButton btnPingPs4, btnInstallNodejs, btnInstalleServerModule;
        private DarkUI.Controls.DarkSectionPanel grpRename;
        private DarkUI.Controls.DarkLabel darkLabel12, darkLabelPlaceholderHint, darkLabelNamingPatternExample;
        private DarkUI.Controls.DarkButton btnPlaceTitle, btnPlaceTitleId, btnPlaceVersion, btnPlaceAppVer, btnPlaceCategory, btnPlaceContentId, btnPlaceRegion, btnPlaceSysVer;
        private DarkUI.Controls.DarkButton darkButton1;
        private DarkUI.Controls.DarkTextBox tbCustomNamePattern;
        private DarkUI.Controls.DarkSectionPanel grpTrophyCache;
        private DarkUI.Controls.DarkLabel lblTrophyCacheDesc, lblTrophyCacheStatus;
        private DarkUI.Controls.DarkButton btnBuildTrophyCache, btnCancelTrophyCache, btnClearTrophyCache;
        private System.Windows.Forms.ProgressBar pbTrophyCacheProgress;
        private DarkUI.Controls.DarkButton btnSaveClose;
        private DarkUI.Controls.DarkTabControl flatTabControl1;
        private DarkUI.Controls.DarkLabel darkLabel6, darkLabel8, darkLabel10, darkLabel11;
        private DarkUI.Controls.DarkSectionPanel darkSectionPanel9;
    }
}
