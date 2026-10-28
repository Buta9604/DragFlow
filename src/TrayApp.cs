using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>The running app: tray icon, mouse hook, dial, shelf, shortcut, rules, updates.</summary>
    sealed partial class TrayApp : ApplicationContext
    {
        static TrayApp instance;

        readonly NotifyIcon tray = new NotifyIcon();
        readonly Settings settings;
        readonly StartOptions opts;
        readonly MouseHook hook = new MouseHook();
        readonly DragController controller;
        readonly RingOverlay overlay = new RingOverlay();
        readonly ActionRunner runner;
        readonly CornerNotifier notifier;
        readonly ShelfWindow shelf;
        readonly ClickDial clickDial;
        readonly Hotkey hotkey;
        readonly RuleEngine rules = new RuleEngine();
        readonly Control marshal = new Control();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 15 };
        readonly System.Windows.Forms.Timer health = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly ToolStripMenuItem enabledItem = new ToolStripMenuItem();
        readonly ToolStripMenuItem pauseItem = new ToolStripMenuItem();
        readonly ToolStripMenuItem undoMenu = new ToolStripMenuItem();
        readonly ToolStripMenuItem dialItem = new ToolStripMenuItem();
        readonly List<EventWaitHandle> signals = new List<EventWaitHandle>();
        readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>();
        long lastErrorNote = -100000;
        string trayState;
        bool updateOffered;

        public TrayApp(StartOptions opts)
        {
            instance = this;
            this.opts = opts;
            marshal.CreateControl();
            IntPtr force = marshal.Handle; // make sure BeginInvoke works from the first drop

            settings = Settings.Current = Settings.Load();
            if (opts.NoContextMenu) { settings.ContextMenu = false; settings.Save(); }
            L.Use(settings.Language);
            Log.IncludePaths |= settings.LogPaths;
            Learning.Current = Learning.Load();
            Learning.Current.Enabled = settings.Learning;
            Usage.Current = Usage.Load();
            ShelfStore.Current = ShelfStore.Load();
            ShelfStore.Current.Prune();
            ActionCatalog.Favorites = settings.Favorites;
            CustomActions.EnsureFile();
            try { UndoLog.Prune(); } catch (Exception ex) { Log.Error("undo housekeeping", ex); }
            try { AppPaths.CleanConvertFolder(); } catch (Exception ex) { Log.Error("temporary files", ex); }

            notifier = new CornerNotifier(marshal);
            runner = new ActionRunner(marshal, notifier);
            runner.Park = delegate (string[] p) { ShelfStore.Current.Add(p); };
            Job.Confirm = delegate (string question)
            {
                Func<bool> ask = delegate { return MessageBox.Show(question, "DragFlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; };
                return marshal.InvokeRequired ? (bool)marshal.Invoke(ask) : ask();
            };
            shelf = new ShelfWindow(ShelfStore.Current, delegate { return Settings.Current; });
            clickDial = new ClickDial(runner, notifier);
            hotkey = new Hotkey(delegate { clickDial.OpenForSelection(); });

            controller = new DragController(overlay, new RealDesktop());
            controller.SetTimer = delegate (bool on) { tick.Enabled = on; };
            controller.FileDragStarted += delegate (DragSession s) { if (s.Source != SourceKind.App || s.Kind != FileKind.Other) shelf.BeginDragZone(s.OriginX, s.OriginY); };
            controller.FileDragEnded += delegate { shelf.EndDragZone(); };
            tick.Tick += delegate { controller.Tick(Clock.Now); };
            overlay.Dropped += delegate (ActionItem item, string[] files, DropInfo info)
            {
                controller.NoteDrop();
                runner.Run(item, files, info, controller.Session);
            };
            var bubble = new GestureBubble();
            bubble.Dropped += delegate (ActionItem item, string[] files, DropInfo info)
            {
                controller.NoteDrop();
                runner.Run(item, files, info, controller.Session);
            };
            controller.Bubble = bubble;

            DragDetector d = hook.Detector;
            d.Probe = SelectionResolver.Probe;
            d.Bypass = delegate { return Native.KeyDown(Native.VK_MENU); };
            d.Enabled = settings.Enabled;
            d.ThresholdX = Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDRAG));
            d.ThresholdY = Math.Max(4, Native.GetSystemMetrics(Native.SM_CYDRAG));
            d.Started += delegate (DragSession s)
            {
                SelectionResolver.Request(s);
                marshal.BeginInvoke((Action)delegate { controller.OnStarted(s, Clock.Now); });
            };
            d.Moved += delegate (int x, int y, long t) { controller.OnMoved(x, y, t); };
            d.Ended += delegate (DragSession s) { marshal.BeginInvoke((Action)delegate { controller.OnEnded(s, Clock.Now); }); };
            hook.Problem += delegate (string why) { notifier.Show(L.T("DragFlow cannot watch the mouse"), L.T("Windows refused the mouse hook. Try restarting DragFlow.") + " (" + why + ")"); };

            Quiet.ElevatedSourceSeen = delegate
            {
                marshal.BeginInvoke((Action)delegate
                {
                    if (settings.ElevationHintShown) return;
                    settings.ElevationHintShown = true;
                    settings.Save();
                    notifier.Show(L.T("Dragging from an administrator window"),
                        L.T("That window runs as administrator, so Windows will not let DragFlow receive its drops. The dial stays hidden there."));
                });
            };

            rules.Run = delegate (Rule rule, string[] files) { runner.RunAction(rule.ActionId, L.T("Rule") + ": " + rule.Name, files); };
            rules.Paused = delegate (Rule rule, string why) { notifier.Show(L.T("Rule paused"), "\"" + rule.Name + "\": " + why + ". " + L.T("It starts again when you change the rules or restart DragFlow.")); };

            BuildTray();
            health.Tick += delegate { HealthCheck(); };

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.PowerModeChanged += OnPower;

            try { hook.Start(); }
            catch (Exception ex) { Log.Error("hook", ex); notifier.Show(L.T("DragFlow cannot watch the mouse"), ex.Message); }
            health.Start();
            SelectionResolver.Start();
            ApplyIntegrations(true);
            if (AppPaths.IsDefault)
            {
                KeepAlive.Clear(); // started (at sign-in or by hand): the guard may watch again
                KeepGuard(true);
                StartupTask.SyncLater();
            }

            Listen("DragFlow.ShowSettings" + opts.Suffix, ShowSettings);
            Listen("DragFlow.Dial" + opts.Suffix, delegate
            {
                string[] given = null;
                try
                {
                    if (File.Exists(Program.DialRequestFile)) { given = File.ReadAllLines(Program.DialRequestFile); File.Delete(Program.DialRequestFile); }
                }
                catch (Exception) { }
                if (given != null && given.Length > 0) clickDial.OpenForPaths(given); else clickDial.OpenForSelection();
            });
            SettingsWindow.RunRuleNow = delegate (Rule rule) { return rules.RunNow(rule); };
            SettingsWindow.Restart = delegate
            {
                Program.StartAgain("--restart");
                ExitThread();
            };
            Listen("DragFlow.Exit" + opts.Suffix, delegate { ExitThread(); });

            if (ShelfStore.Current.Groups.Count > 0) marshal.BeginInvoke((Action)shelf.ShowIfNeeded);
            if (!settings.FirstRunDone && AppPaths.IsDefault)
                marshal.BeginInvoke((Action)delegate { WelcomeWindow.ShowOnce(settings); ApplyIntegrations(false); });
            else if (opts.Updated) notifier.Show(L.T("DragFlow updated"), L.F("You now have version {0}.", Program.Version));
            else if (opts.AfterCrash) notifier.Show(L.T("DragFlow restarted"), L.T("It stopped unexpectedly and started again. Your undo history is kept."));
            if (AppPaths.MissingSettingsFolder != null)
                notifier.Show(L.T("Settings folder not found"), L.F("{0} is not available right now, so DragFlow is using its default settings folder for this session. Changes you make now stay there.", AppPaths.MissingSettingsFolder));
            if (opts.OpenDial)
            {
                var later = new System.Windows.Forms.Timer { Interval = 1500 };
                later.Tick += delegate { later.Dispose(); if (opts.DialPaths.Length > 0) clickDial.OpenForPaths(opts.DialPaths); else clickDial.OpenForSelection(); };
                later.Start();
            }
        }

        /// <summary>A named signal another start of DragFlow (or the uninstaller) can send; runs the action on the UI thread.</summary>
        void Listen(string name, Action action)
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            signals.Add(ev);
            var t = new Thread(delegate ()
            {
                while (true)
                {
                    ev.WaitOne();
                    try { marshal.BeginInvoke(action); } catch (Exception) { return; }
                }
            }) { IsBackground = true, Name = "DragFlow signal " + name };
            t.Start();
        }

        /// <summary>Keyboard shortcut, right-click menu entry and folder rules, as the settings say.</summary>
        void ApplyIntegrations(bool starting)
        {
            hotkey.Clear();
            if (settings.HotkeyEnabled && !hotkey.Set(settings.Hotkey))
                notifier.Show(L.T("Shortcut not available"), L.F("{0} is used by another program. Choose another in Settings > Shortcuts.", settings.Hotkey));
            dialItem.Text = L.T("Actions for selected files") + (settings.HotkeyEnabled ? "\t" + settings.Hotkey : "");
            if (AppPaths.IsDefault) // never touch the registry when running with a test profile
            {
                try
                {
                    if (settings.ContextMenu) ExplorerMenu.Register(Application.ExecutablePath);
                    else ExplorerMenu.Unregister();
                }
                catch (Exception ex) { Log.Error("right-click menu", ex); }
            }
            rules.Apply(RuleStore.Load(), settings.RulesEnabled);
            if (!starting) Log.Write("settings applied: rules watching " + rules.Watching + " folder(s)");
        }

        public static void ReportError(Exception ex)
        {
            TrayApp t = instance;
            if (t == null) return;
            if (Clock.Now - t.lastErrorNote < 30000) return;
            t.lastErrorNote = Clock.Now;
            t.notifier.Show(L.T("Something went wrong"), ex.Message + " " + L.T("DragFlow kept running; details are in the log."));
        }

        void BuildTray()
        {
            enabledItem.Click += delegate { SetEnabled(!settings.Enabled); };
            pauseItem.Click += delegate
            {
                settings.PausedUntil = settings.IsPaused ? DateTime.MinValue : DateTime.Now.AddHours(1);
                settings.Save();
                controller.Cancel("paused", Clock.Now);
                UpdateTrayState();
            };
            dialItem.Click += delegate { clickDial.OpenForSelection(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(enabledItem);
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(dialItem);
            menu.Items.Add(undoMenu);
            menu.Items.Add(L.T("Activity…"), null, delegate { ActivityWindow.ShowWindow(runner); });
            menu.Items.Add(L.T("Save last actions as a recipe…"), null, delegate { SaveRecipeDialog.Show(); });
            menu.Items.Add(L.T("Settings…"), null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Check for updates"), null, delegate { CheckForUpdates(true); });
            menu.Items.Add(L.T("Report a problem…"), null, delegate { ReportProblem(); });
            menu.Items.Add(L.T("Exit"), null, delegate { AskExit(); });
            menu.Opening += delegate
            {
                enabledItem.Text = L.T("Enabled");
                enabledItem.Checked = settings.Enabled;
                pauseItem.Text = settings.IsPaused ? L.F("Resume (paused until {0})", settings.PausedUntil.ToShortTimeString()) : L.T("Pause for 1 hour");
                pauseItem.Enabled = settings.Enabled;
                undoMenu.Text = L.T("Undo");
                FillUndoMenu();
            };

            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowSettings(); };
            UpdateTrayState();
        }

        /// <summary>The tray icon shows the state: blue when on, amber when paused, grey when off.</summary>
        void UpdateTrayState()
        {
            string state = !settings.Enabled ? "off" : settings.IsPaused ? "paused" : "on";
            if (state == trayState) return;
            trayState = state;
            Icon ic;
            if (!icons.TryGetValue(state, out ic)) icons[state] = ic = MakeIcon(state);
            tray.Icon = ic;
            string text = state == "off" ? L.T("turned off") : state == "paused" ? L.F("paused until {0}", settings.PausedUntil.ToShortTimeString()) : L.T("drag a file to see actions");
            string tip = "DragFlow " + Program.Version + " – " + text;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void FillUndoMenu()
        {
            undoMenu.DropDownItems.Clear();
            var history = UndoLog.History();
            if (history.Count == 0)
            {
                undoMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("Nothing to undo")) { Enabled = false });
                return;
            }
            foreach (UndoEntry e in history.Take(12))
            {
                string id = e.Id;
                string when = e.Time.Date == DateTime.Today ? e.Time.ToShortTimeString() : e.Time.ToString("ddd HH:mm");
                undoMenu.DropDownItems.Add(e.Title + " (" + FileOps.Plural(e.ItemCount, "item", "items") + ", " + when + ")", null,
                    delegate { runner.UndoNow(id); });
            }
            undoMenu.DropDownItems.Add(new ToolStripSeparator());
            undoMenu.DropDownItems.Add(L.T("Clear undo history"), null, delegate
            {
                if (MessageBox.Show(L.T("Forget everything DragFlow could undo? Your files are not touched."), "DragFlow", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    UndoLog.Clear();
            });
        }

        void SetEnabled(bool on)
        {
            settings.Enabled = on;
            hook.Detector.Enabled = on;
            if (!on) controller.Cancel("turned off", Clock.Now);
            settings.Save();
            UpdateTrayState();
        }

        void HealthCheck()
        {
            POINT p;
            if (Native.GetCursorPos(out p)) hook.CheckHealth(p.X, p.Y);
            KeepGuard(false);
            UpdateTrayState();
            // Once a day, if an update source is set, look for a new version (in the background).
            if (!updateOffered && settings.UpdateFeed.Length > 0 && DateTime.Now - settings.LastUpdateCheck > TimeSpan.FromDays(1))
            {
                settings.LastUpdateCheck = DateTime.Now;
                settings.Save();
                CheckForUpdates(false);
            }
        }

        /// <summary>Look for a newer version; offer to install it.</summary>
        void CheckForUpdates(bool userAsked)
        {
            string feed = settings.UpdateFeed;
            if (feed.Length == 0)
            {
                if (userAsked) notifier.Show(L.T("No update source set"), L.T("Choose where new versions are published in Settings > Shortcuts & updates."),
                    new NoteButton(L.T("Settings"), ShowSettings));
                return;
            }
            Task.Factory.StartNew(delegate
            {
                try
                {
                    UpdateInfo info = Updater.Check(feed);
                    if (info == null) { if (userAsked) notifier.Show(L.T("Up to date"), L.F("DragFlow {0} is the newest version.", Program.Version)); return; }
                    updateOffered = true;
                    notifier.Show(L.F("DragFlow {0} is available", info.Version.ToString(3)), string.IsNullOrEmpty(info.Notes) ? L.T("Install it now? DragFlow restarts by itself.") : info.Notes,
                        new NoteButton(L.T("Install"), delegate { InstallUpdate(feed, info); }));
                }
                catch (Exception ex)
                {
                    Log.Error("update check", ex);
                    if (userAsked) notifier.Show(L.T("Could not check for updates"), ex.Message);
                }
            });
        }

        void InstallUpdate(string feed, UpdateInfo info)
        {
            Task.Factory.StartNew(delegate
            {
                try
                {
                    string staged = Updater.Download(feed, info);
                    for (int waited = 0; ActionRunner.Running > 0 && waited < 600; waited++)
                    {
                        if (waited == 0) notifier.Show(L.T("Update ready"), L.T("It installs as soon as the current job is finished."));
                        Thread.Sleep(1000);
                    }
                    if (ActionRunner.Running > 0) { notifier.Show(L.T("Update postponed"), L.T("DragFlow is still busy. Try Check for updates again later.")); return; }
                    marshal.BeginInvoke((Action)delegate
                    {
                        try
                        {
                            Updater.Install(staged, Application.ExecutablePath);
                            Program.StartAgain("--updated");
                            ExitThread();
                        }
                        catch (Exception ex) { notifier.Show(L.T("Could not install the update"), ex.Message); }
                    });
                }
                catch (Exception ex) { notifier.Show(L.T("Could not install the update"), ex.Message); }
            });
        }

        void OnSessionSwitch(object s, SessionSwitchEventArgs e)
        {
            marshal.BeginInvoke((Action)delegate
            {
                controller.Cancel("session " + e.Reason, Clock.Now);
                if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
                    hook.Reinstall("session " + e.Reason);
            });
        }

        void OnDisplayChanged(object s, EventArgs e)
        {
            marshal.BeginInvoke((Action)delegate { controller.Cancel("display settings changed", Clock.Now); });
        }

        void OnPower(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) marshal.BeginInvoke((Action)delegate { controller.Cancel("resumed", Clock.Now); hook.Reinstall("resumed from sleep"); });
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.PowerModeChanged -= OnPower;
            tray.Visible = false;
            tick.Stop(); health.Stop();
            overlay.Close();
            hook.Dispose();
            hotkey.Dispose();
            rules.Dispose();
            shelf.Close();
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
