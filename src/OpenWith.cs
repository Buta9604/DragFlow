using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>Opening, sharing, e-mailing and printing files through Windows' own facilities.</summary>
    public static class OpenWith
    {
        public sealed class App
        {
            public string Name, Path;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(int flags, int str, string assoc, string extra, StringBuilder outBuf, ref int outLen);

        const int ASSOCF_NOTRUNCATE = 0x20, ASSOCF_INIT_IGNOREUNKNOWN = 0x400;
        const int ASSOCSTR_COMMAND = 1, ASSOCSTR_EXECUTABLE = 2, ASSOCSTR_FRIENDLYAPPNAME = 4;

        static string Query(int what, string ext, string verb)
        {
            try
            {
                int len = 1024;
                var sb = new StringBuilder(len);
                int hr = AssocQueryString(ASSOCF_NOTRUNCATE | ASSOCF_INIT_IGNOREUNKNOWN, what, ext, verb, sb, ref len);
                return hr == 0 && sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Name of the app that opens this type by default ("Photos"), or null.</summary>
        public static string DefaultAppName(string ext) { return Query(ASSOCSTR_FRIENDLYAPPNAME, ext, null); }

        public static bool CanPrint(string ext) { return Query(ASSOCSTR_COMMAND, ext, "print") != null; }

        /// <summary>Apps you recently chose with "Open with" for this type (from Explorer's own list), default app excluded.</summary>
        public static List<App> RecentApps(string ext, int max)
        {
            var result = new List<App>();
            try
            {
                string def = Query(ASSOCSTR_EXECUTABLE, ext, null);
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\OpenWithList"))
                {
                    if (k == null) return result;
                    string mru = k.GetValue("MRUList") as string ?? "";
                    foreach (char c in mru)
                    {
                        if (result.Count >= max) break;
                        string exe = k.GetValue(c.ToString()) as string;
                        if (string.IsNullOrEmpty(exe)) continue;
                        string path = Resolve(exe);
                        if (path == null) continue;
                        if (def != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(def), StringComparison.OrdinalIgnoreCase)) continue;
                        if (result.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                        result.Add(new App { Path = path, Name = FriendlyName(path) });
                    }
                }
            }
            catch (Exception ex) { Log.Error("open-with list", ex); }
            return result;
        }

        static string Resolve(string exe)
        {
            foreach (string keyPath in new[] { @"Applications\" + exe + @"\shell\open\command", @"Applications\" + exe + @"\shell\edit\command" })
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    string cmd = k == null ? null : k.GetValue("") as string;
                    string path = ExeFromCommand(cmd);
                    if (path != null && File.Exists(path)) return path;
                }
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                using (RegistryKey k = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                {
                    string p = k == null ? null : (k.GetValue("") as string ?? "").Trim('"');
                    if (!string.IsNullOrEmpty(p) && File.Exists(Environment.ExpandEnvironmentVariables(p))) return Environment.ExpandEnvironmentVariables(p);
                }
            return null;
        }

        /// <summary>"\"C:\\x\\app.exe\" \"%1\"" → C:\x\app.exe</summary>
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 1 ? cmd.Substring(1, end - 1) : null;
            }
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? cmd.Substring(0, exe + 4) : null;
        }

        static string FriendlyName(string path)
        {
            try
            {
                string d = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(d) && d.Length <= 22) return d.Trim();
            }
            catch (Exception) { }
            return Path.GetFileNameWithoutExtension(path);
        }

        // ---------- doing it ----------

        public static void Open(IList<string> paths)
        {
            foreach (string p in paths.Take(20)) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        }

        public static void OpenIn(string exe, IList<string> paths)
        {
            foreach (string p in paths.Take(20)) Process.Start(new ProcessStartInfo(exe, "\"" + p + "\"") { UseShellExecute = false, WorkingDirectory = FileOps.ParentOf(p) });
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OPENASINFO
        {
            public string pcszFile, pcszClass;
            public int oaifInFlags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHOpenWithDialog(IntPtr parent, ref OPENASINFO info);

        /// <summary>Windows' own "How do you want to open this file?" chooser.</summary>
        public static void Choose(string file)
        {
            var info = new OPENASINFO { pcszFile = file, oaifInFlags = 0x1 | 0x4 }; // OAIF_ALLOW_REGISTRATION | OAIF_EXEC
            int hr = SHOpenWithDialog(IntPtr.Zero, ref info);
            if (hr < 0 && hr != unchecked((int)0x800704C7)) // ERROR_CANCELLED
                Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + file);
        }

        public static void Print(IList<string> paths)
        {
            foreach (string p in paths.Take(20)) Process.Start(new ProcessStartInfo(p) { Verb = "print", UseShellExecute = true });
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb, lpFile, lpParameters, lpDirectory;
            public int nShow;
            public IntPtr hInstApp, lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon, hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        /// <summary>The Explorer Properties window for an item.</summary>
        public static void Properties(string path)
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO)),
                fMask = 0xC, // SEE_MASK_INVOKEIDLIST
                lpVerb = "properties",
                lpFile = path,
                nShow = 1
            };
            if (!ShellExecuteEx(ref info)) throw new InvalidOperationException("Windows could not show the properties.");
        }

        // ---------- e-mail (Simple MAPI: Outlook classic, Thunderbird and similar) ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class MapiMessageW
        {
            public int reserved;
            public string subject, noteText, messageType, dateReceived, conversationID;
            public int flags;
            public IntPtr originator;
            public int recipCount;
            public IntPtr recips;
            public int fileCount;
            public IntPtr files;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MapiFileDescW
        {
            public int reserved, flags, position;
            public IntPtr pathName, fileName, fileType;
        }

        [DllImport("mapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "MAPISendMailW")]
        static extern int MAPISendMailW(IntPtr session, IntPtr uiParam, MapiMessageW message, int flags, int reserved);

        /// <summary>A new e-mail with the files attached, in the default mail program. Returns false if none answers.</summary>
        public static bool Email(IList<string> paths)
        {
            var files = paths.Where(File.Exists).Take(20).ToList();
            if (files.Count == 0) throw new InvalidOperationException("Folders cannot be attached; zip them first.");
            int size = Marshal.SizeOf(typeof(MapiFileDescW));
            IntPtr block = Marshal.AllocHGlobal(size * files.Count);
            var strings = new List<IntPtr>();
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    IntPtr path = Marshal.StringToHGlobalUni(files[i]), name = Marshal.StringToHGlobalUni(Path.GetFileName(files[i]));
                    strings.Add(path); strings.Add(name);
                    var d = new MapiFileDescW { position = -1, pathName = path, fileName = name };
                    Marshal.StructureToPtr(d, new IntPtr(block.ToInt64() + i * size), false);
                }
                var msg = new MapiMessageW { subject = files.Count == 1 ? Path.GetFileName(files[0]) : files.Count + " files", fileCount = files.Count, files = block };
                int rc;
                try { rc = MAPISendMailW(IntPtr.Zero, IntPtr.Zero, msg, 0x8 /* MAPI_DIALOG */ | 0x1 /* LOGON_UI */, 0); }
                catch (Exception) { return false; }
                return rc == 0 || rc == 1; // success or user cancelled
            }
            finally
            {
                foreach (IntPtr s in strings) Marshal.FreeHGlobal(s);
                Marshal.FreeHGlobal(block);
            }
        }
    }
}
