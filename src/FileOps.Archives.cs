using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace DragFlow
{
    public static partial class FileOps
    {
        public static string Zip(IList<string> paths)
        {
            string dir = ParentOf(paths[0]);
            string name = paths.Count == 1
                ? (Directory.Exists(paths[0]) ? NameOf(paths[0]) : Path.GetFileNameWithoutExtension(paths[0]))
                : "Archive";
            string zipPath = UniquePath(dir, name, ".zip");
            using (UndoLog.Begin("Zip"))
            {
                bool ok = false;
                try
                {
                    using (var fs = new FileStream(zipPath, FileMode.CreateNew))
                    using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        Job.SetTotal(paths.Count);
                        foreach (string p in paths)
                        {
                            Job.Step(NameOf(p));
                            if (Directory.Exists(p)) AddDirectory(za, p, NameOf(p));
                            else za.CreateEntryFromFile(p, Path.GetFileName(p), CompressionLevel.Optimal);
                            Job.Advance();
                        }
                    }
                    ok = true;
                }
                finally
                {
                    if (ok) UndoLog.Created(zipPath);
                    else TryRemovePartial(zipPath);
                }
            }
            return zipPath;
        }

        /// <summary>A half-written output after a failure or Cancel goes to the Recycle Bin.</summary>
        static void TryRemovePartial(string path)
        {
            try { if (File.Exists(path) || Directory.Exists(path)) UndoLog.Discard(path); }
            catch (Exception ex) { Log.Error("remove partial output", ex); }
        }

        static void AddDirectory(ZipArchive za, string dir, string entryRoot)
        {
            za.CreateEntry(entryRoot + "/");
            foreach (string sub in Directory.GetDirectories(dir))
                AddDirectory(za, sub, entryRoot + "/" + Path.GetFileName(sub));
            foreach (string f in Directory.GetFiles(dir))
            {
                Job.Step(Path.GetFileName(f));
                za.CreateEntryFromFile(f, entryRoot + "/" + Path.GetFileName(f), CompressionLevel.Optimal);
            }
        }

        /// <summary>
        /// Extract an archive. toNewFolder=false puts the contents next to the archive, true into a new folder named
        /// after it. Never overwrites: clashing files get " (1)" names. Entries that try to escape the target are refused.
        /// </summary>
        public static string Extract(string archive, bool toNewFolder)
        {
            string parent = ParentOf(archive);
            string stem = Path.GetFileNameWithoutExtension(archive);
            if (stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) stem = Path.GetFileNameWithoutExtension(stem);
            string dest = toNewFolder ? UniquePath(parent, stem, "") : parent;
            Guard.Modifiable(new[] { dest });
            using (UndoLog.Begin("Extract"))
            {
                // Unpack into a private hidden folder first, then move the results into place: only what was really
                // extracted is recorded for undo, and nothing already there is mixed into or overwritten.
                string temp = Path.Combine(parent, ".dragflow-extract-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(temp);
                try { File.SetAttributes(temp, File.GetAttributes(temp) | FileAttributes.Hidden); } catch (Exception) { }
                try
                {
                    if (Path.GetExtension(archive).ToLowerInvariant() == ".zip") ExtractZip(archive, temp);
                    else ExtractWithTar(archive, temp, true);
                    if (toNewFolder)
                    {
                        Directory.Move(temp, dest);
                        temp = null;
                        var di = new DirectoryInfo(dest);
                        di.Attributes &= ~FileAttributes.Hidden;
                        UndoLog.Created(dest);
                    }
                    else
                    {
                        foreach (string entry in Directory.GetFileSystemEntries(temp))
                        {
                            bool dir = Directory.Exists(entry);
                            string name = Path.GetFileName(entry);
                            string target = UniquePath(parent, dir ? name : Path.GetFileNameWithoutExtension(name), dir ? "" : Path.GetExtension(name));
                            MoveEntry(entry, target);
                            UndoLog.Created(target);
                        }
                    }
                }
                finally
                {
                    if (temp != null && Directory.Exists(temp))
                    {
                        if (!Directory.EnumerateFileSystemEntries(temp).Any()) Directory.Delete(temp);
                        else TryRemovePartial(temp);
                    }
                }
            }
            return dest;
        }

        static void ExtractZip(string archive, string dest)
        {
            string root = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            using (ZipArchive za = ZipFile.OpenRead(archive))
            {
                Job.SetTotal(za.Entries.Count);
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    Job.Step(e.Name);
                    Job.Advance();
                    string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The archive contains an unsafe path (" + e.FullName + "); nothing more was extracted.");
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target))
                        target = UniquePath(Path.GetDirectoryName(target), Path.GetFileNameWithoutExtension(target), Path.GetExtension(target));
                    e.ExtractToFile(target);
                }
            }
        }

        /// <summary>Windows' built-in tar (bsdtar) handles 7z, rar, tar, gz, bz2, xz and cab; it refuses ".." paths itself.</summary>
        static void ExtractWithTar(string archive, string dest, bool freshFolder)
        {
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (!File.Exists(tar)) throw new InvalidOperationException("This Windows version has no tar.exe, so only .zip files can be extracted.");
            RunTool(tar, "-xf \"" + archive + "\" -C \"" + dest + "\"" + (freshFolder ? "" : " -k"), "Could not extract");
        }

        /// <summary>Run a command-line tool, killing it if the user cancels; throws with its error output on failure.</summary>
        internal static void RunTool(string exe, string args, string failure, string workDir = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            if (workDir != null) psi.WorkingDirectory = workDir;
            using (Process p = Process.Start(psi))
            {
                var err = new System.Text.StringBuilder();
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                while (!p.WaitForExit(200))
                {
                    Job j = Job.Current;
                    if (j != null && j.Cancelled)
                    {
                        try { p.Kill(); } catch (Exception) { }
                        p.WaitForExit(2000);
                        throw new OperationCanceledException();
                    }
                }
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException(failure + ": " + err.ToString().Trim());
            }
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
    }
}
