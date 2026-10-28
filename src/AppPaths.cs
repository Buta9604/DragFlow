using System;
using System.IO.Compression;
using System.IO;

namespace DragFlow
{
    /// <summary>
    /// Where DragFlow keeps its data. Settings and user files live in %APPDATA%\DragFlow (roams with the account);
    /// logs, undo history and caches live in %LOCALAPPDATA%\DragFlow. "--profile &lt;dir&gt;" (and the tests) redirect both.
    /// </summary>
    public static class AppPaths
    {
        static string roaming, local;

        public static void UseProfile(string dir)
        {
            roaming = Path.Combine(dir, "roaming");
            local = Path.Combine(dir, "local");
        }

        public static void Reset() { roaming = null; local = null; chosen = null; chosenLooked = false; }

        /// <summary>True for the real data folders (not a test profile): only then does DragFlow touch the registry.</summary>
        public static bool IsDefault { get { return roaming == null; } }

        static string DefaultRoaming { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DragFlow"); } }

        /// <summary>A text file in the default folder naming the folder the settings really live in (e.g. inside OneDrive).</summary>
        static string PointerFile { get { return Path.Combine(roaming ?? DefaultRoaming, "location.txt"); } }

        static string chosen;
        static bool chosenLooked;

        public static string Roaming
        {
            get
            {
                if (roaming != null) return roaming;
                if (!chosenLooked)
                {
                    chosenLooked = true;
                    chosen = ResolvePointer(PointerFile, 10000);
                }
                return chosen ?? DefaultRoaming;
            }
        }

        /// <summary>The settings folder named in location.txt that could not be found at start (a USB stick, a share, OneDrive not ready), or null.</summary>
        public static string MissingSettingsFolder { get; internal set; }

        /// <summary>
        /// The folder location.txt points to. A drive or cloud folder may take a moment to appear after signing in, so this waits
        /// a little; if it is still missing, the default folder is used for this run and MissingSettingsFolder says so.
        /// </summary>
        internal static string ResolvePointer(string pointerFile, int waitMs)
        {
            string dir;
            try
            {
                if (!File.Exists(pointerFile)) return null;
                dir = File.ReadAllText(pointerFile).Trim();
            }
            catch (Exception) { return null; }
            if (dir.Length == 0) return null;
            long until = Clock.Now + waitMs;
            while (!Directory.Exists(dir))
            {
                if (Clock.Now >= until) { MissingSettingsFolder = dir; return null; }
                System.Threading.Thread.Sleep(250);
            }
            return dir;
        }

        static readonly string[] SettingsFiles = { "*.ini", "*.tsv", "*.txt" };

        /// <summary>Copy the settings into another folder and use it from now on (takes effect after a restart).</summary>
        public static void MoveSettingsTo(string dir)
        {
            dir = Path.GetFullPath(dir).TrimEnd('\\');
            if (string.Equals(dir, Roaming.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(dir);
            CopySettings(Roaming, dir, true);
            Directory.CreateDirectory(Path.GetDirectoryName(PointerFile));
            File.WriteAllText(PointerFile, dir);
            chosen = dir;
        }

        static void CopySettings(string from, string to, bool withScripts)
        {
            if (!Directory.Exists(from)) return;
            foreach (string pattern in SettingsFiles)
                foreach (string f in Directory.GetFiles(from, pattern))
                    if (!string.Equals(Path.GetFileName(f), "location.txt", StringComparison.OrdinalIgnoreCase)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            string lang = Path.Combine(from, "lang");
            if (Directory.Exists(lang))
            {
                Directory.CreateDirectory(Path.Combine(to, "lang"));
                foreach (string f in Directory.GetFiles(lang)) File.Copy(f, Path.Combine(to, "lang", Path.GetFileName(f)), true);
            }
            string scripts = Path.Combine(from, "scripts"); // the user's own scripts move with the settings (they are not in backups)
            if (withScripts && Directory.Exists(scripts))
            {
                Directory.CreateDirectory(Path.Combine(to, "scripts"));
                foreach (string f in Directory.GetFiles(scripts)) File.Copy(f, Path.Combine(to, "scripts", Path.GetFileName(f)), true);
            }
        }

        /// <summary>All settings as one zip file.</summary>
        public static void Backup(string zip)
        {
            string staging = Path.Combine(Path.GetTempPath(), "dragflow-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                CopySettings(Roaming, staging, false);
                if (File.Exists(zip)) File.Delete(zip);
                System.IO.Compression.ZipFile.CreateFromDirectory(staging, zip);
            }
            finally { try { Directory.Delete(staging, true); } catch (Exception) { } }
        }

        /// <summary>Put the settings from a backup zip in place (takes effect after a restart).</summary>
        public static void Restore(string zip)
        {
            using (var za = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                string root = Path.GetFullPath(Roaming).TrimEnd('\\') + "\\";
                Directory.CreateDirectory(Roaming);
                foreach (var e in za.Entries)
                {
                    // Only the settings files themselves: nothing in subfolders (a backup never carries scripts to run).
                    if (e.FullName.EndsWith("/") || e.FullName.Contains("/") && !e.FullName.StartsWith("lang/") || e.FullName.Contains("\\")) continue;
                    string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never outside the settings folder
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            }
        }

        public static string Local
        {
            get { return local ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragFlow"); }
        }

        public static string RoamingFile(string name) { return Path.Combine(Roaming, name); }

        /// <summary>Temporary working copies left behind by a crash or a power cut (older than a day) are removed at start.</summary>
        public static void CleanConvertFolder()
        {
            string d = Path.Combine(Local, "convert");
            if (!Directory.Exists(d)) return;
            foreach (string e in Directory.GetFileSystemEntries(d))
                try
                {
                    if (DateTime.Now - Directory.GetLastWriteTime(e) < TimeSpan.FromDays(1)) continue;
                    if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e);
                }
                catch (Exception) { }
        }

        /// <summary>A folder under %LOCALAPPDATA%\DragFlow, created on demand.</summary>
        public static string LocalDir(string name)
        {
            string d = Path.Combine(Local, name);
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
