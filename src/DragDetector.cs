using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DragFlow
{
    public enum SourceKind { Explorer, Desktop, App }

    /// <summary>One press-and-drag that started on a surface DragFlow watches (Explorer, the desktop, an opted-in app).</summary>
    public sealed class DragSession
    {
        static int nextId;
        public readonly int Id = Interlocked.Increment(ref nextId);

        public int OriginX, OriginY;
        public long StartedAt;
        public IntPtr RootWindow, TabWindow;
        public string RootClass = "", Process = "";
        public SourceKind Source;

        /// <summary>0 = still checking, 1 = dragging files, -1 = not a file drag or the dial should stay hidden.</summary>
        public volatile int FileState;
        /// <summary>Items being dragged when Explorer could tell us; null means "unknown" (the drop still carries the truth).</summary>
        public volatile string[] Paths;
        /// <summary>The dial's actions, built off the UI thread so the pointer never stalls.</summary>
        public volatile List<ActionItem> Menu;
        /// <summary>Shown in the middle of the dial, e.g. "3 items · 12 MB".</summary>
        public volatile string Summary;
        /// <summary>Why the dial stays hidden this time (paused, excluded app, full screen…), for the log.</summary>
        public volatile string QuietReason;
        /// <summary>What kind of thing is dragged, when known.</summary>
        public FileKind Kind = FileKind.Other;
        /// <summary>The folder the drag started in, when known.</summary>
        public volatile string Folder;
        /// <summary>Opened on purpose (shortcut or right-click menu), not by dragging: no quiet rules, no pointer check.</summary>
        public bool Explicit;
        /// <summary>Started with a pen or a finger (Windows marks those mouse messages).</summary>
        public bool Touch;
        /// <summary>A thumbnail and a detail ("4032 × 3024") of what is dragged, filled in a little after the drag starts.</summary>
        public volatile System.Drawing.Bitmap Thumb;
        public volatile string Details;
    }

    public static class Clock
    {
        static readonly Stopwatch sw = Stopwatch.StartNew();
        public static long Now { get { return sw.ElapsedMilliseconds; } }
    }

    /// <summary>The window under a press, as far as dragging is concerned.</summary>
    public sealed class Surface
    {
        public IntPtr Root, Tab;
        public string Class = "", Process = "";
        public SourceKind Source;
    }

    /// <summary>
    /// Turns raw mouse events into drags. Pure logic (the window look-up is injected), so it is tested without a mouse.
    /// A new press while a drag is still open ends that drag first, so a lost button-up can never leave one hanging.
    /// </summary>
    public sealed class DragDetector
    {
        public event Action<DragSession> Started;
        public event Action<int, int, long> Moved;
        public event Action<DragSession> Ended;

        /// <summary>Which surface is at a point, or null when presses there should be ignored.</summary>
        public Func<int, int, Surface> Probe = delegate { return null; };
        /// <summary>True to skip this press (Alt held).</summary>
        public Func<bool> Bypass = delegate { return false; };
        public volatile bool Enabled = true;
        public int ThresholdX = 4, ThresholdY = 4;

        DragSession pending;
        bool dragging;
        int downX, downY;

        public bool Dragging { get { return dragging; } }

        public void Down(int x, int y, long t) { Down(x, y, t, false); }

        public void Down(int x, int y, long t, bool touch)
        {
            EndIfDragging();
            pending = null;
            if (!Enabled || Bypass()) return;
            Surface s = Probe(x, y);
            if (s == null) return;
            downX = x; downY = y;
            pending = new DragSession
            {
                OriginX = x, OriginY = y, StartedAt = t,
                RootWindow = s.Root, TabWindow = s.Tab, RootClass = s.Class, Process = s.Process, Source = s.Source, Touch = touch
            };
        }

        public void Move(int x, int y, long t)
        {
            if (pending == null) return;
            if (!dragging)
            {
                if (Math.Abs(x - downX) < ThresholdX && Math.Abs(y - downY) < ThresholdY) return;
                dragging = true;
                pending.StartedAt = t;
                Action<DragSession> started = Started;
                if (started != null) started(pending);
            }
            Action<int, int, long> moved = Moved;
            if (moved != null) moved(x, y, t);
        }

        public void Up()
        {
            EndIfDragging();
            pending = null;
        }

        void EndIfDragging()
        {
            if (!dragging) return;
            dragging = false;
            DragSession s = pending;
            Action<DragSession> ended = Ended;
            if (ended != null) ended(s);
        }
    }
}
