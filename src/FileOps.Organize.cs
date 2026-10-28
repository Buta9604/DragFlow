using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragFlow
{
    public sealed class SizeEntry
    {
        public string Name;
        public long Bytes;
        public bool IsFolder;
    }

    public static partial class FileOps
    {
        const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000, RecallOnOpen = (FileAttributes)0x40000;

        /// <summary>OneDrive/cloud "online-only" placeholder: reading it would download it.</summary>
        public static bool IsOnlineOnly(string file)
        {
            try
            {
                FileAttributes a = File.GetAttributes(file);
                return (a & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0;
            }
            catch (Exception) { return false; }
        }

        public static int SortByType(string folder)
        {
            Guard.Organizable(folder);
            using (UndoLog.Begin("Sort by type"))
            {
                var moves = new List<KeyValuePair<string, string>>();
                foreach (string f in Directory.GetFiles(folder))
                {
                    string bucket;
                    switch (Classify(f))
                    {
                        case FileKind.Image: bucket = "Images"; break;
                        case FileKind.Video: bucket = "Videos"; break;
                        case FileKind.Audio: bucket = "Audio"; break;
                        case FileKind.Pdf:
                        case FileKind.Word:
                        case FileKind.Text: bucket = "Documents"; break;
                        case FileKind.Archive: bucket = "Archives"; break;
                        default: bucket = "Other"; break;
                    }
                    string dir = Path.Combine(folder, bucket);
                    if (!Directory.Exists(dir)) UndoLog.EnsureFolder(dir);
                    moves.Add(new KeyValuePair<string, string>(f, Path.Combine(dir, Path.GetFileName(f))));
                }
                return ApplyMoves(moves);
            }
        }

        /// <summary>Files into "2026-10" style folders, by photo date for images and modified date otherwise.</summary>
        public static int SortByDate(string folder)
        {
            Guard.Organizable(folder);
            using (UndoLog.Begin("Sort by date"))
            {
                var moves = new List<KeyValuePair<string, string>>();
                foreach (string f in Directory.GetFiles(folder))
                {
                    Job.CheckCancel();
                    DateTime d = Classify(f) == FileKind.Image && !IsOnlineOnly(f) ? PhotoDate(f) : File.GetLastWriteTime(f);
                    string dir = Path.Combine(folder, d.ToString("yyyy-MM"));
                    if (!Directory.Exists(dir)) UndoLog.EnsureFolder(dir);
                    moves.Add(new KeyValuePair<string, string>(f, Path.Combine(dir, Path.GetFileName(f))));
                }
                return ApplyMoves(moves);
            }
        }

        public static int Flatten(string folder)
        {
            Guard.Organizable(folder);
            using (UndoLog.Begin("Flatten"))
            {
                var moves = new List<KeyValuePair<string, string>>();
                var taken = new HashSet<string>(Directory.GetFiles(folder).Select(f => Path.GetFileName(f).ToLowerInvariant()));
                foreach (string f in FilesUnder(folder).ToList())
                {
                    if (string.Equals(ParentOf(f), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileName(f), stem = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
                    for (int i = 1; taken.Contains(name.ToLowerInvariant()); i++) name = stem + " (" + i + ")" + ext;
                    taken.Add(name.ToLowerInvariant());
                    moves.Add(new KeyValuePair<string, string>(f, Path.Combine(folder, name)));
                }
                int n = ApplyMoves(moves);
                RemoveEmptyFolders(folder);
                return n;
            }
        }

        /// <summary>Remove empty sub-folders (never the folder itself). Undo recreates them. Returns how many went.</summary>
        public static int RemoveEmptyFolders(string folder)
        {
            Guard.Organizable(folder);
            int n = 0;
            using (UndoLog.Begin("Remove empty folders"))
                foreach (string d in FoldersUnder(folder).ToList().OrderByDescending(x => x.Length))
                {
                    Job.CheckCancel();
                    if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                    {
                        Directory.Delete(d); // empty, so nothing is lost; undo makes it again
                        UndoLog.RemovedFolder(d);
                        n++;
                    }
                }
            return n;
        }

        /// <summary>
        /// Find identical files (same size, then same SHA-256) anywhere inside the folder and move every copy but the
        /// first into a "Duplicates" sub-folder. Nothing is deleted, and it can be undone. Online-only (cloud) files are
        /// skipped so nothing gets downloaded; <paramref name="skipped"/> says how many.
        /// </summary>
        public static int MoveDuplicates(string folder, out long bytesFreed, out int skipped)
        {
            Guard.Organizable(folder);
            bytesFreed = 0;
            string dupDir = Path.Combine(folder, "Duplicates");
            var all = FilesUnder(folder)
                .Where(f => !f.StartsWith(dupDir + "\\", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var files = all.Where(f => !IsOnlineOnly(f)).ToList();
            skipped = all.Count - files.Count;
            NaturalSort(files);

            var moves = new List<KeyValuePair<string, string>>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bySizeGroups = files.GroupBy(f => new FileInfo(f).Length).Where(g => g.Count() > 1 && g.Key > 0).ToList();
            Job.SetTotal(bySizeGroups.Sum(g => g.Count()));
            long freed = 0;
            using (UndoLog.Begin("Move duplicates"))
            {
                foreach (var bySize in bySizeGroups)
                {
                    var hashed = new List<KeyValuePair<string, string>>();
                    foreach (string f in bySize)
                    {
                        Job.Step(Path.GetFileName(f));
                        Job.Advance();
                        try { hashed.Add(new KeyValuePair<string, string>(f, Sha256(f))); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    foreach (var byHash in hashed.GroupBy(h => h.Value).Where(g => g.Count() > 1))
                    {
                        foreach (var extra in byHash.Skip(1))
                        {
                            if (!Directory.Exists(dupDir)) UndoLog.EnsureFolder(dupDir);
                            string name = Path.GetFileNameWithoutExtension(extra.Key), ext = Path.GetExtension(extra.Key);
                            string dest = Path.Combine(dupDir, name + ext);
                            for (int i = 1; File.Exists(dest) || claimed.Contains(dest); i++) dest = Path.Combine(dupDir, name + " (" + i + ")" + ext);
                            claimed.Add(dest);
                            moves.Add(new KeyValuePair<string, string>(extra.Key, dest));
                            freed += bySize.Key;
                        }
                    }
                }
                bytesFreed = freed;
                return ApplyMoves(moves);
            }
        }

        public static int MoveDuplicates(string folder, out long bytesFreed)
        {
            int skipped;
            return MoveDuplicates(folder, out bytesFreed, out skipped);
        }

        /// <summary>What takes up the space: immediate children with their total sizes, biggest first.
        /// Only reads sizes, so cloud placeholders are not downloaded.</summary>
        public static List<SizeEntry> SizeReport(string folder)
        {
            var list = new List<SizeEntry>();
            string[] dirs = SafeDirs(folder);
            Job.SetTotal(dirs.Length);
            foreach (string d in dirs)
            {
                Job.Step(Path.GetFileName(d));
                list.Add(new SizeEntry { Name = Path.GetFileName(d), Bytes = FolderSize(d), IsFolder = true });
                Job.Advance();
            }
            foreach (string f in SafeFiles(folder))
            {
                try { list.Add(new SizeEntry { Name = Path.GetFileName(f), Bytes = new FileInfo(f).Length }); }
                catch (IOException) { }
            }
            return list.OrderByDescending(e => e.Bytes).ToList();
        }

        static long FolderSize(string dir)
        {
            long total = 0;
            foreach (string f in SafeFiles(dir))
            {
                try { total += new FileInfo(f).Length; } catch (IOException) { }
            }
            foreach (string d in SafeDirs(dir))
            {
                Job.CheckCancel();
                if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) continue; // skip junctions
                total += FolderSize(d);
            }
            return total;
        }

        static string[] SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir); } catch (UnauthorizedAccessException) { return new string[0]; } catch (IOException) { return new string[0]; }
        }

        static string[] SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch (UnauthorizedAccessException) { return new string[0]; } catch (IOException) { return new string[0]; }
        }
    }
}
