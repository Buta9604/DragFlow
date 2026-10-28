using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DragFlow
{
    public enum FileKind { Image, Pdf, Video, Audio, Word, Text, Archive, Folder, Other, Mixed, Web }

    /// <summary>
    /// Every file operation DragFlow can do, split across FileOps*.cs by area.
    /// No UI in here so it can all be tested headless. Every change is recorded in the undo journal.
    /// </summary>
    public static partial class FileOps
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);

        static readonly string[] GdiImageExts = { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff" };
        /// <summary>Formats GDI+ cannot read; Windows' own codecs (WIC) decode them when the codec is installed.</summary>
        static readonly string[] WicImageExts = { ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp" };
        static readonly string[] VideoExts = { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v", ".flv" };
        static readonly string[] AudioExts = { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus" };
        static readonly string[] ArchiveExts = { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab" };
        /// <summary>Word, Excel and PowerPoint documents (and the OpenDocument kinds).</summary>
        public static readonly string[] OfficeExts = { ".doc", ".docx", ".rtf", ".odt", ".xls", ".xlsx", ".xlsm", ".ods", ".ppt", ".pptx", ".odp" };

        static readonly string[] TextExts =
        {
            ".txt", ".md", ".csv", ".tsv", ".json", ".log", ".xml", ".css", ".js", ".ts", ".tsx", ".jsx",
            ".cs", ".py", ".java", ".c", ".cpp", ".h", ".go", ".rs", ".rb", ".php", ".sql", ".ini", ".yaml", ".yml", ".toml", ".bat", ".ps1", ".sh"
        };

        public static FileKind Classify(string path)
        {
            if (Directory.Exists(path)) return FileKind.Folder;
            return ClassifyName(path);
        }

        /// <summary>Kind from the name alone (no disk access), e.g. for an e-mail attachment that is not saved yet.</summary>
        public static FileKind ClassifyName(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (GdiImageExts.Contains(ext) || WicImageExts.Contains(ext)) return FileKind.Image;
            if (ext == ".pdf") return FileKind.Pdf;
            if (VideoExts.Contains(ext)) return FileKind.Video;
            if (AudioExts.Contains(ext)) return FileKind.Audio;
            if (OfficeExts.Contains(ext)) return FileKind.Word;
            if (ArchiveExts.Contains(ext)) return FileKind.Archive;
            if (WebCapture.IsWebFile(name)) return FileKind.Web;
            if (TextExts.Contains(ext)) return FileKind.Text;
            return FileKind.Other;
        }

        /// <summary>One kind for a whole selection: the shared kind, or Mixed.</summary>
        public static FileKind KindOf(IList<string> paths)
        {
            if (paths == null || paths.Count == 0) return FileKind.Other;
            FileKind k = Classify(paths[0]);
            return paths.All(p => Classify(p) == k) ? k : FileKind.Mixed;
        }

        public static bool NeedsWic(string path) { return WicImageExts.Contains(Path.GetExtension(path).ToLowerInvariant()); }

        // ---------- helpers ----------

        public static string UniquePath(string dir, string name, string ext)
        {
            string p = Path.Combine(dir, name + ext);
            for (int i = 1; File.Exists(p) || Directory.Exists(p); i++)
                p = Path.Combine(dir, name + " (" + i + ")" + ext);
            UndoLog.Reserve(p); // DragFlow is about to write here: folder rules must not react to it
            return p;
        }

        public static string ParentOf(string path)
        {
            return Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? path;
        }

        public static string NameOf(string path)
        {
            return Path.GetFileName(path.TrimEnd('\\', '/'));
        }

        static void NaturalSort(List<string> paths)
        {
            paths.Sort(StrCmpLogicalW);
        }

        /// <summary>Files of one kind from the given paths; folders contribute their top-level files, naturally sorted.</summary>
        public static List<string> FilesOfKind(IEnumerable<string> paths, FileKind kind)
        {
            var result = new List<string>();
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    var files = Directory.GetFiles(p).Where(f => Classify(f) == kind).ToList();
                    NaturalSort(files);
                    result.AddRange(files);
                }
                else if (Classify(p) == kind) result.Add(p);
            }
            return result;
        }

        public static List<string> ImagesIn(IEnumerable<string> paths) { return FilesOfKind(paths, FileKind.Image); }

        /// <summary>Does any of these folders hold an image near the top? Looks at no more than 300 entries per folder.</summary>
        public static bool HasImagesQuick(IEnumerable<string> folders)
        {
            foreach (string f in folders)
            {
                try
                {
                    if (Directory.EnumerateFiles(f).Take(300).Any(x => Classify(x) == FileKind.Image)) return true;
                }
                catch (Exception) { }
            }
            return false;
        }

        /// <summary>Count files under a folder, stopping at limit+1 (for "this is big, continue?" questions).</summary>
        public static int CountFiles(string folder, bool recursive, int limit)
        {
            try
            {
                return (recursive ? FilesUnder(folder) : Directory.EnumerateFiles(folder)).Take(limit + 1).Count();
            }
            catch (Exception) { return 0; }
        }

        /// <summary>Every sub-folder, never following junctions or symbolic links out of the folder (or round in loops).</summary>
        public static IEnumerable<string> FoldersUnder(string folder)
        {
            var stack = new Stack<string>();
            stack.Push(folder);
            while (stack.Count > 0)
            {
                string[] subs;
                try { subs = Directory.GetDirectories(stack.Pop()); }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }
                foreach (string d in subs)
                {
                    FileAttributes a;
                    try { a = File.GetAttributes(d); } catch (Exception) { continue; }
                    if ((a & FileAttributes.ReparsePoint) != 0) continue;
                    yield return d;
                    stack.Push(d);
                }
            }
        }

        /// <summary>Every file in the folder and its real sub-folders (junctions and links are not followed).</summary>
        public static IEnumerable<string> FilesUnder(string folder)
        {
            foreach (string d in new[] { folder }.Concat(FoldersUnder(folder)))
            {
                string[] files;
                try { files = Directory.GetFiles(d); }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }
                foreach (string f in files) yield return f;
            }
        }

        public static string Plural(int n, string one, string many)
        {
            return n + " " + (n == 1 ? one : many);
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return (u == 0 ? v.ToString("0") : v.ToString(v < 10 ? "0.0" : "0")) + " " + units[u];
        }

        // ---------- rename / move (all undoable) ----------

        public static int ApplyMoves(IList<KeyValuePair<string, string>> moves)
        {
            Guard.Modifiable(moves.Select(m => m.Key));
            List<KeyValuePair<string, string>> done = null;
            using (UndoLog.Begin("Move"))
            {
                try
                {
                    List<string> problems;
                    done = MoveSet(moves, out problems);
                }
                finally
                {
                    if (done != null && done.Count > 0) UndoLog.PushMoves(done); // even after Cancel, what was moved can be undone
                }
            }
            return done.Count;
        }

        static bool Exists(string p) { return File.Exists(p) || Directory.Exists(p); }

        /// <summary>
        /// Carry out a set of moves without overwriting anything. A move whose target is taken by something outside
        /// the set is skipped (and so is anything that would need its place). When moves depend on each other
        /// (swaps, a→b with b→c) everything goes through temporary names first. Returns the moves that happened.
        /// </summary>
        public static List<KeyValuePair<string, string>> MoveSet(IList<KeyValuePair<string, string>> moves, out List<string> problems)
        {
            problems = new List<string>();
            var todo = moves.Where(m => !string.Equals(m.Key, m.Value, StringComparison.Ordinal)).ToList();
            var sources = new HashSet<string>(todo.Select(m => m.Key), StringComparer.OrdinalIgnoreCase);
            bool chained = todo.Any(m => !string.Equals(m.Key, m.Value, StringComparison.OrdinalIgnoreCase) && sources.Contains(m.Value));
            var done = new List<KeyValuePair<string, string>>();
            if (!chained)
            {
                foreach (var m in todo)
                {
                    Job.CheckCancel();
                    bool caseOnly = string.Equals(m.Key, m.Value, StringComparison.OrdinalIgnoreCase);
                    if (!caseOnly && Exists(m.Value)) { problems.Add(NameOf(m.Value) + ": that name is taken"); continue; }
                    if (!Exists(m.Key)) { problems.Add(NameOf(m.Key) + ": no longer there"); continue; }
                    MoveEntry(m.Key, m.Value);
                    done.Add(m);
                }
                return done;
            }

            // Drop moves that cannot happen, repeatedly: a target taken by an outsider, a duplicate target, or a
            // target that is the source of a move that is itself not happening.
            var keep = todo.Where(m => Exists(m.Key)).ToList();
            bool changed = true;
            while (changed)
            {
                changed = false;
                var moving = new HashSet<string>(keep.Select(m => m.Key), StringComparer.OrdinalIgnoreCase);
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in keep.ToList())
                {
                    bool outsider = Exists(m.Value) && !moving.Contains(m.Value);
                    if (outsider || !targets.Add(m.Value))
                    {
                        problems.Add(NameOf(m.Value) + ": that name is taken");
                        keep.Remove(m);
                        changed = true;
                    }
                }
            }
            var staged = new List<string[]>(); // from, temporary, to
            try
            {
                foreach (var m in keep)
                {
                    Job.CheckCancel();
                    string temp = Path.Combine(ParentOf(m.Key), ".dragflow-" + Guid.NewGuid().ToString("N").Substring(0, 12));
                    MoveEntry(m.Key, temp);
                    staged.Add(new[] { m.Key, temp, m.Value });
                }
                foreach (string[] st in staged)
                {
                    MoveEntry(st[1], st[2]);
                    done.Add(new KeyValuePair<string, string>(st[0], st[2]));
                    st[1] = null;
                }
            }
            finally
            {
                // Anything still on a temporary name (an error or Cancel in between) goes back where it was.
                foreach (string[] st in staged)
                {
                    if (st[1] == null || !Exists(st[1])) continue;
                    try { MoveEntry(st[1], Exists(st[0]) ? UniquePath(ParentOf(st[0]), Path.GetFileNameWithoutExtension(st[0]), Path.GetExtension(st[0])) : st[0]); }
                    catch (Exception ex) { Log.Error("putting back after an interrupted rename", ex); }
                }
            }
            return done;
        }

        public static void RenameOne(string path, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("That name has characters Windows does not allow.");
            string dest = Path.Combine(ParentOf(path), newName.Trim());
            using (UndoLog.Begin("Rename"))
                if (ApplyMoves(new[] { new KeyValuePair<string, string>(path, dest) }) == 0)
                    throw new IOException("Something called \"" + newName + "\" already exists there.");
        }

        /// <summary>Move a file or folder; folders going to another drive are copied, then the original is removed.</summary>
        public static void MoveEntry(string from, string to)
        {
            if (Directory.Exists(from))
            {
                if (!string.Equals(from, to, StringComparison.Ordinal) && string.Equals(from.TrimEnd('\\'), to.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    // Directory.Move refuses a change of letter case only: go through a temporary name.
                    string temp = from.TrimEnd('\\') + ".dragflow-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Directory.Move(from, temp);
                    Directory.Move(temp, to);
                    return;
                }
                bool sameRoot = string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase);
                if (sameRoot) Directory.Move(from, to);
                else MoveFolderAcrossDrives(from, to);
            }
            else File.Move(from, to);
        }

        /// <summary>Copy the whole folder first and only remove the original once every file arrived.</summary>
        public static void MoveFolderAcrossDrives(string from, string to)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(from, to, false);
            long a = FolderBytes(from), b = FolderBytes(to);
            if (a != b) throw new IOException("The copy of the folder is incomplete, so the original was kept.");
            Directory.Delete(from, true); // the complete copy now lives at the destination
        }

        static long FolderBytes(string dir)
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }

        /// <summary>Move items into a folder, renaming on clashes. Undoable.</summary>
        public static int MoveTo(IList<string> paths, string destDir)
        {
            List<string> unused;
            return MoveTo(paths, destDir, out unused);
        }

        /// <summary>Move items into a folder; <paramref name="newPaths"/> says where each one ended up.</summary>
        public static int MoveTo(IList<string> paths, string destDir, out List<string> newPaths)
        {
            newPaths = new List<string>();
            if (!Directory.Exists(destDir)) throw new DirectoryNotFoundException("The folder " + destDir + " does not exist.");
            string full = Path.GetFullPath(destDir).TrimEnd('\\');
            Guard.Modifiable(new[] { full });
            var moves = new List<KeyValuePair<string, string>>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in paths)
            {
                string src = Path.GetFullPath(p).TrimEnd('\\');
                if (string.Equals(ParentOf(src), full, StringComparison.OrdinalIgnoreCase)) continue; // already there
                if (full.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(full, src, StringComparison.OrdinalIgnoreCase)) continue; // into itself
                bool dir = Directory.Exists(src);
                string stem = dir ? NameOf(src) : Path.GetFileNameWithoutExtension(src), ext = dir ? "" : Path.GetExtension(src);
                string dest = UniquePath(full, stem, ext);
                for (int i = 1; taken.Contains(dest); i++) dest = UniquePath(full, stem + " (" + i + ")", ext);
                taken.Add(dest);
                moves.Add(new KeyValuePair<string, string>(src, dest));
            }
            if (moves.Count == 0) throw new InvalidOperationException("Already in that folder.");
            using (UndoLog.Begin("Move to " + NameOf(full)))
            {
                int n = ApplyMoves(moves);
                newPaths.AddRange(moves.Select(m => m.Value).Where(v => File.Exists(v) || Directory.Exists(v)));
                return n;
            }
        }
    }
}
