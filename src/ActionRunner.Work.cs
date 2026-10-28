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
    /// <summary>The work behind each action id, and recipes.</summary>
    public sealed partial class ActionRunner
    {
        [ThreadStatic] static List<string> manyOutputs;

        static int running;

        /// <summary>How many actions are working right now (an update waits for them).</summary>
        public static int Running { get { return System.Threading.Volatile.Read(ref running); } }

        void Work(string id, string[] paths, ActionResult r)
        {
            manyOutputs = null;
            List<string> outs = null;
            if (id.StartsWith("recipe:")) { RunRecipe(id.Substring(7), paths, r); return; }
            string[] folders = paths.Where(Directory.Exists).ToArray();
            string arg = id.Contains(":") ? id.Substring(id.IndexOf(':') + 1) : "";
            string output = null, message = null;

            if (id == "pdf")
            {
                output = FileOps.ToPdf(paths);
                if (paths.Any(p => FileOps.Classify(p) == FileKind.Pdf))
                    message = "\"" + FileOps.NameOf(output) + "\" created. PDF pages were added as pictures, so their text cannot be selected or searched.";
            }
            else if (id.StartsWith("convert:")) message = Many(FileOps.ConvertImages(paths, arg), "image converted", "images converted", out output);
            else if (id.StartsWith("convertx:"))
            {
                ConvertOptions o = ConvertOptions.Parse(arg);
                bool media = paths.Length > 0 && paths.All(p => { FileKind k = FileOps.Classify(p); return k == FileKind.Video || k == FileKind.Audio; });
                if (!media && Converter.Describe(o.Format) != null)
                {
                    message = Many(Converter.ConvertImages(paths, o), "image converted", "images converted", out output);
                    List<string> missed = Converter.LastMissedTarget;
                    if (missed != null && missed.Count > 0)
                        message += " · " + FileOps.Plural(missed.Count, "file", "files") + " could not get under " + o.TargetKB + " KB, so the smallest possible was kept";
                }
                else message = Many(Converter.ConvertMedia(paths, o), "file converted", "files converted", out output);
            }
            else if (id.StartsWith("media:"))
                message = Many(Converter.ConvertMedia(paths, new ConvertOptions { Format = arg }), "file converted", "files converted", out output);
            else if (id == "webpdf")
            {
                outs = new List<string>();
                Batch.Each(paths.ToList(), delegate (string p) { outs.Add(WebCapture.ToPdf(p)); });
                if (outs.Count == 0) throw Batch.Nothing("Nothing was saved.");
                output = outs[0];
                if (outs.Count > 1) message = outs.Count + " PDFs saved";
            }
            else if (id.StartsWith("webimg:"))
            {
                outs = new List<string>();
                Batch.Each(paths.ToList(), delegate (string p) { outs.Add(WebCapture.ToImage(p, arg)); });
                if (outs.Count == 0) throw Batch.Nothing("Nothing was saved.");
                output = outs[0];
                if (outs.Count > 1) message = outs.Count + " pictures saved";
            }
            else if (id == "shrink") message = Many(FileOps.Shrink(paths), "smaller copy saved", "smaller copies saved", out output);
            else if (id.StartsWith("resize:")) message = Many(FileOps.Resize(paths, arg), "resized copy saved", "resized copies saved", out output);
            else if (id == "strip") message = Many(FileOps.StripMetadata(paths), "clean copy saved", "clean copies saved", out output);
            else if (id.StartsWith("rotate:"))
            {
                message = FileOps.Plural(FileOps.Rotate(paths, arg), "image", "images") + (arg == "flip" ? " flipped" : " rotated");
                outs = FileOps.ImagesIn(paths);
            }
            else if (id == "ocr") message = ToClipboard(WinRt.OcrImages(FileOps.ImagesIn(paths)));
            else if (id == "ocr-pdf") message = ToClipboard(WinRt.OcrPdf(paths[0]));
            else if (id == "pdfimages" || id == "pdfimages:jpg")
            {
                output = WinRt.PdfToImages(paths[0], arg == "jpg" ? "jpg" : "png");
                message = "Pages saved to \"" + Path.GetFileName(output) + "\"";
            }
            else if (id == "wallpaper")
            {
                string previous = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")) previous = k == null ? null : k.GetValue("WallPaper") as string;
                if (!UndoLog.SetWallpaper(Path.GetFullPath(FileOps.ImagesIn(paths)[0])))
                    throw new InvalidOperationException("Windows refused to set that image as wallpaper.");
                UndoLog.WallpaperWas(previous);
                message = "Wallpaper set";
            }
            else if (id == "zip") output = FileOps.Zip(paths);
            else if (id == "extract" || id == "extractfolder")
            {
                Batch.Each(paths.ToList(), delegate (string p) { output = FileOps.Extract(p, id == "extractfolder"); });
                if (output == null) throw Batch.Nothing("Nothing was extracted.");
                message = "Extracted to \"" + Path.GetFileName(output.TrimEnd('\\')) + "\"";
            }
            else if (id == "sha256") message = Checksum(paths[0]);
            else if (id == "sort" || id == "sortdate" || id == "flatten")
            {
                if (!Confirm(folders, id == "flatten", "Organising")) { r.Cancelled = true; r.Message = "Left as it was."; return; }
                int n = folders.Sum(f => id == "sort" ? FileOps.SortByType(f) : id == "sortdate" ? FileOps.SortByDate(f) : FileOps.Flatten(f));
                message = FileOps.Plural(n, "file", "files") + (id == "sort" ? " sorted by type" : id == "sortdate" ? " sorted into month folders" : " moved up");
            }
            else if (id == "removeempty") message = FileOps.Plural(folders.Sum(f => FileOps.RemoveEmptyFolders(f)), "empty folder", "empty folders") + " removed";
            else if (id == "dupes")
            {
                if (!Confirm(folders, true, "Finding duplicates")) { r.Cancelled = true; r.Message = "Left as it was."; return; }
                long freed = 0;
                int n = 0, skipped = 0;
                foreach (string f in folders) { long b; int sk; n += FileOps.MoveDuplicates(f, out b, out sk); freed += b; skipped += sk; }
                message = n == 0 ? "No duplicates found"
                                 : FileOps.Plural(n, "duplicate", "duplicates") + " (" + FileOps.FormatSize(freed) + ") moved into a \"Duplicates\" folder";
                if (skipped > 0) message += " · " + FileOps.Plural(skipped, "online-only file was", "online-only files were") + " skipped so nothing had to download";
                if (n > 0) output = Path.Combine(folders[0], "Duplicates");
            }
            else if (id == "sizereport")
            {
                if (!Confirm(folders.Take(1).ToArray(), true, "Measuring")) { r.Cancelled = true; r.Message = "Left alone."; return; }
                string folder = folders[0];
                List<SizeEntry> entries = FileOps.SizeReport(folder);
                r.ShowAfter = delegate { new SizeReportForm(folder, entries).Show(); };
                message = "Size report ready";
            }
            else if (id.StartsWith("moveto:"))
            {
                int n = FileOps.MoveTo(paths, arg, out outs);
                output = arg;
                message = FileOps.Plural(n, "item", "items") + " moved to " + FileOps.NameOf(arg);
            }
            else if (id.StartsWith("copyto:"))
            {
                List<string> copies = FileOps.CopyTo(paths, arg);
                outs = copies;
                output = copies.Count == 1 ? copies[0] : arg;
                message = FileOps.Plural(copies.Count, "item", "items") + " copied to " + FileOps.NameOf(arg);
            }
            else if (id == "copyhere") message = Many(FileOps.CopyHere(paths), "copy made", "copies made", out output);
            else if (id == "shortcut") message = Many(FileOps.DesktopShortcuts(paths), "shortcut put on the desktop", "shortcuts put on the desktop", out output);
            else if (id.StartsWith("newfolder:")) { output = FileOps.NewFolderWith(paths, arg); message = FileOps.Plural(paths.Length, "item", "items") + " moved into \"" + FileOps.NameOf(output) + "\""; }
            else if (id.StartsWith("trim:"))
            {
                string[] se = arg.Split('|');
                output = FileOps.Trim(paths[0], se[0], se.Length > 1 ? se[1] : "");
            }
            else if (id.StartsWith("ff:"))
            {
                outs = new List<string>();
                Batch.Each(paths.ToList(), delegate (string p) { outs.Add(FileOps.Ffmpeg(p, arg)); });
                if (outs.Count == 0) throw Batch.Nothing("Nothing was converted.");
                output = outs[0];
                if (outs.Count > 1) message = outs.Count + " files done";
            }
            else if (id == "word2pdf")
            {
                outs = new List<string>();
                Batch.Each(paths.ToList(), delegate (string p) { outs.Add(FileOps.OfficeToPdf(p)); });
                if (outs.Count == 0) throw Batch.Nothing("Nothing was converted.");
                output = outs[0];
            }
            else if (id == "copytext")
            {
                string text = FileOps.ReadTextForClipboard(paths);
                OnUi(delegate { Clipboard.SetText(text.Length == 0 ? " " : text); });
                message = "Text copied (" + FileOps.Plural(text.Length, "character", "characters") + ")";
            }
            else if (id == "textstats") message = FileOps.TextStats(paths);
            else if (id.StartsWith("crop:")) message = Many(FileOps.Crop(paths, arg), "picture cropped", "pictures cropped", out output);
            else if (id.StartsWith("watermark:")) message = Many(FileOps.Watermark(paths, arg), "picture marked", "pictures marked", out output);
            else if (id == "gray") message = Many(FileOps.Grayscale(paths), "grayscale copy saved", "grayscale copies saved", out output);
            else if (id == "border") message = Many(FileOps.Border(paths), "copy with a border saved", "copies with a border saved", out output);
            else if (id == "collage") output = FileOps.Collage(paths);
            else if (id == "animgif") output = FileOps.AnimatedGif(paths);
            else if (id.StartsWith("vt:size:")) output = FileOps.CompressToSize(paths[0], double.Parse(arg.Substring(5), System.Globalization.CultureInfo.InvariantCulture));
            else if (id == "vt:rotate") { outs = new List<string>(); Batch.Each(paths.ToList(), delegate (string p) { outs.Add(FileOps.RotateVideo(p, "right")); }); output = outs.FirstOrDefault(); }
            else if (id.StartsWith("vt:speed:") || id.StartsWith("at:speed:")) output = FileOps.Speed(paths[0], double.Parse(arg.Substring(6), System.Globalization.CultureInfo.InvariantCulture));
            else if (id == "vt:merge" || id == "at:merge") output = FileOps.Merge(paths);
            else if (id == "vt:sheet") output = FileOps.ContactSheet(paths[0]);
            else if (id == "vt:subs") output = FileOps.BurnSubtitles(paths[0]);
            else if (id == "at:fade") { outs = new List<string>(); Batch.Each(paths.ToList(), delegate (string p) { outs.Add(FileOps.Fade(p)); }); output = outs.FirstOrDefault(); }
            else if (id == "at:silence") { outs = new List<string>(); Batch.Each(paths.ToList(), delegate (string p) { outs.Add(FileOps.TrimSilence(p)); }); output = outs.FirstOrDefault(); }
            else if (id == "tt:crlf" || id == "tt:lf") message = FileOps.Plural(FileOps.LineEndings(paths, id == "tt:crlf"), "file changed", "files changed");
            else if (id == "tt:utf8") message = FileOps.Plural(FileOps.ToUtf8(paths), "file converted to UTF-8", "files converted to UTF-8");
            else if (id == "tt:json") message = FileOps.Plural(FileOps.TidyJson(paths), "file tidied", "files tidied");
            else if (id == "tt:sort" || id == "tt:dedupe") message = FileOps.Plural(FileOps.SortLines(paths, id == "tt:dedupe"), "file changed", "files changed");
            else if (id == "tt:mdhtml") { outs = new List<string>(); Batch.Each(paths.ToList(), delegate (string p) { outs.Add(FileOps.MarkdownToHtml(p)); }); output = outs.FirstOrDefault(); }
            else if (id == "compare") message = FileOps.Compare(paths[0], paths[1]);
            else if (id == "listcontents") output = FileOps.ListContents(folders.Length > 0 ? folders[0] : FileOps.ParentOf(paths[0]));
            else if (id == "recycle") message = FileOps.RecycleAll(paths);
            else if (!WorkPower(id, arg, paths, r, out output, out message, out outs)) throw new InvalidOperationException("Unknown action " + id);

            r.Output = output;
            r.Message = message;
            r.Outputs = outs ?? manyOutputs ?? (output != null ? new List<string> { output } : null);
        }

        /// <summary>
        /// Run a recipe: each step works on what the previous one produced. Everything is one undo step; files made
        /// only along the way are put in the Recycle Bin at the end.
        /// </summary>
        [ThreadStatic] static HashSet<string> recipesRunning;

        void RunRecipe(string name, string[] paths, ActionResult r)
        {
            Recipe recipe = RecipeStore.Find(name);
            if (recipe == null) throw new InvalidOperationException("The recipe \"" + name + "\" no longer exists.");
            if (recipesRunning == null) recipesRunning = new HashSet<string>();
            // A recipe that (through others) contains itself would never end.
            if (!recipesRunning.Add(name)) throw new InvalidOperationException("The recipe \"" + name + "\" uses itself as a step; remove that step in Settings › Recipes.");
            try { RunRecipeSteps(recipe, paths, r); }
            finally { recipesRunning.Remove(name); }
        }

        void RunRecipeSteps(Recipe recipe, string[] paths, ActionResult r)
        {
            var originals = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            var made = new List<string>();
            string[] current = paths;
            for (int i = 0; i < recipe.Steps.Count; i++)
            {
                string step = recipe.Steps[i];
                Job.Step(RecipeSteps.Label(step) + " (" + (i + 1) + "/" + recipe.Steps.Count + ")");
                var sub = new ActionResult();
                Work(step, current, sub);
                if (sub.Outputs == null || sub.Outputs.Count == 0) throw new InvalidOperationException(RecipeSteps.Label(step) + ": nothing came out of this step.");
                made.AddRange(sub.Outputs.Where(o => !originals.Contains(o)));
                current = sub.Outputs.ToArray();
            }
            var final = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
            HashSet<string> created = UndoLog.CreatedSoFar(); // moved, renamed or rotated files are never tidied away
            foreach (string m in made.Distinct(StringComparer.OrdinalIgnoreCase))
                if (created.Contains(m) && !final.Contains(m) && !originals.Contains(m) && (File.Exists(m) || Directory.Exists(m)))
                {
                    // An in-between file inside a folder that is itself a result stays where it is.
                    if (final.Any(f => m.StartsWith(f.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                    try { UndoLog.Discard(m); } catch (Exception ex) { Log.Error("recipe tidy-up", ex); }
                }
            r.Outputs = current.ToList();
            r.Output = current[0];
            r.Message = recipe.Name + ": " + FileOps.Plural(current.Length, "file", "files") + " ready";
        }

        /// <summary>Ask before working through more than a couple of thousand files.</summary>
        static bool Confirm(string[] folders, bool recursive, string what)
        {
            int n = folders.Sum(f => FileOps.CountFiles(f, recursive, Job.LargeScan));
            return Job.ConfirmLarge(n, what);
        }

        static string Many(List<string> outputs, string one, string many, out string first)
        {
            manyOutputs = outputs;
            first = outputs[0];
            return outputs.Count + " " + (outputs.Count == 1 ? one : many);
        }

        string ToClipboard(string text)
        {
            if (text.Trim().Length == 0) return "No text found";
            OnUi(delegate { Clipboard.SetText(text); });
            return FileOps.Plural(Regex.Matches(text, @"\S+").Count, "word", "words") + " of text copied — paste with Ctrl+V";
        }
    }
}
