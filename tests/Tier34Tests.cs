using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>Polish (tier 3) and power features (tier 4).</summary>
    static class Tier34Tests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Translation();
            LookAndAccess();
            DialWithoutDragging();
            Rules(Make.Dir(root, "rules"));
            Recipes(Make.Dir(root, "recipes"));
            ActivityAndUpdates(Make.Dir(root, "updates"));
            Speed();
            ReviewTwo(Make.Dir(root, "review2"));
        }

        /// <summary>Problems found in the second code review, kept fixed.</summary>
        static void ReviewTwo(string dir)
        {
            // A rule never reacts to a file DragFlow is writing (or has just written) in its own folder.
            string watch = Make.Dir(dir, "watched");
            var rule = new Rule { Name = "pics", Folder = watch, Pattern = "*", Action = "shrink" };
            var engine = new RuleEngine();
            int runs = 0;
            engine.Run = delegate { runs++; };
            string photo = Make.Png(watch, "photo.png");
            string reserved = FileOps.UniquePath(watch, "photo_small", ".jpg"); // DragFlow picks the name first...
            engine.Notice(rule, reserved, 0);                                      // ...the folder watch sees it appear...
            File.WriteAllBytes(reserved, new byte[] { 1, 2, 3 });                  // ...while it is still being written
            engine.Process(1000); engine.Process(2000); engine.Process(3000);
            Check(runs == 0, "review 2: a rule never re-processes what DragFlow is writing (no endless loops)");
            engine.Dispose();

            // A recipe never tidies away files it only moved.
            string dest = Make.Dir(dir, "moved here");
            RecipeStore.Save(new List<Recipe> { new Recipe { Name = "Move and zip", Types = "*", Steps = new List<string> { "moveto:" + dest, "zip" } } });
            string note = Make.Text(dir, "keep me.txt", "precious");
            ActionResult r = new ActionRunner(new Control(), new FakeNotifier()).Execute("recipe:Move and zip", new[] { note }, new DropInfo(), new Job("Move and zip"));
            Check(File.Exists(Path.Combine(dest, "keep me.txt")) && File.Exists(r.Output), "review 2: a recipe never puts your own moved files in the Recycle Bin");

            // Update information written with a byte-order mark still reads; plain http is refused.
            UpdateInfo bom = Updater.Parse("\uFEFFversion=2.0.0\r\nfile=a.exe\r\nsha256=" + new string('b', 64));
            Check(bom != null && bom.Version == new Version(2, 0, 0), "review 2: latest.ini with a byte-order mark is read");
            bool refused = false;
            try { Updater.Check("http://example.com/dragflow"); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "review 2: update sources over plain http are refused");
            string edge = WebCapture.FindEdge();
            if (edge != null) Check((Signature.Signer(edge) ?? "").Contains("Microsoft"), "review 2: a validly signed program's publisher is read (Edge: Microsoft)");
            Check(Signature.Signer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DragFlow.Tests.exe")) == null, "review 2: an unsigned program has no signer");

            // A save folder with ";" in its name survives.
            ConvertOptions o = ConvertOptions.Parse(new ConvertOptions { Format = "jpg", Output = @"C:\a;b\c", MaxSide = 800 }.ToString());
            Check(o.Output == @"C:\a;b\c" && o.MaxSide == 800, "review 2: a save folder with ; in its name is kept whole");

            // Video to GIF from the Convert window goes to the video converter.
            string ff = FileOps.FindFfmpeg();
            if (ff != null)
            {
                string clip = Path.Combine(dir, "clip.mp4");
                var psi = new ProcessStartInfo(ff, "-y -loglevel error -f lavfi -i testsrc=duration=1:size=160x120:rate=10 -pix_fmt yuv420p \"" + clip + "\"") { UseShellExecute = false, CreateNoWindow = true };
                using (Process p = Process.Start(psi)) p.WaitForExit();
                ActionResult g = new ActionRunner(new Control(), new FakeNotifier()).Execute("convertx:gif;h=120", new[] { clip }, new DropInfo(), new Job("Convert"));
                Check(g.Failures.Count == 0 && g.Output != null && g.Output.EndsWith(".gif"), "review 2: a video becomes a GIF from the Convert window too");
            }
        }

        static void Translation()
        {
            L.UseTable(L.Parse(new[] { "en: Move to", "tr: Verschieben nach", "en: Copy to", "tr: Kopieren nach", "en: {0} items", "tr: {0} Elemente" }), "de");
            try
            {
                var item = new ActionItem("", "Move to", "", Color.Blue);
                Check(item.Label == "Verschieben nach" && item.Key == "Move to", "translation: labels are translated, the English name stays for settings and code");
                Check(item.Matches("Move to") && item.Matches("Verschieben nach"), "translation: settings can use either name");
                Check(RadialRenderer.LabelFor(item, true) == "Kopieren nach", "translation: Ctrl's \"Copy to\" is translated too");
                Check(L.F("{0} items", 3) == "3 Elemente" && L.T("Not in the table") == "Not in the table", "translation: missing texts stay English; blanks are filled in");
                List<ActionItem> menu = ActionCatalog.For(new[] { Path.Combine(Path.GetTempPath(), "x.zip") });
                Check(menu.Last().Key == "More" && menu.Any(i => i.Label == "Verschieben nach"), "translation: the dial still knows which slice is More");
            }
            finally { L.UseTable(null, "en"); }
            Check(L.Use("en") && L.Language == "en", "translation: English needs no file");
        }

        static void LookAndAccess()
        {
            Theme hc = Theme.HighContrastTheme();
            Check(hc.HighContrast && hc.Surface.A == 255 && hc.BorderWidth >= 2 && hc.AccentFor(Color.Red) == SystemColors.Highlight,
                "look: high contrast uses solid system colours and thicker edges");
            Check(Theme.ReduceMotion(new Settings { Motion = "off" }) && !Theme.ReduceMotion(new Settings { Motion = "on" }), "look: animations can be switched off");
            Check(Theme.For(new Settings { ThemeMode = "light" }) == Theme.LightTheme || SystemInformation.HighContrast, "look: light or dark can be chosen");

            var s = new Settings { DialSize = 1.5, ThemeMode = "dark", Motion = "off", HiddenActions = new List<string> { "Zip" }, Hotkey = "Ctrl+Shift+Q",
                                   ContextMenu = false, UpdateFeed = @"C:\feed", Language = "de", RulesEnabled = false, ConvertImage = "webp;q=70" };
            s.Save();
            Settings back = Settings.Load();
            Check(back.DialSize == 1.5 && back.ThemeMode == "dark" && back.Motion == "off" && back.HiddenActions[0] == "Zip" && back.Hotkey == "Ctrl+Shift+Q"
                  && !back.ContextMenu && back.UpdateFeed == @"C:\feed" && back.Language == "de" && !back.RulesEnabled && back.ConvertImage == "webp;q=70",
                "settings: the new options survive a restart");

            Settings.Current = new Settings { HiddenActions = new List<string> { "Zip", "Copy path" } };
            List<ActionItem> txt = ActionCatalog.For(new[] { Path.Combine(Path.GetTempPath(), "a.txt") });
            Check(!txt.Any(i => i.Key == "Zip") && !txt.Last().Children.Any(c => c.Key == "Copy path"), "actions: hidden actions are left off the dial");
            Settings.Current = new Settings();

            // dial size, first-use tips, number keys
            var ov = new FakeOverlay();
            var desk = new FakeDesktop { X = 500, Y = 500 };
            var settings = new Settings { DialSize = 1.25 };
            var ctl = new DragController(ov, desk) { GetSettings = delegate { return settings; }, GetLearning = delegate { return new Learning(); } };
            var session = new DragSession { OriginX = 500, OriginY = 500, FileState = 1, Menu = ActionCatalog.For((string[])null) };
            ctl.OnStarted(session, 0);
            long t = 0;
            for (int i = 0; i < 20; i++) { t += 15; desk.X++; ctl.Tick(t); }
            Check(ov.Visible && Math.Abs(ov.Scale - 1.25) < 0.001, "access: the larger dial size is used");
            Check(ov.Tip != null && settings.TipsShown == 1, "help: the first few dials show how to use them");
            desk.Keys.Add(0x32); // the "2" key
            t += 15; ctl.Tick(t);
            double x, y;
            new RadialGeometry(ov.Items.Count, 1.25).IconCenter(1, out x, out y);
            Check(desk.Moves == 1 && Math.Abs(desk.X - (ov.AX + x)) < 1.5 && Math.Abs(desk.Y - (ov.AY + y)) < 1.5, "access: pressing 2 puts the pointer on the second action");
            settings.TipsShown = DragController.TipTimes;
            desk.Keys.Clear();
            ctl.OnStarted(new DragSession { OriginX = 900, OriginY = 500, FileState = 1, Menu = ActionCatalog.For((string[])null) }, t);
            desk.X = 900;
            for (int i = 0; i < 20; i++) { t += 15; desk.X++; ctl.Tick(t); }
            Check(ov.Tip == null, "help: after that the tip stays away");
        }

        static void DialWithoutDragging()
        {
            uint mods; Keys key;
            Check(Hotkey.Parse("Ctrl+Alt+D", out mods, out key) && mods == 3 && key == Keys.D, "shortcut: Ctrl+Alt+D is understood");
            Check(Hotkey.Parse("ctrl+shift+5", out mods, out key) && key == Keys.D5 && mods == 6, "shortcut: numbers and any order of words work");
            Check(!Hotkey.Parse("D", out mods, out key) && !Hotkey.Parse("Ctrl+Banana", out mods, out key), "shortcut: a key without Ctrl/Alt/Shift, or nonsense, is refused");

            var menu = new List<ActionItem>
            {
                new ActionItem("zip", "Zip", "\uE7B8", Color.Purple),
                new ActionItem("", "Convert", "\uE895", Color.Blue).With(new ActionItem("convert:png", "PNG", "", Color.Blue), new ActionItem("convert:jpg", "JPG", "", Color.Blue)),
                new ActionItem("rename", "Rename", "\uE8AC", Color.Orange)
            };
            var ring = new RingOverlay();
            ActionItem picked = null;
            int dismissed = 0;
            ring.Picked += delegate (ActionItem a) { picked = a; };
            ring.Dismissed += delegate { dismissed++; };
            try
            {
                ring.ShowForClick(-3000, -3000, menu, 1.0, Clock.Now, "3 items");
                ring.SimulateKey(Keys.Right);
                ring.SimulateKey(Keys.Enter);
                Check(picked != null && picked.Id == "zip", "keyboard dial: arrow then Enter picks an action");
                ring.Close();
                picked = null;
                ring.ShowForClick(-3000, -3000, menu, 1.0, Clock.Now, null);
                ring.SimulateKey(Keys.D2);
                Check(ring.OpenOptions == 1 && picked == null, "keyboard dial: a number opens that slice's options");
                ring.SimulateKey(Keys.Down);
                ring.SimulateKey(Keys.Enter);
                Check(picked != null && picked.Id == "convert:jpg", "keyboard dial: arrows move through the options, Enter picks one");
                ring.Close();
                ring.ShowForClick(-3000, -3000, menu, 1.0, Clock.Now, null);
                dismissed = 0;
                ring.SimulateKey(Keys.Escape);
                Check(dismissed >= 1 && ring.Closing, "keyboard dial: Esc closes it");
                ring.Close();
                picked = null;
                ring.ShowForClick(-3000, -3000, menu, 1.0, Clock.Now, null);
                double ix, iy;
                new RadialGeometry(3, 1.0).IconCenter(2, out ix, out iy);
                ring.SimulateClick((int)(-3000 + ix), (int)(-3000 + iy));
                Check(picked != null && picked.Id == "rename", "click dial: clicking a slice picks it");
            }
            finally { ring.Close(); }
        }

        static void Rules(string dir)
        {
            string watch = Make.Dir(dir, "Downloads"), target = Make.Dir(dir, "PDFs");
            var rule = new Rule { Name = "PDFs away", Folder = watch, Pattern = "*.pdf;*.docx", Action = "moveto", Target = target };
            Check(rule.MatchesName("Report.PDF") && rule.MatchesName("a.docx") && !rule.MatchesName("a.png"), "rules: file patterns match names (any capitals)");
            Check(rule.ActionId == "moveto:" + target && rule.Problem() == null, "rules: a complete rule has nothing wrong with it");
            Check(new Rule { Folder = watch, Action = "moveto", Target = watch }.Problem() != null && new Rule { Folder = dir + "\\nope", Action = "shrink" }.Problem() != null,
                "rules: rules that cannot work are explained");
            RuleStore.Save(new List<Rule> { rule, new Rule { Enabled = false, Name = "Shrink", Folder = watch, Pattern = "*.jpg", Action = "shrink", Subfolders = true } });
            List<Rule> back = RuleStore.Load();
            Check(back.Count == 2 && back[0].Target == target && !back[1].Enabled && back[1].Subfolders, "rules: saved and read back");

            var engine = new RuleEngine();
            var runs = new List<string[]>();
            engine.Run = delegate (Rule r, string[] files) { lock (runs) runs.Add(files); };
            string pdf = Make.Text(watch, "invoice.pdf", "pdf");
            string partial = Make.Text(watch, "big.pdf.crdownload", "half");
            engine.Notice(rule, pdf, 0);
            engine.Notice(rule, partial, 0);
            engine.Process(1000);
            Check(runs.Count == 0, "rules: a new file is not touched until it has stopped changing");
            engine.Process(2000);
            Check(runs.Count == 1 && runs[0].Length == 1 && runs[0][0] == pdf, "rules: once settled it is handled; half-downloads are ignored");
            engine.Notice(rule, pdf, 3000); engine.Process(4000); engine.Process(5000);
            Check(runs.Count == 1, "rules: the same file is never handled twice");
            string made = Make.Text(watch, "made-by-dragflow.pdf");
            using (UndoLog.Begin("test")) UndoLog.Created(made);
            engine.Notice(rule, made, 6000); engine.Process(7000); engine.Process(8000);
            Check(runs.Count == 1, "rules: files DragFlow itself just made never trigger a rule (no loops)");
            string locked = Make.Text(watch, "open.pdf");
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                engine.Notice(rule, locked, 9000); engine.Process(10000); engine.Process(11000);
                Check(runs.Count == 1, "rules: a file still open in another program is left alone");
            }
            engine.Process(12000);
            Check(runs.Count == 2, "rules: and handled once it is closed");
            string paused = null;
            engine.Paused = delegate (Rule r, string why) { paused = why; };
            for (int i = 0; i < 35; i++)
            {
                string f = Make.Text(watch, "flood" + i + ".pdf", "x" + i);
                engine.Notice(rule, f, 20000 + i * 10);
                engine.Process(20001 + i * 10); engine.Process(20002 + i * 10);
            }
            Check(paused != null, "rules: a rule that fires too often is paused");

            // a real folder watch, end to end
            var live = new RuleEngine();
            var got = new ManualResetEvent(false);
            live.Run = delegate (Rule r, string[] files) { if (files.Any(f => f.EndsWith("live.pdf"))) got.Set(); };
            string liveDir = Make.Dir(dir, "Live");
            live.Apply(new[] { new Rule { Name = "live", Folder = liveDir, Pattern = "*.pdf", Action = "moveto", Target = target } }, true);
            Check(live.Watching == 1, "rules: enabled rules watch their folders");
            File.WriteAllText(Path.Combine(liveDir, "live.pdf"), "x");
            Check(got.WaitOne(8000), "rules: a file saved into a watched folder is picked up within seconds");
            live.Apply(new Rule[0], false);
            Check(live.Watching == 0, "rules: turning rules off stops watching");
            live.Dispose();
            engine.Dispose();
        }

        static void Recipes(string dir)
        {
            RecipeStore.Save(new List<Recipe> { new Recipe { Name = "Web ready", Types = "image", Steps = new List<string> { "resize:50", "strip", "zip" } } });
            Recipe r = RecipeStore.Find("Web ready");
            Check(r != null && r.Steps.Count == 3 && r.Matches(new[] { "a.png" }) && !r.Matches(new[] { "a.txt" }), "recipes: saved, read back, and matched to pictures");
            Check(RecipeSteps.Label("moveto:C:\\Photos") == "Move to a folder Photos", "recipes: steps have readable names");
            string img = Make.Png(dir, "holiday.png", 200, 100);
            List<ActionItem> menu = ActionCatalog.For(new[] { img });
            Check(menu.Any(i => i.Children.Any(c => c.Id == "recipe:Web ready")), "recipes: they appear on the dial");

            UndoLog.Clear();
            var runner = new ActionRunner(new Control(), new FakeNotifier());
            ActionResult res = runner.Execute("recipe:Web ready", new[] { img }, new DropInfo(), new Job("Web ready"));
            Check(res.Output != null && res.Output.EndsWith(".zip") && File.Exists(res.Output) && File.Exists(img), "recipes: resize, clean and zip in one go; the original stays");
            Check(!File.Exists(Path.Combine(dir, "holiday_50pct.png")) && !Directory.GetFiles(dir, "*_clean*.png").Any(), "recipes: in-between files are tidied away");
            using (var za = System.IO.Compression.ZipFile.OpenRead(res.Output))
                Check(za.Entries.Count == 1 && za.Entries[0].Name.Contains("_clean"), "recipes: the zip holds the finished picture");
            Check(res.UndoId != null && UndoLog.Undo(res.UndoId).Problems.Count == 0 && !File.Exists(res.Output), "recipes: one Undo reverses the whole recipe");

            List<string> moved;
            string a = Make.Text(dir, "m1.txt"), dest = Make.Dir(dir, "dest");
            FileOps.MoveTo(new[] { a }, dest, out moved);
            Check(moved.Count == 1 && moved[0] == Path.Combine(dest, "m1.txt"), "recipes: a Move step knows where things went");
        }

        static void ActivityAndUpdates(string dir)
        {
            ActivityLog.Clear();
            ActivityLog.Record("Zip", "\"a.zip\" created", "123-abc", @"C:\a.zip");
            ActivityLog.Record("Convert to JPG", "2 images converted", "", "");
            List<ActivityEntry> act = ActivityLog.Load();
            Check(act.Count == 2 && act[0].Title == "Zip" && act[0].UndoId == "123-abc" && act[1].Result == "2 images converted", "activity: what DragFlow did is listed");

            Check(Updater.Parse("version=9.0.0\nfile=..\\..\\evil.exe\nsha256=" + new string('a', 64)).File == "evil.exe", "updates: the file name can never point outside the update folder");
            Check(Updater.Parse("version=9.0.0\nfile=x.exe\nsha256=short") == null, "updates: incomplete update information is refused");

            string feed = Make.Dir(dir, "feed");
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DragFlow.exe");
            if (!File.Exists(exe)) { Console.WriteLine("  SKIP  updates (DragFlow.exe not next to the tests)"); return; }
            byte[] real = File.ReadAllBytes(exe);
            Version v = AssemblyName.GetAssemblyName(exe).Version;
            File.WriteAllBytes(Path.Combine(feed, "DragFlow-new.exe"), real);
            File.WriteAllText(Path.Combine(feed, "latest.ini"), "version=" + v + "\nfile=DragFlow-new.exe\nsha256=" + Updater.Sha256Of(real) + "\nnotes=Faster\n");
            Check(Updater.Check(feed) == null || v > Updater.Current, "updates: the same version is not offered again");
            var info = new UpdateInfo { Version = v, File = "DragFlow-new.exe", Sha256 = Updater.Sha256Of(real) };
            string staged = Updater.Download(feed, info);
            Check(File.Exists(staged), "updates: a matching download is accepted");
            bool refused = false;
            try { Updater.Download(feed, new UpdateInfo { Version = v, File = "DragFlow-new.exe", Sha256 = new string('0', 64) }); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "updates: a download that does not match its checksum is thrown away");
            refused = false;
            try { Updater.Download(feed, new UpdateInfo { Version = new Version(v.Major + 1, 0, 0), File = "DragFlow-new.exe", Sha256 = Updater.Sha256Of(real) }); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "updates: a file that is not the promised version is thrown away");

            string app = Make.Dir(dir, "app");
            string installed = Path.Combine(app, "DragFlow.exe");
            File.WriteAllText(installed, "old version");
            Updater.Install(staged, installed);
            Check(File.ReadAllBytes(installed).Length == real.Length && File.Exists(Path.Combine(app, "DragFlow.old.exe")), "updates: the new version takes the old one's place, which is kept until the next start");

            string setup = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DragFlow-Setup.exe");
            if (File.Exists(setup))
            {
                Assembly a = Assembly.Load(File.ReadAllBytes(setup));
                Check(a.GetManifestResourceNames().Contains("DragFlow.exe") && a.GetName().Version == v, "installer: DragFlow-Setup.exe carries this DragFlow inside it");
            }
        }

        static void Speed()
        {
            List<ActionItem> items = ActionCatalog.For(new[] { Path.Combine(Path.GetTempPath(), "speed.png") });
            var geo = new RadialGeometry(items.Count, 1.5);
            var v = new RadialView
            {
                Geo = geo, Items = items, Theme = Theme.DarkTheme, Open = 1, Hover = new float[items.Count], Heading = new float[items.Count],
                LabelWidths = RadialRenderer.MeasurePills(items, 1.5), LabelIndex = 2, Summary = "3 items\n2 MB"
            };
            int kids = items.FindIndex(i => i.IsParent);
            v.KidsParent = kids; v.KidsOpen = 1;
            v.Chips = geo.LayoutPills(kids, RadialRenderer.MeasurePills(items[kids].Children, 1.5));
            v.ChipHover = new float[v.Chips.Length];
            using (var frame = new Bitmap(geo.HalfSize * 2, geo.HalfSize * 2))
            {
                RadialRenderer.RenderInto(frame, v);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 30; i++) { v.Hover[i % items.Count] = (i % 3) / 2f; RadialRenderer.RenderInto(frame, v); }
                double ms = sw.Elapsed.TotalMilliseconds / 30;
                Console.WriteLine("  info  one dial frame at 150% takes " + ms.ToString("0.0") + " ms");
                Check(ms < 25, "speed: a dial frame draws well within a screen refresh");
            }
        }
    }
}
