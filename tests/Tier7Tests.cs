using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragFlow.Tests
{
    /// <summary>A pretend running app for the dashboard.</summary>
    sealed class FakeDashHost : IDashHost
    {
        public int Applied, Reports, UpdateChecks, Forgot, Classic;
        public Settings Settings { get; set; }
        public void Apply() { Applied++; }
        public ActionRunner Runner { get { return new ActionRunner(new System.Windows.Forms.Control(), new FakeNotifier()); } }
        public bool HookOk { get { return true; } }
        public int RulesWatching { get { return 2; } }
        public void ReportProblem() { Reports++; }
        public void CheckForUpdates() { UpdateChecks++; }
        public void ForgetRuleMemory() { Forgot++; }
        public void ShowClassicSettings() { Classic++; }
    }

    /// <summary>Tier 7: always running (the guard and the sign-in task) and the dashboard.</summary>
    static class Tier7Tests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Guard();
            Task();
            Dashboard(Make.Dir(root, "dash"));
            Usage();
            Sentences(Make.Dir(root, "rules"));
            Editor();
            ScriptTest(Make.Dir(root, "scripts"));
        }

        /// <summary>The guard's loop with a pretend clock and a pretend DragFlow.</summary>
        static void Guard()
        {
            KeepAlive.Clear();
            long now = 0;
            int starts = 0;
            bool running = true;
            int ticks = 0;
            var sleeps = new List<int>();
            // DragFlow runs for a while, then keeps dying right after each restart
            KeepAlive.Run(delegate { return now; }, delegate (int ms) { now += ms; sleeps.Add(ms); if (++ticks == 5) running = false; return true; },
                delegate { return running; }, delegate { starts++; running = false; }, delegate { return false; });
            Check(starts == KeepAlive.MaxRestarts, "guard: it restarts DragFlow, then gives up after " + KeepAlive.MaxRestarts + " times in 10 minutes (" + starts + ")");
            Check(sleeps.Contains(5000) && sleeps.Contains(30000) && sleeps.Contains(120000), "guard: it waits longer after each restart (5 s, 30 s, 2 min)");
            Check((KeepAlive.Status() ?? "").StartsWith("gave up"), "guard: giving up is reported for the dashboard");

            now = 0; starts = 0; ticks = 0; running = true;
            KeepAlive.Run(delegate { return now; }, delegate (int ms)
            {
                now += ms;
                if (++ticks == 3) running = false;          // it stops…
                if (ticks == 4) KeepAlive.AskToStop("session"); // …because the user chose Exit
                return true;
            }, delegate { return running; }, delegate { starts++; }, delegate { return false; });
            Check(starts == 0 && (KeepAlive.Status() ?? "").StartsWith("stopped"), "guard: after Exit it does not bring DragFlow back");
            KeepAlive.Clear();
            Check(!KeepAlive.StopRequested, "guard: starting DragFlow again lets the guard watch again");

            now = 0; starts = 0; ticks = 0; running = true;
            KeepAlive.Run(delegate { return now; }, delegate (int ms)
            {
                now += ms; ticks++;
                if (ticks == 3) running = false;  // an update: the old copy leaves…
                if (ticks == 4) running = true;   // …the new one is there two seconds later
                if (ticks == 20) KeepAlive.AskToStop("session");
                return true;
            }, delegate { return running; }, delegate { starts++; }, delegate { return false; });
            Check(starts == 0, "guard: a restart for an update is not mistaken for a crash");
            KeepAlive.Clear();

            // told to leave (an update or restart signals it), or the program file changed: it goes, without restarting anything
            now = 0; starts = 0; ticks = 0; running = false;
            KeepAlive.Run(delegate { return now; }, delegate (int ms) { now += ms; return ++ticks < 2; }, delegate { return running; }, delegate { starts++; }, delegate { return false; });
            Check(starts == 0 && (KeepAlive.Status() ?? "").Contains("updated or restarted"), "guard: when told to leave it leaves at once");
            ticks = 0;
            KeepAlive.Run(delegate { return now; }, delegate (int ms) { now += ms; ticks++; return true; }, delegate { return false; }, delegate { starts++; }, delegate { return ticks > 1; });
            Check(starts == 0, "guard: after an update (a different program file) the old guard leaves; the new DragFlow starts its own");
        }

        static void Task()
        {
            string xml = StartupTask.Xml(@"C:\Users\me\AppData\Local\DragFlow\DragFlow.exe");
            Check(xml.Contains("<LogonTrigger>") && xml.Contains("<RunLevel>LeastPrivilege</RunLevel>") && xml.Contains("DragFlow.exe</Command>") && xml.Contains("--startup")
                  && xml.Contains("<RestartOnFailure>") && xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"),
                "sign-in task: starts DragFlow at sign-in, with normal rights, never times out, and retries if it fails");
            Check(StartupTask.Xml("C:\\a&b\\x.exe").Contains("a&amp;b"), "sign-in task: special characters in the path are escaped");
        }

        static void Dashboard(string dir)
        {
            var host = new FakeDashHost { Settings = new Settings() };
            Settings.Current = host.Settings;
            var w = new DashboardWindow(host);
            int failed = 0;
            var names = new List<string>();
            foreach (string key in w.PageKeys.ToList())
            {
                w.ShowPage(key);
                if (w.PageFailed) { failed++; names.Add(key); }
            }
            Check(w.PageKeys.Count() >= 13 && failed == 0, "dashboard: every page builds (" + w.PageKeys.Count() + " pages" + (names.Count > 0 ? "; failed: " + string.Join(", ", names.ToArray()) : "") + ")");
            Check(w.Matches("api key") >= 1 && w.Matches("zzzz") == 0 && w.Matches("dark") >= 1, "dashboard: search finds settings by name");
            w.Matches("");

            host.Settings.GesturesEnabled = true;
            host.Settings.Save();
            int before = host.Applied;
            w.Change("Shapes off", delegate { host.Settings.GesturesEnabled = false; });
            Check(!Settings.Load().GesturesEnabled && host.Applied == before + 1, "dashboard: a change is saved and applied at once");
            w.UndoLast();
            Check(Settings.Load().GesturesEnabled && host.Settings.GesturesEnabled, "dashboard: Undo puts it back");

            RuleStore.Save(new List<Rule> { new Rule { Name = "a" } });
            w.Change("Rule removed", delegate { RuleStore.Save(new List<Rule>()); });
            w.UndoLast();
            Check(RuleStore.Load().Count == 1, "dashboard: Undo also covers rules, recipes and profiles");
            RuleStore.Save(new List<Rule>());
            w.Close();
        }

        static void Usage()
        {
            AiUsage.Clear();
            var when = new DateTime(2026, 10, 27);
            AiUsage.Record("claude-opus-5-5", 1000000, 100000, when);
            AiUsage.Record("claude-haiku-5-5", 1000000, 0, when);
            AiUsage.Record("claude-opus-5-5", 5, 5, when.AddMonths(-1));
            AiUsage.MonthTotal m = AiUsage.For(when);
            Check(m.Requests == 2 && Math.Abs(m.Cost - (4 + 2 + 0.50)) < 0.001, "AI usage: tokens and an estimated cost per month (" + m.Cost.ToString("0.00") + ")");
            string stopped = null;
            try { AiUsage.CheckLimit(5, when); } catch (InvalidOperationException ex) { stopped = ex.Message; }
            Check(stopped != null && stopped.Contains("limit"), "AI usage: AI actions pause once the monthly limit is reached");
            AiUsage.CheckLimit(0, when);
            AiUsage.CheckLimit(50, when);
            Check(true, "AI usage: no limit, or one not reached, lets them run");
            string json = "{\"model\":\"claude-sonnet-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":200,\"output_tokens\":30}}";
            AiUsage.Clear();
            AiClient.ReadAnswer(json);
            Check(AiUsage.For(DateTime.Now).Requests == 1 && AiUsage.For(DateTime.Now).Input == 200, "AI usage: every answer is counted");
            AiUsage.Clear();
        }

        static void Sentences(string dir)
        {
            var r = new Rule { Folder = Make.Dir(dir, "Downloads"), Pattern = "*.pdf", Action = "moveto", Target = Make.Dir(dir, "Archive"), MinAgeDays = 30, Schedule = "weekly sun 18:00" };
            string s = DashboardWindow.Sentence(r);
            Check(s.Contains("*.pdf") && s.Contains("Downloads") && s.Contains("older than 30 days") && s.Contains("18:00") && s.Contains("Archive"),
                "rules page: a rule reads as a sentence (" + s + ")");
            string old = Make.Text(r.Folder, "old.pdf"), fresh = Make.Text(r.Folder, "new.pdf");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-40)); File.SetCreationTime(old, DateTime.Now.AddDays(-40));
            List<string> found = RulePreview.Matches(r, 10);
            Check(found.Count == 1 && found[0] == old && File.Exists(old), "rules page: Try it lists what the rule would take, and touches nothing");
        }

        static void Editor()
        {
            var e = new ScriptEditor();
            string text = "# DragFlow: name=X; types=*\r\n$a = \"hello\" # note\r\nif ($a) { 'yes' }";
            e.Text = text;
            e.Language = ".ps1";
            Check(e.Text == text, "scripts studio: the editor gives back exactly the text it was given, colours or not");
        }

        static void ScriptTest(string dir)
        {
            Directory.CreateDirectory(Scripts.Folder);
            string file = Path.Combine(Scripts.Folder, "echo.ps1");
            File.WriteAllText(file, "# DragFlow: name=Echo; types=*\r\n\"got $($args.Count)\"\r\nWrite-Error 'a warning'\r\n");
            string refused = null;
            try { Scripts.Test(file, new[] { Make.Text(dir, "a.txt") }); } catch (InvalidOperationException ex) { refused = ex.Message; }
            Scripts.Approve(file);
            string output = Scripts.Test(file, new[] { Make.Text(dir, "b.txt") });
            Check(refused != null && output.Contains("got 1") && output.Contains("! ") && output.Contains("warning"), "scripts studio: Test shows everything the script printed, errors marked");
            Check(Scripts.Templates.Length >= 5 && Scripts.Templates.All(t => t[2].Contains("DragFlow: name=")), "scripts studio: templates are ready to use");
            Directory.Delete(Scripts.Folder, true);
        }

        /// <summary>"--dashboard dir": a picture of every page, for checking the look.</summary>
        public static void Pictures(string outDir, bool dark)
        {
            Directory.CreateDirectory(outDir);
            var host = new FakeDashHost { Settings = new Settings { ThemeMode = dark ? "dark" : "light" } };
            Settings.Current = host.Settings;
            ActivityLog.Record("To PDF", "\"invoice.pdf\" created", "", "");
            ActivityLog.Record("Rule: Downloads tidy", "4 items moved to Archive", "x", "");
            var w = new DashboardWindow(host) { Left = -20000, Top = -20000, ShowInTaskbar = false, Height = 1600 };
            w.Show();
            foreach (string key in w.PageKeys.ToList())
            {
                w.ShowPage(key);
                w.UpdateLayout();
                var fe = (FrameworkElement)w.Content;
                var bmp = new RenderTargetBitmap((int)fe.ActualWidth, (int)fe.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(fe);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(outDir, (dark ? "dark-" : "") + key + ".png"))) enc.Save(fs);
            }
            w.Close();
        }
    }
}
