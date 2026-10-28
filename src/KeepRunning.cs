using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>
    /// A Windows scheduled task that starts DragFlow when you sign in (next to the "Run" entry, so it still starts when a
    /// tuning tool deletes that). Per user, normal rights: no administrator needed. It follows "Start with Windows",
    /// including when you switch DragFlow off in Task Manager's Startup apps.
    /// </summary>
    public static class StartupTask
    {
        public const string Name = "DragFlow";
        static readonly object gate = new object();

        /// <summary>A short id for an exe path, kept in the task's description (plain ASCII, so the comparison never depends on code pages).</summary>
        internal static string IdOf(string exe)
        {
            using (var sha = SHA256.Create())
                return "id:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(exe.ToLowerInvariant()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        internal static string Xml(string exe)
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Starts DragFlow when you sign in. " + IdOf(exe) + "</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority>" +
                "<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure><Enabled>true</Enabled></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command><Arguments>--startup</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
        }

        /// <summary>Run schtasks with a real time limit (output read as it comes, so a full pipe never blocks it).</summary>
        static int Schtasks(string args, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            var sb = new StringBuilder();
            using (Process p = Process.Start(psi))
            {
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch (Exception) { } output = "timed out"; return -1; }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        /// <summary>The id of the program the task starts ("id:…"), "" for a task without one, or null when there is no task.</summary>
        public static string Target()
        {
            try
            {
                string xml;
                if (Schtasks("/Query /TN \"" + Name + "\" /XML", out xml) != 0) return null;
                int i = xml.IndexOf("id:", StringComparison.Ordinal);
                return i < 0 || i + 19 > xml.Length ? "" : xml.Substring(i, 19);
            }
            catch (Exception) { return null; }
        }

        public static bool Install(string exe)
        {
            string file = Path.Combine(Path.GetTempPath(), "dragflow-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(file, Xml(exe), Encoding.Unicode);
                string output;
                int rc = Schtasks("/Create /TN \"" + Name + "\" /XML \"" + file + "\" /F", out output);
                if (rc != 0) Log.Write("sign-in task not created: " + output.Trim());
                return rc == 0;
            }
            catch (Exception ex) { Log.Error("sign-in task", ex); return false; }
            finally { try { File.Delete(file); } catch (Exception) { } }
        }

        public static void Remove()
        {
            try { string output; Schtasks("/Delete /TN \"" + Name + "\" /F", out output); } catch (Exception) { }
        }

        /// <summary>
        /// Switched off in Task Manager › Startup apps (or Settings › Apps › Startup)? Windows keeps the Run entry and marks
        /// it off in StartupApproved; the task must respect that too.
        /// </summary>
        public static bool DisabledInStartupApps()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                {
                    var v = k == null ? null : k.GetValue("DragFlow") as byte[];
                    return v != null && v.Length > 0 && (v[0] & 1) == 1;
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>Make the task match "start with Windows" (and the current place of DragFlow.exe). Only for the real profile.</summary>
        public static void Sync(bool startWithWindows)
        {
            if (!AppPaths.IsDefault) return;
            lock (gate)
            {
                bool want = startWithWindows && !DisabledInStartupApps();
                string target = Target();
                if (!want) { if (target != null) Remove(); return; }
                if (target != IdOf(Application.ExecutablePath)) Install(Application.ExecutablePath);
            }
        }

        /// <summary>The same, on a background thread (schtasks takes a moment); the wish is read when it runs, so the last one wins.</summary>
        public static void SyncLater()
        {
            if (!AppPaths.IsDefault) return;
            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                try { Sync(Settings.StartsWithWindows); } catch (Exception ex) { Log.Error("sign-in task", ex); }
            });
        }
    }

    /// <summary>
    /// The guard: a second, tiny DragFlow process ("DragFlow.exe --guard") that starts DragFlow again when it stops
    /// without being asked to (a crash, or ended in Task Manager). It waits a little longer after each restart and gives up
    /// after five in ten minutes, so a broken install never loops. It leaves when DragFlow is closed on purpose, updated or
    /// restarted (the new DragFlow starts a fresh guard from the new program file), and DragFlow in turn restarts it.
    /// </summary>
    public static class KeepAlive
    {
        static readonly int[] WaitsMs = { 5000, 30000, 120000 };
        public const int MaxRestarts = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        static string StopFile { get { return Path.Combine(AppPaths.Local, "guard-stop.txt"); } }
        static string StatusFile { get { return Path.Combine(AppPaths.Local, "guard.txt"); } }

        public static string MainMutex(string suffix) { return "DragFlow.SingleInstance" + suffix; }
        static string GuardMutex(string suffix) { return "DragFlow.Guard" + suffix; }
        static string StopEvent(string suffix) { return "DragFlow.GuardStop" + suffix; }

        /// <summary>"session": off until the next sign-in; "always": off until DragFlow is started by hand.</summary>
        public static void AskToStop(string how)
        {
            try { Directory.CreateDirectory(AppPaths.Local); File.WriteAllText(StopFile, how); } catch (Exception) { }
            SignalStop("");
        }

        /// <summary>Tell a running guard to leave now (an update or restart is about to start a new DragFlow).</summary>
        public static void SignalStop(string suffix)
        {
            try { using (var ev = EventWaitHandle.OpenExisting(StopEvent(suffix))) ev.Set(); } catch (Exception) { }
        }

        /// <summary>DragFlow is running and wants to be watched: no stop note.</summary>
        public static void Clear()
        {
            try { if (File.Exists(StopFile)) File.Delete(StopFile); } catch (Exception) { }
        }

        public static bool StopRequested { get { return File.Exists(StopFile); } }

        public static bool IsRunning(string suffix) { return Exists(GuardMutex(suffix)); }

        static bool Exists(string mutex)
        {
            try
            {
                Mutex m;
                if (!Mutex.TryOpenExisting(mutex, out m)) return false;
                m.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException) { return true; } // it exists, owned by an administrator copy
        }

        /// <summary>Start the guard if it is not running (called by DragFlow at start and every half minute).</summary>
        public static void Ensure(string suffix, string profileArgs)
        {
            if (IsRunning(suffix) || StopRequested) return;
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, ("--guard " + profileArgs).Trim()) { UseShellExecute = false }); }
            catch (Exception ex) { Log.Error("starting the guard", ex); }
        }

        /// <summary>What the guard last reported, for the dashboard.</summary>
        public static string Status()
        {
            try { return File.Exists(StatusFile) ? File.ReadAllText(StatusFile).Trim() : null; } catch (Exception) { return null; }
        }

        static void Report(string text)
        {
            try { Directory.CreateDirectory(AppPaths.Local); File.WriteAllText(StatusFile, text); } catch (Exception) { }
        }

        /// <summary>
        /// The guard's loop. sleep returns false when the guard was told to leave; quit says the program file changed
        /// (an update). Returns when DragFlow was closed on purpose, after an update, or after giving up.
        /// </summary>
        public static void Run(Func<long> now, Func<int, bool> sleep, Func<bool> mainRunning, Action startMain, Func<bool> quit)
        {
            var restarts = new System.Collections.Generic.List<long>();
            Report("watching");
            int missing = 0;
            while (true)
            {
                if (!sleep(2000) || quit()) { Report("stopped: DragFlow was updated or restarted"); return; }
                if (StopRequested) { Report("stopped: DragFlow was closed on purpose"); return; }
                if (mainRunning()) { missing = 0; continue; }
                // Gone. An update or a restart has a moment between the old copy leaving and the new one starting.
                if (++missing < 2) continue;
                restarts.RemoveAll(t => now() - t > (long)Window.TotalMilliseconds);
                if (restarts.Count >= MaxRestarts)
                {
                    Report("gave up: DragFlow stopped " + restarts.Count + " times in 10 minutes");
                    Log.Write("guard: giving up after " + restarts.Count + " restarts");
                    return;
                }
                int wait = WaitsMs[Math.Min(restarts.Count, WaitsMs.Length - 1)];
                Log.Write("guard: DragFlow is not running; starting it in " + wait / 1000 + " s");
                if (!sleep(wait) || quit()) { Report("stopped: DragFlow was updated or restarted"); return; }
                if (StopRequested) { Report("stopped: DragFlow was closed on purpose"); return; }
                if (mainRunning()) { missing = 0; continue; }
                restarts.Add(now());
                startMain();
                Report("watching (restarted DragFlow " + restarts.Count + " time(s), last at " + DateTime.Now.ToString("HH:mm") + ")");
                missing = 0;
                if (!sleep(5000)) { Report("stopped: DragFlow was updated or restarted"); return; } // give it time to start
            }
        }

        static string FileVersion()
        {
            try { return FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion; } catch (Exception) { return null; }
        }

        /// <summary>Entry point for "--guard": one guard per user, no window, its own log file.</summary>
        public static void Main(string suffix, string profileArgs)
        {
            Log.Name = "guard";
            bool created;
            using (var m = new Mutex(true, GuardMutex(suffix), out created))
            using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEvent(suffix)))
            {
                if (!created) return;
                Log.Write("guard started");
                string version = FileVersion();
                Run(delegate { return Clock.Now; },
                    delegate (int ms) { return !stop.WaitOne(ms); },
                    delegate { return Exists(MainMutex(suffix)); },
                    delegate
                    {
                        try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, ("--after-crash " + profileArgs).Trim()) { UseShellExecute = false }); }
                        catch (Exception ex) { Log.Error("guard restart", ex); }
                    },
                    delegate { return FileVersion() != version; }); // the program was updated: the new DragFlow starts its own guard
                Log.Write("guard stopped");
                Log.Flush();
            }
        }
    }
}
