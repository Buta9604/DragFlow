using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    /// <summary>
    /// Folder rules: "for files dragged from this folder, offer these actions first, hide those, and put these
    /// folders first under Move to". Stored in %APPDATA%\DragFlow\profiles.ini as [Name] sections; edited in Settings.
    /// </summary>
    public sealed class Profile
    {
        public string Name = "", Folder = "";
        /// <summary>File kinds it applies to (image, pdf, video, audio, word, text, archive, folder, other, mixed); empty = all.</summary>
        public List<string> Types = new List<string>();
        /// <summary>Action ids (or labels) to put first, in order.</summary>
        public List<string> First = new List<string>();
        /// <summary>Action ids (or labels) to leave out.</summary>
        public List<string> Hide = new List<string>();
        public List<string> MoveTo = new List<string>();
        /// <summary>Programs the drag comes from (outlook, chrome…); empty = any. A profile may name programs instead of a folder.</summary>
        public List<string> Apps = new List<string>();

        public bool Matches(string folder, FileKind kind) { return Matches(folder, kind, null); }

        public bool Matches(string folder, FileKind kind, string app)
        {
            if (Apps.Count > 0 && (app == null || !Apps.Any(a => Settings.NormalizeApp(a) == app))) return false;
            if (!string.IsNullOrEmpty(Folder))
            {
                if (string.IsNullOrEmpty(folder)) return false;
                string f = folder.TrimEnd('\\'), mine = Folder.TrimEnd('\\');
                bool inside = string.Equals(f, mine, StringComparison.OrdinalIgnoreCase) || f.StartsWith(mine + "\\", StringComparison.OrdinalIgnoreCase);
                if (!inside) return false;
            }
            else if (Apps.Count == 0) return false;
            return Types.Count == 0 || Types.Contains("*") || Types.Contains(kind.ToString().ToLowerInvariant());
        }
    }

    public static class Profiles
    {
        public static string FilePath { get { return AppPaths.RoamingFile("profiles.ini"); } }

        static readonly object gate = new object();
        static string cachedPath;
        static DateTime cachedStamp;
        static List<Profile> cached = new List<Profile>();

        public static List<Profile> Load()
        {
            lock (gate)
            {
                try
                {
                    string path = FilePath;
                    DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    if (path != cachedPath || stamp != cachedStamp)
                    {
                        cached = stamp == DateTime.MinValue ? new List<Profile>() : Parse(File.ReadAllLines(path, Encoding.UTF8));
                        cachedPath = path; cachedStamp = stamp;
                    }
                    return new List<Profile>(cached);
                }
                catch (IOException) { return new List<Profile>(); }
                catch (UnauthorizedAccessException) { return new List<Profile>(); }
            }
        }

        /// <summary>The most specific profile for a drag (program-specific first, then the deepest folder), or null.</summary>
        public static Profile For(string folder, FileKind kind, string app = null)
        {
            return Load().Where(p => p.Matches(folder, kind, app)).OrderByDescending(p => p.Apps.Count > 0).ThenByDescending(p => p.Folder.Length).FirstOrDefault();
        }

        static List<string> Items(string v)
        {
            return v.Split(new[] { ',', '|' }).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        public static List<Profile> Parse(IEnumerable<string> lines)
        {
            var list = new List<Profile>();
            Profile cur = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    cur = new Profile { Name = line.Substring(1, line.Length - 2).Trim() };
                    list.Add(cur);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (cur == null || eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                if (k == "folder") cur.Folder = v.Trim('"');
                else if (k == "types") cur.Types = Items(v.ToLowerInvariant());
                else if (k == "actions" || k == "first") cur.First = Items(v);
                else if (k == "hide") cur.Hide = Items(v);
                else if (k == "apps") cur.Apps = Items(v.ToLowerInvariant());
                else if (k == "moveto") cur.MoveTo = v.Split('|').Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0).ToList();
            }
            return list.Where(p => p.Folder.Length > 0 || p.Apps.Count > 0).ToList();
        }

        public static void Save(IList<Profile> profiles)
        {
            var sb = new StringBuilder();
            sb.Append("# DragFlow folder profiles. Edit them in DragFlow Settings > Profiles.\r\n");
            sb.Append("# Folder = where the dragged items are; Types = image, pdf, video, audio, word, text, archive, folder, other, mixed (empty = all)\r\n");
            sb.Append("# Actions = shown first, in this order; Hide = left out; MoveTo = folders listed first under Move to (separated by |)\r\n\r\n");
            foreach (Profile p in profiles)
            {
                sb.Append('[').Append(p.Name.Length > 0 ? p.Name : "Profile").Append("]\r\n");
                if (p.Folder.Length > 0) sb.Append("Folder = ").Append(p.Folder).Append("\r\n");
                if (p.Apps.Count > 0) sb.Append("Apps = ").Append(string.Join(", ", p.Apps.ToArray())).Append("\r\n");
                if (p.Types.Count > 0) sb.Append("Types = ").Append(string.Join(", ", p.Types.ToArray())).Append("\r\n");
                if (p.First.Count > 0) sb.Append("Actions = ").Append(string.Join(", ", p.First.ToArray())).Append("\r\n");
                if (p.Hide.Count > 0) sb.Append("Hide = ").Append(string.Join(", ", p.Hide.ToArray())).Append("\r\n");
                if (p.MoveTo.Count > 0) sb.Append("MoveTo = ").Append(string.Join(" | ", p.MoveTo.ToArray())).Append("\r\n");
                sb.Append("\r\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
