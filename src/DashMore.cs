using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DragFlow
{
    /// <summary>Dashboard pages: Shapes and shortcuts, Apps and quiet, Profiles.</summary>
    public sealed partial class DashboardWindow
    {
        // ---------- Shapes and shortcuts ----------

        UIElement ShapesPage()
        {
            string[] choices = ActionCatalog.AllNames.Where(n => n != "Edit" && n != "Tools" && n != "Smart" && n != "Open & share" && n != "Convert" && n != "Resize" && n != "Organize").ToArray();
            var rows = new StackPanel();
            foreach (string shape in Gesture.Shapes)
            {
                string sh = shape;
                var box = new ComboBox { Width = 220, IsEditable = true, FontSize = 13.5 };
                foreach (string c in choices) box.Items.Add(c);
                box.Text = Gesture.ActionFor(S.Gestures, sh) ?? "";
                box.LostKeyboardFocus += delegate { SaveShape(sh, box.Text); };
                box.SelectionChanged += delegate { if (box.SelectedItem != null) SaveShape(sh, (string)box.SelectedItem); };
                rows.Children.Add(UI.Setting(L.F("Shape {0}", sh), ShapeHint(sh), box));
            }

            // a pad to practise on: draw with the mouse, see what DragFlow recognises
            var pad = new Canvas { Width = 360, Height = 220, Background = UI.T.Field, ClipToBounds = true, Cursor = Cursors.Pen };
            var ink = new Polyline { Stroke = UI.T.Accent, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
            pad.Children.Add(ink);
            var result = UI.Sub(L.T("Hold the left button and draw a Z, V, L or N here."));
            Gesture g = null;
            long t0 = 0;
            pad.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                ink.Points.Clear(); g = new Gesture(1); t0 = Environment.TickCount; pad.CaptureMouse();
                result.Text = L.T("Drawing…");
            };
            pad.MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (g == null || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(pad);
                ink.Points.Add(p);
                string shape = g.Add(p.X, p.Y, Environment.TickCount - t0);
                if (shape != null)
                {
                    string action = Gesture.ActionFor(S.Gestures, shape);
                    result.Text = L.F("Recognised {0}", shape) + (action != null ? " → " + L.T(action) : " " + L.T("(no action chosen for it)"));
                    result.Foreground = UI.T.Success;
                }
            };
            pad.MouseLeftButtonUp += delegate
            {
                pad.ReleaseMouseCapture();
                if (g != null && result.Text == L.T("Drawing…")) { result.Text = L.T("Not recognised. Draw it bigger, in one go."); result.Foreground = UI.T.Sub; }
                g = null;
            };
            var padBorder = new Border { Child = pad, CornerRadius = new CornerRadius(6), BorderBrush = UI.T.CardBorder, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };

            var shortcut = UI.Field(S.Hotkey, 180, null);
            shortcut.IsReadOnly = true;
            shortcut.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                e.Handled = true;
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;
                var parts = new List<string>();
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
                string keyName = k >= Key.D0 && k <= Key.D9 ? ((int)(k - Key.D0)).ToString() : k.ToString();
                parts.Add(keyName);
                string text = string.Join("+", parts.ToArray());
                uint mods; System.Windows.Forms.Keys key;
                if (!Hotkey.Parse(text, out mods, out key)) { shortcut.Text = L.T("Add Ctrl, Alt or Shift"); return; }
                shortcut.Text = text;
                Change(L.F("Shortcut is now {0}", text), delegate { S.Hotkey = text; });
            };

            return UI.Stack(0,
                UI.Card(L.T("Shapes while dragging"), L.T("After the dial has gone out of the way, draw a quick shape: its action appears beside the pointer. Move onto it and let go."),
                    UI.Setting(L.T("Use shapes"), null, Flip(L.T("Use shapes"), S.GesturesEnabled, delegate (bool v) { S.GesturesEnabled = v; })),
                    rows),
                UI.Card(L.T("Practise"), L.T("Nothing is moved here; it only shows what DragFlow would recognise."), padBorder, result),
                UI.Card(L.T("Keyboard and right-click"), null,
                    UI.Setting(L.T("Open the dial for the selected files with a shortcut"), null, Flip(L.T("Keyboard shortcut"), S.HotkeyEnabled, delegate (bool v) { S.HotkeyEnabled = v; })),
                    UI.Setting(L.T("Shortcut"), L.T("Click the box, then press the keys you want."), shortcut),
                    UI.Line(),
                    UI.Setting(L.T("\"DragFlow actions…\" in Explorer's right-click menu"), L.T("On Windows 11 it is under Show more options. Send to › DragFlow is always there."),
                        Flip(L.T("Right-click entry"), S.ContextMenu, delegate (bool v) { S.ContextMenu = v; }))),
                UI.Card(L.T("Pen and touch"), null,
                    UI.Setting(L.T("Larger dial for pen and finger drags"), L.T("It also waits a little longer before it gets out of the way."), Flip(L.T("Touch dial"), S.TouchDial, delegate (bool v) { S.TouchDial = v; }))));
        }

        static string ShapeHint(string shape)
        {
            return shape == "Z" ? L.T("Right, back down to the left, right again") : shape == "V" ? L.T("Down to the right, then up to the right")
                 : shape == "L" ? L.T("Down, then right") : L.T("Up, down to the right, up again");
        }

        void SaveShape(string shape, string action)
        {
            action = (action ?? "").Replace("|", " ").Replace("=", " ").Trim();
            if (action == (Gesture.ActionFor(S.Gestures, shape) ?? "")) return;
            string map = string.Join("|", Gesture.Shapes.Select(sh => sh + "=" + (sh == shape ? action : Gesture.ActionFor(S.Gestures, sh) ?? "")).ToArray());
            Change(L.F("Shape {0}: {1}", shape, action.Length == 0 ? L.T("nothing") : action), delegate { S.Gestures = map; });
        }

        // ---------- Apps and quiet ----------

        UIElement QuietPage()
        {
            TextBox excludedApps = UI.Lines(S.ExcludedApps, 90, delegate (List<string> v) { Change(L.T("Quiet programs saved"), delegate { S.ExcludedApps = v.Select(Settings.NormalizeApp).Where(x => x.Length > 0).Distinct().ToList(); }); });
            var running = UI.Combo(Quiet.RunningPrograms().Where(n => n.Length > 0).OrderBy(n => n), -1, 200, delegate { });
            var addRunning = UI.Btn(L.T("Add"), delegate
            {
                if (running.SelectedItem == null) return;
                string n = Settings.NormalizeApp((string)running.SelectedItem);
                if (S.ExcludedApps.Contains(n)) return;
                Change(L.F("\"{0}\" keeps the dial hidden", n), delegate { S.ExcludedApps.Add(n); });
                excludedApps.Text = string.Join("\r\n", S.ExcludedApps.ToArray());
            });
            TextBox folders = UI.Lines(S.ExcludedFolders, 80, delegate (List<string> v) { Change(L.T("Quiet folders saved"), delegate { S.ExcludedFolders = v; }); });
            var browse = UI.Btn(L.T("Add a folder…"), delegate
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    Change(L.T("Quiet folder added"), delegate { if (!S.ExcludedFolders.Contains(d.SelectedPath)) S.ExcludedFolders.Add(d.SelectedPath); });
                    folders.Text = string.Join("\r\n", S.ExcludedFolders.ToArray());
                }
            });
            return UI.Stack(0,
                UI.Card(L.T("Stay quiet"), null,
                    UI.Setting(L.T("While a full-screen game, video or slideshow is open"), null, Flip(L.T("Quiet in full screen"), S.QuietWhenFullScreen, delegate (bool v) { S.QuietWhenFullScreen = v; })),
                    UI.Line(),
                    UI.Setting(L.T("While these programs run"), L.T("One per line, for example obs64 or powerpnt."), UI.Row(8, running, addRunning)),
                    excludedApps,
                    UI.Line(),
                    UI.Setting(L.T("For items in these folders"), null, browse),
                    folders,
                    UI.Line(),
                    UI.Setting(L.T("Pause for a while"), S.IsPaused ? L.F("Paused until {0}", S.PausedUntil.ToShortTimeString()) : null,
                        UI.Row(8, UI.Btn(L.T("1 hour"), delegate { Change(L.T("Paused for an hour"), delegate { S.PausedUntil = DateTime.Now.AddHours(1); }); Refresh(); }),
                                  UI.Btn(L.T("Until tomorrow"), delegate { Change(L.T("Paused until tomorrow"), delegate { S.PausedUntil = DateTime.Today.AddDays(1).AddHours(6); }); Refresh(); }),
                                  S.IsPaused ? UI.Btn(L.T("Resume"), delegate { Change(L.T("Resumed"), delegate { S.PausedUntil = DateTime.MinValue; }); Refresh(); }) : null))),
                UI.Card(L.T("Other programs"), L.T("Also show the dial for drags out of these programs (one per line), such as outlook, olk, chrome, msedge."),
                    UI.Lines(S.OtherApps, 80, delegate (List<string> v) { Change(L.T("Programs saved"), delegate { S.OtherApps = v.Select(Settings.NormalizeApp).Where(x => x.Length > 0 && x != "explorer").Distinct().ToList(); }); })),
                UI.Card(L.T("Shelf"), L.T("A place at the screen edge to park files while you drag; drag them out later."),
                    UI.Setting(L.T("Show the shelf while I drag files"), null, Flip(L.T("Shelf"), S.ShelfEnabled, delegate (bool v) { S.ShelfEnabled = v; })),
                    UI.Setting(L.T("Keep items on the shelf after dragging them out"), null, Flip(L.T("Keep on shelf"), S.ShelfKeepAfterDrag, delegate (bool v) { S.ShelfKeepAfterDrag = v; }))));
        }

        // ---------- Profiles ----------

        int profileIndex;

        UIElement ProfilesPage()
        {
            List<Profile> list = Profiles.Load();
            var names = new ListBox { Width = 220, Height = 300, Background = UI.T.Field, Foreground = UI.T.Text, BorderBrush = UI.T.CardBorder, ItemContainerStyle = Rows() };
            foreach (Profile p in list) names.Items.Add(p.Name.Length > 0 ? p.Name : L.T("(no name)"));
            profileIndex = Math.Min(profileIndex, list.Count - 1);
            if (profileIndex >= 0) names.SelectedIndex = profileIndex;
            bool renaming = false;
            names.SelectionChanged += delegate { if (renaming || names.SelectedIndex < 0) return; profileIndex = names.SelectedIndex; Refresh(); };
            Action save = delegate { Change(L.T("Profiles saved"), delegate { Profiles.Save(list); }); };
            var add = UI.Btn(L.T("New profile"), delegate { list.Add(new Profile { Name = L.T("New profile") }); profileIndex = list.Count - 1; save(); Refresh(); }, true, "");
            var del = UI.Btn(L.T("Delete"), delegate { if (profileIndex < 0) return; list.RemoveAt(profileIndex); profileIndex = Math.Max(0, profileIndex - 1); save(); Refresh(); });

            UIElement editor = UI.Sub(L.T("Profiles put other actions first for one folder (and the folders inside it), or for drags out of certain programs. Make one with New profile."));
            if (profileIndex >= 0 && profileIndex < list.Count)
            {
                Profile p = list[profileIndex];
                Func<List<string>, string> csv = delegate (List<string> v) { return string.Join(", ", v.ToArray()); };
                Func<string, List<string>> split = delegate (string v) { return v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); };
                TextBox folder = UI.Field(p.Folder, 300, delegate (string v) { p.Folder = v.Trim(); save(); });
                editor = UI.Stack(4,
                    UI.Setting(L.T("Name"), null, UI.Field(p.Name, 300, delegate (string v)
                    {
                        p.Name = v.Trim(); save();
                        renaming = true; names.Items[profileIndex] = p.Name; names.SelectedIndex = profileIndex; renaming = false;
                    })),
                    UI.Setting(L.T("Folder"), L.T("Empty when the profile is for programs only."), UI.Row(6, folder, UI.Btn("…", delegate
                    {
                        using (var d = new System.Windows.Forms.FolderBrowserDialog())
                            if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { folder.Text = d.SelectedPath; p.Folder = d.SelectedPath; save(); }
                    }))),
                    UI.Setting(L.T("Programs"), L.T("Drags out of these programs, comma separated (outlook, chrome)."), UI.Field(csv(p.Apps), 300, delegate (string v) { p.Apps = split(v).Select(Settings.NormalizeApp).ToList(); save(); })),
                    UI.Setting(L.T("File kinds"), L.T("image, pdf, video, audio, word, text, archive, folder; empty for all."), UI.Field(csv(p.Types), 300, delegate (string v) { p.Types = split(v.ToLowerInvariant()); save(); })),
                    UI.Setting(L.T("Put first"), L.T("Action names, comma separated."), UI.Field(csv(p.First), 300, delegate (string v) { p.First = split(v); save(); })),
                    UI.Setting(L.T("Leave out"), null, UI.Field(csv(p.Hide), 300, delegate (string v) { p.Hide = split(v); save(); })),
                    UI.Setting(L.T("Move to folders"), L.T("Offered first under Move to; separate with |"), UI.Field(string.Join("|", p.MoveTo.ToArray()), 300, delegate (string v) { p.MoveTo = v.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); save(); })));
            }
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = UI.Stack(8, names, UI.Row(8, add, del));
            var right = new Border { Child = editor, Margin = new Thickness(20, 0, 0, 0) };
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return UI.Card(null, null, grid);
        }
    }
}
