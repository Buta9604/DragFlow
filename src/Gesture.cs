using System;
using System.Collections.Generic;
using System.Linq;

namespace DragFlow
{
    /// <summary>
    /// Recognises a quick shape drawn while dragging, after the dial has got out of the way: Z, N, V or L. The pointer's
    /// recent path is cut into steps of a fixed length, each step gets one of eight directions, and the sequence of
    /// directions (short wobbles dropped) is compared with each shape's strokes. Pure logic, so it is tested without a mouse.
    /// </summary>
    public sealed class Gesture
    {
        public const string DefaultMap = "Z=Zip|V=To PDF|L=Shelf|N=Rename";
        public static readonly string[] Shapes = { "Z", "V", "L", "N" };

        // Directions: 0 = right, 1 = down-right, 2 = down, 3 = down-left, 4 = left, 5 = up-left, 6 = up, 7 = up-right (screen coordinates).
        // Each shape is a list of strokes; each stroke accepts the ideal direction and how people tend to draw it.
        // Three-stroke shapes are checked first, so a steep Z is never taken for an L.
        static readonly KeyValuePair<string, int[][]>[] Strokes =
        {
            new KeyValuePair<string, int[][]>("Z", new[] { new[] { 0 }, new[] { 3, 2 }, new[] { 0 } }),
            new KeyValuePair<string, int[][]>("N", new[] { new[] { 6 }, new[] { 1, 2 }, new[] { 6 } }),
            new KeyValuePair<string, int[][]>("V", new[] { new[] { 1, 2 }, new[] { 7, 6 } }),
            new KeyValuePair<string, int[][]>("L", new[] { new[] { 2 }, new[] { 0 } })
        };

        struct P { public double X, Y; public long T; }

        readonly List<P> points = new List<P>();
        readonly double step;
        long lastMatchAt = -1000000; // not long.MinValue: "t - lastMatchAt" would overflow

        /// <summary>Only the last moments of the drag count.</summary>
        public int WindowMs = 1600;
        /// <summary>A shape must be at least this big (pixels at 100 % scale) so ordinary drag paths do not count.</summary>
        public double MinSize = 90;

        public Gesture(double scale)
        {
            step = 14 * Math.Max(0.5, scale);
            MinSize *= Math.Max(0.5, scale);
        }

        public void Reset() { points.Clear(); }

        /// <summary>Add a pointer position; returns the shape just completed ("Z"…), or null.</summary>
        public string Add(double x, double y, long t)
        {
            if (points.Count > 0)
            {
                P last = points[points.Count - 1];
                if ((x - last.X) * (x - last.X) + (y - last.Y) * (y - last.Y) < step * step) return null;
            }
            points.Add(new P { X = x, Y = y, T = t });
            while (points.Count > 2 && points[0].T < t - WindowMs) points.RemoveAt(0);
            if (t - lastMatchAt < 800) return null; // one shape at a time
            string shape = Match();
            if (shape != null) { lastMatchAt = t; points.Clear(); points.Add(new P { X = x, Y = y, T = t }); }
            return shape;
        }

        /// <summary>The direction runs of the recent path (direction, index of the point where it starts), one-step wobbles removed.</summary>
        internal List<KeyValuePair<int, int>> Runs()
        {
            var runs = new List<KeyValuePair<int, int>>(); // direction, start point
            var lengths = new List<int>();
            for (int i = 1; i < points.Count; i++)
            {
                double a = Math.Atan2(points[i].Y - points[i - 1].Y, points[i].X - points[i - 1].X);
                int d = (((int)Math.Round(a / (Math.PI / 4))) % 8 + 8) % 8;
                if (runs.Count > 0 && runs[runs.Count - 1].Key == d) lengths[lengths.Count - 1]++;
                else { runs.Add(new KeyValuePair<int, int>(d, i - 1)); lengths.Add(1); }
            }
            var merged = new List<KeyValuePair<int, int>>();
            for (int i = 0; i < runs.Count; i++)
            {
                if (lengths[i] < 2) continue;
                if (merged.Count == 0 || merged[merged.Count - 1].Key != runs[i].Key) merged.Add(runs[i]);
            }
            return merged;
        }

        string Match()
        {
            if (points.Count < 6) return null;
            List<KeyValuePair<int, int>> runs = Runs();
            foreach (var shape in Strokes)
            {
                int[][] strokes = shape.Value;
                if (runs.Count < strokes.Length) continue;
                bool ok = true;
                for (int i = 0; i < strokes.Length && ok; i++) ok = strokes[i].Contains(runs[runs.Count - strokes.Length + i].Key);
                if (!ok) continue;
                // The shape itself (not the drag before it) must be big enough and really two-dimensional:
                // straight down and back up is not a V.
                // The first stroke may continue a straight drag that came before: count only about as much of it as the next stroke.
                int first = runs[runs.Count - strokes.Length].Value, second = runs[runs.Count - strokes.Length + 1].Value;
                int third = strokes.Length > 2 ? runs[runs.Count - strokes.Length + 2].Value : points.Count - 1;
                int from = Math.Max(first, second - (int)Math.Ceiling((third - second) * 1.5));
                var part = points.Skip(from).ToList();
                double w = part.Max(p => p.X) - part.Min(p => p.X), h = part.Max(p => p.Y) - part.Min(p => p.Y);
                if (Math.Max(w, h) < MinSize || Math.Min(w, h) < 0.35 * Math.Max(w, h)) continue;
                return shape.Key;
            }
            return null;
        }

        /// <summary>The action key a shape is set to ("Zip"), from a map like "Z=Zip|V=To PDF".</summary>
        public static string ActionFor(string map, string shape)
        {
            foreach (string part in (map ?? "").Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq).Trim(), shape, StringComparison.OrdinalIgnoreCase))
                {
                    string action = part.Substring(eq + 1).Trim();
                    return action.Length > 0 ? action : null;
                }
            }
            return null;
        }

        /// <summary>The action in a dial menu (slices and their options) that a key names, or null when this menu has none.</summary>
        public static ActionItem Find(IList<ActionItem> menu, string key)
        {
            if (menu == null || string.IsNullOrEmpty(key)) return null;
            foreach (ActionItem i in menu)
            {
                if (!i.IsParent && i.Matches(key)) return i;
                foreach (ActionItem k in i.Children) if (k.Matches(key)) return k;
            }
            return null;
        }
    }
}
