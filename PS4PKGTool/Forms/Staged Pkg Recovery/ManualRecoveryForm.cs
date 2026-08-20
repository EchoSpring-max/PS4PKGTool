using PS4PKGTool.Utilities.PS4PKGToolHelper;
using System;
using System.IO;
using System.Windows.Forms;

namespace PS4PKGTool
{
    /// <summary>
    /// Manual recovery for a standalone legacy staged PKG whose original
    /// filename was never recorded. The USER chooses the destination folder
    /// and filename explicitly - the app never invents a name from the PKG
    /// title. The filename field is deliberately blank.
    /// </summary>
    public partial class ManualRecoveryForm : DarkUI.Forms.DarkForm
    {
        private readonly StagedPkgRecoveryItem _item;

        public ManualRecoveryForm(StagedPkgRecoveryItem item)
        {
            InitializeComponent();
            Icon = Helper.AppIcon;
            _item = item;
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            lblCurrent.Text = item.CurrentPath;
            lblTitle.Text = item.Title.Length > 0 ? item.Title : "(unknown)";
            lblTitleId.Text = item.TitleId.Length > 0 ? item.TitleId : "(unknown)";
            lblType.Text = item.PkgType.Length > 0 ? item.PkgType : "(unknown)";
            lblSize.Text = item.Size > 0 ? Helper.RoundBytes(item.Size) : "(unknown)";

            // Suggest the folder that holds the staged PKG, but never a name.
            string folder = Path.GetDirectoryName(item.CurrentPath) ?? "";
            tbFolder.Text = folder;
        }

        public string? ChosenDestinationPath { get; private set; }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose the destination folder for the recovered package",
                ShowNewFolderButton = true,
            };
            if (tbFolder.Text.Length > 0 && Directory.Exists(tbFolder.Text))
                dialog.SelectedPath = tbFolder.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                tbFolder.Text = dialog.SelectedPath;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            string folder;
            try { folder = string.IsNullOrWhiteSpace(tbFolder.Text) ? "" : Path.GetFullPath(tbFolder.Text.Trim()); }
            catch (Exception ex) { ShowWarning("The destination folder is invalid:\n" + ex.Message, false); return; }

            string name = tbFilename.Text.Trim();
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                ShowWarning("Choose an existing destination folder.", false);
                return;
            }
            if (name.Length == 0)
            {
                ShowWarning("Enter a destination file name (the original name was not recorded).", false);
                return;
            }
            if (!name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            {
                ShowWarning("The destination file name must end with .pkg.", false);
                return;
            }

            string target;
            try { target = Path.GetFullPath(Path.Combine(folder, name)); }
            catch (Exception ex) { ShowWarning("The destination path is invalid:\n" + ex.Message, false); return; }

            if (OrbisTempRecovery.IsStagingArtifact(target))
            {
                ShowWarning("The destination must not be inside a p4t_v_* staging directory\n" +
                    "or use a ps4pkgtool_orbis_*.pkg name.", false);
                return;
            }
            if (!File.Exists(_item.CurrentPath))
            {
                ShowWarning("The staged package no longer exists at:\n" + _item.CurrentPath, false);
                return;
            }
            if (OrbisSafePkgOperation.IsPathInActiveOperation(_item.CurrentPath))
            {
                ShowWarning("The staged package is in use by an active operation.", false);
                return;
            }
            if (File.Exists(target))
            {
                ShowWarning("The destination already exists - it will not be overwritten:\n" + target, false);
                return;
            }

            ChosenDestinationPath = target;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
