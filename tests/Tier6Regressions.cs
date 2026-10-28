using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>Regression checks for the review of Tier 6.</summary>
    static class Tier6Regressions
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Recipes(Make.Dir(root, "recipes"));
            Restore(Make.Dir(root, "restore"));
            Phone(Make.Dir(root, "phone"));
            Titles(Make.Dir(root, "titles"));
            Checksums(Make.Dir(root, "checksums"));
            AiSizes(Make.Dir(root, "ai"));
            ScriptPipes(Make.Dir(root, "scripts"));
            SeeThrough(Make.Dir(root, "dial"));
            ShapeBeforeSelectionKnown();
        }

        /// <summary>Found live: a quick Z drawn while DragFlow is still finding out what is dragged was lost.</summary>
        static void ShapeBeforeSelectionKnown()
        {
            var ov = new FakeOverlay();
            var desk = new FakeDesktop { X = 500, Y = 500 };
            var bubble = new FakeBubble();
            var c = new DragController(ov, desk) { GetSettings = delegate { return new Settings(); }, GetLearning = delegate { return new Learning(); }, Bubble = bubble };
            var s = new DragSession { OriginX = 500, OriginY = 500, FileState = 0 };
            c.OnStarted(s, 0);
            long t = 0;
            Action<int, int> to = delegate (int x, int y)
            {
                double x0 = desk.X, y0 = desk.Y, len = Math.Sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0));
                for (double d = 8; d <= len; d += 8) { desk.X = (int)(x0 + (x - x0) * d / len); desk.Y = (int)(y0 + (y - y0) * d / len); t += 10; c.Tick(t); }
            };
            to(900, 500); to(1060, 500); to(910, 630); to(1070, 630);
            Check(!bubble.Visible, "review 6: (nothing offered while the selection is still unknown)");
            s.Menu = ActionCatalog.For((string[])null);
            s.FileState = 1;
            t += 15; c.Tick(t);
            Check(bubble.Visible && bubble.Offered != null && bubble.Offered.Id == "zip", "review 6: a shape drawn before the selection was known is offered once it is");
        }

        /// <summary>Review 6, #1 and #4: a recipe never runs itself; only safe, replayable steps are recorded.</summary>
        static void Recipes(string dir)
        {
            RecipeStore.Save(new List<Recipe>
            {
                new Recipe { Name = "Loop", Types = "*", Steps = new List<string> { "recipe:Loop" } },
                new Recipe { Name = "A", Types = "*", Steps = new List<string> { "copyhere", "recipe:B" } },
                new Recipe { Name = "B", Types = "*", Steps = new List<string> { "recipe:A" } }
            });
            var runner = new ActionRunner(new Control(), new FakeNotifier());
            string f = Make.Text(dir, "x.txt");
            ActionResult r = runner.Execute("recipe:Loop", new[] { f }, new DropInfo(), new Job("Loop"));
            Check(r.Failures.Any(x => x.Contains("uses itself")), "review 6: a recipe that contains itself stops with a message instead of crashing DragFlow");
            r = runner.Execute("recipe:A", new[] { f }, new DropInfo(), new Job("A"));
            Check(r.Failures.Any(x => x.Contains("uses itself")), "review 6: so do two recipes that contain each other");
            RecipeStore.Save(new List<Recipe>());

            RecipeRecorder.Clear();
            RecipeRecorder.Note("recipe:Loop", "Loop", new[] { f }, new[] { f });
            RecipeRecorder.Note("ai:summary", "AI summary", new[] { f }, new[] { f + ".md" });
            RecipeRecorder.Note("trim:0:05|0:10", "Trim", new[] { f }, new[] { f });
            RecipeRecorder.Note("smartname", "Smart name", new[] { f }, null);
            Check(RecipeRecorder.Suggest(DateTime.Now).Count == 0, "review 6: recipes, AI steps, steps with separators and steps that make nothing are not recorded");
        }

        /// <summary>Review 6, #16: restoring a backup brings back settings files only; scripts move with the settings folder.</summary>
        static void Restore(string dir)
        {
            string zip = Path.Combine(dir, "crafted.zip");
            using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("settings.ini").Open())) w.Write("Enabled=1");
                using (var w = new StreamWriter(z.CreateEntry("scripts/evil.ps1").Open())) w.Write("# DragFlow: name=Evil");
            }
            AppPaths.Restore(zip);
            Check(File.Exists(AppPaths.RoamingFile("settings.ini")) && !File.Exists(Path.Combine(Scripts.Folder, "evil.ps1")), "review 6: a backup file cannot plant scripts");
        }

        static int Status(string url, string method)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method; req.Timeout = 8000;
            try { using (var resp = (HttpWebResponse)req.GetResponse()) return (int)resp.StatusCode; }
            catch (WebException ex) { var resp = ex.Response as HttpWebResponse; return resp == null ? -1 : (int)resp.StatusCode; }
        }

        /// <summary>Review 6, #3: HEAD is not a download, browser icon requests do not stop it, a slow visitor blocks nobody.</summary>
        static void Phone(string dir)
        {
            PhoneTransfer.LoopbackOnly = true;
            var t = new PhoneTransfer(new[] { Make.Text(dir, "a.txt", "hello") });
            try
            {
                string origin = t.Url.Substring(0, t.Url.IndexOf('/', 8));
                Check(Status(t.Url + "f/0", "HEAD") == 200 && t.Downloads == 0, "review 6: a HEAD request is not counted as a download");
                for (int i = 0; i < 60; i++) Status(origin + "/favicon.ico", "GET");
                Check(Status(t.Url, "GET") == 200, "review 6: the phone's own icon requests never stop the transfer");
                var slow = new List<TcpClient>();
                for (int i = 0; i < 5; i++)
                {
                    var c = new TcpClient();
                    c.Connect(IPAddress.Loopback, int.Parse(origin.Substring(origin.LastIndexOf(':') + 1)));
                    c.GetStream().WriteByte((byte)'G');
                    slow.Add(c);
                }
                var watch = Stopwatch.StartNew();
                int s = Status(t.Url + "f/0", "GET");
                Check(s == 200 && watch.ElapsedMilliseconds < 3000, "review 6: visitors that send nothing do not hold up a real download");
                foreach (TcpClient c in slow) c.Close();
            }
            finally { t.Stop(); PhoneTransfer.LoopbackOnly = false; }
        }

        /// <summary>Review 6, #5 and #17: encrypted PDFs, bookmark titles and "bill" do not give wrong names.</summary>
        static void Titles(string dir)
        {
            string enc = Path.Combine(dir, "statement.pdf");
            File.WriteAllText(enc, "%PDF-1.6\n1 0 obj << /Title (Ü9¤ ½Þq) >> endobj\ntrailer << /Info 1 0 R /Encrypt 5 0 R >>\n%%EOF", Encoding.GetEncoding(28591));
            Check(FileOps.PdfTitle(enc) == null, "review 6: an encrypted PDF's title is not read (it would be gibberish)");
            string book = Path.Combine(dir, "book.pdf");
            File.WriteAllText(book, "%PDF-1.4\n3 0 obj << /Title (Introduction) /Parent 2 0 R >> endobj\n7 0 obj << /Title (Annual report 2026) /Author (Sam) >> endobj\ntrailer << /Info 7 0 R >>\n%%EOF", Encoding.ASCII);
            Check(FileOps.PdfTitle(book) == "Annual report 2026", "review 6: the document's own title is used, not a bookmark's");
            string junk = Path.Combine(dir, "junk.pdf");
            File.WriteAllText(junk, "%PDF-1.4\n7 0 obj << /Title (ab\\001\\002cd) >> endobj\ntrailer << /Info 7 0 R >>\n%%EOF", Encoding.ASCII);
            Check(FileOps.PdfTitle(junk) == null, "review 6: a title with control characters is not used");
            Check(FileOps.InvoiceName("Please pay the bill by 3 October 2026") == null, "review 6: the word \"bill\" alone does not make an invoice");
        }

        /// <summary>Review 6, #7: a bare commit id or GUID on the clipboard is not taken for a published checksum.</summary>
        static void Checksums(string dir)
        {
            string file = Make.Text(dir, "tool.zip", "zip");
            Check(Protect.ExpectedChecksum(file, "3f786850e387550fdab836ed7e6dc881de23001b") == null, "review 6: a bare 40-digit code (a commit id) is ignored");
            Check(Protect.ExpectedChecksum(file, Guid.NewGuid().ToString("N")) == null, "review 6: a GUID is not taken for an MD5");
            Check(Protect.ExpectedChecksum(file, "SHA1: 3f786850e387550fdab836ed7e6dc881de23001b") == "3f786850e387550fdab836ed7e6dc881de23001b", "review 6: a named SHA-1 is still used");
        }

        /// <summary>Review 6, #8: files that cannot fit in one request are refused before anything is encoded or sent.</summary>
        static void AiSizes(string dir)
        {
            Settings.Current = new Settings { AiEnabled = true };
            AiClient.SaveKey("sk-ant-test-0123456789abcdefXYZ");
            string old = AiClient.Endpoint;
            AiClient.Endpoint = "http://127.0.0.1:9/never";
            try
            {
                Func<string, long, string> big = delegate (string name, long size)
                {
                    string p = Path.Combine(dir, name);
                    using (var fs = new FileStream(p, FileMode.Create)) { fs.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n"), 0, 9); fs.SetLength(size); }
                    return p;
                };
                string why = null;
                try { AiClient.Ask(new[] { big("huge.pdf", 25L * 1024 * 1024) }, "x"); } catch (InvalidOperationException ex) { why = ex.Message; }
                Check(why != null && why.Contains("22 MB"), "review 6: a PDF too big for one request is refused up front");
                why = null;
                try { AiClient.Ask(new[] { big("a.pdf", 14L * 1024 * 1024), big("b.pdf", 14L * 1024 * 1024) }, "x"); } catch (InvalidOperationException ex) { why = ex.Message; }
                Check(why != null && why.Contains("too large to send at once"), "review 6: so are files that are too big together");
            }
            finally { AiClient.Endpoint = old; AiClient.ForgetKey(); Settings.Current = new Settings(); }
        }

        /// <summary>Review 6, #9: a script that leaves a program running does not keep its action waiting.</summary>
        static void ScriptPipes(string dir)
        {
            Directory.CreateDirectory(Scripts.Folder);
            File.WriteAllText(Path.Combine(Scripts.Folder, "starter.cmd"), ":: DragFlow: name=Starter; types=*; all\r\n@start \"\" /b ping -n 25 127.0.0.1\r\n@echo started\r\n");
            Scripts.Approve(Path.Combine(Scripts.Folder, "starter.cmd"));
            var watch = Stopwatch.StartNew();
            string result = Scripts.Run("Starter", new[] { Make.Text(dir, "x.txt") });
            Check(watch.ElapsedMilliseconds < 12000 && result.Contains("started"), "review 6: a script that starts a background program finishes promptly (" + watch.ElapsedMilliseconds + " ms)");
            Directory.Delete(Scripts.Folder, true);
        }

        /// <summary>Review 6, #6: the preview never covers a spot where a drop might be meant for something underneath.</summary>
        static void SeeThrough(string dir)
        {
            // the details line steps aside when the pointer comes to it
            var ring = new RingOverlay();
            try
            {
                List<ActionItem> menu = ActionCatalog.For(new[] { Make.Png(dir, "p.png") });
                ring.Show(-3000, -3000, menu, 1.0, Clock.Now, "1 file\n2 KB", null);
                ring.SetPreview(null, "300 × 200");
                ring.Tick(Clock.Now + 400, -3000, -3000, -1, false);
                Check(!ring.DetailsHidden, "review 6: (the details line shows)");
                ring.Tick(Clock.Now + 450, -3000, -3000 + 120, -1, false);
                ring.Tick(Clock.Now + 500, -3000, -3000 + 120, -1, false);
                Check(ring.DetailsHidden, "review 6: the details line disappears under the pointer, so a drop there goes through");
            }
            finally { ring.Close(); }

            // no thumbnail in the middle when the dial had to move away from the dragged item (near a screen edge)
            var ov = new FakeOverlay();
            var desk = new FakeDesktop { X = 100, Y = 500 };
            var c = new DragController(ov, desk) { GetSettings = delegate { return new Settings(); }, GetLearning = delegate { return new Learning(); } };
            var s = new DragSession { OriginX = 100, OriginY = 500, FileState = 1, Menu = ActionCatalog.For((string[])null) };
            c.OnStarted(s, 0);
            long t = 0;
            for (int i = 0; i < 20; i++) { t += 15; desk.X++; c.Tick(t); }
            s.Thumb = new Bitmap(10, 10); s.Details = "10 × 10";
            t += 15; c.Tick(t);
            Check(ov.Visible && ov.Thumb == null && ov.Details == "10 × 10", "review 6: at a screen edge the dial shows the details but no thumbnail (" + ov.Visible + "/" + (ov.Thumb != null) + "/" + ov.Details + "/" + c.Engine.DismissReason + ")");
        }
    }
}
