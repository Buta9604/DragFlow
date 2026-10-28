using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>The toolbox: picture, video, audio and text tools, Office to PDF, everyday extras, smarter rules, the dial from anywhere, settings anywhere.</summary>
    static class Tier5Tests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Pictures(Make.Dir(root, "pictures"));
            Media(Make.Dir(root, "media"));
            Text(Make.Dir(root, "text"));
            Office(Make.Dir(root, "office"));
            Everyday(Make.Dir(root, "everyday"));
            Rules(Make.Dir(root, "rules"));
            Anywhere(Make.Dir(root, "anywhere"));
        }

        static Size SizeOf(string path) { using (Image i = Image.FromFile(path)) return i.Size; }

        static void Pictures(string dir)
        {
            string wide = Make.Png(dir, "wide.png", 400, 200), tall = Make.Png(dir, "tall.png", 200, 400);
            Check(SizeOf(FileOps.Crop(new[] { wide }, "1:1")[0]) == new Size(200, 200) && SizeOf(FileOps.Crop(new[] { tall }, "16:9")[0]) == new Size(200, 113),
                "pictures: crop to square and 16:9 from the middle");
            string marked = FileOps.Watermark(new[] { wide }, "© Sam")[0];
            Check(marked.EndsWith("wide_marked.png") && SizeOf(marked) == new Size(400, 200), "pictures: watermark keeps the size and format");
            string gray = FileOps.Grayscale(new[] { wide })[0];
            using (var b = new Bitmap(gray)) { Color c = b.GetPixel(10, 10); Check(c.R == c.G && c.G == c.B, "pictures: grayscale really is grey"); }
            Check(SizeOf(FileOps.Border(new[] { wide })[0]) == new Size(412, 212), "pictures: a border 3 % of the shorter side");
            string collage = FileOps.Collage(new[] { wide, tall, marked, gray });
            Check(File.Exists(collage) && SizeOf(collage).Width == 2 * 400 + 3 * 8, "pictures: a collage lays pictures out in a grid");
            if (FileOps.FindFfmpeg() != null)
            {
                string gif = FileOps.AnimatedGif(new[] { wide, tall });
                Check(File.Exists(gif) && new FileInfo(gif).Length > 500 && SizeOf(gif) == new Size(640, 480), "pictures: an animated GIF from several pictures");
            }
            List<ActionItem> menu = ActionCatalog.For(new[] { wide, tall });
            ActionItem edit = menu.First(i => i.Key == "Edit");
            Check(edit.Children.Any(c => c.Id == "collage") && edit.Children.Any(c => c.Id == "crop:1:1") && edit.Children.Count <= ActionCatalog.MaxChips, "pictures: the Edit slice offers the tools");
            Check(!ActionCatalog.For(new[] { wide }).First(i => i.Key == "Edit").Children.Any(c => c.Id == "collage"), "pictures: a collage needs several pictures");
            UndoLog.Clear();
            FileOps.Grayscale(new[] { wide });
            Check(UndoLog.UndoLast() == 1, "pictures: every tool can be undone");
        }

        static void Ff(string args)
        {
            var psi = new ProcessStartInfo(FileOps.FindFfmpeg(), "-y -loglevel error " + args) { UseShellExecute = false, CreateNoWindow = true };
            using (Process p = Process.Start(psi)) p.WaitForExit();
        }

        static void Media(string dir)
        {
            if (FileOps.FindFfmpeg() == null) { Console.WriteLine("  SKIP  video and audio tools (ffmpeg not found)"); return; }
            string clip = Path.Combine(dir, "clip.mp4");
            Ff("-f lavfi -i testsrc=duration=4:size=320x240:rate=15 -f lavfi -i sine=frequency=440:duration=4 -pix_fmt yuv420p -shortest \"" + clip + "\"");
            double dur = FileOps.Duration(clip);
            Check(dur > 3.5 && dur < 4.5, "media: the length of a video is read");
            string small = FileOps.CompressToSize(clip, 0.3);
            Check(File.Exists(small) && new FileInfo(small).Length <= 0.3 * 1024 * 1024 && small.EndsWith("_0.3MB.mp4"), "media: compress to a size stays under it");
            string rot = FileOps.RotateVideo(clip, "right");
            Check(File.Exists(rot), "media: rotate a video");
            string fast = FileOps.Speed(clip, 2);
            double fastDur = FileOps.Duration(fast);
            Check(fastDur > 1.5 && fastDur < 2.6, "media: double speed halves the length");
            string merged = FileOps.Merge(new[] { clip, clip });
            double mergedDur = FileOps.Duration(merged);
            Check(mergedDur > 7 && mergedDur < 9, "media: merged clips add up");
            string sheet = FileOps.ContactSheet(clip);
            Check(File.Exists(sheet) && SizeOf(sheet).Width == 1280, "media: a contact sheet of 16 frames");
            File.WriteAllText(Path.ChangeExtension(clip, ".srt"), "1\r\n00:00:00,000 --> 00:00:03,000\r\nHello subtitles\r\n");
            if (FileOps.HasFilter("subtitles"))
            {
                string subbed = FileOps.BurnSubtitles(clip);
                Check(File.Exists(subbed) && subbed.EndsWith("_subtitled.mp4"), "media: subtitles next to the video are burned in");
                Check(ActionCatalog.For(new[] { clip }).First(i => i.Key == "Tools").Children.Any(c => c.Id == "vt:subs"), "media: the Subtitles tool shows only when an .srt is there");
            }
            string wav = Path.Combine(dir, "tone.wav");
            Ff("-f lavfi -i \"sine=frequency=440:duration=1,adelay=1500|1500,apad=pad_dur=1\" \"" + wav + "\"");
            string faded = FileOps.Fade(wav);
            Check(File.Exists(faded) && faded.EndsWith("_fade.wav"), "media: fade in and out");
            string trimmed = FileOps.TrimSilence(wav);
            Check(File.Exists(trimmed) && FileOps.Duration(trimmed) < FileOps.Duration(wav) - 1, "media: silence at the start and end is trimmed");
            string slow = FileOps.Speed(wav, 0.5);
            Check(FileOps.Duration(slow) > FileOps.Duration(wav) * 1.6, "media: half speed for audio");
            Check(ActionCatalog.For(new[] { wav }).First(i => i.Key == "Tools").Children.Any(c => c.Id == "at:silence"), "media: audio gets its Tools slice");
        }

        static void Text(string dir)
        {
            string a = Path.Combine(dir, "a.txt");
            File.WriteAllText(a, "one\nthree\ntwo\nthree\n", new UTF8Encoding(false));
            Check(FileOps.LineEndings(new[] { a }, true) == 1 && File.ReadAllText(a) == "one\r\nthree\r\ntwo\r\nthree\r\n", "text: Windows line endings");
            Check(FileOps.LineEndings(new[] { a }, false) == 1 && File.ReadAllText(a) == "one\nthree\ntwo\nthree\n", "text: Unix line endings");
            Check(FileOps.SortLines(new[] { a }, false) == 1 && File.ReadAllText(a) == "one\nthree\nthree\ntwo\n", "text: sort lines, keeping the file's own line ending");
            Check(FileOps.SortLines(new[] { a }, true) == 1 && File.ReadAllText(a) == "one\nthree\ntwo\n", "text: remove duplicate lines");
            UndoLog.UndoLast();
            Check(File.ReadAllText(a) == "one\nthree\nthree\ntwo\n", "text: in-place edits can be undone");

            string ansi = Path.Combine(dir, "ansi.txt");
            File.WriteAllBytes(ansi, Encoding.Default.GetBytes("café über"));
            Check(FileOps.ToUtf8(new[] { ansi }) == 1 && File.ReadAllText(ansi, new UTF8Encoding(false, true)) == "café über", "text: an ANSI file becomes UTF-8");
            Check(FileOps.ToUtf8(new[] { ansi }) == 0, "text: a file that already is UTF-8 is left alone");

            string json = Path.Combine(dir, "data.json");
            File.WriteAllText(json, "{\"a\":[1,2,{\"b\":\"x\\\"y\"}],\"c\":true,\"d\":null,\"e\":-1.5e3,\"f\":{}}");
            Check(FileOps.TidyJson(new[] { json }) == 1, "text: JSON is tidied");
            string tidy = File.ReadAllText(json);
            Check(tidy.StartsWith("{\r\n  \"a\": [\r\n    1,") && tidy.Contains("\"b\": \"x\\\"y\"") && tidy.Contains("\"f\": {}") && tidy.TrimEnd().EndsWith("}"), "text: tidy JSON is indented and keeps every value");
            File.WriteAllText(json, "{\"a\": [1, 2}");
            bool refused = false;
            try { FileOps.TidyJson(new[] { json }); } catch (InvalidOperationException ex) { refused = ex.Message.Contains("not valid JSON"); }
            Check(refused && File.ReadAllText(json) == "{\"a\": [1, 2}", "text: broken JSON is reported and left untouched");

            string md = Path.Combine(dir, "notes.md");
            File.WriteAllText(md, "# Title\n\nSome **bold** and *it* and `code` and [a link](http://x.y).\n\n- one\n- two\n\n1. first\n2. second\n\n> quoted\n\n```\nraw <b>\n```\n");
            string html = File.ReadAllText(FileOps.MarkdownToHtml(md));
            Check(html.Contains("<h1>Title</h1>") && html.Contains("<strong>bold</strong>") && html.Contains("<em>it</em>") && html.Contains("<code>code</code>")
                  && html.Contains("<a href=\"http://x.y\">a link</a>") && html.Contains("<ul>\n<li>one</li>") && html.Contains("<ol>\n<li>first</li>")
                  && html.Contains("<blockquote>") && html.Contains("raw &lt;b&gt;"), "text: Markdown becomes HTML with headings, lists, links, quotes and code");
            List<ActionItem> menu = ActionCatalog.For(new[] { md });
            Check(menu.First(i => i.Key == "Tools").Children.Any(c => c.Id == "tt:mdhtml") && !ActionCatalog.For(new[] { a }).First(i => i.Key == "Tools").Children.Any(c => c.Id == "tt:mdhtml"),
                "text: Markdown to HTML is offered for .md files only");
        }

        static void Office(string dir)
        {
            Check(FileOps.Classify("x.xlsx") == FileKind.Word && FileOps.Classify("x.pptx") == FileKind.Word, "office: spreadsheets and slides count as documents");
            string how = FileOps.OfficeConverterFor("x.xlsx");
            if (how == null) { Console.WriteLine("  SKIP  Excel to PDF (no Excel or LibreOffice)"); }
            else if (how == "excel")
            {
                string xlsx = Path.Combine(dir, "book.xlsx");
                try
                {
                    dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application"));
                    try
                    {
                        app.Visible = false; app.DisplayAlerts = false;
                        dynamic wb = app.Workbooks.Add();
                        wb.Worksheets[1].Cells[1, 1].Value = "Hello DragFlow";
                        wb.SaveAs(xlsx, 51);
                        wb.Close(false);
                    }
                    finally { app.Quit(); }
                    System.Threading.Thread.Sleep(1500); // Excel takes a moment to leave
                    int excels = Process.GetProcessesByName("EXCEL").Length;
                    string pdf = FileOps.OfficeToPdf(xlsx);
                    Check(File.Exists(pdf) && WinRt.PdfPageCount(pdf) >= 1, "office: an Excel workbook becomes a PDF");
                    for (int i = 0; i < 40 && Process.GetProcessesByName("EXCEL").Length > excels; i++) System.Threading.Thread.Sleep(250);
                    Check(Process.GetProcessesByName("EXCEL").Length <= excels, "office: the invisible Excel is gone afterwards");
                    Check(ActionCatalog.For(new[] { xlsx }).Any(i => i.Id == "word2pdf"), "office: the To PDF slice shows for it");
                }
                catch (Exception ex) { Console.WriteLine("  SKIP  Excel to PDF (" + ex.Message + ")"); }
            }
        }

        static void Everyday(string dir)
        {
            string a = Make.Text(dir, "a.txt", "same"), b = Make.Text(dir, "b.txt", "same"), c = Make.Text(dir, "c.txt", "else");
            Check(FileOps.Compare(a, b).StartsWith("Identical") && FileOps.Compare(a, c).StartsWith("Different"), "everyday: compare two files");
            Check(ActionCatalog.For(new[] { a, b }).Last().Children.Any(x => x.Id == "compare") && !ActionCatalog.For(new[] { a }).Last().Children.Any(x => x.Id == "compare"),
                "everyday: Compare appears for exactly two files");
            string folder = Make.Dir(dir, "stuff");
            Make.Text(folder, "one.txt", "1"); Make.Dir(folder, "sub");
            string csv = FileOps.ListContents(folder);
            string[] lines = File.ReadAllLines(csv);
            Check(csv.EndsWith("stuff contents.csv") && lines.Length == 3 && lines[1].StartsWith("\"sub\",Folder") && lines[2].StartsWith("\"one.txt\",\"TXT\",1,"), "everyday: a folder's contents as a spreadsheet");
            Check(FileOps.RecycleAll(new[] { c }).StartsWith("1 item moved to the Recycle Bin") && !File.Exists(c) && Directory.GetFiles(TestMain.Trash).Any(f => f.EndsWith("c.txt")), "everyday: Recycle Bin goes through the same safe door");
            Check(ActionCatalog.For(new[] { a }).Last().Children.Any(x => x.Id == "recycle"), "everyday: the Recycle Bin option is under More");
        }

        static void Rules(string dir)
        {
            string watch = Make.Dir(dir, "Downloads");
            string old = Make.Text(watch, "old.pdf", "x"), fresh = Make.Text(watch, "new.pdf", "x");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-40)); File.SetCreationTime(old, DateTime.Now.AddDays(-40));
            var rule = new Rule { Name = "tidy", Folder = watch, Pattern = "*.pdf", Action = "recycle", MinAgeDays = 30 };
            Check(rule.Qualifies(old) && !rule.Qualifies(fresh), "rules: \"older than 30 days\" picks the right files");
            Check(!new Rule { Folder = watch, MinSizeMB = 1 }.Qualifies(old), "rules: \"bigger than\" too");
            RuleStore.Save(new List<Rule> { rule });
            Rule back = RuleStore.Load()[0];
            Check(back.MinAgeDays == 30 && back.Scan, "rules: conditions are saved, and an age rule always scans");
            var engine = new RuleEngine();
            var runs = new List<string[]>();
            engine.Run = delegate (Rule r, string[] files) { runs.Add(files); };
            engine.Notice(back, fresh, 0); engine.Process(1000); engine.Process(2000);
            Check(runs.Count == 0, "rules: an age rule ignores files as they arrive (they are new)");
            engine.Apply(new[] { back }, true);
            engine.Process(3000);
            Check(runs.Count == 1 && runs[0].Length == 1 && runs[0][0] == old, "rules: the scan finds the old file and leaves the new one");
            engine.Process(4000);
            Check(runs.Count == 1, "rules: a scan does not repeat within the hour");
            Check(engine.RunNow(new Rule { Folder = watch, Pattern = "*.pdf", Action = "recycle" }) == 1 && runs.Count == 2 && runs[1][0] == fresh,
                "rules: Run now takes the existing files not handled before");
            engine.Dispose();
        }

        /// <summary>The real dial: moving from a slice's icon to one of its options in a long column must never close it.</summary>
        static void ReachOptions(string dir)
        {
            string img = Make.Png(dir, "reach.png");
            List<ActionItem> menu = ActionCatalog.For(new[] { img });
            int edit = menu.FindIndex(i => i.Key == "Edit");
            var ring = new RingOverlay();
            var desk = new FakeDesktop { X = -2600, Y = -2600, Area = new Rectangle(-4000, -4000, 8000, 8000) }; // off screen, so nothing flashes up
            var c = new DragController(ring, desk) { GetSettings = delegate { return new Settings { TipsShown = 99 }; }, GetLearning = delegate { return new Learning(); } };
            try
            {
                var s = new DragSession { OriginX = -2600, OriginY = -2600, FileState = 1, Menu = menu };
                c.OnStarted(s, 0);
                long t = 0;
                Action<int> run = delegate (int ms) { for (int i = 0; i < ms / 15; i++) { t += 15; c.Tick(t); } };
                for (int i = 0; i < 6; i++) { desk.X += 5; desk.Y += 3; t += 15; c.Tick(t); }
                run(700);
                Check(ring.Visible, "reach: the dial opened");
                var geo = new RadialGeometry(menu.Count, 1.0);
                double ix, iy;
                geo.IconCenter(edit, out ix, out iy);
                for (int i = 1; i <= 8; i++) { desk.X = (int)(c.Engine.AnchorX + ix * i / 8); desk.Y = (int)(c.Engine.AnchorY + iy * i / 8); t += 15; c.Tick(t); }
                run(500);
                Check(ring.HasKids, "reach: resting on Edit opens its options");
                RectangleF[] chips = geo.LayoutPills(edit, RadialRenderer.MeasurePills(menu[edit].Children, 1.0));
                int right = menu[edit].Children.FindIndex(k => k.Id == "rotate:right");
                double cx = chips[right].X + chips[right].Width / 2, cy = chips[right].Y + chips[right].Height / 2;
                double sx = desk.X, sy = desk.Y;
                for (int i = 1; i <= 8; i++) { desk.X = (int)(sx + (c.Engine.AnchorX + cx - sx) * i / 8); desk.Y = (int)(sy + (c.Engine.AnchorY + cy - sy) * i / 8); t += 15; c.Tick(t); }
                run(300);
                Check(ring.Visible && c.Engine.State == IntentState.Revealing, "reach: moving on to the Right option keeps the dial open (reason: " + c.Engine.DismissReason + ")");
                Check(ring.HitTest(desk.X, desk.Y) != null && ring.HitTest(desk.X, desk.Y).Id == "rotate:right", "reach: the pointer is on Right");
            }
            finally { ring.Close(); }
        }

        static void Anywhere(string dir)
        {
            ReachOptions(Make.Dir(dir, "reach"));
            var menu = new List<ActionItem>
            {
                new ActionItem("zip", "Zip", "", Color.Purple), new ActionItem("rename", "Rename", "", Color.Orange),
                new ActionItem("", "Resize", "", Color.Green).With(new ActionItem("shrink", "Shrink", "", Color.Green), new ActionItem("resize:50", "50%", "", Color.Green))
            };
            var ring = new RingOverlay();
            ActionItem picked = null;
            ring.Picked += delegate (ActionItem a) { picked = a; };
            try
            {
                ring.ShowForClick(-3000, -3000, menu, 1.0, Clock.Now, null);
                ring.SimulateKey(Keys.R); ring.SimulateKey(Keys.E);
                Check(ring.KeyFocus == 1, "anywhere: typing \"re\" jumps to Rename");
                ring.SimulateKey(Keys.Enter);
                Check(picked != null && picked.Id == "rename", "anywhere: and Enter picks it");
                ring.Close();
                ring.ShowForClick(-3000, -3000, menu, 1.0, Clock.Now, null);
                ring.SimulateKey(Keys.D3); ring.SimulateKey(Keys.S); ring.SimulateKey(Keys.H); ring.SimulateKey(Keys.Enter);
                Check(picked != null && picked.Id == "shrink", "anywhere: typing works inside a slice's options too");
            }
            finally { ring.Close(); }

            string f = Make.Text(dir, "copied.txt");
            var list = new System.Collections.Specialized.StringCollection();
            list.Add(f);
            Clipboard.SetFileDropList(list);
            string[] copied = ClickDial.ClipboardFiles();
            Clipboard.Clear();
            Check(copied != null && copied.Length == 1 && copied[0] == f, "anywhere: copied files are read from the clipboard");

            // settings anywhere
            Make.Text(AppPaths.Roaming, "custom-note.ini", "x=1");
            string zip = Path.Combine(dir, "backup.zip");
            AppPaths.Backup(zip);
            using (var za = System.IO.Compression.ZipFile.OpenRead(zip))
                Check(za.Entries.Any(e => e.Name == "custom-note.ini") && za.Entries.Any(e => e.Name == "settings.ini"), "anywhere: a backup zip holds the settings files");
            File.Delete(Path.Combine(AppPaths.Roaming, "custom-note.ini"));
            AppPaths.Restore(zip);
            Check(File.ReadAllText(Path.Combine(AppPaths.Roaming, "custom-note.ini")) == "x=1", "anywhere: restoring brings them back");
            string elsewhere = Make.Dir(dir, "OneDrive settings");
            AppPaths.MoveSettingsTo(elsewhere);
            Check(File.Exists(Path.Combine(elsewhere, "custom-note.ini")) && File.ReadAllText(Path.Combine(AppPaths.Roaming, "location.txt")).Trim() == elsewhere,
                "anywhere: settings can be moved to a folder of your choice");
        }
    }
}
