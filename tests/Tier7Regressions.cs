using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow.Tests
{
    /// <summary>Regression checks for the review of Tier 7.</summary>
    static class Tier7Regressions
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Approval(Make.Dir(root, "approval"));
            Encodings(Make.Dir(root, "encodings"));
            Costs();
            TaskId();
            TryIt(Make.Dir(root, "tryit"));
            Undo();
        }

        /// <summary>Review 7, #4: only what was shown gets approved; planted modules and programs do not run.</summary>
        static void Approval(string dir)
        {
            string file = Path.Combine(dir, "s.ps1");
            File.WriteAllText(file, "# DragFlow: name=S\r\n'one'\r\n");
            byte[] shown = File.ReadAllBytes(file);
            File.WriteAllText(file, "# DragFlow: name=S\r\n'swapped while the question was open'\r\n");
            Scripts.ApproveHash(Scripts.FingerprintOf(shown));
            Check(!Scripts.Approved(file), "review 7: approving what was shown does not approve a file swapped meanwhile");
            ScriptAction a = Scripts.Read(file);
            var psi = Scripts.Command(a, new[] { file });
            Check(psi.EnvironmentVariables["PYTHONSAFEPATH"] == "1" && psi.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"] == "1",
                "review 7: scripts cannot pull in a planted Python module or a same-named program from the files' folder");
        }

        /// <summary>Review 7, #6: saving keeps a script's encoding; batch files never get a byte order mark.</summary>
        static void Encodings(string dir)
        {
            byte[] ansi = Encoding.Default.GetBytes("echo café\r\n");
            Encoding enc;
            string text = Scripts.Decode(ansi, out enc);
            Check(text.Contains("café") && Scripts.Encode(text, enc, "x.ps1").SequenceEqual(ansi), "review 7: an older (ANSI) script keeps its characters and encoding when saved");
            byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("@echo off\r\n")).ToArray();
            string t2 = Scripts.Decode(bom, out enc);
            byte[] saved = Scripts.Encode(t2, enc, "x.cmd");
            Check(saved[0] == (byte)'@' && Scripts.Encode(t2, enc, "x.ps1")[0] == 0xEF, "review 7: batch files are saved without a byte order mark; others keep theirs");
        }

        /// <summary>Review 7, #8: every attempt of a fallback counts, at its own model's price; long Haiku prompts cost more.</summary>
        static void Costs()
        {
            AiUsage.Clear();
            string json = "{\"model\":\"claude-opus-4-8\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":1000,\"output_tokens\":10," +
                          "\"iterations\":[{\"model\":\"claude-opus-5-5\",\"input_tokens\":1000000,\"output_tokens\":0},{\"model\":\"claude-opus-4-8\",\"input_tokens\":1000,\"output_tokens\":10}]}}";
            AiClient.ReadAnswer(json);
            AiUsage.MonthTotal m = AiUsage.For(DateTime.Now);
            Check(m.Requests == 2 && m.Cost > 4, "review 7: a declined first attempt is counted too (" + m.Cost.ToString("0.00") + ")");
            AiUsage.Clear();
            Check(Math.Abs(AiUsage.Cost("claude-haiku-5-5", 200000, 0) - 0.10) < 0.0001 && Math.Abs(AiUsage.Cost("claude-haiku-5-5", 50000, 0) - 0.005) < 0.0001,
                "review 7: Haiku's higher price for long prompts is used");
        }

        /// <summary>Review 7, #18: the task is matched to this DragFlow by an id that does not depend on the code page.</summary>
        static void TaskId()
        {
            string a = StartupTask.IdOf(@"C:\Users\José\AppData\Local\DragFlow\DragFlow.exe"), b = StartupTask.IdOf(@"c:\users\josé\appdata\local\dragflow\dragflow.exe");
            Check(a == b && a.Length == 19 && a.All(c => c < 128) && StartupTask.Xml(@"C:\Users\José\x.exe").Contains(StartupTask.IdOf(@"C:\Users\José\x.exe")),
                "review 7: the sign-in task carries a plain id of the program path");
            Check(StartupTask.Xml("C:\\x.exe").Contains("<Priority>5</Priority>"), "review 7: the task starts DragFlow at normal priority");
        }

        /// <summary>Review 7, #16: Try it leaves out files the rule already handled.</summary>
        static void TryIt(string dir)
        {
            string f = Make.Text(dir, "a.pdf"), g = Make.Text(dir, "b.pdf");
            File.WriteAllLines(Path.Combine(AppPaths.Local, "rules-done.txt"), new[] { f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks });
            List<string> found = RulePreview.Matches(new Rule { Folder = dir, Pattern = "*.pdf" }, 10);
            Check(found.Count == 1 && found[0] == g, "review 7: Try it does not list files the rule already handled");
            File.Delete(Path.Combine(AppPaths.Local, "rules-done.txt"));
        }

        /// <summary>Review 7, #11: changes Undo cannot reverse do not offer Undo.</summary>
        static void Undo()
        {
            var host = new FakeDashHost { Settings = new Settings() };
            Settings.Current = host.Settings;
            var w = new DashboardWindow(host);
            w.Change("x", delegate { host.Settings.Preview = false; });
            w.Saved("API key removed");
            w.UndoLast();
            Check(!host.Settings.Preview, "review 7: after a change Undo cannot reverse, Undo does not roll back an older change instead");
            w.Close();
        }
    }
}
