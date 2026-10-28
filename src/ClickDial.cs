using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>
    /// The dial without dragging: opened by a keyboard shortcut or Explorer's right-click menu for whatever is selected
    /// in the Explorer window (or on the desktop) in front. Click an action, or use the arrow keys and Enter.
    /// </summary>
    public sealed class ClickDial
    {
        readonly RingOverlay overlay = new RingOverlay();
        readonly ActionRunner runner;
        readonly INotifier notifier;
        readonly Timer timer = new Timer { Interval = 15 };
        DragSession session;
        long openedAt;
        bool shown;

        public ClickDial(ActionRunner runner, INotifier notifier)
        {
            this.runner = runner; this.notifier = notifier;
            timer.Tick += delegate { Tick(); };
            overlay.Picked += delegate (ActionItem item)
            {
                DragSession s = session;
                if (s == null || s.Paths == null) return;
                var info = new DropInfo { Copy = (Control.ModifierKeys & Keys.Control) != 0 };
                runner.Run(item, s.Paths, info, s);
            };
        }

        public bool Open { get { return overlay.Visible; } }

        /// <summary>Open the dial for these files (Send to, or files on the clipboard), at the pointer.</summary>
        public void OpenForPaths(string[] paths)
        {
            if (overlay.Visible) overlay.Close();
            paths = (paths ?? new string[0]).Where(x => File.Exists(x) || Directory.Exists(x)).ToArray();
            if (paths.Length == 0) { notifier.Show(L.T("Nothing selected"), L.T("Select one or more files or folders first.")); return; }
            Point at = Cursor.Position;
            var s = new DragSession { OriginX = at.X, OriginY = at.Y, Source = SourceKind.Explorer, Process = "explorer", Explicit = true, Paths = paths };
            s.Kind = FileOps.KindOf(paths);
            s.Folder = FileOps.ParentOf(paths[0]);
            s.Summary = SelectionResolver.Summarize(paths);
            s.Menu = ActionCatalog.For(paths);
            s.FileState = 1;
            session = s;
            shown = false;
            openedAt = Clock.Now;
            timer.Start();
        }

        /// <summary>Files on the clipboard (copied in Explorer), if any.</summary>
        public static string[] ClipboardFiles()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return null;
                return Clipboard.GetFileDropList().Cast<string>().ToArray();
            }
            catch (Exception) { return null; }
        }

        /// <summary>Open the dial for the selection in the window in front, at the pointer.</summary>
        public void OpenForSelection()
        {
            if (overlay.Visible) { overlay.Close(); return; }
            IntPtr root = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
            string cls = root == IntPtr.Zero ? "" : Native.ClassOf(root);
            Point p = Cursor.Position;
            var s = new DragSession { OriginX = p.X, OriginY = p.Y, RootWindow = root, RootClass = cls, Process = "explorer", Explicit = true };
            if (cls == "CabinetWClass") { s.Source = SourceKind.Explorer; s.TabWindow = Native.FindWindowEx(root, IntPtr.Zero, "ShellTabWindowClass", null); }
            else if (cls == "Progman" || cls == "WorkerW") s.Source = SourceKind.Desktop;
            else
            {
                // Not in Explorer: copied files on the clipboard will do.
                string[] copied = ClipboardFiles();
                if (copied != null && copied.Length > 0) { OpenForPaths(copied); return; }
                notifier.Show(L.T("Nothing selected"), L.T("Select files in File Explorer or on the desktop (or copy them), then press the shortcut."));
                return;
            }
            session = s;
            shown = false;
            openedAt = Clock.Now;
            SelectionResolver.Request(s);
            timer.Start();
        }

        void Tick()
        {
            long now = Clock.Now;
            DragSession s = session;
            if (s != null && !shown)
            {
                if (s.FileState == 0)
                {
                    if (now - openedAt <= 4000) return;
                    Stop();
                    notifier.Show(L.T("Still looking"), L.T("Explorer did not answer in time. Try again."));
                    return;
                }
                if (s.FileState < 0 || s.Paths == null || s.Menu == null)
                {
                    Stop();
                    notifier.Show(L.T("Nothing selected"), L.T("Select one or more files or folders first."));
                    return;
                }
                double scale = Native.ScaleAt(s.OriginX, s.OriginY) * Math.Max(1.0, Math.Min(1.5, Settings.Current.DialSize));
                Rectangle wa = Screen.FromPoint(new Point(s.OriginX, s.OriginY)).WorkingArea;
                double margin = (RadialGeometry.OuterR + 60) * scale;
                double ax = Math.Max(wa.Left + margin, Math.Min(wa.Right - margin, s.OriginX));
                double ay = Math.Max(wa.Top + margin, Math.Min(wa.Bottom - margin, s.OriginY));
                overlay.ShowForClick(ax, ay, s.Menu, scale, now, s.Summary);
                shown = true;
            }
            Point c = Cursor.Position;
            overlay.Tick(now, c.X, c.Y, -1, (Control.ModifierKeys & Keys.Control) != 0);
            if (shown && !overlay.Visible) Stop();
        }

        void Stop()
        {
            timer.Stop();
            session = null;
            shown = false;
        }
    }

    /// <summary>A system-wide keyboard shortcut ("Ctrl+Alt+D").</summary>
    public sealed class Hotkey : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x312;
        readonly Action pressed;
        bool registered;

        public Hotkey(Action pressed)
        {
            this.pressed = pressed;
            CreateHandle(new CreateParams());
        }

        /// <summary>"Ctrl+Alt+D" → modifiers and key. Returns false for something unusable.</summary>
        public static bool Parse(string text, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            foreach (string raw in (text ?? "").Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 0x2;
                else if (p == "alt") mods |= 0x1;
                else if (p == "shift") mods |= 0x4;
                else if (p == "win" || p == "windows") mods |= 0x8;
                else if (p.Length > 0)
                {
                    Keys k;
                    if (p.Length == 1 && char.IsDigit(p[0])) p = "D" + p;
                    if (!Enum.TryParse(p, true, out k)) return false;
                    key = k;
                }
            }
            return key != Keys.None && mods != 0;
        }

        /// <summary>Use this shortcut. Returns false if it is not valid or another program already has it.</summary>
        public bool Set(string text)
        {
            Clear();
            uint mods; Keys key;
            if (!Parse(text, out mods, out key)) return false;
            registered = Native.RegisterHotKey(Handle, 1, mods | 0x4000 /* MOD_NOREPEAT */, (uint)key);
            return registered;
        }

        public void Clear()
        {
            if (registered) Native.UnregisterHotKey(Handle, 1);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) { try { pressed(); } catch (Exception ex) { Log.Error("shortcut", ex); } return; }
            base.WndProc(ref m);
        }

        public void Dispose() { Clear(); DestroyHandle(); }
    }

    /// <summary>"DragFlow actions…" in Explorer's right-click menu (for this user only; under "Show more options" on Windows 11).</summary>
    public static class ExplorerMenu
    {
        static readonly string[] Roots = { @"Software\Classes\*\shell\DragFlow", @"Software\Classes\Directory\shell\DragFlow" };

        public static bool IsRegistered(string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Roots[0] + @"\command"))
                return k != null && string.Equals(k.GetValue("") as string, Command(exe), StringComparison.OrdinalIgnoreCase);
        }

        static string Command(string exe) { return "\"" + exe + "\" --dial"; }

        public static void Register(string exe)
        {
            if (IsRegistered(exe)) return;
            foreach (string root in Roots)
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(root))
                {
                    k.SetValue("MUIVerb", L.T("DragFlow actions…"));
                    k.SetValue("Icon", "\"" + exe + "\",0");
                    k.SetValue("MultiSelectModel", "Player"); // one launch for any number of selected items
                }
                using (RegistryKey c = Registry.CurrentUser.CreateSubKey(root + @"\command")) c.SetValue("", Command(exe));
            }
            Log.Write("right-click menu entry added");
        }

        public static void Unregister()
        {
            foreach (string root in Roots)
                try { Registry.CurrentUser.DeleteSubKeyTree(root, false); } catch (Exception) { }
        }
    }
}
