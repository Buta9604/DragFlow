using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>
    /// "Convert… Options": every format this PC can write, with quality, size, a file-size target and where to save
    /// (pictures); resolution, quality and sound (video); bitrate (audio). Remembers the last choices per kind.
    /// </summary>
    public sealed class ConvertDialog : Form
    {
        enum Mode { Image, Video, Audio }

        readonly Mode mode;
        readonly ComboBox format = new ComboBox(), size = new ComboBox(), saveTo = new ComboBox(), quality2 = new ComboBox(), bitrate = new ComboBox();
        readonly TrackBar quality = new TrackBar();
        readonly Label qualityText = new Label();
        readonly CheckBox target = new CheckBox(), noAudio = new CheckBox();
        readonly NumericUpDown targetKb = new NumericUpDown();
        string chosenFolder;
        List<string> formatIds = new List<string>();

        static readonly int[] Sides = { 0, 3840, 2560, 1920, 1280, 1024, 800, 640 };
        static readonly int[] Heights = { 0, 2160, 1440, 1080, 720, 480, 360 };
        static readonly int[] Rates = { 320, 256, 192, 160, 128, 96, 64 };

        /// <summary>Ask, and return the options as text for a "convertx:" action, or null if cancelled.</summary>
        public static string Ask(IList<string> paths)
        {
            Mode m;
            FileKind k = FileOps.KindOf(paths);
            if (k == FileKind.Video) m = Mode.Video;
            else if (k == FileKind.Audio) m = Mode.Audio;
            else if (k == FileKind.Image || k == FileKind.Folder || FileOps.ImagesIn(paths).Count > 0) m = Mode.Image;
            else throw new InvalidOperationException("Only pictures, video and audio can be converted here.");
            using (var d = new ConvertDialog(m, paths))
            {
                if (d.ShowDialog() != DialogResult.OK) return null;
                ConvertOptions o = d.Result();
                string text = o.ToString();
                Settings s = Settings.Current;
                if (m == Mode.Image) s.ConvertImage = text; else if (m == Mode.Video) s.ConvertVideo = text; else s.ConvertAudio = text;
                s.Save();
                return text;
            }
        }

        /// <summary>The window for these files without showing it (for screenshots in the tests).</summary>
        internal static Form CreateForPreview(IList<string> paths)
        {
            FileKind k = FileOps.KindOf(paths);
            return new ConvertDialog(k == FileKind.Video ? Mode.Video : k == FileKind.Audio ? Mode.Audio : Mode.Image, paths);
        }

        ConvertDialog(Mode m, IList<string> paths)
        {
            mode = m;
            Text = L.T("Convert") + " " + (paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : FileOps.Plural(paths.Count, "item", "items"));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; TopMost = true; ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(440, 330);

            Settings s = Settings.Current;
            ConvertOptions last = ConvertOptions.Parse(m == Mode.Image ? s.ConvertImage : m == Mode.Video ? s.ConvertVideo : s.ConvertAudio);
            int y = 16;
            Row(L.T("Format"), format, ref y);
            format.DropDownStyle = ComboBoxStyle.DropDownList;
            if (m == Mode.Image)
                foreach (Converter.Format f in Converter.ImageFormats()) { formatIds.Add(f.Id); format.Items.Add(f.Label + (f.Lossy ? "" : "  (" + L.T("lossless") + ")")); }
            else
            {
                string[] list = m == Mode.Video ? new[] { "mp4", "webm", "mov", "mkv", "gif", "mp3", "m4a", "wav" } : Converter.AudioFormats;
                foreach (string f in list.Where(Converter.CanWriteMedia))
                {
                    formatIds.Add(f);
                    bool sound = m == Mode.Video && Converter.AudioFormats.Contains(f);
                    format.Items.Add(f.ToUpperInvariant() + (sound ? "  (" + L.T("sound only") + ")" : ""));
                }
            }
            if (formatIds.Count == 0)
                throw new InvalidOperationException("Nothing can be converted here: ffmpeg (or its encoders) is missing. Put ffmpeg.exe next to DragFlow.exe or on your PATH.");
            int idx = formatIds.IndexOf(last.Format);
            format.SelectedIndex = idx >= 0 ? idx : 0;

            if (m == Mode.Image)
            {
                Row(L.T("Quality"), quality, ref y);
                quality.SetBounds(130, y - 34, 220, 30);
                quality.Minimum = 10; quality.Maximum = 100; quality.TickFrequency = 10; quality.Value = Math.Max(10, last.Quality);
                qualityText.SetBounds(356, y - 30, 70, 24);
                Controls.Add(qualityText);
                quality.ValueChanged += delegate { Refresh2(); };

                Row(L.T("Size"), size, ref y);
                size.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (int side in Sides) size.Items.Add(side == 0 ? L.T("Keep the original size") : L.F("At most {0} px wide or tall", side));
                size.SelectedIndex = Math.Max(0, Array.IndexOf(Sides, last.MaxSide));

                target.Text = L.T("File no bigger than");
                target.SetBounds(16, y, 160, 24);
                target.Checked = last.TargetKB > 0;
                targetKb.SetBounds(180, y, 90, 24);
                targetKb.Minimum = 10; targetKb.Maximum = 100000; targetKb.Increment = 50;
                targetKb.Value = last.TargetKB > 0 ? Math.Min(100000, last.TargetKB) : 500;
                var kb = new Label { Text = "KB", Left = 276, Top = y + 3, AutoSize = true };
                Controls.AddRange(new Control[] { target, targetKb, kb });
                target.CheckedChanged += delegate { Refresh2(); };
                y += 36;
            }
            else if (m == Mode.Video)
            {
                Row(L.T("Resolution"), size, ref y);
                size.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (int h in Heights) size.Items.Add(h == 0 ? L.T("Keep") : h + "p");
                size.SelectedIndex = Math.Max(0, Array.IndexOf(Heights, last.Height));
                Row(L.T("Quality"), quality2, ref y);
                quality2.DropDownStyle = ComboBoxStyle.DropDownList;
                quality2.Items.AddRange(new object[] { L.T("High"), L.T("Medium"), L.T("Small file") });
                quality2.SelectedIndex = last.VideoQuality == "high" ? 0 : last.VideoQuality == "small" ? 2 : 1;
                noAudio.Text = L.T("Remove the sound");
                noAudio.SetBounds(130, y, 280, 24);
                noAudio.Checked = last.NoAudio;
                Controls.Add(noAudio);
                y += 36;
            }
            else
            {
                Row(L.T("Bitrate"), bitrate, ref y);
                bitrate.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (int r in Rates) bitrate.Items.Add(r + " kbit/s" + (r == 320 ? "  (" + L.T("best") + ")" : r == 128 ? "  (" + L.T("good") + ")" : ""));
                bitrate.SelectedIndex = Math.Max(0, Array.IndexOf(Rates, last.Bitrate));
            }

            Row(L.T("Save to"), saveTo, ref y);
            saveTo.DropDownStyle = ComboBoxStyle.DropDownList;
            saveTo.Items.AddRange(new object[] { L.T("Next to the originals"), L.T("A \"Converted\" folder next to them"), L.T("Choose a folder…") });
            if (last.Output == "sub") saveTo.SelectedIndex = 1;
            else if (last.Output != "next" && Directory.Exists(last.Output)) { chosenFolder = last.Output; saveTo.Items.Add(last.Output); saveTo.SelectedIndex = 3; }
            else saveTo.SelectedIndex = 0;
            saveTo.SelectedIndexChanged += delegate
            {
                if (saveTo.SelectedIndex != 2) return;
                using (var fb = new FolderBrowserDialog { Description = L.T("Save the converted files in which folder?") })
                {
                    if (fb.ShowDialog(this) == DialogResult.OK)
                    {
                        chosenFolder = fb.SelectedPath;
                        while (saveTo.Items.Count > 3) saveTo.Items.RemoveAt(3);
                        saveTo.Items.Add(chosenFolder);
                        saveTo.SelectedIndex = 3;
                    }
                    else saveTo.SelectedIndex = 0;
                }
            };

            var note = new Label
            {
                Text = L.T("The originals are never changed. Every conversion can be undone."),
                Left = 16, Top = y + 4, Width = 408, Height = 22, ForeColor = Color.DimGray
            };
            ClientSize = new Size(440, y + 80);
            var ok = new Button { Text = L.T("Convert"), DialogResult = DialogResult.OK, Left = 244, Top = ClientSize.Height - 46, Width = 90, Height = 30 };
            var cancel = new Button { Text = L.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 340, Top = ClientSize.Height - 46, Width = 90, Height = 30 };
            Controls.AddRange(new Control[] { note, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            format.SelectedIndexChanged += delegate { Refresh2(); };
            Shown += delegate { Activate(); format.Focus(); };
            Refresh2();
        }

        void Row(string label, Control c, ref int y)
        {
            Controls.Add(new Label { Text = label, Left = 16, Top = y + 4, Width = 110 });
            c.SetBounds(130, y, 294, 26);
            Controls.Add(c);
            y += 38;
        }

        void Refresh2()
        {
            if (mode != Mode.Image) return;
            string id = formatIds.Count > 0 ? formatIds[Math.Max(0, format.SelectedIndex)] : "jpg";
            Converter.Format f = Converter.Describe(id);
            bool lossy = f != null && f.Lossy;
            quality.Enabled = lossy && !target.Checked;
            qualityText.Text = lossy ? (target.Checked ? L.T("automatic") : quality.Value + "%") : L.T("lossless");
            targetKb.Enabled = target.Checked;
        }

        ConvertOptions Result()
        {
            var o = new ConvertOptions { Format = formatIds[Math.Max(0, format.SelectedIndex)] };
            o.Quality = ConvertOptions.DefaultQuality(o.Format);
            if (mode == Mode.Image)
            {
                o.Quality = quality.Value;
                o.MaxSide = Sides[Math.Max(0, size.SelectedIndex)];
                o.TargetKB = target.Checked ? (int)targetKb.Value : 0;
            }
            else if (mode == Mode.Video)
            {
                o.Height = Heights[Math.Max(0, size.SelectedIndex)];
                o.VideoQuality = quality2.SelectedIndex == 0 ? "high" : quality2.SelectedIndex == 2 ? "small" : "medium";
                o.NoAudio = noAudio.Checked;
            }
            else o.Bitrate = Rates[Math.Max(0, bitrate.SelectedIndex)];
            o.Output = saveTo.SelectedIndex == 1 ? "sub" : saveTo.SelectedIndex >= 3 && chosenFolder != null ? chosenFolder : "next";
            return o;
        }
    }
}
