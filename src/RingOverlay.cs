using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>How a drop on the dial was made.</summary>
    public sealed class DropInfo
    {
        /// <summary>Ctrl was held: copy instead of move.</summary>
        public bool Copy;
        /// <summary>Files DragFlow had to save first (drags from other apps); undo removes them too.</summary>
        public List<string> Saved = new List<string>();
    }

    /// <summary>
    /// The dial's window: one layered, per-pixel-alpha window that is also an OLE drop target.
    /// Fully transparent pixels (the centre and everything around the dial) let drops and clicks fall through.
    /// In click mode (opened by the keyboard shortcut or the right-click menu) it takes the keyboard instead.
    /// </summary>
    public sealed class MenuWindow : Form
    {
        public Func<int, int, ActionItem> HitTest;
        public event Action<ActionItem, string[], DropInfo> FilesDropped;
        /// <summary>Click mode: a click at a screen point, a key, or the window losing focus.</summary>
        public event Action<int, int> Clicked;
        public event Action<Keys> KeyPressed;
        public event Action LostFocus2;
        public readonly bool ClickMode;
        /// <summary>Names of the slices, for screen readers.</summary>
        public Func<List<string>> SliceNames = delegate { return new List<string>(); };

        public MenuWindow(bool clickMode)
        {
            ClickMode = clickMode;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AllowDrop = !clickMode;
            KeyPreview = true;
            Size = new Size(1, 1);
            Text = "DragFlow actions";
            AccessibleName = L.T("DragFlow actions");
            AccessibleRole = AccessibleRole.MenuPopup;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                if (!ClickMode) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return !ClickMode; } }

        DragDropEffects EffectAt(DragEventArgs e)
        {
            if (!VirtualFiles.CanProvideFiles(e.Data) || HitTest == null) return DragDropEffects.None;
            ActionItem item = HitTest(e.X, e.Y);
            return item != null && !item.IsParent ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragEnter(DragEventArgs e) { e.Effect = EffectAt(e); base.OnDragEnter(e); }
        protected override void OnDragOver(DragEventArgs e) { e.Effect = EffectAt(e); base.OnDragOver(e); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            ActionItem item = HitTest == null ? null : HitTest(e.X, e.Y);
            if (item != null && !item.IsParent)
            {
                var info = new DropInfo { Copy = (e.KeyState & 8) != 0 };
                string[] files = null;
                try { files = VirtualFiles.FromDrop(e.Data, out info.Saved); }
                catch (Exception ex) { Log.Error("reading the drop", ex); }
                if (files != null && files.Length > 0)
                {
                    e.Effect = DragDropEffects.Copy; // Copy so the source never deletes the original
                    Action<ActionItem, string[], DropInfo> dropped = FilesDropped;
                    if (dropped != null) dropped(item, files, info);
                }
            }
            base.OnDragDrop(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Action<int, int> c = Clicked;
            if (c != null && e.Button == MouseButtons.Left) c(Left + e.X, Top + e.Y);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Action<Keys> k = KeyPressed;
            if (ClickMode && k != null) { k(keyData); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Action l = LostFocus2;
            if (ClickMode && l != null) l();
        }

        /// <summary>Tell screen readers which action has the keyboard focus.</summary>
        public void AnnounceFocus(int index)
        {
            if (index >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, index);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new DialAccessible(this); }

        sealed class DialAccessible : ControlAccessibleObject
        {
            readonly MenuWindow w;
            public DialAccessible(MenuWindow owner) : base(owner) { w = owner; }
            public override int GetChildCount() { return w.SliceNames().Count; }
            public override AccessibleObject GetChild(int index)
            {
                List<string> names = w.SliceNames();
                return index >= 0 && index < names.Count ? new SliceAccessible(this, names[index]) : null;
            }
            public override AccessibleRole Role { get { return AccessibleRole.MenuPopup; } }
        }

        sealed class SliceAccessible : AccessibleObject
        {
            readonly AccessibleObject parent;
            readonly string name;
            public SliceAccessible(AccessibleObject parent, string name) { this.parent = parent; this.name = name; }
            public override string Name { get { return name; } }
            public override AccessibleRole Role { get { return AccessibleRole.MenuItem; } }
            public override AccessibleObject Parent { get { return parent; } }
            public override AccessibleStates State { get { return AccessibleStates.Focusable; } }
        }

        public void Present(Bitmap bmp, int x, int y, byte alpha)
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hBmp);
            try
            {
                var dst = new POINT(x, y);
                var size = new SIZE(bmp.Width, bmp.Height);
                var src = new POINT(0, 0);
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    /// <summary>
    /// The radial menu: a compact dial around the anchor. Pointing into a slice selects it (the whole slice is
    /// the target, not just the icon); hovering a slice with options fans small chips out beside it.
    /// Click mode adds clicking and the keyboard: arrows or Tab move, Enter or a number picks, Esc closes.
    /// </summary>
    public sealed class RingOverlay : IOverlay
    {
        const int OpenMs = 170, CloseMs = 130, KidsDelayMs = 220, KidsOpenMs = 160, KidsLeaveMs = 200;

        public event Action<ActionItem, string[], DropInfo> Dropped;
        /// <summary>Click mode: an action was chosen.</summary>
        public event Action<ActionItem> Picked;
        /// <summary>Click mode: closed without choosing.</summary>
        public event Action Dismissed;

        MenuWindow win;
        RadialGeometry geo;
        RadialView view;
        IList<ActionItem> items;
        Bitmap frame;
        double ax, ay;
        long showT, closeAt, hoverSince, kidsT;
        bool closing, dirty, clickMode, reduceMotion, keyMode;
        int hover = -1, chipHover = -1, keyChip = -1;
        int lastCursorX = int.MinValue, lastCursorY;
        float lastAlpha = -1;
        string typed = "";
        long typedAt;

        public bool Visible { get { return win != null; } }
        public bool Closing { get { return closing; } }
        public bool HasKids { get { return view != null && view.KidsParent >= 0; } }
        public bool EverHovered { get; private set; }
        public bool ClickMode { get { return clickMode; } }

        public void Show(double anchorX, double anchorY, IList<ActionItem> menu, double scale, long now, string summary, string tip)
        {
            Open(anchorX, anchorY, menu, scale, now, summary, tip, false);
        }

        /// <summary>Open for clicking and the keyboard (no drag in progress).</summary>
        public void ShowForClick(double anchorX, double anchorY, IList<ActionItem> menu, double scale, long now, string summary)
        {
            Open(anchorX, anchorY, menu, scale, now, summary, L.T("Click an action · arrows and Enter · Esc closes"), true);
        }

        void Open(double anchorX, double anchorY, IList<ActionItem> menu, double scale, long now, string summary, string tip, bool click)
        {
            EverHovered = false;
            Close();
            items = menu;
            clickMode = click;
            reduceMotion = Theme.ReduceMotion(Settings.Current);
            ax = anchorX; ay = anchorY; showT = reduceMotion ? now - OpenMs : now;
            geo = new RadialGeometry(menu.Count, scale);
            view = new RadialView
            {
                Geo = geo,
                Items = menu,
                Theme = Theme.Current(),
                Open = reduceMotion ? 1 : 0,
                Hover = new float[menu.Count],
                Heading = new float[menu.Count],
                LabelWidths = RadialRenderer.MeasurePills(menu, scale),
                Summary = summary,
                Tip = tip,
                KeyFocus = -1
            };
            hover = -1; chipHover = -1; keyChip = -1; keyMode = false; lastAlpha = -1; dirty = true; lastCursorX = int.MinValue;
            int size = geo.HalfSize * 2;
            frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            win = new MenuWindow(click) { HitTest = HitTest };
            win.SliceNames = delegate { return items == null ? new List<string>() : items.Select(i => i.Label).ToList(); };
            win.FilesDropped += delegate (ActionItem item, string[] files, DropInfo info)
            {
                Action<ActionItem, string[], DropInfo> d = Dropped;
                if (d != null) d(item, files, info);
                BeginClose(Clock.Now, 0);
            };
            if (click)
            {
                win.Clicked += OnClick;
                win.KeyPressed += OnKey;
                win.LostFocus2 += delegate { if (!closing) Dismiss(); };
            }
            win.Show();
            if (click) { win.Activate(); Native.SetForegroundWindow(win.Handle); }
        }

        /// <summary>The action under a screen point: a sub-option chip first, then a slice.</summary>
        public ActionItem HitTest(int x, int y)
        {
            if (view == null) return null;
            int chip = ChipAt(x, y);
            if (chip >= 0) return items[view.KidsParent].Children[chip];
            int seg = geo.SegmentAt(x - ax, y - ay);
            return seg >= 0 ? items[seg] : null;
        }

        int ChipAt(int x, int y)
        {
            if (view.KidsParent < 0 || view.Chips == null || view.KidsOpen < 0.3f) return -1;
            float dx = (float)(x - ax), dy = (float)(y - ay);
            for (int j = 0; j < view.Chips.Length; j++)
            {
                RectangleF r = view.Chips[j];
                r.Inflate(3, 3);
                if (r.Contains(dx, dy)) return j;
            }
            return -1;
        }

        public void SetPreview(Image thumb, string details)
        {
            if (view == null) return;
            view.Thumb = thumb; view.Details = details;
            dirty = true;
        }

        public bool IsOverMenu(int x, int y)
        {
            if (view == null) return false;
            double dx = x - ax, dy = y - ay;
            return Math.Sqrt(dx * dx + dy * dy) <= geo.Outer + 6 * geo.Scale || ChipAt(x, y) >= 0;
        }

        /// <summary>Fade out. With a delay the dial stays put so a drop already in flight can land.</summary>
        public void BeginClose(long now, int delayMs)
        {
            if (closing || !Visible) return;
            closing = true;
            closeAt = now + delayMs;
        }

        // ---------- click mode ----------

        void OnClick(int x, int y)
        {
            if (closing) return;
            int chip = ChipAt(x, y);
            if (chip >= 0) { Pick(items[view.KidsParent].Children[chip]); return; }
            int seg = geo.SegmentAt(x - ax, y - ay);
            if (seg < 0) return;
            if (items[seg].IsParent) OpenKids(seg, Clock.Now);
            else Pick(items[seg]);
        }

        void OnKey(Keys key)
        {
            if (closing) return;
            keyMode = true;
            Keys k = key & Keys.KeyCode;
            int n = items.Count;
            if (k == Keys.Escape)
            {
                if (view.KidsParent >= 0) { CloseKids(); return; }
                Dismiss();
                return;
            }
            // Type the first letters of an action's name to jump to it.
            char ch = (char)0;
            if (k >= Keys.A && k <= Keys.Z && (key & (Keys.Control | Keys.Alt)) == 0) ch = (char)('a' + (k - Keys.A));
            if (ch != 0)
            {
                long nowMs = Clock.Now;
                typed = (nowMs - typedAt < 1200 ? typed : "") + ch;
                typedAt = nowMs;
                if (view.KidsParent >= 0)
                {
                    List<ActionItem> kids = items[view.KidsParent].Children;
                    int hit = kids.FindIndex(c => c.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
                    if (hit >= 0) { keyChip = hit; dirty = true; }
                }
                else
                {
                    int hit = items.ToList().FindIndex(i => i.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
                    if (hit >= 0 && hit != view.KeyFocus) { view.KeyFocus = hit; dirty = true; win.AnnounceFocus(hit); }
                }
                return;
            }
            if (view.KidsParent >= 0)
            {
                int count = view.Chips.Length;
                if (k == Keys.Down || k == Keys.Right || k == Keys.Tab && (key & Keys.Shift) == 0) keyChip = (keyChip + 1) % count;
                else if (k == Keys.Up || k == Keys.Left || k == Keys.Tab) keyChip = (keyChip - 1 + count) % count;
                else if (k == Keys.Back) { CloseKids(); return; }
                else if ((k == Keys.Enter || k == Keys.Space) && keyChip >= 0) { Pick(items[view.KidsParent].Children[keyChip]); return; }
                else if (k >= Keys.D1 && k <= Keys.D9 && k - Keys.D1 < count) { Pick(items[view.KidsParent].Children[k - Keys.D1]); return; }
                dirty = true;
                return;
            }
            int f = view.KeyFocus;
            if (k == Keys.Right || k == Keys.Down || k == Keys.Tab && (key & Keys.Shift) == 0) f = f < 0 ? 0 : (f + 1) % n;
            else if (k == Keys.Left || k == Keys.Up || k == Keys.Tab) f = f < 0 ? n - 1 : (f - 1 + n) % n;
            else if (k >= Keys.D1 && k <= Keys.D9 && k - Keys.D1 < n) { f = k - Keys.D1; Activate(f); return; }
            else if ((k == Keys.Enter || k == Keys.Space) && f >= 0) { Activate(f); return; }
            if (f != view.KeyFocus) { view.KeyFocus = f; dirty = true; win.AnnounceFocus(f); }
        }

        /// <summary>For the tests: press a key or click as if on the dial.</summary>
        internal void SimulateKey(Keys k) { OnKey(k); }
        internal void SimulateClick(int x, int y) { OnClick(x, y); }
        internal int KeyFocus { get { return view == null ? -1 : view.KeyFocus; } }
        internal int OpenOptions { get { return view == null ? -1 : view.KidsParent; } }
        internal bool DetailsHidden { get { return view != null && view.HideDetails; } }

        void Activate(int index)
        {
            view.KeyFocus = index;
            if (items[index].IsParent) { OpenKids(index, Clock.Now); keyChip = 0; }
            else Pick(items[index]);
            dirty = true;
        }

        void OpenKids(int index, long now)
        {
            view.KidsParent = index;
            view.Chips = geo.LayoutPills(index, RadialRenderer.MeasurePills(items[index].Children, geo.Scale));
            view.ChipHover = new float[view.Chips.Length];
            view.KidsOpen = reduceMotion ? 1 : 0;
            kidsT = reduceMotion ? now - KidsOpenMs : now;
            dirty = true;
        }

        void CloseKids()
        {
            view.KidsParent = -1; view.Chips = null; keyChip = -1;
            dirty = true;
        }

        void Pick(ActionItem item)
        {
            Action<ActionItem> p = Picked;
            BeginClose(Clock.Now, 0);
            if (p != null) p(item);
        }

        void Dismiss()
        {
            BeginClose(Clock.Now, 0);
            Action d = Dismissed;
            if (d != null) d();
        }

        // ---------- every frame ----------

        public void Tick(long now, int cursorX, int cursorY, int headingTile, bool copyMode)
        {
            if (!Visible) return;
            if (view.CopyMode != copyMode) { view.CopyMode = copyMode; dirty = true; }
            int openMs = reduceMotion ? 1 : OpenMs, closeMs = reduceMotion ? 1 : CloseMs;

            float alpha = Math.Min(1f, (now - showT) / (float)openMs);
            if (closing && now > closeAt)
            {
                alpha *= 1f - Math.Min(1f, (now - closeAt) / (float)closeMs);
                if (alpha <= 0) { Close(); return; }
            }

            // In click mode the keyboard wins until the mouse actually moves.
            bool moved = lastCursorX != int.MinValue && (Math.Abs(cursorX - lastCursorX) > 2 || Math.Abs(cursorY - lastCursorY) > 2);
            lastCursorX = cursorX; lastCursorY = cursorY;
            if (moved && keyMode) { keyMode = false; view.KeyFocus = -1; keyChip = -1; dirty = true; }

            int seg = keyMode ? -1 : geo.SegmentAt(cursorX - ax, cursorY - ay);
            if (seg != hover) { hover = seg; hoverSince = now; }
            if (seg >= 0) EverHovered = true;
            // Open options stay while the pointer merely crosses a neighbouring slice on its way to them
            // (a long column of options reaches past the ring); they close once it rests somewhere else.
            if (seg >= 0 && view.KidsParent >= 0 && seg != view.KidsParent && now - hoverSince >= KidsLeaveMs) CloseKids();
            if (hover >= 0 && items[hover].IsParent && view.KidsParent != hover && now - hoverSince >= (reduceMotion ? 120 : KidsDelayMs))
                OpenKids(hover, now);
            chipHover = keyMode ? keyChip : ChipAt(cursorX, cursorY);
            RectangleF db = view.DetailsBox;
            db.Inflate(14, 14);
            bool overDetails = view.DetailsBox.Width > 0 && db.Contains((float)(cursorX - ax), (float)(cursorY - ay));
            if (overDetails != view.HideDetails) { view.HideDetails = overDetails; dirty = true; }
            view.LabelIndex = hover >= 0 ? hover : view.LabelIndex;

            // ease every animated value toward its target (or jump there when motion is reduced)
            float rate = reduceMotion ? 1f : 0.35f;
            dirty |= Approach(ref view.Open, Math.Min(1f, (now - showT) / (float)openMs), 1f);
            for (int i = 0; i < items.Count; i++)
            {
                float hTarget = (i == hover || i == view.KidsParent) ? 1f : 0f;
                float gTarget = (hover < 0 && view.KidsParent < 0 && i == headingTile) ? 1f : 0f;
                dirty |= Approach(ref view.Hover[i], hTarget, rate);
                dirty |= Approach(ref view.Heading[i], gTarget, reduceMotion ? 1f : 0.25f);
            }
            if (view.KidsParent >= 0)
            {
                dirty |= Approach(ref view.KidsOpen, Math.Min(1f, (now - kidsT) / (float)(reduceMotion ? 1 : KidsOpenMs)), 1f);
                for (int j = 0; j < view.ChipHover.Length; j++)
                    dirty |= Approach(ref view.ChipHover[j], j == chipHover ? 1f : 0f, reduceMotion ? 1f : 0.4f);
            }

            if (!dirty && Math.Abs(alpha - lastAlpha) < 0.004f) return;
            RadialRenderer.RenderInto(frame, view);
            win.Present(frame, (int)Math.Round(ax) - geo.HalfSize, (int)Math.Round(ay) - geo.HalfSize, (byte)(255 * alpha));
            lastAlpha = alpha;
            dirty = false;
        }

        static bool Approach(ref float value, float target, float rate)
        {
            if (Math.Abs(value - target) < 0.003f)
            {
                if (value == target) return false;
                value = target;
                return true;
            }
            value += (target - value) * rate;
            return true;
        }

        public void Close()
        {
            if (win != null) { win.Dispose(); win = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            view = null;
            closing = false;
        }
    }
}
