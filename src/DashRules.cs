using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DragFlow
{
    /// <summary>Dashboard pages: Rules (as sentences, with Try it) and Recipes.</summary>
    public sealed partial class DashboardWindow
    {
        int ruleOpen = -1;

        /// <summary>"When a PDF lands in Downloads and it is older than 30 days, every Sunday at 18:00, move it to Archive."</summary>
        public static string Sentence(Rule r)
        {
            string files = r.Pattern == "*" || r.Pattern.Length == 0 ? L.T("a file") : r.Pattern.Replace(";", " or ").Replace(",", " or ");
            string where = FileOps.NameOf(r.Folder);
            if (string.IsNullOrEmpty(where)) where = L.T("(no folder chosen)");
            var parts = new List<string>();
            if (r.MinAgeDays > 0) parts.Add(L.F("is older than {0} days", r.MinAgeDays));
            if (r.MinSizeMB > 0) parts.Add(L.F("is bigger than {0} MB", r.MinSizeMB));
            if (r.WebOnly) parts.Add(L.T("came from the internet"));
            string conditions = parts.Count == 0 ? "" : ", " + L.T("and it") + " " + string.Join(L.T(" and "), parts.ToArray());
            RuleSchedule sch = RuleSchedule.Parse(r.Schedule);
            string when = sch == null ? (r.Scan ? ", " + L.T("checked every hour") : "")
                        : sch.Day == null ? ", " + L.F("every day at {0}", sch.Time.ToString(@"hh\:mm")) : ", " + L.F("every {0} at {1}", CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(sch.Day.Value), sch.Time.ToString(@"hh\:mm"));
            string action = RuleActions.Label(r.Action) ?? L.T("(no action chosen)");
            if ((r.Action == "moveto" || r.Action == "copyto") && r.Target.Length > 0) action += ": " + FileOps.NameOf(r.Target);
            string lead = sch == null && r.MinAgeDays == 0 ? L.F("When {0} lands in {1}", files, where) : L.F("For {0} in {1}", files, where);
            return lead + conditions + when + " → " + action;
        }

        UIElement RulesPage()
        {
            List<Rule> rules = RuleStore.Load();
            Action save = delegate { Change(L.T("Rules saved"), delegate { RuleStore.Save(rules); }); };
            var cards = new List<UIElement>();
            cards.Add(UI.Card(null, null,
                UI.Setting(L.T("Run my rules"), L.F("{0} rule(s); {1} folder(s) watched now.", rules.Count, host.RulesWatching), Flip(L.T("Rules"), S.RulesEnabled, delegate (bool v) { S.RulesEnabled = v; })),
                UI.Btn(L.T("New rule"), delegate
                {
                    rules.Add(new Rule { Name = L.T("New rule"), Enabled = false, Pattern = "*.pdf", Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Action = "moveto" });
                    ruleOpen = rules.Count - 1;
                    RuleStore.Save(rules);
                    Refresh();
                }, true, "")));
            for (int i = 0; i < rules.Count; i++)
            {
                int index = i;
                Rule r = rules[i];
                string problem = r.Problem();
                var head = UI.Setting(r.Name.Length > 0 ? r.Name : L.T("(no name)"), Sentence(r),
                    UI.Row(8,
                        problem != null ? UI.Chip(L.T("Needs attention"), UI.T.Warning) : null,
                        UI.Btn(ruleOpen == index ? L.T("Close") : L.T("Edit"), delegate { ruleOpen = ruleOpen == index ? -1 : index; Refresh(); }),
                        UI.Toggle(r.Name, r.Enabled, delegate (bool v)
                        {
                            if (v && r.Problem() != null) { MessageBox.Show(r.Problem(), "DragFlow"); Refresh(); return; }
                            r.Enabled = v; save();
                        })));
                cards.Add(UI.Card(null, null, head, problem != null ? UI.Text(problem, 12.5, UI.T.Warning) : null, ruleOpen == index ? RuleEditor(rules, r, save) : null));
            }
            if (rules.Count == 0) cards.Add(UI.Sub(L.T("No rules yet. A rule could be: when a PDF lands in Downloads, move it to Documents › PDFs.")));
            return UI.Stack(0, cards.ToArray());
        }

        UIElement RuleEditor(List<Rule> rules, Rule r, Action save)
        {
            Func<Action, Action> then = delegate (Action a) { return delegate { a(); save(); }; };
            Func<Action, Action> redraw = delegate (Action a) { return delegate { a(); save(); Refresh(); }; }; // for changes that show or hide fields
            TextBox folder = UI.Field(r.Folder, 330, delegate (string v) { then(delegate { r.Folder = v.Trim(); })(); });
            TextBox target = UI.Field(r.Target, 330, delegate (string v) { then(delegate { r.Target = v.Trim(); })(); });
            Func<TextBox, Action<string>, Button> browse = delegate (TextBox box, Action<string> set)
            {
                return UI.Btn("…", delegate
                {
                    using (var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = box.Text })
                        if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { box.Text = d.SelectedPath; then(delegate { set(d.SelectedPath); })(); }
                });
            };
            var actionIds = RuleActions.Choices.Select(c => c[0]).Concat(RecipeStore.Load().Select(rc => "recipe:" + rc.Name)).ToList();
            var action = UI.Combo(actionIds.Select(id => RuleActions.Label(id)), actionIds.IndexOf(r.Action), 330, delegate (int i) { redraw(delegate { r.Action = actionIds[i]; })(); });

            RuleSchedule sch = RuleSchedule.Parse(r.Schedule);
            int mode = sch != null ? (sch.Day == null ? 2 : 3) : r.Scan ? 1 : 0;
            string[] days = Enumerable.Range(0, 7).Select(d => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)((d + 1) % 7))).ToArray();
            int day = sch != null && sch.Day != null ? ((int)sch.Day.Value + 6) % 7 : 6;
            string time = sch != null ? sch.Time.ToString(@"hh\:mm") : "18:00";
            Action<int, int, string> setWhen = delegate (int m, int d, string t)
            {
                redraw(delegate
                {
                    string[] en = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
                    r.Scan = m == 1 || r.MinAgeDays > 0;
                    r.Schedule = m == 2 ? "daily " + t : m == 3 ? "weekly " + en[d] + " " + t : "";
                })();
            };
            ComboBox whenBox = null, dayBox = null;
            TextBox timeBox = null;
            whenBox = UI.Combo(new[] { L.T("As files arrive"), L.T("As files arrive, and every hour"), L.T("Every day at"), L.T("Every week on") }, mode, 230,
                delegate (int m) { setWhen(m, dayBox.SelectedIndex, timeBox.Text.Trim()); });
            dayBox = UI.Combo(days, day, 130, delegate (int d) { setWhen(whenBox.SelectedIndex, d, timeBox.Text.Trim()); });
            dayBox.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
            timeBox = UI.Field(time, 70, delegate (string t)
            {
                if (RuleSchedule.Parse("daily " + t.Trim()) == null) { MessageBox.Show(L.T("Type a time such as 18:00."), "DragFlow"); return; }
                setWhen(whenBox.SelectedIndex, dayBox.SelectedIndex, t.Trim());
            });
            timeBox.Visibility = mode >= 2 ? Visibility.Visible : Visibility.Collapsed;

            var found = UI.Sub("");
            var tryIt = UI.Btn(L.T("Try it"), delegate
            {
                List<string> files = RulePreview.Matches(r, 11);
                found.Text = files.Count == 0 ? L.T("Right now it would take no files.")
                           : L.F("Right now it would take {0}:", files.Count > 10 ? L.T("more than 10 files") : files.Count.ToString()) + "\r\n" +
                             string.Join("\r\n", files.Take(10).Select(Path.GetFileName).ToArray());
            }, false, "");
            var remove = UI.Btn(L.T("Delete rule"), delegate
            {
                if (MessageBox.Show(L.F("Delete the rule \"{0}\"?", r.Name), "DragFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                rules.Remove(r); ruleOpen = -1; save(); Refresh();
            });
            int age = r.MinAgeDays, size = r.MinSizeMB;
            return UI.Stack(2,
                UI.Line(),
                UI.Setting(L.T("Name"), null, UI.Field(r.Name, 330, delegate (string v) { then(delegate { r.Name = v.Trim(); })(); })),
                UI.Setting(L.T("Watch this folder"), null, UI.Row(6, folder, browse(folder, delegate (string v) { r.Folder = v; }))),
                UI.Setting(L.T("And the folders inside it"), null, UI.Toggle(L.T("Subfolders"), r.Subfolders, delegate (bool v) { then(delegate { r.Subfolders = v; })(); })),
                UI.Setting(L.T("Files"), L.T("Patterns such as *.pdf;*.docx, or * for all."), UI.Field(r.Pattern, 330, delegate (string v) { then(delegate { r.Pattern = v.Trim().Length == 0 ? "*" : v.Trim(); })(); })),
                UI.Setting(L.T("Only when older than (days)"), L.T("Empty or 0 for any age. Age rules look at what is already there."), UI.Field(age > 0 ? age.ToString() : "", 70, delegate (string v)
                {
                    int n; int.TryParse(v.Trim(), out n);
                    then(delegate { r.MinAgeDays = Math.Max(0, n); if (n > 0) r.Scan = true; })();
                })),
                UI.Setting(L.T("Only when bigger than (MB)"), null, UI.Field(size > 0 ? size.ToString() : "", 70, delegate (string v) { int n; int.TryParse(v.Trim(), out n); then(delegate { r.MinSizeMB = Math.Max(0, n); })(); })),
                UI.Setting(L.T("Only files downloaded from the internet"), null, UI.Toggle(L.T("Downloads only"), r.WebOnly, delegate (bool v) { then(delegate { r.WebOnly = v; })(); })),
                UI.Setting(L.T("Do this"), L.T("An action, or one of your recipes."), action),
                r.Action == "moveto" || r.Action == "copyto" ? UI.Setting(L.T("To this folder"), null, UI.Row(6, target, browse(target, delegate (string v) { r.Target = v; }))) : null,
                UI.Setting(L.T("When"), null, UI.Row(6, whenBox, dayBox, timeBox)),
                UI.Row(8, tryIt, remove),
                found);
        }

        // ---------- Recipes ----------

        int recipeOpen;

        UIElement RecipesPage()
        {
            List<Recipe> list = RecipeStore.Load();
            Action save = delegate { Change(L.T("Recipes saved"), delegate { RecipeStore.Save(list.Where(r => r.Name.Length > 0 && r.Steps.Count > 0).ToList()); }); };
            var names = new ListBox { Width = 220, Height = 260, Background = UI.T.Field, Foreground = UI.T.Text, BorderBrush = UI.T.CardBorder, ItemContainerStyle = Rows() };
            foreach (Recipe r in list) names.Items.Add(r.Name);
            recipeOpen = Math.Min(recipeOpen, list.Count - 1);
            if (recipeOpen >= 0) names.SelectedIndex = recipeOpen;
            names.SelectionChanged += delegate { recipeOpen = names.SelectedIndex; Refresh(); };

            List<RecipeRecorder.Step> recent = RecipeRecorder.Suggest(DateTime.Now);
            var record = UI.Btn(L.T("Record what I just did"), delegate
            {
                if (recent.Count == 0) { MessageBox.Show(L.T("Nothing to record yet: run one or more actions on some files first (within the last hour)."), "DragFlow"); return; }
                string name = L.T("My recipe");
                for (int i = 2; list.Any(r => r.Name == name); i++) name = L.T("My recipe") + " " + i;
                list.Add(new Recipe { Name = name, Types = RecipeRecorder.TypesFor(recent[0]), Steps = recent.Select(st => st.Id).ToList() });
                recipeOpen = list.Count - 1;
                save(); Refresh();
            }, true, "");
            var add = UI.Btn(L.T("New recipe"), delegate
            {
                list.Add(new Recipe { Name = L.T("New recipe"), Types = "image", Steps = new List<string> { "resize:1920", "strip", "zip" } });
                recipeOpen = list.Count - 1; save(); Refresh();
            });
            var left = UI.Stack(8, names, UI.Row(8, record, add),
                UI.Sub(recent.Count == 0 ? L.T("Run some actions, then Record turns them into a recipe.") : L.T("Last actions:") + " " + string.Join(" › ", recent.Select(st => st.Label).ToArray())));

            UIElement editor = UI.Sub(L.T("A recipe runs several actions in a row; each step works on what the step before made, and everything is one Undo. Recipes appear on the dial under Custom."));
            if (recipeOpen >= 0 && recipeOpen < list.Count)
            {
                Recipe r = list[recipeOpen];
                var steps = new ListBox { Height = 150, Background = UI.T.Field, Foreground = UI.T.Text, BorderBrush = UI.T.CardBorder, ItemContainerStyle = Rows() };
                foreach (string st in r.Steps) steps.Items.Add(RecipeSteps.Label(st));
                var choice = UI.Combo(RecipeSteps.Choices.Select(c => L.T(c[1])), 0, 220, delegate { });
                Action<int, int> move = delegate (int from, int to)
                {
                    if (from < 0 || to < 0 || to >= r.Steps.Count) return;
                    string x = r.Steps[from]; r.Steps.RemoveAt(from); r.Steps.Insert(to, x); save(); Refresh();
                };
                editor = UI.Stack(6,
                    UI.Setting(L.T("Name"), null, UI.Field(r.Name, 260, delegate (string v) { r.Name = v.Trim().Replace("|", "/").Replace(">", "-"); save(); Refresh(); })),
                    UI.Setting(L.T("For"), L.T("* for anything, kinds such as image or pdf, or extensions such as .png"), UI.Field(r.Types, 260, delegate (string v) { r.Types = v.Trim().Length == 0 ? "*" : v.Trim(); save(); })),
                    UI.Text(L.T("Steps"), 13.5, null, true),
                    steps,
                    UI.Row(6, choice, UI.Btn(L.T("Add step"), delegate
                    {
                        string id = RecipeSteps.Choices[choice.SelectedIndex][0];
                        if (id == "moveto" || id == "copyto")
                            using (var d = new System.Windows.Forms.FolderBrowserDialog()) { if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; id += ":" + d.SelectedPath; }
                        r.Steps.Add(id); save(); Refresh();
                    }),
                        UI.Btn("↑", delegate { move(steps.SelectedIndex, steps.SelectedIndex - 1); }),
                        UI.Btn("↓", delegate { move(steps.SelectedIndex, steps.SelectedIndex + 1); }),
                        UI.Btn(L.T("Remove step"), delegate { if (steps.SelectedIndex < 0) return; r.Steps.RemoveAt(steps.SelectedIndex); save(); Refresh(); })),
                    UI.Row(8,
                        UI.Btn(L.T("Run on files…"), delegate
                        {
                            var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = L.F("Run \"{0}\" on which files?", r.Name) };
                            if (d.ShowDialog(this) == true) host.Runner.RunAction("recipe:" + r.Name, r.Name, d.FileNames);
                        }, false, ""),
                        UI.Btn(L.T("Delete recipe"), delegate
                        {
                            if (MessageBox.Show(L.F("Delete the recipe \"{0}\"?", r.Name), "DragFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                            list.Remove(r); recipeOpen = Math.Max(0, recipeOpen - 1); save(); Refresh();
                        })));
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var right = new Border { Child = editor, Margin = new Thickness(20, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return UI.Card(null, null, grid);
        }
    }
}
