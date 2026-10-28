using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>Problems found in code review, kept fixed.</summary>
    static partial class Tier1Tests
    {
        static void ReviewRegressions(string dir)
        {
            UndoLog.Clear();
            // A failed undo must keep the backup so it can be tried again.
            string img = Make.Png(dir, "locked.png", 40, 20);
            FileOps.Rotate(new[] { img }, "right");
            string id = UndoLog.Latest.Id;
            UndoResult first;
            using (new FileStream(img, FileMode.Open, FileAccess.Read, FileShare.None)) first = UndoLog.Undo(id);
            UndoEntry left = UndoLog.History().FirstOrDefault(e => e.Id == id);
            Check(first.Problems.Count == 1 && left != null && left.Title.EndsWith("(unfinished)") && Directory.Exists(Path.Combine(UndoLog.Dir, id)),
                "review: an undo that fails (file in use) keeps its backup and stays in the list");
            UndoResult second = UndoLog.Undo(id);
            using (var im = System.Drawing.Image.FromFile(img))
                Check(second.Restored == 1 && im.Width == 40, "review: and works when tried again");

            // Housekeeping must not remove the backups of a job still running on another thread.
            var inside = new System.Threading.ManualResetEvent(false);
            var finish = new System.Threading.ManualResetEvent(false);
            string slow = Make.Png(dir, "slow.png");
            string slowId = null;
            var worker = new System.Threading.Thread(delegate ()
            {
                using (UndoLog.Begin("Slow rotate"))
                {
                    UndoLog.BackupBeforeEdit(slow);
                    slowId = UndoLog.CurrentId;
                    inside.Set();
                    finish.WaitOne();
                }
            });
            worker.Start();
            inside.WaitOne();
            for (int i = 0; i < 3; i++) using (UndoLog.Begin("Other " + i)) UndoLog.Created(Make.Text(dir, "o" + i + ".txt")); // each save sweeps old backups
            bool kept = Directory.Exists(Path.Combine(UndoLog.Dir, slowId));
            finish.Set();
            worker.Join();
            Check(kept && UndoLog.History().Any(e => e.Title == "Slow rotate"), "review: housekeeping leaves a running job's backups alone");
            UndoLog.Clear();

            // Swaps and chains of renames go through temporary names, and undo the same way.
            string x1 = Make.Text(dir, "x_01.txt", "one"), x2 = Make.Text(dir, "x_02.txt", "two");
            string x3 = Path.Combine(dir, "x_03.txt");
            int n = FileOps.ApplyMoves(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(x1, x2), new KeyValuePair<string, string>(x2, x3) });
            Check(n == 2 && File.ReadAllText(x2) == "one" && File.ReadAllText(x3) == "two" && !File.Exists(x1), "review: renaming 01→02 and 02→03 together works");
            UndoLog.UndoLast();
            Check(File.ReadAllText(x1) == "one" && File.ReadAllText(x2) == "two" && !File.Exists(x3), "review: and undoing it puts both back");
            n = FileOps.ApplyMoves(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(x1, x2), new KeyValuePair<string, string>(x2, x1) });
            Check(n == 2 && File.ReadAllText(x1) == "two" && File.ReadAllText(x2) == "one", "review: two names can be swapped");
            UndoLog.UndoLast();

            // A folder whose name only changes case.
            string folder = Make.Dir(dir, "casefolder");
            FileOps.RenameOne(folder, "CaseFolder");
            Check(Directory.GetDirectories(dir).Any(d => Path.GetFileName(d) == "CaseFolder"), "review: a folder can be renamed to different capitals");
            UndoLog.UndoLast();
            Check(Directory.GetDirectories(dir).Any(d => Path.GetFileName(d) == "casefolder"), "review: and back again with undo");

            // Extract here records only what came out of the archive, and never mixes into a folder already there.
            string arch = Make.Dir(dir, "arch");
            string inner = Make.Dir(arch, "pack");
            Make.Text(inner, "new.txt", "from zip");
            string zip = FileOps.Zip(new[] { inner });
            Directory.Delete(inner, true);
            string existing = Make.Dir(arch, "pack");
            Make.Text(existing, "mine.txt", "already here");
            UndoLog.Clear();
            FileOps.Extract(zip, false);
            Check(File.Exists(Path.Combine(arch, "pack (1)", "new.txt")) && Directory.GetFiles(existing).Length == 1, "review: extract here never mixes into an existing folder");
            UndoEntry ex = UndoLog.Latest;
            Check(ex.Steps.Count == 1 && ex.Steps[0].A == Path.Combine(arch, "pack (1)"), "review: only the extracted items are recorded for undo");
            Check(!Directory.GetDirectories(arch).Any(d => Path.GetFileName(d).StartsWith(".dragflow")), "review: no temporary folder is left behind");
            UndoLog.UndoLast();

            // Organising never follows a junction out of the folder.
            string outside = Make.Dir(dir, "outside");
            Make.Text(outside, "keep.txt", "do not move me");
            Make.Dir(outside, "emptyinside");
            string tidy = Make.Dir(dir, "tidy");
            Make.Text(Make.Dir(tidy, "sub"), "inner.txt");
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(tidy, "link") + "\" \"" + outside + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(psi)) p.WaitForExit();
            if (Directory.Exists(Path.Combine(tidy, "link")))
            {
                FileOps.Flatten(tidy);
                FileOps.RemoveEmptyFolders(tidy);
                Check(File.Exists(Path.Combine(outside, "keep.txt")) && Directory.Exists(Path.Combine(outside, "emptyinside")) && File.Exists(Path.Combine(tidy, "inner.txt")),
                    "review: Flatten and Remove empty stay out of folders linked from elsewhere");
                Directory.Delete(Path.Combine(tidy, "link")); // removes the link only
            }
            else Console.WriteLine("  SKIP  junction test (could not create a junction)");

            // Two drops of an attachment folder with the same name never write into each other.
            var d1 = new DataObject();
            byte[] desc = NestedDescriptor("Album\\a.txt");
            d1.SetData("FileGroupDescriptorW", new MemoryStream(desc));
            d1.SetData("FileContents", new MemoryStream(System.Text.Encoding.ASCII.GetBytes("first")));
            List<string> s1, s2;
            string[] f1 = VirtualFiles.FromDrop(d1, out s1);
            var d2 = new DataObject();
            d2.SetData("FileGroupDescriptorW", new MemoryStream(desc));
            d2.SetData("FileContents", new MemoryStream(System.Text.Encoding.ASCII.GetBytes("second")));
            string[] f2 = VirtualFiles.FromDrop(d2, out s2);
            Check(f1[0] != f2[0] && File.ReadAllText(Path.Combine(f1[0], "a.txt")) == "first" && File.ReadAllText(Path.Combine(f2[0], "a.txt")) == "second",
                "review: a second drop of the same attachment folder gets its own folder");

            // A job that fails outright reports why instead of crashing.
            string bad = Path.Combine(dir, "bad.png");
            File.WriteAllText(bad, "not an image");
            ActionResult r = new ActionRunner(new Control(), new FakeNotifier()).Execute("pdf", new[] { bad }, new DropInfo(), new Job("To PDF"));
            Check(!r.Cancelled && r.Failures.Count >= 1 && r.Failures[0].Contains("bad.png"), "review: a failing action reports the real reason");
        }

        static byte[] NestedDescriptor(string name)
        {
            var block = new byte[4 + 592];
            BitConverter.GetBytes(1).CopyTo(block, 0);
            BitConverter.GetBytes((uint)0x80).CopyTo(block, 4 + 36);
            System.Text.Encoding.Unicode.GetBytes(name).CopyTo(block, 4 + 72);
            return block;
        }
    }
}
