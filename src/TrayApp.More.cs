using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>The tray app's settings, problem report, icons, and keeping DragFlow running.</summary>
    sealed partial class TrayApp
    {
        bool settingsOpen;

        /// <summary>The dashboard (Settings… in the tray, or double-click the icon).</summary>
        void ShowSettings()
        {
            try { DashboardWindow.Open(new DashHost(this)); }
            catch (Exception ex)
            {
                // the old window still works if the new one cannot open for some reason
                Log.Error("dashboard", ex);
                ShowClassicSettings();
            }
        }

        /// <summary>The previous settings window, kept for its tables.</summary>
        void ShowClassicSettings()
        {
            if (settingsOpen) return;
            settingsOpen = true;
            try { if (SettingsWindow.Edit(settings)) ApplySettings(); }
            finally { settingsOpen = false; }
        }

        /// <summary>Settings changed (in either window): put them to work.</summary>
        void ApplySettings()
        {
            hook.Detector.Enabled = settings.Enabled;
            ActionCatalog.Favorites = settings.Favorites;
            Learning.Current.Enabled = settings.Learning;
            Log.IncludePaths = settings.LogPaths || Log.Verbose;
            L.Use(settings.Language);
            ApplyIntegrations(false);
            trayState = null;
            UpdateTrayState();
            KeepGuard(true);
            var missing = settings.Favorites.Where(d => !Directory.Exists(d)).ToList();
            if (missing.Count > 0 && missing.Count != lastMissing)
                notifier.Show(L.T("Folder not found"), string.Join(", ", missing.ToArray()) + " " + L.T("will be skipped until it exists."));
            lastMissing = missing.Count;
        }

        int lastMissing;

        /// <summary>What the dashboard may use of the running app.</summary>
        sealed class DashHost : IDashHost
        {
            readonly TrayApp app;
            public DashHost(TrayApp app) { this.app = app; }
            public Settings Settings { get { return app.settings; } }
            public void Apply() { app.ApplySettings(); }
            public ActionRunner Runner { get { return app.runner; } }
            public bool HookOk { get { return app.hook.Installed; } }
            public int RulesWatching { get { return app.rules.Watching; } }
            public void ReportProblem() { app.ReportProblem(); }
            public void CheckForUpdates() { app.CheckForUpdates(true); }
            public void ForgetRuleMemory() { app.rules.ForgetHandled(); }
            public void ShowClassicSettings() { app.ShowClassicSettings(); }
        }

        /// <summary>Zip the logs and basic facts (no file contents) for sending to whoever helps.</summary>
        void ReportProblem()
        {
            try
            {
                Log.Flush();
                string zip = FileOps.UniquePath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DragFlow problem report " + DateTime.Now.ToString("yyyy-MM-dd"), ".zip");
                using (var fs = new FileStream(zip, FileMode.CreateNew))
                using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (string f in Directory.Exists(Log.Dir) ? Directory.GetFiles(Log.Dir, "*.log") : new string[0])
                        za.CreateEntryFromFile(f, "logs/" + Path.GetFileName(f));
                    using (var w = new StreamWriter(za.CreateEntry("about.txt").Open()))
                    {
                        w.WriteLine("DragFlow " + Program.Version);
                        w.WriteLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " 64-bit" : ""));
                        w.WriteLine(".NET " + Environment.Version);
                        w.WriteLine("Hook installed: " + hook.Installed + ", re-installed " + hook.Reinstalls + " time(s)");
                        w.WriteLine("Enabled: " + settings.Enabled + ", paused: " + settings.IsPaused + ", learning: " + settings.Learning);
                        w.WriteLine("Rules watching: " + rules.Watching + " folder(s)");
                        foreach (string l in Learning.Current.Describe()) w.WriteLine(l);
                    }
                }
                Process.Start("explorer.exe", "/select,\"" + zip + "\"");
                notifier.Show(L.T("Problem report saved"), "\"" + Path.GetFileName(zip) + "\" " + L.T("is on your desktop. It holds DragFlow's logs only.") +
                    (settings.LogPaths ? " " + L.T("It includes file names, because that option is on.") : ""));
            }
            catch (Exception ex) { notifier.Show(L.T("Could not save the report"), ex.Message); }
        }

        static Icon MakeIcon(string state)
        {
            Color fill = state == "off" ? Color.FromArgb(150, 155, 165) : state == "paused" ? Color.FromArgb(240, 160, 40) : Color.FromArgb(66, 133, 244);
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var b = new SolidBrush(fill)) g.FillEllipse(b, 1, 1, 30, 30);
                    using (var pen = new Pen(Color.White, 2.5f)) g.DrawEllipse(pen, 7, 7, 18, 18);
                    if (state == "paused")
                        using (var w = new SolidBrush(Color.White)) { g.FillRectangle(w, 12, 11, 3, 10); g.FillRectangle(w, 17, 11, 3, 10); }
                    else using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, 13, 13, 6, 6);
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        int guardTicks;

        /// <summary>
        /// Make the guard match "keep running" while DragFlow runs: on means no stop note and a guard; off means a running
        /// guard is told to leave. Checked at start, after every settings change and every half minute.
        /// </summary>
        void KeepGuard(bool now)
        {
            if (!AppPaths.IsDefault) return;
            if (!now && ++guardTicks % 30 != 0) return;
            if (settings.KeepRunning)
            {
                KeepAlive.Clear();
                KeepAlive.Ensure(opts.Suffix, "");
            }
            else if (KeepAlive.IsRunning(opts.Suffix)) KeepAlive.AskToStop("always");
        }

        /// <summary>Exit from the tray: with the guard on, ask whether it stays off until the next sign-in or for good.</summary>
        void AskExit()
        {
            if (AppPaths.IsDefault && (settings.KeepRunning || KeepAlive.IsRunning(opts.Suffix)))
            {
                string how = ExitDialog.Ask();
                if (how == null) return;
                KeepAlive.AskToStop(how);
                if (how == "always")
                {
                    settings.KeepRunning = false;
                    settings.Save();
                    try { Settings.StartsWithWindows = false; } catch (Exception ex) { Log.Error("startup setting", ex); }
                }
            }
            ExitThread();
        }
    }

    /// <summary>"Turn DragFlow off until I sign in again, or for good?"</summary>
    static class ExitDialog
    {
        public static string Ask()
        {
            using (var f = new Form
            {
                Text = "DragFlow", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false, MinimizeBox = false, TopMost = true, Font = new Font("Segoe UI", 9.5f), AutoScaleMode = AutoScaleMode.Font,
                ClientSize = new Size(440, 150)
            })
            {
                string choice = null;
                var text = new Label { Text = L.T("Turn DragFlow off?") + "\r\n" + L.T("Normally it starts when you sign in and comes back by itself if it stops."), Left = 16, Top = 14, Width = 410, Height = 56 };
                var session = new Button { Text = L.T("Until I sign in again"), Left = 16, Top = 96, Width = 160, Height = 32 };
                var always = new Button { Text = L.T("For good"), Left = 184, Top = 96, Width = 120, Height = 32 };
                var cancel = new Button { Text = L.T("Cancel"), Left = 312, Top = 96, Width = 112, Height = 32, DialogResult = DialogResult.Cancel };
                session.Click += delegate { choice = "session"; f.DialogResult = DialogResult.OK; };
                always.Click += delegate { choice = "always"; f.DialogResult = DialogResult.OK; };
                f.Controls.AddRange(new Control[] { text, session, always, cancel });
                f.AcceptButton = session; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? choice : null;
            }
        }
    }
}
