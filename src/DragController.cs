using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>The dial as the controller sees it (the real one is <see cref="RingOverlay"/>; tests use a fake).</summary>
    public interface IOverlay
    {
        bool Visible { get; }
        bool HasKids { get; }
        /// <summary>The pointer has been over a slice at some point since Show.</summary>
        bool EverHovered { get; }
        void Show(double anchorX, double anchorY, IList<ActionItem> items, double scale, long now, string summary, string tip);
        bool IsOverMenu(int x, int y);
        void BeginClose(long now, int delayMs);
        void Tick(long now, int cursorX, int cursorY, int headingTile, bool copyMode);
        void Close();
        /// <summary>A thumbnail and a detail line for what is dragged (either may be null).</summary>
        void SetPreview(Image thumb, string details);
    }

    /// <summary>What the controller needs from Windows (the real one is <see cref="RealDesktop"/>; tests use a fake).</summary>
    public interface IDesktop
    {
        bool CursorPos(out int x, out int y);
        bool ButtonDown();
        bool KeyDown(int vk);
        double ScaleAt(int x, int y);
        Rectangle WorkingArea(int x, int y);
        void MoveCursor(int x, int y);
    }

    public sealed class RealDesktop : IDesktop
    {
        public bool CursorPos(out int x, out int y)
        {
            POINT p;
            bool ok = Native.GetCursorPos(out p);
            x = p.X; y = p.Y;
            return ok;
        }
        public bool ButtonDown() { return Native.AnyButtonDown(); }
        public bool KeyDown(int vk) { return Native.KeyDown(vk); }
        public double ScaleAt(int x, int y) { return Native.ScaleAt(x, y); }
        public Rectangle WorkingArea(int x, int y) { return Screen.FromPoint(new Point(x, y)).WorkingArea; }
        public void MoveCursor(int x, int y) { Native.SetCursorPos(x, y); }
    }

    /// <summary>
    /// Glue: drag detector → intent engine → dial → action runner, run from a ~66 fps tick on the UI thread.
    /// Safety nets: if the button is found released without a button-up arriving, the drag ends; the dial never stays
    /// up longer than a minute; every drag's outcome feeds <see cref="Learning"/>.
    /// </summary>
    public sealed class DragController
    {
        /// <summary>How far past the dial centre the cursor can go before the dial gets out of the way.</summary>
        public const double DismissRadius = 140, DismissRadiusWithOptions = 250;
        public const int ResolveTimeoutMs = 1500, DropGraceMs = 600, MaxDialMs = 60000, ReleasedTicks = 3;

        readonly IOverlay overlay;
        readonly IDesktop desk;
        readonly IntentEngine engine = new IntentEngine();
        readonly ConcurrentQueue<long[]> samples = new ConcurrentQueue<long[]>();

        public Func<Settings> GetSettings = delegate { return Settings.Current; };
        public Func<Learning> GetLearning = delegate { return Learning.Current; };
        /// <summary>Start or stop the tick timer.</summary>
        public Action<bool> SetTimer = delegate { };
        /// <summary>Raised when a drag of files is confirmed (the shelf shows its drop zone) and when it ends.</summary>
        public event Action<DragSession> FileDragStarted, FileDragEnded;
        /// <summary>Builds a menu when the background look-up did not finish in time.</summary>
        public Func<DragSession, List<ActionItem>> FallbackMenu = delegate { return ActionCatalog.For((string[])null); };

        DragSession session;
        bool active, shown, escaped, used, fileDragAnnounced, outcomePending;
        long sessionStart, shownAt, endedAt;
        int lastX, lastY, releasedTicks, shownCount;
        double shownScale = 1, size = 1;
        readonly bool[] digitWasDown = new bool[10];
        Gesture gesture;
        long bubbleAt;
        int lastDx, lastDy;
        Image shownThumb;
        string shownDetails;

        /// <summary>Where a drawn shape offers its action (null: shapes are not recognised).</summary>
        public IGestureBubble Bubble;
        /// <summary>A pen or touch drag: the dial is larger and lets the pointer wander further.</summary>
        public const double TouchSize = 1.5, TouchReach = 1.35;

        /// <summary>The tip shown under the dial the first few times.</summary>
        public const int TipTimes = 5;
        public static readonly string TipText = "Point at an action and let go · Esc closes · Alt skips";

        public DragController(IOverlay overlay, IDesktop desktop)
        {
            this.overlay = overlay; desk = desktop;
        }

        public bool Active { get { return active; } }
        public DragSession Session { get { return session; } }
        public IntentEngine Engine { get { return engine; } }
        public DragOutcome LastOutcome { get; private set; }

        public void OnStarted(DragSession s, long now)
        {
            if (active) OnEnded(session, now);
            FinishOutcome(now);
            long[] drop;
            while (samples.TryDequeue(out drop)) { }

            session = s;
            sessionStart = now;
            active = true; shown = false; escaped = false; used = false; fileDragAnnounced = false; outcomePending = true;
            releasedTicks = 0;
            Settings settings = GetSettings();
            Learning learning = GetLearning();
            engine.Sensitivity = settings.Sensitivity * learning.EffectiveEagerness;
            engine.ObserveMs = learning.EffectiveObserveMs;
            engine.SuppressedSectors = learning.EffectiveSuppressed;
            size = Math.Max(1.0, Math.Min(1.5, settings.DialSize));
            if (s.Touch && settings.TouchDial) { size = TouchSize; engine.Sensitivity *= TouchReach; }
            double scale = desk.ScaleAt(s.OriginX, s.OriginY) * size;
            engine.RingRadius = RadialGeometry.IconR * scale;
            engine.RevealRadius = RadialGeometry.OuterR * scale;
            engine.OuterRadius = DismissRadius * scale;
            gesture = settings.GesturesEnabled && Bubble != null && !s.Touch ? new Gesture(desk.ScaleAt(s.OriginX, s.OriginY)) : null;
            pendingShape = null;
            if (Bubble != null) Bubble.Hide(0);
            shownThumb = null; shownDetails = null;
            engine.Begin(s.OriginX, s.OriginY, now);
            Log.Debug("drag started " + s.Source + " class=" + s.RootClass + " app=" + s.Process);
            lastX = s.OriginX; lastY = s.OriginY;
            overlay.Close();
            SetTimer(true);
        }

        /// <summary>Pointer samples between ticks; safe to call from any thread.</summary>
        public void OnMoved(int x, int y, long t)
        {
            if (active) samples.Enqueue(new long[] { x, y, t });
        }

        public void OnEnded(DragSession s, long now)
        {
            if (!active || (s != null && session != null && s.Id != session.Id)) return;
            active = false;
            endedAt = now;
            if (overlay.Visible)
            {
                // A drop onto a slice is still in flight when the button goes up.
                int delay = overlay.IsOverMenu(lastX, lastY) ? DropGraceMs : 0;
                overlay.BeginClose(now, delay);
            }
            if (Bubble != null && Bubble.Visible) Bubble.Hide(Bubble.Contains(lastX, lastY) ? DropGraceMs : 0);
            if (fileDragAnnounced)
            {
                Action<DragSession> e = FileDragEnded;
                if (e != null) e(session);
            }
        }

        /// <summary>Something happened that makes any open dial wrong (screen locked, display changed, DragFlow disabled).</summary>
        public void Cancel(string reason, long now)
        {
            if (!active && !overlay.Visible) return;
            Log.Write("drag cancelled: " + reason);
            engine.Dismiss("other");
            OnEnded(session, now);
            overlay.Close();
            if (Bubble != null) Bubble.Hide(0);
            FinishOutcome(now);
        }

        /// <summary>The dial reported a drop on one of its actions.</summary>
        public void NoteDrop()
        {
            used = true;
        }

        public void Tick(long now)
        {
            if (active) TickActive(now);
            overlay.Tick(now, lastX, lastY, engine.HeadingTile, desk.KeyDown(Native.VK_CONTROL));
            if (!active)
            {
                if (outcomePending && (!overlay.Visible || now - endedAt > DropGraceMs + 400)) FinishOutcome(now);
                if (!overlay.Visible) SetTimer(false);
            }
        }

        void TickActive(long now)
        {
            long[] smp;
            while (samples.TryDequeue(out smp)) { lastX = (int)smp[0]; lastY = (int)smp[1]; engine.AddSample(smp[0], smp[1], smp[2]); Shape(smp[0], smp[1], smp[2]); }
            int cx, cy;
            if (desk.CursorPos(out cx, out cy)) { lastX = cx; lastY = cy; }
            engine.AddSample(lastX, lastY, now);
            Shape(lastX, lastY, now);

            // Safety net: the button is up but no button-up reached us (UAC prompt, lock screen, a cancelled drag).
            if (!desk.ButtonDown())
            {
                if (++releasedTicks >= ReleasedTicks)
                {
                    Log.Write("button released without a button-up event; ending the drag");
                    OnEnded(session, now);
                    return;
                }
            }
            else releasedTicks = 0;

            int state = session.FileState;
            if (state == 0 && now - sessionStart > ResolveTimeoutMs)
            {
                Log.Write("selection look-up slow; showing a general menu");
                state = session.FileState = 1;
            }
            if (state == 1 && !fileDragAnnounced)
            {
                fileDragAnnounced = true;
                Action<DragSession> e = FileDragStarted;
                if (e != null) e(session);
            }
            if (state < 0) engine.Dismiss("notfile");
            if (desk.KeyDown(Native.VK_ESCAPE) && engine.State != IntentState.Dismissed) { escaped = true; engine.Dismiss("escape"); }

            if (engine.State == IntentState.Revealing && !shown && state == 1)
            {
                // If finding out what is being dragged took a moment and the cursor has already left, stay hidden.
                if (engine.DistanceFromAnchor > engine.RevealRadius) engine.Dismiss("flick");
                else ShowRing(now);
            }
            if (engine.State == IntentState.Dismissed && overlay.Visible) overlay.BeginClose(now, 0);
            if (overlay.Visible && now - shownAt > MaxDialMs) { Log.Write("dial open for a minute; closing it"); engine.Dismiss("other"); overlay.BeginClose(now, 0); }

            if (Bubble != null && Bubble.Visible && now - bubbleAt > BubbleMs && !Bubble.Contains(lastX, lastY)) Bubble.Hide(0);
            if (overlay.Visible && (session.Thumb != shownThumb || session.Details != shownDetails))
            {
                shownThumb = session.Thumb; shownDetails = session.Details;
                // The thumbnail fills the dial's middle; only when that middle is over the dragged item itself (not when the
                // dial was moved away from a screen edge), so it never covers somewhere a drop could be meant for.
                bool centred = Math.Abs(engine.AnchorX - session.OriginX) < 3 && Math.Abs(engine.AnchorY - session.OriginY) < 3;
                overlay.SetPreview(centred ? shownThumb : null, shownDetails);
            }
            if (overlay.Visible)
            {
                engine.OverTile = overlay.IsOverMenu(lastX, lastY);
                double scale = desk.ScaleAt(lastX, lastY) * size;
                engine.OuterRadius = (overlay.HasKids ? DismissRadiusWithOptions : DismissRadius) * scale;
                JumpWithNumberKeys();
            }
        }

        /// <summary>How long a shape's offer waits for the pointer.</summary>
        public const int BubbleMs = 3000;

        /// <summary>
        /// Once the dial is out of the way (moved past, flicked, or not wanted here), a quick shape drawn while dragging
        /// offers that shape's action beside the pointer. Never after Esc, and only for actions this drag's menu has.
        /// </summary>
        void Shape(double x, double y, long t)
        {
            if (gesture == null || session == null || session.FileState < 0) return;
            if (engine.State != IntentState.Dismissed || engine.DismissReason == "escape" || overlay.Visible) { gesture.Reset(); pendingShape = null; return; }
            int dx = (int)x - lastGestureX, dy = (int)y - lastGestureY;
            if (dx * dx + dy * dy > 64) { lastDx = dx; lastDy = dy; lastGestureX = (int)x; lastGestureY = (int)y; }
            string shape = gesture.Add(x, y, t);
            if (shape != null) { pendingShape = shape; pendingAt = t; }
            // Finding out what is dragged can take a moment longer than drawing the shape: the offer waits for it, briefly.
            if (pendingShape == null || session.FileState != 1) return;
            if (t - pendingAt > 1500) { pendingShape = null; return; }
            shape = pendingShape;
            pendingShape = null;
            string key = Gesture.ActionFor(GetSettings().Gestures, shape);
            ActionItem item = Gesture.Find(session.Menu ?? FallbackMenu(session), key);
            Log.Debug("shape " + shape + (item == null ? " (no such action here)" : " offers " + item.Id));
            if (item == null) return;
            double scale = desk.ScaleAt((int)x, (int)y);
            Point at = GestureBubble.Place((int)x, (int)y, lastDx, lastDy, scale, desk.WorkingArea((int)x, (int)y));
            Bubble.Show(item, at.X, at.Y, scale);
            bubbleAt = t;
        }

        string pendingShape;
        long pendingAt;
        int lastGestureX, lastGestureY;

        /// <summary>Pressing 1–9 while the dial is up puts the pointer on that slice (no aiming needed); then let go.</summary>
        void JumpWithNumberKeys()
        {
            for (int d = 1; d <= 9; d++)
            {
                bool down = desk.KeyDown(0x30 + d);
                if (down && !digitWasDown[d] && d <= shownCount)
                {
                    double x, y;
                    new RadialGeometry(shownCount, shownScale).IconCenter(d - 1, out x, out y);
                    desk.MoveCursor((int)Math.Round(engine.AnchorX + x), (int)Math.Round(engine.AnchorY + y));
                }
                digitWasDown[d] = down;
            }
        }

        void ShowRing(long now)
        {
            List<ActionItem> items = session.Menu ?? FallbackMenu(session);
            double ox = engine.AnchorX, oy = engine.AnchorY; // the dragged item
            double scale = desk.ScaleAt((int)ox, (int)oy) * size;

            // Keep the dial and its labels on screen.
            Rectangle wa = desk.WorkingArea((int)ox, (int)oy);
            double margin = (RadialGeometry.OuterR + 60) * scale;
            double ax = Clamp(ox, wa.Left + margin, wa.Right - margin);
            double ay = Clamp(oy, wa.Top + margin, wa.Bottom - margin);

            engine.SetAnchor(ax, ay);
            engine.SetTiles(items.Count);
            Settings settings = GetSettings();
            string tip = null;
            if (settings.TipsShown < TipTimes)
            {
                tip = L.T(TipText);
                settings.TipsShown++;
                settings.Save();
            }
            overlay.Show(ax, ay, items, scale, now, session.Summary, tip);
            shown = true;
            shownAt = now;
            shownCount = items.Count;
            shownScale = scale;
            for (int d = 0; d < 10; d++) digitWasDown[d] = desk.KeyDown(0x30 + d);
            Log.Debug("dial shown with " + items.Count + " slices");
        }

        void FinishOutcome(long now)
        {
            if (!outcomePending || session == null) return;
            outcomePending = false;
            if (session.FileState < 0) return; // not a file drag: nothing to learn
            var o = new DragOutcome
            {
                Revealed = shown,
                Used = used,
                Escaped = escaped,
                Sector = engine.RevealSector,
                ShownMs = shown ? Math.Max(0, (active ? now : endedAt) - shownAt) : 0,
                Speed = engine.RevealSpeed
            };
            if (shown && !used && overlay.EverHovered) o.ShownMs = Math.Max(o.ShownMs, 600); // looked at it: not a quick pass
            LastOutcome = o;
            Log.Debug("outcome shown=" + o.Revealed + " used=" + o.Used + " esc=" + o.Escaped + " ms=" + o.ShownMs + " sector=" + o.Sector + " reason=" + engine.DismissReason);
            try { GetLearning().Record(o); } catch (Exception ex) { Log.Error("learning", ex); }
        }

        static double Clamp(double v, double lo, double hi)
        {
            if (hi < lo) return (lo + hi) / 2;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
