using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    /// <summary>
    /// Remembers which actions you run, on what kind of file and from which folder, so the dial can put the likely
    /// one at 12 o'clock and list recent "Move to" folders. Recent use counts more (a two-week half-life).
    /// Stored locally in %APPDATA%\DragFlow\usage.tsv (last 3000 uses).
    /// </summary>
    public sealed class Usage
    {
        public static Usage Current = new Usage();

        public const int MaxRecords = 3000;
        const double HalfLifeDays = 14;

        sealed class Rec
        {
            public DateTime When;
            public FileKind Kind;
            public string Id, Folder;
        }

        readonly List<Rec> recs = new List<Rec>();
        public Func<DateTime> Now = delegate { return DateTime.Now; };

        public string FilePath { get { return AppPaths.RoamingFile("usage.tsv"); } }

        public void Record(FileKind kind, string folder, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var r = new Rec { When = Now(), Kind = kind, Id = id, Folder = Norm(folder) };
            lock (recs)
            {
                recs.Add(r);
                if (recs.Count > MaxRecords + 200) { recs.RemoveRange(0, recs.Count - MaxRecords); Save(); return; }
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, Line(r) + "\r\n", Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static string Norm(string folder) { return string.IsNullOrEmpty(folder) ? "" : folder.TrimEnd('\\').ToLowerInvariant(); }

        static string Line(Rec r)
        {
            return r.When.Ticks + "\t" + r.Kind + "\t" + r.Id.Replace('\t', ' ') + "\t" + (r.Folder ?? "").Replace('\t', ' ');
        }

        double Weight(DateTime when)
        {
            double days = Math.Max(0, (Now() - when).TotalDays);
            return Math.Pow(0.5, days / HalfLifeDays);
        }

        /// <summary>How strongly an action is expected for this kind of file from this folder (0 = never used).</summary>
        public double Score(FileKind kind, string folder, string id)
        {
            string f = Norm(folder);
            double s = 0;
            lock (recs)
                foreach (Rec r in recs)
                {
                    if (r.Id != id) continue;
                    double k = (r.Kind == kind ? 1 : 0) + (f.Length > 0 && r.Folder == f ? 2 : 0);
                    if (k > 0) s += k * Weight(r.When);
                }
            return s;
        }

        /// <summary>Folders recently used with "Move to"/"Copy to", newest first.</summary>
        public List<string> RecentTargets(int max)
        {
            var list = new List<string>();
            lock (recs)
                for (int i = recs.Count - 1; i >= 0 && list.Count < max; i--)
                {
                    string id = recs[i].Id;
                    if (!id.StartsWith("moveto:") && !id.StartsWith("copyto:")) continue;
                    string dir = id.Substring(7);
                    if (dir.Length > 0 && !list.Contains(dir, StringComparer.OrdinalIgnoreCase)) list.Add(dir);
                }
            return list;
        }

        public int Count { get { lock (recs) return recs.Count; } }

        /// <summary>The action ids used most since a moment, most used first (for the dashboard).</summary>
        public List<KeyValuePair<string, int>> Top(DateTime since, int n)
        {
            lock (recs)
                return recs.Where(r => r.When >= since).GroupBy(r => r.Id).Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                           .OrderByDescending(kv => kv.Value).Take(n).ToList();
        }

        public void Clear()
        {
            lock (recs) recs.Clear();
            Save();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                lock (recs) File.WriteAllLines(FilePath, recs.Select(Line).ToArray(), Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static Usage Load()
        {
            var u = new Usage();
            try
            {
                if (!File.Exists(u.FilePath)) return u;
                foreach (string line in File.ReadAllLines(u.FilePath, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    long ticks;
                    FileKind kind;
                    if (p.Length < 4 || !long.TryParse(p[0], out ticks) || !Enum.TryParse(p[1], out kind)) continue;
                    u.recs.Add(new Rec { When = new DateTime(ticks), Kind = kind, Id = p[2], Folder = p[3] });
                }
                if (u.recs.Count > MaxRecords) u.recs.RemoveRange(0, u.recs.Count - MaxRecords);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return u;
        }
    }
}
