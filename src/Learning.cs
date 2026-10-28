using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DragFlow
{
    /// <summary>How one drag ended, as far as the dial is concerned.</summary>
    public sealed class DragOutcome
    {
        /// <summary>The dial was shown.</summary>
        public bool Revealed;
        /// <summary>Something was dropped on the dial.</summary>
        public bool Used;
        /// <summary>Esc was pressed.</summary>
        public bool Escaped;
        /// <summary>Direction of the first movement (0..7) or -1.</summary>
        public int Sector = -1;
        /// <summary>How long the dial was on screen.</summary>
        public long ShownMs;
        public double Speed;

        /// <summary>Shown, then left behind quickly without being touched: the dial was in the way.</summary>
        public bool QuickPass { get { return Revealed && !Used && !Escaped && ShownMs < 600; } }
    }

    /// <summary>
    /// Learns from how drags end and tunes the dial within safe limits: how long it watches before opening, how far you
    /// can move before it gets out of the way, and directions where a brisk drag never wants it (say, toward the
    /// navigation pane). Everything stays on this PC in %APPDATA%\DragFlow\learning.ini and can be reset.
    /// </summary>
    public sealed class Learning
    {
        public static Learning Current = new Learning();

        public const int Window = 60, MinSamples = 10;
        public const double DefaultObserveMs = 60;

        readonly List<DragOutcome> recent = new List<DragOutcome>();
        public bool Enabled = true;
        /// <summary>Learned multiplier on how far you can move before the dial hides (0.8 – 1.25).</summary>
        public double Eagerness = 1.0;
        /// <summary>Learned time the dial watches the first movement before opening (50 – 130 ms).</summary>
        public double ObserveMs = DefaultObserveMs;
        /// <summary>Learned directions where the dial stays hidden on brisk drags.</summary>
        public int SuppressedSectors;
        /// <summary>The user's own nudge from "show it more / less" (-0.3 – +0.3).</summary>
        public double UserBias;
        public int TotalDrags, TotalShown, TotalUsed;

        public string FilePath { get { return AppPaths.RoamingFile("learning.ini"); } }

        public double EffectiveEagerness { get { return Enabled ? Clamp(Eagerness + UserBias, 0.7, 1.4) : Clamp(1 + UserBias, 0.7, 1.4); } }
        public double EffectiveObserveMs { get { return Clamp((Enabled ? ObserveMs : DefaultObserveMs) - UserBias * 100, 40, 150); } }
        public int EffectiveSuppressed { get { return Enabled && UserBias <= 0 ? SuppressedSectors : 0; } }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }

        public void Record(DragOutcome o)
        {
            lock (recent)
            {
                TotalDrags++;
                if (o.Revealed) TotalShown++;
                if (o.Used) TotalUsed++;
                if (o.Revealed)
                {
                    recent.Add(o);
                    while (recent.Count > Window) recent.RemoveAt(0);
                    if (Enabled) Adapt();
                }
            }
            Save();
        }

        void Adapt()
        {
            var rev = recent;
            if (rev.Count < MinSamples) return;
            double n = rev.Count;
            double useRate = rev.Count(r => r.Used) / n;
            double quickPass = rev.Count(r => r.QuickPass) / n;

            double targetEager = Clamp(1.0 + 0.5 * (useRate - 0.25) - 0.4 * (quickPass - 0.4), 0.8, 1.25);
            Eagerness = Eagerness * 0.7 + targetEager * 0.3;
            double targetObserve = Clamp(DefaultObserveMs + 80 * Math.Max(0, quickPass - 0.5) - 20 * Math.Max(0, useRate - 0.4), 50, 130);
            ObserveMs = ObserveMs * 0.7 + targetObserve * 0.3;

            int mask = 0;
            for (int s = 0; s < 8; s++)
            {
                var inS = rev.Where(r => r.Sector == s).ToList();
                if (inS.Count >= 8 && inS.All(r => !r.Used) && inS.Count(r => r.QuickPass) >= 0.85 * inS.Count) mask |= 1 << s;
            }
            SuppressedSectors = mask;
        }

        public void ShowMore() { UserBias = Clamp(UserBias + 0.1, -0.3, 0.3); Save(); }
        public void ShowLess() { UserBias = Clamp(UserBias - 0.1, -0.3, 0.3); Save(); }

        public void Reset()
        {
            lock (recent) recent.Clear();
            Eagerness = 1.0; ObserveMs = DefaultObserveMs; SuppressedSectors = 0; UserBias = 0;
            TotalDrags = TotalShown = TotalUsed = 0;
            Save();
        }

        static readonly string[] SectorNames = { "right", "down-right", "down", "down-left", "left", "up-left", "up", "up-right" };

        /// <summary>Plain-words summary for the Learning page.</summary>
        public List<string> Describe()
        {
            var lines = new List<string>();
            lock (recent)
            {
                lines.Add("Drags seen: " + TotalDrags + ". The dial opened " + TotalShown + " times and you used it " + TotalUsed + " times.");
                if (recent.Count < MinSamples) lines.Add("Still learning: it needs about " + (MinSamples - recent.Count) + " more drags where the dial opens.");
                else
                {
                    double used = recent.Count(r => r.Used) * 100.0 / recent.Count, passed = recent.Count(r => r.QuickPass) * 100.0 / recent.Count;
                    lines.Add("Recently you used the dial on " + used.ToString("0") + "% of the drags where it opened, and moved straight past it on " + passed.ToString("0") + "%.");
                }
                lines.Add("It waits " + EffectiveObserveMs.ToString("0") + " ms before opening and lets you move " + (EffectiveEagerness * 100).ToString("0") + "% of the normal distance before hiding.");
                int m = EffectiveSuppressed;
                if (m != 0)
                    lines.Add("It stays hidden when you drag briskly " + string.Join(", ", Enumerable.Range(0, 8).Where(s => (m & (1 << s)) != 0).Select(s => SectorNames[s]).ToArray()) + ", because you always go past it there.");
                if (UserBias > 0.001) lines.Add("You asked to see it more (+" + (UserBias * 10).ToString("0") + ").");
                if (UserBias < -0.001) lines.Add("You asked to see it less (" + (UserBias * 10).ToString("0") + ").");
            }
            return lines;
        }

        // ---------- persistence ----------

        public void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "Eagerness=" + Eagerness.ToString("0.000", CultureInfo.InvariantCulture),
                    "ObserveMs=" + ObserveMs.ToString("0.0", CultureInfo.InvariantCulture),
                    "Suppressed=" + SuppressedSectors,
                    "UserBias=" + UserBias.ToString("0.00", CultureInfo.InvariantCulture),
                    "Totals=" + TotalDrags + "," + TotalShown + "," + TotalUsed
                };
                lock (recent)
                    foreach (DragOutcome o in recent)
                        lines.Add("o=" + (o.Used ? 1 : 0) + "," + (o.Escaped ? 1 : 0) + "," + o.Sector + "," + o.ShownMs + "," + ((int)o.Speed));
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static Learning Load()
        {
            var l = new Learning();
            try
            {
                if (!File.Exists(l.FilePath)) return l;
                foreach (string line in File.ReadAllLines(l.FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    double d;
                    int i;
                    if (k == "Eagerness" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) l.Eagerness = Clamp(d, 0.8, 1.25);
                    else if (k == "ObserveMs" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) l.ObserveMs = Clamp(d, 50, 130);
                    else if (k == "Suppressed" && int.TryParse(v, out i)) l.SuppressedSectors = i & 0xFF;
                    else if (k == "UserBias" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) l.UserBias = Clamp(d, -0.3, 0.3);
                    else if (k == "Totals")
                    {
                        string[] t = v.Split(',');
                        if (t.Length == 3) { int.TryParse(t[0], out l.TotalDrags); int.TryParse(t[1], out l.TotalShown); int.TryParse(t[2], out l.TotalUsed); }
                    }
                    else if (k == "o")
                    {
                        string[] t = v.Split(',');
                        if (t.Length < 5) continue;
                        var o = new DragOutcome { Revealed = true, Used = t[0] == "1", Escaped = t[1] == "1" };
                        int.TryParse(t[2], out o.Sector);
                        long.TryParse(t[3], out o.ShownMs);
                        int sp; int.TryParse(t[4], out sp); o.Speed = sp;
                        l.recent.Add(o);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return l;
        }
    }
}
