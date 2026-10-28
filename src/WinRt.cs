using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace DragFlow
{
    /// <summary>
    /// Windows' own OCR and PDF engines (built into Windows 10/11, nothing to install).
    /// Calls are made synchronously: the C# 5 compiler that ships with Windows cannot await WinRT operations,
    /// and these always run on background threads anyway.
    /// </summary>
    public static class WinRt
    {
        static T Wait<T>(IAsyncOperation<T> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(5);
            if (op.Status != AsyncStatus.Completed) throw Failure(op.ErrorCode);
            return op.GetResults();
        }

        static void Wait(IAsyncAction op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(5);
            if (op.Status != AsyncStatus.Completed) throw Failure(op.ErrorCode);
        }

        static Exception Failure(Exception inner)
        {
            return new InvalidOperationException(inner == null ? "Windows could not finish that." : inner.Message, inner);
        }

        static OcrEngine Engine()
        {
            OcrEngine e = OcrEngine.TryCreateFromUserProfileLanguages();
            if (e == null) throw new InvalidOperationException("No OCR language is installed. Add one in Settings > Time & language > Language.");
            return e;
        }

        public static bool OcrAvailable()
        {
            try { return OcrEngine.TryCreateFromUserProfileLanguages() != null; }
            catch (Exception) { return false; }
        }

        /// <summary>Text from images, one block per image, line breaks kept.</summary>
        public static string OcrImages(IList<string> images)
        {
            OcrEngine engine = Engine();
            var parts = new List<string>();
            foreach (string path in images)
            {
                Job.Step(Path.GetFileName(path));
                StorageFile f = Wait(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
                using (IRandomAccessStream s = Wait(f.OpenAsync(FileAccessMode.Read)))
                    parts.Add(Recognize(engine, s));
            }
            return string.Join("\r\n\r\n", parts.Where(p => p.Length > 0).ToArray());
        }

        /// <summary>Text from a scanned or normal PDF by rendering each page and reading it (first 50 pages).</summary>
        public static string OcrPdf(string pdf) { return OcrPdf(pdf, 50); }

        /// <summary>Text from the first pages of a PDF only.</summary>
        public static string OcrPdf(string pdf, uint maxPages)
        {
            OcrEngine engine = Engine();
            WinPdf.PdfDocument doc = LoadPdf(pdf);
            var parts = new List<string>();
            uint pages = Math.Min(doc.PageCount, maxPages);
            for (uint i = 0; i < pages; i++)
            {
                Job.Step("page " + (i + 1));
                using (WinPdf.PdfPage page = doc.GetPage(i))
                using (var mem = new InMemoryRandomAccessStream())
                {
                    var opt = new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * 2) };
                    Wait(page.RenderToStreamAsync(mem, opt));
                    mem.Seek(0);
                    parts.Add(Recognize(engine, mem));
                }
            }
            return string.Join("\r\n\r\n", parts.Where(p => p.Length > 0).ToArray());
        }

        /// <summary>Every page as a PNG (about 150 dpi) in a "&lt;name&gt; pages" folder next to the PDF.</summary>
        public static string PdfToImages(string pdf) { return PdfToImages(pdf, "png"); }

        /// <summary>Every page as a PNG or JPG (about 150 dpi) in a "&lt;name&gt; pages" folder next to the PDF.</summary>
        public static string PdfToImages(string pdf, string format)
        {
            bool jpg = format == "jpg";
            WinPdf.PdfDocument doc = LoadPdf(pdf);
            string dir = Path.GetDirectoryName(Path.GetFullPath(pdf));
            string outDir = FileOps.UniquePath(dir, Path.GetFileNameWithoutExtension(pdf) + " pages", "");
            Directory.CreateDirectory(outDir);
            using (UndoLog.Begin("PDF to images")) UndoLog.Created(outDir);
            StorageFolder folder = Wait(StorageFolder.GetFolderFromPathAsync(outDir));
            int width = Math.Max(2, doc.PageCount.ToString().Length);
            Job.SetTotal((int)doc.PageCount);
            for (uint i = 0; i < doc.PageCount; i++)
            {
                Job.Step("page " + (i + 1));
                Job.Advance();
                using (WinPdf.PdfPage page = doc.GetPage(i))
                {
                    StorageFile file = Wait(folder.CreateFileAsync("page " + (i + 1).ToString().PadLeft(width, '0') + (jpg ? ".jpg" : ".png"), CreationCollisionOption.GenerateUniqueName));
                    using (IRandomAccessStream s = Wait(file.OpenAsync(FileAccessMode.ReadWrite)))
                    {
                        var opt = new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * 1.5625) };
                        if (jpg) opt.BitmapEncoderId = BitmapEncoder.JpegEncoderId;
                        Wait(page.RenderToStreamAsync(s, opt));
                    }
                }
            }
            return outDir;
        }

        public static uint PdfPageCount(string pdf) { return LoadPdf(pdf).PageCount; }

        /// <summary>Each page as PNG bytes at <paramref name="scale"/> times its natural size (first 300 pages).</summary>
        public static List<byte[]> PdfPagesAsPng(string pdf, double scale) { return PdfPagesAsPng(pdf, scale, 300); }

        public static List<byte[]> PdfPagesAsPng(string pdf, double scale, int maxPages)
        {
            WinPdf.PdfDocument doc = LoadPdf(pdf);
            var pages = new List<byte[]>();
            uint n = Math.Min(doc.PageCount, (uint)Math.Max(1, maxPages));
            for (uint i = 0; i < n; i++)
            {
                Job.CheckCancel();
                using (WinPdf.PdfPage page = doc.GetPage(i))
                using (var mem = new InMemoryRandomAccessStream())
                {
                    var opt = new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale) };
                    Wait(page.RenderToStreamAsync(mem, opt));
                    pages.Add(ReadAll(mem));
                }
            }
            return pages;
        }

        /// <summary>Decode any image Windows has a codec for (WebP, HEIC, AVIF, JPEG XR…) and return it as PNG bytes.</summary>
        public static byte[] DecodeToPng(string path)
        {
            StorageFile f = Wait(StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
            using (IRandomAccessStream s = Wait(f.OpenAsync(FileAccessMode.Read)))
            {
                BitmapDecoder d;
                try { d = Wait(BitmapDecoder.CreateAsync(s)); }
                catch (Exception)
                {
                    throw new InvalidOperationException("Windows has no codec for " + Path.GetExtension(path) +
                        " files. Install the matching extension (for example \"HEIF Image Extensions\") from the Microsoft Store.");
                }
                SoftwareBitmap bmp = Wait(d.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb));
                using (bmp)
                using (var mem = new InMemoryRandomAccessStream())
                {
                    BitmapEncoder e = Wait(BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, mem));
                    e.SetSoftwareBitmap(bmp);
                    Wait(e.FlushAsync());
                    return ReadAll(mem);
                }
            }
        }

        /// <summary>Encode PNG bytes as HEIC with Windows' own encoder (needs the HEIF and HEVC extensions). quality 0–1.</summary>
        public static byte[] EncodeHeic(byte[] png, double quality)
        {
            using (var input = new InMemoryRandomAccessStream())
            {
                using (var w = new DataWriter(input))
                {
                    w.WriteBytes(png);
                    Wait(w.StoreAsync());
                    w.DetachStream();
                }
                input.Seek(0);
                BitmapDecoder d = Wait(BitmapDecoder.CreateAsync(input));
                SoftwareBitmap bmp = Wait(d.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
                using (bmp)
                using (var output = new InMemoryRandomAccessStream())
                {
                    var props = new BitmapPropertySet();
                    props.Add("ImageQuality", new BitmapTypedValue((float)Math.Max(0.01, Math.Min(1.0, quality)), Windows.Foundation.PropertyType.Single));
                    BitmapEncoder e = Wait(BitmapEncoder.CreateAsync(BitmapEncoder.HeifEncoderId, output, props));
                    e.SetSoftwareBitmap(bmp);
                    Wait(e.FlushAsync());
                    return ReadAll(output);
                }
            }
        }

        static byte[] ReadAll(IRandomAccessStream mem)
        {
            var bytes = new byte[mem.Size];
            using (var reader = new DataReader(mem.GetInputStreamAt(0)))
            {
                Wait(reader.LoadAsync((uint)mem.Size));
                reader.ReadBytes(bytes);
            }
            return bytes;
        }

        static WinPdf.PdfDocument LoadPdf(string pdf)
        {
            StorageFile f = Wait(StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdf)));
            return Wait(WinPdf.PdfDocument.LoadFromFileAsync(f));
        }

        static string Recognize(OcrEngine engine, IRandomAccessStream stream)
        {
            BitmapDecoder d = Wait(BitmapDecoder.CreateAsync(stream));
            var transform = new BitmapTransform();
            uint max = OcrEngine.MaxImageDimension;
            if (d.OrientedPixelWidth > max || d.OrientedPixelHeight > max)
            {
                double s = (double)max / Math.Max(d.OrientedPixelWidth, d.OrientedPixelHeight);
                transform.ScaledWidth = (uint)(d.PixelWidth * s);
                transform.ScaledHeight = (uint)(d.PixelHeight * s);
            }
            SoftwareBitmap bmp = Wait(d.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage));
            using (bmp)
            {
                OcrResult r = Wait(engine.RecognizeAsync(bmp));
                return string.Join("\r\n", r.Lines.Select(l => l.Text).ToArray());
            }
        }
    }
}
