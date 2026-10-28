using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace DragFlow.Tests
{
    /// <summary>Conversion in every direction: pictures, video, audio, web pages and text.</summary>
    static class ConvertTests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Options();
            Pictures(Make.Dir(root, "pictures"));
            Media(Make.Dir(root, "media"));
            Web(Make.Dir(root, "web"));
            Menus(Make.Dir(root, "menus"));
        }

        static void Options()
        {
            var o = ConvertOptions.Parse("webp;q=70;max=1920;kb=300;out=sub");
            Check(o.Format == "webp" && o.Quality == 70 && o.MaxSide == 1920 && o.TargetKB == 300 && o.Output == "sub", "convert: options are read from the action");
            Check(ConvertOptions.Parse(o.ToString()).ToString() == o.ToString(), "convert: options survive a round trip");
            Check(ConvertOptions.Parse("avif").Quality == 70 && ConvertOptions.Parse("jpg").Quality == 90, "convert: each format has a sensible standard quality");
            var v = ConvertOptions.Parse("webm;h=720;vq=small;noaudio=1;br=128");
            Check(v.Height == 720 && v.VideoQuality == "small" && v.NoAudio && v.Bitrate == 128, "convert: video and audio options are read too");
        }

        static Size SizeOf(string path)
        {
            using (Image img = Image.FromStream(new MemoryStream(WinRt.DecodeToPng(path)))) return img.Size;
        }

        /// <summary>A photo-like picture (noise compresses badly, so file-size targets are tested properly).</summary>
        static string Photo(string dir, string name, int w, int h)
        {
            var rnd = new Random(7);
            string path = Path.Combine(dir, name);
            using (var bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.SkyBlue);
                    for (int i = 0; i < 400; i++)
                        using (var b = new SolidBrush(Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256))))
                            g.FillEllipse(b, rnd.Next(w), rnd.Next(h), rnd.Next(5, 80), rnd.Next(5, 80));
                }
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        static void Pictures(string dir)
        {
            string png = Photo(dir, "photo.png", 640, 400);
            var writable = Converter.ImageFormats().Select(f => f.Id).ToList();
            Check(writable.Contains("jpg") && writable.Contains("png") && writable.Contains("ico"), "convert: JPG, PNG and ICO can always be written");
            foreach (string f in new[] { "webp", "avif", "heic" })
                if (!writable.Contains(f)) Console.WriteLine("  SKIP  writing " + f.ToUpperInvariant() + " (no encoder on this PC)");

            var made = new Dictionary<string, string>();
            foreach (string f in writable.Where(x => x != "png"))
            {
                try
                {
                    string out1 = Converter.ConvertImages(new[] { png }, new ConvertOptions { Format = f, Quality = ConvertOptions.DefaultQuality(f) })[0];
                    made[f] = out1;
                    bool ok = File.Exists(out1) && out1.EndsWith("." + f) && new FileInfo(out1).Length > 0;
                    if (f != "ico") ok = ok && SizeOf(out1) == new Size(640, 400);
                    Check(ok, "convert: PNG → " + f.ToUpperInvariant() + " makes a real " + f.ToUpperInvariant() + " at the same size");
                }
                catch (Exception ex) { Check(false, "convert: PNG → " + f.ToUpperInvariant() + " (" + ex.Message + ")"); }
            }
            if (made.ContainsKey("ico"))
                using (var ico = new Icon(made["ico"], 48, 48)) Check(ico.Width == 48, "convert: the icon has the sizes Windows needs");

            foreach (string f in new[] { "webp", "avif", "heic" })
            {
                if (!made.ContainsKey(f)) continue;
                try
                {
                    string jpg = Converter.ConvertImages(new[] { made[f] }, new ConvertOptions { Format = "jpg" })[0];
                    using (Image img = Image.FromFile(jpg))
                        Check(img.RawFormat.Equals(ImageFormat.Jpeg) && img.Width == 640, "convert: " + f.ToUpperInvariant() + " → JPG works (the other direction)");
                }
                catch (Exception ex) { Check(false, "convert: " + f.ToUpperInvariant() + " → JPG (" + ex.Message + ")"); }
            }

            string small = Converter.ConvertImages(new[] { png }, new ConvertOptions { Format = "jpg", MaxSide = 320, Output = "sub" })[0];
            Check(SizeOf(small) == new Size(320, 200) && Path.GetFileName(Path.GetDirectoryName(small)) == "Converted", "convert: can resize and save into a Converted folder");

            string big = Photo(dir, "big.png", 2400, 1600);
            ConvertOptions target = new ConvertOptions { Format = "jpg", TargetKB = 150 };
            string fit = Converter.ConvertImages(new[] { big }, target)[0];
            Check(new FileInfo(fit).Length <= 150 * 1024, "convert: \"no bigger than 150 KB\" is met");
            if (writable.Contains("webp"))
            {
                string fitW = Converter.ConvertImages(new[] { big }, new ConvertOptions { Format = "webp", TargetKB = 60 })[0];
                Check(new FileInfo(fitW).Length <= 60 * 1024, "convert: size targets work for WebP too");
            }
            string same = Converter.ConvertImages(new[] { Make.Png(dir, "s.png") }, new ConvertOptions { Format = "png", MaxSide = 10 })[0];
            Check(same.EndsWith("s_converted.png"), "convert: resizing into the same format makes a separate copy");
            UndoLog.Clear();
            Converter.ConvertImages(new[] { png }, new ConvertOptions { Format = "jpg" });
            Check(UndoLog.UndoLast() == 1, "convert: every conversion can be undone");
        }

        static void RunFf(string ff, string args)
        {
            var psi = new ProcessStartInfo(ff, "-y -loglevel error " + args) { UseShellExecute = false, CreateNoWindow = true };
            using (Process p = Process.Start(psi)) p.WaitForExit();
        }

        static void Media(string dir)
        {
            string ff = FileOps.FindFfmpeg();
            if (ff == null) { Console.WriteLine("  SKIP  video and audio conversion (ffmpeg not found)"); return; }
            string clip = Path.Combine(dir, "clip.mp4");
            RunFf(ff, "-f lavfi -i testsrc=duration=2:size=320x240:rate=15 -f lavfi -i sine=frequency=440:duration=2 -pix_fmt yuv420p -shortest \"" + clip + "\"");
            foreach (string f in Converter.VideoFormats.Where(x => x != "mp4"))
            {
                if (!Converter.CanWriteMedia(f)) { Console.WriteLine("  SKIP  video to " + f); continue; }
                var outs = Converter.ConvertMedia(new[] { clip }, new ConvertOptions { Format = f, Height = f == "gif" ? 120 : 0 });
                Check(File.Exists(outs[0]) && outs[0].EndsWith("." + f) && new FileInfo(outs[0]).Length > 1000, "convert: MP4 → " + f.ToUpperInvariant());
            }
            var shrunk = Converter.ConvertMedia(new[] { clip }, new ConvertOptions { Format = "mp4", Height = 120, VideoQuality = "small", NoAudio = true });
            Check(shrunk[0].EndsWith("clip_converted.mp4"), "convert: MP4 → smaller MP4 without sound");
            string wav = Path.Combine(dir, "tone.wav");
            RunFf(ff, "-f lavfi -i sine=frequency=440:duration=1 \"" + wav + "\"");
            foreach (string f in Converter.AudioFormats.Where(x => x != "wav"))
            {
                if (!Converter.CanWriteMedia(f)) { Console.WriteLine("  SKIP  audio to " + f); continue; }
                var outs = Converter.ConvertMedia(new[] { wav }, new ConvertOptions { Format = f, Bitrate = 128 });
                Check(File.Exists(outs[0]) && new FileInfo(outs[0]).Length > 100, "convert: WAV → " + f.ToUpperInvariant());
            }
            string mp3 = Directory.GetFiles(dir, "tone*.mp3").FirstOrDefault();
            if (mp3 != null)
            {
                var back = Converter.ConvertMedia(new[] { mp3 }, new ConvertOptions { Format = "wav" });
                Check(File.Exists(back[0]), "convert: MP3 → WAV (the other direction)");
            }
            var sound = Converter.ConvertMedia(new[] { clip }, new ConvertOptions { Format = "m4a" });
            Check(sound[0].EndsWith(".m4a"), "convert: the sound of a video can be saved as M4A");
        }

        static void Web(string dir)
        {
            if (WebCapture.FindEdge() == null) { Console.WriteLine("  SKIP  web pages (Microsoft Edge not found)"); return; }
            string html = Path.Combine(dir, "page.html");
            File.WriteAllText(html, "<html><body style='font-family:Segoe UI'><h1>Hello DragFlow</h1>" + string.Concat(Enumerable.Repeat("<p>Line of text for the page.</p>", 80)) + "</body></html>");
            string pdf = WebCapture.ToPdf(html);
            Check(File.Exists(pdf) && WinRt.PdfPageCount(pdf) >= 1, "web: a web page is saved as a PDF");
            string jpg = WebCapture.ToImage(html, "jpg");
            using (Image img = Image.FromFile(jpg)) Check(img.RawFormat.Equals(ImageFormat.Jpeg) && img.Height > img.Width, "web: a web page is saved as one long JPG");
            string png = WebCapture.ToImage(html, "png");
            Check(File.Exists(png) && png.EndsWith(".png"), "web: or as PNG");
            Check(!Directory.GetFiles(dir, "*.pdf").Any(f => f != pdf), "web: the picture route leaves no extra PDF behind");

            string txt = Make.Text(dir, "notes.txt", "First line\r\nSecond line with <angle> & ampersand");
            string tpdf = WebCapture.ToPdf(txt);
            Check(File.Exists(tpdf) && WinRt.PdfPageCount(tpdf) == 1, "web: a text file is saved as a PDF");

            string url = Path.Combine(dir, "site.url");
            File.WriteAllText(url, "[InternetShortcut]\r\nURL=javascript:alert(1)\r\n");
            bool refused = false;
            try { WebCapture.ToPdf(url); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "web: only http and https shortcuts are opened");
            File.WriteAllText(url, "[InternetShortcut]\r\nURL=https://example.com/\r\n");
            Check(WebCapture.UrlOf(url) == "https://example.com/", "web: the address is read from a .url shortcut");
            UndoLog.Clear();
            string again = WebCapture.ToPdf(html);
            Check(UndoLog.UndoLast() == 1 && !File.Exists(again), "web: saving a page can be undone");
        }

        static void Menus(string dir)
        {
            Usage.Current = new Usage();
            string webp = Path.Combine(dir, "pic.webp");
            File.WriteAllBytes(webp, new byte[] { 1 });
            List<ActionItem> m = ActionCatalog.For(new[] { webp });
            Check(m[0].Id == "convert:jpg" && m[0].Label == "To JPG", "menus: WebP, HEIC and AVIF pictures lead with To JPG");
            ActionItem conv = m.First(i => i.Key == "Convert");
            Check(!conv.Children.Any(c => c.Id == "convert:webp") && conv.Children.Last().Id == "convertdlg", "menus: Convert never offers the format it already is, and ends with Options…");
            string jpg = Make.Png(dir, "p.jpg");
            ActionItem jconv = ActionCatalog.For(new[] { jpg }).First(i => i.Key == "Convert");
            Check(jconv.Children.Any(c => c.Id == "convert:webp") == Converter.CanWrite("webp") && !jconv.Children.Any(c => c.Id == "convert:jpg"),
                "menus: a JPG can be turned into WebP and the others");
            Check(m.All(i => i.Children.Count <= ActionCatalog.MaxChips) && m.Count <= ActionCatalog.MaxSlices, "menus: still within the dial's limits");
            if (WebCapture.FindEdge() != null)
            {
                string html = Make.Text(dir, "x.html", "<p>x</p>");
                List<ActionItem> w = ActionCatalog.For(new[] { html });
                Check(w[0].Id == "webpdf" && w.Any(i => i.Key == "To picture"), "menus: web pages get To PDF and To picture");
                Check(ActionCatalog.For(new[] { Make.Text(dir, "t.txt") }).Any(i => i.Id == "webpdf"), "menus: text files get To PDF");
            }
            string pdf = FileOps.ToPdf(new[] { Make.Png(dir, "pg.png") });
            ActionItem toImages = ActionCatalog.For(new[] { pdf }).First(i => i.Key == "To images");
            Check(toImages.Children.Any(c => c.Id == "pdfimages:jpg") && toImages.Children.Any(c => c.Id == "pdfimages"), "menus: PDF pages can become PNG or JPG");
            string pages = WinRt.PdfToImages(pdf, "jpg");
            Check(Directory.GetFiles(pages, "*.jpg").Length == 1, "convert: PDF → JPG pages");
        }
    }
}
