using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragFlow
{
    /// <summary>Dashboard pages: Home, General, Dial.</summary>
    public sealed partial class DashboardWindow
    {
        // ---------- Home ----------

        UIElement HomePage()
        {
            bool on = S.Enabled && !S.IsPaused;
            string state = !S.Enabled ? L.T("DragFlow is off") : S.IsPaused ? L.F("DragFlow is paused until {0}", S.PausedUntil.ToShortTimeString()) : L.T("DragFlow is on");
            string sub = !S.Enabled ? L.T("The dial does not appear when you drag.")
                       : S.KeepRunning ? L.T("It starts when you sign in and comes back by itself if it stops.") : L.T("It starts when you sign in.");
            var pause = S.IsPaused ? UI.Btn(L.T("Resume"), delegate { Change(L.T("Resumed"), delegate { S.PausedUntil = DateTime.MinValue; }); Refresh(); })
                                   : UI.Btn(L.T("Pause for 1 hour"), delegate { Change(L.T("Paused for an hour"), delegate { S.PausedUntil = DateTime.Now.AddHours(1); }); Refresh(); }, false, "");
            var status = UI.Card(null, null, UI.Setting(state, sub, UI.Row(10, S.Enabled ? pause : null,
                UI.Toggle(L.T("DragFlow on"), S.Enabled, delegate (bool v) { Change(v ? L.T("DragFlow turned on") : L.T("DragFlow turned off"), delegate { S.Enabled = v; }); Refresh(); }))));
            ((StackPanel)status.Child).Children[0].SetValue(TextBlock.FontSizeProperty, 15.0);

            // health
            string guard = KeepAlive.Status();
            bool guardOk = !S.KeepRunning || (guard != null && guard.StartsWith("watching")) || !AppPaths.IsDefault;
            List<Rule> rules = RuleStore.Load();
            int active = rules.Count(r => r.Enabled && r.Problem() == null), broken = rules.Count(r => r.Enabled && r.Problem() != null);
            AiUsage.MonthTotal month = AiUsage.For(DateTime.Now);
            var health = UI.Wrap(
                UI.Tile(L.T("Mouse watcher"), host.HookOk ? L.T("Working") : L.T("Not working"), host.HookOk ? UI.T.Success : UI.T.Danger, ""),
                UI.Tile(L.T("Keep running"), !S.KeepRunning ? L.T("Off") : guardOk ? L.T("Watching") : L.T("Check Help"), guardOk ? UI.T.Success : UI.T.Warning, ""),
                UI.Tile(L.T("Rules"), broken > 0 ? L.F("{0} need attention", broken) : L.F("{0} active", active), broken > 0 ? UI.T.Warning : null, ""),
                UI.Tile(L.T("AI this month"), AiClient.HasKey ? "$" + month.Cost.ToString("0.00", CultureInfo.InvariantCulture) : L.T("Not set up"), null, ""));

            // numbers
            List<ActivityEntry> all = ActivityLog.Load();
            int week = all.Count(e => e.Time > DateTime.Now.AddDays(-7));
            var common = all.Where(e => e.Time > DateTime.Now.AddDays(-30)).GroupBy(e => e.Title).OrderByDescending(g => g.Count()).FirstOrDefault();
            int minutes = week * 20 / 60;
            var numbers = UI.Wrap(
                UI.Tile(L.T("Actions this week"), week.ToString(), null, ""),
                UI.Tile(L.T("Used most this month"), common == null ? "–" : common.Key, null, ""),
                UI.Tile(L.T("Time saved, roughly"), minutes >= 60 ? (minutes / 60) + " h " + (minutes % 60) + " min" : minutes + " min", null, ""));

            // quick switches
            var quick = UI.Wrap(
                QuickSwitch(L.T("Shapes"), S.GesturesEnabled, delegate (bool v) { S.GesturesEnabled = v; }),
                QuickSwitch(L.T("Preview in the dial"), S.Preview, delegate (bool v) { S.Preview = v; }),
                QuickSwitch(L.T("Rules"), S.RulesEnabled, delegate (bool v) { S.RulesEnabled = v; }),
                AiClient.HasKey ? QuickSwitch(L.T("AI helper"), S.AiEnabled, delegate (bool v) { S.AiEnabled = v; }) : null,
                QuickSwitch(L.T("Shelf"), S.ShelfEnabled, delegate (bool v) { S.ShelfEnabled = v; }));

            // recent activity
            var list = new StackPanel();
            foreach (ActivityEntry e in all.AsEnumerable().Reverse().Take(8))
            {
                ActivityEntry entry = e;
                Button undo = null;
                if (!string.IsNullOrEmpty(entry.UndoId))
                    undo = UI.Btn(L.T("Undo"), delegate { host.Runner.UndoNow(entry.UndoId); });
                Button show = null;
                if (!string.IsNullOrEmpty(entry.Output) && (File.Exists(entry.Output) || Directory.Exists(entry.Output)))
                    show = UI.Btn(L.T("Show"), delegate { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + entry.Output.TrimEnd('\\') + "\""); });
                list.Children.Add(UI.Setting(entry.Title + " · " + entry.Result, When(entry.Time), UI.Row(6, show, undo)));
            }
            if (list.Children.Count == 0) list.Children.Add(UI.Sub(L.T("Nothing yet. Drag a file in Explorer and pause for a moment to see the dial.")));

            return UI.Stack(0, status, Heading(L.T("Health")), health, Heading(L.T("Your week")), numbers,
                Heading(L.T("Quick switches")), UI.Card(null, null, quick), Heading(L.T("Recent activity")), UI.Card(null, null, list));
        }

        static string When(DateTime t)
        {
            return t.Date == DateTime.Today ? L.T("Today") + " " + t.ToShortTimeString()
                 : t.Date == DateTime.Today.AddDays(-1) ? L.T("Yesterday") + " " + t.ToShortTimeString() : t.ToString("ddd d MMM HH:mm");
        }

        static TextBlock Heading(string text)
        {
            TextBlock t = UI.Text(text, 14, null, true);
            t.Margin = new Thickness(0, 14, 0, 8);
            return t;
        }

        UIElement QuickSwitch(string name, bool on, Action<bool> set)
        {
            var row = UI.Row(10, Flip(name, on, set), UI.Text(name, 13.5));
            row.Margin = new Thickness(0, 4, 28, 4);
            return row;
        }

        // ---------- General ----------

        UIElement GeneralPage()
        {
            bool startup = false;
            try { startup = Settings.StartsWithWindows; } catch (Exception) { }
            var sens = new Slider { Minimum = 5, Maximum = 20, TickFrequency = 5, IsSnapToTickEnabled = false, Width = 220, Value = Math.Max(5, Math.Min(20, S.Sensitivity * 10)) };
            sens.PreviewMouseUp += delegate { double v = Math.Round(sens.Value) / 10.0; if (Math.Abs(v - S.Sensitivity) > 0.001) Change(L.T("How far you can move: saved"), delegate { S.Sensitivity = v; }); };
            sens.LostKeyboardFocus += delegate { double v = Math.Round(sens.Value) / 10.0; if (Math.Abs(v - S.Sensitivity) > 0.001) Change(L.T("How far you can move: saved"), delegate { S.Sensitivity = v; }); };

            var langs = new List<string> { "auto", "en" };
            foreach (string dir in L.Folders().Where(Directory.Exists))
                foreach (string f in Directory.GetFiles(dir, "*.txt")) langs.Add(Path.GetFileNameWithoutExtension(f));
            langs = langs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var language = UI.Combo(langs.Select(x => x == "auto" ? L.T("Windows language") : x == "en" ? "English" : x), Math.Max(0, langs.IndexOf(S.Language)), 200,
                delegate (int i) { Change(L.T("Language saved (some texts change after a restart)"), delegate { S.Language = langs[i]; }); });

            return UI.Stack(0,
                UI.Card(L.T("Starting"), null,
                    UI.Setting(L.T("Start with Windows"), L.T("DragFlow starts a few seconds after you sign in."),
                        UI.Toggle(L.T("Start with Windows"), startup, delegate (bool v)
                        {
                            try { Settings.StartsWithWindows = v; } catch (Exception ex) { Log.Error("startup", ex); }
                            Saved(v ? L.T("DragFlow starts with Windows") : L.T("DragFlow no longer starts with Windows"));
                        })),
                    UI.Line(),
                    UI.Setting(L.T("Keep DragFlow running"), L.T("If it stops without being asked to (a crash, or ended in Task Manager), a small guard starts it again. Exit in the tray menu still turns it off."),
                        Flip(L.T("Keep DragFlow running"), S.KeepRunning, delegate (bool v) { S.KeepRunning = v; })),
                    UI.Sub(GuardText())),
                UI.Card(L.T("The dial"), null,
                    UI.Setting(L.T("Show the dial when I drag a file or folder"), null, Flip(L.T("Show the dial"), S.Enabled, delegate (bool v) { S.Enabled = v; })),
                    UI.Line(),
                    UI.Setting(L.T("How far you can move before the dial hides"), L.T("Lower: it gets out of the way sooner. Higher: it stays longer. A quick flick never shows it."), sens),
                    UI.Line(),
                    UI.Setting(L.T("Extra \"Move to\" folders"), L.T("One per line; they are offered first under Move to."), null),
                    UI.Lines(S.Favorites, 80, delegate (List<string> v) { Change(L.T("Move to folders saved"), delegate { S.Favorites = v; ActionCatalog.Favorites = v; }); })),
                UI.Card(L.T("Language and log"), null,
                    UI.Setting(L.T("Language"), L.T("Translations are files in the lang folder."), language),
                    UI.Line(),
                    UI.Setting(L.T("Include file names in the diagnostic log"), L.T("Only if someone helping you asks for it."), Flip(L.T("File names in the log"), S.LogPaths, delegate (bool v) { S.LogPaths = v; }))));
        }

        static string GuardText()
        {
            if (!AppPaths.IsDefault) return L.T("(The guard only runs for the real settings, not for a test profile.)");
            string st = KeepAlive.Status();
            return st == null ? L.T("The guard has not reported yet.") : L.T("Guard:") + " " + st;
        }

        // ---------- Dial ----------

        static readonly string[][] Kinds =
        {
            new[] { "Pictures", "photo.jpg" }, new[] { "PDFs", "report.pdf" }, new[] { "Folders", "" }, new[] { "Videos", "clip.mp4" },
            new[] { "Text files", "notes.txt" }, new[] { "Documents", "letter.docx" }, new[] { "Archives", "files.zip" }
        };
        int dialKind;

        List<ActionItem> SampleMenu(int kind)
        {
            string sample = Kinds[kind][1];
            if (sample.Length == 0) sample = AppPaths.LocalDir("convert"); // a real folder
            else sample = Path.Combine(Path.GetTempPath(), sample);
            return ActionCatalog.For(new[] { sample });
        }

        /// <summary>The dial as it would look for this kind of file, drawn by the dial's own renderer.</summary>
        Image DialPicture(int kind)
        {
            List<ActionItem> menu = SampleMenu(kind);
            var geo = new RadialGeometry(menu.Count, Math.Max(1.0, Math.Min(1.5, S.DialSize)));
            var view = new RadialView
            {
                Geo = geo, Items = menu, Theme = Theme.For(S), Open = 1, Hover = new float[menu.Count], Heading = new float[menu.Count],
                LabelWidths = RadialRenderer.MeasurePills(menu, geo.Scale), Summary = "1 " + Kinds[kind][0].ToLowerInvariant().TrimEnd('s'), LabelIndex = 0
            };
            view.Hover[0] = 1;
            using (System.Drawing.Bitmap bmp = RadialRenderer.Render(view))
            {
                IntPtr h = bmp.GetHbitmap(System.Drawing.Color.FromArgb(0));
                try
                {
                    BitmapSource full = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    int c = geo.HalfSize, r = (int)Math.Min(c, (RadialGeometry.OuterR + 36) * geo.Scale);
                    var src = new CroppedBitmap(full, new Int32Rect(c - r, c - r, 2 * r, 2 * r)); // just the dial and its label, not the room for options
                    src.Freeze();
                    return new Image { Source = src, Width = 2 * r, Height = 2 * r, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
                }
                finally { Native.DeleteObject(h); }
            }
        }

        UIElement DialPage()
        {
            var kinds = UI.Combo(Kinds.Select(k => L.T(k[0])), dialKind, 180, delegate (int i) { dialKind = i; Refresh(); });
            List<ActionItem> menu = SampleMenu(dialKind);
            var slices = new StackPanel();
            foreach (ActionItem item in menu)
            {
                string kids = item.IsParent ? string.Join(", ", item.Children.Select(k => k.Label).ToArray()) : "";
                slices.Children.Add(UI.Setting(item.Label, kids.Length == 0 ? null : kids, null));
            }
            var preview = UI.Card(L.T("Preview"), L.T("The dial for this kind of file, with your order, hidden actions, size and colours."),
                UI.Row(12, UI.Text(L.T("For"), 13.5), kinds),
                new Grid { Children = { DialPicture(dialKind) } },
                slices);

            var sizes = new[] { 1.0, 1.25, 1.5 };
            var look = UI.Card(L.T("Look"), null,
                UI.Setting(L.T("Colours"), null, UI.Combo(new[] { L.T("Follow Windows"), L.T("Light"), L.T("Dark") }, S.ThemeMode == "light" ? 1 : S.ThemeMode == "dark" ? 2 : 0, 180,
                    delegate (int i) { Change(L.T("Colours saved"), delegate { S.ThemeMode = i == 1 ? "light" : i == 2 ? "dark" : "auto"; }); Refresh(); })),
                UI.Setting(L.T("Size"), null, UI.Combo(new[] { L.T("Normal"), L.T("Large"), L.T("Extra large") }, Math.Max(0, Array.IndexOf(sizes, S.DialSize)), 180,
                    delegate (int i) { Change(L.T("Dial size saved"), delegate { S.DialSize = sizes[i]; }); Refresh(); })),
                UI.Setting(L.T("Animations"), null, UI.Combo(new[] { L.T("Follow Windows"), L.T("On"), L.T("Off") }, S.Motion == "on" ? 1 : S.Motion == "off" ? 2 : 0, 180,
                    delegate (int i) { Change(L.T("Animations saved"), delegate { S.Motion = i == 1 ? "on" : i == 2 ? "off" : "auto"; }); })),
                UI.Setting(L.T("Thumbnail and details of what is dragged"), null, Flip(L.T("Preview in the dial"), S.Preview, delegate (bool v) { S.Preview = v; })),
                UI.Setting(L.T("Learn how I drag"), L.T("When the dial should open, and where you never want it."), Flip(L.T("Learning"), S.Learning, delegate (bool v) { S.Learning = v; })),
                UI.Setting(L.T("Put the actions I use most first"), null, Flip(L.T("Ranking"), S.Ranking, delegate (bool v) { S.Ranking = v; })));

            // always first
            var pinned = new ListBox { Height = 120, Background = UI.T.Field, Foreground = UI.T.Text, BorderBrush = UI.T.CardBorder, ItemContainerStyle = Rows() };
            foreach (string p in S.PinnedActions) pinned.Items.Add(p);
            var add = UI.Combo(ActionCatalog.AllNames.Select(n => L.T(n)), -1, 200, delegate { });
            add.IsEditable = false;
            Action savePins = delegate { var v = pinned.Items.Cast<string>().ToList(); Change(L.T("Order saved"), delegate { S.PinnedActions = v; }); };
            var pinCard = UI.Card(L.T("Always first"), L.T("These actions come first, in this order, whenever the dial offers them."),
                pinned,
                UI.Row(8, add,
                    UI.Btn(L.T("Add"), delegate { if (add.SelectedIndex < 0) return; string n = ActionCatalog.AllNames[add.SelectedIndex]; if (!pinned.Items.Contains(n)) { pinned.Items.Add(n); savePins(); } }),
                    UI.Btn("↑", delegate { int i = pinned.SelectedIndex; if (i <= 0) return; object x = pinned.Items[i]; pinned.Items.RemoveAt(i); pinned.Items.Insert(i - 1, x); pinned.SelectedIndex = i - 1; savePins(); }),
                    UI.Btn("↓", delegate { int i = pinned.SelectedIndex; if (i < 0 || i >= pinned.Items.Count - 1) return; object x = pinned.Items[i]; pinned.Items.RemoveAt(i); pinned.Items.Insert(i + 1, x); pinned.SelectedIndex = i + 1; savePins(); }),
                    UI.Btn(L.T("Remove"), delegate { if (pinned.SelectedIndex < 0) return; pinned.Items.RemoveAt(pinned.SelectedIndex); savePins(); })));

            // shown and hidden
            var checks = new WrapPanel();
            foreach (string name in ActionCatalog.AllNames)
            {
                string n = name;
                var cb = new CheckBox { Content = L.T(n), IsChecked = !S.HiddenActions.Contains(n, StringComparer.OrdinalIgnoreCase), Foreground = UI.T.Text, Width = 200, Margin = new Thickness(0, 3, 0, 3) };
                cb.Click += delegate
                {
                    bool show = cb.IsChecked == true;
                    Change(show ? L.F("\"{0}\" is back on the dial", L.T(n)) : L.F("\"{0}\" hidden", L.T(n)), delegate
                    {
                        S.HiddenActions.RemoveAll(h => string.Equals(h, n, StringComparison.OrdinalIgnoreCase));
                        if (!show) S.HiddenActions.Add(n);
                    });
                };
                checks.Children.Add(cb);
            }
            var shown = UI.Card(L.T("Actions on the dial"), L.T("Untick what you never use. Everything else stays where it is."), checks);
            return UI.Stack(0, preview, look, pinCard, shown);
        }
    }
}
