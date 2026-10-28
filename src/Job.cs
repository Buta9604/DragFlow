using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragFlow
{
    /// <summary>
    /// A running action: progress, a Cancel flag and per-item failures. File operations report through the static
    /// helpers, which do nothing when no job is running (tests, quick calls), so their signatures stay simple.
    /// </summary>
    public sealed class Job
    {
        [ThreadStatic] static Job current;

        public readonly string Title;
        public volatile bool Cancelled;
        public volatile int Done, Total;
        public volatile string Status = "";
        public readonly List<string> Failures = new List<string>();
        public readonly long Started = Clock.Now;

        /// <summary>Asks the user a yes/no question (set by the app; tests and headless runs answer yes).</summary>
        public static Func<string, bool> Confirm = delegate { return true; };

        public Job(string title) { Title = title; }

        public static Job Current { get { return current; } }

        public sealed class Scope : IDisposable
        {
            readonly Job previous;
            public Scope(Job j) { previous = current; current = j; }
            public void Dispose() { current = previous; }
        }

        public Scope Enter() { return new Scope(this); }

        /// <summary>Throws if the user pressed Cancel; otherwise shows what is being worked on.</summary>
        public static void Step(string status)
        {
            Job j = current;
            if (j == null) return;
            if (j.Cancelled) throw new OperationCanceledException();
            if (status != null) j.Status = status;
        }

        public static void CheckCancel() { Step(null); }

        public static void SetTotal(int n) { Job j = current; if (j != null) { j.Total = n; j.Done = 0; } }
        public static void Advance() { Job j = current; if (j != null) j.Done++; }

        public static bool IsRunning { get { return current != null; } }

        /// <summary>Ask before scanning a lot of files. Returns false if the user said no.</summary>
        public static bool ConfirmLarge(int count, string what)
        {
            if (count <= LargeScan) return true;
            return Confirm("That is " + count.ToString("N0") + "+ files. " + what + " could take a while. Continue?");
        }

        public const int LargeScan = 2000;
    }

    /// <summary>Runs a body for each item; with a job running, a failing item is noted and the rest carry on.</summary>
    public static class Batch
    {
        public static void Each<T>(IList<T> items, Func<T, string> name, Action<T> body)
        {
            Job.SetTotal(items.Count);
            foreach (T item in items)
            {
                string n = name(item);
                Job.Step(n);
                try { body(item); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Job j = Job.Current;
                    if (j == null) throw;
                    lock (j.Failures) j.Failures.Add(n + ": " + ex.Message);
                    Log.Write("item failed: " + ex.Message);
                }
                Job.Advance();
            }
        }

        public static void Each(IList<string> files, Action<string> body)
        {
            Each(files, f => Path.GetFileName(f.TrimEnd('\\')), body);
        }

        /// <summary>Nothing was produced: report the first real failure if there was one, else the given reason.</summary>
        public static Exception Nothing(string reason)
        {
            Job j = Job.Current;
            if (j != null && j.Failures.Count > 0) return new InvalidOperationException(j.Failures[0]);
            return new InvalidOperationException(reason);
        }
    }

    /// <summary>Places DragFlow refuses to change: Windows, Program Files, drive roots and the profile folder itself.</summary>
    public static class Guard
    {
        static string[] Protected()
        {
            var list = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432")
            };
            return list.Where(p => !string.IsNullOrEmpty(p)).Select(p => p.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static bool IsProtected(string path)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); }
            catch (Exception) { return true; }
            foreach (string p in Protected())
                if (string.Equals(full, p, StringComparison.OrdinalIgnoreCase) || full.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Throws if any item sits inside a protected system folder.</summary>
        public static void Modifiable(IEnumerable<string> paths)
        {
            string bad = paths.FirstOrDefault(IsProtected);
            if (bad != null) throw new InvalidOperationException("DragFlow does not change files inside Windows or Program Files.");
        }

        /// <summary>Organising (sort, flatten, duplicates, remove empty) is refused for drive roots, the profile folder and system folders.</summary>
        public static void Organizable(string folder)
        {
            Modifiable(new[] { folder });
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            string root = Path.GetPathRoot(full).TrimEnd('\\');
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase) || string.Equals(full, profile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DragFlow will not reorganise a whole drive or your user folder. Pick a smaller folder.");
        }
    }
}
