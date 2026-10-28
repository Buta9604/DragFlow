using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("DragFlow")]
[assembly: AssemblyProduct("DragFlow")]
[assembly: AssemblyDescription("Actions for the files you drag")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace DragFlow
{
    /// <summary>What the app was started with.</summary>
    public sealed class StartOptions
    {
        public string Suffix = "";
        public bool AfterCrash, Updated, Restarted, OpenDial, NoContextMenu;
        /// <summary>Files given on the command line after --dial (Explorer's Send to does this).</summary>
        public string[] DialPaths = new string[0];
    }

    static class Program
    {
        public static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }

        [STAThread]
        static void Main(string[] args)
        {
            string profile = ArgAfter(args, "--profile");
            if (profile != null) AppPaths.UseProfile(Path.GetFullPath(profile));
            InstallCrashHandlers();

            if (args.Contains("--guard"))
            {
                // The guard keeps DragFlow running; only for the real profile (tests and e2e runs use --profile).
                if (profile == null) KeepAlive.Main("", "");
                return;
            }

            if (args.Contains("--uninstall"))
            {
                Application.EnableVisualStyles();
                Uninstaller.Run(args.Contains("--quiet"));
                return;
            }

            // DragFlow must run at the same level as Explorer, or Windows will not deliver drops to it.
            if (Native.OwnIntegrity() >= 0x3000 && !args.Contains("--allow-elevated") && profile == null)
            {
                int shellLevel = Native.IntegrityOfWindow(Native.GetShellWindow());
                if (shellLevel >= 0 && shellLevel < 0x3000)
                {
                    Log.Write("started as administrator; restarting at normal level");
                    Process.Start("explorer.exe", "\"" + Application.ExecutablePath + "\"");
                    return;
                }
            }

            var opts = new StartOptions
            {
                Suffix = profile == null ? "" : "." + Math.Abs(AppPaths.Roaming.ToLowerInvariant().GetHashCode()),
                AfterCrash = args.Contains("--after-crash"),
                Updated = args.Contains("--updated"),
                Restarted = args.Contains("--restart"),
                OpenDial = args.Contains("--dial"),
                NoContextMenu = args.Contains("--no-context-menu"),
                DialPaths = args.Where(a => !a.StartsWith("--") && (File.Exists(a) || Directory.Exists(a))).ToArray()
            };

            bool created;
            using (var mutex = new Mutex(true, "DragFlow.SingleInstance" + opts.Suffix, out created))
            {
                if (!created && (opts.AfterCrash || opts.Updated || opts.Restarted))
                {
                    // The previous copy can take a few seconds to disappear; wait for it instead of giving up.
                    try { created = mutex.WaitOne(15000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created && (opts.AfterCrash || opts.Updated || opts.Restarted || args.Contains("--startup")))
                {
                    Log.Write("already running; this extra start is not needed");
                    return; // started by the guard, the sign-in task and the Run entry together: one is enough, quietly
                }
                if (!created)
                {
                    // Already running: the right-click menu or Send to asks for the dial; a plain second start opens the settings.
                    if (opts.OpenDial && opts.DialPaths.Length > 0)
                        try { File.WriteAllLines(DialRequestFile, opts.DialPaths); } catch (Exception) { }
                    Signal(opts.OpenDial ? "DragFlow.Dial" : "DragFlow.ShowSettings", opts.Suffix);
                    return;
                }
                if (args.Contains("--startup"))
                {
                    // Signing in is busy enough: wait a little, quietly, before starting.
                    try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
                    Thread.Sleep(6000);
                    try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch (Exception) { }
                }
                Updater.CleanUp();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Log.Write("DragFlow " + Version + " starting" + (opts.AfterCrash ? " after a crash" : "") + (opts.Updated ? " after an update" : "") + (opts.Restarted ? " again" : ""));
                Application.Run(new TrayApp(opts));
                Log.Write("DragFlow exiting");
                Log.Flush();
            }
        }

        /// <summary>Where a second start leaves the files it was given for the dial.</summary>
        public static string DialRequestFile { get { return Path.Combine(AppPaths.Local, "dial-request.txt"); } }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);

        static void Signal(string name, string suffix)
        {
            // This copy was started by the user's click, so it may hand the right to come to the front to the running copy.
            try { AllowSetForegroundWindow(-1 /* ASFW_ANY */); } catch (Exception) { }
            try { using (var ev = EventWaitHandle.OpenExisting(name + suffix)) ev.Set(); }
            catch (Exception) { }
        }

        static string ArgAfter(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>The command line for starting DragFlow again: the lasting options (profile, no right-click menu) plus the given flag.</summary>
        public static string RestartArgs(string[] args, string flag)
        {
            var keep = new List<string> { flag };
            string profile = ArgAfter(args, "--profile");
            if (profile != null) { keep.Add("--profile"); keep.Add("\"" + profile.TrimEnd('\\') + "\""); }
            foreach (string sticky in new[] { "--no-context-menu", "--allow-elevated" }) if (args.Contains(sticky)) keep.Add(sticky);
            return string.Join(" ", keep);
        }

        /// <summary>Start DragFlow again with the same lasting options (used after an update or a settings move/restore).</summary>
        public static void StartAgain(string flag)
        {
            if (AppPaths.IsDefault) KeepAlive.SignalStop(""); // the new copy starts a fresh guard from its own program file
            Process.Start(Application.ExecutablePath, RestartArgs(Environment.GetCommandLineArgs().Skip(1).ToArray(), flag));
        }

        static void InstallCrashHandlers()
        {
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Log.Error("UI thread", e.Exception);
                TrayApp.ReportError(e.Exception);
            };
            TaskScheduler.UnobservedTaskException += delegate (object s, UnobservedTaskExceptionEventArgs e)
            {
                Log.Error("background task", e.Exception);
                e.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("fatal", e.ExceptionObject as Exception);
                Log.Flush();
                if (e.IsTerminating) RestartAfterCrash();
            };
        }

        /// <summary>Start again after a crash, but never more than once a minute (no crash loops).</summary>
        static void RestartAfterCrash()
        {
            try
            {
                if (AppPaths.IsDefault && KeepAlive.IsRunning("") && !KeepAlive.StopRequested) return; // the guard restarts it, with sensible waits
                string stamp = Path.Combine(AppPaths.Local, "last-crash.txt");
                DateTime last;
                if (File.Exists(stamp) && DateTime.TryParse(File.ReadAllText(stamp), out last) && DateTime.Now - last < TimeSpan.FromMinutes(1)) return;
                Directory.CreateDirectory(AppPaths.Local);
                File.WriteAllText(stamp, DateTime.Now.ToString("o"));
                Process.Start(Application.ExecutablePath, "--after-crash");
            }
            catch (Exception) { }
        }
    }
}
