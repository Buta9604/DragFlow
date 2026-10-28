using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace DragFlow.Tests
{
    /// <summary>A dial that only records what it was asked to do.</summary>
    sealed class FakeOverlay : IOverlay
    {
        public bool Visible { get; set; }
        public bool HasKids { get { return false; } }
        public bool EverHovered { get; set; }
        public int Shows;
        public IList<ActionItem> Items;
        public string Summary, Tip;
        public double AX, AY, Scale;
        bool closing;
        long closeAt;

        public void Show(double anchorX, double anchorY, IList<ActionItem> items, double scale, long now, string summary, string tip)
        {
            Visible = true; closing = false; Shows++;
            Items = items; Summary = summary; Tip = tip; AX = anchorX; AY = anchorY; Scale = scale;
        }

        public bool IsOverMenu(int x, int y)
        {
            double dx = x - AX, dy = y - AY;
            return Visible && Math.Sqrt(dx * dx + dy * dy) <= RadialGeometry.OuterR + 6;
        }

        public void BeginClose(long now, int delayMs)
        {
            if (!Visible || closing) return;
            closing = true;
            closeAt = now + delayMs;
        }

        public void Tick(long now, int x, int y, int heading, bool copy)
        {
            if (closing && now >= closeAt + 130) { Visible = false; closing = false; }
        }

        public void Close() { Visible = false; closing = false; }

        public Image Thumb;
        public string Details;
        public void SetPreview(Image thumb, string details) { Thumb = thumb; Details = details; }
    }

    /// <summary>A gesture target that only records what it was asked to offer.</summary>
    sealed class FakeBubble : IGestureBubble
    {
        public ActionItem Offered;
        public Rectangle Bounds;
        public bool Visible { get; set; }
        public void Show(ActionItem item, int x, int y, double scale) { Offered = item; Visible = true; Bounds = new Rectangle(x - 50, y - 16, 100, 32); }
        public bool Contains(int x, int y) { return Visible && Bounds.Contains(x, y); }
        public void Hide(int delayMs) { Visible = false; }
    }

    /// <summary>A mouse and screen the tests move by hand.</summary>
    sealed class FakeDesktop : IDesktop
    {
        public int X, Y;
        public bool Button = true;
        public readonly HashSet<int> Keys = new HashSet<int>();

        public bool CursorPos(out int x, out int y) { x = X; y = Y; return true; }
        public bool ButtonDown() { return Button; }
        public bool KeyDown(int vk) { return Keys.Contains(vk); }
        public double ScaleAt(int x, int y) { return 1.0; }
        public Rectangle Area = new Rectangle(0, 0, 1920, 1040);
        public Rectangle WorkingArea(int x, int y) { return Area; }
        public void MoveCursor(int x, int y) { X = x; Y = y; Moves++; }
        public int Moves;
    }

    sealed class FakeNotifier : INotifier
    {
        public readonly List<string> Shown = new List<string>();
        public void Show(string title, string text, params NoteButton[] buttons) { Shown.Add(title + ": " + text); }
    }

    static class Make
    {
        public static string Dir(string root, string name)
        {
            string d = Path.Combine(root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        public static string Png(string dir, string name, int w = 40, int h = 30)
        {
            string path = Path.Combine(dir, name);
            using (var bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.SteelBlue);
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        public static string Text(string dir, string name, string content = "x")
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, content);
            return path;
        }
    }
}
