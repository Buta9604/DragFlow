using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow.Tests
{
    /// <summary>Regression checks for the findings of the third code review (the Tier 5 toolbox).</summary>
    static class Tier5Regressions
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            RulesRemember(Make.Dir(root, "remember"));
            RulesAgeAndIgnore(Make.Dir(root, "age"));
            RulesPausedScan(Make.Dir(root, "paused"));
            RecycleWording(Make.Dir(root, "recycle"));
            Everyday(Make.Dir(root, "everyday"));
            TextTools(Make.Dir(root, "text"));
            StartAgain(Make.Dir(root, "restart"));
            Media(Make.Dir(root, "media"));
        }

        /// <summary>Review 3, #1: an hourly scan must not redo files after a restart (duplicates of every conversion or copy).</summary>
        static void RulesRemember(string dir)
        {
            string watch = Make.Dir(dir, "Watched"), target = Make.Dir(dir, "Copies");
            string f = Make.Text(watch, "photo.jpg", "jpeg");
            var rule = new Rule { Name = "copy", Folder = watch, Pattern = "*.jpg", Action = "copyto", Target = target, Scan = true };
            var first = new RuleEngine { TicksOnItsOwn = false };
            var runs = new List<string[]>();
            first.Run = delegate (Rule r, string[] files) { lock (runs) runs.Add(files); };
            first.Apply(new[] { rule }, true);
            first.Process(1000);
            Check(runs.Count == 1 && runs[0][0] == f, "review 3: the first scan handles the file");
            first.Dispose();

            var second = new RuleEngine { TicksOnItsOwn = false }; // DragFlow started again: the memory of handled files is on disk
            second.Run = delegate (Rule r, string[] files) { lock (runs) runs.Add(files); };
            second.Apply(new[] { rule }, true);
            second.Process(1000);
            Check(runs.Count == 1, "review 3: after a restart the scan does not redo it");
            Check(second.RunNow(rule) == 0, "review 3: nor does Run now");
            File.WriteAllText(f, "a new version");
            second.Apply(new[] { rule }, true);
            second.Process(2000);
            Check(runs.Count == 2, "review 3: a changed file counts as new");
            second.Dispose();
        }

        /// <summary>Review 3, #2 and #8 and #3: age uses the newer of created/modified; cloud placeholders and put-aside files are left alone.</summary>
        static void RulesAgeAndIgnore(string dir)
        {
            string copy = Make.Text(dir, "copied-old-photo.jpg", "x");
            File.SetLastWriteTime(copy, DateTime.Now.AddDays(-400)); // a copy keeps the old modified date but was created just now
            var rule = new Rule { Folder = dir, MinAgeDays = 30 };
            Check(!rule.Qualifies(copy), "review 3: a freshly copied file with an old modified date is not \"older than 30 days\"");
            File.SetCreationTime(copy, DateTime.Now.AddDays(-400));
            Check(rule.Qualifies(copy), "review 3: it is once both dates are old");

            string placeholder = Make.Text(dir, "cloud.docx", "x");
            File.SetAttributes(placeholder, File.GetAttributes(placeholder) | FileAttributes.Offline);
            Check(RuleEngine.Ignorable(placeholder), "review 3: a cloud placeholder is never downloaded by a rule");
            File.SetAttributes(placeholder, FileAttributes.Normal);
            string aside = Make.Text(Make.Dir(dir, ShellOps.AsideFolderName), "removed.pdf", "x");
            Check(RuleEngine.Ignorable(aside), "review 3: files DragFlow put aside are not fed back into rules");
        }

        /// <summary>Review 3, #7: a paused rule stays paused through the hourly scan.</summary>
        static void RulesPausedScan(string dir)
        {
            string watch = Make.Dir(dir, "Inbox"), target = Make.Dir(dir, "Out");
            var rule = new Rule { Name = "flood", Folder = watch, Pattern = "*.pdf", Action = "moveto", Target = target, Scan = true };
            var engine = new RuleEngine { ScanEveryMs = 0, TicksOnItsOwn = false }; // a scan on every tick, so the flood below arrives through scans
            int runs = 0; string paused = null;
            engine.Run = delegate { runs++; };
            engine.Paused = delegate (Rule r, string why) { paused = why; };
            engine.Apply(new[] { rule }, true);
            for (int i = 0; i < 35; i++)
            {
                Make.Text(watch, "flood" + i + ".pdf", "x" + i);
                engine.Process(20000 + i * 10);
            }
            Check(paused != null && runs >= 30, "review 3: a flood found by the scan pauses the rule");
            runs = 0;
            Make.Text(watch, "later.pdf", "z");
            engine.Process(50000); engine.Process(50001);
            Check(runs == 0, "review 3: the hourly scan skips a paused rule");
            engine.Dispose();
            var fresh = new RuleEngine { ScanEveryMs = 0, TicksOnItsOwn = false };
            fresh.Run = delegate { runs++; };
            fresh.Apply(new[] { rule }, true);
            fresh.Process(60000);
            Check(runs == 1, "review 3: (a rule that is not paused does pick the new file up)");
            fresh.Dispose();

            // Review 3 follow-up: turning rules off (or changing them) while files are landing used to stop DragFlow for good
            // (the watcher's event waited for the engine's lock, the engine waited for the watcher to finish).
            var busy = new RuleEngine { TicksOnItsOwn = false };
            string busyDir = Make.Dir(dir, "Busy");
            busy.Apply(new[] { new Rule { Name = "busy", Folder = busyDir, Pattern = "*", Action = "moveto", Target = target } }, true);
            var writer = new System.Threading.Thread(delegate () { for (int i = 0; i < 300; i++) try { File.WriteAllText(Path.Combine(busyDir, "f" + i + ".txt"), "x"); } catch (IOException) { } });
            writer.Start();
            System.Threading.Thread.Sleep(30);
            var done = new System.Threading.ManualResetEvent(false);
            var disposer = new System.Threading.Thread(delegate () { busy.Apply(new Rule[0], false); busy.Dispose(); done.Set(); });
            disposer.Start();
            Check(done.WaitOne(15000), "review 3: changing or stopping rules while files are landing never hangs");
            writer.Join();
        }

        /// <summary>Review 3, #3: when there is no Recycle Bin (USB stick, share) the notification must not claim there was.</summary>
        static void RecycleWording(string dir)
        {
            string a = Make.Text(dir, "a.txt", "1"), b = Make.Text(dir, "b.txt", "2");
            Func<string, bool> real = UndoLog.Discard;
            try
            {
                UndoLog.Discard = delegate (string p) { real(p); return false; }; // "put aside" happened
                string msg = FileOps.RecycleAll(new[] { a });
                Check(!msg.Contains("Recycle Bin (restore") && msg.Contains(ShellOps.AsideFolderName), "review 3: put-aside items are reported as such, not as recycled");
                UndoLog.Discard = real;
                Check(FileOps.RecycleAll(new[] { b }).Contains("Recycle Bin"), "review 3: recycled items still are");
            }
            finally { UndoLog.Discard = real; }
        }

        /// <summary>Review 3, #12: compare across buffer boundaries; the contents list of a whole drive has a real home.</summary>
        static void Everyday(string dir)
        {
            var big = new byte[200000];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)(i % 251);
            string a = Path.Combine(dir, "a.bin"), b = Path.Combine(dir, "b.bin");
            File.WriteAllBytes(a, big);
            big[150000] ^= 1;
            File.WriteAllBytes(b, big);
            Check(FileOps.Compare(a, b).Contains("150,000"), "review 3: the first difference is found past the first buffer");
            string list = FileOps.ListContentsPath("C:\\");
            Check(Path.IsPathRooted(list) && list.EndsWith("Drive C contents.csv") && !list.StartsWith("C:\\Drive"), "review 3: a drive's contents list goes to Documents, never to a relative path");
        }

        /// <summary>Review 3, #9 and #10: absurdly nested JSON is refused, not a crash; code spans are left exactly as written.</summary>
        static void TextTools(string dir)
        {
            string deep = Path.Combine(dir, "deep.json");
            File.WriteAllText(deep, new string('[', 5000) + new string(']', 5000));
            string refused = null;
            try { FileOps.TidyJson(new[] { deep }); } catch (InvalidOperationException ex) { refused = ex.Message; }
            Check(refused != null && refused.Contains("too deeply nested") && File.ReadAllText(deep).Length == 10000, "review 3: 5000 levels of JSON are refused, the file untouched");
            string html = Markdown.Inline("use `a*b*c` or `[x](y)` or `<b>` but *not* this");
            Check(html.Contains("<code>a*b*c</code>") && html.Contains("<code>[x](y)</code>") && html.Contains("<code>&lt;b&gt;</code>") && html.Contains("<em>not</em>"),
                "review 3: stars and brackets inside code spans stay as they are");
        }

        /// <summary>Review 3, #11 and #6: restarting keeps the profile and skips the "updated" toast; a missing settings folder is noticed.</summary>
        static void StartAgain(string dir)
        {
            string args = Program.RestartArgs(new[] { "--profile", "C:\\p\\", "--updated", "--dial", "C:\\f.txt", "--no-context-menu" }, "--restart");
            Check(args == "--restart --profile \"C:\\p\" --no-context-menu", "review 3: a restart keeps the profile and the lasting options only (" + args + ")");
            string pointer = Path.Combine(dir, "location.txt");
            string there = Make.Dir(dir, "there");
            File.WriteAllText(pointer, there);
            Check(AppPaths.ResolvePointer(pointer, 300) == there && AppPaths.MissingSettingsFolder == null, "review 3: the settings pointer resolves to an existing folder");
            File.WriteAllText(pointer, Path.Combine(dir, "unplugged"));
            long t = Clock.Now;
            Check(AppPaths.ResolvePointer(pointer, 300) == null && AppPaths.MissingSettingsFolder == Path.Combine(dir, "unplugged") && Clock.Now - t >= 250,
                "review 3: a missing folder is waited for a little, then reported");
            AppPaths.MissingSettingsFolder = null;
        }

        /// <summary>Review 3, #12: silent videos and MP4-unfriendly sound tracks.</summary>
        static void Media(string dir)
        {
            if (FileOps.FindFfmpeg() == null) return;
            string silent = Path.Combine(dir, "silent.mp4");
            var psi = new System.Diagnostics.ProcessStartInfo(FileOps.FindFfmpeg(), "-y -loglevel error -f lavfi -i testsrc=duration=2:size=160x120:rate=10 -pix_fmt yuv420p \"" + silent + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit();
            Check(FileOps.AudioCodec(silent) == null, "review 3: a silent video is recognised as such");
            Check(File.Exists(FileOps.Speed(silent, 2)), "review 3: a silent video can change speed");
            Check(File.Exists(FileOps.Merge(new[] { silent, silent })), "review 3: silent videos can be merged");
            Check(File.Exists(FileOps.RotateVideo(silent, "left")), "review 3: and rotated");
            string ogg = Path.Combine(dir, "vorbis.mkv");
            psi = new System.Diagnostics.ProcessStartInfo(FileOps.FindFfmpeg(), "-y -loglevel error -f lavfi -i testsrc=duration=2:size=160x120:rate=10 -f lavfi -i sine=frequency=440:duration=2 -pix_fmt yuv420p -c:a libvorbis -shortest \"" + ogg + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit();
            if (File.Exists(ogg))
            {
                Check(FileOps.AudioCodec(ogg) == "vorbis", "review 3: the sound codec is read");
                Check(File.Exists(FileOps.RotateVideo(ogg, "right")), "review 3: a Vorbis sound track is re-encoded for MP4 instead of failing");
            }
            bool likely = FileOps.FilterLikely("subtitles");
            Check(likely || !FileOps.HasFilter("subtitles"), "review 3: the quick filter check never says no when the slow one says yes");
        }
    }
}
