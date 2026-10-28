using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace DragFlow.Tests
{
    /// <summary>Dependency-free test runner. Usage: DragFlow.Tests.exe [--render out.png]</summary>
    public static class TestMain
    {
        static int passed, failed;

        static long lastProgress = Environment.TickCount;

        internal static void Check(bool ok, string name)
        {
            lastProgress = Environment.TickCount;
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        /// <summary>Where undo puts files the tests created (instead of the real Recycle Bin).</summary>
        internal static string Trash;

        [STAThread]
        public static int Main(string[] args)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dragflow-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            Sandbox(tmp);
            StartWatchdog(Thread.CurrentThread);
            // A crash on any thread must end the run with a message, never hang in a Windows error dialog.
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Console.WriteLine("  CRASH " + e.ExceptionObject);
                Console.Out.Flush();
                Environment.Exit(3);
            };
            try
            {
                if (args.Length == 2 && args[0] == "--render") { RenderDemo(args[1]); return 0; }
                if (args.Length >= 1 && args[0] == "--layout") { LayoutInfo.Print(args); return 0; }
                if (args.Length == 2 && args[0] == "--windows") { WindowShots.Save(args[1], tmp); return 0; }
                if (args.Length == 2 && args[0] == "--make-icon") { MakeIcon(args[1]); return 0; }

                if (args.Length >= 2 && args[0] == "--dashboard") { Tier7Tests.Pictures(args[1], args.Length > 2 && args[2] == "dark"); return 0; }
                if (args.Length == 1 && args[0] == "--tier7")
                {
                    Tier7Tests.Run(Path.Combine(tmp, "tier7"));
                    Tier7Regressions.Run(Path.Combine(tmp, "review7"));
                    Console.WriteLine(passed + " passed, " + failed + " failed");
                    return failed == 0 ? 0 : 1;
                }
                if (args.Length == 1 && args[0] == "--tier6")
                {
                    // just the newest checks, for quick runs while working on them
                    Tier6Tests.Run(Path.Combine(tmp, "tier6"));
                    Tier6Ai.Run(Path.Combine(tmp, "tier6ai"));
                    Tier6Regressions.Run(Path.Combine(tmp, "review6"));
                    Console.WriteLine(passed + " passed, " + failed + " failed");
                    return failed == 0 ? 0 : 1;
                }
                Console.WriteLine("Intent engine");
                IntentTests();
                Console.WriteLine("Dial geometry");
                GeometryTests();
                Console.WriteLine("File operations");
                FileTests(tmp);
                Console.WriteLine("Earlier features");
                FeatureTests.Run(Path.Combine(tmp, "features"));
                Console.WriteLine("Reliability and safety (tier 1)");
                Tier1Tests.Run(Path.Combine(tmp, "tier1"));
                Console.WriteLine("Smart features (tier 2)");
                Tier2Tests.Run(Path.Combine(tmp, "tier2"));
                Console.WriteLine("Converting in every direction");
                ConvertTests.Run(Path.Combine(tmp, "convert"));
                Console.WriteLine("Polish and power features (tiers 3 and 4)");
                Tier34Tests.Run(Path.Combine(tmp, "tier34"));
                Console.WriteLine("The toolbox (tier 5)");
                Tier5Tests.Run(Path.Combine(tmp, "tier5"));
                Console.WriteLine("Third review");
                Tier5Regressions.Run(Path.Combine(tmp, "review3"));
                Console.WriteLine("Beyond the dial (tier 6)");
                Tier6Tests.Run(Path.Combine(tmp, "tier6"));
                Tier6Ai.Run(Path.Combine(tmp, "tier6ai"));
                Console.WriteLine("Review of tier 6");
                Tier6Regressions.Run(Path.Combine(tmp, "review6"));
                Console.WriteLine("Dashboard and always running (tier 7)");
                Tier7Tests.Run(Path.Combine(tmp, "tier7"));
                Console.WriteLine("Review of tier 7");
                Tier7Regressions.Run(Path.Combine(tmp, "review7"));
            }
            catch (Exception ex) { Console.WriteLine("  CRASH " + ex); failed++; }
            finally
            {
                Log.Flush();
                try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>If no check completes for 90 seconds the run is stuck: print where the main thread is and give up.</summary>
        static void StartWatchdog(Thread main)
        {
            var t = new Thread(delegate ()
            {
                while (true)
                {
                    Thread.Sleep(5000);
                    if (Environment.TickCount - lastProgress < 90000) continue;
                    try
                    {
#pragma warning disable 618
                        main.Suspend();
                        var trace = new System.Diagnostics.StackTrace(main, true);
                        main.Resume();
#pragma warning restore 618
                        Console.WriteLine("  HUNG  no progress for 90 s; the main thread is at:" + trace);
                    }
                    catch (Exception ex) { Console.WriteLine("  HUNG  no progress for 90 s (" + ex.Message + ")"); }
                    Console.Out.Flush();
                    Environment.Exit(4);
                }
            }) { IsBackground = true, Name = "watchdog" };
            t.Start();
        }

        /// <summary>DragFlow's program icon (src\DragFlow.ico): the tray symbol, drawn large and saved at every size Windows uses.</summary>
        static void MakeIcon(string output)
        {
            using (var bmp = new Bitmap(256, 256))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(8, 8, 240, 240), Color.FromArgb(90, 150, 255), Color.FromArgb(40, 100, 220), 60f))
                        g.FillEllipse(fill, 8, 8, 240, 240);
                    using (var pen = new Pen(Color.White, 20)) g.DrawEllipse(pen, 58, 58, 140, 140);
                    using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, 104, 104, 48, 48);
                }
                if (File.Exists(output)) File.Delete(output);
                Converter.WriteIco(bmp, output);
            }
        }

        /// <summary>Everything the tests touch lives in a temporary folder: settings, undo journal, logs, usage, shelf.</summary>
        static void Sandbox(string tmp)
        {
            AppPaths.UseProfile(Path.Combine(tmp, "profile"));
            Trash = Path.Combine(tmp, "trash");
            Directory.CreateDirectory(Trash);
            UndoLog.Discard = delegate (string p)
            {
                FileOps.MoveEntry(p, Path.Combine(Trash, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(p.TrimEnd('\\'))));
                return true;
            };
            Settings.Current = new Settings();
            Usage.Current = new Usage();
            Learning.Current = new Learning();
            ShelfStore.Current = new ShelfStore();
            VirtualFiles.OverrideDir = Path.Combine(tmp, "incoming");
            Job.Confirm = delegate { return true; };
        }

        // ---------- intent engine ----------

        /// <summary>Drive the engine with a path: f(tMs) -> (x, y), sampled every 8 ms.</summary>
        static IntentEngine Run(Func<int, double[]> path, int durationMs, Action<IntentEngine, int> each = null)
        {
            var e = new IntentEngine();
            double[] p0 = path(0);
            e.Begin(p0[0], p0[1], 0);
            for (int t = 8; t <= durationMs; t += 8)
            {
                double[] p = path(t);
                e.AddSample(p[0], p[1], t);
                if (each != null) each(e, t);
            }
            return e;
        }

        static void IntentTests()
        {
            bool everRevealed = false;
            IntentEngine flick = Run(t => new[] { 1800.0 * t / 1000, 0.0 }, 500,
                (e, t) => { if (e.State == IntentState.Revealing) everRevealed = true; });
            Check(flick.State == IntentState.Dismissed && !everRevealed, "fast straight flick never shows the ring");

            IntentEngine normal = Run(t => new[] { 700.0 * Math.Min(t, 60) / 1000, 0.0 }, 200);
            Check(normal.State == IntentState.Revealing, "an ordinary drag start opens the dial");
            Check(normal.AnchorX == 0 && normal.AnchorY == 0, "the dial sits over the dragged item, not the cursor");

            IntentEngine moveOn = Run(t => new[] { t < 200 ? 0.0 : 600.0 * (t - 200) / 1000, 0.0 }, 600);
            Check(moveOn.State == IntentState.Dismissed, "moving on past the dial hides it");

            IntentEngine pause = Run(t => new[] { Math.Min(t, 150) * 0.1, 0.0 }, 600);
            Check(pause.State == IntentState.Revealing, "slow start then a pause shows the ring");

            IntentEngine hover = Run(t => new[] { 100 + 6 * Math.Sin(t / 40.0), 100 + 6 * Math.Cos(t / 40.0) }, 500);
            Check(hover.State == IntentState.Revealing, "hovering in place shows the ring");

            IntentEngine travel = Run(t => new[] { 900.0 * t / 1000, 40 * Math.Sin(t / 300.0) }, 600);
            Check(travel.State != IntentState.Revealing, "steady fast travel does not show the ring");

            // Reveal, then leave quickly in a direction with no tile.
            var e2 = new IntentEngine();
            e2.Begin(0, 0, 0);
            int t2 = 0;
            for (; t2 <= 500 && e2.State == IntentState.Observing; t2 += 8) e2.AddSample(0, 0, t2);
            Check(e2.State == IntentState.Revealing, "stationary cursor reveals after the observe window");
            e2.SetTiles(6);
            for (int i = 1; i <= 60 && e2.State == IntentState.Revealing; i++)
                e2.AddSample(i * 20.0, 0, t2 + i * 8); // 2500 px/s straight right, between tiles
            Check(e2.State == IntentState.Dismissed, "leaving the ring fast with no tile ahead dismisses it");

            // Heading at the top tile.
            var e3 = new IntentEngine();
            e3.Begin(0, 0, 0);
            int t3 = 0;
            for (; t3 <= 500 && e3.State == IntentState.Observing; t3 += 8) e3.AddSample(0, 0, t3);
            e3.SetTiles(6);
            for (int i = 1; i <= 4; i++) e3.AddSample(0, -i * 6.0, t3 + i * 8); // still short of the 61 px icon ring
            Check(e3.HeadingTile == 0, "moving up is recognised as heading for the top tile");
            Check(e3.State == IntentState.Revealing, "moving toward a tile keeps the ring open");

            var e4 = new IntentEngine();
            e4.Begin(0, 0, 0);
            e4.SetAnchor(0, 0);
            e4.SetTiles(4);
            e4.AddSample(0, -90, 20);
            Check(e4.FocusTile() == 0, "cursor above the anchor focuses the top tile");
            e4.AddSample(90, 0, 40);
            Check(e4.FocusTile() == 1, "cursor right of the anchor focuses the second tile");

            var e5 = new IntentEngine { OverTile = false };
            e5.Begin(0, 0, 0);
            e5.Dismiss();
            e5.AddSample(0, 0, 500);
            Check(e5.State == IntentState.Dismissed, "once dismissed the ring stays dismissed for the drag");

            var layoutX = 0.0; var layoutY = 0.0;
            RingLayout.Center(0, 0, 0, 6, 100, out layoutX, out layoutY);
            Check(Math.Abs(layoutX) < 1e-6 && Math.Abs(layoutY + 100) < 1e-6, "tile 0 sits straight above the anchor");
        }

        // ---------- file operations ----------

        static void MakePng(string path, int w, int h, Color c)
        {
            using (var bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.Clear(c);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void FileTests(string tmp)
        {
            string a = Path.Combine(tmp, "a.png"), b = Path.Combine(tmp, "b.png");
            MakePng(a, 200, 100, Color.CornflowerBlue);
            MakePng(b, 80, 120, Color.Tomato);

            string pdf = FileOps.ToPdf(new[] { a, b });
            string head = Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
            Check(head.StartsWith("%PDF-1.4") && head.Contains("/Count 2") && head.TrimEnd().EndsWith("%%EOF"), "two images become a two-page PDF");

            List<string> jpgs = FileOps.ConvertImages(new[] { a }, "jpg");
            using (var img = Image.FromFile(jpgs[0]))
                Check(img.RawFormat.Equals(ImageFormat.Jpeg) && img.Width == 200, "PNG converts to a real JPEG at the same size");

            bool threw = false;
            try { FileOps.ConvertImages(new[] { a }, "png"); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "converting to the format it already is reports nothing to do");

            List<string> small = FileOps.Shrink(new[] { a });
            Check(File.Exists(small[0]) && small[0].EndsWith("_small.jpg"), "shrink writes a _small.jpg copy");

            string zip = FileOps.Zip(new[] { a, b });
            using (ZipArchive za = ZipFile.OpenRead(zip))
                Check(za.Entries.Select(x => x.Name).OrderBy(x => x).SequenceEqual(new[] { "a.png", "b.png" }), "zip contains both files");

            Check(FileOps.Sha256(a).Length == 64, "SHA-256 is 64 hex characters");

            // rename + undo
            string stuff = Path.Combine(tmp, "stuff");
            Directory.CreateDirectory(stuff);
            foreach (string n in new[] { "x.txt", "y.txt", "z.txt" }) File.WriteAllText(Path.Combine(stuff, n), n);
            var files = Directory.GetFiles(stuff).OrderBy(x => x).ToList();
            var plan = FileOps.PlanRename(files, "doc_{n}");
            Check(Path.GetFileName(plan[0].Value) == "doc_01.txt" && Path.GetFileName(plan[2].Value) == "doc_03.txt", "batch rename numbers items and keeps extensions");
            FileOps.ApplyMoves(plan);
            Check(File.Exists(Path.Combine(stuff, "doc_02.txt")) && !File.Exists(Path.Combine(stuff, "y.txt")), "batch rename applied");
            Check(UndoLog.UndoLast() == 3 && File.Exists(Path.Combine(stuff, "y.txt")), "undo restores the original names");

            // sort + flatten
            File.Copy(a, Path.Combine(stuff, "pic.png"));
            Check(FileOps.SortByType(stuff) == 4 && File.Exists(Path.Combine(stuff, "Images", "pic.png")), "sort by type files images away");
            Check(UndoLog.UndoLast() == 4 && File.Exists(Path.Combine(stuff, "pic.png")), "undo restores sorted files");

            string deep = Path.Combine(tmp, "deep", "one", "two");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "f.txt"), "1");
            File.WriteAllText(Path.Combine(tmp, "deep", "f.txt"), "2");
            Check(FileOps.Flatten(Path.Combine(tmp, "deep")) == 1
                  && File.Exists(Path.Combine(tmp, "deep", "f (1).txt"))
                  && !Directory.Exists(Path.Combine(tmp, "deep", "one")), "flatten moves nested files up without overwriting");

            // single rename rejects bad names / collisions
            threw = false;
            try { FileOps.RenameOne(a, "bad|name.png"); } catch (ArgumentException) { threw = true; }
            Check(threw, "rename rejects characters Windows does not allow");
            threw = false;
            try { FileOps.RenameOne(a, "b.png"); } catch (IOException) { threw = true; }
            Check(threw, "rename refuses to overwrite an existing file");
        }

        // ---------- dial geometry ----------

        static void GeometryTests()
        {
            var geo = new RadialGeometry(6, 1.0);
            Check(geo.SegmentAt(0, -61) == 0, "pointing straight up selects the first slice");
            Check(geo.SegmentAt(53, -30) == 1, "pointing up-right selects the second slice");
            Check(geo.SegmentAt(-53, -30) == 5, "pointing up-left selects the last slice");
            Check(geo.SegmentAt(5, 5) == -1, "the see-through centre selects nothing");
            Check(geo.SegmentAt(0, -140) == -1, "outside the dial selects nothing");

            bool clean = true;
            for (int n = 3; n <= 6; n++)
            {
                var g = new RadialGeometry(n, 1.5);
                for (int seg = 0; seg < n; seg++)
                {
                    RectangleF[] chips = g.LayoutPills(seg, new float[] { 58, 92, 64, 70 });
                    for (int i = 0; i < chips.Length; i++)
                    {
                        RectangleF c = chips[i];
                        double nx = Math.Max(c.Left, Math.Min(0, c.Right)), ny = Math.Max(c.Top, Math.Min(0, c.Bottom));
                        if (Math.Sqrt(nx * nx + ny * ny) < g.Outer) clean = false;          // touches the dial
                        if (Math.Abs(c.X) + c.Width > g.HalfSize || Math.Abs(c.Y) + c.Height > g.HalfSize) clean = false; // clipped
                        for (int j = i + 1; j < chips.Length; j++) if (c.IntersectsWith(chips[j])) clean = false;
                    }
                }
            }
            Check(clean, "sub-option chips never overlap each other, the dial, or the window edge");
        }

        // ---------- visual check ----------

        static void RenderDemo(string output)
        {
            CustomActions.OverridePath = Path.Combine(Path.GetTempPath(), "dragflow-demo-no-actions.ini");
            var images = ActionCatalog.For(new[] { Path.Combine(Path.GetTempPath(), "demo.png") });
            var folder = ActionCatalog.For(new[] { Path.GetTempPath() });
            var archive = ActionCatalog.For(new[] { Path.Combine(Path.GetTempPath(), "demo.webp") });
            int conv = archive.FindIndex(i => i.Key == "Convert");
            int resize = images.FindIndex(i => i.Label == "Resize"), move = images.FindIndex(i => i.Label == "Move to");
            int organize = folder.FindIndex(i => i.Label == "Organize");
            const int W = 380, H = 420;
            using (var canvas = new Bitmap(W * 4, H))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                Panel(g, 0, W, H, Theme.LightTheme, images, resize, -1, resize, 2, "Image: Resize options");
                Panel(g, 1, W, H, Theme.LightTheme, folder, organize, -1, organize, 1, "Folder: Organize options");
                Panel(g, 2, W, H, Theme.DarkTheme, archive, conv, -1, conv, 1, "WebP picture: Convert options");
                Panel(g, 3, W, H, Theme.DarkTheme, images, move, -1, move, 0, "Image: Move to");
                canvas.Save(output, ImageFormat.Png);
            }
        }

        static void Panel(Graphics g, int col, int w, int h, Theme th, List<ActionItem> items, int hover, int heading, int kids, int chipHover, string caption)
        {
            Color bg = th.Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, col * w, 0, w, h);
            var geo = new RadialGeometry(items.Count, 1.0);
            var v = new RadialView
            {
                Geo = geo, Items = items, Theme = th, Open = 1,
                Hover = new float[items.Count], Heading = new float[items.Count],
                LabelIndex = hover, LabelWidths = RadialRenderer.MeasurePills(items, 1.0),
                Summary = col == 2 ? "5 items\n12 MB" : col == 3 ? "photo.jpg\n2.4 MB" : null
            };
            if (hover >= 0) v.Hover[hover] = 1;
            if (heading >= 0) v.Heading[heading] = 1;
            if (kids >= 0)
            {
                v.KidsParent = kids; v.KidsOpen = 1;
                v.Chips = geo.LayoutPills(kids, RadialRenderer.MeasurePills(items[kids].Children, 1.0));
                v.ChipHover = new float[v.Chips.Length];
                if (chipHover >= 0) v.ChipHover[chipHover] = 1;
            }
            using (Bitmap dial = RadialRenderer.Render(v))
                g.DrawImage(dial, col * w + w / 2 - geo.HalfSize, h / 2 - geo.HalfSize + 10);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font("Segoe UI", 11f, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(th.Subtle))
                g.DrawString(caption, f, tb, col * w + 14, 12);
        }
    }
}
