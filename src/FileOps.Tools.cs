using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    /// <summary>Picture tools (crop, watermark, grayscale, border, collage, animated GIF) and everyday extras.</summary>
    public static partial class FileOps
    {
        static string OutputFor(string file, string suffix, string ext)
        {
            return UniquePath(ParentOf(file), Path.GetFileNameWithoutExtension(file) + suffix, ext);
        }

        /// <summary>Same format as the source for PNG, JPG for everything else.</summary>
        static string KeepFormat(string file) { return FormatOf(file) == "png" ? "png" : "jpg"; }

        static List<string> EachImage(IList<string> paths, string title, string suffix, Func<Image, Image> edit)
        {
            var outputs = new List<string>();
            using (UndoLog.Begin(title))
                Batch.Each(ImagesIn(paths), delegate (string file)
                {
                    using (Image src = LoadImage(file))
                    using (Image result = edit(src))
                    {
                        string fmt = KeepFormat(file);
                        string dest = OutputFor(file, suffix, "." + fmt);
                        SaveAs(result, dest, fmt);
                        UndoLog.Created(dest);
                        outputs.Add(dest);
                    }
                });
            if (outputs.Count == 0) throw Batch.Nothing("No pictures to work on.");
            return outputs;
        }

        /// <summary>Crop from the middle to a shape: "1:1" (square) or "16:9".</summary>
        public static List<string> Crop(IList<string> paths, string ratio)
        {
            string[] p = ratio.Split(':');
            double want = double.Parse(p[0]) / double.Parse(p[1]);
            string suffix = ratio == "1:1" ? "_square" : "_" + ratio.Replace(':', 'x');
            return EachImage(paths, "Crop", suffix, delegate (Image src)
            {
                int w = src.Width, h = src.Height;
                if ((double)w / h > want) w = Math.Max(1, (int)Math.Round(h * want, MidpointRounding.AwayFromZero)); else h = Math.Max(1, (int)Math.Round(w / want, MidpointRounding.AwayFromZero));
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle((src.Width - w) / 2, (src.Height - h) / 2, w, h), GraphicsUnit.Pixel);
                return bmp;
            });
        }

        /// <summary>Text in the bottom-right corner, sized to the picture.</summary>
        public static List<string> Watermark(IList<string> paths, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Type the text for the watermark.");
            return EachImage(paths, "Watermark", "_marked", delegate (Image src)
            {
                var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    float size = Math.Max(10f, Math.Min(src.Width, src.Height) / 22f);
                    using (var font = new Font("Segoe UI Semibold", size, GraphicsUnit.Pixel))
                    {
                        SizeF ts = g.MeasureString(text, font);
                        float x = src.Width - ts.Width - size * 0.8f, y = src.Height - ts.Height - size * 0.6f;
                        using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.DrawString(text, font, shadow, x + size / 12, y + size / 12);
                        using (var ink = new SolidBrush(Color.FromArgb(190, 255, 255, 255))) g.DrawString(text, font, ink, x, y);
                    }
                }
                return bmp;
            });
        }

        public static List<string> Grayscale(IList<string> paths)
        {
            var matrix = new ColorMatrix(new[]
            {
                new[] { .299f, .299f, .299f, 0, 0 }, new[] { .587f, .587f, .587f, 0, 0 }, new[] { .114f, .114f, .114f, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 }
            });
            return EachImage(paths, "Grayscale", "_gray", delegate (Image src)
            {
                var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(bmp))
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetColorMatrix(matrix);
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                }
                return bmp;
            });
        }

        /// <summary>A white border, 3 % of the shorter side.</summary>
        public static List<string> Border(IList<string> paths)
        {
            return EachImage(paths, "Border", "_border", delegate (Image src)
            {
                int b = Math.Max(4, Math.Min(src.Width, src.Height) * 3 / 100);
                var bmp = new Bitmap(src.Width + 2 * b, src.Height + 2 * b, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.DrawImage(src, b, b, src.Width, src.Height);
                }
                return bmp;
            });
        }

        /// <summary>All the pictures in a grid, 400 px cells, as "Collage.jpg" next to the first one.</summary>
        public static string Collage(IList<string> paths)
        {
            List<string> images = ImagesIn(paths);
            if (images.Count < 2) throw new InvalidOperationException("A collage needs at least two pictures.");
            if (images.Count > 64) images = images.Take(64).ToList();
            int cell = 400, gap = 8;
            int cols = (int)Math.Ceiling(Math.Sqrt(images.Count)), rows = (int)Math.Ceiling(images.Count / (double)cols);
            string dest = UniquePath(ParentOf(images[0]), "Collage", ".jpg");
            using (UndoLog.Begin("Collage"))
            using (var bmp = new Bitmap(cols * cell + (cols + 1) * gap, rows * cell + (rows + 1) * gap, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    Job.SetTotal(images.Count);
                    for (int i = 0; i < images.Count; i++)
                    {
                        Job.Step(Path.GetFileName(images[i]));
                        Job.Advance();
                        int cx = gap + (i % cols) * (cell + gap), cy = gap + (i / cols) * (cell + gap);
                        try
                        {
                            using (Image img = LoadImage(images[i]))
                            {
                                double s = Math.Min((double)cell / img.Width, (double)cell / img.Height);
                                int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
                                g.DrawImage(img, cx + (cell - w) / 2, cy + (cell - h) / 2, w, h);
                            }
                        }
                        catch (Exception ex) { Log.Error("collage " + Log.P(images[i]), ex); }
                    }
                }
                File.WriteAllBytes(dest, EncodeJpeg(bmp, 90));
                UndoLog.Created(dest);
            }
            return dest;
        }

        /// <summary>The pictures as frames of an animated GIF (one second each), 640×480 letterboxed. Needs ffmpeg.</summary>
        public static string AnimatedGif(IList<string> paths)
        {
            List<string> images = ImagesIn(paths);
            if (images.Count < 2) throw new InvalidOperationException("An animation needs at least two pictures.");
            string ff = RequireFfmpeg();
            string work = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string dest = UniquePath(ParentOf(images[0]), "Animation", ".gif");
            try
            {
                Job.SetTotal(images.Count);
                for (int i = 0; i < images.Count; i++)
                {
                    Job.Step(Path.GetFileName(images[i]));
                    Job.Advance();
                    using (Image img = LoadImage(images[i]))
                    using (Bitmap rgb = ToRgb(img, 0))
                        rgb.Save(Path.Combine(work, "frame" + i.ToString("D5") + ".png"), ImageFormat.Png);
                }
                using (UndoLog.Begin("Animated GIF"))
                {
                    RunFfmpegTo(ff, "-framerate 1 -i \"" + Path.Combine(work, "frame%05d.png") + "\" -vf \"scale=640:480:force_original_aspect_ratio=decrease," +
                        "pad=640:480:(ow-iw)/2:(oh-ih)/2:white,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0", dest);
                    if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                    UndoLog.Created(dest);
                }
            }
            finally { try { Directory.Delete(work, true); } catch (Exception) { } }
            return dest;
        }

        // ---------- everyday extras ----------

        /// <summary>Are two files the same, byte for byte? Returns a plain-words answer.</summary>
        public static string Compare(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length)
                return "Different: " + fa.Name + " is " + FormatSize(fa.Length) + ", " + fb.Name + " is " + FormatSize(fb.Length) + ".";
            using (FileStream sa = fa.OpenRead(), sb = fb.OpenRead())
            {
                var ba = new byte[65536]; var bb = new byte[65536];
                long at = 0;
                while (true)
                {
                    Job.CheckCancel();
                    int na = Fill(sa, ba), nb = Fill(sb, bb);
                    if (na != nb) return "Different.";
                    if (na == 0) return "Identical: both are " + FormatSize(fa.Length) + " and every byte matches.";
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return "Different: the first difference is at byte " + (at + i).ToString("N0") + " of " + fa.Length.ToString("N0") + ".";
                    at += na;
                }
            }
        }

        /// <summary>Read a whole buffer's worth, or everything that is left (a read may stop short on network drives).</summary>
        static int Fill(Stream s, byte[] buf)
        {
            int n = 0;
            while (n < buf.Length)
            {
                int r = s.Read(buf, n, buf.Length - n);
                if (r <= 0) break;
                n += r;
            }
            return n;
        }

        /// <summary>Where the contents list of a folder goes: next to the folder, or in Documents for a whole drive.</summary>
        internal static string ListContentsPath(string folder)
        {
            string full = Path.GetFullPath(folder);
            if (string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
                return UniquePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Drive " + full.TrimEnd('\\', ':') + " contents", ".csv");
            full = full.TrimEnd('\\');
            return UniquePath(ParentOf(full), NameOf(full) + " contents", ".csv");
        }

        /// <summary>A spreadsheet (CSV) of what is in the folder, saved next to it.</summary>
        public static string ListContents(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            string dest = ListContentsPath(folder);
            var sb = new StringBuilder("Name,Type,Size (bytes),Modified,Path\r\n");
            int n = 0;
            foreach (string d in SafeDirs(full))
            {
                sb.Append(Csv(Path.GetFileName(d))).Append(",Folder,,").Append(Directory.GetLastWriteTime(d).ToString("yyyy-MM-dd HH:mm")).Append(',').Append(Csv(d)).Append("\r\n");
                n++;
            }
            foreach (string f in SafeFiles(full))
            {
                Job.CheckCancel();
                var fi = new FileInfo(f);
                sb.Append(Csv(fi.Name)).Append(',').Append(Csv(fi.Extension.TrimStart('.').ToUpperInvariant())).Append(',').Append(fi.Length).Append(',')
                  .Append(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")).Append(',').Append(Csv(f)).Append("\r\n");
                n++;
            }
            using (UndoLog.Begin("List contents"))
            {
                File.WriteAllText(dest, sb.ToString(), new UTF8Encoding(true)); // a BOM so Excel reads accents correctly
                UndoLog.Created(dest);
            }
            return dest;
        }

        static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        /// <summary>Send items to the Recycle Bin. They can be restored from there, not through DragFlow's Undo. Returns what happened, in plain words.</summary>
        public static string RecycleAll(IList<string> paths)
        {
            Guard.Modifiable(paths);
            int binned = 0, aside = 0;
            Batch.Each(paths.ToList(), delegate (string p) { if (UndoLog.Discard(p)) binned++; else aside++; });
            string text = binned > 0 ? Plural(binned, "item", "items") + " moved to the Recycle Bin (restore them from there if needed)" : "";
            if (aside > 0) // a USB stick or network drive: no Recycle Bin there, so nothing was deleted
                text += (text.Length > 0 ? " · " : "") + Plural(aside, "item", "items") + " moved to a \"" + ShellOps.AsideFolderName + "\" folder on that drive, which has no Recycle Bin";
            return text;
        }
    }
}
