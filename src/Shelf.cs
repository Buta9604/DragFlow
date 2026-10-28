using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>One drop onto the shelf: the items stay where they are; the shelf only remembers them.</summary>
    public sealed class ShelfGroup
    {
        public string[] Paths;
        public DateTime Added;

        public string Label
        {
            get
            {
                if (Paths.Length == 1) return FileOps.NameOf(Paths[0]);
                return Paths.Length + " items";
            }
        }
    }

    /// <summary>What is parked, kept in %APPDATA%\DragFlow\shelf.txt so it survives a restart.</summary>
    public sealed class ShelfStore
    {
        public static ShelfStore Current = new ShelfStore();
        public const int MaxGroups = 30;

        readonly List<ShelfGroup> groups = new List<ShelfGroup>();
        public event Action Changed = delegate { };

        public string FilePath { get { return AppPaths.RoamingFile("shelf.txt"); } }

        public List<ShelfGroup> Groups
        {
            get { lock (groups) return groups.ToList(); }
        }

        public void Add(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            lock (groups)
            {
                groups.RemoveAll(g => g.Paths.Length == paths.Length && g.Paths.All(p => paths.Contains(p, StringComparer.OrdinalIgnoreCase)));
                groups.Add(new ShelfGroup { Paths = paths.ToArray(), Added = DateTime.Now });
                while (groups.Count > MaxGroups) groups.RemoveAt(0);
            }
            Save();
            Changed();
        }

        public void Remove(ShelfGroup g)
        {
            lock (groups) groups.Remove(g);
            Save();
            Changed();
        }

        public void Clear()
        {
            lock (groups) groups.Clear();
            Save();
            Changed();
        }

        /// <summary>Forget items that were moved or deleted since they were parked.</summary>
        public void Prune()
        {
            bool changed = false;
            lock (groups)
                foreach (ShelfGroup g in groups.ToList())
                {
                    string[] still = g.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
                    if (still.Length == g.Paths.Length) continue;
                    changed = true;
                    if (still.Length == 0) groups.Remove(g); else g.Paths = still;
                }
            if (changed) { Save(); Changed(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                lock (groups)
                    File.WriteAllLines(FilePath, groups.Select(g => g.Added.Ticks + "\t" + string.Join("\t", g.Paths)).ToArray(), Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static ShelfStore Load()
        {
            var s = new ShelfStore();
            try
            {
                if (File.Exists(s.FilePath))
                    foreach (string line in File.ReadAllLines(s.FilePath, Encoding.UTF8))
                    {
                        string[] p = line.Split('\t');
                        long t;
                        if (p.Length < 2 || !long.TryParse(p[0], out t)) continue;
                        s.groups.Add(new ShelfGroup { Added = new DateTime(t), Paths = p.Skip(1).Where(x => x.Length > 0).ToArray() });
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }
    }

    /// <summary>
    /// The shelf at the right edge of the screen. While files are being dragged it opens a "Park here" drop zone;
    /// afterwards it folds into a slim tab that opens on hover. Drag an entry out into any folder or app, as if
    /// dragging it from where it really is.
    /// </summary>
    public sealed class ShelfWindow : Form
    {
        const int W = 250, TabW = 16, TabH = 120, RowH = 40, HeadH = 34, DropH = 64;

        readonly ShelfStore store;
        readonly Func<Settings> settings;
        readonly Timer timer = new Timer { Interval = 120 };
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        bool dragZone, expanded, pinned, dropHover;
        Rectangle screen;
        int hoverRow = -1;
        Point downAt;
        ShelfGroup downGroup;
        long leftAt = -1;
        static readonly Font HeadFont = new Font("Segoe UI Semibold", 10f), SmallFont = new Font("Segoe UI", 7.5f), TabFont = new Font("Segoe UI Semibold", 8f);
        static readonly StringFormat Ellipsis = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        static readonly StringFormat Centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public ShelfWindow(ShelfStore store, Func<Settings> settings)
        {
            this.store = store; this.settings = settings;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AllowDrop = true;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9f);
            store.Changed += delegate { if (IsHandleCreated) BeginInvoke((Action)Relayout); };
            timer.Tick += delegate { OnTimer(); };
            timer.Start();
            screen = Screen.PrimaryScreen.WorkingArea;
            IntPtr unused = Handle; // so changes made before the shelf was first shown still reach it
        }

        Theme Th { get { return Theme.Current(); } }

        /// <summary>A file drag started somewhere: open the drop zone on that screen.</summary>
        public void BeginDragZone(int x, int y)
        {
            if (!settings().ShelfEnabled) return;
            screen = Screen.FromPoint(new Point(x, y)).WorkingArea;
            dragZone = true;
            Relayout();
        }

        /// <summary>Show the folded tab if anything is parked (at start-up).</summary>
        public void ShowIfNeeded() { Relayout(); }

        public void EndDragZone()
        {
            if (!dragZone) return;
            dragZone = false;
            dropHover = false;
            leftAt = Clock.Now;
            Relayout();
        }

        void Relayout()
        {
            List<ShelfGroup> groups = store.Groups;
            bool show = dragZone || groups.Count > 0;
            if (!show) { if (Visible) Hide(); return; }
            Rectangle b;
            if (dragZone || expanded || pinned)
            {
                int h = HeadH + (dragZone ? DropH : 0) + Math.Max(groups.Count, dragZone ? 0 : 1) * RowH + 10;
                h = Math.Min(h, screen.Height - 40);
                b = new Rectangle(screen.Right - W, screen.Top + (screen.Height - h) / 2, W, h);
            }
            else b = new Rectangle(screen.Right - TabW, screen.Top + (screen.Height - TabH) / 2, TabW, TabH);
            Bounds = b;
            Region old = Region;
            Region = Rounded(new Rectangle(0, 0, b.Width, b.Height), 10, b.Width == W);
            if (old != null) old.Dispose();
            if (!Visible) Show();
            Invalidate();
        }

        static Region Rounded(Rectangle r, int radius, bool leftOnly)
        {
            using (var p = new GraphicsPath())
            {
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddLine(r.Right, r.Y, r.Right, r.Bottom);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                return new Region(p);
            }
        }

        void OnTimer()
        {
            if (!Visible || dragZone || pinned) return;
            bool inside = Bounds.Contains(Cursor.Position);
            if (inside && !expanded) { expanded = true; Relayout(); }
            else if (!inside && expanded && (Control.MouseButtons & MouseButtons.Left) == 0)
            {
                if (leftAt < 0) leftAt = Clock.Now;
                if (Clock.Now - leftAt > 600) { expanded = false; leftAt = -1; Relayout(); }
            }
            else if (inside) leftAt = -1;
        }

        // ---------- parking: drops onto the shelf ----------

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = VirtualFiles.CanProvideFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            dropHover = true; Invalidate();
            base.OnDragEnter(e);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            e.Effect = VirtualFiles.CanProvideFiles(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            base.OnDragOver(e);
        }

        protected override void OnDragLeave(EventArgs e) { dropHover = false; Invalidate(); base.OnDragLeave(e); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            dropHover = false;
            try
            {
                List<string> saved;
                string[] files = VirtualFiles.FromDrop(e.Data, out saved);
                if (files != null && files.Length > 0)
                {
                    e.Effect = DragDropEffects.Copy; // nothing moves: the shelf only remembers where they are
                    store.Add(files);
                    Log.Write("parked " + files.Length + " item(s)");
                }
            }
            catch (Exception ex) { Log.Error("shelf drop", ex); }
            base.OnDragDrop(e);
        }

        // ---------- taking things back out ----------

        int ListTop { get { return HeadH + (dragZone ? DropH : 0); } }

        int RowAt(Point p)
        {
            if (p.Y < ListTop) return -1;
            int i = (p.Y - ListTop) / RowH;
            return i < store.Groups.Count ? i : -1;
        }

        Rectangle RemoveBox(int row) { return new Rectangle(W - 30, ListTop + row * RowH + 10, 20, 20); }
        Rectangle ClearBox { get { return new Rectangle(W - 62, 8, 52, 20); } }
        Rectangle PinBox { get { return new Rectangle(W - 104, 8, 38, 20); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Width != W) return;
            if (ClearBox.Contains(e.Location)) { store.Clear(); return; }
            if (PinBox.Contains(e.Location)) { pinned = !pinned; Relayout(); return; }
            int row = RowAt(e.Location);
            if (row < 0) return;
            ShelfGroup g = store.Groups[row];
            if (RemoveBox(row).Contains(e.Location)) { store.Remove(g); return; }
            downAt = e.Location;
            downGroup = g;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            if (row != hoverRow) { hoverRow = row; Invalidate(); }
            if (downGroup == null || (e.Button & MouseButtons.Left) == 0) return;
            if (Math.Abs(e.X - downAt.X) < 4 && Math.Abs(e.Y - downAt.Y) < 4) return;
            ShelfGroup g = downGroup;
            downGroup = null;
            string[] still = g.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            if (still.Length == 0) { store.Remove(g); return; }
            var data = new DataObject();
            var list = new StringCollection();
            list.AddRange(still);
            data.SetFileDropList(list);
            DragDropEffects done = DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            Log.Write("dragged " + still.Length + " item(s) off the shelf: " + done);
            if (done != DragDropEffects.None && !settings().ShelfKeepAfterDrag) store.Remove(g);
            else store.Prune();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); downGroup = null; }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int row = RowAt(e.Location);
            if (row < 0) return;
            string first = store.Groups[row].Paths[0];
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + first.TrimEnd('\\') + "\"");
        }

        // ---------- drawing ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Theme th = Th;
            Color bg = th.Dark ? Color.FromArgb(38, 39, 45) : Color.FromArgb(250, 250, 252);
            Color text = th.Dark ? Color.FromArgb(238, 239, 244) : Color.FromArgb(30, 31, 36);
            g.Clear(bg);
            List<ShelfGroup> groups = store.Groups;

            if (Width != W)
            {
                using (var b = new SolidBrush(Color.FromArgb(66, 133, 244))) g.FillRectangle(b, 3, 10, 4, Height - 20);
                using (var tb = new SolidBrush(text))
                    g.DrawString(groups.Count.ToString(), TabFont, tb, new RectangleF(0, Height / 2 - 8, Width, 16), Centered);
                return;
            }

            using (var tb = new SolidBrush(text))
                g.DrawString("Shelf", HeadFont, tb, 12, 8);
            using (var sb = new SolidBrush(th.Subtle))
            {
                if (groups.Count > 0) g.DrawString("Clear", Font, sb, ClearBox.X + 8, ClearBox.Y + 2);
                g.DrawString(pinned ? "Unpin" : "Pin", Font, sb, PinBox.X, PinBox.Y + 2);
            }

            if (dragZone)
            {
                var zone = new Rectangle(10, HeadH, W - 20, DropH - 8);
                using (var pen = new Pen(Color.FromArgb(dropHover ? 255 : 150, 66, 133, 244), 2) { DashStyle = DashStyle.Dash })
                using (var fill = new SolidBrush(Color.FromArgb(dropHover ? 50 : 18, 66, 133, 244)))
                {
                    g.FillRectangle(fill, zone);
                    g.DrawRectangle(pen, zone);
                }
                using (var tb = new SolidBrush(text))
                    g.DrawString("Drop here to park", Font, tb, zone, Centered);
            }

            if (groups.Count == 0 && !dragZone)
                using (var sb = new SolidBrush(th.Subtle)) g.DrawString("Nothing parked", Font, sb, 12, ListTop + 10);

            for (int i = 0; i < groups.Count; i++)
            {
                int y = ListTop + i * RowH;
                if (y + RowH > Height) break;
                if (i == hoverRow)
                    using (var hb = new SolidBrush(th.Dark ? Color.FromArgb(52, 54, 62) : Color.FromArgb(236, 238, 244))) g.FillRectangle(hb, 6, y + 2, W - 12, RowH - 4);
                Bitmap icon = IconFor(groups[i].Paths[0]);
                if (icon != null) g.DrawImage(icon, 14, y + (RowH - 16) / 2, 16, 16);
                using (var tb = new SolidBrush(text))
                    g.DrawString(groups[i].Label, Font, tb, new RectangleF(38, y + 4, W - 76, 18), Ellipsis);
                using (var sb = new SolidBrush(th.Subtle))
                {
                    g.DrawString(FileOps.NameOf(FileOps.ParentOf(groups[i].Paths[0])), SmallFont, sb, new RectangleF(38, y + 21, W - 76, 15), Ellipsis);
                    if (i == hoverRow) g.DrawString("✕", Font, sb, RemoveBox(i).X + 3, RemoveBox(i).Y + 1);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        Bitmap IconFor(string path)
        {
            Bitmap b;
            if (icons.TryGetValue(path, out b)) return b;
            b = null;
            try
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x100 | 0x1 /* ICON | SMALLICON */) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    using (Icon ic = Icon.FromHandle(info.hIcon)) b = ic.ToBitmap();
                    DestroyIcon(info.hIcon);
                }
            }
            catch (Exception) { }
            icons[path] = b;
            return b;
        }
    }
}
