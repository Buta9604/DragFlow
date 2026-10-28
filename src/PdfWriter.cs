using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DragFlow
{
    public sealed class PdfPage
    {
        public byte[] Jpeg;
        public int PxWidth, PxHeight;
        /// <summary>Multiplier from pixels to page size (1 = 96 dpi; 0.5 for pages rendered at double size).</summary>
        public double PointScale = 1;
    }

    /// <summary>Minimal PDF writer: one JPEG image per page, no dependencies.</summary>
    public static class PdfWriter
    {
        const double MaxPoints = 842; // longest page side (A4 height)

        public static void Write(string path, IList<PdfPage> pages)
        {
            if (pages.Count == 0) throw new ArgumentException("No pages");
            var offsets = new List<long>();
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                Action<string> w = delegate (string s)
                {
                    byte[] b = Encoding.ASCII.GetBytes(s);
                    fs.Write(b, 0, b.Length);
                };
                Action<int> mark = delegate (int obj)
                {
                    while (offsets.Count < obj) offsets.Add(0);
                    offsets[obj - 1] = fs.Position;
                };

                w("%PDF-1.4\n");

                mark(1);
                w("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                var kids = new StringBuilder();
                for (int i = 0; i < pages.Count; i++) kids.Append(3 + 3 * i).Append(" 0 R ");
                mark(2);
                w("2 0 obj\n<< /Type /Pages /Count " + pages.Count + " /Kids [" + kids + "] >>\nendobj\n");

                for (int i = 0; i < pages.Count; i++)
                {
                    PdfPage p = pages[i];
                    int pageObj = 3 + 3 * i, contentObj = 4 + 3 * i, imageObj = 5 + 3 * i;
                    double wpt = p.PxWidth * 0.75 * p.PointScale, hpt = p.PxHeight * 0.75 * p.PointScale;
                    double longest = Math.Max(wpt, hpt);
                    if (longest > MaxPoints) { wpt *= MaxPoints / longest; hpt *= MaxPoints / longest; }
                    string ws = wpt.ToString("0.##", CultureInfo.InvariantCulture);
                    string hs = hpt.ToString("0.##", CultureInfo.InvariantCulture);

                    mark(pageObj);
                    w(pageObj + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + ws + " " + hs + "] " +
                      "/Resources << /XObject << /Im0 " + imageObj + " 0 R >> >> /Contents " + contentObj + " 0 R >>\nendobj\n");

                    string content = "q " + ws + " 0 0 " + hs + " 0 0 cm /Im0 Do Q";
                    mark(contentObj);
                    w(contentObj + " 0 obj\n<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\nendobj\n");

                    mark(imageObj);
                    w(imageObj + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + p.PxWidth + " /Height " + p.PxHeight +
                      " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + p.Jpeg.Length + " >>\nstream\n");
                    fs.Write(p.Jpeg, 0, p.Jpeg.Length);
                    w("\nendstream\nendobj\n");
                }

                long xref = fs.Position;
                w("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long o in offsets) w(o.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                w("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }
    }
}
