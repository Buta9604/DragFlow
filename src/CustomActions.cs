using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DragFlow
{
    /// <summary>One user-defined action: "Label | file types | program | arguments".</summary>
    public sealed class CustomAction
    {
        public string Label, Types, Program, Args;

        /// <summary>True when every dragged item fits the type list (* = anything, folder = folders, .ext = files).</summary>
        public bool Matches(string[] paths)
        {
            if (paths == null || paths.Length == 0) return Types.Trim() == "*";
            string[] tokens = Types.Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToArray();
            if (tokens.Contains("*")) return true;
            return paths.All(p =>
                Directory.Exists(p) ? tokens.Contains("folder")
                                    : tokens.Contains(Path.GetExtension(p).ToLowerInvariant()));
        }
    }

    /// <summary>Loads and runs custom actions from %APPDATA%\DragFlow\actions.ini (re-read on every drag, no restart needed).</summary>
    public static class CustomActions
    {
        /// <summary>Tests point this at a temporary file so they never read the user's real actions.</summary>
        public static string OverridePath;

        public static string FilePath
        {
            get { return OverridePath ?? AppPaths.RoamingFile("actions.ini"); }
        }

        const string Template =
@"# DragFlow custom actions. They appear in the dial under ""Custom"" for files they match.
# One action per line:   Label | file types | program | arguments
#   file types : * for anything, folder for folders, or extensions like .png,.jpg
#   arguments  : {file} = run once for each dragged item, {files} = all items at once, {dir} = folder of the first item
# Lines starting with # are ignored. Save the file and drag again; no restart needed.

Open in Notepad | .txt,.md,.log,.csv,.json,.ini,.xml | notepad.exe | ""{file}""
# Open in VS Code | * | code | ""{file}""
# Command prompt here | folder | cmd.exe | /k cd /d ""{file}""
# Copy to backup drive | * | robocopy.exe | ""{dir}"" ""D:\Backup"" ""{file}""
";

        /// <summary>Write the actions back (Settings > Custom actions), keeping the explanation at the top.</summary>
        public static void Save(IList<CustomAction> actions)
        {
            var lines = Template.Split('\n').Select(l => l.TrimEnd('\r')).TakeWhile(l => l.StartsWith("#") || l.Length == 0).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add("");
            foreach (CustomAction a in actions)
                lines.Add(a.Label.Replace("|", "/") + " | " + a.Types + " | " + a.Program + " | " + a.Args);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines.ToArray());
        }

        public static void EnsureFile()
        {
            if (File.Exists(FilePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, Template);
        }

        static readonly object gate = new object();
        static string cachedPath;
        static DateTime cachedStamp;
        static long cachedLength = -1;
        static List<CustomAction> cached = new List<CustomAction>();

        /// <summary>The actions in the file; only re-read when the file changed (checked on every drag).</summary>
        public static List<CustomAction> Load()
        {
            lock (gate)
            {
                try
                {
                    string path = FilePath;
                    DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    long length = stamp == DateTime.MinValue ? -1 : new FileInfo(path).Length;
                    // Two saves within one clock tick keep the same time stamp: the length tells them apart too.
                    if (path == cachedPath && stamp == cachedStamp && length == cachedLength) return new List<CustomAction>(cached);
                    cached = stamp == DateTime.MinValue ? new List<CustomAction>() : Parse(File.ReadAllLines(path));
                    cachedPath = path; cachedStamp = stamp; cachedLength = length;
                    return new List<CustomAction>(cached);
                }
                catch (IOException) { return new List<CustomAction>(); }
                catch (UnauthorizedAccessException) { return new List<CustomAction>(); }
            }
        }

        public static List<CustomAction> Parse(IEnumerable<string> lines)
        {
            var list = new List<CustomAction>();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split(new[] { '|' }, 4);
                if (parts.Length < 3) continue;
                var a = new CustomAction
                {
                    Label = parts[0].Trim(),
                    Types = parts[1].Trim(),
                    Program = parts[2].Trim(),
                    Args = parts.Length > 3 ? parts[3].Trim() : "\"{file}\""
                };
                if (a.Label.Length > 0 && a.Program.Length > 0 && list.All(x => x.Label != a.Label)) list.Add(a);
            }
            return list;
        }

        /// <summary>The command lines that running the action would start (one per item when {file} is used).</summary>
        public static List<string> Expand(CustomAction a, string[] paths)
        {
            string dir = Path.GetDirectoryName(paths[0].TrimEnd('\\')) ?? paths[0];
            string all = string.Join(" ", paths.Select(p => "\"" + p + "\"").ToArray());
            Func<string, string> fill = delegate (string file)
            {
                // {files} is already quoted; avoid doubling quotes if the user wrote "{files}"
                return a.Args.Replace("\"{files}\"", all).Replace("{files}", all).Replace("{file}", file).Replace("{dir}", dir);
            };
            return a.Args.Contains("{file}") ? paths.Select(fill).ToList() : new List<string> { fill(paths[0]) };
        }

        public static void Run(string label, string[] paths)
        {
            CustomAction a = Load().FirstOrDefault(x => x.Label == label);
            if (a == null) throw new InvalidOperationException("The custom action \"" + label + "\" is no longer in actions.ini.");
            string dir = Path.GetDirectoryName(paths[0].TrimEnd('\\')) ?? paths[0];
            foreach (string args in Expand(a, paths))
            {
                Log.Write("custom action \"" + a.Label + "\"");
                Process.Start(new ProcessStartInfo(a.Program, args) { UseShellExecute = true, WorkingDirectory = dir });
            }
        }
    }
}
