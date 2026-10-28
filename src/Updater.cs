using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace DragFlow
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string File, Sha256, Notes;
    }

    /// <summary>
    /// Updates from a place you choose (Settings > Shortcuts & updates): a folder such as a OneDrive folder, or a web
    /// address, holding latest.ini (version, file, sha256, notes) next to the new DragFlow.exe. The download must
    /// match its SHA-256 and be a DragFlow build of that version, or it is thrown away. The running copy is renamed
    /// (Windows allows that), the new one put in its place, and DragFlow restarts.
    /// scripts\release.ps1 makes latest.ini.
    /// </summary>
    public static class Updater
    {
        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        static bool IsWeb(string feed) { return feed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase); }

        static string Combine(string feed, string name)
        {
            return IsWeb(feed) ? feed.TrimEnd('/') + "/" + Uri.EscapeDataString(name) : Path.Combine(feed, name);
        }

        static byte[] Fetch(string location)
        {
            if (IsWeb(location))
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient()) return wc.DownloadData(location);
            }
            return File.ReadAllBytes(location);
        }

        public static UpdateInfo Parse(string text)
        {
            var info = new UpdateInfo();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF').Trim();
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                if (k == "version") { Version ver; if (Version.TryParse(v, out ver)) info.Version = ver; }
                else if (k == "file") info.File = Path.GetFileName(v); // a plain name next to latest.ini, never a path
                else if (k == "sha256") info.Sha256 = v.ToLowerInvariant();
                else if (k == "notes") info.Notes = v;
            }
            return info.Version != null && !string.IsNullOrEmpty(info.File) && info.Sha256 != null && info.Sha256.Length == 64 ? info : null;
        }

        /// <summary>A newer version at the feed, or null.</summary>
        public static UpdateInfo Check(string feed)
        {
            if (string.IsNullOrWhiteSpace(feed)) return null;
            if (feed.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Updates over plain http could be faked on the way; use an https address or a folder.");
            UpdateInfo info = Parse(Encoding.UTF8.GetString(Fetch(Combine(feed.Trim(), "latest.ini"))));
            if (info == null) throw new InvalidOperationException("The update information (latest.ini) is incomplete.");
            return info.Version > Current ? info : null;
        }

        public static string Sha256Of(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Download and check the new version; returns the verified file, ready to install.</summary>
        public static string Download(string feed, UpdateInfo info)
        {
            byte[] data = Fetch(Combine(feed.Trim(), info.File));
            if (Sha256Of(data) != info.Sha256) throw new InvalidOperationException("The download does not match its checksum, so it was not used.");
            string dir = AppPaths.LocalDir("update");
            string staged = Path.Combine(dir, "DragFlow-" + info.Version + ".exe");
            File.WriteAllBytes(staged, data);
            AssemblyName name;
            try { name = AssemblyName.GetAssemblyName(staged); }
            catch (Exception) { File.Delete(staged); throw new InvalidOperationException("The download is not a DragFlow program, so it was not used."); }
            if (name.Name != "DragFlow" || name.Version < info.Version)
            {
                File.Delete(staged);
                throw new InvalidOperationException("The download is not the promised DragFlow version, so it was not used.");
            }
            // A signed DragFlow only accepts updates signed by the same publisher.
            string mine = Signature.Signer(Application.ExecutablePath);
            if (mine != null && Signature.Signer(staged) != mine)
            {
                File.Delete(staged);
                throw new InvalidOperationException("The download is not signed by the same publisher as this DragFlow, so it was not used.");
            }
            return staged;
        }

        /// <summary>Put the new exe in place of <paramref name="exe"/>; the old one is kept as DragFlow.old.exe until the next start.</summary>
        public static void Install(string staged, string exe)
        {
            string old = Path.Combine(Path.GetDirectoryName(exe), "DragFlow.old.exe");
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old); // a running program may be renamed, not overwritten
            try { File.Copy(staged, exe); }
            catch (Exception)
            {
                File.Move(old, exe); // put the working version back
                throw;
            }
            Log.Write("updated to " + AssemblyName.GetAssemblyName(exe).Version);
        }

        /// <summary>At start-up: remove what an update left behind.</summary>
        public static void CleanUp()
        {
            try
            {
                string old = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "DragFlow.old.exe");
                if (File.Exists(old)) File.Delete(old);
                string dir = Path.Combine(AppPaths.Local, "update");
                if (Directory.Exists(dir)) foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            }
            catch (Exception) { }
        }
    }

    /// <summary>Checks a program's digital signature with Windows (WinVerifyTrust).</summary>
    public static class Signature
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile, pgKnownSubject;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData, pSIPClientData;
            public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData, pwszURLReference;
            public uint dwProvFlags, dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [System.Runtime.InteropServices.DllImport("wintrust.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        static Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        /// <summary>The publisher of a validly signed file, or null when it is unsigned or the signature is not valid.</summary>
        public static string Signer(string path)
        {
            var file = new WINTRUST_FILE_INFO { cbStruct = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = path };
            IntPtr pFile = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(file));
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(file, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2 /* WTD_UI_NONE */, fdwRevocationChecks = 0, dwUnionChoice = 1 /* WTD_CHOICE_FILE */, pFile = pFile,
                    dwStateAction = 0, dwProvFlags = 0x80 /* WTD_CACHE_ONLY_URL_RETRIEVAL */
                };
                if (WinVerifyTrust(IntPtr.Zero, ref GenericVerifyV2, ref data) != 0) return null;
                return System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path).Subject;
            }
            catch (Exception) { return null; }
            finally
            {
                System.Runtime.InteropServices.Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                System.Runtime.InteropServices.Marshal.FreeHGlobal(pFile);
            }
        }
    }

    /// <summary>Everything "DragFlow.exe --uninstall" removes (what the installer added), keeping or removing your settings.</summary>
    public static class Uninstaller
    {
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DragFlow";

        public static void Run(bool quiet)
        {
            DialogResult keep = DialogResult.Yes;
            if (!quiet)
            {
                if (MessageBox.Show("Remove DragFlow from this PC?", "DragFlow", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                keep = MessageBox.Show("Keep your DragFlow settings, custom actions, rules and recipes, in case you install it again?",
                    "DragFlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }
            KeepAlive.AskToStop("always"); // so the guard does not bring it back while it is being removed
            try { using (var ev = System.Threading.EventWaitHandle.OpenExisting("DragFlow.Exit")) ev.Set(); } catch (Exception) { }
            System.Threading.Thread.Sleep(1500);
            foreach (Process p in Process.GetProcessesByName("DragFlow").Where(p => p.Id != Process.GetCurrentProcess().Id))
                try { p.Kill(); } catch (Exception) { }

            try { Settings.StartsWithWindows = false; } catch (Exception) { }
            StartupTask.Remove();
            ExplorerMenu.Unregister();
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch (Exception) { }
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DragFlow.lnk");
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch (Exception) { }
            string sendTo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "DragFlow actions.lnk");
            try { if (File.Exists(sendTo)) File.Delete(sendTo); } catch (Exception) { }
            if (keep == DialogResult.No) try { Directory.Delete(AppPaths.Roaming, true); } catch (Exception) { }

            // The program folder (app, logs, undo history) is removed once this copy has exited.
            string folder = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.Equals(folder.TrimEnd('\\'), AppPaths.Local.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + folder + "\"")
                { CreateNoWindow = true, UseShellExecute = false });
            if (!quiet) MessageBox.Show("DragFlow was removed.", "DragFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
