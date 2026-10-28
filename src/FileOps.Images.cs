using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    public static partial class FileOps
    {
        internal static Image LoadImage(string path)
        {
            // Copy into memory so the file is not left locked. WebP/HEIC/AVIF go through Windows' own codecs first.
            byte[] bytes = NeedsWic(path) ? WinRt.DecodeToPng(path) : File.ReadAllBytes(path);
            Image img;
            try { img = Image.FromStream(new MemoryStream(bytes), false, false); }
            catch (ArgumentException)
            {
                bytes = WinRt.DecodeToPng(path); // unusual variants GDI+ cannot read
                img = Image.FromStream(new MemoryStream(bytes), false, false);
            }
            using (img)
            {
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96, 96);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
                ApplyOrientation(img, bmp);
                return bmp;
            }
        }

        static void ApplyOrientation(Image source, Image target)
        {
            try
            {
                if (!source.PropertyIdList.Contains(0x0112)) return;
                byte o = source.GetPropertyItem(0x0112).Value[0];
                if (o == 3) target.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if (o == 6) target.RotateFlip(RotateFlipType.Rotate90FlipNone);
                else if (o == 8) target.RotateFlip(RotateFlipType.Rotate270FlipNone);
            }
            catch (ArgumentException) { }
        }

        /// <summary>Date the photo was taken (EXIF), falling back to the file's modified date.</summary>
        public static DateTime PhotoDate(string path)
        {
            try
            {
                if (!NeedsWic(path))
                    using (var fs = File.OpenRead(path))
                    using (Image img = Image.FromStream(fs, false, false))
                    {
                        if (img.PropertyIdList.Contains(0x9003))
                        {
                            string s = Encoding.ASCII.GetString(img.GetPropertyItem(0x9003).Value).TrimEnd('\0');
                            DateTime d;
                            if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
                        }
                    }
            }
            catch (Exception) { }
            return File.GetLastWriteTime(path);
        }

        /// <summary>Flatten onto white (JPEG has no alpha) with an optional max side length.</summary>
        internal static Bitmap ToRgb(Image src, int maxSide)
        {
            int w = src.Width, h = src.Height;
            if (maxSide > 0 && Math.Max(w, h) > maxSide)
            {
                double s = (double)maxSide / Math.Max(w, h);
                w = Math.Max(1, (int)(w * s)); h = Math.Max(1, (int)(h * s));
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            bmp.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        internal static Bitmap Scaled(Image src, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96, 96);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        internal static byte[] EncodeJpeg(Image img, long quality)
        {
            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            using (var ms = new MemoryStream())
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                img.Save(ms, codec, ep);
                return ms.ToArray();
            }
        }

        internal static void SaveAs(Image src, string dest, string format)
        {
            switch (format)
            {
                case "jpg":
                case "jpeg":
                case "jfif":
                    using (Bitmap rgb = ToRgb(src, 0)) File.WriteAllBytes(dest, EncodeJpeg(rgb, 92));
                    break;
                case "png": src.Save(dest, ImageFormat.Png); break;
                case "bmp": src.Save(dest, ImageFormat.Bmp); break;
                case "gif": src.Save(dest, ImageFormat.Gif); break;
                case "tif":
                case "tiff": src.Save(dest, ImageFormat.Tiff); break;
                default: throw new ArgumentException("Unknown format " + format);
            }
        }

        static string FormatOf(string path) { return Path.GetExtension(path).TrimStart('.').ToLowerInvariant(); }

        /// <summary>Images become one page each; PDFs given alongside are added page by page (as pictures of the pages).</summary>
        public static string ToPdf(IList<string> paths)
        {
            var sources = new List<string>();
            foreach (string p in paths)
            {
                if (Directory.Exists(p)) sources.AddRange(ImagesIn(new[] { p }));
                else if (Classify(p) == FileKind.Image || Classify(p) == FileKind.Pdf) sources.Add(p);
            }
            if (sources.Count == 0) throw new InvalidOperationException("No images found to turn into a PDF.");

            var pages = new List<PdfPage>();
            Batch.Each(sources, delegate (string file)
            {
                if (Classify(file) == FileKind.Pdf)
                {
                    foreach (byte[] png in WinRt.PdfPagesAsPng(file, 2.0))
                        using (Image src = Image.FromStream(new MemoryStream(png)))
                        using (Bitmap rgb = ToRgb(src, 0))
                            pages.Add(new PdfPage { Jpeg = EncodeJpeg(rgb, 88), PxWidth = rgb.Width, PxHeight = rgb.Height, PointScale = 0.5 });
                    return;
                }
                using (Image src = LoadImage(file))
                using (Bitmap rgb = ToRgb(src, 0))
                    pages.Add(new PdfPage { Jpeg = EncodeJpeg(rgb, 90), PxWidth = rgb.Width, PxHeight = rgb.Height });
            });
            if (pages.Count == 0) throw Batch.Nothing("No pages could be read.");

            string dir = ParentOf(paths[0]), name;
            if (paths.Count == 1) name = Directory.Exists(paths[0]) ? NameOf(paths[0]) : Path.GetFileNameWithoutExtension(paths[0]);
            else name = sources.All(s => Classify(s) == FileKind.Pdf) ? "Combined" : "Images";

            string output = UniquePath(dir, name, ".pdf");
            using (UndoLog.Begin("To PDF"))
            {
                PdfWriter.Write(output, pages);
                UndoLog.Created(output);
            }
            return output;
        }

        /// <summary>Convert pictures with the standard quality for the format (see <see cref="Converter"/> for more control).</summary>
        public static List<string> ConvertImages(IList<string> paths, string format)
        {
            format = format.ToLowerInvariant();
            if (format == "jpeg") format = "jpg";
            if (format == "tif") format = "tiff";
            return Converter.ConvertImages(paths, new ConvertOptions { Format = format, Quality = ConvertOptions.DefaultQuality(format) });
        }

        /// <summary>Smaller JPEG copies (quality 70, longest side at most 2560 px).</summary>
        public static List<string> Shrink(IList<string> paths)
        {
            var outputs = new List<string>();
            List<string> images = ImagesIn(paths);
            using (UndoLog.Begin("Shrink"))
                Batch.Each(images, delegate (string file)
                {
                    using (Image src = LoadImage(file))
                    using (Bitmap rgb = ToRgb(src, 2560))
                    {
                        string dest = UniquePath(ParentOf(file), Path.GetFileNameWithoutExtension(file) + "_small", ".jpg");
                        File.WriteAllBytes(dest, EncodeJpeg(rgb, 70));
                        UndoLog.Created(dest);
                        outputs.Add(dest);
                    }
                });
            if (outputs.Count == 0) throw Batch.Nothing("No images to shrink.");
            return outputs;
        }

        /// <summary>Resized copies. mode: "50" = half size, otherwise the longest side in pixels ("1920", "1280").</summary>
        public static List<string> Resize(IList<string> paths, string mode)
        {
            var outputs = new List<string>();
            using (UndoLog.Begin("Resize"))
                Batch.Each(ImagesIn(paths), delegate (string file)
                {
                    using (Image src = LoadImage(file))
                    {
                        int w, h;
                        string suffix;
                        if (mode == "50") { w = Math.Max(1, src.Width / 2); h = Math.Max(1, src.Height / 2); suffix = "_50pct"; }
                        else
                        {
                            int max = int.Parse(mode, CultureInfo.InvariantCulture);
                            double s = Math.Min(1.0, (double)max / Math.Max(src.Width, src.Height));
                            w = Math.Max(1, (int)Math.Round(src.Width * s)); h = Math.Max(1, (int)Math.Round(src.Height * s));
                            suffix = "_" + max;
                        }
                        string fmt = FormatOf(file) == "png" ? "png" : "jpg";
                        string dest = UniquePath(ParentOf(file), Path.GetFileNameWithoutExtension(file) + suffix, "." + fmt);
                        using (Bitmap scaled = Scaled(src, w, h)) SaveAs(scaled, dest, fmt);
                        UndoLog.Created(dest);
                        outputs.Add(dest);
                    }
                });
            if (outputs.Count == 0) throw Batch.Nothing("No images to resize.");
            return outputs;
        }

        /// <summary>Rotate or flip images in place. The originals are backed up in the undo journal first.</summary>
        public static int Rotate(IList<string> paths, string mode)
        {
            RotateFlipType op = mode == "left" ? RotateFlipType.Rotate270FlipNone
                              : mode == "right" ? RotateFlipType.Rotate90FlipNone
                              : RotateFlipType.RotateNoneFlipX;
            List<string> images = ImagesIn(paths).Where(f => !NeedsWic(f)).ToList();
            if (images.Count == 0) throw new InvalidOperationException("No images to rotate (WebP and HEIC can be converted first).");
            Guard.Modifiable(images);
            int n = 0;
            using (UndoLog.Begin(mode == "flip" ? "Flip" : "Rotate"))
                Batch.Each(images, delegate (string file)
                {
                    UndoLog.BackupBeforeEdit(file);
                    using (Image src = LoadImage(file))
                    {
                        src.RotateFlip(op);
                        SaveAs(src, file, FormatOf(file));
                    }
                    n++;
                });
            if (n == 0) throw Batch.Nothing("No images could be rotated.");
            return n;
        }

        /// <summary>Re-encode without EXIF/GPS/camera metadata.</summary>
        public static List<string> StripMetadata(IList<string> paths)
        {
            var outputs = new List<string>();
            using (UndoLog.Begin("Strip EXIF"))
                Batch.Each(ImagesIn(paths), delegate (string file)
                {
                    using (Image src = LoadImage(file))
                    {
                        bool png = FormatOf(file) == "png";
                        string dest = UniquePath(ParentOf(file), Path.GetFileNameWithoutExtension(file) + "_clean", png ? ".png" : ".jpg");
                        SaveAs(src, dest, png ? "png" : "jpg");
                        UndoLog.Created(dest);
                        outputs.Add(dest);
                    }
                });
            if (outputs.Count == 0) throw Batch.Nothing("No images to clean.");
            return outputs;
        }

        /// <summary>A decoded copy of the first image, for the clipboard.</summary>
        public static Image LoadForClipboard(IList<string> paths)
        {
            List<string> images = ImagesIn(paths);
            if (images.Count == 0) throw new InvalidOperationException("No image to copy.");
            return LoadImage(images[0]);
        }

        /// <summary>Save a picture (e.g. dragged out of a browser) as PNG into a folder; returns the file.</summary>
        public static string SaveBitmap(Image img, string dir, string name)
        {
            string dest = UniquePath(dir, name, ".png");
            img.Save(dest, ImageFormat.Png);
            return dest;
        }
    }
}
