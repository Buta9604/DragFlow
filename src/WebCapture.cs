using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>
    /// Web pages (saved .html/.mhtml files and .url shortcuts) and text files to PDF or to one long picture, using the
    /// Microsoft Edge that comes with Windows, run invisibly with its own empty profile (your browsing data is not used).
    /// </summary>
    public static class WebCapture
    {
        static readonly string[] Exts = { ".html", ".htm", ".mhtml", ".mht", ".url", ".website", ".xhtml" };
        const int TimeoutMs = 120000;

        public static bool IsWebFile(string path) { return Exts.Contains(Path.GetExtension(path).ToLowerInvariant()); }

        static string edge;
        static bool looked;

        public static string FindEdge()
        {
            if (looked) return edge;
            looked = true;
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")
            };
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"))
                    if (k != null && k.GetValue("") is string) candidates.Insert(0, ((string)k.GetValue("")).Trim('"'));
            }
            catch (Exception) { }
            edge = candidates.FirstOrDefault(File.Exists);
            return edge;
        }

        /// <summary>The address a web file stands for. .url shortcuts must point at http or https.</summary>
        public static string UrlOf(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".url" || ext == ".website")
            {
                string line = File.ReadAllLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                string url = line == null ? "" : line.Substring(4).Trim();
                Uri u;
                if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("Only web addresses (http or https) can be saved.");
                return u.AbsoluteUri;
            }
            return new Uri(Path.GetFullPath(file)).AbsoluteUri;
        }

        /// <summary>Save the page (or text file) as a PDF next to it.</summary>
        public static string ToPdf(string file)
        {
            string dest = FileOps.UniquePath(FileOps.ParentOf(Path.GetFullPath(file)), Path.GetFileNameWithoutExtension(file), ".pdf");
            using (UndoLog.Begin("To PDF"))
            {
                Print(file, dest);
                UndoLog.Created(dest);
            }
            return dest;
        }

        /// <summary>Save the page (or text file) as one long picture (jpg or png): every printed page, stacked.</summary>
        public static string ToImage(string file, string format)
        {
            string temp = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N") + ".pdf");
            string dest = FileOps.UniquePath(FileOps.ParentOf(Path.GetFullPath(file)), Path.GetFileNameWithoutExtension(file), format == "png" ? ".png" : ".jpg");
            try
            {
                Print(file, temp);
                List<byte[]> pages = WinRt.PdfPagesAsPng(temp, 1.5, 40);
                if (pages.Count == 0) throw new InvalidOperationException("The page came out empty.");
                var images = pages.Select(b => Image.FromStream(new MemoryStream(b))).ToList();
                try
                {
                    int w = images.Max(i => i.Width), h = Math.Min(60000, images.Sum(i => i.Height));
                    using (var tall = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(tall))
                        {
                            g.Clear(Color.White);
                            int y = 0;
                            foreach (Image i in images) { if (y >= h) break; g.DrawImage(i, (w - i.Width) / 2, y, i.Width, i.Height); y += i.Height; }
                        }
                        using (UndoLog.Begin("To picture"))
                        {
                            if (format == "png") tall.Save(dest, ImageFormat.Png);
                            else File.WriteAllBytes(dest, FileOps.EncodeJpeg(tall, 90));
                            UndoLog.Created(dest);
                        }
                    }
                }
                finally { foreach (Image i in images) i.Dispose(); }
            }
            finally { try { File.Delete(temp); } catch (Exception) { } }
            return dest;
        }

        static bool CanOpen(string path)
        {
            try { using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) return true; }
            catch (IOException) { return false; }
        }

        /// <summary>Print a page or text file to PDF with Edge, invisibly.</summary>
        static void Print(string file, string pdf)
        {
            string exe = FindEdge();
            if (exe == null) throw new InvalidOperationException("Microsoft Edge was not found, and it is needed to turn pages into PDFs.");
            string url, tempHtml = null;
            if (IsWebFile(file)) url = UrlOf(file);
            else
            {
                if (new FileInfo(file).Length > 5 * 1024 * 1024) throw new InvalidOperationException("That text file is too big to print (over 5 MB).");
                tempHtml = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N") + ".html");
                string text = File.ReadAllText(file);
                string page = Markdown.IsMarkdown(file)
                    ? Markdown.ToHtml(text, Path.GetFileNameWithoutExtension(file))
                    : "<!doctype html><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(Path.GetFileName(file)) +
                      "</title><pre style=\"white-space:pre-wrap;word-wrap:break-word;font:10.5pt Consolas,monospace\">" + WebUtility.HtmlEncode(text) + "</pre>";
                File.WriteAllText(tempHtml, page, Encoding.UTF8);
                url = new Uri(tempHtml).AbsoluteUri;
            }
            // A private profile per print, so two prints at once never share (and lock) one.
            string profile = Path.Combine(AppPaths.LocalDir("edge-profile"), Guid.NewGuid().ToString("N"));
            string args = "--headless=new --disable-gpu --no-first-run --no-default-browser-check --disable-extensions --disable-sync " +
                          "--user-data-dir=\"" + profile + "\" --no-pdf-header-footer --virtual-time-budget=8000 " +
                          "--print-to-pdf=\"" + pdf + "\" \"" + url + "\"";
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += delegate { };
                    p.ErrorDataReceived += delegate { };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    long start = Clock.Now;
                    while (!p.WaitForExit(200))
                    {
                        Job j = Job.Current;
                        bool cancel = j != null && j.Cancelled;
                        if (cancel || Clock.Now - start > TimeoutMs)
                        {
                            try { p.Kill(); } catch (Exception) { }
                            if (cancel) throw new OperationCanceledException();
                            throw new InvalidOperationException("The page took too long to load.");
                        }
                    }
                }
                // Edge hands the work to helper processes, so the file can appear a moment after the first one exits.
                long waitFrom = Clock.Now, lastSize = -1;
                while (Clock.Now - waitFrom < 20000)
                {
                    Job.CheckCancel();
                    long size = File.Exists(pdf) ? new FileInfo(pdf).Length : -1;
                    if (size > 0 && size == lastSize && CanOpen(pdf)) break;
                    lastSize = size;
                    System.Threading.Thread.Sleep(250);
                }
                if (!File.Exists(pdf) || new FileInfo(pdf).Length == 0) throw new InvalidOperationException("Edge could not open that page.");
            }
            finally
            {
                if (tempHtml != null) try { File.Delete(tempHtml); } catch (Exception) { }
                try { Directory.Delete(profile, true); } catch (Exception) { }
            }
        }
    }
}
