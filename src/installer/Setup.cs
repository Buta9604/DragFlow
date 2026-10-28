using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DragFlow Setup")]
[assembly: AssemblyProduct("DragFlow")]
[assembly: AssemblyDescription("Installs DragFlow for the current user")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace DragFlowSetup
{
    /// <summary>
    /// DragFlow-Setup.exe: installs (or updates) DragFlow for the current user only, without administrator rights.
    /// DragFlow.exe travels inside this file. "--quiet" installs with the default choices and no questions.
    /// </summary>
    static class Setup
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DragFlow";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragFlow"); } }
        static string Exe { get { return Path.Combine(InstallDir, "DragFlow.exe"); } }

        static byte[] Payload()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DragFlow.exe"))
            using (var ms = new MemoryStream())
            {
                if (s == null) throw new InvalidOperationException("This setup file is damaged: DragFlow is missing from it.");
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static Version VersionOf(string exe)
        {
            try { return AssemblyName.GetAssemblyName(exe).Version; } catch (Exception) { return null; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool quiet = args.Any(a => a == "--quiet" || a == "/quiet" || a == "/S");
            byte[] payload;
            try { payload = Payload(); }
            catch (Exception ex) { if (!quiet) MessageBox.Show(ex.Message, "DragFlow Setup"); return 2; }
            Version newVersion = Assembly.GetExecutingAssembly().GetName().Version;
            Version installed = File.Exists(Exe) ? VersionOf(Exe) : null;

            bool startup = true, menu = true;
            if (!quiet)
            {
                using (var f = new SetupForm(newVersion, installed))
                {
                    if (f.ShowDialog() != DialogResult.OK) return 1;
                    startup = f.Startup; menu = f.AddMenu;
                }
            }
            try
            {
                Install(payload, newVersion, startup, menu);
                if (!quiet)
                    MessageBox.Show("DragFlow " + newVersion.ToString(3) + " is installed and running.\r\n\r\nLook for its round blue icon next to the clock, then start dragging a file in File Explorer.",
                        "DragFlow Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show("DragFlow could not be installed: " + ex.Message, "DragFlow Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
        }

        static void Install(byte[] payload, Version version, bool startup, bool menu)
        {
            // Close a running DragFlow politely, then make sure.
            try { using (var ev = EventWaitHandle.OpenExisting("DragFlow.Exit")) ev.Set(); } catch (Exception) { }
            for (int i = 0; i < 30 && Process.GetProcessesByName("DragFlow").Length > 0; i++) Thread.Sleep(100);
            foreach (Process p in Process.GetProcessesByName("DragFlow")) try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }

            Directory.CreateDirectory(InstallDir);
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllBytes(Exe, payload); break; }
                catch (IOException) { if (attempt >= 10) throw; Thread.Sleep(300); }
            }

            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DragFlow.lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = Exe; sc.WorkingDirectory = InstallDir; sc.Description = "Actions for the files you drag";
            sc.Save();
            string sendTo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "DragFlow actions.lnk");
            dynamic st = shell.CreateShortcut(sendTo);
            st.TargetPath = Exe; st.Arguments = "--dial"; st.WorkingDirectory = InstallDir; st.Description = "Open the DragFlow dial for these files";
            st.Save();

            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (startup) run.SetValue("DragFlow", "\"" + Exe + "\" --startup");
                else run.DeleteValue("DragFlow", false);
            }

            using (RegistryKey u = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                u.SetValue("DisplayName", "DragFlow");
                u.SetValue("DisplayVersion", version.ToString(3));
                u.SetValue("Publisher", "DragFlow");
                u.SetValue("DisplayIcon", "\"" + Exe + "\",0");
                u.SetValue("InstallLocation", InstallDir);
                u.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                u.SetValue("QuietUninstallString", "\"" + Exe + "\" --uninstall --quiet");
                u.SetValue("NoModify", 1, RegistryValueKind.DWord);
                u.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                u.SetValue("EstimatedSize", (int)Math.Max(1, payload.Length / 1024), RegistryValueKind.DWord);
                u.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }

            SetContextMenuSetting(menu);

            // DragFlow must run as a normal user; from an administrator window, start it through Explorer.
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (admin) Process.Start("explorer.exe", "\"" + Exe + "\"");
            else Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = InstallDir });
        }

        /// <summary>Write ContextMenu=0/1 into DragFlow's settings file, keeping everything else.</summary>
        static void SetContextMenuSetting(bool on)
        {
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DragFlow", "settings.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var lines = File.Exists(file) ? File.ReadAllLines(file).Where(l => !l.StartsWith("ContextMenu=")).ToList() : new System.Collections.Generic.List<string>();
            lines.Add("ContextMenu=" + (on ? "1" : "0"));
            File.WriteAllLines(file, lines.ToArray());
        }
    }

    sealed class SetupForm : Form
    {
        readonly CheckBox startup = new CheckBox(), menu = new CheckBox();
        public bool Startup { get { return startup.Checked; } }
        public bool AddMenu { get { return menu.Checked; } }

        public SetupForm(Version version, Version installed)
        {
            Text = "DragFlow Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(480, 270);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            string head = installed == null ? "Install DragFlow " + version.ToString(3)
                : installed < version ? "Update DragFlow " + installed.ToString(3) + " to " + version.ToString(3)
                : "Reinstall DragFlow " + version.ToString(3);
            var title = new Label { Text = head, Font = new Font("Segoe UI Semibold", 13f), Left = 20, Top = 16, Width = 440, Height = 30 };
            var text = new Label
            {
                Left = 20, Top = 54, Width = 440, Height = 80,
                Text = "DragFlow opens a dial of actions when you drag files in File Explorer: convert, resize, zip, move and much more, all undoable.\r\n\r\n" +
                       "It installs for you only and needs no administrator rights. Your settings are kept when updating."
            };
            startup.Text = "Start DragFlow with Windows"; startup.SetBounds(20, 144, 440, 26); startup.Checked = true;
            menu.Text = "Add \"DragFlow actions\" to Explorer's right-click menu"; menu.SetBounds(20, 172, 440, 26); menu.Checked = true;
            var ok = new Button { Text = installed == null ? "Install" : "Update", DialogResult = DialogResult.OK, Left = 268, Top = 222, Width = 96, Height = 32 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 370, Top = 222, Width = 90, Height = 32 };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { title, text, startup, menu, ok, cancel });
        }
    }
}
