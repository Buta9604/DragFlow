using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DragFlow
{
    public sealed class ActivityEntry
    {
        public DateTime Time;
        public string Title = "", Result = "", UndoId = "", Output = "";
    }

    /// <summary>Everything DragFlow did, newest last, in %LOCALAPPDATA%\DragFlow\activity.tsv (the last 1000 entries).</summary>
    public static class ActivityLog
    {
        const int Max = 1000;
        static readonly object gate = new object();

        public static string FilePath { get { return Path.Combine(AppPaths.Local, "activity.tsv"); } }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        public static void Record(string title, string result, string undoId, string output)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.Local);
                    File.AppendAllText(FilePath, DateTime.Now.Ticks + "\t" + Clean(title) + "\t" + Clean(result) + "\t" + Clean(undoId) + "\t" + Clean(output) + "\r\n", Encoding.UTF8);
                    var fi = new FileInfo(FilePath);
                    if (fi.Length > 400 * 1024)
                    {
                        string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                        File.WriteAllLines(FilePath, lines.Skip(Math.Max(0, lines.Length - Max)).ToArray(), Encoding.UTF8);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static List<ActivityEntry> Load()
        {
            var list = new List<ActivityEntry>();
            lock (gate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return list;
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        long t;
                        if (p.Length < 5 || !long.TryParse(p[0], out t)) continue;
                        list.Add(new ActivityEntry { Time = new DateTime(t), Title = p[1], Result = p[2], UndoId = p[3], Output = p[4] });
                    }
                }
                catch (IOException) { }
            }
            return list;
        }

        public static void Clear()
        {
            lock (gate) { try { File.Delete(FilePath); } catch (IOException) { } }
        }
    }

    /// <summary>The Activity window: what DragFlow did, searchable, with Undo and Show for each entry.</summary>
    public sealed class ActivityWindow : Form
    {
        static ActivityWindow open;
        readonly TextBox search = new TextBox();
        readonly ListView list = new ListView();
        readonly Button undo = new Button(), show = new Button();
        readonly ActionRunner runner;
        List<ActivityEntry> all = new List<ActivityEntry>();

        public static void ShowWindow(ActionRunner runner)
        {
            if (open != null && !open.IsDisposed) { open.Reload(); open.Activate(); return; }
            open = new ActivityWindow(runner);
            open.Show();
        }

        ActivityWindow(ActionRunner runner)
        {
            this.runner = runner;
            Text = L.T("DragFlow activity");
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(520, 320);

            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 10, 12, 6) };
            var label = new Label { Text = L.T("Search"), AutoSize = true, Left = 12, Top = 14 };
            search.SetBounds(70, 10, 300, 26);
            search.TextChanged += delegate { Fill(); };
            top.Controls.AddRange(new Control[] { label, search });

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.Columns.Add(L.T("When"), 130);
            list.Columns.Add(L.T("Action"), 190);
            list.Columns.Add(L.T("Result"), 420);
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.DoubleClick += delegate { DoShow(); };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            undo.Text = L.T("Undo"); undo.SetBounds(12, 9, 100, 30); undo.Click += delegate { DoUndo(); };
            show.Text = L.T("Show"); show.SetBounds(120, 9, 100, 30); show.Click += delegate { DoShow(); };
            var clear = new Button { Text = L.T("Clear list"), Width = 100, Height = 30, Top = 9, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            clear.Left = ClientSize.Width - 112;
            clear.Click += delegate
            {
                if (MessageBox.Show(L.T("Clear the activity list? Nothing is undone and your files are not touched."), "DragFlow", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                ActivityLog.Clear(); Reload();
            };
            bottom.Controls.AddRange(new Control[] { undo, show, clear });
            Controls.Add(list);
            Controls.Add(top);
            Controls.Add(bottom);
            Reload();
        }

        void Reload()
        {
            all = ActivityLog.Load();
            all.Reverse();
            Fill();
        }

        void Fill()
        {
            string q = search.Text.Trim();
            var undoable = new HashSet<string>(UndoLog.History().Select(e => e.Id));
            list.BeginUpdate();
            list.Items.Clear();
            foreach (ActivityEntry e in all)
            {
                if (q.Length > 0 && (e.Title + " " + e.Result + " " + e.Output).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var row = new ListViewItem(e.Time.Date == DateTime.Today ? e.Time.ToShortTimeString() : e.Time.ToString("g")) { Tag = e };
                row.SubItems.Add(e.Title);
                row.SubItems.Add(e.Result + (e.UndoId.Length > 0 && !undoable.Contains(e.UndoId) ? "  (" + L.T("undone or expired") + ")" : ""));
                list.Items.Add(row);
            }
            list.EndUpdate();
            UpdateButtons();
        }

        ActivityEntry Selected { get { return list.SelectedItems.Count == 0 ? null : (ActivityEntry)list.SelectedItems[0].Tag; } }

        void UpdateButtons()
        {
            ActivityEntry e = Selected;
            undo.Enabled = e != null && e.UndoId.Length > 0 && UndoLog.History().Any(h => h.Id == e.UndoId);
            show.Enabled = e != null && e.Output.Length > 0 && (File.Exists(e.Output) || Directory.Exists(e.Output));
        }

        void DoUndo()
        {
            ActivityEntry e = Selected;
            if (e == null || e.UndoId.Length == 0) return;
            runner.UndoNow(e.UndoId);
            undo.Enabled = false;
            var t = new Timer { Interval = 1200 };
            t.Tick += delegate { t.Dispose(); if (!IsDisposed) Fill(); };
            t.Start();
        }

        void DoShow()
        {
            ActivityEntry e = Selected;
            if (e == null || e.Output.Length == 0 || !(File.Exists(e.Output) || Directory.Exists(e.Output))) return;
            Process.Start("explorer.exe", "/select,\"" + e.Output.TrimEnd('\\') + "\"");
        }
    }
}
