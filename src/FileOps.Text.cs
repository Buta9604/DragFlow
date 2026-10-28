using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DragFlow
{
    /// <summary>Text tools: line endings, UTF-8, tidy JSON, sort lines, Markdown.</summary>
    public static partial class FileOps
    {
        static List<string> TextFiles(IList<string> paths)
        {
            var files = paths.Where(p => Classify(p) == FileKind.Text || Classify(p) == FileKind.Web).ToList();
            if (files.Count == 0) throw new InvalidOperationException("No text files to work on.");
            Guard.Modifiable(files);
            return files;
        }

        /// <summary>Read a text file, remembering its encoding so it can be written back the same way.</summary>
        internal static string ReadText(string file, out Encoding enc)
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { enc = new UTF8Encoding(true); return enc.GetString(bytes, 3, bytes.Length - 3); }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { enc = Encoding.Unicode; return enc.GetString(bytes, 2, bytes.Length - 2); }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { enc = Encoding.BigEndianUnicode; return enc.GetString(bytes, 2, bytes.Length - 2); }
            try { enc = new UTF8Encoding(false, true); return enc.GetString(bytes); }
            catch (DecoderFallbackException) { enc = Encoding.Default; return enc.GetString(bytes); }
        }

        /// <summary>Change text files in place (with a backup for undo).</summary>
        static int EditText(IList<string> paths, string title, Func<string, string> edit, bool forceUtf8)
        {
            int n = 0;
            using (UndoLog.Begin(title))
                Batch.Each(TextFiles(paths), delegate (string file)
                {
                    if (new FileInfo(file).Length > 50 * 1024 * 1024) throw new InvalidOperationException("over 50 MB; open it in an editor instead");
                    Encoding enc;
                    string text = ReadText(file, out enc);
                    string changed = edit(text);
                    Encoding outEnc = forceUtf8 ? new UTF8Encoding(false) : enc;
                    bool sameEncoding = outEnc.CodePage == enc.CodePage && outEnc.GetPreamble().Length == enc.GetPreamble().Length;
                    if (changed == text && sameEncoding) return;
                    UndoLog.BackupBeforeEdit(file);
                    File.WriteAllText(file, changed, outEnc);
                    n++;
                });
            return n;
        }

        /// <summary>Windows (CRLF) or Unix (LF) line endings.</summary>
        public static int LineEndings(IList<string> paths, bool windows)
        {
            return EditText(paths, windows ? "Windows line endings" : "Unix line endings", delegate (string t)
            {
                string lf = t.Replace("\r\n", "\n").Replace("\r", "\n");
                return windows ? lf.Replace("\n", "\r\n") : lf;
            }, false);
        }

        /// <summary>Any encoding to UTF-8 without a byte-order mark.</summary>
        public static int ToUtf8(IList<string> paths) { return EditText(paths, "To UTF-8", t => t, true); }

        /// <summary>Sort lines (optionally dropping repeats), keeping the file's own line ending.</summary>
        public static int SortLines(IList<string> paths, bool removeDuplicates)
        {
            return EditText(paths, removeDuplicates ? "Remove duplicate lines" : "Sort lines", delegate (string t)
            {
                string nl = t.Contains("\r\n") ? "\r\n" : "\n";
                bool trailing = t.EndsWith("\n");
                var lines = t.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
                if (removeDuplicates) lines = lines.Distinct(StringComparer.Ordinal).ToList();
                else lines.Sort(StringComparer.CurrentCulture);
                return string.Join(nl, lines.ToArray()) + (trailing ? nl : "");
            }, false);
        }

        /// <summary>Pretty-print JSON (two-space indents). Invalid JSON is reported, not changed.</summary>
        public static int TidyJson(IList<string> paths)
        {
            return EditText(paths, "Tidy JSON", delegate (string t)
            {
                var sb = new StringBuilder();
                int pos = 0;
                JsonCopy(t, ref pos, sb, 0);
                JsonSkipSpace(t, ref pos);
                if (pos < t.Length) throw new InvalidOperationException("not valid JSON (extra text after the end)");
                return sb.ToString() + "\r\n";
            }, false);
        }

        static void JsonSkipSpace(string t, ref int i) { while (i < t.Length && char.IsWhiteSpace(t[i])) i++; }

        static void JsonCopy(string t, ref int i, StringBuilder sb, int depth)
        {
            JsonSkipSpace(t, ref i);
            if (i >= t.Length) throw new InvalidOperationException("not valid JSON (it ends too early)");
            char c = t[i];
            if (c == '{' || c == '[')
            {
                if (depth >= 512) throw new InvalidOperationException("not valid JSON (too deeply nested)"); // the recursion must end somewhere
                char close = c == '{' ? '}' : ']';
                i++;
                JsonSkipSpace(t, ref i);
                if (i < t.Length && t[i] == close) { i++; sb.Append(c).Append(close); return; }
                sb.Append(c);
                bool first = true;
                while (true)
                {
                    if (!first) { JsonSkipSpace(t, ref i); if (i >= t.Length || t[i] != ',') break; i++; sb.Append(','); }
                    first = false;
                    sb.Append("\r\n").Append(' ', (depth + 1) * 2);
                    if (c == '{')
                    {
                        JsonSkipSpace(t, ref i);
                        if (i >= t.Length || t[i] != '"') throw new InvalidOperationException("not valid JSON (a name in quotes was expected)");
                        JsonString(t, ref i, sb);
                        JsonSkipSpace(t, ref i);
                        if (i >= t.Length || t[i] != ':') throw new InvalidOperationException("not valid JSON (a colon was expected)");
                        i++;
                        sb.Append(": ");
                    }
                    JsonCopy(t, ref i, sb, depth + 1);
                }
                JsonSkipSpace(t, ref i);
                if (i >= t.Length || t[i] != close) throw new InvalidOperationException("not valid JSON (missing " + close + ")");
                i++;
                sb.Append("\r\n").Append(' ', depth * 2).Append(close);
                return;
            }
            if (c == '"') { JsonString(t, ref i, sb); return; }
            int start = i;
            while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] == '-' || t[i] == '+' || t[i] == '.')) i++;
            string word = t.Substring(start, i - start);
            double num;
            if (word == "true" || word == "false" || word == "null" || double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) { sb.Append(word); return; }
            throw new InvalidOperationException("not valid JSON (unexpected \"" + (word.Length > 0 ? word : c.ToString()) + "\")");
        }

        static void JsonString(string t, ref int i, StringBuilder sb)
        {
            int start = i++;
            while (i < t.Length && t[i] != '"') { if (t[i] == '\\') i++; i++; }
            if (i >= t.Length) throw new InvalidOperationException("not valid JSON (a quote is never closed)");
            i++;
            sb.Append(t, start, i - start);
        }

        /// <summary>A Markdown file as a web page (.html) next to it.</summary>
        public static string MarkdownToHtml(string file)
        {
            Encoding enc;
            string html = Markdown.ToHtml(ReadText(file, out enc), Path.GetFileNameWithoutExtension(file));
            string dest = OutputFor(file, "", ".html");
            using (UndoLog.Begin("Markdown to HTML"))
            {
                File.WriteAllText(dest, html, new UTF8Encoding(false));
                UndoLog.Created(dest);
            }
            return dest;
        }
    }

    /// <summary>A small Markdown renderer: headings, lists, bold, italics, code, links, quotes, rules, paragraphs.</summary>
    public static class Markdown
    {
        public static bool IsMarkdown(string file)
        {
            string e = Path.GetExtension(file).ToLowerInvariant();
            return e == ".md" || e == ".markdown";
        }

        public static string ToHtml(string md, string title)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(WebUtility.HtmlEncode(title)).Append("</title><style>")
              .Append("body{font:11pt Segoe UI,sans-serif;max-width:46em;margin:2em auto;line-height:1.5;color:#222}pre{background:#f4f4f6;padding:.7em;overflow:auto}")
              .Append("code{background:#f4f4f6;padding:0 .2em}blockquote{border-left:3px solid #ccc;margin:0;padding:0 1em;color:#555}h1,h2,h3{line-height:1.2}</style></head><body>\n");
            string[] lines = md.Replace("\r\n", "\n").Split('\n');
            bool inCode = false, inList = false, inOrdered = false, inQuote = false;
            var para = new StringBuilder();
            Action flushPara = delegate { if (para.Length > 0) { sb.Append("<p>").Append(Inline(para.ToString().Trim())).Append("</p>\n"); para.Length = 0; } };
            Action closeLists = delegate
            {
                if (inList) { sb.Append("</ul>\n"); inList = false; }
                if (inOrdered) { sb.Append("</ol>\n"); inOrdered = false; }
                if (inQuote) { sb.Append("</blockquote>\n"); inQuote = false; }
            };
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("```"))
                {
                    flushPara(); closeLists();
                    sb.Append(inCode ? "</code></pre>\n" : "<pre><code>");
                    inCode = !inCode;
                    continue;
                }
                if (inCode) { sb.Append(WebUtility.HtmlEncode(raw)).Append('\n'); continue; }
                Match h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                if (h.Success) { flushPara(); closeLists(); int n = h.Groups[1].Length; sb.Append("<h" + n + ">").Append(Inline(h.Groups[2].Value)).Append("</h" + n + ">\n"); continue; }
                if (Regex.IsMatch(line, @"^(\*\s*){3,}$|^(-\s*){3,}$")) { flushPara(); closeLists(); sb.Append("<hr>\n"); continue; }
                Match li = Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
                if (li.Success) { flushPara(); if (inOrdered) { sb.Append("</ol>\n"); inOrdered = false; } if (!inList) { sb.Append("<ul>\n"); inList = true; } sb.Append("<li>").Append(Inline(li.Groups[1].Value)).Append("</li>\n"); continue; }
                Match ol = Regex.Match(line, @"^\s*\d+[.)]\s+(.*)$");
                if (ol.Success) { flushPara(); if (inList) { sb.Append("</ul>\n"); inList = false; } if (!inOrdered) { sb.Append("<ol>\n"); inOrdered = true; } sb.Append("<li>").Append(Inline(ol.Groups[1].Value)).Append("</li>\n"); continue; }
                if (line.StartsWith(">")) { flushPara(); if (!inQuote) { sb.Append("<blockquote>\n"); inQuote = true; } sb.Append("<p>").Append(Inline(line.TrimStart('>', ' '))).Append("</p>\n"); continue; }
                if (line.Length == 0) { flushPara(); closeLists(); continue; }
                closeLists();
                para.Append(line).Append(' ');
            }
            flushPara(); closeLists();
            if (inCode) sb.Append("</code></pre>\n");
            sb.Append("</body></html>\n");
            return sb.ToString();
        }

        /// <summary>Bold, italics, code and links inside a line.</summary>
        public static string Inline(string text)
        {
            // Code spans are set aside first, so stars and brackets inside them are left exactly as written.
            var codes = new List<string>();
            string s = Regex.Replace(text, @"`([^`]+)`", delegate (Match m) { codes.Add(m.Groups[1].Value); return "\u0001" + (codes.Count - 1) + "\u0001"; });
            s = WebUtility.HtmlEncode(s);
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            s = Regex.Replace(s, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<em>$1</em>");
            s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)\s]+)\)", "<img alt=\"$1\" src=\"$2\">");
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)\)", "<a href=\"$2\">$1</a>");
            s = Regex.Replace(s, "\u0001(\\d+)\u0001", delegate (Match m) { return "<code>" + WebUtility.HtmlEncode(codes[int.Parse(m.Groups[1].Value)]) + "</code>"; });
            return s;
        }
    }
}
