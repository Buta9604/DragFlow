using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DragFlow
{
    /// <summary>
    /// Translations. Text is written in English in the code and passed through <see cref="T"/>; a language file maps
    /// each English text to its translation. Files live in "lang" next to DragFlow.exe or in %APPDATA%\DragFlow\lang,
    /// named after the language ("de.txt", "es-MX.txt"), in pairs of lines:
    ///   en: Move to
    ///   tr: Verschieben nach
    /// Missing entries simply stay English. docs\translations\template.txt lists every text.
    /// </summary>
    public static class L
    {
        static Dictionary<string, string> map = new Dictionary<string, string>();

        /// <summary>The language in use ("en" when nothing is translated).</summary>
        public static string Language { get; private set; }

        static L() { Language = "en"; }

        /// <summary>Use a language ("auto" = the Windows display language). Returns false if no file was found.</summary>
        public static bool Use(string language)
        {
            map = new Dictionary<string, string>();
            Language = "en";
            if (string.IsNullOrEmpty(language) || language == "auto") language = CultureInfo.CurrentUICulture.Name;
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string name in new[] { language, language.Split('-')[0] })
                foreach (string dir in Folders())
                {
                    string file = Path.Combine(dir, name + ".txt");
                    if (!File.Exists(file)) continue;
                    map = Parse(File.ReadAllLines(file, Encoding.UTF8));
                    Language = name;
                    return true;
                }
            return false;
        }

        public static IEnumerable<string> Folders()
        {
            yield return Path.Combine(AppPaths.Roaming, "lang");
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
        }

        public static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var m = new Dictionary<string, string>();
            string en = null;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("en:")) en = Unescape(line.Substring(3).TrimStart());
                else if (line.StartsWith("tr:") && en != null)
                {
                    string tr = Unescape(line.Substring(3).TrimStart());
                    if (tr.Length > 0) m[en] = tr;
                    en = null;
                }
            }
            return m;
        }

        public static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unescape(string s) { return s.Replace("\\n", "\n").Replace("\\\\", "\\"); }

        /// <summary>The translation of an English text, or the text itself.</summary>
        public static string T(string english)
        {
            if (english == null) return null;
            string t;
            return map.TryGetValue(english, out t) ? t : english;
        }

        /// <summary>Translate a text with {0}-style blanks, then fill them in.</summary>
        public static string F(string english, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(english), args);
        }

        /// <summary>For tests: use this table directly.</summary>
        public static void UseTable(Dictionary<string, string> table, string language)
        {
            map = table ?? new Dictionary<string, string>();
            Language = language ?? "en";
        }
    }
}
