using DarkUI.Controls;
using DarkUI.Forms;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace PS4PKGTool
{
    internal sealed record MissingBasePatch(
        string Filename,
        string Title,
        string TitleId,
        string Version,
        string Directory);

    /// <summary>Read-only report of loaded patch PKGs whose base-game PKG is absent.</summary>
    internal sealed class MissingBasePkgResultsForm : DarkForm
    {
        public MissingBasePkgResultsForm(int checkedPatchCount, IReadOnlyList<MissingBasePatch> missingPatches)
        {
            Icon = Utilities.PS4PKGToolHelper.Helper.AppIcon;
            Text = "Patches Missing Base PKG";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 360);
            Size = new Size(1050, 520);

            var summary = new DarkLabel
            {
                Dock = DockStyle.Top,
                Height = 42,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 220, 220),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0),
                Text = $"Checked {checkedPatchCount} patch PKG(s). {missingPatches.Count} base PKG(s) missing."
            };

            var table = new DataTable();
            table.Columns.Add("Status");
            table.Columns.Add("Patch PKG");
            table.Columns.Add("Title");
            table.Columns.Add("Title ID");
            table.Columns.Add("Patch Version");
            table.Columns.Add("Location");
            foreach (var patch in missingPatches)
                table.Rows.Add("Missing base", patch.Filename, patch.Title, patch.TitleId, patch.Version, patch.Directory);

            var grid = new DarkDataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                DataSource = table,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false
            };
            grid.Columns["Status"].FillWeight = 75;
            grid.Columns["Patch PKG"].FillWeight = 160;
            grid.Columns["Title"].FillWeight = 150;
            grid.Columns["Title ID"].FillWeight = 75;
            grid.Columns["Patch Version"].FillWeight = 85;
            grid.Columns["Location"].FillWeight = 220;

            var close = new DarkButton
            {
                Text = "Close",
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(80, 30),
                Location = new Point(ClientSize.Width - 92, 8)
            };
            close.Click += (_, _) => Close();

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 42 };
            footer.Controls.Add(close);
            footer.Resize += (_, _) => close.Left = footer.ClientSize.Width - close.Width - 12;

            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(summary);
        }
    }
}
