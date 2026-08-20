namespace PS4PKGTool
{
    partial class StagedPkgRecoveryForm
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
            lvItems = new DarkUI.Controls.DarkListView();
            lblCounts = new DarkUI.Controls.DarkLabel();
            btnRecoverSelected = new DarkUI.Controls.DarkButton();
            btnRecoverAll = new DarkUI.Controls.DarkButton();
            btnReview = new DarkUI.Controls.DarkButton();
            btnManualRecovery = new DarkUI.Controls.DarkButton();
            btnRemoveStale = new DarkUI.Controls.DarkButton();
            btnIgnore = new DarkUI.Controls.DarkButton();
            SuspendLayout();
            //
            // lblHeader
            //
            lblHeader.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblHeader.Location = new System.Drawing.Point(13, 12);
            lblHeader.Name = "lblHeader";
            lblHeader.Size = new System.Drawing.Size(759, 70);
            lblHeader.TabIndex = 0;
            lblHeader.Text = "PS4 PKG Tool found package files left from previous operations.\r\nThese are packages that were temporarily moved or renamed during an orbis-pub-cmd operation: orbis-pub-cmd only accepts ASCII (ANSI) paths, so packages with special characters (™, ：, Japanese, etc.) are staged as p4t_v_* / ps4pkgtool_orbis_*.pkg first. A crash, kill or power loss can leave them staged.\r\nOnly items with a known original path can be recovered automatically - everything else is shown for review.";
            //
            // lvItems
            //
            lvItems.Font = new System.Drawing.Font("Segoe UI", 9F);
            lvItems.FullRowSelect = true;
            lvItems.Location = new System.Drawing.Point(13, 90);
            lvItems.MultiSelect = true;
            lvItems.Name = "lvItems";
            lvItems.Size = new System.Drawing.Size(759, 274);
            lvItems.TabIndex = 1;
            lvItems.UseCompatibleStateImageBehavior = false;
            lvItems.View = System.Windows.Forms.View.Details;
            lvItems.Columns.Add("Title", 180);
            lvItems.Columns.Add("Title ID", 90);
            lvItems.Columns.Add("Type", 60);
            lvItems.Columns.Add("Size", 80);
            lvItems.Columns.Add("Status", 240);
            lvItems.Columns.Add("Current", 360);
            lvItems.Columns.Add("Original", 360);
            lvItems.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            lvItems.SelectedIndexChanged += lvItems_SelectedIndexChanged;
            //
            // lblCounts
            //
            lblCounts.Font = new System.Drawing.Font("Segoe UI", 9F);
            lblCounts.Location = new System.Drawing.Point(13, 373);
            lblCounts.Name = "lblCounts";
            lblCounts.Size = new System.Drawing.Size(400, 20);
            lblCounts.TabIndex = 2;
            lblCounts.Text = "0 staged item(s).";
            //
            // btnRecoverSelected
            //
            btnRecoverSelected.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnRecoverSelected.Location = new System.Drawing.Point(13, 400);
            btnRecoverSelected.Name = "btnRecoverSelected";
            btnRecoverSelected.Size = new System.Drawing.Size(125, 28);
            btnRecoverSelected.TabIndex = 3;
            btnRecoverSelected.Text = "Recover Selected";
            btnRecoverSelected.Click += btnRecoverSelected_Click;
            //
            // btnRecoverAll
            //
            btnRecoverAll.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnRecoverAll.Location = new System.Drawing.Point(144, 400);
            btnRecoverAll.Name = "btnRecoverAll";
            btnRecoverAll.Size = new System.Drawing.Size(125, 28);
            btnRecoverAll.TabIndex = 4;
            btnRecoverAll.Text = "Recover All Safe";
            btnRecoverAll.Click += btnRecoverAll_Click;
            //
            // btnReview
            //
            btnReview.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnReview.Location = new System.Drawing.Point(275, 400);
            btnReview.Name = "btnReview";
            btnReview.Size = new System.Drawing.Size(90, 28);
            btnReview.TabIndex = 5;
            btnReview.Text = "Review";
            btnReview.Click += btnReview_Click;
            //
            // btnManualRecovery
            //
            btnManualRecovery.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnManualRecovery.Location = new System.Drawing.Point(371, 400);
            btnManualRecovery.Name = "btnManualRecovery";
            btnManualRecovery.Size = new System.Drawing.Size(125, 28);
            btnManualRecovery.TabIndex = 6;
            btnManualRecovery.Text = "Manual Recovery...";
            btnManualRecovery.Click += btnManualRecovery_Click;
            //
            // btnRemoveStale
            //
            btnRemoveStale.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnRemoveStale.Location = new System.Drawing.Point(502, 400);
            btnRemoveStale.Name = "btnRemoveStale";
            btnRemoveStale.Size = new System.Drawing.Size(174, 28);
            btnRemoveStale.TabIndex = 7;
            btnRemoveStale.Text = "Remove Stale Recovery Record";
            btnRemoveStale.Click += btnRemoveStale_Click;
            //
            // btnIgnore
            //
            btnIgnore.Font = new System.Drawing.Font("Segoe UI", 9F);
            btnIgnore.Location = new System.Drawing.Point(682, 400);
            btnIgnore.Name = "btnIgnore";
            btnIgnore.Size = new System.Drawing.Size(90, 28);
            btnIgnore.TabIndex = 8;
            btnIgnore.Text = "Ignore";
            btnIgnore.Click += btnIgnore_Click;
            //
            // StagedPkgRecoveryForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(784, 441);
            Controls.Add(btnIgnore);
            Controls.Add(btnRemoveStale);
            Controls.Add(btnManualRecovery);
            Controls.Add(btnReview);
            Controls.Add(btnRecoverAll);
            Controls.Add(btnRecoverSelected);
            Controls.Add(lblCounts);
            Controls.Add(lvItems);
            Controls.Add(lblHeader);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "StagedPkgRecoveryForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Staged PKGs found";
            ResumeLayout(false);
            PerformLayout();
        }

        private DarkUI.Controls.DarkLabel lblHeader;
        private DarkUI.Controls.DarkListView lvItems;
        private DarkUI.Controls.DarkLabel lblCounts;
        private DarkUI.Controls.DarkButton btnRecoverSelected;
        private DarkUI.Controls.DarkButton btnRecoverAll;
        private DarkUI.Controls.DarkButton btnReview;
        private DarkUI.Controls.DarkButton btnManualRecovery;
        private DarkUI.Controls.DarkButton btnRemoveStale;
        private DarkUI.Controls.DarkButton btnIgnore;
    }
}
