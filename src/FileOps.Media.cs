using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragFlow
{
    public static partial class FileOps
    {
        // ---------- ffmpeg ----------

        static string ffmpegPath;
        static long ffmpegCheckedAt = -1;

        /// <summary>ffmpeg next to DragFlow.exe or on PATH. The answer is cached for five minutes.</summary>
        public static string FindFfmpeg()
        {
            long now = Clock.Now;
            if (ffmpegCheckedAt >= 0 && now - ffmpegCheckedAt < 5 * 60 * 1000) return ffmpegPath;
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            ffmpegPath = File.Exists(local) ? local : FindOnPath("ffmpeg.exe");
            ffmpegCheckedAt = now;
            return ffmpegPath;
        }

        public static string FindOnPath(string exe)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                try
                {
                    string c = Path.Combine(dir.Trim().Trim('"'), exe);
                    if (File.Exists(c)) return c;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        /// <summary>Convert or process one media file with ffmpeg. mode: mp4, gif, mp3, wav, ogg, small, thumb, normalize.</summary>
        public static string Ffmpeg(string input, string mode)
        {
            string ext, args, pre = "", stem = Path.GetFileNameWithoutExtension(input);
            switch (mode)
            {
                case "mp4": ext = ".mp4"; args = "-c:v libx264 -preset medium -crf 23 -c:a aac"; break;
                case "gif": ext = ".gif"; args = "-vf \"fps=12,scale=480:-1:flags=lanczos\" -loop 0"; break;
                case "mp3": ext = ".mp3"; args = "-vn -codec:a libmp3lame -q:a 2"; break;
                case "wav": ext = ".wav"; args = "-vn"; break;
                case "ogg": ext = ".ogg"; args = "-vn -codec:a libvorbis -q:a 5"; break;
                case "small": ext = ".mp4"; stem += "_small"; args = "-c:v libx264 -preset fast -crf 28 -c:a aac -b:a 128k"; break;
                case "thumb": ext = ".jpg"; stem += "_thumb"; pre = "-ss 1 "; args = "-frames:v 1 -q:v 2"; break;
                case "normalize":
                    ext = Path.GetExtension(input).ToLowerInvariant();
                    if (ext == ".m4a" || ext == ".aac") args = "-af loudnorm -c:a aac -b:a 192k";
                    else if (ext == ".mp3") args = "-af loudnorm -codec:a libmp3lame -q:a 2";
                    else if (ext == ".ogg" || ext == ".opus") { ext = ".ogg"; args = "-af loudnorm -codec:a libvorbis -q:a 5"; }
                    else { ext = ".wav"; args = "-af loudnorm"; }
                    stem += "_normalized";
                    break;
                default: throw new ArgumentException("Unknown mode " + mode);
            }
            string exe = RequireFfmpeg();
            string dest = UniquePath(ParentOf(input), stem, ext);
            using (UndoLog.Begin("Convert"))
            {
                RunFfmpegTo(exe, pre + "-i \"" + input + "\" " + args, dest);
                if (mode == "thumb" && !File.Exists(dest)) // clips shorter than a second: take the very first frame
                    RunFfmpegTo(exe, "-i \"" + input + "\" " + args, dest);
                if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                UndoLog.Created(dest);
            }
            return dest;
        }

        /// <summary>Cut a clip between two times ("0:05", "1:02:03", "75" seconds); no re-encoding, so it is quick.</summary>
        public static string Trim(string input, string start, string end)
        {
            double s = ParseTime(start), e = end == null || end.Trim().Length == 0 ? -1 : ParseTime(end);
            if (e >= 0 && e <= s) throw new ArgumentException("The end time must be after the start time.");
            string exe = RequireFfmpeg();
            string dest = UniquePath(ParentOf(input), Path.GetFileNameWithoutExtension(input) + "_trimmed", Path.GetExtension(input));
            string range = "-ss " + s.ToString("0.###", CultureInfo.InvariantCulture) + (e >= 0 ? " -to " + e.ToString("0.###", CultureInfo.InvariantCulture) : "");
            using (UndoLog.Begin("Trim"))
            {
                RunFfmpegTo(exe, "-i \"" + input + "\" " + range + " -c copy -avoid_negative_ts make_zero", dest);
                if (!File.Exists(dest)) throw new InvalidOperationException("ffmpeg produced no output.");
                UndoLog.Created(dest);
            }
            return dest;
        }

        /// <summary>"90", "1:30", "0:01:30", "1:30.5" → seconds.</summary>
        public static double ParseTime(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0) return 0;
            double total = 0;
            foreach (string part in t.Split(':'))
            {
                double v;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0)
                    throw new ArgumentException("\"" + text + "\" is not a time. Use seconds or minutes:seconds, e.g. 1:30.");
                total = total * 60 + v;
            }
            return total;
        }

        static string RequireFfmpeg()
        {
            string exe = FindFfmpeg();
            if (exe == null) throw new InvalidOperationException("ffmpeg was not found. Put ffmpeg.exe next to DragFlow.exe or on your PATH.");
            return exe;
        }

        internal static void RunFfmpegTo(string exe, string args, string dest, string workDir = null)
        {
            try { RunTool(exe, "-y -hide_banner -loglevel error " + args + " \"" + dest + "\"", "ffmpeg failed", workDir); }
            catch (Exception)
            {
                TryRemovePartial(dest);
                throw;
            }
        }

        // ---------- Word ----------

        public static bool WordAvailable() { return Type.GetTypeFromProgID("Word.Application") != null; }

        static string libreOffice;
        static bool libreLooked;

        public static string FindLibreOffice()
        {
            if (libreLooked) return libreOffice;
            libreLooked = true;
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                string p = Path.Combine(root, @"LibreOffice\program\soffice.exe");
                if (File.Exists(p)) { libreOffice = p; break; }
            }
            return libreOffice;
        }

        /// <summary>Which program turns this document into a PDF: word, excel, powerpoint, libreoffice, or null.</summary>
        public static string OfficeConverterFor(string file)
        {
            string e = Path.GetExtension(file).ToLowerInvariant();
            string app = e == ".doc" || e == ".docx" || e == ".rtf" || e == ".odt" ? "Word"
                       : e == ".xls" || e == ".xlsx" || e == ".xlsm" || e == ".ods" ? "Excel"
                       : e == ".ppt" || e == ".pptx" || e == ".odp" ? "PowerPoint" : null;
            if (app == null) return null;
            if (Type.GetTypeFromProgID(app + ".Application") != null) return app.ToLowerInvariant();
            return FindLibreOffice() != null ? "libreoffice" : null;
        }

        public static string WordToPdf(string input) { return OfficeToPdf(input); }

        /// <summary>A Word, Excel or PowerPoint document as PDF, through the matching Office program or LibreOffice.</summary>
        public static string OfficeToPdf(string input)
        {
            string how = OfficeConverterFor(input);
            if (how == null) throw new InvalidOperationException("Neither Microsoft Office nor LibreOffice is installed for " + Path.GetExtension(input) + " files.");
            string dest = UniquePath(ParentOf(input), Path.GetFileNameWithoutExtension(input), ".pdf");
            if (how == "libreoffice")
            {
                string work = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    // Its own profile folder, or the call is handed to a LibreOffice the user has open and may produce nothing.
                    string profile = new Uri(Path.Combine(work, "profile")).AbsoluteUri;
                    RunTool(FindLibreOffice(), "-env:UserInstallation=" + profile + " --headless --norestore --convert-to pdf --outdir \"" + work + "\" \"" + input + "\"", "LibreOffice could not convert it");
                    string made = Directory.GetFiles(work, "*.pdf").FirstOrDefault();
                    if (made == null) throw new InvalidOperationException("LibreOffice produced no PDF.");
                    File.Move(made, dest);
                }
                finally { try { Directory.Delete(work, true); } catch (Exception) { } }
            }
            else WithOffice(how, input, dest);
            if (!File.Exists(dest)) throw new InvalidOperationException("No PDF was produced.");
            using (UndoLog.Begin("To PDF")) UndoLog.Created(dest);
            return dest;
        }

        const int OfficeTimeoutMs = 3 * 60 * 1000;

        /// <summary>
        /// Drive an invisible Word, Excel or PowerPoint. It must never ask anything (nobody could answer): alerts are off and a
        /// dummy password makes protected files fail instead of prompting. If it hangs anyway, or the user cancels, its process is ended.
        /// </summary>
        static void WithOffice(string how, string input, string dest)
        {
            string exe = how == "word" ? "WINWORD" : how == "excel" ? "EXCEL" : "POWERPNT";
            string progId = how == "word" ? "Word.Application" : how == "excel" ? "Excel.Application" : "PowerPoint.Application";
            int ours = 0;
            Exception failure = null;
            // Office wants to be driven from a single-threaded (STA) thread; the job's own thread is not one.
            var work = new System.Threading.Thread(delegate ()
            {
                var before = new HashSet<int>(System.Diagnostics.Process.GetProcessesByName(exe).Select(p => p.Id));
                dynamic app = null, list = null;
                string step = "starting";
                try
                {
                    app = Activator.CreateInstance(Type.GetTypeFromProgID(progId));
                    ours = System.Diagnostics.Process.GetProcessesByName(exe).Select(p => p.Id).FirstOrDefault(id => !before.Contains(id));
                    step = "opening the file";
                    if (how == "word")
                    {
                        app.Visible = false; app.DisplayAlerts = 0; // wdAlertsNone
                        list = app.Documents; // every object handed out must be released, or Office stays running
                        dynamic doc = list.Open(input, false, true, false, "\x01");
                        step = "saving the PDF";
                        try { doc.ExportAsFixedFormat(dest, 17); } finally { step = "closing the file"; doc.Close(false); Release((object)doc); }
                    }
                    else if (how == "excel")
                    {
                        app.Visible = false; app.DisplayAlerts = false;
                        list = app.Workbooks;
                        dynamic wb = list.Open(input, 0, true, Type.Missing, "\x01");
                        step = "saving the PDF";
                        try { wb.ExportAsFixedFormat(0, dest); } finally { step = "closing the file"; wb.Close(false); Release((object)wb); }
                    }
                    else
                    {
                        app.DisplayAlerts = 1; // ppAlertsNone
                        list = app.Presentations;
                        dynamic pres = list.Open(input, -1 /* read only */, 0, 0 /* no window */);
                        step = "saving the PDF";
                        try { pres.SaveAs(dest, 32 /* ppSaveAsPDF */); } finally { step = "closing the file"; pres.Close(); Release((object)pres); }
                    }
                }
                catch (Exception ex) { failure = new InvalidOperationException(Cap(how) + " failed while " + step + ": " + ex.Message, ex); }
                finally
                {
                    Release((object)list);
                    try { if (app != null) app.Quit(); } catch (Exception) { }
                    Release((object)app); app = null; list = null;
                    // Anything left is cleaned up here, on the thread that made it, before the thread ends.
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                }
            }) { IsBackground = true, Name = "office" };
            work.SetApartmentState(System.Threading.ApartmentState.STA);
            work.Start();
            long started = Clock.Now;
            while (!work.Join(500))
            {
                try { Job.CheckCancel(); }
                catch (OperationCanceledException) { EndProcess(ours); throw; }
                if (Clock.Now - started > OfficeTimeoutMs)
                {
                    EndProcess(ours);
                    throw new InvalidOperationException(how + " did not finish within three minutes (is the file damaged or asking for something?).");
                }
            }
            // The invisible Office this job started should now be closing; if it is still there after a while, end it.
            if (ours != 0)
                try
                {
                    using (var p = System.Diagnostics.Process.GetProcessById(ours))
                        if (!p.WaitForExit(8000)) { Log.Write(how + " did not close by itself; ending it"); EndProcess(ours); }
                }
                catch (ArgumentException) { } // already gone
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        /// <summary>
        /// Let go of a COM object now, so the Office program can exit instead of lingering until a garbage collection.
        /// Callers cast to object: a dynamic argument would make the runtime inspect an object Office may already have destroyed.
        /// </summary>
        static void Release(object com)
        {
            try { if (com != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(com); } catch (Exception) { }
        }

        static void EndProcess(int pid)
        {
            if (pid == 0) return;
            try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) { p.Kill(); p.WaitForExit(3000); } }
            catch (Exception ex) { Log.Error("ending a stuck Office process", ex); }
        }

        // ---------- text files ----------

        const long MaxClipboardText = 5 * 1024 * 1024;

        public static string ReadTextForClipboard(IList<string> paths)
        {
            var files = paths.Where(p => Classify(p) == FileKind.Text).ToList();
            if (files.Count == 0) throw new InvalidOperationException("No text files to copy.");
            if (files.Sum(f => new FileInfo(f).Length) > MaxClipboardText) throw new InvalidOperationException("That is more than 5 MB of text; open it instead.");
            if (files.Count == 1) return File.ReadAllText(files[0]);
            var sb = new StringBuilder();
            foreach (string f in files)
                sb.Append("===== ").Append(Path.GetFileName(f)).Append(" =====\r\n").Append(File.ReadAllText(f)).Append("\r\n\r\n");
            return sb.ToString();
        }

        public static string TextStats(IList<string> paths)
        {
            long lines = 0, words = 0, chars = 0;
            int files = 0;
            foreach (string f in paths.Where(p => Classify(p) == FileKind.Text))
            {
                string text = File.ReadAllText(f);
                files++;
                chars += text.Length;
                words += Regex.Matches(text, @"\S+").Count;
                lines += text.Length == 0 ? 0 : text.Count(c => c == '\n') + (text.EndsWith("\n") ? 0 : 1);
            }
            if (files == 0) throw new InvalidOperationException("No text files to count.");
            return Plural((int)lines, "line", "lines") + " · " + Plural((int)words, "word", "words") + " · " + Plural((int)chars, "character", "characters")
                 + (files > 1 ? " in " + files + " files" : "");
        }
    }
}
