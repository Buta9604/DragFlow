using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>
    /// Batch rename with ready-made patterns that fit the selection (photo dates, the shared name start, the folder
    /// name), name clean-up styles, and a live preview of every new name with clashes marked in red.
    /// </summary>
    public sealed class RenameDialog : Form
    {
        readonly IList<string> paths;
        readonly ComboBox pattern = new ComboBox();
        readonly ComboBox style = new ComboBox();
        readonly ListView preview = new ListView();
        readonly Label status = new Label();
        readonly Button ok = new Button();
        List<KeyValuePair<string, string>> plan;

        static readonly string[] StyleNames = { "Keep as typed", "Clean up (spaces, no _ or .)", "lower case", "UPPER CASE", "Title Case" };

        public static List<KeyValuePair<string, string>> Ask(IList<string> paths)
        {
            using (var d = new RenameDialog(paths))
                return d.ShowDialog() == DialogResult.OK ? d.plan : null;
        }

        RenameDialog(IList<string> paths)
        {
            this.paths = paths;
            Text = "Rename " + paths.Count + " items";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; ShowInTaskbar = true; TopMost = true;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(620, 470);
            MinimumSize = new Size(480, 360);

            var l1 = new Label { Text = "New names", Left = 16, Top = 14, AutoSize = true };
            pattern.SetBounds(16, 36, 380, 26);
            pattern.DropDownStyle = ComboBoxStyle.DropDown;
            pattern.Items.AddRange(FileOps.SuggestPatterns(paths).Cast<object>().ToArray());
            pattern.Text = (string)pattern.Items[0];
            pattern.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            style.SetBounds(408, 36, 196, 26);
            style.DropDownStyle = ComboBoxStyle.DropDownList;
            style.Items.AddRange(StyleNames.Cast<object>().ToArray());
            style.SelectedIndex = 0;
            style.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var help = new Label
            {
                Text = "{name} old name   {n} number   {date} date taken (photos) or modified   {folder} folder name",
                Left = 16, Top = 68, Width = 588, Height = 20, ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            preview.SetBounds(16, 94, 588, 316);
            preview.View = View.Details;
            preview.FullRowSelect = true;
            preview.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            preview.Columns.Add("Now", 280);
            preview.Columns.Add("Will be", 280);
            preview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            status.SetBounds(16, 424, 380, 36);
            status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ok.Text = "Rename"; ok.DialogResult = DialogResult.OK; ok.SetBounds(428, 424, 84, 30);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(520, 424, 84, 30);
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, pattern, style, help, preview, status, ok, cancel });

            pattern.TextChanged += delegate { Update2(); };
            style.SelectedIndexChanged += delegate { Update2(); };
            Shown += delegate { Activate(); pattern.Focus(); pattern.SelectAll(); };
            Update2();
        }

        void Update2()
        {
            string p = pattern.Text;
            if (p.Trim().Length == 0 || p.IndexOfAny(Path.GetInvalidFileNameChars().Where(c => c != '{' && c != '}').ToArray()) >= 0)
            {
                status.Text = "That pattern has characters Windows does not allow in names.";
                status.ForeColor = Color.Firebrick;
                ok.Enabled = false;
                return;
            }
            try { plan = FileOps.PlanRename(paths, p, (NameStyle)style.SelectedIndex); }
            catch (Exception ex) { status.Text = ex.Message; ok.Enabled = false; return; }

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            preview.BeginUpdate();
            preview.Items.Clear();
            foreach (var m in plan.Take(1000))
            {
                bool clash = !targets.Add(m.Value) || (!sources.Contains(m.Value) && (File.Exists(m.Value) || Directory.Exists(m.Value)));
                var row = new ListViewItem(Path.GetFileName(m.Key.TrimEnd('\\')));
                row.SubItems.Add(Path.GetFileName(m.Value));
                if (clash) row.ForeColor = Color.Firebrick;
                preview.Items.Add(row);
            }
            preview.EndUpdate();
            int conflicts = FileOps.Conflicts(plan);
            status.ForeColor = conflicts > 0 ? Color.Firebrick : Color.DimGray;
            status.Text = conflicts > 0
                ? FileOps.Plural(conflicts, "name is", "names are") + " already taken and will be skipped. Add {n} or change the pattern."
                : "Nothing is overwritten. You can undo this from the notification or the tray menu.";
            ok.Enabled = plan.Count > conflicts;
        }
    }
}
