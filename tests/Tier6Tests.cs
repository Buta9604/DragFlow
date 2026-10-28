using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>Tier 6: smart names, similar photos, protection, the QR code, to phone, scripts, scheduled rules, recording recipes.</summary>
    static class Tier6Tests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            SmartNames(Make.Dir(root, "names"));
            Similar(Make.Dir(root, "similar"));
            Encryption(Make.Dir(root, "protect"));
            Checksums(Make.Dir(root, "checksums"));
            Qr(Make.Dir(root, "qr"));
            Phone(Make.Dir(root, "phone"));
            ScriptActions(Make.Dir(root, "scripts"));
            Schedules(Make.Dir(root, "schedules"));
            Recorder(Make.Dir(root, "recorder"));
        }

        /// <summary>A JPEG with an EXIF "date taken" (GDI+ has no public way to make a property item, so one is made raw).</summary>
        internal static string PhotoTaken(string dir, string name, DateTime taken, Color color, int w = 120, int h = 90)
        {
            string path = Path.Combine(dir, name);
            using (var bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(color);
                    g.FillEllipse(Brushes.White, w / 4, h / 4, w / 2, h / 2);
                    g.FillRectangle(Brushes.Black, 0, 0, w / 5, h);
                }
                var item = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                byte[] value = Encoding.ASCII.GetBytes(taken.ToString("yyyy:MM:dd HH:mm:ss") + "\0");
                item.Id = 0x9003; item.Type = 2; item.Len = value.Length; item.Value = value;
                bmp.SetPropertyItem(item);
                bmp.Save(path, ImageFormat.Jpeg);
            }
            return path;
        }

        static void SmartNames(string dir)
        {
            string photo = PhotoTaken(dir, "IMG_0042.jpg", new DateTime(2024, 7, 14, 15, 30, 0), Color.SteelBlue);
            Check(FileOps.SmartNameFor(photo) == "2024-07-14 15.30.00.jpg", "smart name: a photo is named by the date it was taken");
            string pdf = Path.Combine(dir, "scan001.pdf");
            File.WriteAllText(pdf, "%PDF-1.4\n1 0 obj << /Title (Quarterly report \\050draft\\051) /Author (Sam) >> endobj\ntrailer << /Info 1 0 R >>\n%%EOF", Encoding.ASCII);
            Check(FileOps.PdfTitle(pdf) == "Quarterly report (draft)", "smart name: the title inside a PDF is read, escapes included");
            Check(FileOps.PdfHexString("FEFF00480069") == "Hi" && FileOps.PdfString("\\303\\251") == "Ã©", "smart name: PDF hex and octal strings are decoded");
            Check(FileOps.CleanTitle("Microsoft Word - report.docx") == "report" && FileOps.CleanTitle("Untitled") == null && FileOps.CleanTitle("12345") == null,
                "smart name: titles programs fill in by themselves are ignored");
            string docx = Path.Combine(dir, "Document1.docx");
            using (ZipArchive z = ZipFile.Open(docx, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("docProps/core.xml").Open()))
                w.Write("<cp:coreProperties xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Budget 2027 &amp; plans</dc:title></cp:coreProperties>");
            Check(FileOps.OfficeTitle(docx) == "Budget 2027 & plans", "smart name: the title of an Office file is read");
            Check(FileOps.InvoiceName("ACME Ltd\nINVOICE No. 2024-0184\nDate: 03.10.2026\nTotal 120.00") == "2026-10-03 Invoice 2024-0184",
                "smart name: a scanned invoice gives its date and number");
            Check(FileOps.InvoiceName("Thank you!\nRECEIPT\nOctober 3, 2026\nCoffee 3.50") == "2026-10-03 Receipt", "smart name: a receipt gives its date");
            Check(FileOps.InvoiceName("Meeting notes from 3 October 2026") == null, "smart name: a document that is not an invoice is left alone");
            Check(FileOps.FindDate("due 2026-02-30 or 2026-03-01") == new DateTime(2026, 3, 1) && FileOps.FindDate("12 März 2026") == new DateTime(2026, 3, 12),
                "smart name: impossible dates are skipped; German month names work");
            Check(FileOps.SafeFileName("a/b:c*d?") == "a b c d" && FileOps.SafeFileName("con") == "_con", "smart name: names are made safe for Windows");

            UndoLog.Clear();
            int untouched;
            int n = FileOps.SmartRename(new[] { photo, pdf, docx, Make.Text(dir, "notes.txt") }, out untouched);
            Check(n == 3 && untouched == 1 && File.Exists(Path.Combine(dir, "2024-07-14 15.30.00.jpg")) && File.Exists(Path.Combine(dir, "Quarterly report (draft).pdf"))
                  && File.Exists(Path.Combine(dir, "Budget 2027 & plans.docx")), "smart name: files are renamed by their content");
            Check(UndoLog.UndoLast() == 3 && File.Exists(photo) && File.Exists(pdf), "smart name: and it can be undone");

            string trip = Make.Dir(dir, "trip");
            string late = PhotoTaken(trip, "a.jpg", new DateTime(2025, 1, 3), Color.Red), early = PhotoTaken(trip, "b.jpg", new DateTime(2025, 1, 1), Color.Green),
                   middle = PhotoTaken(trip, "c.jpg", new DateTime(2025, 1, 2), Color.Blue);
            Check(FileOps.NumberByDate(new[] { late, early, middle }, "Trip") == 3 && File.Exists(Path.Combine(trip, "Trip 001.jpg"))
                  && FileOps.PhotoDate(Path.Combine(trip, "Trip 001.jpg")) == new DateTime(2025, 1, 1) && FileOps.PhotoDate(Path.Combine(trip, "Trip 003.jpg")) == new DateTime(2025, 1, 3),
                "number by date: photos are numbered in the order they were taken");
        }

        static void Similar(string dir)
        {
            string a = PhotoTaken(dir, "beach.jpg", DateTime.Now, Color.Orange, 400, 300);
            string b = Path.Combine(dir, "beach small.jpg");
            using (Image src = Image.FromFile(a))
            using (var small = new Bitmap(src, 200, 150)) small.Save(b, ImageFormat.Jpeg);
            string c = Make.Png(dir, "other.png", 300, 300);
            using (var bmp = new Bitmap(300, 300))
            {
                using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black, 150, 0, 150, 300); g.FillEllipse(Brushes.Red, 0, 150, 100, 100); }
                bmp.Save(c, ImageFormat.Png);
            }
            Check(FileOps.Distance(FileOps.LookHash(a), FileOps.LookHash(b)) <= 6 && FileOps.Distance(FileOps.LookHash(a), FileOps.LookHash(c)) > 10,
                "similar photos: a smaller copy looks the same, a different picture does not");
            UndoLog.Clear();
            string folder;
            int n = FileOps.MoveSimilar(new[] { dir }, out folder);
            Check(n == 1 && File.Exists(a) && File.Exists(c) && File.Exists(Path.Combine(folder, "beach small.jpg")), "similar photos: the biggest copy stays, the look-alike is moved aside");
            Check(UndoLog.UndoLast() >= 1 && File.Exists(b), "similar photos: undoable");
        }

        static void Encryption(string dir)
        {
            int keep = Protect.Iterations;
            Protect.Iterations = 2000; // quick for the tests; real files use many more
            try
            {
                string secret = Make.Text(dir, "secret.txt", "the plans for Tuesday — ünïcödé");
                byte[] original = File.ReadAllBytes(secret);
                string enc = Protect.Encrypt(secret, "correct horse");
                Check(enc.EndsWith("secret.txt.dfenc") && Protect.IsEncrypted(enc) && File.ReadAllBytes(secret).SequenceEqual(original), "encrypt: an encrypted copy is made, the original is unchanged");
                Check(!Encoding.UTF8.GetString(File.ReadAllBytes(enc)).Contains("plans") && !Encoding.UTF8.GetString(File.ReadAllBytes(enc)).Contains("secret.txt"), "encrypt: neither the contents nor the name can be read");
                string wrong = null;
                try { Protect.Decrypt(enc, "wrong horse"); } catch (InvalidOperationException ex) { wrong = ex.Message; }
                Check(wrong != null && wrong.StartsWith("Wrong password") && Directory.GetFiles(dir).Length == 2, "decrypt: a wrong password is refused and nothing is written");
                string back = Protect.Decrypt(enc, "correct horse");
                Check(Path.GetFileName(back) == "secret (1).txt" && File.ReadAllBytes(back).SequenceEqual(original), "decrypt: the right password gives the file back under its own name");
                byte[] bytes = File.ReadAllBytes(enc);
                bytes[bytes.Length / 2] ^= 1;
                File.WriteAllBytes(enc, bytes);
                string tampered = null;
                try { Protect.Decrypt(enc, "correct horse"); } catch (InvalidOperationException ex) { tampered = ex.Message; }
                Check(tampered != null, "decrypt: a changed file is detected");
                string folder = Make.Dir(dir, "Taxes");
                Make.Text(folder, "2026.txt", "numbers");
                List<string> made = Protect.EncryptAll(new[] { folder }, "correct horse");
                Check(made.Count == 1 && made[0].EndsWith("Taxes.zip.dfenc"), "encrypt: a folder is zipped and encrypted");
                string zip = Protect.Decrypt(made[0], "correct horse");
                using (ZipArchive z = ZipFile.OpenRead(zip)) Check(z.Entries.Any(e => e.FullName.EndsWith("2026.txt")), "decrypt: the folder comes back as a zip");
                bool shortRefused = false;
                try { Protect.EncryptAll(new[] { secret }, "short"); } catch (ArgumentException) { shortRefused = true; }
                Check(shortRefused, "encrypt: passwords shorter than 8 characters are refused");
                Check(ActionCatalog.For(new[] { enc })[0].Id == "decrypt", "decrypt: encrypted files lead with Decrypt on the dial");
            }
            finally { Protect.Iterations = keep; }
        }

        static void Checksums(string dir)
        {
            string file = Make.Text(dir, "setup.exe", "pretend installer");
            string sha = Protect.Hash(file, "SHA-256"), md5 = Protect.Hash(file, "MD5");
            Check(Protect.ExpectedChecksum(file, "SHA256: " + sha.ToUpperInvariant()) == sha, "checksum: one copied from a web page is found");
            Check(Protect.ExpectedChecksum(file, sha + "\n" + md5 + "\n" + Protect.Hash(file, "SHA-1")) == null, "checksum: several different ones on the clipboard are not guessed at");
            File.WriteAllText(Path.Combine(dir, "SHA256SUMS"), "0000000000000000000000000000000000000000000000000000000000000000  other.zip\n" + sha + " *setup.exe\n");
            Check(Protect.ExpectedChecksum(file, "hello") == sha, "checksum: a SHA256SUMS file next to the download is read");
            bool ok;
            Check(Protect.Verify(file, md5, out ok).StartsWith("Matches the MD5") && ok, "checksum: a match is confirmed (MD5 too)");
            Check(Protect.Verify(file, new string('a', 64), out ok).StartsWith("Does NOT match") && !ok, "checksum: a mismatch is reported plainly");
            Check(ActionCatalog.For(new[] { file }).Last().Children.Any(k => k.Id == "sha256" && k.Key == "Checksum"), "checksum: offered as Checksum under More");
        }

        /// <summary>The QR code really scans: decoded with OpenCV when Python has it.</summary>
        static void Qr(string dir)
        {
            QrCode small = QrCode.Encode("http://192.168.1.23:52345/0123456789abcdef0123456789abcdef/");
            Check(small.Size == small.Version * 4 + 17 && small[0, 0] && small[6, 6] && !small[7, 7] && small[small.Size - 1, 0], "QR: the size and finder patterns are right");
            string python = FileOps.FindOnPath("python.exe");
            if (python == null || python.Contains("WindowsApps")) { Console.WriteLine("  SKIP  QR decoding (no Python with OpenCV)"); return; }
            string[] texts = { "http://192.168.1.23:52345/0123456789abcdef0123456789abcdef/", "HELLO", new string('x', 150) + "/" + "ünï", "http://10.0.0.5:61234/" + new string('a', 200) };
            int decoded = 0, tried = 0;
            foreach (string t in texts)
            {
                QrCode q = QrCode.Encode(t);
                string png = Path.Combine(dir, "qr" + tried + ".png");
                using (Bitmap b = q.ToBitmap(6)) b.Save(png, ImageFormat.Png);
                tried++;
                var psi = new ProcessStartInfo(python, "-I -c \"import cv2,sys; v=cv2.QRCodeDetector().detectAndDecode(cv2.imread(sys.argv[1]))[0]; sys.stdout.buffer.write(v.encode('utf-8'))\" \"" + png + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                try
                {
                    using (Process p = Process.Start(psi))
                    {
                        string got = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(20000);
                        if (p.ExitCode != 0) { Console.WriteLine("  SKIP  QR decoding (OpenCV not available)"); return; }
                        if (got == t) decoded++;
                        else Console.WriteLine("    QR v" + q.Version + " decoded as \"" + got + "\"");
                    }
                }
                catch (Exception ex) { Console.WriteLine("  SKIP  QR decoding (" + ex.Message + ")"); return; }
            }
            Check(decoded == tried, "QR: codes of versions " + string.Join(", ", texts.Select(t => QrCode.Encode(t).Version.ToString()).ToArray()) + " scan back to the same text");
        }

        static string Get(string url, out int status)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 10000;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) { status = (int)resp.StatusCode; return r.ReadToEnd(); }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                status = resp == null ? -1 : (int)resp.StatusCode;
                return null;
            }
        }

        static void Phone(string dir)
        {
            PhoneTransfer.LoopbackOnly = true;
            string a = Make.Text(dir, "notes ü.txt", "hello phone"), folder = Make.Dir(dir, "Album");
            Make.Text(folder, "1.txt", "one");
            var t = new PhoneTransfer(new[] { a, folder });
            try
            {
                int status;
                string page = Get(t.Url, out status);
                Check(status == 200 && WebUtility.HtmlDecode(page).Contains("notes ü.txt") && page.Contains("Album.zip"), "to phone: the page lists the files (folders as zips)");
                string body = Get(t.Url + "f/0", out status);
                Check(status == 200 && body == "hello phone" && t.Downloads == 1, "to phone: a file downloads and is counted");
                Get(t.Url.Replace(t.Url.Split('/')[3], "0123456789abcdef0123456789abcdef"), out status);
                Check(status == 404, "to phone: any other address is refused");
                Get(t.Url + "f/9", out status);
                Check(status == 404, "to phone: only the offered files can be fetched");
                Check(t.Url.Split('/')[3].Length == 32, "to phone: the link carries 128 random bits");
                t.Stop();
                Get(t.Url, out status);
                Check(status != 200, "to phone: after Stop the link no longer works");
            }
            finally { t.Stop(); PhoneTransfer.LoopbackOnly = false; }
        }

        static void ScriptActions(string dir)
        {
            Directory.CreateDirectory(Scripts.Folder);
            File.WriteAllText(Path.Combine(Scripts.Folder, "count.ps1"), "# DragFlow: name=Count words; types=.txt,folder; all\r\n\"$($args.Count) items\"\r\n", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(Scripts.Folder, "fail.cmd"), ":: DragFlow: name=Always fails; types=*; each\r\n@echo broken 1>&2\r\n@exit /b 3\r\n");
            List<ScriptAction> all = Scripts.Load();
            string notYet = null;
            try { Scripts.Run("Count words", new[] { Make.Text(dir, "z.txt") }); } catch (InvalidOperationException ex) { notYet = ex.Message; }
            Check(notYet != null && notYet.Contains("approve"), "scripts: a new script does not run until it is approved");
            foreach (ScriptAction s in all) Scripts.Approve(s.Path);
            ScriptAction count = all.FirstOrDefault(s => s.Name == "Count words");
            string a = Make.Text(dir, "a.txt"), b = Make.Text(dir, "b.txt"), png = Make.Png(dir, "c.png");
            Check(count != null && !count.Each && count.Matches(new[] { a, dir }) && !count.Matches(new[] { png }), "scripts: the header names the action and its file types");
            Check(Scripts.Run("Count words", new[] { a, b }) == "2 items", "scripts: the items arrive as arguments; the last line printed is the result");
            string failure = null;
            try { Scripts.Run("Always fails", new[] { a }); } catch (InvalidOperationException ex) { failure = ex.Message; }
            Check(failure != null && failure.Contains("exit code 3") && failure.Contains("broken"), "scripts: a failing script reports its exit code and error");
            List<ActionItem> menu = ActionCatalog.For(new[] { a });
            Check(menu.Any(i => i.Children.Any(k => k.Id == "script:Count words")) && !ActionCatalog.For(new[] { png }).Any(i => i.Children.Any(k => k.Id == "script:Count words")),
                "scripts: they appear on the dial for matching files only");
            Directory.Delete(Scripts.Folder, true);
            Scripts.EnsureFolder();
            Check(Scripts.Load().Any(s => s.Name == "Show file info"), "scripts: the folder starts with a working example");
            Directory.Delete(Scripts.Folder, true);
        }

        static void Schedules(string dir)
        {
            RuleSchedule daily = RuleSchedule.Parse("daily 18:00"), weekly = RuleSchedule.Parse("Weekly Sunday at 9");
            Check(daily != null && daily.Day == null && weekly != null && weekly.Day == DayOfWeek.Sunday && weekly.Time == TimeSpan.FromHours(9)
                  && RuleSchedule.Parse("weekly 18:00") == null && RuleSchedule.Parse("daily 25:00") == null && RuleSchedule.Parse("sometimes") == null,
                "schedules: daily and weekly times are understood, nonsense is not");
            var mon = new DateTime(2026, 10, 26, 10, 0, 0); // a Monday
            Check(daily.Previous(mon) == new DateTime(2026, 10, 25, 18, 0, 0) && weekly.Previous(mon) == new DateTime(2026, 10, 25, 9, 0, 0), "schedules: the last scheduled moment is found");

            string watch = Make.Dir(dir, "Downloads"), target = Make.Dir(dir, "Sorted");
            string old = Make.Text(watch, "report.pdf");
            var rule = new Rule { Name = "Evening tidy", Folder = watch, Pattern = "*.pdf", Action = "moveto", Target = target, Schedule = "daily 18:00" };
            Check(rule.Problem() == null && rule.Scheduled, "schedules: a scheduled rule is valid");
            Check(new Rule { Folder = watch, Action = "moveto", Target = target, Schedule = "tea time" }.Problem() != null, "schedules: a wrong schedule is explained");
            DateTime now = mon;
            var engine = new RuleEngine { TicksOnItsOwn = false, Now = delegate { return now; } };
            var runs = new List<string[]>();
            engine.Run = delegate (Rule r, string[] files) { runs.Add(files); };
            engine.Apply(new[] { rule }, true);
            engine.Notice(rule, old, 0);
            engine.Process(1000);
            Check(runs.Count == 0, "schedules: a new scheduled rule waits for its time (and ignores arriving files)");
            now = new DateTime(2026, 10, 26, 18, 1, 0);
            engine.Process(2000);
            Check(runs.Count == 1 && runs[0][0] == old, "schedules: at its time the rule handles what is in the folder");
            engine.Process(3000);
            Check(runs.Count == 1, "schedules: once per scheduled time");
            string next = Make.Text(watch, "invoice.pdf", "new");
            now = new DateTime(2026, 10, 28, 9, 0, 0); // the PC was off on Tuesday evening
            engine.Process(4000);
            Check(runs.Count == 2 && runs[1].Length == 1 && runs[1][0] == next, "schedules: a missed time is made up, and only new files are handled");
            engine.Dispose();

            RuleStore.Save(new List<Rule> { rule, new Rule { Name = "web", Folder = watch, Pattern = "*", Action = "recipe:Web ready", WebOnly = true } });
            List<Rule> back = RuleStore.Load();
            Check(back[0].Schedule == "daily 18:00" && back[1].WebOnly && back[1].Action == "recipe:Web ready", "schedules: schedule, downloads-only and recipe are saved");
            Check(back[1].Problem() != null && RuleActions.Label("recipe:Web ready") == "Recipe: Web ready", "rules: a rule naming a missing recipe is explained");

            string downloaded = Make.Text(dir, "from web.pdf");
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"Set-Content -LiteralPath '" + downloaded + "' -Stream Zone.Identifier -Value \\\"[ZoneTransfer]`r`nZoneId=3\\\"\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (Process p = Process.Start(psi)) p.WaitForExit(30000);
            Check(Zone.FromInternet(downloaded) && !Zone.FromInternet(old), "rules: files downloaded from the internet are recognised");
            var webRule = new Rule { Folder = dir, WebOnly = true };
            Check(webRule.Qualifies(downloaded) && !webRule.Qualifies(Make.Text(dir, "made here.pdf")), "rules: \"downloads only\" skips files made on this PC");
        }

        static void Recorder(string dir)
        {
            RecipeRecorder.Clear();
            string a = Make.Png(dir, "a.png"), half = Path.Combine(dir, "a_50.png"), clean = Path.Combine(dir, "a_50_clean.png"), zip = Path.Combine(dir, "a.zip");
            RecipeRecorder.Note("resize:50", "Resize 50%", new[] { a }, new[] { half });
            RecipeRecorder.Note("strip", "Strip EXIF", new[] { half }, new[] { clean });
            RecipeRecorder.Note("zip", "Zip", new[] { clean }, new[] { zip });
            List<RecipeRecorder.Step> chain = RecipeRecorder.Suggest(DateTime.Now);
            Check(chain.Count == 3 && chain[0].Id == "resize:50" && chain[2].Id == "zip" && RecipeRecorder.TypesFor(chain[0]) == "image", "record: the chain of actions just done is found");
            RecipeRecorder.Note("encrypt:#abc", "Encrypt", new[] { zip }, new[] { zip + ".dfenc" });
            Check(RecipeRecorder.Suggest(DateTime.Now).Count == 3, "record: actions with passwords are never recorded");
            RecipeRecorder.Note("gray", "Grayscale", new[] { Make.Png(dir, "other.png") }, new[] { Path.Combine(dir, "other_gray.png") });
            Check(RecipeRecorder.Suggest(DateTime.Now).Count == 1, "record: an unrelated action starts a new chain");
            Check(RecipeSteps.Label("gray") == "Grayscale", "record: recorded steps keep readable names");
            Check(RecipeRecorder.Suggest(DateTime.Now.AddHours(2)).Count == 0, "record: nothing is offered after an hour");
        }
    }
}
