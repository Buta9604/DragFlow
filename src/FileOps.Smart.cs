using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragFlow
{
    /// <summary>Names from what is inside a file (no AI, nothing leaves the PC), photos numbered by date, similar photos.</summary>
    public static partial class FileOps
    {
        // ---------- smart names ----------

        /// <summary>
        /// A better name for a file from what is in it, or null: photos by the date they were taken, PDFs and Office files by
        /// their title, scanned invoices and receipts by the date and number found on them. The extension is kept.
        /// </summary>
        public static string SmartNameFor(string file)
        {
            FileKind k = Classify(file);
            string ext = Path.GetExtension(file);
            string name = null;
            if (k == FileKind.Image)
            {
                DateTime taken;
                if (ExifDate(file, out taken)) name = taken.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
                else if (WinRt.OcrAvailable()) name = InvoiceName(SafeOcr(delegate { return WinRt.OcrImages(new[] { file }); }));
            }
            else if (k == FileKind.Pdf)
            {
                name = CleanTitle(PdfTitle(file));
                if (name == null && WinRt.OcrAvailable()) name = InvoiceName(SafeOcr(delegate { return WinRt.OcrPdf(file, 1); }));
            }
            else if (k == FileKind.Word) name = CleanTitle(OfficeTitle(file));
            if (name == null) return null;
            name = SafeFileName(name);
            if (name.Length == 0 || string.Equals(name, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) return null;
            return name + ext;
        }

        static string SafeOcr(Func<string> f)
        {
            try { return f(); } catch (Exception ex) { Log.Write("smart name: OCR failed: " + ex.Message); return null; }
        }

        /// <summary>Rename each file by its content. Files with nothing to go by are left alone. Returns (renamed, untouched).</summary>
        public static int SmartRename(IList<string> paths, out int untouched)
        {
            var files = paths.Where(File.Exists).ToList();
            var moves = new List<KeyValuePair<string, string>>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Job.SetTotal(files.Count);
            foreach (string f in files)
            {
                Job.Step(Path.GetFileName(f));
                Job.Advance();
                string better = SmartNameFor(f);
                if (better == null) continue;
                string dir = ParentOf(f), stem = Path.GetFileNameWithoutExtension(better), ext = Path.GetExtension(better);
                string dest = Path.Combine(dir, better);
                for (int i = 1; ((File.Exists(dest) || Directory.Exists(dest)) && !string.Equals(dest, f, StringComparison.OrdinalIgnoreCase)) || taken.Contains(dest); i++)
                    dest = Path.Combine(dir, stem + " (" + i + ")" + ext);
                taken.Add(dest);
                moves.Add(new KeyValuePair<string, string>(f, dest));
            }
            untouched = files.Count - moves.Count;
            return moves.Count == 0 ? 0 : ApplyMoves(moves);
        }

        /// <summary>The EXIF "date taken" only (no fallback to the file date).</summary>
        public static bool ExifDate(string path, out DateTime taken)
        {
            taken = DateTime.MinValue;
            try
            {
                if (NeedsWic(path)) return false;
                using (var fs = File.OpenRead(path))
                using (Image img = Image.FromStream(fs, false, false))
                {
                    foreach (int id in new[] { 0x9003, 0x9004, 0x0132 })
                    {
                        if (!img.PropertyIdList.Contains(id)) continue;
                        string s = Encoding.ASCII.GetString(img.GetPropertyItem(id).Value).TrimEnd('\0').Trim();
                        if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out taken) && taken.Year > 1980) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>The title stored in a PDF (document info or XMP), or null.</summary>
        public static string PdfTitle(string pdf)
        {
            try
            {
                var fi = new FileInfo(pdf);
                if (fi.Length > 50L * 1024 * 1024) return null;
                string raw = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(pdf)); // Latin-1 keeps every byte
                // An encrypted PDF stores its title encrypted too: reading it would give gibberish.
                if (Regex.IsMatch(raw, @"/Encrypt\s+\d+\s+\d+\s+R|/Encrypt\s*<<")) return null;
                Match m = Regex.Match(raw, @"<dc:title>\s*<rdf:Alt>\s*<rdf:li[^>]*>([^<]{1,300})</rdf:li>", RegexOptions.Singleline);
                if (m.Success) return Readable(Utf8FromLatin1(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
                // The document's own title lives in the dictionary the trailer names as /Info (a bookmark may also say /Title).
                MatchCollection infos = Regex.Matches(raw, @"/Info\s+(\d+)\s+(\d+)\s+R");
                if (infos.Count == 0) return null;
                Match info = infos[infos.Count - 1];
                Match obj = Regex.Match(raw, @"(?<!\d)" + info.Groups[1].Value + @"\s+" + info.Groups[2].Value + @"\s+obj(.{0,4000}?)endobj", RegexOptions.Singleline);
                if (!obj.Success) return null;
                string dict = obj.Groups[1].Value;
                m = Regex.Match(dict, @"/Title\s*\(((?:\\.|[^\\)]){1,600})\)", RegexOptions.Singleline);
                if (m.Success) return Readable(PdfString(m.Groups[1].Value));
                m = Regex.Match(dict, @"/Title\s*<([0-9A-Fa-f\s]{2,1200})>");
                if (m.Success) return Readable(PdfHexString(m.Groups[1].Value));
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The text when it looks like words, else null (control characters or mostly symbols mean it is not a title).</summary>
        static string Readable(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Any(c => (c < 0x20 && c != '\t') || (c >= 0x7F && c <= 0x9F) || c == '\uFFFD')) return null;
            int good = s.Count(c => char.IsLetterOrDigit(c) || c == ' ' || ".,-_()&'!?:;/".IndexOf(c) >= 0);
            return good >= s.Length * 0.85 ? s : null;
        }

        static string Utf8FromLatin1(string s)
        {
            byte[] b = Encoding.GetEncoding(28591).GetBytes(s);
            return new UTF8Encoding(false, false).GetString(b);
        }

        /// <summary>A PDF literal string: backslash escapes, and UTF-16 when it starts with FE FF.</summary>
        internal static string PdfString(string s)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { bytes.Add((byte)c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': bytes.Add(10); break;
                    case 'r': bytes.Add(13); break;
                    case 't': bytes.Add(9); break;
                    case 'b': bytes.Add(8); break;
                    case 'f': bytes.Add(12); break;
                    case '\r': case '\n': break;
                    default:
                        if (n >= '0' && n <= '7')
                        {
                            int v = n - '0', digits = 1;
                            while (digits < 3 && i + 1 < s.Length && s[i + 1] >= '0' && s[i + 1] <= '7') { v = v * 8 + (s[++i] - '0'); digits++; }
                            bytes.Add((byte)v);
                        }
                        else bytes.Add((byte)n);
                        break;
                }
            }
            return DecodePdfBytes(bytes.ToArray());
        }

        internal static string PdfHexString(string hex)
        {
            hex = Regex.Replace(hex, @"\s", "");
            if (hex.Length % 2 == 1) hex += "0";
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber);
            return DecodePdfBytes(b);
        }

        static string DecodePdfBytes(byte[] b)
        {
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            return Encoding.GetEncoding(28591).GetString(b);
        }

        /// <summary>The title in a .docx, .xlsx or .pptx (docProps/core.xml), or null.</summary>
        public static string OfficeTitle(string file)
        {
            string e = Path.GetExtension(file).ToLowerInvariant();
            if (e != ".docx" && e != ".xlsx" && e != ".pptx" && e != ".xlsm" && e != ".docm") return null;
            try
            {
                using (ZipArchive z = ZipFile.OpenRead(file))
                {
                    ZipArchiveEntry core = z.GetEntry("docProps/core.xml");
                    if (core == null) return null;
                    string xml;
                    using (var r = new StreamReader(core.Open(), Encoding.UTF8)) xml = r.ReadToEnd();
                    Match m = Regex.Match(xml, @"<dc:title>([^<]{1,300})</dc:title>");
                    return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Titles programs fill in by themselves ("Microsoft Word - report.docx", "Untitled") are not real titles.</summary>
        internal static string CleanTitle(string title)
        {
            if (title == null) return null;
            string t = Regex.Replace(title, @"\s+", " ").Trim();
            t = Regex.Replace(t, @"^Microsoft (Word|Excel|PowerPoint) - ", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\.(docx?|xlsx?|pptx?|pdf|txt|rtf|odt)$", "", RegexOptions.IgnoreCase).Trim();
            if (t.Length < 3 || Regex.IsMatch(t, @"^(untitled|title|document\d*|doc\d*|unbenannt|sans titre|presentation\d*|book\d*)$", RegexOptions.IgnoreCase)) return null;
            if (!t.Any(char.IsLetter)) return null;
            return t.Length > 80 ? t.Substring(0, 80).Trim() : t;
        }

        static readonly string[] InvoiceWords = { "invoice", "receipt", "rechnung", "quittung", "factura", "facture", "fattura", "faktura" };

        /// <summary>"2026-10-03 Invoice 184" from the text of a scanned invoice or receipt, or null if it is not one.</summary>
        public static string InvoiceName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string lower = text.ToLowerInvariant();
            string word = InvoiceWords.FirstOrDefault(w => Regex.IsMatch(lower, @"\b" + w + @"\b"));
            if (word == null) return null;
            string kind = word == "receipt" || word == "quittung" ? "Receipt" : "Invoice";
            string number = null;
            Match n = Regex.Match(text, @"\b(?:invoice|rechnung|factura|facture|fattura|faktura|receipt)\s*(?:no\.?|number|nr\.?|num\.?|#|n°|nº)?\s*[:#.]?\s*([A-Z]{0,4}[-/]?\d[A-Z0-9\-/]{0,18})", RegexOptions.IgnoreCase);
            if (n.Success) number = n.Groups[1].Value.Trim('-', '/');
            DateTime? date = FindDate(text);
            if (number == null && date == null) return null;
            return ((date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " : "") + kind + (number != null ? " " + number : "")).Trim();
        }

        static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        /// <summary>The first plausible date in a text: 2026-10-03, 03.10.2026, 10/03/2026 (in the PC's own order) or 3 October 2026.</summary>
        public static DateTime? FindDate(string text)
        {
            foreach (Match m in Regex.Matches(text, @"\b(20\d\d|19\d\d)-(\d{1,2})-(\d{1,2})\b"))
            {
                DateTime? d = MakeDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                if (d.HasValue) return d;
            }
            bool usual = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith("M", StringComparison.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, @"\b(\d{1,2})[./](\d{1,2})[./](20\d\d|19\d\d)\b"))
            {
                bool monthFirst = usual && !m.Value.Contains("."); // dotted dates are day first everywhere
                DateTime? d = monthFirst ? MakeDate(m.Groups[3].Value, m.Groups[1].Value, m.Groups[2].Value) : MakeDate(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value);
                if (!d.HasValue) d = monthFirst ? MakeDate(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value) : MakeDate(m.Groups[3].Value, m.Groups[1].Value, m.Groups[2].Value);
                if (d.HasValue) return d;
            }
            foreach (Match m in Regex.Matches(text, @"\b(\d{1,2})\.?\s+(\p{L}{3,9})\.?,?\s+(20\d\d|19\d\d)\b"))
            {
                int mo = MonthOf(m.Groups[2].Value);
                DateTime? d = mo > 0 ? MakeDate(m.Groups[3].Value, mo.ToString(), m.Groups[1].Value) : null;
                if (d.HasValue) return d;
            }
            foreach (Match m in Regex.Matches(text, @"\b(\p{L}{3,9})\.?\s+(\d{1,2}),?\s+(20\d\d|19\d\d)\b"))
            {
                int mo = MonthOf(m.Groups[1].Value);
                DateTime? d = mo > 0 ? MakeDate(m.Groups[3].Value, mo.ToString(), m.Groups[2].Value) : null;
                if (d.HasValue) return d;
            }
            return null;
        }

        static int MonthOf(string word)
        {
            string w = word.ToLowerInvariant();
            if (w.Length < 3) return 0;
            int i = Array.IndexOf(Months, w.Substring(0, 3));
            if (i < 0 && w.StartsWith("mär")) i = 2;
            if (i < 0 && w.StartsWith("okt")) i = 9;
            if (i < 0 && w.StartsWith("dez")) i = 11;
            return i + 1;
        }

        static DateTime? MakeDate(string y, string m, string d)
        {
            int yy, mm, dd;
            if (!int.TryParse(y, out yy) || !int.TryParse(m, out mm) || !int.TryParse(d, out dd)) return null;
            if (mm < 1 || mm > 12 || dd < 1 || dd > DateTime.DaysInMonth(yy, mm)) return null;
            return new DateTime(yy, mm, dd);
        }

        /// <summary>Characters Windows does not allow in names replaced, spaces tidied, at most 120 characters.</summary>
        public static string SafeFileName(string name)
        {
            string s = new string((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
            s = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.', ' ');
            if (s.Length > 120) s = s.Substring(0, 120).TrimEnd('.', ' ');
            if (Regex.IsMatch(s, @"^(con|prn|aux|nul|com\d|lpt\d)$", RegexOptions.IgnoreCase)) s = "_" + s;
            return s;
        }

        // ---------- photos numbered by date ----------

        /// <summary>"Holiday 001.jpg", "Holiday 002.jpg"… in the order the photos were taken. Undoable.</summary>
        public static int NumberByDate(IList<string> paths, string baseName)
        {
            baseName = SafeFileName(baseName);
            if (baseName.Length == 0) throw new ArgumentException("Type a name for the photos.");
            List<string> photos = ImagesIn(paths);
            if (photos.Count == 0) throw new InvalidOperationException("No pictures to number.");
            var dated = photos.Select(p => new KeyValuePair<string, DateTime>(p, PhotoDate(p))).OrderBy(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            int digits = Math.Max(3, dated.Count.ToString().Length);
            var moves = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < dated.Count; i++)
            {
                string f = dated[i].Key;
                moves.Add(new KeyValuePair<string, string>(f, Path.Combine(ParentOf(f), baseName + " " + (i + 1).ToString(new string('0', digits)) + Path.GetExtension(f).ToLowerInvariant())));
            }
            return ApplyMoves(moves);
        }

        // ---------- similar photos ----------

        /// <summary>A 64-bit fingerprint of how a picture looks (difference hash): close fingerprints mean the same scene.</summary>
        public static ulong LookHash(string image)
        {
            using (Image src = LoadImage(image))
            using (var small = new Bitmap(9, 8, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, 0, 0, 9, 8);
                }
                ulong h = 0;
                int bit = 0;
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++, bit++)
                    {
                        Color a = small.GetPixel(x, y), b = small.GetPixel(x + 1, y);
                        if (a.R * 299 + a.G * 587 + a.B * 114 > b.R * 299 + b.G * 587 + b.B * 114) h |= 1UL << bit;
                    }
                return h;
            }
        }

        public static int Distance(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int n = 0;
            while (x != 0) { x &= x - 1; n++; }
            return n;
        }

        /// <summary>Groups of pictures that look the same (each group lists the best one first: most pixels, then biggest file).</summary>
        public static List<List<string>> SimilarGroups(IList<string> images, int maxDistance)
        {
            var hashes = new List<KeyValuePair<string, ulong>>();
            Job.SetTotal(images.Count);
            foreach (string f in images)
            {
                Job.Step(Path.GetFileName(f));
                Job.Advance();
                try { hashes.Add(new KeyValuePair<string, ulong>(f, LookHash(f))); }
                catch (Exception ex) { Log.Write("similar photos: skipped a picture: " + ex.Message); }
            }
            // Best pictures first; each later one joins the group whose best picture it looks like. Comparing with the
            // group's best (not with any member) keeps a burst of slightly different shots from chaining into one group.
            var ordered = hashes.OrderByDescending(h => PixelCount(h.Key)).ThenByDescending(h => new FileInfo(h.Key).Length).ToList();
            var groups = new List<List<KeyValuePair<string, ulong>>>();
            foreach (var h in ordered)
            {
                List<KeyValuePair<string, ulong>> home = groups.FirstOrDefault(g => Distance(g[0].Value, h.Value) <= maxDistance);
                if (home != null) home.Add(h); else groups.Add(new List<KeyValuePair<string, ulong>> { h });
            }
            return groups.Where(g => g.Count > 1).Select(g => g.Select(h => h.Key).ToList()).ToList();
        }

        static long PixelCount(string image)
        {
            try
            {
                using (var fs = File.OpenRead(image))
                using (Image img = Image.FromStream(fs, false, false)) return (long)img.Width * img.Height;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Keep the best of each group of look-alike pictures and move the others into a "Similar photos" folder
        /// (inside the dragged folder, or next to the dragged pictures). Undoable. Returns how many were moved.
        /// </summary>
        public static int MoveSimilar(IList<string> paths, out string folder)
        {
            string dir = paths.Count == 1 && Directory.Exists(paths[0]) ? paths[0] : ParentOf(paths[0]);
            Guard.Modifiable(new[] { dir });
            folder = Path.Combine(dir, "Similar photos");
            string target = folder;
            List<string> images = ImagesIn(paths).Where(f => !IsOnlineOnly(f)).ToList();
            var moves = new List<KeyValuePair<string, string>>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> group in SimilarGroups(images, 6))
                foreach (string extra in group.Skip(1))
                {
                    string name = Path.GetFileNameWithoutExtension(extra), ext = Path.GetExtension(extra);
                    string dest = Path.Combine(target, name + ext);
                    for (int i = 1; File.Exists(dest) || claimed.Contains(dest); i++) dest = Path.Combine(target, name + " (" + i + ")" + ext);
                    claimed.Add(dest);
                    moves.Add(new KeyValuePair<string, string>(extra, dest));
                }
            if (moves.Count == 0) return 0;
            using (UndoLog.Begin("Similar photos"))
            {
                if (!Directory.Exists(target)) UndoLog.EnsureFolder(target);
                return ApplyMoves(moves);
            }
        }
    }
}
