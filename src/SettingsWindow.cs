using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Settings in plain words, one tab per topic. Returns true when the user saved.</summary>
    public sealed partial class SettingsWindow : Form
    {
        readonly Settings s;
        readonly CheckBox enabled = new CheckBox(), startup = new CheckBox(), logPaths = new CheckBox();
        readonly TrackBar sensitivity = new TrackBar();
        readonly TextBox favorites = new TextBox();
        readonly CheckBox learning = new CheckBox(), ranking = new CheckBox();
        readonly Label learnText = new Label();
        readonly TextBox pinned = new TextBox();
        readonly CheckBox fullScreen = new CheckBox();
        readonly TextBox excludedApps = new TextBox(), excludedFolders = new TextBox(), otherApps = new TextBox();
        readonly CheckBox shelf = new CheckBox(), shelfKeep = new CheckBox();
        readonly DataGridView profiles = new DataGridView();

        public static bool Edit(Settings settings)
        {
            using (var w = new SettingsWindow(settings)) return w.ShowDialog() == DialogResult.OK;
        }

        SettingsWindow(Settings settings)
        {
            s = settings;
            Text = "DragFlow settings";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(640, 600);
            MinimumSize = new Size(560, 480);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
            tabs.TabPages.Add(General());
            tabs.TabPages.Add(Learn());
            tabs.TabPages.Add(QuietPage());
            tabs.TabPages.Add(AppsPage());
            tabs.TabPages.Add(ProfilesPage());
            tabs.TabPages.Add(LookPage());
            tabs.TabPages.Add(ShortcutsPage());
            tabs.TabPages.Add(ActionsPage());
            tabs.TabPages.Add(CustomPage());
            tabs.TabPages.Add(RulesPage());
            tabs.TabPages.Add(RecipesPage());
            tabs.TabPages.Add(BackupPage());
            tabs.TabPages.Add(PowerPage());
            tabs.Multiline = true;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            bottom.Controls.AddRange(new Control[] { ok, cancel });
            bottom.Layout += delegate { cancel.Location = new Point(bottom.Width - 102, 9); ok.Location = new Point(bottom.Width - 200, 9); };
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(tabs);
            Controls.Add(bottom);
            FormClosing += delegate (object o, FormClosingEventArgs e) { if (DialogResult == DialogResult.OK && !Apply()) e.Cancel = true; };
        }

        // Pages start at their real size so controls anchored to the right edge stretch correctly.
        static TabPage Page(string title) { return new TabPage(title) { Padding = new Padding(12), AutoScroll = true, Size = new Size(608, 420) }; }

        static Label Note(string text, int top, int height)
        {
            return new Label { Text = text, Left = 12, Top = top, Width = 540, Height = height, ForeColor = Color.DimGray, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        }

        static TextBox Lines(TextBox t, int top, int height, IEnumerable<string> lines)
        {
            t.SetBounds(12, top, 540, height);
            t.Multiline = true; t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = true;
            t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            t.Text = string.Join("\r\n", lines.ToArray());
            return t;
        }

        static List<string> LinesOf(TextBox t)
        {
            return t.Lines.Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        TabPage General()
        {
            TabPage p = Page("General");
            enabled.Text = "Show the action dial when I drag a file or folder"; enabled.SetBounds(12, 12, 540, 24); enabled.Checked = s.Enabled;
            startup.Text = "Start DragFlow with Windows"; startup.SetBounds(12, 40, 540, 24); startup.Checked = Settings.StartsWithWindows;
            var l = new Label { Text = "How far you can move before the dial hides", Left = 12, Top = 76, Width = 540 };
            sensitivity.SetBounds(6, 98, 548, 45);
            sensitivity.Minimum = 5; sensitivity.Maximum = 20; sensitivity.TickFrequency = 5;
            sensitivity.Value = Math.Max(5, Math.Min(20, (int)Math.Round(s.Sensitivity * 10)));
            sensitivity.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var hint = Note("Lower: it gets out of the way sooner. Higher: it stays longer. A quick flick never shows it. " +
                            "Hold Alt while dragging to skip it, press Esc to close it, hold Ctrl over \"Move to\" to copy instead.", 144, 54);
            var favLabel = new Label { Text = "Extra \"Move to\" folders (one per line; listed first)", Left = 12, Top = 204, Width = 540 };
            Lines(favorites, 226, 70, s.Favorites);
            logPaths.Text = "Include file names in the diagnostic log (only if asked when reporting a problem)"; logPaths.SetBounds(12, 306, 540, 24); logPaths.Checked = s.LogPaths;
            var custom = new Button { Text = "Edit custom actions…", Left = 12, Top = 340, Width = 170, Height = 30 };
            custom.Click += delegate { CustomActions.EnsureFile(); Process.Start("notepad.exe", "\"" + CustomActions.FilePath + "\""); };
            var logs = new Button { Text = "Open log folder", Left = 190, Top = 340, Width = 140, Height = 30 };
            logs.Click += delegate { Directory.CreateDirectory(Log.Dir); Process.Start("explorer.exe", "\"" + Log.Dir + "\""); };
            p.Controls.AddRange(new Control[] { enabled, startup, l, sensitivity, hint, favLabel, favorites, logPaths, custom, logs });
            return p;
        }

        TabPage Learn()
        {
            TabPage p = Page("Smart");
            learning.Text = "Learn how I drag (when the dial should open, and where I never want it)"; learning.SetBounds(12, 12, 540, 24); learning.Checked = s.Learning;
            ranking.Text = "Put the actions I use most first"; ranking.SetBounds(12, 40, 540, 24); ranking.Checked = s.Ranking;
            learnText.SetBounds(12, 72, 540, 120);
            learnText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            RefreshLearn();
            var more = new Button { Text = "Show it more", Left = 12, Top = 198, Width = 120, Height = 30 };
            more.Click += delegate { Learning.Current.ShowMore(); RefreshLearn(); };
            var less = new Button { Text = "Show it less", Left = 140, Top = 198, Width = 120, Height = 30 };
            less.Click += delegate { Learning.Current.ShowLess(); RefreshLearn(); };
            var reset = new Button { Text = "Reset learning", Left = 268, Top = 198, Width = 130, Height = 30 };
            reset.Click += delegate { Learning.Current.Reset(); RefreshLearn(); };
            var forget = new Button { Text = "Forget action history", Left = 406, Top = 198, Width = 146, Height = 30 };
            forget.Click += delegate { Usage.Current.Clear(); MessageBox.Show("The dial is back to its standard order.", "DragFlow"); };
            var pinLabel = new Label { Text = "Always put these actions first (names as shown on the dial, separated by commas)", Left = 12, Top = 244, Width = 540 };
            pinned.SetBounds(12, 266, 540, 26);
            pinned.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pinned.Text = string.Join(", ", s.PinnedActions.ToArray());
            var pinHint = Note("For example: Move to, Rename, Zip. Folder profiles (last tab) can do this for one folder only.", 296, 40);
            p.Controls.AddRange(new Control[] { learning, ranking, learnText, more, less, reset, forget, pinLabel, pinned, pinHint });
            return p;
        }

        void RefreshLearn()
        {
            learnText.Text = string.Join("\r\n\r\n", Learning.Current.Describe().ToArray()) + "\r\n\r\nNothing leaves this PC.";
        }

        TabPage QuietPage()
        {
            TabPage p = Page("Quiet");
            fullScreen.Text = "Stay hidden while a full-screen game, video or slideshow is open on any screen"; fullScreen.SetBounds(12, 12, 540, 24); fullScreen.Checked = s.QuietWhenFullScreen;
            var appsLabel = new Label { Text = "Stay hidden while these programs are running (one per line, e.g. obs64 or powerpnt)", Left = 12, Top = 48, Width = 540 };
            Lines(excludedApps, 70, 90, s.ExcludedApps);
            var running = new ComboBox { Left = 12, Top = 166, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            var add = new Button { Text = "Add running program", Left = 320, Top = 165, Width = 170, Height = 28 };
            running.DropDown += delegate
            {
                running.Items.Clear();
                running.Items.AddRange(Quiet.RunningPrograms().Where(n => n.Length > 0).OrderBy(n => n).Cast<object>().ToArray());
            };
            add.Click += delegate
            {
                if (running.SelectedItem == null) return;
                excludedApps.AppendText((excludedApps.Text.Length > 0 && !excludedApps.Text.EndsWith("\n") ? "\r\n" : "") + running.SelectedItem);
            };
            var foldersLabel = new Label { Text = "Never show the dial for items in these folders (one per line)", Left = 12, Top = 206, Width = 540 };
            Lines(excludedFolders, 228, 90, s.ExcludedFolders);
            var hint = Note("You can also pause DragFlow for an hour from its tray menu.", 326, 30);
            p.Controls.AddRange(new Control[] { fullScreen, appsLabel, excludedApps, running, add, foldersLabel, excludedFolders, hint });
            return p;
        }

        TabPage AppsPage()
        {
            TabPage p = Page("Apps & shelf");
            var l = new Label { Text = "Also show the dial for drags out of these programs (one per line)", Left = 12, Top = 12, Width = 540 };
            Lines(otherApps, 34, 90, s.OtherApps);
            var hint = Note("For example outlook (classic Outlook), olk (new Outlook), chrome, msedge, firefox, thunderbird. " +
                            "Dropping an attachment or a picture on an action saves it to Downloads\\DragFlow first, then runs the action.", 130, 54);
            shelf.Text = "Show the shelf at the screen edge while I drag files, so I can park them there"; shelf.SetBounds(12, 196, 540, 24); shelf.Checked = s.ShelfEnabled;
            shelfKeep.Text = "Keep items on the shelf after dragging them out"; shelfKeep.SetBounds(12, 224, 540, 24); shelfKeep.Checked = s.ShelfKeepAfterDrag;
            var shelfHint = Note("Parking does not move anything: the shelf remembers where the items are. Drag them out into any folder or app later.", 254, 40);
            p.Controls.AddRange(new Control[] { l, otherApps, hint, shelf, shelfKeep, shelfHint });
            return p;
        }

        TabPage ProfilesPage()
        {
            TabPage p = Page("Profiles");
            var l = Note("Profiles change the dial for items in one folder (and the folders inside it), or for drags out of certain programs. " +
                         "Actions and Hide take names as shown on the dial, separated by commas; Move-to folders are separated by |.", 8, 54);
            profiles.SetBounds(12, 64, 540, 300);
            profiles.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            profiles.AllowUserToAddRows = true;
            profiles.RowHeadersVisible = false;
            profiles.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            profiles.Columns.Add("Name", "Name");
            profiles.Columns.Add("Folder", "Folder");
            profiles.Columns.Add("Apps", "Programs");
            profiles.Columns.Add("Types", "File types");
            profiles.Columns.Add("First", "Actions first");
            profiles.Columns.Add("Hide", "Hide");
            profiles.Columns.Add("MoveTo", "Move-to folders");
            foreach (Profile pr in Profiles.Load())
                profiles.Rows.Add(pr.Name, pr.Folder, string.Join(", ", pr.Apps.ToArray()), string.Join(", ", pr.Types.ToArray()), string.Join(", ", pr.First.ToArray()),
                    string.Join(", ", pr.Hide.ToArray()), string.Join(" | ", pr.MoveTo.ToArray()));
            var browse = new Button { Text = "Pick folder for selected row…", Left = 12, Top = 372, Width = 220, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            browse.Click += delegate
            {
                DataGridViewRow row = profiles.CurrentRow;
                if (row == null) return;
                using (var d = new FolderBrowserDialog())
                    if (d.ShowDialog() == DialogResult.OK)
                    {
                        if (row.IsNewRow) { profiles.Rows.Add("", d.SelectedPath, "", "", "", "", ""); }
                        else row.Cells["Folder"].Value = d.SelectedPath;
                    }
            };
            var types = Note("File types: image, pdf, video, audio, word, text, archive, folder, other, mixed (empty = all).", 408, 30);
            types.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.AddRange(new Control[] { l, profiles, browse, types });
            return p;
        }

        static string Cell(DataGridViewRow r, string col) { return Convert.ToString(r.Cells[col].Value ?? "").Trim(); }

        static List<string> Split(string v, char sep)
        {
            return v.Split(sep).Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0).ToList();
        }

        bool Apply()
        {
            var list = new List<Profile>();
            foreach (DataGridViewRow r in profiles.Rows)
            {
                if (r.IsNewRow) continue;
                string folder = Cell(r, "Folder");
                List<string> apps = Split(Cell(r, "Apps"), ',').Select(Settings.NormalizeApp).Where(x => x.Length > 0).ToList();
                if (folder.Length == 0 && apps.Count == 0) continue;
                if (folder.Length > 0 && !Directory.Exists(folder))
                {
                    MessageBox.Show("The profile folder \"" + folder + "\" does not exist.", "DragFlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                list.Add(new Profile
                {
                    Name = Cell(r, "Name").Length > 0 ? Cell(r, "Name") : (folder.Length > 0 ? FileOps.NameOf(folder) : apps[0]),
                    Folder = folder,
                    Apps = apps,
                    Types = Split(Cell(r, "Types").ToLowerInvariant(), ','),
                    First = Split(Cell(r, "First"), ','),
                    Hide = Split(Cell(r, "Hide"), ','),
                    MoveTo = Split(Cell(r, "MoveTo"), '|')
                });
            }
            try { Profiles.Save(list); }
            catch (Exception ex) { MessageBox.Show("Could not save the profiles: " + ex.Message, "DragFlow"); return false; }

            s.Enabled = enabled.Checked;
            s.Sensitivity = sensitivity.Value / 10.0;
            s.Favorites = LinesOf(favorites);
            s.LogPaths = logPaths.Checked;
            s.Learning = learning.Checked;
            s.Ranking = ranking.Checked;
            s.PinnedActions = Split(pinned.Text, ',');
            s.QuietWhenFullScreen = fullScreen.Checked;
            s.ExcludedApps = LinesOf(excludedApps).Select(Settings.NormalizeApp).Where(x => x.Length > 0).Distinct().ToList();
            s.ExcludedFolders = LinesOf(excludedFolders);
            s.OtherApps = LinesOf(otherApps).Select(Settings.NormalizeApp).Where(x => x.Length > 0 && x != "explorer").Distinct().ToList();
            s.ShelfEnabled = shelf.Checked;
            s.ShelfKeepAfterDrag = shelfKeep.Checked;
            try { Settings.StartsWithWindows = startup.Checked; } catch (Exception ex) { Log.Error("startup setting", ex); }
            if (!ApplyMore()) return false;
            if (!ApplyPower()) return false;
            s.Save();
            return true;
        }
    }
}
