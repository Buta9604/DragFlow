using System;
using System.Collections.Generic;

namespace DragFlow
{
    public enum IntentState { Observing, Revealing, Dismissed }

    /// <summary>
    /// Decides, from the mouse path, whether the dial should be over the dragged item.
    /// The dial opens as soon as a drag starts (unless the cursor has already left the dial area,
    /// i.e. a decisive flick) and gets out of the way once the cursor moves on past it.
    /// Pure logic: all times are caller-supplied milliseconds so it is fully testable.
    /// </summary>
    public sealed class IntentEngine
    {
        /// <summary>1.0 = default. Higher lets you move further before the dial hides.</summary>
        public double Sensitivity = 1.0;
        /// <summary>Short look at the first movement so a decisive flick never flashes the dial.</summary>
        public double ObserveMs = 60;
        /// <summary>Icon ring radius (used to tell which slice the cursor is heading for).</summary>
        public double RingRadius = 61;
        /// <summary>Dial edge: if the cursor is already beyond this when the dial would open, it stays hidden.</summary>
        public double RevealRadius = 92;
        /// <summary>Moving further than this from the dial centre hides it for the rest of the drag.</summary>
        public double OuterRadius = 140;
        /// <summary>Set by the host while the cursor is over the dial or its options; it never hides then.</summary>
        public bool OverTile;
        /// <summary>Learned directions (bit per <see cref="SectorOf"/> sector) where a quick, decisive drag means "not for the dial".</summary>
        public int SuppressedSectors;

        /// <summary>Why the dial was dismissed: flick, learned, distance, escape, other.</summary>
        public string DismissReason { get; private set; }
        /// <summary>Direction of the first movement when the dial opened (-1 = barely moved).</summary>
        public int RevealSector { get; private set; }
        /// <summary>Pointer speed when the dial opened.</summary>
        public double RevealSpeed { get; private set; }

        public IntentState State { get; private set; }
        public double Speed { get; private set; }
        public double AnchorX { get; private set; }
        public double AnchorY { get; private set; }
        public int HeadingTile { get; private set; }
        public int TileCount { get; private set; }

        struct Sample { public double X, Y; public long T; }

        readonly List<Sample> win = new List<Sample>();
        double[] tileX = new double[0];
        double[] tileY = new double[0];
        double curX, curY, startX, startY;
        long startT, lastT;

        public void Begin(double x, double y, long t)
        {
            win.Clear();
            win.Add(new Sample { X = x, Y = y, T = t });
            curX = startX = x; curY = startY = y;
            startT = lastT = t;
            State = IntentState.Observing;
            Speed = 0; HeadingTile = -1; TileCount = 0;
            OverTile = false;
            DismissReason = null; RevealSector = -1; RevealSpeed = 0;
        }

        public void Dismiss() { Dismiss("other"); }

        public void Dismiss(string reason)
        {
            if (State == IntentState.Dismissed) return;
            State = IntentState.Dismissed;
            DismissReason = reason;
        }

