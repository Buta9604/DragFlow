using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace DragFlow
{
    /// <summary>
    /// When a drag starts, works out (on one long-lived background thread, so the pointer never stalls) whether the dial
    /// should show, what is being dragged, and which actions to offer. This only shapes the menu: the real list of
    /// files always comes from the drop itself.
    /// </summary>
    public static class SelectionResolver
    {
        static readonly object gate = new object();
        static readonly AutoResetEvent signal = new AutoResetEvent(false);
        static DragSession next;
        static Thread worker;
        static dynamic shell;

        /// <summary>Start the worker; it pays for the slow first UI Automation and Shell calls up front.</summary>
        public static void Start()
        {
            lock (gate)
            {
                if (worker != null) return;
                worker = new Thread(Loop) { IsBackground = true, Name = "DragFlow selection" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
        }

        /// <summary>Resolve a drag. Only the newest request matters; older pending ones are dropped.</summary>
        public static void Request(DragSession session)
        {
            Start();
            lock (gate) next = session;
            signal.Set();
        }

        // ---------- what is under a press (runs on the drag thread; must be quick) ----------

        static readonly int ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>The surface at a point if presses there may start a drag DragFlow cares about.</summary>
        public static Surface Probe(int x, int y)
        {
            IntPtr at = Native.WindowFromPoint(new POINT(x, y));
            IntPtr root = Native.GetAncestor(at, Native.GA_ROOT);
            if (root == IntPtr.Zero) return null;
            uint pid;
            Native.GetWindowThreadProcessId(root, out pid);
            if (pid == ownPid) return null; // our own dial, shelf or dialogs
            string cls = Native.ClassOf(root);
            Log.Debug("mouse down on class " + cls);
            if (cls == "CabinetWClass")
                return new Surface { Root = root, Tab = ShellInterop.TabAt(at, root), Class = cls, Process = "explorer", Source = SourceKind.Explorer };
            if (cls == "Progman" || cls == "WorkerW")
                return new Surface { Root = root, Class = cls, Process = "explorer", Source = SourceKind.Desktop };
            Settings settings = Settings.Current;
            if (settings.OtherApps.Count == 0) return null;
            string name = Native.ProcessNameOf(root);
            if (name.Length == 0 || !settings.OtherApps.Any(a => Settings.NormalizeApp(a) == name)) return null;
            return new Surface { Root = root, Class = cls, Process = name, Source = SourceKind.App };
        }

        // ---------- the background work ----------

        static void Loop()
        {
            try { AutomationElement.FromPoint(new System.Windows.Point(0, 0)); } catch (Exception) { }
            try { ConnectShell(); } catch (Exception) { }
            try { ActionCatalog.WarmUp(); } catch (Exception) { }
            while (true)
            {
                signal.WaitOne();
                DragSession s;
                lock (gate) { s = next; next = null; }
                if (s != null) Resolve(s);
            }
        }

        static void ConnectShell()
        {
            if (shell == null) shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
        }

        public static void Resolve(DragSession s)
        {
            long t0 = Clock.Now;
            try
            {
                Settings settings = Settings.Current;
                string quiet = s.Explicit ? null : Quiet.Reason(s, settings);
                if (quiet != null) { s.QuietReason = quiet; s.FileState = -1; Log.Write("dial stays hidden: " + quiet); return; }

                FileKind? hint = null;
                if (s.Source == SourceKind.App)
                {
                    string name;
                    if (!FindAppItem(s.OriginX, s.OriginY, out hint, out name)) { s.FileState = -1; return; }
                }
                else if (!s.Explicit && FindItem(s.OriginX, s.OriginY) == null) { s.FileState = -1; return; }

                string[] paths = null;
                string folder = null;
                var open = new List<string>();
                try
                {
                    if (s.Source == SourceKind.Explorer) paths = ExplorerSelection(s, out folder, open);
                    else if (s.Source == SourceKind.Desktop)
                    {
                        paths = DesktopSelection();
                        folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        OpenFolders(IntPtr.Zero, open);
                    }
                }
                catch (Exception ex) { Log.Error("selection", ex); }
                if (folder == null && paths != null) folder = FileOps.ParentOf(paths[0]);

                if (Quiet.InFolder(folder, settings.ExcludedFolders) || (paths != null && paths.Any(p => Quiet.InFolder(p, settings.ExcludedFolders))))
                {
                    s.QuietReason = "excluded folder"; s.FileState = -1; Log.Write("dial stays hidden: excluded folder");
                    return;
                }

                var ctx = new MenuContext
                {
                    Paths = paths, Folder = folder, OpenFolders = open.Where(o => !string.Equals(o, folder, StringComparison.OrdinalIgnoreCase)).ToList(),
                    ShelfEnabled = settings.ShelfEnabled, KindHint = hint, App = string.IsNullOrEmpty(s.Process) ? "explorer" : s.Process
                };
                if (paths != null) ctx.Kind = FileOps.KindOf(paths);
                s.Paths = paths;
                s.Folder = folder;
                s.Kind = hint ?? ctx.Kind;
                s.Summary = Summarize(paths);
                s.Menu = ActionCatalog.Build(ctx);
                s.FileState = 1;
                Preview.Start(s);
                Log.Debug("resolved " + Log.P(paths) + " in " + (Clock.Now - t0) + " ms");
            }
            catch (Exception ex)
            {
                Log.Error("resolve", ex);
                s.FileState = 1; // if automation fails, still allow the dial; the drop carries the files
            }
        }

        /// <summary>"photo.jpg · 2.4 MB", "5 items · 12 MB", "3 folders"; null when unknown.</summary>
        public static string Summarize(string[] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            int folders = paths.Count(Directory.Exists);
            long bytes = 0;
            foreach (string p in paths.Where(File.Exists).Take(500))
            {
                try { bytes += new FileInfo(p).Length; } catch (IOException) { }
            }
            string count = paths.Length == 1 ? (folders == 1 ? "1 folder" : "1 file") : folders == paths.Length ? paths.Length + " folders" : paths.Length + " items";
            return folders == paths.Length ? count : count + "\n" + FileOps.FormatSize(bytes);
        }

        static AutomationElement FindItem(int x, int y)
        {
            try { return FindItemNow(x, y); }
            catch (ElementNotAvailableException) { return null; } // the window closed while we looked: nothing to drag there
        }

        static AutomationElement FindItemNow(int x, int y)
        {
            AutomationElement el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            for (int i = 0; i < 5 && el != null; i++)
            {
                ControlType ct = el.Current.ControlType;
                if (ct == ControlType.ListItem || ct == ControlType.DataItem || ct == ControlType.TreeItem) return el;
                if (ct == ControlType.Window || ct == ControlType.Pane && i > 3) break;
                el = TreeWalker.ControlViewWalker.GetParent(el);
            }
            return null;
        }

        static readonly Regex FileName = new Regex(@"[^\\/:*?""<>|\r\n]+\.([A-Za-z0-9]{2,5})\b");

        /// <summary>
        /// In another app (mail, browser): is the press on something that drags as a file — a picture, a link, an
        /// attachment? Text being selected is not. Guesses the file kind from the element's name ("report.pdf, 2 MB").
        /// </summary>
        static bool FindAppItem(int x, int y, out FileKind? hint, out string name)
        {
            hint = null; name = null;
            AutomationElement el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            for (int i = 0; i < 4 && el != null; i++)
            {
                ControlType ct = el.Current.ControlType;
                string n = el.Current.Name ?? "";
                if (ct == ControlType.Image) { hint = FileKind.Image; name = n; return true; }
                if (ct == ControlType.ListItem || ct == ControlType.DataItem || ct == ControlType.Button || ct == ControlType.Hyperlink || ct == ControlType.TreeItem)
                {
                    Match m = FileName.Match(n);
                    if (m.Success) { name = m.Value.Trim(); hint = FileOps.ClassifyName(name); return true; }
                    if (ct == ControlType.Hyperlink || ct == ControlType.ListItem) { name = n; return true; }
                }
                if (ct == ControlType.Document || ct == ControlType.Edit || ct == ControlType.Text || ct == ControlType.Window) break;
                el = TreeWalker.ControlViewWalker.GetParent(el);
            }
            return false;
        }

        /// <summary>Selected items in the Explorer tab the drag started in, plus the folders open in other windows and tabs.</summary>
        static string[] ExplorerSelection(DragSession s, out string folder, List<string> open)
        {
            folder = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    ConnectShell();
                    return FindSelection(s, out folder, open);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    shell = null; // Explorer restarted; reconnect once
                }
            }
            return null;
        }

        static string[] FindSelection(DragSession s, out string folder, List<string> open)
        {
            folder = null;
            string[] result = null;
            long target = s.RootWindow.ToInt64();
            var candidates = new List<dynamic>();
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    string here = FolderOf(w);
                    if (Convert.ToInt64(w.HWND) != target)
                    {
                        if (here != null && !open.Contains(here, StringComparer.OrdinalIgnoreCase)) open.Add(here);
                        continue;
                    }
                    IntPtr tab = ShellInterop.TabOf((object)w);
                    if (s.TabWindow != IntPtr.Zero && tab != IntPtr.Zero && tab != s.TabWindow)
                    {
                        if (here != null && !open.Contains(here, StringComparer.OrdinalIgnoreCase)) open.Add(here); // another tab of this window
                        continue;
                    }
                    candidates.Add(w);
                }
                catch (Exception) { }
            }
            foreach (dynamic w in candidates)
            {
                try
                {
                    var list = new List<string>();
                    foreach (dynamic it in w.Document.SelectedItems()) list.Add((string)it.Path);
                    if (list.Count == 0) continue;
                    folder = FolderOf(w);
                    result = list.ToArray();
                    break;
                }
                catch (Exception) { }
            }
            return result;
        }

        static void OpenFolders(IntPtr exceptRoot, List<string> open)
        {
            try
            {
                ConnectShell();
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        string here = FolderOf(w);
                        if (here != null && !open.Contains(here, StringComparer.OrdinalIgnoreCase)) open.Add(here);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { shell = null; }
        }

        static string FolderOf(dynamic w)
        {
            try
            {
                string p = (string)w.Document.Folder.Self.Path;
                return !string.IsNullOrEmpty(p) && Directory.Exists(p) ? p.TrimEnd('\\') : null;
            }
            catch (Exception) { return null; }
        }

        static string[] DesktopSelection()
        {
            try { return ShellInterop.DesktopSelection(); }
            catch (Exception ex) { Log.Error("desktop selection", ex); return null; }
        }
    }
}
