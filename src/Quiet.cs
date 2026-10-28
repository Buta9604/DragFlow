using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DragFlow
{
    /// <summary>
    /// Reasons for the dial to stay out of the way: paused, an excluded program running, a full-screen app or
    /// presentation on another screen, or a drag from a program running as administrator (whose drops Windows
    /// would not deliver to DragFlow anyway).
    /// </summary>
    public static class Quiet
    {
        /// <summary>Raised once when a drag comes from an elevated program, so the user learns why nothing shows.</summary>
        public static Action ElevatedSourceSeen = delegate { };

        static string[] running = new string[0];
        static long runningAt = -100000;
        static int ownIntegrity = -2;

        /// <summary>Lower-case names of running programs, refreshed at most every 3 seconds.</summary>
        public static string[] RunningPrograms()
        {
            long now = Clock.Now;
            if (now - runningAt > 3000)
            {
                try
                {
                    Process[] ps = Process.GetProcesses();
                    running = ps.Select(p => { try { return p.ProcessName.ToLowerInvariant(); } catch (Exception) { return ""; } }).Distinct().ToArray();
                    foreach (Process p in ps) p.Dispose();
                }
                catch (Exception) { }
                runningAt = now;
            }
            return running;
        }

        public static string ExcludedRunning(IEnumerable<string> excluded, IEnumerable<string> runningNow)
        {
            var set = new HashSet<string>(runningNow);
            return excluded.Select(Settings.NormalizeApp).FirstOrDefault(e => e.Length > 0 && set.Contains(e));
        }

        public static bool InFolder(string path, IEnumerable<string> folders)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.TrimEnd('\\');
            foreach (string f in folders)
            {
                string x = (f ?? "").Trim().TrimEnd('\\');
                if (x.Length == 0) continue;
                if (string.Equals(p, x, StringComparison.OrdinalIgnoreCase) || p.StartsWith(x + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Why the dial should stay hidden for this drag, or null to go ahead.</summary>
        public static string Reason(DragSession s, Settings settings)
        {
            if (!settings.Enabled) return "turned off";
            if (settings.IsPaused) return "paused until " + settings.PausedUntil.ToShortTimeString();
            if (settings.ExcludedApps.Count > 0)
            {
                string app = ExcludedRunning(settings.ExcludedApps, RunningPrograms());
                if (app != null) return "excluded program running: " + app;
            }
            if (settings.QuietWhenFullScreen)
            {
                int state;
                if (Native.SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4)) return "presentation or full-screen game";
                if (FullScreenElsewhere(s.OriginX, s.OriginY)) return "full-screen app on another screen";
            }
            if (s.RootWindow != IntPtr.Zero && SourceIsElevated(s.RootWindow))
            {
                ElevatedSourceSeen();
                return "the drag comes from a program running as administrator";
            }
            return null;
        }

        static bool SourceIsElevated(IntPtr window)
        {
            if (ownIntegrity == -2) ownIntegrity = Native.OwnIntegrity();
            int theirs = Native.IntegrityOfWindow(window);
            return theirs > ownIntegrity && ownIntegrity >= 0;
        }

        /// <summary>Is a borderless window covering a whole monitor other than the one the drag is on?</summary>
        static bool FullScreenElsewhere(int x, int y)
        {
            bool found = false;
            IntPtr dragMonitor = Native.MonitorFromPoint(new POINT(x, y), 2);
            Native.MonitorEnumProc proc = delegate (IntPtr mon, IntPtr hdc, ref RECT r, IntPtr data)
            {
                if (mon == dragMonitor) return true;
                var c = new POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                IntPtr root = Native.GetAncestor(Native.WindowFromPoint(c), Native.GA_ROOT);
                if (root == IntPtr.Zero) return true;
                string cls = Native.ClassOf(root);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
                RECT w;
                if (!Native.GetWindowRect(root, out w)) return true;
                bool covers = w.Left <= r.Left && w.Top <= r.Top && w.Right >= r.Right && w.Bottom >= r.Bottom;
                bool captionless = (Native.GetWindowLong(root, -16 /* GWL_STYLE */) & 0x00C00000 /* WS_CAPTION */) != 0x00C00000;
                if (covers && captionless) { found = true; return false; }
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return found;
        }
    }
}
