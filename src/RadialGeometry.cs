using System;
using System.Drawing;

namespace DragFlow
{
    /// <summary>
    /// Pure layout and hit-testing for the dial. Coordinates are relative to the anchor (dial centre),
    /// sizes are 96-dpi pixels multiplied by Scale.
    /// </summary>
    public sealed class RadialGeometry
    {
        public const double InnerR = 30, OuterR = 92, IconR = 61, ChipGap = 8, ChipH = 28, ChipSpacing = 6, Margin = 24;

        public readonly int Count;
        public readonly double Scale;

        public RadialGeometry(int count, double scale)
        {
            Count = Math.Max(1, count);
            Scale = scale;
        }

        public double Inner { get { return InnerR * Scale; } }
        public double Outer { get { return OuterR * Scale; } }
        public double IconRadius { get { return IconR * Scale; } }

        /// <summary>Half the side of the square window: dial + widest chip row + shadow.</summary>
        public int HalfSize { get { return (int)Math.Ceiling((OuterR + ChipGap + 120 + Margin) * Scale); } }

        /// <summary>Segment whose slice contains the angle; slices are centred on RingLayout.Angle.</summary>
        public int SegmentForAngle(double angle)
        {
            double step = 2 * Math.PI / Count;
            double rel = angle - RingLayout.Angle(0, Count) + step / 2;
            rel %= 2 * Math.PI;
            if (rel < 0) rel += 2 * Math.PI;
            return Math.Min(Count - 1, (int)(rel / step));
        }

        /// <summary>The slice under a point, or -1 in the see-through centre or outside the dial.</summary>
        public int SegmentAt(double dx, double dy)
        {
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < Inner || d > Outer + 4 * Scale) return -1;
            return SegmentForAngle(Math.Atan2(dy, dx));
        }

        public void IconCenter(int index, out double dx, out double dy)
        {
            RingLayout.Center(0, 0, index, Count, IconRadius, out dx, out dy);
        }

        /// <summary>
        /// Pills beside a slice, laid out like a small submenu: slices that point mostly sideways get a
        /// same-width column (left- or right-aligned to the dial), slices that point up or down get a centred row.
        /// The near edge always sits ChipGap outside the dial, so pills never touch it or each other.
        /// </summary>
        public RectangleF[] LayoutPills(int segment, float[] widths)
        {
            double theta = RingLayout.Angle(segment, Count);
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            double h = ChipH * Scale, gap = ChipSpacing * Scale, edge = Outer + ChipGap * Scale;
            double ix, iy;
            IconCenter(segment, out ix, out iy);
            int n = widths.Length;
            var rects = new RectangleF[n];

            if (Math.Abs(cos) > 0.35)
            {
                float w = 0;
                foreach (float x in widths) w = Math.Max(w, x);
                if (n > 1) w = Math.Max(w, (float)(64 * Scale));
                double total = n * h + (n - 1) * gap;
                double top = Math.Max(-HalfSize + Margin * Scale, Math.Min(HalfSize - Margin * Scale - total, iy - total / 2));
                double left = cos > 0 ? edge : -edge - w;
                for (int i = 0; i < n; i++)
                    rects[i] = new RectangleF((float)left, (float)(top + i * (h + gap)), n > 1 ? w : widths[i], (float)h);
                if (n == 1 && cos < 0) rects[0].X = (float)(-edge - widths[0]);
            }
            else
            {
                // A centred row; long lists wrap onto further rows, stacking away from the dial.
                double avail = 2 * (HalfSize - Margin * Scale);
                var rows = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
                var cur = new System.Collections.Generic.List<int>();
                double curW = 0;
                for (int i = 0; i < n; i++)
                {
                    double add = widths[i] + (cur.Count > 0 ? gap : 0);
                    if (cur.Count > 0 && curW + add > avail) { rows.Add(cur); cur = new System.Collections.Generic.List<int>(); curW = 0; add = widths[i]; }
                    cur.Add(i);
                    curW += add;
                }
                rows.Add(cur);
                for (int r = 0; r < rows.Count; r++)
                {
                    double total = (rows[r].Count - 1) * gap;
                    foreach (int i in rows[r]) total += widths[i];
                    double y = sin < 0 ? -edge - h - r * (h + gap) : edge + r * (h + gap);
                    double pos = Math.Max(-HalfSize + Margin * Scale, Math.Min(HalfSize - Margin * Scale - total, ix - total / 2));
                    foreach (int i in rows[r])
                    {
                        rects[i] = new RectangleF((float)pos, (float)y, widths[i], (float)h);
                        pos += widths[i] + gap;
                    }
                }
            }
            return rects;
        }
    }
}
