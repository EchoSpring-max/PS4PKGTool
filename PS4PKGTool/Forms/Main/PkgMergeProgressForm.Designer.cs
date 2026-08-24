namespace PS4PKGTool
{
    internal sealed partial class PkgMergeProgressForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.FlowLayoutPanel footerPanel;
        private DarkUI.Controls.DarkLabel _stage;
        private DarkUI.Controls.DarkLabel _detail;
        private DarkUI.Controls.DarkProgressBar _progress;
        private DarkUI.Controls.DarkButton _cancel;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            footerPanel = new System.Windows.Forms.FlowLayoutPanel();
            _cancel = new DarkUI.Controls.DarkButton();
            _progress = new DarkUI.Controls.DarkProgressBar();
            _detail = new DarkUI.Controls.DarkLabel();
            _stage = new DarkUI.Controls.DarkLabel();
            footerPanel.SuspendLayout();
            SuspendLayout();
            // 
            // footerPanel
            // 
            footerPanel.Controls.Add(_cancel);
            footerPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            footerPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            footerPanel.Location = new System.Drawing.Point(0, 101);
            footerPanel.Name = "footerPanel";
            footerPanel.Padding = new System.Windows.Forms.Padding(12, 7, 12, 7);
            footerPanel.Size = new System.Drawing.Size(560, 44);
            footerPanel.TabIndex = 0;
            // 
            // _cancel
            // 
            _cancel.Location = new System.Drawing.Point(449, 10);
            _cancel.Name = "_cancel";
            _cancel.Size = new System.Drawing.Size(84, 30);
            _cancel.TabIndex = 0;
            _cancel.Text = "Cancel";
            _cancel.Click += cancel_Click;
            // 
            // _progress
            // 
            _progress.Location = new System.Drawing.Point(12, 66);
            _progress.MarqueeAnimationSpeed = 25;
            _progress.Name = "_progress";
            _progress.Size = new System.Drawing.Size(536, 18);
            _progress.TabIndex = 1;
            _progress.TextMode = DarkUI.Controls.DarkProgressBarMode.NoText;
            // 
            // _detail
            // 
            _detail.AutoEllipsis = true;
            _detail.Dock = System.Windows.Forms.DockStyle.Top;
            _detail.Location = new System.Drawing.Point(0, 28);
            _detail.Name = "_detail";
            _detail.Padding = new System.Windows.Forms.Padding(12, 2, 12, 0);
            _detail.Size = new System.Drawing.Size(560, 38);
            _detail.TabIndex = 2;
            // 
            // _stage
            // 
            _stage.Dock = System.Windows.Forms.DockStyle.Top;
            _stage.Location = new System.Drawing.Point(0, 0);
            _stage.Name = "_stage";
            _stage.Padding = new System.Windows.Forms.Padding(12, 6, 12, 0);
            _stage.Size = new System.Drawing.Size(560, 28);
            _stage.TabIndex = 3;
            // 
            // PkgMergeProgressForm
            // 
            ClientSize = new System.Drawing.Size(560, 145);
            ControlBox = false;
            Controls.Add(footerPanel);
            Controls.Add(_progress);
            Controls.Add(_detail);
            Controls.Add(_stage);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Icon = Properties.Resources.PackageIcon;
            Name = "PkgMergeProgressForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Merging PKGs";
            Shown += PkgMergeProgressForm_Shown;
            footerPanel.ResumeLayout(false);
            ResumeLayout(false);
        }
    }
}
