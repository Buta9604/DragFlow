using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DragFlow
{
    /// <summary>Dashboard pages: Backup and updates, Privacy and data, Help.</summary>
    public sealed partial class DashboardWindow
    {
        // ---------- Backup and updates ----------

        UIElement BackupPage()
        {
            var move = UI.Btn(L.T("Keep settings in another folder…"), delegate
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Choose the folder (for example inside OneDrive). DragFlow copies its settings there and restarts.") })
                {
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    AppPaths.MoveSettingsTo(d.SelectedPath);
                    MessageBox.Show(L.T("Done. DragFlow restarts now to use the new folder."), "DragFlow");
                    if (SettingsWindow.Restart != null) SettingsWindow.Restart();
                }
            }, false, "");
            var backup = UI.Btn(L.T("Back up settings to a file…"), delegate
            {
                var d = new Microsoft.Win32.SaveFileDialog { Filter = "Zip|*.zip", FileName = "DragFlow settings " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip" };
                if (d.ShowDialog(this) != true) return;
                AppPaths.Backup(d.FileName);
                MessageBox.Show(L.T("Saved."), "DragFlow");
            }, false, "");
            var restore = UI.Btn(L.T("Restore settings from a file…"), delegate
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "Zip|*.zip" };
                if (d.ShowDialog(this) != true) return;
                if (MessageBox.Show(L.T("Replace the current settings with the ones in this file? DragFlow restarts afterwards."), "DragFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                AppPaths.Restore(d.FileName);
                if (SettingsWindow.Restart != null) SettingsWindow.Restart();
            }, false, "");
            TextBox feed = UI.Field(S.UpdateFeed, 360, delegate (string v) { Change(L.T("Update source saved"), delegate { S.UpdateFeed = v.Trim(); }); });
            return UI.Stack(0,
                UI.Card(L.T("Your settings"), L.T("Settings, custom actions, profiles, rules, recipes and the shelf live in one folder. Keep it in OneDrive to have the same DragFlow on every PC."),
                    UI.Setting(L.T("Settings folder"), AppPaths.Roaming, null),
                    UI.Wrap(Spaced(move), Spaced(backup), Spaced(restore))),
                UI.Card(L.T("Updates"), L.T("New versions come from a folder or a web address (https) that holds latest.ini. DragFlow checks once a day and asks before installing; only a file with the right checksum is installed."),
                    UI.Setting(L.T("Update source"), L.T("Empty: never look for updates."), feed),
                    UI.Row(10, UI.Btn(L.T("Check now"), delegate { host.CheckForUpdates(); }, false, ""), UI.Sub(L.F("You have version {0}.", Program.Version)))));
        }

        static UIElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 0, 8, 8); return e; }

        // ---------- Privacy and data ----------

        UIElement PrivacyPage()
        {
            Func<string, string, string, Action, UIElement> item = delegate (string name, string where, string confirm, Action clear)
            {
                return UI.Setting(name, where, UI.Btn(L.T("Clear"), delegate
                {
                    if (MessageBox.Show(confirm, "DragFlow", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    clear();
                    MessageBox.Show(L.T("Cleared."), "DragFlow");
                    Refresh();
                }));
            };
            return UI.Stack(0,
                UI.Card(L.T("Nothing leaves your PC unless you ask for it"), L.T("No account, no analytics, no telemetry. Files only go out for AI actions you pick (to Anthropic), To phone (to your own network), Share link (into OneDrive), Share… and Email (to the app you choose)."), null),
                UI.Card(L.T("What DragFlow keeps"), null,
                    item(L.T("How you drag (learning)"), L.T("When the dial should open; no file names."), L.T("Forget how you drag? The dial goes back to its standard timing."), delegate { Learning.Current.Reset(); }),
                    UI.Line(),
                    item(L.T("Which actions you used"), L.T("Puts your favourites first (the last 3000 uses)."), L.T("Forget which actions you used? The dial goes back to its standard order."), delegate { Usage.Current.Clear(); }),
                    UI.Line(),
                    item(L.T("Activity list"), L.T("What DragFlow did (the last 1000 actions)."), L.T("Clear the activity list?"), delegate { ActivityLog.Clear(); }),
                    UI.Line(),
                    item(L.T("Undo history"), L.T("What can still be undone (7 days), with backups of edited files."), L.T("Clear the undo history? What DragFlow did so far can then no longer be undone."), delegate { UndoLog.Clear(); }),
                    UI.Line(),
                    item(L.T("Files rules already handled"), L.T("So rules never redo a file."), L.T("Forget which files rules handled? Rules that look at existing files may then handle them again."), delegate { host.ForgetRuleMemory(); }),
                    UI.Line(),
                    item(L.T("AI usage record"), L.T("Tokens and estimated cost per month."), L.T("Clear the record of AI use?"), delegate { AiUsage.Clear(); }),
                    UI.Line(),
                    AiClient.HasKey ? item(L.T("Your AI API key"), L.T("Stored encrypted for your Windows account."), L.T("Remove the API key from this PC?"), delegate { AiClient.ForgetKey(); S.AiEnabled = false; S.Save(); host.Apply(); }) : null,
                    UI.Setting(L.T("Diagnostic log"), L.T("Two files of at most 1 MB; file names only if you turned that on."), UI.Btn(L.T("Open the log folder"), delegate
                    {
                        Directory.CreateDirectory(Log.Dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Log.Dir + "\"");
                    }))));
        }

        // ---------- Help ----------

        UIElement HelpPage()
        {
            var keys = new StackPanel();
            foreach (string[] k in new[]
            {
                new[] { L.T("Skip the dial once"), L.T("Hold Alt when you start dragging, or press Esc") },
                new[] { L.T("Choose without aiming"), L.T("Press 1 to 9 while the dial is open") },
                new[] { L.T("Copy instead of move"), L.T("Hold Ctrl over Move to") },
                new[] { L.T("Dial for selected files"), S.HotkeyEnabled ? S.Hotkey : L.T("(shortcut off)") },
                new[] { L.T("Find an action on the shortcut dial"), L.T("Type its first letters") },
                new[] { L.T("Shapes"), L.T("After the dial has gone, draw Z, V, L or N while dragging") }
            })
                keys.Children.Add(UI.Setting(k[0], null, UI.Text(k[1], 13.5, UI.T.Sub)));

            var filter = UI.Field("", 260, null);
            var log = new TextBox
            {
                IsReadOnly = true, Height = 260, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Background = UI.T.Field, Foreground = UI.T.Text,
                BorderBrush = UI.T.CardBorder, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Action show = delegate
            {
                Log.Flush();
                string path = Path.Combine(Log.Dir, "dragflow.log");
                string[] lines = new string[0];
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8)) lines = r.ReadToEnd().Split('\n');
                }
                catch (Exception) { }
                string q = filter.Text.Trim();
                var picked = lines.Where(l => q.Length == 0 || l.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Select(l => l.TrimEnd('\r')).ToList();
                log.Text = string.Join("\r\n", picked.Skip(Math.Max(0, picked.Count - 400)).ToArray());
                log.ScrollToEnd();
            };
            filter.TextChanged += delegate { show(); };
            show();
            var hint = UI.Sub(L.T("Search the log"));
            return UI.Stack(0,
                UI.Card(L.T("Keyboard and mouse"), null, keys),
                UI.Card(L.T("Diagnostic log"), L.T("What DragFlow did and any problems it met, newest at the bottom."),
                    UI.Row(8, hint, filter, UI.Btn(L.T("Refresh"), delegate { show(); }, false, "")), log),
                UI.Card(L.T("Something not right?"), null,
                    UI.Setting(L.T("Report a problem"), L.T("Saves a zip of the logs on your desktop to send to whoever helps you. It is not sent anywhere."), UI.Btn(L.T("Save report"), delegate { host.ReportProblem(); })),
                    UI.Setting(L.T("Classic settings window"), L.T("The previous settings window, with every option in tables."), UI.Btn(L.T("Open"), delegate { host.ShowClassicSettings(); })),
                    UI.Setting(L.T("Reset all settings"), L.T("Everything on these pages back to how it was after installing. Rules, recipes, profiles and scripts are kept."), UI.Btn(L.T("Reset"), delegate
                    {
                        if (MessageBox.Show(L.T("Reset all settings to how they were after installing?"), "DragFlow", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                        Change(L.T("All settings reset"), delegate { S.CopyFrom(new Settings { FirstRunDone = true }); });
                        Refresh();
                    }))));
        }
    }
}
