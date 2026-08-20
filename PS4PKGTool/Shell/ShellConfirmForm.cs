using System;
using System.Windows.Forms;
using PS4PKGTool.Utilities.PS4PKGToolHelper;

namespace PS4PKGTool.Shell
{
    /// <summary>
    /// Tiny transient confirmation for lightweight shell actions (copy,
    /// rename). Modal so it works without a persistent message pump;
    /// auto-closes after about two seconds or on click.
    /// </summary>
    public partial class ShellConfirmForm : DarkUI.Forms.DarkForm
    {
        public ShellConfirmForm(string message)
        {
            InitializeComponent();
            Icon = Helper.AppIcon;
            lblShellConfirm.Text = message;

            var timer = new System.Windows.Forms.Timer { Interval = 2200 };
            timer.Tick += (_, _) => { timer.Stop(); DialogResult = DialogResult.OK; Close(); };
            timer.Start();

            Click += (_, _) => Close();
            lblShellConfirm.Click += (_, _) => Close();
        }
    }
}
