using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>Pictures of DragFlow's windows in a grid, for checking layouts by eye: DragFlow.Tests.exe --windows out.png</summary>
    static class WindowShots
    {
        public static void Save(string output, string tmp)
        {
            string dir = Make.Dir(tmp, "shots");
            var settings = new Settings();
            var forms = new List<Form>
            {
                ConvertDialog.CreateForPreview(new[] { Make.Png(dir, "photo.png") }),
                ConvertDialog.CreateForPreview(new[] { Make.Text(dir, "clip.mp4") }),
                ConvertDialog.CreateForPreview(new[] { Make.Text(dir, "song.mp3") }),
                WelcomeWindow.CreateForPreview(settings)
            };
            for (int tab = 0; tab <= 12; tab++) forms.Add(SettingsWindow.CreateForPreview(settings, tab));
            const int perRow = 3;
            int cellW = forms.Max(f => f.Width) + 20, cellH = forms.Max(f => f.Height) + 20;
            int rows = (forms.Count + perRow - 1) / perRow;
            using (var canvas = new Bitmap(cellW * perRow + 20, cellH * rows + 20))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.FromArgb(230, 232, 236));
                    for (int i = 0; i < forms.Count; i++)
                    {
                        Form f = forms[i];
                        f.StartPosition = FormStartPosition.Manual;
                        f.Location = new Point(-5000, -5000);
                        f.TopMost = false;
                        f.Show();
                        Application.DoEvents();
                        using (var b = new Bitmap(f.Width, f.Height))
                        {
                            f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                            g.DrawImage(b, 20 + (i % perRow) * cellW, 20 + (i / perRow) * cellH);
                        }
                        f.Close();
                    }
                }
                canvas.Save(output, ImageFormat.Png);
            }
        }
    }
}
