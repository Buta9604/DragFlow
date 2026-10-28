using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>The small target a drawn shape offers next to the pointer (the real one is <see cref="GestureBubble"/>).</summary>
    public interface IGestureBubble
    {
        bool Visible { get; }
        void Show(ActionItem item, int x, int y, double scale);
        bool Contains(int x, int y);
        /// <summary>Close now, or after a moment so a drop already in flight can land.</summary>
        void Hide(int delayMs);
    }

    /// <summary>
    /// One action in a pill, placed beside the pointer (never under it, so a shape drawn by accident never changes
    /// where the drag goes). Move onto it and let go to run the action.
    /// </summary>
    public sealed class GestureBubble : IGestureBubble
    {
        public event Action<ActionItem, string[], DropInfo> Dropped;

        MenuWindow win;
        ActionItem item;
        static string icons;
        Rectangle bounds;
        readonly Timer later = new Timer();

        public GestureBubble()
        {
            later.Tick += delegate { later.Stop(); Close(); };
        }

        public bool Visible { get { return win != null; } }

        public bool Contains(int x, int y) { return win != null && bounds.Contains(x, y); }

        public void Show(ActionItem action, int x, int y, double scale)
        {
            Close();
            item = action;
            Theme th = Theme.Current();
            float s = (float)Math.Max(1, scale);
            if (icons == null) icons = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            using (var font = new Font("Segoe UI Semibold", 13f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var glyphFont = new Font(icons, 14f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                string text = action.Label;
                Size ts = TextRenderer.MeasureText(text, font);
                int h = (int)(34 * s), w = ts.Width + (int)(52 * s);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Color accent = th.AccentFor(action.Accent);
                    using (GraphicsPath p = Rounded(new RectangleF(0.5f, 0.5f, w - 1, h - 1), h / 2f))
                    {
                        using (var fill = new SolidBrush(Theme.WithAlpha(th.Surface, 245))) g.FillPath(fill, p);
                        using (var pen = new Pen(accent, 2 * s)) g.DrawPath(pen, p);
                    }
                    using (var dot = new SolidBrush(accent)) g.FillEllipse(dot, 6 * s, 5 * s, h - 10 * s, h - 10 * s);
                    using (var white = new SolidBrush(Color.White))
                    using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(string.IsNullOrEmpty(action.Glyph) ? "" : action.Glyph, glyphFont, white, new RectangleF(6 * s, 5 * s, h - 10 * s, h - 10 * s), center);
                    using (var tb = new SolidBrush(th.Text))
                    using (var left = new StringFormat { LineAlignment = StringAlignment.Center })
                        g.DrawString(text, font, tb, new RectangleF(h - 2 * s, 0, w - h, h), left);
                }
                bounds = new Rectangle(x - w / 2, y - h / 2, w, h);
                win = new MenuWindow(false) { HitTest = delegate (int px, int py) { return bounds.Contains(px, py) ? item : null; } };
                win.SliceNames = delegate { return new System.Collections.Generic.List<string> { item.Label }; };
                win.FilesDropped += delegate (ActionItem a, string[] files, DropInfo info)
                {
                    Action<ActionItem, string[], DropInfo> d = Dropped;
                    if (d != null) d(a, files, info);
                    Hide(50); // not inside the drop itself: Windows is still delivering it to this window
                };
                win.Show();
                win.Bounds = bounds;
                win.Present(bmp, bounds.X, bounds.Y, 255);
                bmp.Dispose();
            }
            Log.Debug("gesture offers " + action.Id);
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        public void Hide(int delayMs)
        {
            if (win == null) return;
            if (delayMs <= 0) { later.Stop(); Close(); return; }
            later.Interval = delayMs;
            later.Start();
        }

        void Close()
        {
            if (win == null) return;
            MenuWindow w = win;
            win = null;
            try { w.Close(); w.Dispose(); } catch (Exception) { }
        }

        /// <summary>Where the bubble goes: beside the pointer, across the direction it was moving, kept on the screen.</summary>
        public static Point Place(int x, int y, double dirX, double dirY, double scale, Rectangle screen)
        {
            double len = Math.Sqrt(dirX * dirX + dirY * dirY);
            double px = 0, py = -1; // above, when the movement was sideways
            if (len > 0.01 && Math.Abs(dirY / len) > 0.7) { px = -1; py = 0; } // to the left, when it was up or down
            double dist = 64 * scale;
            int bx = (int)(x + px * dist), by = (int)(y + py * dist);
            int margin = (int)(80 * scale);
            bx = Math.Max(screen.Left + margin, Math.Min(screen.Right - margin, bx));
            by = Math.Max(screen.Top + 20, Math.Min(screen.Bottom - 20, by));
            if (Math.Abs(bx - x) < 30 * scale && Math.Abs(by - y) < 30 * scale) by = y + (int)(dist * (y - screen.Top > dist + 20 ? -1 : 1)); // never under the pointer
            return new Point(bx, by);
        }
    }
}
