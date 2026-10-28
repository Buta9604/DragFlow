using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Single text-prompt dialog used for renaming (with a live preview line).</summary>
    public sealed class PromptDialog : Form
    {
        readonly TextBox box = new TextBox();
        readonly Label preview = new Label();

        public static string Ask(string title, string prompt, string initial, int selectLength, Func<string, string> previewFn)
        {
            using (var d = new PromptDialog(title, prompt, initial, selectLength, previewFn))
                return d.ShowDialog() == DialogResult.OK ? d.box.Text : null;
        }

        PromptDialog(string title, string prompt, string initial, int selectLength, Func<string, string> previewFn)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true; TopMost = true;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(460, 150);

            var label = new Label { Text = prompt, Left = 16, Top = 14, Width = 428, Height = 20 };
            box.SetBounds(16, 38, 428, 26);
            box.Text = initial;
            preview.SetBounds(16, 70, 428, 40);
            preview.ForeColor = Color.DimGray;
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 268, Top = 114, Width = 84 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 360, Top = 114, Width = 84 };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { label, box, preview, ok, cancel });

            if (previewFn != null)
            {
                box.TextChanged += delegate { try { preview.Text = previewFn(box.Text); } catch (Exception) { preview.Text = ""; } };
                preview.Text = previewFn(initial);
            }
            Shown += delegate
            {
                Activate();
                box.Focus();
                box.Select(0, selectLength < 0 ? box.Text.Length : selectLength);
            };
        }
    }

    /// <summary>What is taking up space in a folder: biggest items first, double-click to show one in Explorer.</summary>
    public sealed class SizeReportForm : Form
    {
        public SizeReportForm(string folder, List<SizeEntry> entries)
        {
            long total = entries.Sum(e => e.Bytes);
            Text = "Size of " + Path.GetFileName(folder.TrimEnd('\\'));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(520, 460);
            MinimumSize = new Size(380, 300);
            TopMost = true;

            var header = new Label
            {
                Text = FileOps.FormatSize(total) + " in " + FileOps.Plural(entries.Count, "item", "items") + "  ·  " + folder,
                Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 10, 12, 0), AutoEllipsis = true
            };
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None };
            list.Columns.Add("Name", 280);
            list.Columns.Add("Size", 100, HorizontalAlignment.Right);
            list.Columns.Add("Share", 110);
            foreach (SizeEntry e in entries.Take(200))
            {
                double share = total > 0 ? (double)e.Bytes / total : 0;
                string bar = new string('█', (int)Math.Round(share * 10)).PadRight(10, '░');
                var row = new ListViewItem((e.IsFolder ? "📁 " : "") + e.Name) { Tag = Path.Combine(folder, e.Name) };
                row.SubItems.Add(FileOps.FormatSize(e.Bytes));
                row.SubItems.Add(bar + " " + (share * 100).ToString("0") + "%");
                list.Items.Add(row);
            }
            list.DoubleClick += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                Process.Start("explorer.exe", "/select,\"" + list.SelectedItems[0].Tag + "\"");
            };
            Controls.Add(list);
            Controls.Add(header);
            Shown += delegate { Activate(); TopMost = false; };
        }
    }
}
