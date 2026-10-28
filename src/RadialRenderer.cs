using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace DragFlow
{
    /// <summary>Everything needed to draw one frame of the dial.</summary>
    public sealed class RadialView
    {
        public RadialGeometry Geo;
        public IList<ActionItem> Items;
        public Theme Theme = Theme.DarkTheme;
        /// <summary>0..1 open animation.</summary>
        public float Open = 1;
        /// <summary>Per-slice 0..1 hover and predicted-heading glow.</summary>
        public float[] Hover, Heading;
        public int LabelIndex = -1;
        public float[] LabelWidths;
        public int KidsParent = -1;
        public float KidsOpen;
        public RectangleF[] Chips;
        public float[] ChipHover;
        /// <summary>Shown in the see-through centre, e.g. "5 items" and "12 MB" on two lines.</summary>
        public string Summary;
        /// <summary>Ctrl is held: "Move to" reads "Copy to".</summary>
        public bool CopyMode;
        /// <summary>A short how-to shown under the dial the first few times.</summary>
        public string Tip;
        /// <summary>Slice picked with the keyboard (click mode), or -1.</summary>
        public int KeyFocus = -1;
        /// <summary>A thumbnail of what is dragged (in the centre) and a detail such as "4032 × 3024".</summary>
        public Image Thumb;
        public string Details;
        /// <summary>Where the details line was drawn (relative to the centre), and whether the pointer is over it: then it
        /// is left out, so that spot is see-through and a drop there reaches whatever is underneath.</summary>
        public RectangleF DetailsBox;
        public bool HideDetails;
    }

    /// <summary>Draws the dial: a compact donut, slice highlight, one label pill, sub-option chips.</summary>
    public static class RadialRenderer
    {
        /// <summary>Longer labels (folder names) are cut with "…" so pills never leave the window.</summary>
        public const double MaxPillWidth = 118;
        static Font glyphFont, pillFont, smallFont;
        static double fontScale = -1;
        static readonly StringFormat Center = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        static readonly StringFormat Wrap = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        /// <summary>"1 file · 2.4 MB · 4032 × 3024" when there is a thumbnail or a detail to show, else null.</summary>
        public static string DetailsLine(RadialView v)
        {
            if (v.Thumb == null && string.IsNullOrEmpty(v.Details)) return null;
            string line = (v.Summary ?? "").Replace("\n", " · ").Trim();
            if (!string.IsNullOrEmpty(v.Details)) line = line.Length > 0 ? line + " · " + v.Details : v.Details;
            return line.Length > 0 ? line : null;
        }

        /// <summary>The slice's name, adjusted while Ctrl is held ("Move to" becomes "Copy to").</summary>
        public static string LabelFor(ActionItem item, bool copyMode)
        {
            return copyMode && item.Key == "Move to" ? L.T("Copy to") : item.Label;
        }

        static void EnsureFonts(double s)
        {
            if (glyphFont != null && Math.Abs(fontScale - s) < 0.01) return;
            fontScale = s;
            string[] installed = FontFamily.Families.Select(f => f.Name).ToArray();
            string icons = installed.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                : installed.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : "Segoe UI Symbol";
            glyphFont = new Font(icons, (float)(16 * s), FontStyle.Regular, GraphicsUnit.Pixel);
            pillFont = new Font("Segoe UI Semibold", (float)(12 * s), FontStyle.Regular, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", (float)(9 * s), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        /// <summary>Pill widths for a set of labels (text + padding).</summary>
        public static float[] MeasurePills(IList<ActionItem> items, double s)
        {
            EnsureFonts(s);
            var widths = new float[items.Count];
            using (var bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < items.Count; i++)
                    widths[i] = (float)Math.Min(MaxPillWidth * s, Math.Ceiling(g.MeasureString(items[i].Label, pillFont, 400, StringFormat.GenericTypographic).Width + 26 * s));
            }
            return widths;
        }

        public static Bitmap Render(RadialView v)
        {
            int size = v.Geo.HalfSize * 2;
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            RenderInto(bmp, v);
            return bmp;
        }

        // The ring itself (shadow, body, separators) only changes with the slice count, size and theme: drawn once.
        static Bitmap staticLayer;
        static int staticCount = -1;
        static double staticScale = -1;
        static Theme staticTheme;

        static Bitmap StaticLayer(RadialGeometry geo, Theme th, int n)
        {
            if (staticLayer != null && staticCount == n && Math.Abs(staticScale - geo.Scale) < 0.001 && staticTheme == th) return staticLayer;
            if (staticLayer != null) staticLayer.Dispose();
            int size = geo.HalfSize * 2;
            staticLayer = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(staticLayer))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(geo.HalfSize, geo.HalfSize);
                DrawRing(g, geo, th, n);
            }
            staticCount = n; staticScale = geo.Scale; staticTheme = th;
            return staticLayer;
        }

        static void DrawRing(Graphics g, RadialGeometry geo, Theme th, int n)
        {
            float s = (float)geo.Scale, inner = (float)geo.Inner, outer = (float)geo.Outer;
            // soft shadow (not in high contrast), then the donut body with an edge
            if (!th.HighContrast)
                for (int k = 8; k >= 1; k--)
                    using (GraphicsPath sp = Donut(inner + k * 0.6f * s, outer + k * 1.3f * s, 0, 2.5f * s))
                    using (var sb = new SolidBrush(Theme.WithAlpha(th.Shadow, th.Dark ? 9 : 3)))
                        g.FillPath(sb, sp);
            using (GraphicsPath ring = Donut(inner, outer, 0, 0))
            {
                using (var br = new SolidBrush(th.Surface)) g.FillPath(br, ring);
                using (var pen = new Pen(th.Border, Math.Max(1f, s * th.BorderWidth))) g.DrawPath(pen, ring);
            }
            // separators between slices
            if (n > 1)
                using (var sep = new Pen(Theme.WithAlpha(th.Border, th.Border.A * (th.HighContrast ? 1 : 0.7)), Math.Max(1f, s * 0.8f * th.BorderWidth)))
                    for (int i = 0; i < n; i++)
                    {
                        double a = RingLayout.Angle(i, n) - Math.PI / n;
                        g.DrawLine(sep, (float)(Math.Cos(a) * (inner + 6 * s)), (float)(Math.Sin(a) * (inner + 6 * s)),
                                        (float)(Math.Cos(a) * (outer - 6 * s)), (float)(Math.Sin(a) * (outer - 6 * s)));
                    }
        }

        /// <summary>Draw one frame into an existing bitmap of the right size (re-used between frames).</summary>
        public static void RenderInto(Bitmap bmp, RadialView v)
        {
            RadialGeometry geo = v.Geo;
            Theme th = v.Theme;
            float s = (float)geo.Scale;
            EnsureFonts(s);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = th.HighContrast ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.AntiAliasGridFit;

                float open = Ease(v.Open);
                int n = v.Items.Count;
                if (open >= 0.999f)
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(StaticLayer(geo, th, n), 0, 0);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.TranslateTransform(geo.HalfSize, geo.HalfSize);
                }
                else
                {
                    g.TranslateTransform(geo.HalfSize, geo.HalfSize);
                    float zoom = 0.86f + 0.14f * open;
                    g.ScaleTransform(zoom, zoom);
                    DrawRing(g, geo, th, n);
                }

                float inner = (float)geo.Inner, outer = (float)geo.Outer;
                float step = 360f / n;

                // slice washes: predicted heading (faint), hover or keyboard focus (stronger)
                for (int i = 0; i < n; i++)
                {
                    float hv = Math.Max(v.Hover[i], i == v.KeyFocus ? 1f : 0f);
                    float wash = Math.Max(hv * th.HoverWash, v.Heading[i] * th.HeadingWash);
                    if (wash < 1) continue;
                    float mid = (float)(RingLayout.Angle(i, n) * 180 / Math.PI);
                    using (GraphicsPath w = Wedge(inner + 3 * s, outer - 3 * s, mid - step / 2 + 1.2f, step - 2.4f))
                    using (var wb = new SolidBrush(Theme.WithAlpha(th.AccentFor(v.Items[i].Accent), wash)))
                        g.FillPath(wb, w);
                }

                // icons
                for (int i = 0; i < n; i++)
                {
                    double ix, iy;
                    geo.IconCenter(i, out ix, out iy);
                    ActionItem item = v.Items[i];
                    Color accent = th.AccentFor(item.Accent);
                    float h = Math.Max(v.Hover[i], i == v.KeyFocus ? 1f : 0f);
                    float r = (15 + 2 * h) * s;
                    if (h > 0.01f)
                        using (var cb = new SolidBrush(Theme.WithAlpha(accent, 255 * h)))
                            g.FillEllipse(cb, (float)ix - r, (float)iy - r, 2 * r, 2 * r);
                    Color glyph = th.HighContrast ? (h > 0.5f ? SystemColors.HighlightText : th.Text)
                                : Theme.Mix(th.Dark ? Theme.Mix(accent, Color.White, 0.15) : accent, Color.White, h);
                    using (var gb = new SolidBrush(glyph))
                        g.DrawString(item.Glyph, glyphFont, gb, new RectangleF((float)ix - 20 * s, (float)iy - 20 * s, 40 * s, 40 * s), Center);
                    if (item.IsParent)
                    {
                        double a = RingLayout.Angle(i, n);
                        float dr = 1.8f * s, dd = outer - 8 * s;
                        using (var db = new SolidBrush(Theme.Mix(th.Subtle, accent, h)))
                            g.FillEllipse(db, (float)(Math.Cos(a) * dd) - dr, (float)(Math.Sin(a) * dd) - dr, 2 * dr, 2 * dr);
                    }
                }

                // what is being dragged: its thumbnail in the centre, or a small label
                Image thumb = v.Thumb;
                if (thumb != null)
                {
                    float tr = inner - 3 * s;
                    float k = Math.Max(2 * tr / thumb.Width, 2 * tr / thumb.Height); // fill the circle
                    float tw = thumb.Width * k, tht = thumb.Height * k;
                    using (var clip = new GraphicsPath())
                    {
                        clip.AddEllipse(-tr, -tr, 2 * tr, 2 * tr);
                        GraphicsState saved = g.Save();
                        g.SetClip(clip);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(thumb, -tw / 2, -tht / 2, tw, tht);
                        g.Restore(saved);
                        using (var ring = new Pen(Theme.WithAlpha(th.Border, 200), 1.5f * s)) g.DrawPath(ring, clip);
                    }
                }
                else if (!string.IsNullOrEmpty(v.Summary))
                {
                    // A small label only: the rest of the hole stays see-through, so drops there still reach Explorer.
                    float cr = inner - 4 * s;
                    SizeF ts = g.MeasureString(v.Summary, smallFont, (int)(2 * cr), Wrap);
                    var box = new RectangleF(-ts.Width / 2 - 4 * s, -ts.Height / 2 - 2 * s, ts.Width + 8 * s, ts.Height + 4 * s);
                    using (GraphicsPath bp = Round(box, 6 * s))
                    using (var cb = new SolidBrush(Theme.WithAlpha(th.Surface, th.HighContrast ? 255 : 225)))
                        g.FillPath(cb, bp);
                    using (var tb = new SolidBrush(th.HighContrast ? th.Text : th.Subtle))
                        g.DrawString(v.Summary, smallFont, tb, box, Wrap);
                }

                // name of the hovered slice
                int labelIndex = v.KeyFocus >= 0 ? v.KeyFocus : v.LabelIndex;
                if (labelIndex >= 0 && labelIndex != v.KidsParent)
                {
                    float la = Math.Max(v.Hover[labelIndex], labelIndex == v.KeyFocus ? 1f : 0f);
                    string text = LabelFor(v.Items[labelIndex], v.CopyMode);
                    float w = v.LabelWidths[labelIndex];
                    RectangleF lr = geo.LayoutPills(labelIndex, new[] { w })[0];
                    Pill(g, lr, text, th.Surface, th.Border, th.Text, th, s, la, 0);
                }

                // details line (size, dimensions, pages, length) under the dial, or above it while the tip shows
                string details = DetailsLine(v);
                if (details != null && v.KidsParent < 0)
                {
                    float busy = 0;
                    foreach (float x in v.Hover) busy = Math.Max(busy, x);
                    float da = Math.Max(0, 1 - busy * 2) * open;
                    if (da > 0.01f)
                    {
                        SizeF ds = g.MeasureString(details, smallFont, (int)(260 * s), Wrap);
                        bool above = !string.IsNullOrEmpty(v.Tip);
                        float top = above ? -outer - 14 * s - ds.Height - 6 * s : outer + 14 * s;
                        var box = new RectangleF(-ds.Width / 2 - 8 * s, top, ds.Width + 16 * s, ds.Height + 6 * s);
                        v.DetailsBox = box;
                        if (!v.HideDetails)
                        {
                            Pill(g, box, "", th.Surface, th.Border, th.Text, th, s, da, 0);
                            using (var tb = new SolidBrush(Theme.WithAlpha(th.HighContrast ? th.Text : th.Subtle, 255 * da)))
                                g.DrawString(details, smallFont, tb, box, Wrap);
                        }
                    }
                }

                // first-use tip under the dial, while nothing is pointed at
                if (!string.IsNullOrEmpty(v.Tip) && v.KidsParent < 0)
                {
                    float busy = 0;
                    foreach (float x in v.Hover) busy = Math.Max(busy, x);
                    float ta = Math.Max(0, 1 - busy * 2) * open;
                    if (ta > 0.01f)
                    {
                        SizeF ts = g.MeasureString(v.Tip, smallFont, (int)(240 * s), Wrap);
                        var box = new RectangleF(-ts.Width / 2 - 8 * s, outer + 14 * s, ts.Width + 16 * s, ts.Height + 8 * s);
                        Pill(g, box, "", th.Surface, th.Border, th.Text, th, s, ta, 0);
                        using (var tb = new SolidBrush(Theme.WithAlpha(th.Text, 255 * ta)))
                            g.DrawString(v.Tip, smallFont, tb, box, Wrap);
                    }
                }

                // sub-option chips
                if (v.KidsParent >= 0 && v.Chips != null)
                {
                    ActionItem parent = v.Items[v.KidsParent];
                    Color accent = th.AccentFor(parent.Accent);
                    float ko = Ease(v.KidsOpen);
                    double a = RingLayout.Angle(v.KidsParent, n);
                    for (int j = 0; j < v.Chips.Length; j++)
                    {
                        float stagger = Math.Max(0, Math.Min(1, ko * 1.6f - j * 0.15f));
                        RectangleF cr = v.Chips[j];
                        cr.Offset((float)(-Math.Cos(a) * 10 * s * (1 - stagger)), (float)(-Math.Sin(a) * 10 * s * (1 - stagger)));
                        float hv = v.ChipHover[j];
                        Color fill = Theme.Mix(th.Surface, Theme.WithAlpha(accent, 255), hv);
                        Color text = th.HighContrast ? (hv > 0.5f ? SystemColors.HighlightText : th.Text) : Theme.Mix(th.Text, Color.White, hv);
                        Pill(g, cr, parent.Children[j].Label, fill, th.Border, text, th, s, stagger, hv);
                    }
                }
            }
        }

        static void Pill(Graphics g, RectangleF r, string text, Color fill, Color border, Color textColor, Theme th, float s, float alpha, float lift)
        {
            if (alpha <= 0.01f) return;
            float rad = r.Height / 2;
            if (!th.HighContrast)
            for (int k = 4; k >= 1; k--)
            {
                RectangleF sr = r;
                sr.Inflate(k * s, k * s);
                sr.Offset(0, (1.5f + lift) * s);
                using (GraphicsPath sp = Round(sr, rad + k * s))
                using (var sb = new SolidBrush(Theme.WithAlpha(th.Shadow, (th.Dark ? 14 : 5) * alpha)))
                    g.FillPath(sb, sp);
            }
            using (GraphicsPath p = Round(r, rad))
            {
                using (var b = new SolidBrush(Theme.WithAlpha(fill, fill.A * alpha))) g.FillPath(b, p);
                using (var pen = new Pen(Theme.WithAlpha(border, border.A * alpha), Math.Max(1f, s * th.BorderWidth))) g.DrawPath(pen, p);
            }
            using (var tb = new SolidBrush(Theme.WithAlpha(textColor, 255 * alpha)))
                g.DrawString(text, pillFont, tb, new RectangleF(r.X, r.Y + 0.5f * s, r.Width, r.Height), Center);
        }

        static GraphicsPath Donut(float r1, float r2, float dx, float dy)
        {
            var p = new GraphicsPath(FillMode.Alternate);
            p.AddEllipse(dx - r2, dy - r2, 2 * r2, 2 * r2);
            p.AddEllipse(dx - r1, dy - r1, 2 * r1, 2 * r1);
            return p;
        }

        static GraphicsPath Wedge(float r1, float r2, float startDeg, float sweepDeg)
        {
            var p = new GraphicsPath();
            p.AddArc(-r2, -r2, 2 * r2, 2 * r2, startDeg, sweepDeg);
            p.AddArc(-r1, -r1, 2 * r1, 2 * r1, startDeg + sweepDeg, -sweepDeg);
            p.CloseFigure();
            return p;
        }

        static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static float Ease(float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return 1 - (1 - t) * (1 - t) * (1 - t);
        }
    }
}
