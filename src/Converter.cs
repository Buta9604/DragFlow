using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    /// <summary>
    /// What a conversion should produce. Written into the action id ("convertx:webp;q=80;max=1920;kb=500;out=sub")
    /// so a dial chip, the Convert window and a recipe can all describe the same job.
    /// </summary>
    public sealed class ConvertOptions
    {
        public string Format = "jpg";
        /// <summary>1–100 for lossy picture formats.</summary>
        public int Quality = 90;
        /// <summary>Longest side in pixels for pictures; 0 = keep.</summary>
        public int MaxSide;
        /// <summary>Aim for files no bigger than this (pictures); 0 = no limit.</summary>
        public int TargetKB;
        /// <summary>next = beside the originals, sub = a "Converted" folder beside them, or a folder path.</summary>
        public string Output = "next";
        /// <summary>Video: height in pixels (0 = keep); quality high / medium / small; drop the sound.</summary>
        public int Height;
        public string VideoQuality = "medium";
        public bool NoAudio;
        /// <summary>Audio bitrate in kbit/s for lossy formats.</summary>
        public int Bitrate = 192;

        public override string ToString()
        {
            var sb = new StringBuilder(Format);
            sb.Append(";q=").Append(Quality);
            if (MaxSide > 0) sb.Append(";max=").Append(MaxSide);
            if (TargetKB > 0) sb.Append(";kb=").Append(TargetKB);
            if (Height > 0) sb.Append(";h=").Append(Height);
            if (VideoQuality != "medium") sb.Append(";vq=").Append(VideoQuality);
            if (NoAudio) sb.Append(";noaudio=1");
            if (Bitrate != 192) sb.Append(";br=").Append(Bitrate);
            if (Output != "next") sb.Append(";out=").Append(Output); // last: a folder name may contain ";"
            return sb.ToString();
        }

        public static ConvertOptions Parse(string text)
        {
            var o = new ConvertOptions();
            text = text ?? "";
            int outAt = text.IndexOf(";out=", StringComparison.Ordinal);
            if (outAt >= 0) { o.Output = text.Substring(outAt + 5); text = text.Substring(0, outAt); }
            string[] parts = text.Split(';');
            if (parts[0].Trim().Length > 0) o.Format = parts[0].Trim().ToLowerInvariant();
            o.Quality = DefaultQuality(o.Format);
            foreach (string p in parts.Skip(1))
            {
                int eq = p.IndexOf('=');
                if (eq < 0) continue;
                string k = p.Substring(0, eq), v = p.Substring(eq + 1);
                int n;
                bool num = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                switch (k)
                {
                    case "q": if (num) o.Quality = Math.Max(1, Math.Min(100, n)); break;
                    case "max": if (num) o.MaxSide = Math.Max(0, n); break;
                    case "kb": if (num) o.TargetKB = Math.Max(0, n); break;
                    case "h": if (num) o.Height = Math.Max(0, n); break;
                    case "vq": o.VideoQuality = v == "high" || v == "small" ? v : "medium"; break;
                    case "noaudio": o.NoAudio = v == "1"; break;
                    case "br": if (num) o.Bitrate = Math.Max(32, Math.Min(512, n)); break;
                }
            }
            return o;
        }

        public static int DefaultQuality(string format)
        {
            switch (format)
            {
                case "webp": return 82;
                case "avif": return 70;
                case "heic": return 80;
                default: return 90;
            }
        }

        /// <summary>Where a converted copy of <paramref name="file"/> goes.</summary>
        public string FolderFor(string file)
        {
            string parent = FileOps.ParentOf(Path.GetFullPath(file));
            if (Output == "next" || string.IsNullOrEmpty(Output)) return parent;
            if (Output == "sub") return Path.Combine(parent, "Converted");
            return Output;
        }
    }

    /// <summary>
    /// Converting in every direction: pictures to and from JPG, PNG, WebP, AVIF, HEIC, GIF, TIFF, BMP and ICO
    /// (with quality, size and file-size targets); video and audio between the common formats.
    /// WebP and AVIF are written with ffmpeg, HEIC with Windows' own encoder; formats this PC cannot write are not offered.
    /// </summary>
    public static class Converter
    {
        public sealed class Format
        {
            public string Id, Label;
            public bool Lossy, Alpha;
            public Format(string id, string label, bool lossy, bool alpha) { Id = id; Label = label; Lossy = lossy; Alpha = alpha; }
        }

        static readonly Format[] AllImage =
        {
            new Format("jpg", "JPG", true, false), new Format("png", "PNG", false, true), new Format("webp", "WebP", true, true),
            new Format("avif", "AVIF", true, false), new Format("heic", "HEIC", true, false), new Format("gif", "GIF", false, true),
            new Format("tiff", "TIFF", false, true), new Format("bmp", "BMP", false, false), new Format("ico", "ICO", false, true)
        };

        public static readonly string[] VideoFormats = { "mp4", "webm", "mov", "mkv", "gif" };
        public static readonly string[] AudioFormats = { "mp3", "m4a", "wav", "flac", "ogg", "opus" };

        static HashSet<string> ffEncoders;
        static bool? heic;

        /// <summary>Encoders the ffmpeg on this PC has (cached).</summary>
        static HashSet<string> FfEncoders()
        {
            if (ffEncoders != null) return ffEncoders;
            var set = new HashSet<string>();
            string ff = FileOps.FindFfmpeg();
            if (ff != null)
            {
                try
                {
                    var psi = new ProcessStartInfo(ff, "-hide_banner -encoders") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using (Process p = Process.Start(psi))
                    {
                        string text = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(10000);
                        foreach (string line in text.Split('\n'))
                        {
                            string[] cols = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (cols.Length >= 2 && cols[0].Length == 6) set.Add(cols[1]);
                        }
                    }
                }
                catch (Exception ex) { Log.Error("ffmpeg encoders", ex); }
            }
            ffEncoders = set;
            return set;
        }

        public static bool HasEncoder(string name) { return FfEncoders().Contains(name); }

        static string AvifEncoder()
        {
            if (HasEncoder("libsvtav1")) return "libsvtav1";
            if (HasEncoder("libaom-av1")) return "libaom-av1";
            return null;
        }

        /// <summary>Can this PC write the picture format?</summary>
        public static bool CanWrite(string format)
        {
            switch (format)
            {
                case "webp": return HasEncoder("libwebp");
                case "avif": return AvifEncoder() != null;
                case "heic":
                    if (heic == null) { try { heic = WinRt.EncodeHeic(SamplePng(), 0.8).Length > 0; } catch (Exception) { heic = false; } }
                    return heic.Value;
                default: return AllImage.Any(f => f.Id == format);
            }
        }

        public static bool CanWriteMedia(string format)
        {
            if (FileOps.FindFfmpeg() == null) return false;
            switch (format)
            {
                case "webm": return HasEncoder("libvpx-vp9");
                case "opus": return HasEncoder("libopus");
                case "mp3": return HasEncoder("libmp3lame");
                case "mp4":
                case "mov":
                case "mkv": return HasEncoder("libx264");
                default: return true;
            }
        }

        /// <summary>Picture formats this PC can write, in menu order.</summary>
        public static List<Format> ImageFormats() { return AllImage.Where(f => CanWrite(f.Id)).ToList(); }

        public static Format Describe(string id) { return AllImage.FirstOrDefault(f => f.Id == id); }

        static byte[] SamplePng()
        {
            using (var b = new Bitmap(8, 8))
            using (var ms = new MemoryStream())
            {
                using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.SteelBlue);
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>Check the slow-to-ask encoders once in the background.</summary>
        public static void WarmUp() { CanWrite("heic"); FfEncoders(); }

        static string Ext(string format) { return format == "jpeg" ? ".jpg" : "." + format; }

        /// <summary>Is the file already in this format (so converting to it means nothing)?</summary>
        public static bool AlreadyIs(string file, string format)
        {
            string e = Path.GetExtension(file).ToLowerInvariant();
            if (e == Ext(format)) return true;
            if (format == "jpg") return e == ".jpeg" || e == ".jfif";
            if (format == "tiff") return e == ".tif";
            if (format == "heic") return e == ".heif";
            return false;
        }

        // ---------- pictures ----------

        /// <summary>Convert pictures (folders contribute their pictures). Returns the new files.</summary>
        public static List<string> ConvertImages(IList<string> paths, ConvertOptions o)
        {
            if (!CanWrite(o.Format)) throw new InvalidOperationException(o.Format.ToUpperInvariant() + " pictures cannot be written on this PC.");
            var work = new List<KeyValuePair<string, string>>(); // file -> output folder
            foreach (string p in paths)
            {
                bool isDir = Directory.Exists(p);
                foreach (string file in FileOps.ImagesIn(new[] { p }))
                {
                    if (AlreadyIs(file, o.Format) && o.MaxSide == 0 && o.TargetKB == 0) continue;
                    string dir = isDir && o.Output == "next" ? Path.Combine(p, "Converted " + o.Format.ToUpperInvariant()) : o.FolderFor(file);
                    work.Add(new KeyValuePair<string, string>(file, dir));
                }
            }
            if (work.Count == 0) throw new InvalidOperationException("Nothing to convert (already in that format).");
            var outputs = new List<string>();
            var missed = new List<string>();
            using (UndoLog.Begin("Convert to " + o.Format.ToUpperInvariant()))
                Batch.Each(work, w => Path.GetFileName(w.Key), delegate (KeyValuePair<string, string> w)
                {
                    if (!Directory.Exists(w.Value)) UndoLog.EnsureFolder(w.Value);
                    string stem = Path.GetFileNameWithoutExtension(w.Key) + (AlreadyIs(w.Key, o.Format) ? "_converted" : "");
                    string dest = FileOps.UniquePath(w.Value, stem, Ext(o.Format));
                    bool reached = ConvertOne(w.Key, dest, o);
                    UndoLog.Created(dest);
                    outputs.Add(dest);
                    if (!reached) missed.Add(Path.GetFileName(dest));
                });
            if (outputs.Count == 0) throw Batch.Nothing("Nothing could be converted.");
            LastMissedTarget = missed;
            return outputs;
        }

        /// <summary>Files from the last conversion that could not get under the size target.</summary>
        [ThreadStatic] public static List<string> LastMissedTarget;

        /// <summary>Write one converted picture. Returns false if a size target could not be met.</summary>
        public static bool ConvertOne(string file, string dest, ConvertOptions o)
        {
            using (Image loaded = FileOps.LoadImage(file))
            {
                Image src = loaded;
                Bitmap scaled = null;
                try
                {
                    if (o.MaxSide > 0 && Math.Max(src.Width, src.Height) > o.MaxSide)
                    {
                        double s = (double)o.MaxSide / Math.Max(src.Width, src.Height);
                        scaled = FileOps.Scaled(src, Math.Max(1, (int)Math.Round(src.Width * s)), Math.Max(1, (int)Math.Round(src.Height * s)));
                        src = scaled;
                    }
                    Format f = Describe(o.Format);
                    if (f == null) throw new ArgumentException("Unknown format " + o.Format);
                    if (!f.Lossy)
                    {
                        if (o.Format == "ico") WriteIco(src, dest);
                        else FileOps.SaveAs(src, dest, o.Format);
                        return o.TargetKB == 0 || new FileInfo(dest).Length <= o.TargetKB * 1024L;
                    }
                    if (o.TargetKB <= 0) { File.WriteAllBytes(dest, Encode(src, o.Format, o.Quality)); return true; }
                    return EncodeToTarget(src, dest, o);
                }
                finally { if (scaled != null) scaled.Dispose(); }
            }
        }

        /// <summary>Find the best quality that fits the size target (and shrink the picture if even low quality is too big).</summary>
        static bool EncodeToTarget(Image src, string dest, ConvertOptions o)
        {
            long target = o.TargetKB * 1024L;
            Image cur = src;
            var made = new List<Bitmap>();
            try
            {
                for (int round = 0; round < 6; round++)
                {
                    int lo = 25, hi = Math.Max(30, o.Quality);
                    byte[] best = null;
                    while (lo <= hi)
                    {
                        Job.CheckCancel();
                        int q = (lo + hi) / 2;
                        byte[] b = Encode(cur, o.Format, q);
                        if (b.Length <= target) { best = b; lo = q + 1; } else hi = q - 1;
                    }
                    if (best != null) { File.WriteAllBytes(dest, best); return true; }
                    var smaller = FileOps.Scaled(cur, Math.Max(1, (int)(cur.Width * 0.8)), Math.Max(1, (int)(cur.Height * 0.8)));
                    made.Add(smaller);
                    cur = smaller;
                }
                File.WriteAllBytes(dest, Encode(cur, o.Format, 25));
                return false;
            }
            finally { foreach (Bitmap b in made) b.Dispose(); }
        }

        /// <summary>A lossy encoding of the picture in memory.</summary>
        public static byte[] Encode(Image img, string format, int quality)
        {
            switch (format)
            {
                case "jpg":
                    using (Bitmap rgb = FileOps.ToRgb(img, 0)) return FileOps.EncodeJpeg(rgb, quality);
                case "heic":
                    return WinRt.EncodeHeic(PngBytes(img, false), quality / 100.0);
                case "webp":
                    return ViaFfmpeg(img, ".webp", "-c:v libwebp -quality " + quality + " -compression_level 4", true);
                case "avif":
                    {
                        string enc = AvifEncoder();
                        int crf = (int)Math.Round(58 - quality * 0.4); // 90 → 22, 70 → 30, 40 → 42
                        string args = enc == "libsvtav1"
                            ? "-c:v libsvtav1 -crf " + crf + " -preset 8 -still-picture 1 -pix_fmt yuv420p"
                            : "-c:v libaom-av1 -crf " + crf + " -b:v 0 -cpu-used 6 -still-picture 1 -pix_fmt yuv420p";
                        return ViaFfmpeg(img, ".avif", args, false);
                    }
            }
            throw new ArgumentException("Unknown lossy format " + format);
        }

        static byte[] PngBytes(Image img, bool keepAlpha)
        {
            using (var ms = new MemoryStream())
            {
                if (keepAlpha) img.Save(ms, ImageFormat.Png);
                else using (Bitmap rgb = FileOps.ToRgb(img, 0)) rgb.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static byte[] ViaFfmpeg(Image img, string ext, string args, bool keepAlpha)
        {
            string ff = FileOps.FindFfmpeg();
            if (ff == null) throw new InvalidOperationException("ffmpeg is needed for " + ext + " files.");
            string dir = AppPaths.LocalDir("convert");
            string id = Guid.NewGuid().ToString("N");
            string inPng = Path.Combine(dir, id + ".png"), outFile = Path.Combine(dir, id + ext);
            try
            {
                File.WriteAllBytes(inPng, PngBytes(img, keepAlpha));
                FileOps.RunTool(ff, "-y -hide_banner -loglevel error -i \"" + inPng + "\" " + args + " \"" + outFile + "\"", "ffmpeg failed");
                if (!File.Exists(outFile)) throw new InvalidOperationException("ffmpeg produced no output.");
                return File.ReadAllBytes(outFile);
            }
            finally
            {
                try { File.Delete(inPng); } catch (Exception) { }
                try { File.Delete(outFile); } catch (Exception) { }
            }
        }

        /// <summary>A Windows icon with 16, 24, 32, 48, 64, 128 and 256 pixel images (stored as PNG, as Windows allows).</summary>
        public static void WriteIco(Image src, string dest)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                double s = Math.Min((double)size / src.Width, (double)size / src.Height);
                int w = Math.Max(1, (int)Math.Round(src.Width * s)), h = Math.Max(1, (int)Math.Round(src.Height * s));
                using (var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
                    }
                    using (var ms = new MemoryStream()) { canvas.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
                }
            }
            using (var fs = new FileStream(dest, FileMode.CreateNew))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(images[i].Length);
                    bw.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] b in images) bw.Write(b);
            }
        }

        // ---------- video and audio ----------

        /// <summary>ffmpeg arguments for a video or audio conversion with these options.</summary>
        public static string MediaArgs(string format, ConvertOptions o, bool sourceHasVideo)
        {
            string scale = o.Height > 0 ? "scale=-2:" + o.Height : null;
            int x264 = o.VideoQuality == "high" ? 18 : o.VideoQuality == "small" ? 28 : 23;
            int vp9 = o.VideoQuality == "high" ? 24 : o.VideoQuality == "small" ? 40 : 32;
            string audio = o.NoAudio ? "-an" : null;
            string br = o.Bitrate + "k";
            switch (format)
            {
                case "mp4":
                case "mov":
                case "mkv":
                    return Join(scale == null ? null : "-vf " + scale, "-c:v libx264 -preset medium -crf " + x264 + " -pix_fmt yuv420p",
                        audio ?? "-c:a aac -b:a 160k", format == "mp4" || format == "mov" ? "-movflags +faststart" : null);
                case "webm":
                    return Join(scale == null ? null : "-vf " + scale, "-c:v libvpx-vp9 -crf " + vp9 + " -b:v 0 -row-mt 1", audio ?? "-c:a libopus -b:a 128k");
                case "gif":
                    return "-vf \"fps=12,scale=" + (o.Height > 0 ? "-2:" + o.Height : "480:-1") + ":flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0";
                case "mp3": return Join(sourceHasVideo ? "-vn" : null, "-codec:a libmp3lame -b:a " + br);
                case "m4a": return Join(sourceHasVideo ? "-vn" : null, "-c:a aac -b:a " + br);
                case "ogg": return Join(sourceHasVideo ? "-vn" : null, "-c:a libvorbis -b:a " + br);
                case "opus": return Join(sourceHasVideo ? "-vn" : null, "-c:a libopus -b:a " + br);
                case "flac": return Join(sourceHasVideo ? "-vn" : null, "-c:a flac");
                case "wav": return Join(sourceHasVideo ? "-vn" : null, "-c:a pcm_s16le");
            }
            throw new ArgumentException("Unknown format " + format);
        }

        static string Join(params string[] parts) { return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)).ToArray()); }

        /// <summary>Convert video or audio files. Returns the new files.</summary>
        public static List<string> ConvertMedia(IList<string> paths, ConvertOptions o)
        {
            if (!CanWriteMedia(o.Format)) throw new InvalidOperationException(o.Format.ToUpperInvariant() + " cannot be written on this PC (ffmpeg or its encoder is missing).");
            var outputs = new List<string>();
            using (UndoLog.Begin("Convert to " + o.Format.ToUpperInvariant()))
                Batch.Each(paths.Where(File.Exists).ToList(), delegate (string file)
                {
                    bool video = FileOps.Classify(file) == FileKind.Video;
                    string dir = o.FolderFor(file);
                    if (!Directory.Exists(dir)) UndoLog.EnsureFolder(dir);
                    string stem = Path.GetFileNameWithoutExtension(file) + (AlreadyIs(file, o.Format) ? "_converted" : "");
                    string dest = FileOps.UniquePath(dir, stem, Ext(o.Format));
                    FileOps.RunFfmpegTo(FileOps.FindFfmpeg(), "-i \"" + file + "\" " + MediaArgs(o.Format, o, video), dest);
                    if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                    UndoLog.Created(dest);
                    outputs.Add(dest);
                });
            if (outputs.Count == 0) throw Batch.Nothing("Nothing could be converted.");
            return outputs;
        }
    }
}