        /// <summary>One of 8 compass sectors for a movement: 0 = right, 2 = down, 4 = left, 6 = up (screen coordinates).</summary>
        public static int SectorOf(double dx, double dy)
        {
            int s = (int)Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4));
            return ((s % 8) + 8) % 8;
        }

        /// <summary>The host may nudge the dial away from a screen edge; do this before SetTiles.</summary>
        public void SetAnchor(double x, double y) { AnchorX = x; AnchorY = y; }

        public double DistanceFromAnchor
        {
            get { return Math.Sqrt((curX - AnchorX) * (curX - AnchorX) + (curY - AnchorY) * (curY - AnchorY)); }
        }

        public void SetTiles(int count)
        {
            TileCount = count;
            tileX = new double[count];
            tileY = new double[count];
            for (int i = 0; i < count; i++)
                RingLayout.Center(AnchorX, AnchorY, i, count, RingRadius, out tileX[i], out tileY[i]);
        }

        public void AddSample(double x, double y, long t)
        {
            if (State == IntentState.Dismissed) return;
            curX = x; curY = y;
            if (t - win[win.Count - 1].T < 4) return;

            win.Add(new Sample { X = x, Y = y, T = t });
            while (win.Count > 2 && win[0].T < t - 400) win.RemoveAt(0);
            Speed = ComputeSpeed(t);
            lastT = t;

            if (State == IntentState.Observing) StepObserving(t);
            else StepRevealing();
        }

        void StepObserving(long t)
        {
            if (t - startT < ObserveMs) return;
            double dx = curX - startX, dy = curY - startY;
            double moved = Math.Sqrt(dx * dx + dy * dy);
            // Already past where the dial would be, or about to leave it within ~80 ms: a flick, stay hidden.
            if (moved > RevealRadius || moved + Speed * 0.08 > OuterRadius * Sensitivity)
            {
                Dismiss("flick");
                return;
            }
            int sector = moved >= 4 ? SectorOf(dx, dy) : -1;
            // A brisk start toward a direction where this user always passes the dial by: leave it hidden.
            if (sector >= 0 && moved >= 8 && Speed > 250 && (SuppressedSectors & (1 << sector)) != 0)
            {
                Dismiss("learned");
                return;
            }
            RevealSector = sector;
            RevealSpeed = Speed;
            State = IntentState.Revealing;
            AnchorX = startX; AnchorY = startY; // the dial sits over the item being dragged
        }

        void StepRevealing()
        {
            HeadingTile = ComputeHeadingTile();
            if (OverTile) return;
            if (DistanceFromAnchor > OuterRadius * Sensitivity) Dismiss("distance");
        }

        /// <summary>The tile the cursor currently points at from the anchor, or -1.</summary>
        public int FocusTile()
        {
            if (TileCount == 0) return -1;
            double dx = curX - AnchorX, dy = curY - AnchorY;
            if (dx * dx + dy * dy < 50 * 50) return -1;
            double a = Math.Atan2(dy, dx);
            double half = Math.PI / TileCount;
            for (int i = 0; i < TileCount; i++)
                if (AngleDiff(a, RingLayout.Angle(i, TileCount)) <= half) return i;
            return -1;
        }

        int ComputeHeadingTile()
        {
            if (TileCount == 0) return -1;
            long from = lastT - 120;
            Sample first = win[win.Count - 1];
            for (int i = 0; i < win.Count; i++)
                if (win[i].T >= from) { first = win[i]; break; }
            Sample last = win[win.Count - 1];
            double vx = last.X - first.X, vy = last.Y - first.Y;
            if (vx * vx + vy * vy < 10 * 10) return -1;
            double heading = Math.Atan2(vy, vx);

            int best = -1;
            double bestDiff = 0.5; // ~29 degrees
            for (int i = 0; i < TileCount; i++)
            {
                double ddx = tileX[i] - curX, ddy = tileY[i] - curY;
                if (ddx * ddx + ddy * ddy < 20 * 20) continue;
                double diff = AngleDiff(heading, Math.Atan2(ddy, ddx));
                if (diff < bestDiff) { bestDiff = diff; best = i; }
            }
            return best;
        }

        double ComputeSpeed(long t)
        {
            double path = 0;
            long firstT = t;
            bool has = false;
            double px = 0, py = 0;
            for (int i = 0; i < win.Count; i++)
            {
                if (win[i].T < t - 100) continue;
                if (has) path += Dist(px, py, win[i].X, win[i].Y);
                else firstT = win[i].T;
                px = win[i].X; py = win[i].Y; has = true;
            }
            double span = Math.Max(t - firstT, 60);
            return path / span * 1000.0;
        }

        static double Dist(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        static double AngleDiff(double a, double b)
        {
            double d = Math.Abs(a - b) % (2 * Math.PI);
            return d > Math.PI ? 2 * Math.PI - d : d;
        }

    }
}
