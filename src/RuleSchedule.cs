using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace DragFlow
{
    /// <summary>"daily 18:00" or "weekly sun 18:00" (day names in English, or 1–7 with Monday first).</summary>
    public sealed class RuleSchedule
    {
        public DayOfWeek? Day;
        public TimeSpan Time;

        static readonly string[] Days = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public static RuleSchedule Parse(string text)
        {
            string t = (text ?? "").Trim().ToLowerInvariant();
            Match m = Regex.Match(t, @"^(daily|every day|weekly|every week)\s+(?:([a-z]{3,9}|[1-7])\s+)?(?:at\s*)?(\d{1,2})(?:[:.](\d{2}))?$");
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[3].Value), min = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
            if (h > 23 || min > 59) return null;
            var s = new RuleSchedule { Time = new TimeSpan(h, min, 0) };
            string day = m.Groups[2].Success ? m.Groups[2].Value : "";
            if (m.Groups[1].Value.Contains("week"))
            {
                if (day.Length == 0) return null;
                int n;
                if (int.TryParse(day, out n)) s.Day = (DayOfWeek)(n % 7);
                else
                {
                    int i = Array.IndexOf(Days, day.Substring(0, 3));
                    if (i < 0) return null;
                    s.Day = (DayOfWeek)i;
                }
            }
            else if (day.Length > 0) return null;
            return s;
        }

        /// <summary>The latest scheduled moment at or before now.</summary>
        public DateTime Previous(DateTime now)
        {
            DateTime at;
            if (Day == null)
            {
                at = now.Date + Time;
                return at <= now ? at : at.AddDays(-1);
            }
            int back = ((int)now.DayOfWeek - (int)Day.Value + 7) % 7;
            at = now.Date.AddDays(-back) + Time;
            return at <= now ? at : at.AddDays(-7);
        }
    }

    /// <summary>Windows' "this came from the internet" mark: the Zone.Identifier stream browsers add to downloads.</summary>
    public static class Zone
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        public static bool FromInternet(string file)
        {
            try
            {
                string full = Path.GetFullPath(file);
                string longPath = full.StartsWith(@"\\") ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full; // network shares need the UNC form
                using (SafeFileHandle h = CreateFile(longPath + ":Zone.Identifier", 0x80000000 /* read */, 7, IntPtr.Zero, 3 /* existing */, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return false;
                    using (var fs = new FileStream(h, FileAccess.Read))
                    using (var r = new StreamReader(fs))
                    {
                        Match m = Regex.Match(r.ReadToEnd(), @"ZoneId\s*=\s*(\d)");
                        return m.Success && (m.Groups[1].Value == "3" || m.Groups[1].Value == "4");
                    }
                }
            }
            catch (Exception) { return false; }
        }
    }
}

namespace DragFlow
{
    /// <summary>"Try it": the files a rule would take right now, without doing anything.</summary>
    public static class RulePreview
    {
        public static List<string> Matches(Rule rule, int max)
        {
            var found = new List<string>();
            if (rule.Folder.Length == 0 || !Directory.Exists(rule.Folder)) return found;
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string file = Path.Combine(AppPaths.Local, "rules-done.txt");
                if (File.Exists(file)) foreach (string k in File.ReadAllLines(file)) done.Add(k);
            }
            catch (Exception) { }
            IEnumerable<string> files = rule.Subfolders ? FileOps.FilesUnder(rule.Folder) : Directory.EnumerateFiles(rule.Folder);
            foreach (string f in files.Take(5000))
            {
                if (!rule.MatchesName(f) || RuleEngine.Ignorable(f) || !rule.Qualifies(f)) continue;
                if (done.Contains(f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks)) continue; // handled before
                found.Add(f);
                if (found.Count >= max) break;
            }
            return found;
        }
    }
}
