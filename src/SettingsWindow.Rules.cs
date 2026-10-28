using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Settings › Rules: folder rules with conditions, schedules and recipes.</summary>
    public sealed partial class SettingsWindow
    {
        static readonly System.Collections.Generic.Dictionary<string, int> Widths = new System.Collections.Generic.Dictionary<string, int>
        {
            { "On", 32 },
            { "Name", 100 },
            { "Folder", 140 },
            { "Pattern", 80 },
            { "Action", 160 },
            { "Target", 130 },
            { "Sub", 70 },
            { "Age", 70 },
            { "Size", 70 },
            { "Scan", 60 },
            { "When", 120 },
            { "Web", 80 }
        };

        TabPage RulesPage()
        {
            TabPage p = Page(L.T("Rules"));
            rulesOn.Text = L.T("Run these rules: when a new file lands in a folder, DragFlow does the action by itself");
            rulesOn.SetBounds(12, 8, 560, 24); rulesOn.Checked = s.RulesEnabled;
            rulesGrid.SetBounds(12, 38, 560, 262);
            rulesGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rulesGrid.RowHeadersVisible = false;
            rulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            rulesGrid.ScrollBars = ScrollBars.Both;
            rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "On", HeaderText = L.T("On"), FillWeight = 25 });
            rulesGrid.Columns.Add("Name", L.T("Name"));
            rulesGrid.Columns.Add("Folder", L.T("Watch folder"));
            rulesGrid.Columns.Add("Pattern", L.T("Files"));
            var action = new DataGridViewComboBoxColumn { Name = "Action", HeaderText = L.T("Do"), FlatStyle = FlatStyle.Flat };
            foreach (string[] c in RuleActions.Choices) action.Items.Add(L.T(c[1]));
            foreach (Recipe rc in RecipeStore.Load()) action.Items.Add(RuleActions.Label("recipe:" + rc.Name));
            rulesGrid.Columns.Add(action);
            rulesGrid.Columns.Add("Target", L.T("Target folder"));
            rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Sub", HeaderText = L.T("Subfolders"), FillWeight = 45 });
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Age", HeaderText = L.T("Older than (days)"), FillWeight = 45 });
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = L.T("Bigger than (MB)"), FillWeight = 45 });
            rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Scan", HeaderText = L.T("Hourly scan"), FillWeight = 40 });
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "When", HeaderText = L.T("When (e.g. daily 18:00)"), FillWeight = 60 });
            rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Web", HeaderText = L.T("Downloads only"), FillWeight = 40 });
            foreach (DataGridViewColumn col in rulesGrid.Columns) col.Width = Widths.ContainsKey(col.Name) ? Widths[col.Name] : 100;
            foreach (Rule r in RuleStore.Load())
            {
                string label = RuleActions.Label(r.Action);
                if (label != null && !action.Items.Contains(label)) action.Items.Add(label);
                rulesGrid.Rows.Add(r.Enabled, r.Name, r.Folder, r.Pattern, label, r.Target, r.Subfolders,
                    r.MinAgeDays > 0 ? r.MinAgeDays.ToString() : "", r.MinSizeMB > 0 ? r.MinSizeMB.ToString() : "", r.Scan, r.Schedule ?? "", r.WebOnly);
            }
            var pick = new Button { Text = L.T("Pick folder for the selected cell…"), Left = 12, Top = 372, Width = 250, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            pick.Click += delegate
            {
                DataGridViewCell cell = rulesGrid.CurrentCell;
                if (cell == null) return;
                string col = rulesGrid.Columns[cell.ColumnIndex].Name;
                if (col != "Folder" && col != "Target") { MessageBox.Show(this, L.T("Click a Watch folder or Target folder cell first."), "DragFlow"); return; }
                using (var d = new FolderBrowserDialog())
                {
                    if (d.ShowDialog() != DialogResult.OK) return;
                    if (cell.OwningRow.IsNewRow) rulesGrid.Rows.Add(true, "", col == "Folder" ? d.SelectedPath : "", "*", null, col == "Target" ? d.SelectedPath : "", false, "", "", false, "", false);
                    else cell.Value = d.SelectedPath;
                }
            };
            var runNow = new Button { Text = L.T("Run selected rule on existing files now"), Left = 270, Top = 372, Width = 290, Height = 30, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            runNow.Click += delegate
            {
                DataGridViewRow row = rulesGrid.CurrentRow;
                if (row == null || row.IsNewRow || RunRuleNow == null) return;
                Rule rule = RuleFromRow(row);
                string problem = rule == null ? L.T("Choose an action first.") : rule.Problem();
                if (problem != null) { MessageBox.Show(this, problem, "DragFlow"); return; }
                try { MessageBox.Show(this, L.F("Started on {0} file(s).", RunRuleNow(rule)), "DragFlow"); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragFlow"); }
            };
            p.Controls.Add(Note(L.T("Files: patterns such as *.pdf;*.docx. New files are handled once they have finished downloading; \"older than\" needs the hourly scan. When: empty for \"as files arrive\", or daily 18:00, weekly sun 18:00. Do can also run a recipe. Every run can be undone (except the Recycle Bin)."), 306, 62));
            p.Controls[p.Controls.Count - 1].Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.AddRange(new Control[] { rulesOn, rulesGrid, pick, runNow });
            return p;
        }

        static Rule RuleFromRow(DataGridViewRow r)
        {
            string label = Cell(r, "Action");
            string[] choice = RuleActions.Choices.FirstOrDefault(c => L.T(c[1]) == label);
            string prefix = L.T("Recipe:") + " ";
            if (choice == null && label.StartsWith(prefix)) choice = new[] { "recipe:" + label.Substring(prefix.Length), label };
            if (choice == null) return null;
            int age, size;
            int.TryParse(Cell(r, "Age"), out age);
            int.TryParse(Cell(r, "Size"), out size);
            return new Rule
            {
                Enabled = r.Cells["On"].Value is bool && (bool)r.Cells["On"].Value,
                Name = Cell(r, "Name").Length > 0 ? Cell(r, "Name") : FileOps.NameOf(Cell(r, "Folder")),
                Folder = Cell(r, "Folder"), Pattern = Cell(r, "Pattern").Length > 0 ? Cell(r, "Pattern") : "*",
                Action = choice[0], Target = Cell(r, "Target"),
                Subfolders = r.Cells["Sub"].Value is bool && (bool)r.Cells["Sub"].Value,
                MinAgeDays = Math.Max(0, age), MinSizeMB = Math.Max(0, size),
                Scan = (r.Cells["Scan"].Value is bool && (bool)r.Cells["Scan"].Value) || age > 0,
                Schedule = Cell(r, "When"),
                WebOnly = r.Cells["Web"].Value is bool && (bool)r.Cells["Web"].Value
            };
        }
    }
}
