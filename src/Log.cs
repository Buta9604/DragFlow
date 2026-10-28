using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DragFlow
{
    /// <summary>
    /// Always-on, low-cost diagnostics in %LOCALAPPDATA%\DragFlow\logs (two files of at most 1 MB each).
    /// Writes are queued and flushed by a background thread, so logging never slows the mouse.
    /// File names and paths are left out unless the user opts in (Settings) or DRAGFLOW_LOG=1 is set.
    /// </summary>
    public static class Log
    {
        const long MaxBytes = 1024 * 1024;
        static readonly object gate = new object();
        static StringBuilder pending = new StringBuilder();
        static readonly AutoResetEvent signal = new AutoResetEvent(false);
        static Thread writer;

        /// <summary>Extra detail (every mouse-down, every state change). DRAGFLOW_LOG=1 turns it on.</summary>
        public static bool Verbose = Environment.GetEnvironmentVariable("DRAGFLOW_LOG") == "1";
        /// <summary>Include file and folder paths in the log.</summary>
        public static bool IncludePaths = Environment.GetEnvironmentVariable("DRAGFLOW_LOG") == "1";

        public static string Dir { get { return Path.Combine(AppPaths.Local, "logs"); } }
        /// <summary>"dragflow" for the app; the guard writes its own file so the two never collide.</summary>
        public static string Name = "dragflow";
        public static string FilePath { get { return Path.Combine(Dir, Name + ".log"); } }

        public static void Write(string text)
        {
            lock (gate) pending.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(text).Append("\r\n");
            EnsureWriter();
            signal.Set();
        }

        public static void Debug(string text) { if (Verbose) Write(text); }

        public static void Error(string context, Exception ex)
        {
            Write("ERROR " + context + ": " + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + "\r\n" + ex.StackTrace));
        }

        /// <summary>A path as it should appear in the log: in full only when the user allowed it.</summary>
        public static string P(string path)
        {
            if (path == null) return "null";
            if (IncludePaths) return path;
            string ext = Path.GetExtension(path.TrimEnd('\\'));
            return "<" + (ext.Length > 0 ? ext.ToLowerInvariant() : "item") + ">";
        }

        public static string P(string[] paths)
        {
            if (paths == null) return "null";
            return paths.Length + " item(s) " + string.Join(" ", paths.Take(5).Select(P).ToArray());
        }

        static void EnsureWriter()
        {
            if (writer != null) return;
            lock (gate)
            {
                if (writer != null) return;
                writer = new Thread(Loop) { IsBackground = true, Name = "DragFlow log", Priority = ThreadPriority.BelowNormal };
                writer.Start();
            }
        }

        static void Loop()
        {
            while (true)
            {
                signal.WaitOne(500);
                Flush();
            }
        }

        /// <summary>Write anything queued now (also used by the crash handler).</summary>
        public static void Flush()
        {
            string text;
            lock (gate)
            {
                if (pending.Length == 0) return;
                text = pending.ToString();
                pending = new StringBuilder();
            }
            try
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = Path.Combine(Dir, Name + ".1.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, text, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
