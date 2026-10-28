using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>Reliability and safety: the drag path, stuck drags, undo for everything, cancel, guards, settings.</summary>
    static partial class Tier1Tests
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Detector();
            Controller();
            Undo(Make.Dir(root, "undo"));
            JobsAndBatches(Make.Dir(root, "jobs"));
            Guards(Make.Dir(root, "guards"));
            Runner(Make.Dir(root, "runner"));
            SettingsAndLog(Make.Dir(root, "settings"));
            ReviewRegressions(Make.Dir(root, "regressions"));
        }

        // ---------- problems found in code review, kept fixed ----------

        // ---------- drag detector ----------

        static void Detector()
        {
            var d = new DragDetector
            {
                Probe = delegate { return new Surface { Class = "CabinetWClass", Source = SourceKind.Explorer }; },
                ThresholdX = 4, ThresholdY = 4
            };
            int started = 0, ended = 0;
            d.Started += delegate { started++; };
            d.Ended += delegate { ended++; };
            d.Down(100, 100, 0);
            d.Move(102, 102, 5);
            Check(started == 0, "detector: a wobble inside the drag threshold is not a drag");
            d.Move(110, 100, 10);
            Check(started == 1 && d.Dragging, "detector: moving past the threshold starts a drag");
            d.Down(300, 300, 20);
            Check(ended == 1, "detector: a new press ends a drag whose button-up was never seen");
            d.Up();
            Check(ended == 1, "detector: releasing without a drag ends nothing");

            d.Bypass = delegate { return true; };
            d.Down(0, 0, 30); d.Move(50, 0, 40);
            Check(started == 1, "detector: holding Alt skips the dial");
            d.Bypass = delegate { return false; };
            d.Probe = delegate { return null; };
            d.Down(0, 0, 50); d.Move(50, 0, 60);
            Check(started == 1, "detector: presses outside Explorer are ignored");
            d.Probe = delegate { return new Surface(); };
            d.Enabled = false;
            d.Down(0, 0, 70); d.Move(50, 0, 80);
            Check(started == 1, "detector: turned off means no drags");
        }

        // ---------- the drag path with a fake mouse and dial ----------

        sealed class Rig
        {
            public FakeOverlay Overlay = new FakeOverlay();
            public FakeDesktop Desk = new FakeDesktop { X = 500, Y = 500 };
            public DragController C;
            public Learning Learn = new Learning();
            public DragSession S;
            public long T;
            public int FileStarts, FileEnds;

            public Rig(int fileState, bool menu = true)
            {
                C = new DragController(Overlay, Desk);
                C.GetLearning = delegate { return Learn; };
                C.GetSettings = delegate { return new Settings(); };
                C.FallbackMenu = delegate { return new List<ActionItem> { new ActionItem("zip", "Fallback", "", System.Drawing.Color.Gray) }; };
                C.FileDragStarted += delegate { FileStarts++; };
                C.FileDragEnded += delegate { FileEnds++; };
                S = new DragSession { OriginX = 500, OriginY = 500, FileState = fileState, Summary = "1 file" };
                if (menu) S.Menu = ActionCatalog.For((string[])null);
                C.OnStarted(S, 0);
            }

            /// <summary>Advance time in 15 ms ticks, moving the pointer by (dx, dy) each tick.</summary>
            public void Run(int ms, int dx = 0, int dy = 0)
            {
                for (int i = 0; i < ms / 15; i++)
                {
                    T += 15;
                    Desk.X += dx; Desk.Y += dy;
                    C.Tick(T);
                }
            }

            public void Release()
            {
                Desk.Button = false;
                C.OnEnded(S, T);
                Run(1500);
            }
        }

        static void Controller()
        {
            var r = new Rig(1);
            r.Run(300, 1, 0);
            Check(r.Overlay.Visible && r.Overlay.Shows == 1, "drag path: an ordinary drag start opens the dial");
            Check(r.Overlay.Summary == "1 file", "drag path: the dial shows what is being dragged in its centre");
            Check(r.FileStarts == 1, "drag path: the shelf hears about the file drag once");
            r.Desk.Button = false; // the button-up never arrives
            r.Run(60);
            Check(!r.C.Active, "drag path: a missed button-up still ends the drag (no stuck dial)");
            r.Run(1500);
            Check(!r.Overlay.Visible, "drag path: and the dial closes");
            Check(r.FileEnds == 1, "drag path: the shelf hears the drag ended");
            Check(r.Learn.TotalDrags == 1 && r.C.LastOutcome != null && r.C.LastOutcome.Revealed, "drag path: every drag's outcome is recorded for learning");

            var flick = new Rig(1);
            flick.Run(400, 30, 0);
            Check(flick.Overlay.Shows == 0, "drag path: a quick flick never shows the dial");

            var notFile = new Rig(-1);
            notFile.Run(400);
            Check(notFile.Overlay.Shows == 0, "drag path: rubber-band selections and quiet times never show the dial");

            var slow = new Rig(0, false);
            slow.Run(1000);
            Check(slow.Overlay.Shows == 0, "drag path: the dial waits while the selection is looked up");
            slow.Run(700);
            Check(slow.Overlay.Shows == 1 && slow.Overlay.Items[0].Label == "Fallback", "drag path: a slow look-up falls back to a general menu");

            var esc = new Rig(1);
            esc.Run(300);
            esc.Desk.Keys.Add(Native.VK_ESCAPE);
            esc.Run(300);
            Check(!esc.Overlay.Visible, "drag path: Esc closes the dial");
            esc.Desk.Keys.Clear();
            esc.Release();
            Check(esc.C.LastOutcome.Escaped, "drag path: Esc is remembered as such");

            var away = new Rig(1);
            away.Run(300);
            away.Run(300, 20, 0);
            Check(!away.Overlay.Visible, "drag path: moving on past the dial hides it");

            var used = new Rig(1);
            used.Run(300);
            used.C.NoteDrop();
            used.Release();
            Check(used.C.LastOutcome.Used, "drag path: a drop on the dial counts as using it");

            var forever = new Rig(1);
            forever.Run(300);
            forever.Run(DragController.MaxDialMs + 600);
            Check(!forever.Overlay.Visible, "drag path: the dial never stays up longer than a minute");

            var cancel = new Rig(1);
            cancel.Run(300);
            cancel.C.Cancel("locked", cancel.T);
            Check(!cancel.Overlay.Visible && !cancel.C.Active, "drag path: locking the PC or changing displays closes the dial");

            var learned = new Rig(1);
            learned.Learn.SuppressedSectors = 1 << 4; // learned: brisk drags to the left are never for the dial
            learned.Learn.TotalDrags = 0;
            learned.C.OnStarted(learned.S, learned.T);
            learned.Run(200, -12, 0);
            Check(learned.Overlay.Shows == 0, "drag path: a learned direction keeps the dial hidden");
        }

        // ---------- undo for everything ----------

        static void Undo(string dir)
        {
            UndoLog.Clear();
            string a = Make.Png(dir, "a.png"), b = Make.Png(dir, "b.png");

            List<string> jpgs = FileOps.ConvertImages(new[] { a, b }, "jpg");
            Check(UndoLog.Latest.Title.StartsWith("Convert") && UndoLog.Latest.ItemCount == 2, "undo: converting is recorded as one step with both new files");
            Check(UndoLog.UndoLast() == 2 && !File.Exists(jpgs[0]) && !File.Exists(jpgs[1]) && File.Exists(a),
                "undo: undoing a conversion removes the new files (to the Recycle Bin) and keeps the originals");
            Check(Directory.GetFiles(TestMain.Trash).Length >= 2, "undo: removed files are put aside, never deleted for good");

            string zip = FileOps.Zip(new[] { a, b });
            List<string> copies = FileOps.CopyHere(new[] { a });
            List<UndoEntry> history = UndoLog.History();
            Check(history.Count == 2 && history[0].Title == "Copy here" && history[1].Title == "Zip", "undo: history keeps several steps, newest first");
            UndoLog.Undo(history[1].Id);
            Check(!File.Exists(zip) && File.Exists(copies[0]), "undo: an older step can be undone on its own");
            UndoLog.UndoLast();

            string arch = Make.Dir(dir, "arch");
            Make.Text(arch, "one.txt", "1");
            string z2 = FileOps.Zip(new[] { Path.Combine(arch, "one.txt") });
            File.Delete(Path.Combine(arch, "one.txt"));
            UndoLog.Clear();
            FileOps.Extract(z2, false);
            Check(File.Exists(Path.Combine(arch, "one.txt")), "undo: extract here puts the files next to the archive");
            UndoLog.UndoLast();
            Check(!File.Exists(Path.Combine(arch, "one.txt")) && File.Exists(z2), "undo: undoing extract here removes what was extracted");

            string tree = Make.Dir(dir, "tree");
            Make.Dir(Path.Combine(tree, "e1"), "e2");
            Check(FileOps.RemoveEmptyFolders(tree) == 2 && UndoLog.UndoLast() == 2 && Directory.Exists(Path.Combine(tree, "e1", "e2")),
                "undo: removed empty folders come back");

            string sorted = Make.Dir(dir, "sorted");
            Make.Png(sorted, "p.png");
            FileOps.SortByType(sorted);
            UndoLog.UndoLast();
            Check(File.Exists(Path.Combine(sorted, "p.png")) && !Directory.Exists(Path.Combine(sorted, "Images")), "undo: sorting is undone and the folders it made are tidied away");

            string rot = Make.Png(dir, "rot.png", 40, 20);
            FileOps.Rotate(new[] { rot }, "right");
            string backupDir = Path.Combine(UndoLog.Dir, UndoLog.Latest.Id);
            Check(Directory.Exists(backupDir), "undo: in-place edits keep their backup in the undo journal, not in %TEMP%");
            UndoLog.UndoLast();
            Check(!Directory.Exists(backupDir), "undo: the backup is cleaned up once used");

            UndoLog.Clear();
            for (int i = 0; i < 25; i++) using (UndoLog.Begin("Step " + i)) UndoLog.Created(Make.Text(dir, "n" + i + ".txt"));
            Check(UndoLog.History().Count == UndoLog.MaxEntries, "undo: only the last 20 steps are kept");
            Check(UndoLog.History()[0].Title == "Step 24", "undo: the history is read back from disk (survives a restart)");
            UndoLog.Clear();
            Check(!UndoLog.CanUndo, "undo: the history can be cleared");
        }

        // ---------- cancel and partial failure ----------

        static void JobsAndBatches(string dir)
        {
            UndoLog.Clear();
            var job = new Job("Test");
            var made = new List<string>();
            bool cancelled = false;
            using (job.Enter())
            {
                try
                {
                    using (UndoLog.Begin("Make files"))
                        Batch.Each(new[] { "1", "2", "3" }.ToList(), x => x, delegate (string x)
                        {
                            string f = Make.Text(dir, "made" + x + ".txt");
                            UndoLog.Created(f);
                            made.Add(f);
                            job.Cancelled = true; // the user presses Cancel during the first item
                        });
                }
                catch (OperationCanceledException) { cancelled = true; }
            }
            Check(cancelled && made.Count == 1, "jobs: Cancel stops a batch before the next item");
            Check(UndoLog.Latest != null && UndoLog.Latest.ItemCount == 1, "jobs: what was done before Cancel can still be undone");
            UndoLog.UndoLast();

            string good = Make.Png(dir, "good.png");
            string bad = Path.Combine(dir, "broken.png");
            File.WriteAllText(bad, "not a picture");
            var job2 = new Job("Convert");
            List<string> outs;
            using (job2.Enter()) outs = FileOps.ConvertImages(new[] { good, bad }, "jpg");
            Check(outs.Count == 1 && job2.Failures.Count == 1 && job2.Failures[0].StartsWith("broken.png"), "jobs: one bad file does not stop the rest; it is listed");

            bool threw = false;
            var job3 = new Job("Convert");
            using (job3.Enter())
            {
                try { FileOps.ConvertImages(new[] { bad }, "jpg"); }
                catch (InvalidOperationException ex) { threw = ex.Message.StartsWith("broken.png"); }
            }
            Check(threw, "jobs: when nothing worked, the real reason is reported");

            Check(Job.ConfirmLarge(10, "x"), "jobs: small folders are not questioned");
            Func<string, bool> old = Job.Confirm;
            string asked = null;
            Job.Confirm = delegate (string q) { asked = q; return false; };
            Check(!Job.ConfirmLarge(5000, "Finding duplicates") && asked != null && asked.Contains("Finding duplicates"), "jobs: huge folders ask before scanning");
            Job.Confirm = old;
        }

        // ---------- places DragFlow refuses to touch ----------

        static void Guards(string dir)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Check(Guard.IsProtected(Path.Combine(win, "notepad.exe")) && Guard.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
                "guard: Windows and Program Files are protected");
            Check(!Guard.IsProtected(dir), "guard: ordinary folders are not");
            bool threw = false;
            try { FileOps.RenameOne(Path.Combine(win, "win.ini"), "x.ini"); } catch (InvalidOperationException) { threw = true; }
            Check(threw && File.Exists(Path.Combine(win, "win.ini")), "guard: renaming inside Windows is refused");
            threw = false;
            try { FileOps.SortByType(Path.GetPathRoot(dir)); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "guard: sorting a whole drive is refused");
            threw = false;
            try { FileOps.Flatten(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "guard: flattening the user folder is refused");

            string src = Make.Dir(dir, "moving");
            Make.Text(Make.Dir(src, "sub"), "f.txt", "hello");
            string dest = Path.Combine(dir, "moved");
            FileOps.MoveFolderAcrossDrives(src, dest);
            Check(!Directory.Exists(src) && File.ReadAllText(Path.Combine(dest, "sub", "f.txt")) == "hello", "moving folders to another drive copies everything, then removes the original");
        }

        // ---------- the action runner, headless ----------

        static void Runner(string dir)
        {
            UndoLog.Clear();
            var notes = new FakeNotifier();
            var runner = new ActionRunner(new Control(), notes);
            string a = Make.Png(dir, "pic.png");
            ActionResult r = runner.Execute("convert:jpg", new[] { a }, new DropInfo(), new Job("Convert"));
            Check(r.Output != null && File.Exists(r.Output) && r.UndoId != null, "runner: an action reports its output and an undo id");
            UndoLog.Undo(r.UndoId);
            Check(!File.Exists(r.Output), "runner: the notification's Undo removes what the action made");

            string target = Make.Dir(dir, "target");
            string t = Make.Text(dir, "note.txt");
            r = runner.Execute("copyto:" + target, new[] { t }, new DropInfo { Copy = true }, new Job("Copy"));
            Check(File.Exists(t) && File.Exists(Path.Combine(target, "note.txt")), "runner: Ctrl + Move to copies instead");
            r = runner.Execute("moveto:" + target, new[] { t }, new DropInfo(), new Job("Move"));
            Check(!File.Exists(t) && File.Exists(Path.Combine(target, "note (1).txt")), "runner: Move to moves, never overwriting");
            UndoLog.Undo(r.UndoId);
            Check(File.Exists(t), "runner: and can be undone");

            string saved = Make.Text(Make.Dir(dir, "incoming"), "attachment.txt", "from mail");
            r = runner.Execute("copyhere", new[] { saved }, new DropInfo { Saved = new List<string> { saved } }, new Job("Copy here"));
            UndoLog.Undo(r.UndoId);
            Check(!File.Exists(saved), "runner: files saved from another app are removed again by undo");

            string f1 = Make.Text(dir, "x1.txt"), f2 = Make.Text(dir, "x2.txt");
            r = runner.Execute("newfolder:Batch", new[] { f1, f2 }, new DropInfo(), new Job("New folder"));
            Check(File.Exists(Path.Combine(dir, "Batch", "x1.txt")) && File.Exists(Path.Combine(dir, "Batch", "x2.txt")), "runner: New folder gathers the items into it");
            UndoLog.Undo(r.UndoId);
            Check(File.Exists(f1) && !Directory.Exists(Path.Combine(dir, "Batch")), "runner: undoing it puts them back and removes the folder");

            var cancelled = new Job("Zip") { Cancelled = true };
            r = runner.Execute("zip", new[] { f1, f2 }, new DropInfo(), cancelled);
            Check(r.Cancelled && !Directory.GetFiles(dir, "*.zip").Any(), "runner: a cancelled zip leaves no half-written file");
        }

        // ---------- settings, logging, quiet rules ----------

        static void SettingsAndLog(string dir)
        {
            var s = new Settings
            {
                Sensitivity = 1.4, ExcludedApps = new List<string> { "obs64" }, ExcludedFolders = new List<string> { dir },
                OtherApps = new List<string> { "outlook" }, ShelfEnabled = false, PinnedActions = new List<string> { "Zip", "Rename" },
                PausedUntil = DateTime.Now.AddMinutes(30), Ranking = false
            };
            s.Save();
            Settings back = Settings.Load();
            Check(back.Sensitivity == 1.4 && back.ExcludedApps[0] == "obs64" && back.ExcludedFolders[0] == dir && back.OtherApps[0] == "outlook"
                  && !back.ShelfEnabled && back.PinnedActions.SequenceEqual(new[] { "Zip", "Rename" }) && back.IsPaused && !back.Ranking,
                "settings: every option survives a restart");
            Check(Settings.NormalizeApp("\"OBS64.EXE\"") == "obs64", "settings: program names are tidied (OBS64.EXE → obs64)");

            Check(Quiet.ExcludedRunning(new[] { "OBS64.exe" }, new[] { "explorer", "obs64" }) == "obs64", "quiet: an excluded program that is running is noticed");
            Check(Quiet.ExcludedRunning(new[] { "obs64" }, new[] { "explorer" }) == null, "quiet: not running, not quiet");
            Check(Quiet.InFolder(Path.Combine(dir, "a", "b.png"), new[] { dir + "\\" }) && !Quiet.InFolder(dir + "x\\b.png", new[] { dir }),
                "quiet: excluded folders cover what is inside them, nothing else");
            var paused = new Settings { PausedUntil = DateTime.Now.AddHours(1) };
            Check(Quiet.Reason(new DragSession(), paused) != null, "quiet: pausing keeps the dial hidden");

            bool was = Log.IncludePaths;
            Log.IncludePaths = false;
            Check(Log.P(@"C:\secret\holiday.PNG") == "<.png>", "log: file names stay out of the log unless allowed");
            Log.IncludePaths = was;
            Log.Write("test line");
            Log.Flush();
            Check(File.Exists(Log.FilePath) && File.ReadAllText(Log.FilePath).Contains("test line"), "log: the rolling log is written");
        }
    }
}
