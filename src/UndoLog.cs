using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DragFlow
{
    public sealed class UndoStep
    {
        /// <summary>M = moved A to B, R = A was edited in place (backup in B), C = A was created,
        /// E = empty folder A was created, D = empty folder A was removed, W = wallpaper was A.</summary>
        public char Kind;
        public string A, B;
    }

    public sealed class UndoEntry
    {
        public string Id, Title;
        public DateTime Time;
        public readonly List<UndoStep> Steps = new List<UndoStep>();

        /// <summary>How many files or folders undoing this would touch (helper steps are not counted).</summary>
        public int ItemCount { get { return Steps.Count(s => s.Kind != 'E'); } }
    }

    public sealed class UndoResult
    {
        public string Title;
        public int Restored;
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>
    /// The undo journal. Every change DragFlow makes is recorded on disk (%LOCALAPPDATA%\DragFlow\undo), the last
    /// 20 operations are kept for up to 7 days, and undoing never deletes for good: files DragFlow created go to
    /// the Recycle Bin, moves are moved back, in-place edits are restored from a backup.
    /// Operations group their steps with <see cref="Begin"/>; nested scopes join the outer one.
    /// </summary>
    public static class UndoLog
    {
        public const int MaxEntries = 20;
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
        static readonly object gate = new object();

        /// <summary>How a created file is taken away on undo. Tests swap this for a plain move.</summary>
        public static Func<string, bool> Discard = ShellOps.RecycleOrPutAside;

        public static string Dir { get { return AppPaths.LocalDir("undo"); } }

        // ---------- recording ----------

        [ThreadStatic] static Tx current;

        sealed class Tx
        {
            public UndoEntry Entry;
            public int Depth;
        }

        public sealed class Scope : IDisposable
        {
            bool done;
            public void Dispose()
            {
                if (done) return;
                done = true;
                Tx tx = current;
                if (tx == null) return;
                if (--tx.Depth > 0) return;
                current = null;
                try { Commit(tx.Entry); }
                finally { lock (gate) busy.Remove(tx.Entry.Id); }
            }
        }

        /// <summary>Entries being recorded or undone right now (on any thread): housekeeping leaves their backups alone.</summary>
        static readonly HashSet<string> busy = new HashSet<string>();

        /// <summary>Start (or join) the operation running on this thread; everything recorded until the outermost
        /// scope is disposed becomes one undo entry.</summary>
        public static Scope Begin(string title)
        {
            if (current != null) current.Depth++;
            else
            {
                current = new Tx { Entry = NewEntry(title), Depth = 1 };
                lock (gate) busy.Add(current.Entry.Id);
            }
            return new Scope();
        }

        /// <summary>Id of the operation being recorded on this thread, or null.</summary>
        public static string CurrentId { get { return current == null ? null : current.Entry.Id; } }

        static long lastTicks;

        static UndoEntry NewEntry(string title)
        {
            long ticks;
            lock (gate) { ticks = lastTicks = Math.Max(lastTicks + 1, DateTime.UtcNow.Ticks); } // strictly increasing: ids sort in order
            return new UndoEntry
            {
                Id = ticks.ToString("D19") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Title = string.IsNullOrEmpty(title) ? "Last change" : title,
                Time = DateTime.Now
            };
        }

        static void Add(char kind, string a, string b)
        {
            var step = new UndoStep { Kind = kind, A = a, B = b };
            if (current != null) { current.Entry.Steps.Add(step); return; }
            UndoEntry e = NewEntry(null);
            e.Steps.Add(step);
            Commit(e);
        }

        public static void PushMoves(IEnumerable<KeyValuePair<string, string>> moves)
        {
            using (Begin("Move"))
                foreach (var m in moves) { Add('M', m.Key, m.Value); Touched(m.Value); }
        }

        public static void Created(string path) { Add('C', path, null); Touched(path); }

        /// <summary>Note a path DragFlow is about to create, so folder rules leave it alone even while it is being written.</summary>
        public static void Reserve(string path) { Touched(path); }

        /// <summary>Files and folders created so far in the operation running on this thread.</summary>
        public static HashSet<string> CreatedSoFar()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (current != null) foreach (UndoStep s in current.Entry.Steps) if (s.Kind == 'C') set.Add(s.A);
            return set;
        }

        static readonly Dictionary<string, long> recent = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        static void Touched(string path)
        {
            lock (recent)
            {
                long now = Clock.Now;
                if (recent.Count > 2000) foreach (string k in recent.Where(kv => now - kv.Value > 120000).Select(kv => kv.Key).ToList()) recent.Remove(k);
                recent[path.TrimEnd('\\')] = now;
            }
        }

        /// <summary>Did DragFlow itself create or move this (or a folder holding it) in the last two minutes? Folder rules leave those alone.</summary>
        public static bool RecentlyTouched(string path)
        {
            long now = Clock.Now;
            lock (recent)
                for (string p = path.TrimEnd('\\'); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                {
                    long t;
                    if (recent.TryGetValue(p, out t) && now - t < 120000) return true;
                }
            return false;
        }
        public static void CreatedFolder(string path) { Add('E', path, null); }
        public static void RemovedFolder(string path) { Add('D', path, null); }
        public static void WallpaperWas(string path) { Add('W', path ?? "", null); }

        /// <summary>Make the folder (and any missing parents) and record each new one so undo can tidy them away.</summary>
        public static void EnsureFolder(string path)
        {
            var missing = new List<string>();
            for (string d = path; !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d)) missing.Add(d);
            Directory.CreateDirectory(path);
            for (int i = missing.Count - 1; i >= 0; i--) CreatedFolder(missing[i]);
        }

        /// <summary>Copy a file aside before editing it in place; undo copies it back.</summary>
        public static void BackupBeforeEdit(string file)
        {
            using (Begin("Edit"))
            {
                string dir = Path.Combine(Dir, current.Entry.Id);
                Directory.CreateDirectory(dir);
                string backup = Path.Combine(dir, current.Entry.Steps.Count + Path.GetExtension(file));
                File.Copy(file, backup, true);
                Add('R', file, backup);
            }
        }

        /// <summary>Record that the folder now holds entries that were not in <paramref name="before"/>.</summary>
        public static void CreatedSince(string folder, ICollection<string> before)
        {
            foreach (string e in Directory.GetFileSystemEntries(folder))
                if (!before.Contains(e)) Created(e);
        }

        public static HashSet<string> Snapshot(string folder)
        {
            return new HashSet<string>(Directory.Exists(folder) ? Directory.GetFileSystemEntries(folder) : new string[0], StringComparer.OrdinalIgnoreCase);
        }

        static void Commit(UndoEntry e)
        {
            if (e.Steps.Count == 0) { DeleteEntryFiles(e.Id); return; }
            lock (gate)
            {
                Write(e);
                Prune();
            }
        }

        static void Write(UndoEntry e)
        {
            lock (gate)
            {
                var sb = new StringBuilder();
                sb.Append("DragFlowUndo 1\r\n");
                sb.Append("time\t").Append(e.Time.Ticks).Append("\r\n");
                sb.Append("title\t").Append(Clean(e.Title)).Append("\r\n");
                foreach (UndoStep s in e.Steps)
                    sb.Append(s.Kind).Append('\t').Append(s.A ?? "").Append('\t').Append(s.B ?? "").Append("\r\n");
                try { File.WriteAllText(Path.Combine(Dir, e.Id + ".undo"), sb.ToString(), Encoding.UTF8); }
                catch (IOException ex) { Log.Error("undo journal", ex); }
            }
        }

        static string Clean(string s) { return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        // ---------- reading ----------

        /// <summary>Recorded operations, newest first.</summary>
        public static List<UndoEntry> History()
        {
            var list = new List<UndoEntry>();
            lock (gate)
            {
                foreach (string f in Directory.GetFiles(Dir, "*.undo"))
                {
                    UndoEntry e = Read(f);
                    if (e != null) list.Add(e);
                }
            }
            return list.OrderByDescending(e => e.Id, StringComparer.Ordinal).ToList();
        }

        static UndoEntry Read(string file)
        {
            try
            {
                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                if (lines.Length < 3 || lines[0] != "DragFlowUndo 1") return null;
                var e = new UndoEntry { Id = Path.GetFileNameWithoutExtension(file) };
                foreach (string line in lines.Skip(1))
                {
                    string[] p = line.Split('\t');
                    if (p[0] == "time" && p.Length > 1) e.Time = new DateTime(long.Parse(p[1]));
                    else if (p[0] == "title" && p.Length > 1) e.Title = p[1];
                    else if (p[0].Length == 1 && p.Length >= 3) e.Steps.Add(new UndoStep { Kind = p[0][0], A = p[1], B = p[2].Length == 0 ? null : p[2] });
                }
                return e;
            }
            catch (Exception) { return null; }
        }

        public static bool CanUndo { get { lock (gate) return Directory.GetFiles(Dir, "*.undo").Length > 0; } }

        public static UndoEntry Latest { get { return History().FirstOrDefault(); } }

        // ---------- undoing ----------

        /// <summary>Undo the newest operation. Returns how many items were put back (0 = nothing to undo).</summary>
        public static int UndoLast()
        {
            UndoEntry e = Latest;
            return e == null ? 0 : Undo(e.Id).Restored;
        }

        public static UndoResult Undo(string id)
        {
            var result = new UndoResult();
            UndoEntry e;
            lock (gate)
            {
                string file = Path.Combine(Dir, id + ".undo");
                e = File.Exists(file) ? Read(file) : null;
                if (e == null) return result;
                File.Delete(file); // claim it so a double click cannot undo twice
                busy.Add(id);
            }
            try
            {
                result.Title = e.Title;
                var failed = new List<UndoStep>();
                var steps = e.Steps.ToList();
                var moves = steps.Where(s => s.Kind == 'M').ToList();
                if (Chained(moves))
                {
                    // Swaps and chains (a→b, b→c) must go back through temporary names, all at once.
                    var back = moves.Select(m => new KeyValuePair<string, string>(m.B, m.A)).ToList();
                    List<string> problems;
                    var done = FileOps.MoveSet(back, out problems);
                    result.Restored += done.Count;
                    foreach (UndoStep m in moves)
                        if (!done.Any(d => string.Equals(d.Value, m.A, StringComparison.OrdinalIgnoreCase))) failed.Add(m);
                    result.Problems.AddRange(problems);
                    steps.RemoveAll(s => s.Kind == 'M');
                }
                for (int i = steps.Count - 1; i >= 0; i--)
                {
                    UndoStep s = steps[i];
                    try
                    {
                        if (UndoStepNow(s) && s.Kind != 'E') result.Restored++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(s);
                        result.Problems.Add(Path.GetFileName((s.A ?? "").TrimEnd('\\')) + ": " + ex.Message);
                    }
                }
                if (failed.Count == 0) DeleteEntryFiles(e.Id);
                else
                {
                    // Keep what could not be undone (and its backups) so it can be tried again later.
                    var rest = new UndoEntry { Id = e.Id, Title = e.Title.EndsWith(" (unfinished)") ? e.Title : e.Title + " (unfinished)", Time = e.Time };
                    rest.Steps.AddRange(e.Steps.Where(failed.Contains));
                    Write(rest);
                }
                Log.Write("undo \"" + e.Title + "\": " + result.Restored + " restored, " + result.Problems.Count + " problem(s)");
                return result;
            }
            finally { lock (gate) busy.Remove(id); }
        }

        /// <summary>Do these moves depend on each other (one goes where another came from)?</summary>
        static bool Chained(List<UndoStep> moves)
        {
            if (moves.Count < 2) return false;
            var sources = new HashSet<string>(moves.Select(m => m.A), StringComparer.OrdinalIgnoreCase);
            return moves.Any(m => !string.Equals(m.A, m.B, StringComparison.OrdinalIgnoreCase) && sources.Contains(m.B));
        }

        static bool Exists(string p) { return File.Exists(p) || Directory.Exists(p); }

        static bool UndoStepNow(UndoStep s)
        {
            switch (s.Kind)
            {
                case 'M':
                    {
                        bool caseOnly = string.Equals(s.A, s.B, StringComparison.OrdinalIgnoreCase);
                        if (!Exists(s.B)) throw new IOException("it is no longer where DragFlow put it");
                        if (!caseOnly && Exists(s.A)) throw new IOException("something else now has its old name");
                        Directory.CreateDirectory(Path.GetDirectoryName(s.A));
                        FileOps.MoveEntry(s.B, s.A);
                        return true;
                    }
                case 'R':
                    if (!File.Exists(s.B)) throw new IOException("the backup copy is gone");
                    File.Copy(s.B, s.A, true);
                    return true;
                case 'C':
                    if (!Exists(s.A)) return false;
                    Discard(s.A);
                    return true;
                case 'E':
                    if (Directory.Exists(s.A) && !Directory.EnumerateFileSystemEntries(s.A).Any()) Directory.Delete(s.A);
                    return true;
                case 'D':
                    if (Directory.Exists(s.A)) return false;
                    Directory.CreateDirectory(s.A);
                    return true;
                case 'W':
                    if (s.A.Length > 0 && !SetWallpaper(s.A)) throw new InvalidOperationException("Windows refused to change the wallpaper back");
                    return true;
            }
            return false;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(int action, int param, string value, int flags);

        public static bool SetWallpaper(string path) { return SystemParametersInfo(20, 0, path, 3); }

        // ---------- housekeeping ----------

        /// <summary>Keep the newest 20 entries younger than 7 days; drop the rest with their backups.</summary>
        public static void Prune()
        {
            lock (gate)
            {
                var files = Directory.GetFiles(Dir, "*.undo").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
                long cutoff = (DateTime.UtcNow - MaxAge).Ticks;
                for (int i = 0; i < files.Count; i++)
                {
                    string id = Path.GetFileNameWithoutExtension(files[i]);
                    long ticks;
                    bool old = long.TryParse(id.Split('-')[0], out ticks) && ticks < cutoff;
                    if (busy.Contains(id)) continue;
                    if (i >= MaxEntries || old) { TryDelete(files[i]); DeleteEntryFiles(id); }
                }
                foreach (string d in Directory.GetDirectories(Dir))
                {
                    string id = Path.GetFileName(d);
                    if (!File.Exists(d + ".undo") && !busy.Contains(id)) DeleteEntryFiles(id);
                }
            }
        }

        static void DeleteEntryFiles(string id)
        {
            string d = Path.Combine(Dir, id);
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        static void TryDelete(string f)
        {
            try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>Forget everything (tests, and "Clear undo history").</summary>
        public static void Clear()
        {
            lock (gate)
            {
                foreach (string f in Directory.GetFiles(Dir, "*.undo")) TryDelete(f);
                Prune();
            }
        }
    }
}
