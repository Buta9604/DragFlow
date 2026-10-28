using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DragFlow.Tests
{
    /// <summary>Tests for the extended action set: archives, move-to, image edits, organising, OCR/PDF, custom actions, menus.</summary>
    static class FeatureTests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        static string Png(string dir, string name, int w, int h, Color c, string text = null)
        {
            string path = Path.Combine(dir, name);
            using (var bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(c);
                    if (text != null) using (var f = new Font("Segoe UI", 36)) g.DrawString(text, f, Brushes.Black, 10, 20);
                }
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        static Size SizeOf(string path)
        {
            using (var img = Image.FromFile(path)) return img.Size;
        }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            CustomActions.OverridePath = Path.Combine(root, "actions.ini");

            Archives(Dir(root, "archives"));
            MoveTo(Dir(root, "move"));
            ImageEdits(Dir(root, "images"));
            Organize(Dir(root, "organize"));
            Text(Dir(root, "text"));
            OcrAndPdf(Dir(root, "ocr"));
            Video(Dir(root, "video"));
            Custom(Dir(root, "custom"));
            Menus(Dir(root, "menus"));
        }

        static string Dir(string root, string name)
        {
            string d = Path.Combine(root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        static void Archives(string dir)
        {
            string src = Dir(dir, "src");
            File.WriteAllText(Path.Combine(src, "one.txt"), "1");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "sub", "two.txt"), "2");
            string zip = FileOps.Zip(new[] { src });

            string into = FileOps.Extract(zip, true);
            Check(File.Exists(Path.Combine(into, "src", "sub", "two.txt")), "zip extracts into a new folder with its structure");
            string again = FileOps.Extract(zip, true);
            Check(again != into && Directory.Exists(again), "extracting twice makes a second folder instead of overwriting");

            string evil = Path.Combine(dir, "evil.zip");
            using (var fs = new FileStream(evil, FileMode.Create))
            using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
                using (var w = new StreamWriter(za.CreateEntry("../escaped.txt").Open())) w.Write("x");
            bool refused = false;
            try { FileOps.Extract(evil, true); } catch (InvalidDataException) { refused = true; }
            Check(refused && !File.Exists(Path.Combine(dir, "escaped.txt")), "archive entries that try to escape the folder are refused");

            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (File.Exists(tar))
            {
                string tgz = Path.Combine(dir, "bundle.tar.gz");
                var psi = new ProcessStartInfo(tar, "-czf \"" + tgz + "\" -C \"" + src + "\" one.txt") { UseShellExecute = false, CreateNoWindow = true };
                using (Process p = Process.Start(psi)) p.WaitForExit();
                string outDir = FileOps.Extract(tgz, true);
                Check(Path.GetFileName(outDir) == "bundle" && File.Exists(Path.Combine(outDir, "one.txt")), ".tar.gz extracts via Windows tar into a folder named after it");
            }
        }

        static void MoveTo(string dir)
        {
            string target = Dir(dir, "target");
            string f = Path.Combine(dir, "note.txt");
            File.WriteAllText(f, "hi");
            File.WriteAllText(Path.Combine(target, "note.txt"), "existing");
            Check(FileOps.MoveTo(new[] { f }, target) == 1 && File.Exists(Path.Combine(target, "note (1).txt")), "move to a folder renames instead of overwriting");
            Check(UndoLog.UndoLast() == 1 && File.Exists(f), "undo brings a moved file back");
            bool threw = false;
            try { FileOps.MoveTo(new[] { Path.Combine(target, "note.txt") }, target); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "moving into the folder it is already in is reported, not silently ignored");
        }

        static void ImageEdits(string dir)
        {
            string img = Png(dir, "wide.png", 200, 100, Color.SeaGreen);
            List<string> half = FileOps.Resize(new[] { img }, "50");
            Check(SizeOf(half[0]) == new Size(100, 50) && half[0].EndsWith("_50pct.png"), "resize 50% halves the image and keeps PNG");
            List<string> big = FileOps.Resize(new[] { img }, "1920");
            Check(SizeOf(big[0]) == new Size(200, 100), "resize to 1920 px never enlarges a smaller image");

            Check(FileOps.Rotate(new[] { img }, "right") == 1 && SizeOf(img) == new Size(100, 200), "rotate right turns the image in place");
            Check(UndoLog.UndoLast() == 1 && SizeOf(img) == new Size(200, 100), "undo restores the image from before the rotation");
        }

        static void Organize(string dir)
        {
            string a = Path.Combine(dir, "a.txt"), b = Dir(dir, "deep");
            File.WriteAllText(a, "same content");
            File.WriteAllText(Path.Combine(b, "a copy.txt"), "same content");
            File.WriteAllText(Path.Combine(Dir(b, "deeper"), "a again.txt"), "same content");
            File.WriteAllText(Path.Combine(dir, "unique.txt"), "different");
            long freed;
            int moved = FileOps.MoveDuplicates(dir, out freed);
            Check(moved == 2 && File.Exists(a) && Directory.GetFiles(Path.Combine(dir, "Duplicates")).Length == 2, "duplicates: two extra copies moved aside, the first kept");
            Check(freed == 2 * "same content".Length, "duplicates: reports how much space they used");
            Check(UndoLog.UndoLast() == 2 && File.Exists(Path.Combine(b, "a copy.txt")), "undo puts duplicates back where they were");

            Check(!Directory.Exists(Path.Combine(dir, "Duplicates")), "undo also removes the Duplicates folder it had made");
            Dir(Path.Combine(dir, "empty1"), "empty2");
            Check(FileOps.RemoveEmptyFolders(dir) == 2 && Directory.Exists(b), "remove empty folders keeps folders that have files");

            List<SizeEntry> report = FileOps.SizeReport(dir);
            Check(report[0].IsFolder && report[0].Name == "deep" && report[0].Bytes == 2 * "same content".Length, "size report puts the biggest item first with its total");

            string sorted = Dir(dir, "bydate");
            string f = Path.Combine(sorted, "old.txt");
            File.WriteAllText(f, "x");
            File.SetLastWriteTime(f, new DateTime(2024, 3, 9));
            Check(FileOps.SortByDate(sorted) == 1 && File.Exists(Path.Combine(sorted, "2024-03", "old.txt")), "sort by date files things into year-month folders");
            UndoLog.UndoLast();
        }

        static void Text(string dir)
        {
            string a = Path.Combine(dir, "a.md"), b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "hello world\nsecond line");
            File.WriteAllText(b, "x");
            Check(FileOps.TextStats(new[] { a }) == "2 lines · 4 words · 23 characters", "count reports lines, words and characters");
            string both = FileOps.ReadTextForClipboard(new[] { a, b });
            Check(both.Contains("===== a.md =====") && both.Contains("===== b.txt ====="), "copying several text files labels each one");
        }

        static void OcrAndPdf(string dir)
        {
            string img = Png(dir, "words.png", 600, 120, Color.White, "Hello DragFlow 2026");
            if (WinRt.OcrAvailable())
                Check(WinRt.OcrImages(new[] { img }).Contains("DragFlow"), "copy text reads the words in an image (Windows OCR)");
            else Console.WriteLine("  SKIP  OCR (no OCR language installed)");

            Png(dir, "second.png", 300, 200, Color.Orange);
            string pdf = FileOps.ToPdf(new[] { img, Path.Combine(dir, "second.png") });
            Check(WinRt.PdfPageCount(pdf) == 2, "Windows' PDF engine opens DragFlow's PDFs");
            string pages = WinRt.PdfToImages(pdf);
            Check(Directory.GetFiles(pages, "*.png").Length == 2, "PDF to images writes one PNG per page");
            if (WinRt.OcrAvailable())
            {
                string text = WinRt.OcrPdf(pdf); // "l" and "I" look alike in Segoe UI, so check unambiguous words
                Check(text.Contains("Hello") && text.Contains("2026"), "copy text from a PDF reads its pages");
            }
        }

        static void Video(string dir)
        {
            string ff = FileOps.FindFfmpeg();
            if (ff == null) { Console.WriteLine("  SKIP  video (ffmpeg not found)"); return; }
            string clip = Path.Combine(dir, "clip.mp4");
            var psi = new ProcessStartInfo(ff, "-y -loglevel error -f lavfi -i testsrc=duration=2:size=320x240:rate=10 -pix_fmt yuv420p \"" + clip + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (Process p = Process.Start(psi)) p.WaitForExit();
            string thumb = FileOps.Ffmpeg(clip, "thumb");
            Check(File.Exists(thumb) && SizeOf(thumb) == new Size(320, 240), "video thumbnail saves a frame as JPG");
        }

        static void Custom(string dir)
        {
            File.WriteAllLines(CustomActions.OverridePath, new[]
            {
                "# comment",
                "",
                "Open in Notepad | .txt,.md | notepad.exe | \"{file}\"",
                "Everything | * | tool.exe | {files}",
                "Folders only | folder | cmd.exe",
                "broken line without pipes"
            });
            List<CustomAction> all = CustomActions.Load();
            Check(all.Count == 3 && all[2].Args == "\"{file}\"", "custom actions skip comments and bad lines; arguments default to the file");

            string txt = Path.Combine(dir, "a.txt"), png = Path.Combine(dir, "b.png");
            File.WriteAllText(txt, ""); File.WriteAllText(png, "");
            Check(all[0].Matches(new[] { txt }) && !all[0].Matches(new[] { txt, png }), "custom action types must fit every dragged item");
            Check(all[2].Matches(new[] { dir }) && !all[2].Matches(new[] { txt }), "the folder type matches folders only");
            Check(CustomActions.Expand(all[0], new[] { txt, txt }).Count == 2, "{file} runs once per item");
            List<string> once = CustomActions.Expand(all[1], new[] { txt, png });
            Check(once.Count == 1 && once[0] == "\"" + txt + "\" \"" + png + "\"", "{files} passes every item in one go");
        }

        static void Menus(string dir)
        {
            string img = Png(dir, "pic.png", 10, 10, Color.Red);
            string txt = Path.Combine(dir, "notes.txt");
            File.WriteAllText(txt, "x");
            string zip = FileOps.Zip(new[] { txt });

            List<ActionItem> image = ActionCatalog.For(new[] { img });
            Check(image.Count <= ActionCatalog.MaxSlices && image[0].Id == "pdf"
                  && image.Any(i => i.Label == "Resize" && i.Children.Count == 4)
                  && image.Any(i => i.Label == "Edit" && i.Children.Any(c => c.Id == "rotate:right"))
                  && image.Any(i => i.Label == "Move to" && i.Children.Count >= 4), "image menu: PDF, Convert, Resize, Edit (with Rotate), Move to …");
            Check(image.All(i => i.Children.Count <= ActionCatalog.MaxChips), "no slice offers more than eight options");
            Check(ActionCatalog.For(new[] { zip })[0].Id == "extract", "archives lead with Extract here");
            Check(ActionCatalog.For(new[] { dir }).Any(i => i.Label == "Organize" && i.Children.Any(k => k.Id == "sortdate")), "folders get Organize with sort by date");

            File.WriteAllLines(CustomActions.OverridePath, new[] { "Shout | .txt | cmd.exe | /c echo {file}" });
            List<ActionItem> text = ActionCatalog.For(new[] { txt });
            Check(text.Any(i => i.Label == "Custom" && i.Children[0].Id == "custom:Shout"), "matching custom actions get their own slice");
            List<ActionItem> imageFull = ActionCatalog.For(new[] { img });
            Check(!imageFull.Any(i => i.Label == "Custom"), "custom actions only appear for the file types they list");
            File.WriteAllLines(CustomActions.OverridePath, new[] { "Shout | * | cmd.exe | /c echo {file}" });
            List<ActionItem> full = ActionCatalog.For(new[] { img });
            Check(full.Count <= ActionCatalog.MaxSlices && full.Last().Children[0].Id == "custom:Shout", "when the dial is full, custom actions go under More");
            Check(ActionCatalog.For(null).Any(i => i.Label == "Custom"), "custom actions for any file type also show when the selection is unknown");
            File.Delete(CustomActions.OverridePath);
            Check(ActionCatalog.For(null).Select(i => i.Label).SequenceEqual(new[] { "Rename", "Zip", "Move to", "More" }), "unknown selection falls back to a generic menu");
        }
    }
}
