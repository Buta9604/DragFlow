using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DragFlow.Tests
{
    /// <summary>
    /// For the end-to-end script: where a slice and one of its options sit on the dial (96-dpi pixels from the dial
    /// centre), so the script never hard-codes positions. Usage: DragFlow.Tests.exe --layout image "Rotate" "Right"
    /// </summary>
    static class LayoutInfo
    {
        public static void Print(string[] args)
        {
            string kind = args.Length > 1 ? args[1] : "image", slice = args.Length > 2 ? args[2] : "", chip = args.Length > 3 ? args[3] : null;
            string dir = Path.Combine(Path.GetTempPath(), "dragflow-layout");
            Directory.CreateDirectory(dir);
            string sample = kind == "image" ? Make.Png(dir, "sample.png") : Make.Text(dir, "sample." + kind);
            List<ActionItem> items = ActionCatalog.For(new[] { sample });
            int index = items.FindIndex(i => string.Equals(i.Label, slice, StringComparison.OrdinalIgnoreCase));
            var geo = new RadialGeometry(items.Count, 1.0);
            var ci = CultureInfo.InvariantCulture;
            double ix = 0, iy = 0;
            if (index >= 0) geo.IconCenter(index, out ix, out iy);
            Console.WriteLine("slices=" + items.Count + " index=" + index + " x=" + ix.ToString("0.0", ci) + " y=" + iy.ToString("0.0", ci) + " half=" + geo.HalfSize);
            if (index < 0 || chip == null) return;
            ActionItem parent = items[index];
            int j = parent.Children.FindIndex(c => string.Equals(c.Label, chip, StringComparison.OrdinalIgnoreCase));
            if (j < 0) { Console.WriteLine("chip=-1"); return; }
            RectangleF r = geo.LayoutPills(index, RadialRenderer.MeasurePills(parent.Children, 1.0))[j];
            Console.WriteLine("chip=" + j + " x=" + (r.X + r.Width / 2).ToString("0.0", ci) + " y=" + (r.Y + r.Height / 2).ToString("0.0", ci));
        }
    }
}
