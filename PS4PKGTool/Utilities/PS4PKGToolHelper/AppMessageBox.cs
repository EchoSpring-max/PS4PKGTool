using System;
using System.Drawing;
using System.Windows.Forms;

namespace PS4PKGTool.Utilities.PS4PKGToolHelper
{
    public enum AppMessageType { Info, Warning, Error }
    public enum AppMessageButtons { OK, YesNo, YesNoCancel }

    public partial class AppMessageBox : DarkUI.Forms.DarkForm
    {
        public DialogResult Result { get; private set; } = DialogResult.None;

        public AppMessageBox(string title, string message, AppMessageType type, AppMessageButtons buttons)
        {
            InitializeComponent();

            this.Text = title;
            darkLabelTitle.Text = title;
            darkLabelMessage.Text = message;
            darkLabelMessage.MaximumSize = new Size(420, 0);

            // Type → accent color + notification sound. The sound plays only
            // for these notification dialogs (DarkForm plays it on modal
            // open when NotificationIcon is set) - plain forms never set
            // it, so opening Settings etc. stays silent.
            Color accent;
            switch (type)
            {
                case AppMessageType.Error:
                    accent = Color.FromArgb(220, 80, 80);
                    NotificationIcon = MessageBoxIcon.Error;    // SystemSounds.Hand
                    break;
                case AppMessageType.Warning:
                    accent = Color.FromArgb(220, 180, 60);
                    NotificationIcon = MessageBoxIcon.Warning;  // SystemSounds.Exclamation
                    break;
                default:
                    accent = Color.FromArgb(100, 160, 220);
                    NotificationIcon = MessageBoxIcon.Information; // SystemSounds.Asterisk
                    break;
            }
            darkLabelTitle.ForeColor = accent;

            // Auto-size: measure the text and resize the form
            using (Graphics g = darkLabelMessage.CreateGraphics())
            {
                SizeF size = g.MeasureString(message, darkLabelMessage.Font, 420);
                int neededHeight = (int)Math.Ceiling(size.Height) + 90;
                if (neededHeight < 130) neededHeight = 130;
                if (neededHeight > 500) neededHeight = 500;
                this.ClientSize = new Size(460, neededHeight);
                darkLabelMessage.Size = new Size(420, neededHeight - 85);
            }

            // Show Copy button on all dialog types
            btnCopy.Visible = true;
            btnCopy.Location = new Point(20, this.ClientSize.Height - 42);

            // Show/hide buttons
            switch (buttons)
            {
                case AppMessageButtons.OK:
                    btnOK.Visible = true;
                    btnOK.Location = new Point((this.ClientSize.Width - 100) / 2, this.ClientSize.Height - 42);
                    break;
                case AppMessageButtons.YesNo:
                    btnYes.Visible = true; btnNo.Visible = true;
                    btnYes.Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 42);
                    btnNo.Location = new Point(this.ClientSize.Width - 110, this.ClientSize.Height - 42);
                    break;
                case AppMessageButtons.YesNoCancel:
                    btnYes.Visible = true; btnNo.Visible = true; btnCancel.Visible = true;
                    btnYes.Location = new Point(this.ClientSize.Width - 330, this.ClientSize.Height - 42);
                    btnNo.Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 42);
                    btnCancel.Location = new Point(this.ClientSize.Width - 110, this.ClientSize.Height - 42);
                    break;
            }

            this.ActiveControl = btnOK.Visible ? btnOK : btnYes;
        }

        private void btnOK_Click(object sender, EventArgs e) { Result = DialogResult.OK; Close(); }
        private void btnYes_Click(object sender, EventArgs e) { Result = DialogResult.Yes; Close(); }
        private void btnNo_Click(object sender, EventArgs e) { Result = DialogResult.No; Close(); }
        private void btnCancel_Click(object sender, EventArgs e) { Result = DialogResult.Cancel; Close(); }
        private void btnCopy_Click(object sender, EventArgs e)
        {
            var text = $"{darkLabelTitle.Text}\r\n{darkLabelMessage.Text}";
            Clipboard.SetText(text);
            btnCopy.Text = "Copied!";
        }

        public static DialogResult Show(string title, string message, AppMessageType type, AppMessageButtons buttons)
        {
            using (var dlg = new AppMessageBox(title, message, type, buttons))
            {
                dlg.ShowDialog();
                return dlg.Result;
            }
        }
    }
}
