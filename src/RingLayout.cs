using System;

namespace DragFlow
{
    /// <summary>Pure geometry for the radial menu so the overlay and the intent engine agree on tile positions.</summary>
    public static class RingLayout
    {
        public static double Angle(int index, int count)
        {
            return -Math.PI / 2 + index * 2 * Math.PI / Math.Max(1, count);
        }

        public static void Center(double ax, double ay, int index, int count, double radius, out double x, out double y)
        {
            double a = Angle(index, count);
            x = ax + radius * Math.Cos(a);
            y = ay + radius * Math.Sin(a);
        }

        /// <summary>Children fan outward from the anchor, through their parent tile.</summary>
        public static void ChildCenter(double px, double py, double ax, double ay, int j, int k, double radius, out double x, out double y)
        {
            double baseAngle = Math.Atan2(py - ay, px - ax);
            double a = baseAngle + (j - (k - 1) / 2.0) * 0.7;
            x = px + radius * Math.Cos(a);
            y = py + radius * Math.Sin(a);
        }
    }
}
