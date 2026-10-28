using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>First start: what DragFlow does in three lines, the two most useful choices, and a practice folder to try it in.</summary>
    public sealed class WelcomeWindow : Form
    {
        readonly CheckBox startup = new CheckBox(), menu = new CheckBox();

        public static void ShowOnce(Settings s)
        {
            using (var w = new WelcomeWindow(s))
            {
                w.ShowDialog();
                try { Settings.StartsWithWindows = w.startup.Checked; } catch (Exception ex) { Log.Error("startup setting", ex); }
                s.ContextMenu = w.menu.Checked;
                s.FirstRunDone = true;
                s.Save();
            }
        }

        /// <summary>For screenshots in the tests.</summary>
        internal static Form CreateForPreview(Settings s) { return new WelcomeWindow(s); }

        WelcomeWindow(Settings s)
        {
            Text = L.T("Welcome to DragFlow");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true; TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(520, 360);

            var head = new Label { Text = L.T("Actions for the files you drag"), Font = new Font("Segoe UI Semibold", 14f), Left = 24, Top = 18, Width = 470, Height = 32 };
            var steps = new Label
            {
                Left = 24, Top = 60, Width = 474, Height = 150,
                Text = L.T("1.  Start dragging any file or folder in File Explorer. A dial of actions opens around it.") + "\r\n\r\n" +
                       L.T("2.  Point at an action and let go. To drag as usual, just keep moving: the dial gets out of the way.") + "\r\n\r\n" +
                       L.T("3.  Or select files and press Ctrl+Alt+D, or right-click them and choose \"DragFlow actions\".") + "\r\n\r\n" +
                       L.T("Everything DragFlow does can be undone from the notification or the tray icon.")
            };
            startup.Text = L.T("Start DragFlow with Windows");
            startup.SetBounds(24, 222, 470, 26);
            startup.Checked = true;
            menu.Text = L.T("Add \"DragFlow actions\" to Explorer's right-click menu");
            menu.SetBounds(24, 252, 470, 26);
            menu.Checked = s.ContextMenu;

            var practice = new Button { Text = L.T("Try it in a practice folder"), Left = 24, Top = 306, Width = 230, Height = 34 };
            practice.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + MakePracticeFolder() + "\""); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "DragFlow"); }
            };
            var done = new Button { Text = L.T("Start using DragFlow"), DialogResult = DialogResult.OK, Left = 300, Top = 306, Width = 196, Height = 34 };
            AcceptButton = done;
            Controls.AddRange(new Control[] { head, steps, startup, menu, practice, done });
        }

        /// <summary>Documents\DragFlow practice, with a photo, a note and a zip to drag around. Returns the folder.</summary>
        public static string MakePracticeFolder()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DragFlow practice");
            Directory.CreateDirectory(dir);
            string photo = Path.Combine(dir, "Sunset.png");
            if (!File.Exists(photo))
                using (var bmp = new Bitmap(640, 400))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    using (var sky = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 640, 400), Color.FromArgb(255, 140, 60), Color.FromArgb(80, 40, 120), 90f))
                    {
                        g.FillRectangle(sky, 0, 0, 640, 400);
                        using (var sun = new SolidBrush(Color.FromArgb(255, 230, 120))) g.FillEllipse(sun, 260, 220, 120, 120);
                        using (var sea = new SolidBrush(Color.FromArgb(40, 60, 110))) g.FillRectangle(sea, 0, 300, 640, 100);
                    }
                    bmp.Save(photo, ImageFormat.Png);
                }
            string note = Path.Combine(dir, "Shopping list.txt");
            if (!File.Exists(note)) File.WriteAllText(note, "Apples\r\nBread\r\nCoffee\r\n");
            string zip = Path.Combine(dir, "Practice.zip");
            if (!File.Exists(zip))
                using (var fs = new FileStream(zip, FileMode.CreateNew))
                using (var za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                using (var w = new StreamWriter(za.CreateEntry("Inside the zip.txt").Open()))
                    w.Write("Drag Practice.zip and choose Extract here.");
            return dir;
        }
    }
}
