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
    /// <summary>Smart features: learning, ranking, profiles, smart Move-to, new actions, rename smarts, shelf, app drops.</summary>
    static class Tier2Tests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            LearningTests();
            Ranking(Make.Dir(root, "rank"));
            ProfilesAndMoveTo(Make.Dir(root, "profiles"));
            Mixed(Make.Dir(root, "mixed"));
            QuickActions(Make.Dir(root, "quick"));
            Media(Make.Dir(root, "media"));
            RenameSmarts(Make.Dir(root, "rename"));
            ShelfTests(Make.Dir(root, "shelf"));
            AppDrops(Make.Dir(root, "drops"));
            ChipLayout();
        }

        // ---------- 2.1 intent detection that learns ----------

        static void LearningTests()
        {
            var l = new Learning();
            for (int i = 0; i < 20; i++) l.Record(new DragOutcome { Revealed = true, ShownMs = 200, Sector = 4, Speed = 600 });
            Check(l.Eagerness < 1.0 && l.ObserveMs > Learning.DefaultObserveMs, "learning: after many quick pass-bys the dial waits longer and hides sooner");
            Check((l.SuppressedSectors & (1 << 4)) != 0, "learning: always going past it to the left teaches \"not when dragging left\"");
            Check(l.Describe().Any(x => x.Contains("left")), "learning: the Smart page says so in plain words");
            l.Save();
            Learning back = Learning.Load();
            Check(Math.Abs(back.Eagerness - l.Eagerness) < 0.01 && back.SuppressedSectors == l.SuppressedSectors && back.TotalDrags == 20,
                "learning: what was learned survives a restart");
            l.ShowMore();
            Check(l.EffectiveSuppressed == 0 && l.EffectiveObserveMs < l.ObserveMs, "learning: \"show it more\" overrides what it learned");
            l.Reset();
            Check(l.Eagerness == 1.0 && l.SuppressedSectors == 0 && l.TotalDrags == 0, "learning: reset starts over");

            var fan = new Learning();
            for (int i = 0; i < 20; i++) fan.Record(new DragOutcome { Revealed = true, Used = true, ShownMs = 900, Sector = 6 });
            Check(fan.Eagerness > 1.0 && fan.SuppressedSectors == 0, "learning: someone who uses the dial gets it for longer");

            var off = new Learning { Enabled = false };
            for (int i = 0; i < 20; i++) off.Record(new DragOutcome { Revealed = true, ShownMs = 100, Sector = 4 });
            Check(off.EffectiveSuppressed == 0 && off.EffectiveObserveMs == Learning.DefaultObserveMs, "learning: turning it off keeps the standard behaviour");

            var e = new IntentEngine { SuppressedSectors = 1 << 4 };
            e.Begin(0, 0, 0);
            for (int t = 8; t <= 120; t += 8) e.AddSample(-t * 0.9, 0, t);
            Check(e.State == IntentState.Dismissed && e.DismissReason == "learned", "learning: a brisk drag in a learned direction keeps the dial closed");
            var slow = new IntentEngine { SuppressedSectors = 1 << 4 };
            slow.Begin(0, 0, 0);
            for (int t = 8; t <= 200; t += 8) slow.AddSample(-t * 0.08, 0, t);
            Check(slow.State == IntentState.Revealing, "learning: a slow, careful drag that way still gets the dial");
            Check(IntentEngine.SectorOf(0, -10) == 6 && IntentEngine.SectorOf(10, 0) == 0 && IntentEngine.SectorOf(-10, 0) == 4, "learning: directions are named consistently");
        }

        // ---------- 2.2 ranking, pinning ----------

        static void Ranking(string dir)
        {
            Usage.Current = new Usage();
            Settings.Current = new Settings();
            string img = Make.Png(dir, "shot.png");
            List<ActionItem> before = ActionCatalog.For(new[] { img });
            Check(before[0].Id == "pdf", "ranking: with no history the dial keeps its designed order");

            for (int i = 0; i < 3; i++) Usage.Current.Record(FileKind.Image, dir, "rename");
            List<ActionItem> after = ActionCatalog.For(new[] { img });
            Check(after[0].Id == "rename", "ranking: the action you use most for pictures moves to 12 o'clock");
            Check(after.Last().Label == "More", "ranking: More always stays last");

            Usage.Current.Record(FileKind.Image, dir, "convert:gif");
            Usage.Current.Record(FileKind.Image, dir, "convert:gif");
            ActionItem convert = ActionCatalog.For(new[] { img }).First(i => i.Label == "Convert");
            Check(convert.Children[0].Id == "convert:gif", "ranking: inside a slice, the option you use most comes first");

            string other = Make.Dir(dir, "elsewhere");
            string txt = Make.Text(other, "t.txt");
            Check(ActionCatalog.For(new[] { txt })[0].Id == "copytext", "ranking: picture habits do not reorder the text-file dial");

            Usage.Current.Record(FileKind.Text, other, "zip");
            Check(ActionCatalog.For(new[] { txt })[0].Id == "copytext", "ranking: a single use is not enough to reorder");

            Settings.Current.Ranking = false;
            Check(ActionCatalog.For(new[] { img })[0].Id == "pdf", "ranking: can be turned off");
            Settings.Current.Ranking = true;

            Settings.Current.PinnedActions = new List<string> { "Zip" };
            Check(ActionCatalog.For(new[] { txt })[0].Id == "zip", "pinning: pinned actions always come first");
            Settings.Current.PinnedActions = new List<string>();

            Usage.Current.Save();
            Usage back = Usage.Load();
            Check(back.Score(FileKind.Image, dir, "rename") > 2.5, "ranking: history survives a restart");
            Usage u = Usage.Load();
            u.Now = delegate { return DateTime.Now.AddDays(60); };
            Check(u.Score(FileKind.Image, dir, "rename") < 1.0, "ranking: old habits fade (two-week half-life)");
            Usage.Current = new Usage();
        }

        // ---------- 2.3 profiles, 2.7 smart Move-to ----------

        static void ProfilesAndMoveTo(string dir)
        {
            Usage.Current = new Usage();
            Settings.Current = new Settings();
            string phone = Make.Dir(dir, "Phone");
            string album = Make.Dir(dir, "Albums");
            string img = Make.Png(phone, "p1.png");
            Profiles.Save(new List<Profile>
            {
                new Profile { Name = "All", Folder = dir, First = new List<string> { "Zip" } },
                new Profile { Name = "Phone photos", Folder = phone, Types = new List<string> { "image" }, First = new List<string> { "Move to", "Resize" },
                              Hide = new List<string> { "Edit", "Strip EXIF" }, MoveTo = new List<string> { album } }
            });
            List<Profile> loaded = Profiles.Load();
            Check(loaded.Count == 2 && loaded[1].MoveTo[0] == album, "profiles: saved and read back");
            List<ActionItem> menu = ActionCatalog.For(new[] { img });
            Check(menu[0].Label == "Move to" && menu[1].Label == "Resize", "profiles: a folder's own actions come first, in its order");
            Check(!menu.Any(i => i.Label == "Edit") && !menu.First(i => i.Label == "More").Children.Any(c => c.Label == "Strip EXIF"),
                "profiles: hidden actions are left out");
            ActionItem move = menu[0];
            Check(move.Children[0].Id == "park" && move.Children[1].Id == "moveto:" + album, "profiles: their Move-to folders come right after the shelf");
            Check(ActionCatalog.For(new[] { Make.Text(phone, "n.txt") })[0].Id == "zip", "profiles: a profile for pictures does not apply to text; the broader one does");

            Profiles.Save(new List<Profile> { new Profile { Name = "Mail", Apps = new List<string> { "OUTLOOK.EXE" }, First = new List<string> { "Move to" } } });
            Check(Profiles.Load().Count == 1 && Profiles.Load()[0].Apps[0] == "outlook.exe", "profiles: a profile can be for a program instead of a folder");
            var mail = new MenuContext { KindHint = FileKind.Pdf, App = "outlook", ShelfEnabled = true };
            Check(ActionCatalog.Build(mail)[0].Label == "Move to", "profiles: drags out of that program get its order");
            var other = new MenuContext { KindHint = FileKind.Pdf, App = "chrome", ShelfEnabled = true };
            Check(ActionCatalog.Build(other)[0].Key == "To images", "profiles: other programs are not affected");

            Profiles.Save(new List<Profile>());
            string src = Make.Dir(dir, "src");
            string openA = Make.Dir(dir, "OpenWindow");
            string recent = Make.Dir(dir, "Recent");
            string file = Make.Text(src, "f.txt");
            Usage.Current.Record(FileKind.Text, src, "moveto:" + recent);
            var ctx = MenuContext.ForPaths(new[] { file });
            ctx.OpenFolders = new List<string> { openA, src };
            ActionItem mv = ActionCatalog.MoveTo(ctx);
            var ids = mv.Children.Select(c => c.Id).ToList();
            Check(ids.First() == "park" && ids.Last() == "movechoose", "move to: the shelf first, \"Choose…\" last");
            Check(ids.Contains("moveto:" + openA) && ids.Contains("moveto:" + recent), "move to: folders open in Explorer and recent targets are offered");
            Check(!ids.Contains("moveto:" + src), "move to: never offers the folder the items are already in");
            Check(mv.Children.Count <= ActionCatalog.MaxChips, "move to: at most eight options");
            ctx.ShelfEnabled = false;
            Check(ActionCatalog.MoveTo(ctx).Children[0].Id != "park", "move to: no shelf option when the shelf is off");
            Usage.Current = new Usage();
        }

        // ---------- 2.4 mixed and multi selections ----------

        static void Mixed(string dir)
        {
            string img = Make.Png(dir, "a.png", 60, 40);
            string pdf2 = FileOps.ToPdf(new[] { Make.Png(dir, "p1.png"), Make.Png(dir, "p2.png") });
            string txt = Make.Text(dir, "n.txt");
            List<ActionItem> menu = ActionCatalog.For(new[] { img, pdf2, txt });
            Check(menu[0].Label == "Combine to PDF" && menu.Any(i => i.Id == "newfolder") && menu.Any(i => i.Label == "Zip"),
                "mixed: pictures + PDFs + text get Combine to PDF, New folder and Zip");
            Check(menu.Count <= ActionCatalog.MaxSlices && menu.All(i => i.Children.Count <= ActionCatalog.MaxChips), "mixed: the dial stays within its limits");

            string combined = FileOps.ToPdf(new[] { img, pdf2 });
            Check(WinRt.PdfPageCount(combined) == 3, "mixed: a picture and a two-page PDF combine into three pages");
            Check(ActionCatalog.For(new[] { pdf2, combined }).Any(i => i.Label == "Combine"), "mixed: several PDFs get Combine");

            string summary = SelectionResolver.Summarize(new[] { img, txt });
            Check(summary.StartsWith("2 items") && summary.Contains("B"), "mixed: the dial centre shows the count and total size");
            Check(SelectionResolver.Summarize(new[] { dir }) == "1 folder", "mixed: folders are counted, not measured");
            Check(SelectionResolver.Summarize(null) == null, "mixed: nothing shown when the selection is unknown");
        }

        // ---------- 2.5 quick actions ----------

        static void QuickActions(string dir)
        {
            UndoLog.Clear();
            string img = Make.Png(dir, "q.png");
            List<ActionItem> menu = ActionCatalog.For(new[] { img });
            ActionItem open = menu.FirstOrDefault(i => i.Label == "Open & share");
            Check(open != null && open.Children.Any(c => c.Id == "share") && open.Children.Any(c => c.Id == "email") && open.Children[0].Id == "open",
                "quick actions: pictures get Open & share (open, apps, share, e-mail)");
            Check(open.Children.Any(c => c.Id == "openwith"), "quick actions: Open with… lets you pick any app");
            ActionItem more = menu.First(i => i.Label == "More");
            Check(more.Children.Any(c => c.Id == "copyhere") && more.Children.Any(c => c.Id == "properties"), "quick actions: Copy here and Properties are under More");
            Check(ActionCatalog.For(new[] { dir })[ActionCatalog.For(new[] { dir }).Count - 1].Children.Any(c => c.Id == "shortcut"), "quick actions: folders can get a desktop shortcut");

            List<string> copies = FileOps.CopyHere(new[] { img, Make.Dir(dir, "folder") });
            Check(copies.Count == 2 && File.Exists(Path.Combine(dir, "q - Copy.png")) && Directory.Exists(Path.Combine(dir, "folder - Copy")),
                "quick actions: Copy here duplicates files and folders next to the originals");
            UndoLog.UndoLast();
            Check(!File.Exists(Path.Combine(dir, "q - Copy.png")), "quick actions: and can be undone");

            Check(FileOps.SuggestFolderName(new[] { "C:\\x\\Invoice 2026-01.pdf", "C:\\x\\Invoice 2026-02.pdf" }) == "Invoice 2026-0",
                "quick actions: New folder suggests the shared start of the names");
            Check(FileOps.SuggestFolderName(new[] { "C:\\x\\a.txt", "C:\\x\\b.txt" }) == "New folder", "quick actions: or \"New folder\" when there is none");

            Check(OpenWith.ExeFromCommand("\"C:\\Program Files\\App\\app.exe\" \"%1\"") == "C:\\Program Files\\App\\app.exe"
                  && OpenWith.ExeFromCommand("C:\\Windows\\notepad.exe %1") == "C:\\Windows\\notepad.exe", "quick actions: app paths are read from Windows' open commands");
            Check(OpenWith.DefaultAppName(".txt") != null, "quick actions: the default app for a type is found");

            Check(FileOps.ClassifyName("x.webp") == FileKind.Image && FileOps.ClassifyName("x.HEIC") == FileKind.Image, "quick actions: WebP and HEIC count as pictures");
            byte[] png = WinRt.DecodeToPng(img);
            using (Image back = Image.FromStream(new MemoryStream(png)))
                Check(back.Width == 40 && back.Height == 30, "quick actions: Windows' own codecs decode pictures (used for WebP, HEIC, AVIF)");
            string ff = FileOps.FindFfmpeg();
            if (ff != null)
            {
                string webp = Path.Combine(dir, "w.webp");
                RunFfmpeg(ff, "-f lavfi -i color=c=red:s=64x48 -frames:v 1 \"" + webp + "\"");
                if (File.Exists(webp))
                {
                    try
                    {
                        List<string> outs = FileOps.ConvertImages(new[] { webp }, "png");
                        using (Image w = Image.FromFile(outs[0])) Check(w.Width == 64 && w.Height == 48, "quick actions: a WebP converts to PNG");
                    }
                    catch (InvalidOperationException ex) { Console.WriteLine("  SKIP  WebP (" + ex.Message + ")"); }
                }
            }
        }

        static void RunFfmpeg(string ff, string args)
        {
            var psi = new ProcessStartInfo(ff, "-y -loglevel error " + args) { UseShellExecute = false, CreateNoWindow = true };
            using (Process p = Process.Start(psi)) p.WaitForExit();
        }

        static void Media(string dir)
        {
            Check(FileOps.ParseTime("1:30") == 90 && FileOps.ParseTime("0:01:02.5") == 62.5 && FileOps.ParseTime("75") == 75, "trim: times are read as seconds or minutes:seconds");
            bool threw = false;
            try { FileOps.ParseTime("soon"); } catch (ArgumentException) { threw = true; }
            Check(threw, "trim: nonsense times are refused");
            string ff = FileOps.FindFfmpeg();
            if (ff == null) { Console.WriteLine("  SKIP  trim / even volume (ffmpeg not found)"); return; }
            string clip = Path.Combine(dir, "clip.mp4");
            RunFfmpeg(ff, "-f lavfi -i testsrc=duration=3:size=160x120:rate=10 -pix_fmt yuv420p \"" + clip + "\"");
            string cut = FileOps.Trim(clip, "0:01", "0:02");
            Check(File.Exists(cut) && cut.EndsWith("clip_trimmed.mp4"), "trim: a clip is cut without re-encoding");
            string wav = Path.Combine(dir, "tone.wav");
            RunFfmpeg(ff, "-f lavfi -i sine=frequency=440:duration=1 \"" + wav + "\"");
            string even = FileOps.Ffmpeg(wav, "normalize");
            Check(File.Exists(even) && even.EndsWith("tone_normalized.wav"), "even volume: audio is levelled into a new file");
            Check(ActionCatalog.For(new[] { wav }).Any(i => i.Id == "ff:normalize") && ActionCatalog.For(new[] { clip }).Any(i => i.Id == "trim"),
                "media: audio gets Even volume, video gets Trim");
        }

        // ---------- 2.9 rename smarts ----------

        static void RenameSmarts(string dir)
        {
            var files = new List<string>();
            for (int i = 1; i <= 3; i++) files.Add(Make.Png(dir, "IMG_000" + i + ".png"));
            List<string> suggestions = FileOps.SuggestPatterns(files);
            Check(suggestions[0] == "{date}_{n}" && suggestions.Contains("IMG_{n}"), "rename: suggests photo dates and the shared name start");
            var plan = FileOps.PlanRename(files, "{date}_{n}");
            Check(Path.GetFileName(plan[0].Value) == File.GetLastWriteTime(files[0]).ToString("yyyy-MM-dd") + "_1.png".Replace("_1", "_01"),
                "rename: {date} uses the date the picture was taken (or modified)");
            plan = FileOps.PlanRename(files, "{folder} {n}");
            Check(Path.GetFileName(plan[2].Value) == "rename 03.png", "rename: {folder} is the containing folder's name");
            Check(FileOps.CleanName("my__holiday.photo  (final)") == "my holiday photo (final)", "rename: clean up turns separators into single spaces");
            Check(FileOps.ApplyStyle("MY holiday_pics", NameStyle.Title) == "My Holiday Pics" && FileOps.ApplyStyle("Ab", NameStyle.Lower) == "ab",
                "rename: case styles");
            Check(FileOps.Conflicts(FileOps.PlanRename(files, "same", NameStyle.AsIs)) == 0, "rename: {n} is added so batch names never clash");
            Make.Text(dir, "taken_01.png");
            Check(FileOps.Conflicts(FileOps.PlanRename(files, "taken_{n}")) == 1, "rename: names already taken are spotted before renaming");
        }

        // ---------- 2.6 shelf ----------

        static void ShelfTests(string dir)
        {
            var store = new ShelfStore();
            int changes = 0;
            store.Changed += delegate { changes++; };
            string a = Make.Text(dir, "a.txt"), b = Make.Text(dir, "b.txt");
            store.Add(new[] { a });
            store.Add(new[] { a, b });
            store.Add(new[] { a });
            Check(store.Groups.Count == 2 && store.Groups.Last().Paths[0] == a, "shelf: parking the same thing again moves it to the end instead of doubling it");
            Check(changes == 3, "shelf: the window is told about every change");
            ShelfStore back = ShelfStore.Load();
            Check(back.Groups.Count == 2 && back.Groups[0].Label == "2 items", "shelf: parked items survive a restart");
            File.Delete(b);
            store.Prune();
            Check(store.Groups.All(g => g.Paths.All(File.Exists)), "shelf: items deleted elsewhere drop off the shelf");
            Check(File.Exists(a), "shelf: parking never moves or changes the files");
            store.Clear();
            Check(store.Groups.Count == 0, "shelf: can be cleared");
        }

        // ---------- 2.8 drops from other apps ----------

        static byte[] Descriptor(string name, long size)
        {
            var block = new byte[4 + 592];
            BitConverter.GetBytes(1).CopyTo(block, 0);
            BitConverter.GetBytes((uint)0x80).CopyTo(block, 4 + 36);
            BitConverter.GetBytes((uint)(size >> 32)).CopyTo(block, 4 + 64);
            BitConverter.GetBytes((uint)size).CopyTo(block, 4 + 68);
            Encoding.Unicode.GetBytes(name).CopyTo(block, 4 + 72);
            return block;
        }

        static void AppDrops(string dir)
        {
            List<VirtualFiles.Descriptor> d = VirtualFiles.ParseDescriptors(Descriptor("report.pdf", 1234));
            Check(d.Count == 1 && d[0].Name == "report.pdf" && d[0].Size == 1234 && !d[0].IsFolder, "app drops: e-mail attachment descriptions are read");
            Check(VirtualFiles.SafeRelative("..\\..\\Windows\\evil.exe") == "Windows\\evil.exe" && VirtualFiles.SafeRelative("a:b?.txt") == "ab.txt",
                "app drops: names from other programs cannot escape the folder or use forbidden characters");

            var data = new DataObject();
            data.SetData("FileGroupDescriptorW", new MemoryStream(Descriptor("notes.txt", 5)));
            data.SetData("FileContents", new MemoryStream(Encoding.ASCII.GetBytes("hello")));
            List<string> saved;
            string[] files = VirtualFiles.FromDrop(data, out saved);
            Check(files != null && files.Length == 1 && File.ReadAllText(files[0]) == "hello" && saved.Count == 1,
                "app drops: an attachment dropped on the dial is saved as a real file first");
            Check(files[0].StartsWith(VirtualFiles.StagingDir), "app drops: it is saved in Downloads\\DragFlow");

            var pic = new DataObject();
            using (var bmp = new Bitmap(20, 10)) { pic.SetData(DataFormats.Bitmap, bmp); files = VirtualFiles.FromDrop(pic, out saved); }
            Check(files != null && files[0].EndsWith(".png") && File.Exists(files[0]), "app drops: a dragged picture is saved as PNG");

            var plain = new DataObject();
            plain.SetData(DataFormats.FileDrop, new[] { Make.Text(dir, "real.txt") });
            files = VirtualFiles.FromDrop(plain, out saved);
            Check(files.Length == 1 && saved.Count == 0, "app drops: normal Explorer drops are used as they are");

            var ctx = new MenuContext { KindHint = FileKind.Pdf, ShelfEnabled = true };
            Check(ActionCatalog.Build(ctx).Any(i => i.Id == "pdfimages" || i.Children.Any(c => c.Id == "pdfimages")), "app drops: an attachment named .pdf gets the PDF dial before it is saved");
            Check(FileOps.ClassifyName("Holiday photo.JPG") == FileKind.Image, "app drops: kinds are guessed from names alone");
        }

        // ---------- layout of up to eight options ----------

        static void ChipLayout()
        {
            bool clean = true;
            for (int n = 3; n <= ActionCatalog.MaxSlices; n++)
            {
                var g = new RadialGeometry(n, 1.25);
                for (int seg = 0; seg < n; seg++)
                    for (int count = 1; count <= ActionCatalog.MaxChips; count++)
                    {
                        float[] widths = Enumerable.Range(0, count).Select(i => (float)((i % 2 == 0 ? RadialRenderer.MaxPillWidth : 60) * g.Scale)).ToArray();
                        RectangleF[] chips = g.LayoutPills(seg, widths);
                        for (int i = 0; i < chips.Length; i++)
                        {
                            RectangleF c = chips[i];
                            double nx = Math.Max(c.Left, Math.Min(0, c.Right)), ny = Math.Max(c.Top, Math.Min(0, c.Bottom));
                            if (Math.Sqrt(nx * nx + ny * ny) < g.Outer) clean = false;
                            if (Math.Max(Math.Abs(c.Left), Math.Abs(c.Right)) > g.HalfSize || Math.Max(Math.Abs(c.Top), Math.Abs(c.Bottom)) > g.HalfSize) clean = false;
                            for (int j = i + 1; j < chips.Length; j++) if (c.IntersectsWith(chips[j])) clean = false;
                        }
                    }
            }
            Check(clean, "layout: up to eight wide options never overlap each other, the dial or the window edge, for any slice");
        }
    }
}
