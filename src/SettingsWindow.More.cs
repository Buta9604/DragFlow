using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Settings pages for the look, shortcuts and updates, actions, custom actions, rules and recipes.</summary>
    public sealed partial class SettingsWindow
    {
        readonly ComboBox theme = new ComboBox(), dialSize = new ComboBox(), motion = new ComboBox(), language = new ComboBox();
        readonly CheckBox hotkeyOn = new CheckBox(), contextMenu = new CheckBox(), rulesOn = new CheckBox();
        readonly TextBox hotkeyBox = new TextBox(), feed = new TextBox();
        readonly CheckedListBox shown = new CheckedListBox();
        readonly DataGridView custom = new DataGridView(), rulesGrid = new DataGridView();
        readonly ListBox recipeList = new ListBox(), stepList = new ListBox();
        readonly TextBox recipeName = new TextBox(), recipeTypes = new TextBox();
        readonly ComboBox stepChoice = new ComboBox();
        List<Recipe> recipes = RecipeStore.Load();
        // Recipes saved from the tray while this window was open are kept when it saves.
        readonly HashSet<string> recipesAtOpen = new HashSet<string>(RecipeStore.Load().Select(r => r.Name));
        bool loadingRecipe;

        static readonly double[] Sizes = { 1.0, 1.25, 1.5 };

        TabPage LookPage()
        {
            TabPage p = Page(L.T("Look"));
            int y = 14;
            Pair(p, L.T("Colours"), theme, ref y, L.T("Follow Windows"), L.T("Light"), L.T("Dark"));
            theme.SelectedIndex = s.ThemeMode == "light" ? 1 : s.ThemeMode == "dark" ? 2 : 0;
            Pair(p, L.T("Dial size"), dialSize, ref y, L.T("Normal"), L.T("Large"), L.T("Extra large"));
            dialSize.SelectedIndex = Math.Max(0, Array.IndexOf(Sizes, s.DialSize));
            Pair(p, L.T("Animations"), motion, ref y, L.T("Follow Windows"), L.T("On"), L.T("Off"));
            motion.SelectedIndex = s.Motion == "on" ? 1 : s.Motion == "off" ? 2 : 0;
            var langs = new List<string> { "auto", "en" };
            foreach (string dir in L.Folders().Where(Directory.Exists))
                foreach (string f in Directory.GetFiles(dir, "*.txt")) langs.Add(Path.GetFileNameWithoutExtension(f));
            langs = langs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Pair(p, L.T("Language"), language, ref y, langs.Select(x => x == "auto" ? L.T("Windows language") : x == "en" ? "English" : x).ToArray());
            language.Tag = langs;
            language.SelectedIndex = Math.Max(0, langs.FindIndex(x => string.Equals(x, s.Language, StringComparison.OrdinalIgnoreCase)));
            p.Controls.Add(Note(L.T("High contrast in Windows always wins: the dial then uses your high-contrast colours. Translations go in the lang folder (see docs\\translations)."), y + 6, 54));
            return p;
        }

        static void Pair(TabPage p, string label, ComboBox box, ref int y, params string[] items)
        {
            p.Controls.Add(new Label { Text = label, Left = 12, Top = y + 4, Width = 140 });
            box.SetBounds(160, y, 300, 26);
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.AddRange(items.Cast<object>().ToArray());
            p.Controls.Add(box);
            y += 38;
        }

        TabPage ShortcutsPage()
        {
            TabPage p = Page(L.T("Shortcuts & updates"));
            hotkeyOn.Text = L.T("Open the dial for the selected files with this keyboard shortcut:");
            hotkeyOn.SetBounds(12, 12, 560, 24); hotkeyOn.Checked = s.HotkeyEnabled;
            hotkeyBox.SetBounds(32, 40, 200, 26); hotkeyBox.Text = s.Hotkey; hotkeyBox.ReadOnly = true; hotkeyBox.BackColor = SystemColors.Window;
            hotkeyBox.KeyDown += delegate (object o, KeyEventArgs e)
            {
                e.SuppressKeyPress = true;
                Keys k = e.KeyCode;
                if (k == Keys.ControlKey || k == Keys.Menu || k == Keys.ShiftKey || k == Keys.LWin || k == Keys.RWin) return;
                var parts = new List<string>();
                if (e.Control) parts.Add("Ctrl");
                if (e.Alt) parts.Add("Alt");
                if (e.Shift) parts.Add("Shift");
                if (parts.Count == 0) return; // a shortcut needs Ctrl, Alt or Shift
                string key = k >= Keys.D0 && k <= Keys.D9 ? ((int)(k - Keys.D0)).ToString() : k.ToString();
                parts.Add(key);
                hotkeyBox.Text = string.Join("+", parts.ToArray());
            };
            p.Controls.Add(new Label { Text = L.T("Click the box and press the keys you want."), Left = 240, Top = 44, Width = 330, ForeColor = Color.DimGray });
            contextMenu.Text = L.T("Add \"DragFlow actions…\" to Explorer's right-click menu");
            contextMenu.SetBounds(12, 80, 560, 24); contextMenu.Checked = s.ContextMenu;
            p.Controls.Add(Note(L.T("On Windows 11 it is under \"Show more options\" (or Shift+right-click)."), 106, 24));

            p.Controls.Add(new Label { Text = L.T("Where new versions of DragFlow are published (a folder or web address with latest.ini):"), Left = 12, Top = 146, Width = 560 });
            feed.SetBounds(12, 168, 420, 26); feed.Text = s.UpdateFeed; feed.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var browse = new Button { Text = L.T("Folder…"), Left = 440, Top = 167, Width = 80, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.Click += delegate { using (var d = new FolderBrowserDialog()) if (d.ShowDialog() == DialogResult.OK) feed.Text = d.SelectedPath; };
            var check = new Button { Text = L.T("Check now"), Left = 528, Top = 167, Width = 90, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            check.Click += delegate
            {
                string f = feed.Text.Trim();
                check.Enabled = false;
                Task.Factory.StartNew(delegate
                {
                    string msg;
                    try { UpdateInfo info = Updater.Check(f); msg = info == null ? L.F("DragFlow {0} is the newest version there.", Program.Version) : L.F("Version {0} is available. Save the settings, then use Check for updates in the tray menu.", info.Version.ToString(3)); }
                    catch (Exception ex) { msg = ex.Message; }
                    BeginInvoke((Action)delegate { check.Enabled = true; MessageBox.Show(this, msg, "DragFlow"); });
                });
            };
            p.Controls.Add(Note(L.T("For example a OneDrive folder you copy new releases into (scripts\\release.ps1 makes them). DragFlow checks once a day, asks before installing, and only installs a file whose checksum matches."), 200, 54));
            p.Controls.AddRange(new Control[] { hotkeyOn, hotkeyBox, contextMenu, feed, browse, check });
            return p;
        }

        TabPage ActionsPage()
        {
            TabPage p = Page(L.T("Actions"));
            p.Controls.Add(Note(L.T("Untick actions you never want on the dial. Everything else stays where it is."), 8, 24));
            shown.SetBounds(12, 36, 560, 380);
            shown.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            shown.CheckOnClick = true;
            shown.MultiColumn = true;
            shown.ColumnWidth = 180;
            foreach (string name in ActionCatalog.AllNames)
                shown.Items.Add(L.T(name), !s.HiddenActions.Any(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase)));
            p.Controls.Add(shown);
            return p;
        }

        TabPage CustomPage()
        {
            TabPage p = Page(L.T("Custom actions"));
            p.Controls.Add(Note(L.T("Your own commands on the dial. File types: * for anything, folder, or extensions like .png,.jpg. Arguments: {file} once per item, {files} all at once, {dir} the folder."), 8, 40));
            custom.SetBounds(12, 52, 560, 320);
            custom.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            custom.RowHeadersVisible = false;
            custom.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            custom.Columns.Add("Label", L.T("Name on the dial"));
            custom.Columns.Add("Types", L.T("File types"));
            custom.Columns.Add("Program", L.T("Program"));
            custom.Columns.Add("Args", L.T("Arguments"));
            foreach (CustomAction a in CustomActions.Load()) custom.Rows.Add(a.Label, a.Types, a.Program, a.Args);
            var program = new Button { Text = L.T("Pick program…"), Left = 12, Top = 380, Width = 140, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            program.Click += delegate
            {
                DataGridViewRow row = custom.CurrentRow;
                if (row == null) return;
                using (var d = new OpenFileDialog { Filter = L.T("Programs") + "|*.exe;*.cmd;*.bat;*.ps1|" + L.T("All files") + "|*.*" })
                    if (d.ShowDialog() == DialogResult.OK)
                    {
                        if (row.IsNewRow) custom.Rows.Add(Path.GetFileNameWithoutExtension(d.FileName), "*", d.FileName, "\"{file}\"");
                        else row.Cells["Program"].Value = d.FileName;
                    }
            };
            var test = new Button { Text = L.T("Test on a file…"), Left = 160, Top = 380, Width = 140, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            test.Click += delegate
            {
                DataGridViewRow row = custom.CurrentRow;
                if (row == null || row.IsNewRow) return;
                var a = new CustomAction { Label = Cell(row, "Label"), Types = Cell(row, "Types"), Program = Cell(row, "Program"), Args = Cell(row, "Args").Length > 0 ? Cell(row, "Args") : "\"{file}\"" };
                using (var d = new OpenFileDialog())
                {
                    if (d.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        foreach (string args in CustomActions.Expand(a, new[] { d.FileName }))
                            Process.Start(new ProcessStartInfo(a.Program, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(d.FileName) });
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); }
                }
            };
            p.Controls.AddRange(new Control[] { custom, program, test });
            return p;
        }

        TabPage RecipesPage()
        {
            TabPage p = Page(L.T("Recipes"));
            p.Controls.Add(Note(L.T("A recipe runs several actions in a row and appears on the dial under Custom. Each step works on what the step before made; everything is one Undo."), 8, 40));
            recipeList.SetBounds(12, 52, 170, 320);
            recipeList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            var add = new Button { Text = L.T("New"), Left = 12, Top = 378, Width = 80, Height = 28, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var del = new Button { Text = L.T("Delete"), Left = 100, Top = 378, Width = 82, Height = 28, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            p.Controls.Add(new Label { Text = L.T("Name"), Left = 196, Top = 56, Width = 100 });
            recipeName.SetBounds(300, 52, 270, 26);
            p.Controls.Add(new Label { Text = L.T("File types"), Left = 196, Top = 90, Width = 100 });
            recipeTypes.SetBounds(300, 86, 270, 26);
            p.Controls.Add(new Label { Text = L.T("Steps"), Left = 196, Top = 124, Width = 100 });
            stepList.SetBounds(300, 122, 270, 150);
            stepChoice.SetBounds(300, 280, 190, 26);
            stepChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string[] c in RecipeSteps.Choices) stepChoice.Items.Add(L.T(c[1]));
            stepChoice.SelectedIndex = 0;
            var addStep = new Button { Text = L.T("Add"), Left = 496, Top = 279, Width = 74, Height = 28 };
            var up = new Button { Text = "↑", Left = 300, Top = 314, Width = 40, Height = 28 };
            var down = new Button { Text = "↓", Left = 346, Top = 314, Width = 40, Height = 28 };
            var remove = new Button { Text = L.T("Remove step"), Left = 392, Top = 314, Width = 120, Height = 28 };
            p.Controls.AddRange(new Control[] { recipeList, add, del, recipeName, recipeTypes, stepList, stepChoice, addStep, up, down, remove });

            Action fillList = delegate
            {
                int keep = recipeList.SelectedIndex;
                recipeList.Items.Clear();
                foreach (Recipe r in recipes) recipeList.Items.Add(r.Name.Length > 0 ? r.Name : L.T("(no name)"));
                if (recipes.Count > 0) recipeList.SelectedIndex = Math.Max(0, Math.Min(keep, recipes.Count - 1));
            };
            Func<Recipe> current = delegate { return recipeList.SelectedIndex >= 0 && recipeList.SelectedIndex < recipes.Count ? recipes[recipeList.SelectedIndex] : null; };
            Action fillSteps = delegate
            {
                Recipe r = current();
                loadingRecipe = true;
                stepList.Items.Clear();
                recipeName.Text = r == null ? "" : r.Name;
                recipeTypes.Text = r == null ? "" : r.Types;
                if (r != null) foreach (string st in r.Steps) stepList.Items.Add(RecipeSteps.Label(st));
                loadingRecipe = false;
            };
            recipeList.SelectedIndexChanged += delegate { fillSteps(); };
            recipeName.TextChanged += delegate { Recipe r = current(); if (r == null || loadingRecipe) return; r.Name = recipeName.Text.Trim(); recipeList.Items[recipeList.SelectedIndex] = r.Name.Length > 0 ? r.Name : L.T("(no name)"); };
            recipeTypes.TextChanged += delegate { Recipe r = current(); if (r != null && !loadingRecipe) r.Types = recipeTypes.Text.Trim(); };
            add.Click += delegate
            {
                recipes.Add(new Recipe { Name = L.T("New recipe"), Types = "image", Steps = new List<string> { "resize:1920", "strip", "zip" } });
                fillList();
                recipeList.SelectedIndex = recipes.Count - 1;
            };
            del.Click += delegate { Recipe r = current(); if (r == null) return; recipes.Remove(r); fillList(); fillSteps(); };
            addStep.Click += delegate
            {
                Recipe r = current();
                if (r == null) return;
                string id = RecipeSteps.Choices[stepChoice.SelectedIndex][0];
                if (id == "moveto" || id == "copyto")
                    using (var d = new FolderBrowserDialog()) { if (d.ShowDialog() != DialogResult.OK) return; id += ":" + d.SelectedPath; }
                r.Steps.Add(id);
                fillSteps();
            };
            remove.Click += delegate { Recipe r = current(); int i = stepList.SelectedIndex; if (r == null || i < 0) return; r.Steps.RemoveAt(i); fillSteps(); };
            up.Click += delegate
            {
                Recipe r = current(); int i = stepList.SelectedIndex;
                if (r == null || i <= 0) return;
                string t = r.Steps[i]; r.Steps.RemoveAt(i); r.Steps.Insert(i - 1, t); fillSteps(); stepList.SelectedIndex = i - 1;
            };
            down.Click += delegate
            {
                Recipe r = current(); int i = stepList.SelectedIndex;
                if (r == null || i < 0 || i >= r.Steps.Count - 1) return;
                string t = r.Steps[i]; r.Steps.RemoveAt(i); r.Steps.Insert(i + 1, t); fillSteps(); stepList.SelectedIndex = i + 1;
            };
            fillList();
            fillSteps();
            return p;
        }

        /// <summary>Runs a rule on existing files (set by the app).</summary>
        public static Func<Rule, int> RunRuleNow;
        /// <summary>Restarts DragFlow (set by the app), after settings moved or were restored.</summary>
        public static Action Restart;

        TabPage BackupPage()
        {
            TabPage p = Page(L.T("Backup"));
            p.Controls.Add(Note(L.T("Your settings, custom actions, profiles, rules, recipes and shelf live in one folder. Keep them in a OneDrive folder to have the same DragFlow on every PC, or back them up to a file."), 8, 54));
            var where = new Label { Text = L.T("Settings folder:") + " " + AppPaths.Roaming, Left = 12, Top = 70, Width = 560, Height = 40, AutoEllipsis = true };
            var move = new Button { Text = L.T("Keep settings in another folder…"), Left = 12, Top = 116, Width = 260, Height = 30 };
            move.Click += delegate
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Choose the folder (for example inside OneDrive). DragFlow copies its settings there and restarts.") })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        AppPaths.MoveSettingsTo(d.SelectedPath);
                        MessageBox.Show(this, L.T("Done. DragFlow restarts now to use the new folder."), "DragFlow");
                        DialogResult = DialogResult.Cancel;
                        if (Restart != null) Restart();
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); }
                }
            };
            var backup = new Button { Text = L.T("Back up settings to a file…"), Left = 12, Top = 156, Width = 260, Height = 30 };
            backup.Click += delegate
            {
                using (var d = new SaveFileDialog { Filter = "Zip|*.zip", FileName = "DragFlow settings " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try { AppPaths.Backup(d.FileName); MessageBox.Show(this, L.T("Saved."), "DragFlow"); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); }
                }
            };
            var restore = new Button { Text = L.T("Restore settings from a file…"), Left = 12, Top = 196, Width = 260, Height = 30 };
            restore.Click += delegate
            {
                using (var d = new OpenFileDialog { Filter = "Zip|*.zip" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    if (MessageBox.Show(this, L.T("Replace the current settings with the ones in this file? DragFlow restarts afterwards."), "DragFlow", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    try
                    {
                        AppPaths.Restore(d.FileName);
                        DialogResult = DialogResult.Cancel;
                        if (Restart != null) Restart();
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); }
                }
            };
            p.Controls.AddRange(new Control[] { where, move, backup, restore });
            return p;
        }

        bool ApplyMore()
        {
            // custom actions
            var actions = new List<CustomAction>();
            foreach (DataGridViewRow r in custom.Rows)
            {
                if (r.IsNewRow || Cell(r, "Label").Length == 0 || Cell(r, "Program").Length == 0) continue;
                actions.Add(new CustomAction { Label = Cell(r, "Label"), Types = Cell(r, "Types").Length > 0 ? Cell(r, "Types") : "*", Program = Cell(r, "Program"), Args = Cell(r, "Args").Length > 0 ? Cell(r, "Args") : "\"{file}\"" });
            }
            // rules
            var rules = new List<Rule>();
            foreach (DataGridViewRow r in rulesGrid.Rows)
            {
                if (r.IsNewRow || Cell(r, "Folder").Length == 0) continue;
                Rule rule = RuleFromRow(r) ?? new Rule { Folder = Cell(r, "Folder"), Name = Cell(r, "Name") };
                string problem = rule.Problem();
                if (rule.Enabled && problem != null)
                {
                    MessageBox.Show(this, L.T("Rule") + " \"" + rule.Name + "\": " + problem + ".", "DragFlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                rules.Add(rule);
            }
            uint mods; Keys key;
            if (hotkeyOn.Checked && !Hotkey.Parse(hotkeyBox.Text, out mods, out key))
            {
                MessageBox.Show(this, L.T("That keyboard shortcut needs Ctrl, Alt or Shift plus a key."), "DragFlow");
                return false;
            }
            try
            {
                CustomActions.Save(actions);
                RuleStore.Save(rules);
                List<Recipe> keep = recipes.Where(r => r.Name.Length > 0 && r.Steps.Count > 0).ToList();
                keep.AddRange(RecipeStore.Load().Where(r => !recipesAtOpen.Contains(r.Name) && keep.All(k => k.Name != r.Name)));
                RecipeStore.Save(keep);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); return false; }

            s.ThemeMode = theme.SelectedIndex == 1 ? "light" : theme.SelectedIndex == 2 ? "dark" : "auto";
            s.DialSize = Sizes[Math.Max(0, dialSize.SelectedIndex)];
            s.Motion = motion.SelectedIndex == 1 ? "on" : motion.SelectedIndex == 2 ? "off" : "auto";
            var langs = (List<string>)language.Tag;
            s.Language = langs[Math.Max(0, language.SelectedIndex)];
            s.HotkeyEnabled = hotkeyOn.Checked;
            s.Hotkey = hotkeyBox.Text;
            s.ContextMenu = contextMenu.Checked;
            s.UpdateFeed = feed.Text.Trim();
            s.RulesEnabled = rulesOn.Checked;
            s.HiddenActions = new List<string>();
            for (int i = 0; i < ActionCatalog.AllNames.Length; i++)
                if (!shown.GetItemChecked(i)) s.HiddenActions.Add(ActionCatalog.AllNames[i]);
            return true;
        }

        /// <summary>For screenshots in the tests.</summary>
        internal static Form CreateForPreview(Settings settings, int tab)
        {
            var w = new SettingsWindow(settings);
            foreach (Control c in w.Controls) { var t = c as TabControl; if (t != null) t.SelectedIndex = tab; }
            return w;
        }
    }
}
