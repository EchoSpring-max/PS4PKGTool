using System;
using System.Windows.Forms;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Shell
{
    /// <summary>
    /// Small input dialog for the custom rename format (the same placeholder
    /// language as the main app: {TITLE}, {TITLE_ID}, {CONTENT_ID}, ...).
    /// </summary>
    public partial class ShellRenameDialog : DarkUI.Forms.DarkForm
    {
        public string FormatValue { get; private set; } = "";

        public ShellRenameDialog(string initial)
        {
            InitializeComponent();
            txtShellFormat.Text = initial ?? "";
            txtShellFormat.SelectAll();
        }

        private void btnShellOk_Click(object sender, EventArgs e)
        {
            FormatValue = txtShellFormat.Text.Trim();
            if (string.IsNullOrEmpty(FormatValue))
            {
                MessageBoxHelper.ShowWarning("The custom format cannot be empty.", false);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnShellCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
