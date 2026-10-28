using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace DragFlow
{
    /// <summary>
    /// Low-level mouse hook that heals itself.
    /// The hook lives on its own thread whose only job is to pump messages, and the callback only copies three numbers
    /// into a queue, so Windows never has a reason to drop it for being slow. A second thread turns the queue into
    /// drags (window look-ups happen there). If Windows removes the hook anyway, <see cref="CheckHealth"/> notices that
    /// the pointer moves without any events arriving and installs it again.
    /// </summary>
    public sealed class MouseHook : IDisposable
    {
        public readonly DragDetector Detector = new DragDetector();
        /// <summary>Raised (on a background thread) when the hook cannot be installed.</summary>
        public event Action<string> Problem;

        struct Raw { public int Msg, X, Y; public bool Touch; }

        const uint WM_REINSTALL = Native.WM_APP + 1;

        readonly ConcurrentQueue<Raw> queue = new ConcurrentQueue<Raw>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly Native.HookProc proc;
        Thread hookThread, worker;
        volatile uint hookThreadId;
        IntPtr hook = IntPtr.Zero;
        volatile bool stopping, installed;
        long lastEventAt, lastReinstallAt = -100000;
        int lastX = int.MinValue, lastY = int.MinValue;

        public int Reinstalls { get; private set; }
        public bool Installed { get { return installed; } }
        public long LastEventAt { get { return Interlocked.Read(ref lastEventAt); } }

        public MouseHook() { proc = Callback; }

        public void Start()
        {
            worker = new Thread(Work) { IsBackground = true, Name = "DragFlow drags" };
            worker.Start();
            hookThread = new Thread(Pump) { IsBackground = true, Name = "DragFlow mouse hook", Priority = ThreadPriority.AboveNormal };
            hookThread.Start();
            ready.WaitOne(3000);
            if (!installed) throw new InvalidOperationException("Could not install the mouse hook.");
        }

        public void Dispose()
        {
            stopping = true;
            if (hookThreadId != 0) Native.PostThreadMessage(hookThreadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            wake.Set();
        }

        /// <summary>Ask the hook thread to remove and re-add the hook.</summary>
        public void Reinstall(string why)
        {
            long now = Clock.Now;
            if (now - lastReinstallAt < 5000) return;
            lastReinstallAt = now;
            Log.Write("re-installing mouse hook: " + why);
            if (hookThreadId != 0) Native.PostThreadMessage(hookThreadId, WM_REINSTALL, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Called about once a second with the pointer position. If the pointer moved but no mouse event has reached
        /// the hook for a while, Windows has dropped the hook: put it back.
        /// </summary>
        public void CheckHealth(int x, int y)
        {
            bool moved = lastX != int.MinValue && (x != lastX || y != lastY);
            lastX = x; lastY = y;
            if (stopping || !moved) return;
            if (Clock.Now - LastEventAt > 1500) Reinstall("pointer moved but no mouse events arrived");
        }

        void Pump()
        {
            hookThreadId = Native.GetCurrentThreadId();
            Install();
            ready.Set();
            MSG m;
            while (!stopping && Native.GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
            {
                if (m.message == WM_REINSTALL)
                {
                    if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                    Install();
                    Reinstalls++;
                }
            }
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            installed = false;
        }

        void Install()
        {
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
            installed = hook != IntPtr.Zero;
            Interlocked.Exchange(ref lastEventAt, Clock.Now);
            if (installed) Log.Write("hook installed");
            else
            {
                string msg = "SetWindowsHookEx failed, error " + Marshal.GetLastWin32Error();
                Log.Write(msg);
                Action<string> p = Problem;
                if (p != null) p(msg);
            }
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_MOUSEMOVE || msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP)
                {
                    // MSLLHOOKSTRUCT: the point (two ints), mouseData, flags, time, then dwExtraInfo, which Windows marks
                    // for pen and touch input (0xFF5157xx). Nothing else happens here.
                    long extra = Marshal.ReadIntPtr(lParam, IntPtr.Size == 8 ? 24 : 20).ToInt64();
                    queue.Enqueue(new Raw { Msg = msg, X = Marshal.ReadInt32(lParam), Y = Marshal.ReadInt32(lParam, 4), Touch = (extra & 0xFFFFFF00) == 0xFF515700 });
                    Interlocked.Exchange(ref lastEventAt, Clock.Now);
                    wake.Set();
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        void Work()
        {
            while (!stopping)
            {
                wake.WaitOne();
                Raw r;
                while (queue.TryDequeue(out r))
                {
                    try
                    {
                        if (r.Msg == Native.WM_LBUTTONDOWN) Detector.Down(r.X, r.Y, Clock.Now, r.Touch);
                        else if (r.Msg == Native.WM_MOUSEMOVE) Detector.Move(r.X, r.Y, Clock.Now);
                        else Detector.Up();
                    }
                    catch (Exception ex) { Log.Error("drag detector", ex); }
                }
            }
        }
    }
}
