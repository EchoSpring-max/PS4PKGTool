using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PS4PKGTool.Controls
{
    /// <summary>
    /// A checkbox-list dropdown popup for the filter bar aspects.
    /// Checked items are the selected values; Apply pushes them back,
    /// Clear empties the selection. Closes on deactivation.
    /// </summary>
    public sealed class FilterCheckPopup : ToolStripDropDown
    {
        private readonly CheckedListBox _list;
        private readonly Action<List<string>> _onApply;

        public FilterCheckPopup(IEnumerable<string> options, IEnumerable<string> selected, Action<List<string>> onApply)
        {
            _onApply = onApply;
            AutoSize = false;

            var host = new ToolStripControlHost(new Panel { BackColor = Color.FromArgb(60, 63, 65), Width = 210, Height = 260 })
            {
                Margin = Padding.Empty,
            };
            Items.Add(host);
            var panel = (Panel)host.Control;

            _list = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = 210,
                BackColor = Color.FromArgb(60, 63, 65),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
            };
            foreach (string o in options)
                _list.Items.Add(o);
            foreach (string s in selected)
            {
                int idx = _list.Items.IndexOf(s);
                if (idx >= 0) _list.SetItemChecked(idx, true);
            }
            panel.Controls.Add(_list);

            var btnApply = new Button
            {
                Text = "Apply",
                Width = 90,
                Height = 26,
                Left = 4,
                Top = 218,
                BackColor = Color.FromArgb(80, 90, 110),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            btnApply.Click += (_, _) => CloseAndApply();
            panel.Controls.Add(btnApply);

            var btnClear = new Button
            {
                Text = "Clear",
                Width = 90,
                Height = 26,
                Left = 104,
                Top = 218,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            btnClear.Click += (_, _) =>
            {
                for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, false);
                CloseAndApply();
            };
            panel.Controls.Add(btnClear);
        }

        private void CloseAndApply()
        {
            _onApply(_list.CheckedItems.Cast<string>().ToList());
            Close();
        }
    }
}
