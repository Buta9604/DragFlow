using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DragFlow
{
    /// <summary>
    /// What is being dragged, for the middle of the dial: Windows' own thumbnail (pictures, videos, PDFs…) and a detail
    /// such as "4032 × 3024", "12 pages" or "3:25". Made on one background thread so the drag never waits for it.
    /// </summary>
    public static class Preview
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ThumbSize { public int cx, cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(ThumbSize size, int flags, out IntPtr bitmap);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        struct DibSection
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
            public uint f0, f1, f2;
            public IntPtr dshSection;
            public uint dsOffset;
        }

        [DllImport("gdi32.dll")]
        static extern int GetObject(IntPtr handle, int size, ref DibSection ds);

        /// <summary>The bitmap with its transparency (Image.FromHbitmap would turn see-through parts black).</summary>
        static Bitmap WithAlpha(IntPtr hbmp)
        {
            var ds = new DibSection();
            if (GetObject(hbmp, Marshal.SizeOf(typeof(DibSection)), ref ds) == 0 || ds.bmBitsPixel != 32 || ds.bmBits == IntPtr.Zero) return Image.FromHbitmap(hbmp);
            int w = ds.bmWidth, h = Math.Abs(ds.bmHeight), stride = ds.bmWidthBytes;
            var row = new byte[stride];
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            bool anyAlpha = false;
            try
            {
                for (int y = 0; y < h; y++)
                {
                    int srcRow = ds.biHeight > 0 ? h - 1 - y : y; // bottom-up unless the height is negative
                    Marshal.Copy(new IntPtr(ds.bmBits.ToInt64() + (long)srcRow * stride), row, 0, stride);
                    for (int x = 3; x < w * 4; x += 4) if (row[x] != 0) { anyAlpha = true; break; }
                    Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), Math.Min(stride, data.Stride));
                }
            }
            finally { bmp.UnlockBits(data); }
            if (anyAlpha) return bmp;
            bmp.Dispose(); // no transparency at all: an opaque picture
            return Image.FromHbitmap(hbmp);
        }

        static readonly System.Collections.Generic.Queue<Bitmap> recent = new System.Collections.Generic.Queue<Bitmap>();

        /// <summary>Thumbnails of older drags are let go (a dial only ever shows the latest ones).</summary>
        static void Remember(Bitmap b)
        {
            if (b == null) return;
            lock (recent)
            {
                recent.Enqueue(b);
                while (recent.Count > 4) recent.Dequeue().Dispose();
            }
        }

        const int ThumbnailOnly = 0x8, BiggerSizeOk = 0x1, InCacheOnly = 0x10;

        static readonly BlockingCollection<DragSession> queue = new BlockingCollection<DragSession>();
        static Thread worker;

        /// <summary>Work out the preview for a resolved drag (it appears in the dial when ready).</summary>
        public static void Start(DragSession s)
        {
            string[] paths = s.Paths;
            if (paths == null || paths.Length == 0 || !Settings.Current.Preview) return;
            lock (queue)
            {
                if (worker == null)
                {
                    worker = new Thread(Work) { IsBackground = true, Name = "DragFlow preview" };
                    worker.SetApartmentState(ApartmentState.STA); // thumbnail providers expect it
                    worker.Start();
                }
            }
            DragSession stale;
            while (queue.TryTake(out stale)) { } // only the latest drag matters
            queue.Add(s);
        }

        static void Work()
        {
            foreach (DragSession s in queue.GetConsumingEnumerable())
            {
                try
                {
                    string[] paths = s.Paths;
                    if (paths == null || paths.Length == 0) continue;
                    string first = paths[0];
                    if (File.Exists(first)) { Bitmap t = Thumbnail(first, 96); Remember(t); s.Thumb = t; }
                    if (paths.Length == 1) s.Details = Details(first);
                }
                catch (Exception ex) { Log.Debug("preview: " + ex.Message); }
            }
        }

        /// <summary>Windows' thumbnail of a file (no generic icons), or null.</summary>
        public static Bitmap Thumbnail(string path, int size)
        {
            IShellItemImageFactory factory = null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(Path.GetFullPath(path), IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
                // Cloud placeholders: only a thumbnail Windows already has, so nothing downloads.
                int flags = ThumbnailOnly | BiggerSizeOk | (FileOps.IsOnlineOnly(path) ? InCacheOnly : 0);
                if (factory.GetImage(new ThumbSize { cx = size, cy = size }, flags, out hbmp) != 0 || hbmp == IntPtr.Zero) return null;
                using (Image raw = WithAlpha(hbmp))
                {
                    // Keep at most size×size, centred, so the round crop in the dial looks right.
                    double scale = (double)size / Math.Max(raw.Width, raw.Height);
                    int w = Math.Max(1, (int)(raw.Width * Math.Min(1, scale))), h = Math.Max(1, (int)(raw.Height * Math.Min(1, scale)));
                    var bmp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(raw, 0, 0, w, h);
                    }
                    return bmp;
                }
            }
            catch (Exception) { return null; }
            finally
            {
                if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        /// <summary>"4032 × 3024", "12 pages", "3:25", or null.</summary>
        public static string Details(string path)
        {
            if (!File.Exists(path) || FileOps.IsOnlineOnly(path)) return null;
            try
            {
                switch (FileOps.Classify(path))
                {
                    case FileKind.Image:
                        if (FileOps.NeedsWic(path)) return null;
                        using (var fs = File.OpenRead(path))
                        using (Image img = Image.FromStream(fs, false, false)) return img.Width + " × " + img.Height;
                    case FileKind.Pdf:
                        uint pages = WinRt.PdfPageCount(path);
                        return pages == 1 ? "1 page" : pages + " pages";
                    case FileKind.Video:
                    case FileKind.Audio:
                        double d = FileOps.Duration(path);
                        if (d <= 0) return null;
                        var t = TimeSpan.FromSeconds(Math.Round(d));
                        return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes + ":" + t.Seconds.ToString("00");
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
