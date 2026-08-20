namespace PS4PKGTool
{
    partial class ManualRecoveryForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            lblHeader = new DarkUI.Controls.DarkLabel();
            lblCurrentCaption = new DarkUI.Controls.DarkLabel();
            lblCurrent = new DarkUI.Controls.DarkLabel();
            lblTitleCaption = new DarkUI.Controls.DarkLabel();
            lblTitle = new DarkUI.Controls.DarkLabel();
            lblTitleIdCaption = new DarkUI.Controls.DarkLabel();
            lblTitleId = new DarkUI.Controls.DarkLabel();
            lblTypeCaption = new DarkUI.Controls.DarkLabel();
            lblType = new DarkUI.Controls.DarkLabel();
            lblSizeCaption = new DarkUI.Controls.DarkLabel();
            lblSize = new DarkUI.Controls.DarkLabel();
            lblFolderCaption = new DarkUI.Controls.DarkLabel();
            tbFolder = new DarkUI.Controls.DarkTextBox();
            btnBrowse = new DarkUI.Controls.DarkButton();
            lblFilenameCaption = new DarkUI.Controls.DarkLabel();
            tbFilename = new DarkUI.Controls.DarkTextBox();
            btnOk = new DarkUI.Controls.DarkButton();
            btnCancel = new DarkUI.Controls.DarkButton();
            SuspendLayout();
            //
            // lblHeader
            //
            lblHeader.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblHeader.Location = new System.Drawing.Point(13, 12);
            lblHeader.Name = "lblHeader";
            lblHeader.Size = new System.Drawing.Size(580, 52);
            lblHeader.TabIndex = 0;
            lblHeader.Text = "The original filename of this staged package was not recorded.\r\nWhy it is staged: orbis-pub-cmd only accepts ASCII (ANSI) paths, so packages with special characters are temporarily renamed before use.\r\nChoose the destination folder and filename yourself.";
            //
            // lblCurrentCaption
            //
            lblCurrentCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblCurrentCaption.Location = new System.Drawing.Point(13, 76);
            lblCurrentCaption.Name = "lblCurrentCaption";
            lblCurrentCaption.Size = new System.Drawing.Size(90, 18);
            lblCurrentCaption.TabIndex = 1;
            lblCurrentCaption.Text = "Current package:";
            //
            // lblCurrent
            //
            lblCurrent.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblCurrent.Location = new System.Drawing.Point(109, 76);
            lblCurrent.Name = "lblCurrent";
            lblCurrent.Size = new System.Drawing.Size(484, 18);
            lblCurrent.TabIndex = 2;
            lblCurrent.Text = "-";
            //
            // lblTitleCaption
            //
            lblTitleCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTitleCaption.Location = new System.Drawing.Point(13, 100);
            lblTitleCaption.Name = "lblTitleCaption";
            lblTitleCaption.Size = new System.Drawing.Size(90, 18);
            lblTitleCaption.TabIndex = 3;
            lblTitleCaption.Text = "Title:";
            //
            // lblTitle
            //
            lblTitle.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTitle.Location = new System.Drawing.Point(109, 100);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new System.Drawing.Size(484, 18);
            lblTitle.TabIndex = 4;
            lblTitle.Text = "-";
            //
            // lblTitleIdCaption
            //
            lblTitleIdCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTitleIdCaption.Location = new System.Drawing.Point(13, 124);
            lblTitleIdCaption.Name = "lblTitleIdCaption";
            lblTitleIdCaption.Size = new System.Drawing.Size(90, 18);
            lblTitleIdCaption.TabIndex = 5;
            lblTitleIdCaption.Text = "Title ID:";
            //
            // lblTitleId
            //
            lblTitleId.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTitleId.Location = new System.Drawing.Point(109, 124);
            lblTitleId.Name = "lblTitleId";
            lblTitleId.Size = new System.Drawing.Size(200, 18);
            lblTitleId.TabIndex = 6;
            lblTitleId.Text = "-";
            //
            // lblTypeCaption
            //
            lblTypeCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblTypeCaption.Location = new System.Drawing.Point(330, 124);
            lblTypeCaption.Name = "lblTypeCaption";
            lblTypeCaption.Size = new System.Drawing.Size(50, 18);
            lblTypeCaption.TabIndex = 7;
            lblTypeCaption.Text = "Type:";
            //
            // lblType
            //
            lblType.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblType.Location = new System.Drawing.Point(386, 124);
            lblType.Name = "lblType";
            lblType.Size = new System.Drawing.Size(100, 18);
            lblType.TabIndex = 8;
            lblType.Text = "-";
            //
            // lblSizeCaption
            //
            lblSizeCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblSizeCaption.Location = new System.Drawing.Point(500, 124);
            lblSizeCaption.Name = "lblSizeCaption";
            lblSizeCaption.Size = new System.Drawing.Size(40, 18);
            lblSizeCaption.TabIndex = 9;
            lblSizeCaption.Text = "Size:";
            //
            // lblSize
            //
            lblSize.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblSize.Location = new System.Drawing.Point(546, 124);
            lblSize.Name = "lblSize";
            lblSize.Size = new System.Drawing.Size(60, 18);
            lblSize.TabIndex = 10;
            lblSize.Text = "-";
            //
            // lblFolderCaption
            //
            lblFolderCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblFolderCaption.Location = new System.Drawing.Point(13, 158);
            lblFolderCaption.Name = "lblFolderCaption";
            lblFolderCaption.Size = new System.Drawing.Size(120, 18);
            lblFolderCaption.TabIndex = 11;
            lblFolderCaption.Text = "Destination folder:";
            //
            // tbFolder
            //
            tbFolder.Font = new System.Drawing.Font("Segoe UI", 9F);
            tbFolder.Location = new System.Drawing.Point(139, 155);
            tbFolder.Name = "tbFolder";
            tbFolder.Size = new System.Drawing.Size(360, 23);
            tbFolder.TabIndex = 12;
            //
            // btnBrowse
            //
            btnBrowse.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnBrowse.Location = new System.Drawing.Point(508, 153);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Size = new System.Drawing.Size(85, 27);
            btnBrowse.TabIndex = 13;
            btnBrowse.Text = "Browse...";
            btnBrowse.Click += btnBrowse_Click;
            //
            // lblFilenameCaption
            //
            lblFilenameCaption.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblFilenameCaption.Location = new System.Drawing.Point(13, 194);
            lblFilenameCaption.Name = "lblFilenameCaption";
            lblFilenameCaption.Size = new System.Drawing.Size(120, 18);
            lblFilenameCaption.TabIndex = 14;
            lblFilenameCaption.Text = "Filename:";
            //
            // tbFilename
            //
            tbFilename.Font = new System.Drawing.Font("Segoe UI", 9F);
            tbFilename.Location = new System.Drawing.Point(139, 191);
            tbFilename.Name = "tbFilename";
            tbFilename.Size = new System.Drawing.Size(360, 23);
            tbFilename.TabIndex = 15;
            //
            // btnOk
            //
            btnOk.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnOk.Location = new System.Drawing.Point(413, 232);
            btnOk.Name = "btnOk";
            btnOk.Size = new System.Drawing.Size(90, 28);
            btnOk.TabIndex = 16;
            btnOk.Text = "Move / Rename";
            btnOk.Click += btnOk_Click;
            //
            // btnCancel
            //
            btnCancel.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnCancel.Location = new System.Drawing.Point(509, 232);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new System.Drawing.Size(84, 28);
            btnCancel.TabIndex = 17;
            btnCancel.Text = "Cancel";
            //
            // ManualRecoveryForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(606, 272);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);
            Controls.Add(tbFilename);
            Controls.Add(lblFilenameCaption);
            Controls.Add(btnBrowse);
            Controls.Add(tbFolder);
            Controls.Add(lblFolderCaption);
            Controls.Add(lblSize);
            Controls.Add(lblSizeCaption);
            Controls.Add(lblType);
            Controls.Add(lblTypeCaption);
            Controls.Add(lblTitleId);
            Controls.Add(lblTitleIdCaption);
            Controls.Add(lblTitle);
            Controls.Add(lblTitleCaption);
            Controls.Add(lblCurrent);
            Controls.Add(lblCurrentCaption);
            Controls.Add(lblHeader);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ManualRecoveryForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Manual Recovery";
            ResumeLayout(false);
            PerformLayout();
        }

        private DarkUI.Controls.DarkLabel lblHeader;
        private DarkUI.Controls.DarkLabel lblCurrentCaption, lblCurrent;
        private DarkUI.Controls.DarkLabel lblTitleCaption, lblTitle;
        private DarkUI.Controls.DarkLabel lblTitleIdCaption, lblTitleId;
        private DarkUI.Controls.DarkLabel lblTypeCaption, lblType;
        private DarkUI.Controls.DarkLabel lblSizeCaption, lblSize;
        private DarkUI.Controls.DarkLabel lblFolderCaption, lblFilenameCaption;
        private DarkUI.Controls.DarkTextBox tbFolder, tbFilename;
        private DarkUI.Controls.DarkButton btnBrowse, btnOk, btnCancel;
    }
}
