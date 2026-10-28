using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragFlow
{
    public enum NameStyle { AsIs, Clean, Lower, Upper, Title }

    public static partial class FileOps
    {
        // ---------- copy here / copy to / new folder / shortcut ----------

        /// <summary>Copies next to the originals ("name - Copy.ext").</summary>
        public static List<string> CopyHere(IList<string> paths)
        {
            return CopyInto(paths, null, " - Copy", "Copy here");
        }

        /// <summary>Copies into another folder, renaming on clashes.</summary>
        public static List<string> CopyTo(IList<string> paths, string destDir)
        {
            if (!Directory.Exists(destDir)) throw new DirectoryNotFoundException("The folder " + destDir + " does not exist.");
            return CopyInto(paths, destDir, "", "Copy to " + NameOf(destDir));
        }

        static List<string> CopyInto(IList<string> paths, string destDir, string suffix, string title)
        {
            var outputs = new List<string>();
            using (UndoLog.Begin(title))
                Batch.Each(paths.ToList(), delegate (string p)
                {
                    string src = Path.GetFullPath(p).TrimEnd('\\');
                    bool dir = Directory.Exists(src);
                    string target = destDir ?? ParentOf(src);
                    if (dir && (target + "\\").StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A folder cannot be copied into itself.");
                    string dest = UniquePath(target, (dir ? NameOf(src) : Path.GetFileNameWithoutExtension(src)) + suffix, dir ? "" : Path.GetExtension(src));
                    try
                    {
                        if (dir) Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(src, dest, false);
                        else File.Copy(src, dest);
                    }
                    catch (Exception)
                    {
                        TryRemovePartial(dest);
                        throw;
                    }
                    UndoLog.Created(dest);
                    outputs.Add(dest);
                });
            if (outputs.Count == 0) throw Batch.Nothing("Nothing was copied.");
            return outputs;
        }

        /// <summary>Make a folder next to the items and move them into it.</summary>
        public static string NewFolderWith(IList<string> paths, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("That name has characters Windows does not allow.");
            string parent = ParentOf(Path.GetFullPath(paths[0]));
            string folder = UniquePath(parent, name.Trim(), "");
            Guard.Modifiable(paths.Concat(new[] { folder }));
            using (UndoLog.Begin("New folder"))
            {
                UndoLog.EnsureFolder(folder);
                MoveTo(paths, folder);
            }
            return folder;
        }

        /// <summary>A sensible name for a folder holding these items: their shared name start, else "New folder".</summary>
        public static string SuggestFolderName(IList<string> paths)
        {
            string prefix = CommonPrefix(paths.Select(p => Path.GetFileNameWithoutExtension(p.TrimEnd('\\'))).ToList());
            prefix = prefix.TrimEnd(' ', '_', '-', '.', '(');
            return prefix.Length >= 3 ? prefix : "New folder";
        }

        /// <summary>Shortcuts on the desktop pointing at each item.</summary>
        public static List<string> DesktopShortcuts(IList<string> paths)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var outputs = new List<string>();
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            using (UndoLog.Begin("Desktop shortcut"))
                Batch.Each(paths.ToList(), delegate (string p)
                {
                    string full = Path.GetFullPath(p).TrimEnd('\\');
                    string lnk = UniquePath(desktop, NameOf(full), ".lnk");
                    dynamic sc = shell.CreateShortcut(lnk);
                    sc.TargetPath = full;
                    sc.WorkingDirectory = Directory.Exists(full) ? full : ParentOf(full);
                    sc.Save();
                    UndoLog.Created(lnk);
                    outputs.Add(lnk);
                });
            return outputs;
        }

        // ---------- rename planning ----------

        public static string ApplyPattern(string pattern, string nameNoExt, int index, int total, DateTime date)
        {
            return ApplyPattern(pattern, nameNoExt, index, total, date, "");
        }

        public static string ApplyPattern(string pattern, string nameNoExt, int index, int total, DateTime date, string folder)
        {
            int width = Math.Max(2, total.ToString().Length);
            return pattern
                .Replace("{name}", nameNoExt)
                .Replace("{n}", (index + 1).ToString().PadLeft(width, '0'))
                .Replace("{date}", date.ToString("yyyy-MM-dd"))
                .Replace("{folder}", folder);
        }

        public static List<KeyValuePair<string, string>> PlanRename(IList<string> paths, string pattern)
        {
            return PlanRename(paths, pattern, NameStyle.AsIs);
        }

        /// <summary>
        /// Plan a batch rename. Returns (from, to) pairs, never touching disk. Tokens: {name} old name, {n} number,
        /// {date} photo date for pictures (else modified date), {folder} the containing folder's name.
        /// </summary>
        public static List<KeyValuePair<string, string>> PlanRename(IList<string> paths, string pattern, NameStyle style)
        {
            if (paths.Count > 1 && !pattern.Contains("{n}")) pattern += "_{n}";
            var plan = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < paths.Count; i++)
            {
                string p = paths[i];
                bool dir = Directory.Exists(p);
                string ext = dir ? "" : Path.GetExtension(p);
                string stem = dir ? NameOf(p) : Path.GetFileNameWithoutExtension(p);
                DateTime date = pattern.Contains("{date}") ? DateFor(p, dir) : DateTime.MinValue;
                string newStem = ApplyStyle(ApplyPattern(pattern, stem, i, paths.Count, date, NameOf(ParentOf(p))), style);
                if (newStem.Length == 0) newStem = stem;
                plan.Add(new KeyValuePair<string, string>(p, Path.Combine(ParentOf(p), newStem + ext)));
            }
            return plan;
        }

        static readonly Dictionary<string, DateTime> dateCache = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        static DateTime DateFor(string p, bool dir)
        {
            if (dir) return Directory.GetLastWriteTime(p);
            lock (dateCache)
            {
                DateTime d;
                if (dateCache.TryGetValue(p, out d)) return d;
                d = Classify(p) == FileKind.Image && !IsOnlineOnly(p) ? PhotoDate(p) : File.GetLastWriteTime(p);
                dateCache[p] = d;
                return d;
            }
        }

        /// <summary>Planned names that would collide with each other or with something already there.</summary>
        public static int Conflicts(IList<KeyValuePair<string, string>> plan)
        {
            var sources = new HashSet<string>(plan.Select(m => m.Key), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var m in plan)
            {
                bool clash = !seen.Add(m.Value) || (!sources.Contains(m.Value) && (File.Exists(m.Value) || Directory.Exists(m.Value)));
                if (clash) n++;
            }
            return n;
        }

        public static string ApplyStyle(string stem, NameStyle style)
        {
            switch (style)
            {
                case NameStyle.Clean: return CleanName(stem);
                case NameStyle.Lower: return stem.ToLowerInvariant();
                case NameStyle.Upper: return stem.ToUpperInvariant();
                case NameStyle.Title: return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(CleanName(stem).ToLower());
            }
            return stem;
        }

        /// <summary>"my__holiday.photo  (final)" → "my holiday photo (final)": separators become single spaces.</summary>
        public static string CleanName(string stem)
        {
            string s = Regex.Replace(stem, @"[_\.]+", " ");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim(' ', '-');
        }

        /// <summary>Ready-made patterns that fit the selection, best first.</summary>
        public static List<string> SuggestPatterns(IList<string> paths)
        {
            var list = new List<string>();
            string prefix = CommonPrefix(paths.Select(p => Path.GetFileNameWithoutExtension(p.TrimEnd('\\'))).ToList());
            string trimmed = Regex.Replace(prefix, @"[\d\s_\-\.\(]+$", "");
            bool images = paths.Any(p => Classify(p) == FileKind.Image);
            if (images) list.Add("{date}_{n}");
            if (trimmed.Length >= 3) list.Add(trimmed + "_{n}");
            list.Add("{name}_{n}");
            list.Add("{folder} {n}");
            if (!images) list.Add("{date}_{name}");
            return list.Distinct().ToList();
        }

        static string CommonPrefix(IList<string> names)
        {
            if (names.Count == 0) return "";
            string p = names[0];
            foreach (string n in names.Skip(1))
            {
                int i = 0;
                while (i < p.Length && i < n.Length && char.ToLowerInvariant(p[i]) == char.ToLowerInvariant(n[i])) i++;
                p = p.Substring(0, i);
            }
            return names.Count > 1 ? p : "";
        }
    }
}
