using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>What running an action produced.</summary>
    public sealed class ActionResult
    {
        public string Message, Output, UndoId;
        /// <summary>Every file the action made (or where moved files ended up), for recipes.</summary>
        public List<string> Outputs;
        public bool Cancelled;
        public readonly List<string> Failures = new List<string>();
        /// <summary>Show this window when done (size report).</summary>
        public Action ShowAfter;
    }

    /// <summary>Runs an action id against the dropped files, with progress, Cancel, undo and a notification at the end.</summary>
    public sealed partial class ActionRunner
    {
        readonly Control ui;
        readonly INotifier notifier;
        /// <summary>Last file or folder created; "Show" on the notification reveals it in Explorer.</summary>
        public string LastOutput;
        /// <summary>Adds items to the shelf (set by the app).</summary>
        public Action<string[]> Park = delegate { };

        public ActionRunner(Control uiThreadControl, INotifier notifier)
        {
            ui = uiThreadControl; this.notifier = notifier;
        }

        /// <summary>Run an action from code (folder rules): in the background, with progress and a notification.</summary>
        public void RunAction(string id, string label, string[] paths)
        {
            Log.Write("action " + id.Split(':')[0] + " (" + label + ") on " + Log.P(paths));
            ui.BeginInvoke((Action)delegate { Background(id, label, paths, new DropInfo()); });
        }

        /// <summary>Run an action dropped on the dial. Never blocks the drop.</summary>
        public void Run(ActionItem item, string[] paths, DropInfo info, DragSession session)
        {
            string id = item.Id, label = item.Label;
            FileKind kind = paths.Length > 0 ? FileOps.KindOf(paths) : FileKind.Other;
            string folder = paths.Length > 0 ? FileOps.ParentOf(paths[0]) : null;
            if (session != null && session.Source == SourceKind.App && session.Kind != FileKind.Other) kind = session.Kind;
            try { Usage.Current.Record(kind, folder, id); } catch (Exception ex) { Log.Error("usage", ex); }
            Log.Write("action " + id.Split(':')[0] + " on " + Log.P(paths) + (info != null && info.Copy ? " (copy)" : ""));
            // Hop onto a fresh message so Explorer is released first.
            ui.BeginInvoke((Action)delegate { Dispatch(id, label, paths, info ?? new DropInfo()); });
        }

        void Dispatch(string id, string label, string[] paths, DropInfo info)
        {
            try { if (RunOnUiThread(id, label, paths, info)) return; }
            catch (Exception ex) { notifier.Show("Could not " + label.ToLowerInvariant(), ex.Message); return; }
            if (id.StartsWith("moveto:") && info.Copy) { id = "copyto:" + id.Substring(7); label = "Copy to " + FileOps.NameOf(id.Substring(7)); }
            Background(id, label, paths, info);
        }

        void Background(string id, string label, string[] paths, DropInfo info)
        {
            var job = new Job(label);
            var finished = new bool[1];
            ProgressWindow progress = null;
            // Only jobs that take more than a moment get a progress window.
            var showProgress = new System.Windows.Forms.Timer { Interval = 1500 };
            showProgress.Tick += delegate
            {
                showProgress.Dispose();
                if (!finished[0]) { progress = new ProgressWindow(job); progress.Show(); }
            };
            showProgress.Start();
            Task.Factory.StartNew(delegate
            {
                ActionResult r;
                try { r = Execute(id, paths, info, job); }
                catch (Exception ex)
                {
                    Log.Error("action " + id.Split(':')[0], ex);
                    r = new ActionResult();
                    r.Failures.Add(ex.Message);
                }
                if (!r.Cancelled && r.Failures.Count == 0) RecipeRecorder.Note(id, label, paths, r.Outputs);
                ui.BeginInvoke((Action)delegate
                {
                    finished[0] = true;
                    showProgress.Dispose();
                    if (progress != null) { progress.Finished = true; progress.Close(); }
                    Report(label, r);
                });
            });
        }

        void Report(string label, ActionResult r)
        {
            r.Failures.RemoveAll(string.IsNullOrEmpty);
            string summary = r.Cancelled ? L.T("Cancelled") : r.Message ?? (r.Output != null ? "\"" + FileOps.NameOf(r.Output) + "\" created" : r.Failures.Count > 0 ? r.Failures[0] : "Done");
            if (r.Failures.Count > 0 && r.Message != null) summary += " · " + FileOps.Plural(r.Failures.Count, "item", "items") + " failed";
            ActivityLog.Record(label, summary, r.UndoId, r.Output);
            if (r.ShowAfter != null) r.ShowAfter();
            var buttons = new List<NoteButton>();
            if (r.UndoId != null)
            {
                string undoId = r.UndoId;
                buttons.Add(new NoteButton("Undo", delegate { UndoNow(undoId); }));
            }
            if (r.Output != null && (File.Exists(r.Output) || Directory.Exists(r.Output)))
            {
                string output = r.Output;
                LastOutput = output;
                buttons.Add(new NoteButton("Show", delegate { Reveal(output); }));
            }
            if (r.Failures.Count > 0)
            {
                var failures = r.Failures.ToList();
                buttons.Add(new NoteButton("Details", delegate { ShowDetails(label, failures); }));
            }

            if (r.Cancelled) notifier.Show("Cancelled", (r.Message ?? label + " stopped.") + (r.UndoId != null ? " What was already done can be undone." : ""), buttons.ToArray());
            else if (r.Message == null && r.Output == null && r.Failures.Count > 0)
                notifier.Show("Could not " + label.ToLowerInvariant(), r.Failures.Count == 1 ? r.Failures[0] : r.Failures.Count + " items failed.", buttons.ToArray());
            else
            {
                string text = r.Message ?? (r.Output != null ? "\"" + FileOps.NameOf(r.Output) + "\" created" : "Done");
                if (r.Failures.Count > 0) text += " · " + FileOps.Plural(r.Failures.Count, "item", "items") + " failed";
                notifier.Show(r.Failures.Count > 0 ? "Partly done" : "Done", text, buttons.ToArray());
            }
        }

        public void UndoNow(string id)
        {
            Task.Factory.StartNew(delegate
            {
                UndoResult u = UndoLog.Undo(id);
                if (u.Title == null) notifier.Show("Undo", "That was already undone.");
                else if (u.Problems.Count == 0) notifier.Show("Undone", u.Title + ": " + FileOps.Plural(u.Restored, "item", "items") + " put back.");
                else
                {
                    var problems = u.Problems.ToList();
                    notifier.Show("Partly undone", u.Title + ": " + u.Restored + " put back, " + problems.Count + " could not be.",
                        new NoteButton("Details", delegate { ShowDetails("Undo", problems); }));
                }
            });
        }

        static void Reveal(string path)
        {
            Process.Start("explorer.exe", "/select,\"" + path.TrimEnd('\\') + "\"");
        }

        static void ShowDetails(string title, List<string> lines)
        {
            MessageBox.Show(string.Join("\r\n", lines.Take(30).ToArray()) + (lines.Count > 30 ? "\r\n… and " + (lines.Count - 30) + " more" : ""),
                "DragFlow – " + title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        T OnUi<T>(Func<T> f)
        {
            if (!ui.InvokeRequired) return f();
            return (T)ui.Invoke(f);
        }

        void OnUi(Action a)
        {
            if (!ui.InvokeRequired) a(); else ui.Invoke(a);
        }

        /// <summary>Actions that need dialogs, the clipboard or to launch programs. Some ask first, then continue in the background.</summary>
        bool RunOnUiThread(string id, string label, string[] paths, DropInfo info)
        {
            switch (id)
            {
                case "rename": Rename(paths); return true;
                case "rename-contents": RenameContents(paths); return true;
                case "copypath":
                    Clipboard.SetText(string.Join("\r\n", paths));
                    notifier.Show("Copied", paths.Length == 1 ? paths[0] : paths.Length + " paths copied");
                    return true;
                case "copyfile":
                    var list = new StringCollection();
                    list.AddRange(paths);
                    Clipboard.SetFileDropList(list);
                    notifier.Show("Copied " + FileOps.Plural(paths.Length, "item", "items"), "Paste with Ctrl+V into a chat, email or folder");
                    return true;
                case "copyimage":
                    using (Image img = FileOps.LoadForClipboard(paths)) Clipboard.SetImage(img);
                    notifier.Show("Image copied", "Paste with Ctrl+V anywhere");
                    return true;
                case "terminal": OpenTerminal(paths.First(Directory.Exists)); return true;
                case "open": OpenWith.Open(paths); return true;
                case "openwith": OpenWith.Choose(paths[0]); return true;
                case "print": OpenWith.Print(paths); return true;
                case "properties": OpenWith.Properties(paths[0]); return true;
                case "share":
                    {
                        Point p = Cursor.Position;
                        try { ShareSheet.Show(paths, p.X, p.Y); }
                        catch (Exception ex)
                        {
                            Log.Error("share", ex);
                            var files = new StringCollection();
                            files.AddRange(paths);
                            Clipboard.SetFileDropList(files);
                            notifier.Show("Copied instead", "Windows' Share window is not available here, so the files were copied: paste them into the app you want to share with.");
                        }
                        return true;
                    }
                case "email":
                    {
                        // Mail programs keep this call open until the message window closes: never on the UI thread.
                        var t = new Thread(delegate ()
                        {
                            try
                            {
                                if (!OpenWith.Email(paths)) notifier.Show("No e-mail program answered", "Set a default mail app in Windows Settings, or use Share… instead.");
                            }
                            catch (Exception ex) { notifier.Show("Could not start an e-mail", ex.Message); }
                        }) { IsBackground = true, Name = "DragFlow e-mail" };
                        t.SetApartmentState(ApartmentState.STA);
                        t.Start();
                        return true;
                    }
                case "park":
                    Park(paths);
                    notifier.Show("On the shelf", FileOps.Plural(paths.Length, "item", "items") + " parked at the edge of the screen. Drag them out whenever you need them.");
                    return true;
                case "movechoose":
                    {
                        string dest = PickFolder(info.Copy ? "Copy to which folder?" : "Move to which folder?");
                        if (dest == null) return true;
                        if (info.Copy) Background("copyto:" + dest, "Copy to " + FileOps.NameOf(dest), paths, info);
                        else Background("moveto:" + dest, "Move to " + FileOps.NameOf(dest), paths, info);
                        return true;
                    }
                case "newfolder":
                    {
                        string name = PromptDialog.Ask("New folder", "Name of the new folder for these items", FileOps.SuggestFolderName(paths), -1, null);
                        if (name == null) return true;
                        Background("newfolder:" + name, "New folder", paths, info);
                        return true;
                    }
                case "trim":
                    {
                        string range = PromptDialog.Ask("Trim " + Path.GetFileName(paths[0]), "Keep from – to (for example 0:05 - 1:30; leave the end out to keep the rest)", "0:00 - ", -1, null);
                        if (range == null) return true;
                        string[] parts = Regex.Split(range, @"\s*(?:-|–|to)\s*");
                        string start = parts.Length > 0 ? parts[0] : "0", end = parts.Length > 1 ? parts[1] : "";
                        FileOps.ParseTime(start);
                        if (end.Trim().Length > 0) FileOps.ParseTime(end);
                        Background("trim:" + start + "|" + end, "Trim", paths, info);
                        return true;
                    }
            }
            if (id == "watermark")
            {
                string text = PromptDialog.Ask(L.T("Watermark"), L.T("Text to put in the corner of each picture"), Environment.UserName, -1, null);
                if (!string.IsNullOrWhiteSpace(text)) Background("watermark:" + text.Trim(), L.T("Watermark"), paths, info);
                return true;
            }
            if (id == "vt:size")
            {
                string mb = PromptDialog.Ask(L.T("Fit in size"), L.T("Largest size in MB (for example 25 for most chat apps)"), "25", -1, null);
                double size;
                if (mb == null) return true;
                if (!double.TryParse(mb.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size) || size <= 0)
                    throw new ArgumentException("Type a size in MB, such as 25.");
                Background("vt:size:" + size.ToString(System.Globalization.CultureInfo.InvariantCulture), L.T("Fit in size"), paths, info);
                return true;
            }
            if (id == "vt:speed" || id == "at:speed")
            {
                string f = PromptDialog.Ask(L.T("Change speed"), L.T("Speed: 0.5 = half, 1.5 = faster, 2 = double"), "1.5", -1, null);
                double factor;
                if (f == null) return true;
                if (!double.TryParse(f.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out factor))
                    throw new ArgumentException("Type a number such as 1.5.");
                Background(id + ":" + factor.ToString(System.Globalization.CultureInfo.InvariantCulture), L.T("Change speed"), paths, info);
                return true;
            }
            if (id == "recycle")
            {
                if (MessageBox.Show(L.F("Move {0} to the Recycle Bin?", FileOps.Plural(paths.Length, "item", "items")) + "\r\n" + L.T("You can restore them from the Recycle Bin, not with DragFlow's Undo."),
                        "DragFlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return true;
                Background("recycle", L.T("Recycle Bin"), paths, info);
                return true;
            }
            if (id == "convertdlg")
            {
                string opts = ConvertDialog.Ask(paths);
                if (opts != null) Background("convertx:" + opts, L.T("Convert to") + " " + ConvertOptions.Parse(opts).Format.ToUpperInvariant(), paths, info);
                return true;
            }
            if (id == "copylink")
            {
                string links = string.Join("\r\n", paths.Select(WebCapture.UrlOf).ToArray());
                Clipboard.SetText(links);
                notifier.Show(L.T("Link copied"), links);
                return true;
            }
            if (id.StartsWith("app:")) { OpenWith.OpenIn(id.Substring(4), paths); return true; }
            if (id.StartsWith("custom:")) { CustomActions.Run(id.Substring(7), paths); return true; }
            return RunPowerOnUi(id, label, paths, info);
        }

        static string PickFolder(string title)
        {
            using (var d = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true })
                return d.ShowDialog() == DialogResult.OK ? d.SelectedPath : null;
        }

        /// <summary>
        /// Do the work (on the calling thread) inside one undo entry and the given job. Public so the tests can run
        /// actions headless; clipboard actions need an STA thread.
        /// </summary>
        public ActionResult Execute(string id, string[] paths, DropInfo info, Job job)
        {
            var r = new ActionResult();
            using (job.Enter())
            {
                string undoId = null;
                try
                {
                    System.Threading.Interlocked.Increment(ref running);
                    try
                    {
                        using (UndoLog.Begin(job.Title))
                        {
                            undoId = UndoLog.CurrentId;
                            if (info != null) foreach (string s in info.Saved) UndoLog.Created(s);
                            Work(id, paths, r);
                        }
                    }
                    finally { System.Threading.Interlocked.Decrement(ref running); }
                }
                catch (OperationCanceledException) { r.Cancelled = true; }
                catch (Exception ex)
                {
                    // Keep going so the notification still offers Undo for whatever was done before the error.
                    Log.Error("action " + id.Split(':')[0], ex);
                    r.Message = null; r.Output = null;
                    r.Failures.Add(ex.Message);
                }
                lock (job.Failures) r.Failures.AddRange(job.Failures.Where(f => !r.Failures.Contains(f)));
                if (undoId != null && File.Exists(Path.Combine(UndoLog.Dir, undoId + ".undo"))) r.UndoId = undoId;
            }
            if (r.Output != null) LastOutput = r.Output;
            return r;
        }

        static void OpenTerminal(string folder)
        {
            string wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
            if (File.Exists(wt)) Process.Start(new ProcessStartInfo(wt, "-d \"" + folder.TrimEnd('\\') + "\"") { UseShellExecute = false });
            else Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit") { WorkingDirectory = folder, UseShellExecute = true });
        }

        void Rename(string[] paths)
        {
            if (paths.Length == 1)
            {
                string cur = Path.GetFileName(paths[0].TrimEnd('\\'));
                int stem = Directory.Exists(paths[0]) ? -1 : Path.GetFileNameWithoutExtension(cur).Length;
                string name = PromptDialog.Ask("Rename", "New name", cur, stem, null);
                if (name == null) return;
                string undoId;
                using (UndoLog.Begin("Rename"))
                {
                    undoId = UndoLog.CurrentId;
                    FileOps.RenameOne(paths[0], name);
                }
                notifier.Show("Renamed", name, new NoteButton("Undo", delegate { UndoNow(undoId); }));
                return;
            }
            BatchRename(paths);
        }

        void RenameContents(string[] folders)
        {
            var files = new List<string>();
            foreach (string f in folders.Where(Directory.Exists))
            {
                var inside = Directory.GetFileSystemEntries(f).ToList();
                inside.Sort(StringComparer.OrdinalIgnoreCase);
                files.AddRange(inside);
            }
            if (files.Count == 0) { notifier.Show("DragFlow", "That folder is empty."); return; }
            BatchRename(files.ToArray());
        }

        void BatchRename(string[] paths)
        {
            List<KeyValuePair<string, string>> plan = RenameDialog.Ask(paths);
            if (plan == null) return;
            string undoId;
            int n;
            using (UndoLog.Begin("Rename " + paths.Length + " items"))
            {
                undoId = UndoLog.CurrentId;
                n = FileOps.ApplyMoves(plan);
            }
            notifier.Show("Renamed", FileOps.Plural(n, "item", "items") + (n < paths.Length ? " (" + (paths.Length - n) + " skipped: name taken)" : ""),
                new NoteButton("Undo", delegate { UndoNow(undoId); }));
        }
    }
}
