using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DragFlow
{
    /// <summary>"When a new file matching this lands in this folder, do that." Kept in %APPDATA%\DragFlow\rules.ini.</summary>
    public sealed class Rule
    {
        public bool Enabled = true, Subfolders;
        public string Name = "", Folder = "", Pattern = "*", Action = "", Target = "";
        /// <summary>Only files older than this (days) and bigger than this (MB); 0 = any.</summary>
        public int MinAgeDays, MinSizeMB;
        /// <summary>Also look at the files already there, once an hour (needed for "older than").</summary>
        public bool Scan;
        /// <summary>When the rule runs: "" as files arrive (and hourly when Scan), or "daily 18:00", "weekly sun 18:00".</summary>
        public string Schedule = "";
        /// <summary>Only files downloaded from the internet (Windows marks them).</summary>
        public bool WebOnly;

        public bool Scheduled { get { return RuleSchedule.Parse(Schedule) != null; } }

        /// <summary>Does the file meet the age and size conditions?</summary>
        public bool Qualifies(string file)
        {
            try
            {
                var fi = new FileInfo(file);
                if (MinSizeMB > 0 && fi.Length < MinSizeMB * 1024L * 1024L) return false;
                // A copy keeps its old "modified" date but gets a fresh "created" one: the file counts as old only when both are.
                DateTime stamp = fi.CreationTime > fi.LastWriteTime ? fi.CreationTime : fi.LastWriteTime;
                if (MinAgeDays > 0 && (DateTime.Now - stamp).TotalDays < MinAgeDays) return false;
                if (WebOnly && !Zone.FromInternet(file)) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The action id to run, with the target folder for move and copy.</summary>
        public string ActionId
        {
            get { return Action == "moveto" || Action == "copyto" ? Action + ":" + Target : Action; }
        }

        public bool MatchesName(string file)
        {
            string name = Path.GetFileName(file);
            foreach (string raw in (Pattern ?? "*").Split(';', ','))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string rx = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (Regex.IsMatch(name, rx, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>What is wrong with the rule, in plain words, or null.</summary>
        public string Problem()
        {
            if (Folder.Length == 0 || !Directory.Exists(Folder)) return "the folder \"" + Folder + "\" does not exist";
            if (RuleActions.Label(Action) == null) return "no action chosen";
            if ((Action == "moveto" || Action == "copyto") && (Target.Length == 0 || !Directory.Exists(Target))) return "the target folder \"" + Target + "\" does not exist";
            if ((Action == "moveto" || Action == "copyto") && string.Equals(Target.TrimEnd('\\'), Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return "the target folder is the watched folder";
            if (Action.StartsWith("recipe:") && RecipeStore.Find(Action.Substring(7)) == null) return "the recipe \"" + Action.Substring(7) + "\" does not exist";
            if ((Schedule ?? "").Trim().Length > 0 && RuleSchedule.Parse(Schedule) == null) return "\"" + Schedule + "\" is not a schedule (use daily 18:00 or weekly sun 18:00)";
            return null;
        }
    }

    /// <summary>The actions a rule can run.</summary>
    public static class RuleActions
    {
        public static readonly string[][] Choices =
        {
            new[] { "moveto", "Move to a folder" }, new[] { "copyto", "Copy to a folder" },
            new[] { "convert:jpg", "Convert pictures to JPG" }, new[] { "convert:png", "Convert pictures to PNG" },
            new[] { "convert:webp", "Convert pictures to WebP" }, new[] { "shrink", "Shrink pictures" },
            new[] { "strip", "Remove photo info (EXIF)" }, new[] { "pdf", "Pictures to PDF" },
            new[] { "extract", "Extract archives here" }, new[] { "extractfolder", "Extract archives to a folder" },
            new[] { "media:mp4", "Video to MP4" }, new[] { "media:mp3", "Audio to MP3" }, new[] { "webpdf", "Web page to PDF" },
            new[] { "recycle", "Move to the Recycle Bin" }
        };

        public static string Label(string id)
        {
            if (id != null && id.StartsWith("recipe:")) return L.T("Recipe:") + " " + id.Substring(7);
            string[] c = Choices.FirstOrDefault(x => x[0] == id);
            return c == null ? null : L.T(c[1]);
        }
    }

    public static class RuleStore
    {
        public static string FilePath { get { return AppPaths.RoamingFile("rules.ini"); } }

        public static List<Rule> Load()
        {
            var list = new List<Rule>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (line.TrimStart().StartsWith("#") || line.Trim().Length == 0) continue;
                    string[] p = line.Split('|').Select(x => x.Trim()).ToArray();
                    if (p.Length < 6) continue;
                    var r = new Rule
                    {
                        Enabled = p[0] == "on", Name = p[1], Folder = p[2], Pattern = p[3].Length > 0 ? p[3] : "*", Action = p[4], Target = p[5],
                        Subfolders = p.Length > 6 && p[6] == "sub"
                    };
                    if (p.Length > 7) int.TryParse(p[7], out r.MinAgeDays);
                    if (p.Length > 8) int.TryParse(p[8], out r.MinSizeMB);
                    r.Scan = p.Length > 9 && p[9] == "scan";
                    if (p.Length > 10) r.Schedule = p[10];
                    r.WebOnly = p.Length > 11 && p[11] == "web";
                    if (r.Schedule == "hourly") { r.Scan = true; r.Schedule = ""; }
                    if (r.MinAgeDays > 0) r.Scan = true; // "older than" only makes sense with a scan
                    list.Add(r);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return list;
        }

        public static void Save(IList<Rule> rules)
        {
            var sb = new StringBuilder("# DragFlow folder rules (edit them in Settings > Rules)\r\n# on/off | name | folder | files | action | target folder | sub | older than days | bigger than MB | scan | schedule | web\r\n");
            foreach (Rule r in rules)
                sb.Append(r.Enabled ? "on" : "off").Append(" | ").Append(r.Name.Replace("|", "/")).Append(" | ").Append(r.Folder).Append(" | ")
                  .Append(r.Pattern.Replace("|", ";")).Append(" | ").Append(r.Action).Append(" | ").Append(r.Target).Append(" | ").Append(r.Subfolders ? "sub" : "")
                  .Append(" | ").Append(r.MinAgeDays).Append(" | ").Append(r.MinSizeMB).Append(" | ").Append(r.Scan ? "scan" : "")
                  .Append(" | ").Append((r.Schedule ?? "").Replace("|", " ")).Append(" | ").Append(r.WebOnly ? "web" : "").Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }
    }

    /// <summary>
    /// Watches the rules' folders. A new file is handled only once it has stopped growing and nobody is writing it
    /// (so downloads finish first), half-downloaded and temporary files are ignored, and files DragFlow itself just
    /// made or moved never trigger a rule (no loops). Files arriving together are handled together: one job, one
    /// notification, one Undo. A rule that fires more than 30 times a minute is paused.
    /// </summary>
    public sealed class RuleEngine : IDisposable
    {
        sealed class Pending
        {
            public Rule Rule;
            public long Seen;
            public long Size = -2;
        }

        /// <summary>Runs a rule's action on files (set by the app).</summary>
        public Action<Rule, string[]> Run = delegate { };
        public Action<Rule, string> Paused = delegate { };

        static readonly string[] TempExts = { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".temp" };
        const int MaxPerMinute = 30;

        readonly object gate = new object();
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        // Files handled before (path, size, date), remembered across restarts so an hourly scan never redoes a convert or copy.
        readonly HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> doneOrder = new List<string>();
        bool doneLoaded, doneDirty;
        const int MaxDone = 20000;
        readonly Dictionary<Rule, List<long>> fired = new Dictionary<Rule, List<long>>();
        readonly HashSet<Rule> paused = new HashSet<Rule>();
        readonly List<Rule> scanning = new List<Rule>();
        readonly List<Rule> scheduled = new List<Rule>();
        /// <summary>The wall clock for schedules (tests set it).</summary>
        public Func<DateTime> Now = delegate { return DateTime.Now; };
        Timer timer;
        long lastScan = -1;
        /// <summary>How often existing files are looked at (ms).</summary>
        public int ScanEveryMs = 60 * 60 * 1000;
        /// <summary>Off in tests that call Process themselves, so nothing runs behind their back.</summary>
        public bool TicksOnItsOwn = true;

        public int Watching { get { lock (gate) return watchers.Count; } }

        public void Apply(IEnumerable<Rule> rules, bool enabled)
        {
            // Watchers are disposed outside the lock: a watcher raising an event waits for the lock, and disposing it
            // waits for the event to finish, so doing both at once would stop DragFlow for good.
            DisposeAll(DetachWatchers());
            lock (gate)
            {
                pending.Clear();
                paused.Clear();
                scanning.Clear();
                if (!enabled) return;
                scanning.AddRange(rules.Where(x => x.Enabled && x.Scan && !x.Scheduled && x.Problem() == null));
                scheduled.Clear();
                scheduled.AddRange(rules.Where(x => x.Enabled && x.Scheduled && x.Problem() == null));
                lastScan = -1; // the first scan happens on the next tick
                foreach (Rule r in rules.Where(x => x.Enabled && !x.Scheduled && x.Problem() == null))
                {
                    Rule rule = r;
                    try
                    {
                        var w = new FileSystemWatcher(rule.Folder)
                        {
                            IncludeSubdirectories = rule.Subfolders,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
                        };
                        w.Created += delegate (object s, FileSystemEventArgs e) { Notice(rule, e.FullPath, Clock.Now); };
                        w.Renamed += delegate (object s, RenamedEventArgs e) { Notice(rule, e.FullPath, Clock.Now); };
                        w.Changed += delegate (object s, FileSystemEventArgs e) { lock (gate) if (pending.ContainsKey(e.FullPath)) pending[e.FullPath].Seen = Clock.Now; };
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                    }
                    catch (Exception ex) { Log.Error("watching " + Log.P(rule.Folder), ex); }
                }
                if (timer == null && TicksOnItsOwn) timer = new Timer(delegate { try { Process(Clock.Now); } catch (Exception ex) { Log.Error("rules", ex); } }, null, 1000, 1000);
            }
        }

        /// <summary>A cloud file whose contents are not on this PC (OneDrive "Files On-Demand"); touching it would download it.</summary>
        const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

        public static bool Ignorable(string path)
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("~$") || name.StartsWith(".dragflow")) return true;
            if (TempExts.Contains(Path.GetExtension(path).ToLowerInvariant())) return true;
            if (path.IndexOf("\\" + ShellOps.AsideFolderName + "\\", StringComparison.OrdinalIgnoreCase) >= 0) return true; // things DragFlow put aside
            try
            {
                if (Directory.Exists(path)) return true;
                FileAttributes a = File.GetAttributes(path);
                if ((a & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary | FileAttributes.Offline | RecallOnDataAccess)) != 0) return true;
            }
            catch (Exception) { return true; }
            return UndoLog.RecentlyTouched(path);
        }

        /// <summary>A file appeared (or was renamed into place) in a watched folder.</summary>
        public void Notice(Rule rule, string path, long now)
        {
            if (rule.MinAgeDays > 0 || rule.Scheduled) return; // "older than" and scheduled rules only look at what is there
            if (!rule.MatchesName(path) || Ignorable(path)) return;
            lock (gate)
            {
                if (paused.Contains(rule)) return;
                pending[path] = new Pending { Rule = rule, Seen = now };
            }
        }

        static bool Settled(string path)
        {
            try { using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Handle files that have settled (called every second; public so the tests can drive it).</summary>
        public void Process(long now)
        {
            var ready = new List<KeyValuePair<Rule, string>>();
            List<Rule> toScan = null;
            lock (gate)
            {
                if (scanning.Count > 0 && (lastScan < 0 || now - lastScan > ScanEveryMs))
                {
                    lastScan = now;
                    toScan = scanning.Where(r => !paused.Contains(r)).ToList();
                }
                foreach (Rule r in scheduled.Where(x => !paused.Contains(x) && Due(x)).ToList())
                {
                    if (toScan == null) toScan = new List<Rule>();
                    toScan.Add(r);
                }
            }
            // The scan opens every file, so it runs outside the lock: the folder watchers must never wait on it.
            if (toScan != null) foreach (Rule rule in toScan) foreach (string f in Existing(rule)) ready.Add(new KeyValuePair<Rule, string>(rule, f));
            lock (gate)
            {
                foreach (var kv in pending.ToList())
                {
                    string path = kv.Key;
                    Pending p = kv.Value;
                    if (!File.Exists(path) || now - p.Seen > 10 * 60 * 1000) { pending.Remove(path); continue; }
                    long size;
                    try { size = new FileInfo(path).Length; } catch (Exception) { continue; }
                    if (size != p.Size) { p.Size = size; continue; } // still growing (or first look)
                    if (!Settled(path)) continue;
                    pending.Remove(path);
                    if (Ignorable(path) || !p.Rule.Qualifies(path)) continue; // e.g. DragFlow finished writing it in the meantime
                    if (!MarkDone(path + "|" + size + "|" + File.GetLastWriteTimeUtc(path).Ticks)) continue;
                    ready.Add(new KeyValuePair<Rule, string>(p.Rule, path));
                }
            }
            SaveDone();
            foreach (var group in ready.GroupBy(r => r.Key))
            {
                Rule rule = group.Key;
                lock (gate)
                {
                    List<long> times;
                    if (!fired.TryGetValue(rule, out times)) fired[rule] = times = new List<long>();
                    times.RemoveAll(t => now - t > 60000);
                    times.Add(now);
                    if (times.Count > MaxPerMinute)
                    {
                        paused.Add(rule);
                        Paused(rule, "it ran more than " + MaxPerMinute + " times in a minute");
                        continue;
                    }
                }
                Log.Write("rule \"" + rule.Name + "\" on " + group.Count() + " file(s)");
                Run(rule, group.Select(g => g.Value).ToArray());
            }
        }

        /// <summary>Files already in the rule's folder that it applies to (settled, not DragFlow's own, not handled before).</summary>
        public List<string> Existing(Rule rule)
        {
            var list = new List<string>();
            try
            {
                IEnumerable<string> files = rule.Subfolders ? FileOps.FilesUnder(rule.Folder) : Directory.EnumerateFiles(rule.Folder);
                foreach (string f in files.Take(5000))
                {
                    if (!rule.MatchesName(f) || Ignorable(f) || !rule.Qualifies(f)) continue;
                    string key = f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks;
                    lock (gate) { LoadDone(); if (done.Contains(key)) continue; }
                    if (!Settled(f)) continue;
                    lock (gate) MarkDone(key);
                    list.Add(f);
                }
            }
            catch (Exception ex) { Log.Error("scanning " + Log.P(rule.Folder), ex); }
            SaveDone();
            return list;
        }

        // ---------- schedules ----------

        readonly Dictionary<string, DateTime> lastRuns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        bool lastRunsLoaded;
        static string ScheduleFile { get { return Path.Combine(AppPaths.Local, "rules-schedule.txt"); } }

        static string ScheduleKey(Rule r) { return r.Name + "|" + r.Folder + "|" + r.Schedule; }

        /// <summary>
        /// Is the rule's scheduled time past since it last ran (called under the lock)? A new rule waits for its first
        /// time; a time missed while the PC was off is made up at the next chance.
        /// </summary>
        bool Due(Rule r)
        {
            RuleSchedule sch = RuleSchedule.Parse(r.Schedule);
            if (sch == null) return false;
            DateTime now = Now();
            if (!lastRunsLoaded)
            {
                lastRunsLoaded = true;
                try
                {
                    if (File.Exists(ScheduleFile))
                        foreach (string line in File.ReadAllLines(ScheduleFile))
                        {
                            int tab = line.LastIndexOf('\t');
                            long ticks;
                            if (tab > 0 && long.TryParse(line.Substring(tab + 1), out ticks)) lastRuns[line.Substring(0, tab)] = new DateTime(ticks);
                        }
                }
                catch (Exception ex) { Log.Error("reading rule schedules", ex); }
            }
            string key = ScheduleKey(r);
            DateTime last;
            if (!lastRuns.TryGetValue(key, out last)) { MarkRun(key, now); return false; }
            if (last >= sch.Previous(now)) return false;
            MarkRun(key, now);
            return true;
        }

        void MarkRun(string key, DateTime when)
        {
            lastRuns[key] = when;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScheduleFile));
                File.WriteAllLines(ScheduleFile, lastRuns.Select(kv => kv.Key + "\t" + kv.Value.Ticks).ToArray());
            }
            catch (Exception ex) { Log.Error("saving rule schedules", ex); }
        }

        // ---------- the memory of handled files ----------

        static string DoneFile { get { return Path.Combine(AppPaths.Local, "rules-done.txt"); } }

        void LoadDone()
        {
            if (doneLoaded) return;
            doneLoaded = true;
            try
            {
                if (!File.Exists(DoneFile)) return;
                foreach (string k in File.ReadAllLines(DoneFile)) if (k.Length > 0 && done.Add(k)) doneOrder.Add(k);
            }
            catch (Exception ex) { Log.Error("reading the list of handled files", ex); }
        }

        /// <summary>Remember a handled file (under the lock). False if it was handled before. The oldest quarter goes when the list is full.</summary>
        bool MarkDone(string key)
        {
            LoadDone();
            if (!done.Add(key)) return false;
            doneOrder.Add(key);
            doneDirty = true;
            if (doneOrder.Count > MaxDone)
            {
                foreach (string k in doneOrder.Take(MaxDone / 4)) done.Remove(k);
                doneOrder.RemoveRange(0, MaxDone / 4);
            }
            return true;
        }

        void SaveDone()
        {
            string[] keys;
            lock (gate)
            {
                if (!doneDirty) return;
                doneDirty = false;
                keys = doneOrder.ToArray();
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DoneFile));
                File.WriteAllLines(DoneFile, keys);
            }
            catch (Exception ex) { Log.Error("remembering handled files", ex); }
        }

        /// <summary>Forget which files were handled (Privacy and data): rules that look at existing files may take them again.</summary>
        public void ForgetHandled()
        {
            lock (gate) { done.Clear(); doneOrder.Clear(); doneLoaded = true; doneDirty = true; }
            SaveDone();
        }

        /// <summary>Run a rule on the files already there, now. Returns how many files it took.</summary>
        public int RunNow(Rule rule)
        {
            if (rule.Problem() != null) throw new InvalidOperationException(rule.Problem());
            List<string> files = Existing(rule);
            if (files.Count > 0) Run(rule, files.ToArray());
            return files.Count;
        }

        public void Dispose()
        {
            Timer t;
            lock (gate) { t = timer; timer = null; }
            if (t != null) t.Dispose();
            DisposeAll(DetachWatchers());
        }

        List<FileSystemWatcher> DetachWatchers()
        {
            lock (gate)
            {
                var list = new List<FileSystemWatcher>(watchers);
                watchers.Clear();
                foreach (FileSystemWatcher w in list) try { w.EnableRaisingEvents = false; } catch (Exception) { }
                return list;
            }
        }

        static void DisposeAll(List<FileSystemWatcher> list)
        {
            foreach (FileSystemWatcher w in list) try { w.Dispose(); } catch (Exception) { }
        }
    }
}
