using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragFlow
{
    /// <summary>A PowerShell, Python or batch script from the scripts folder, shown on the dial under Custom.</summary>
    public sealed class ScriptAction
    {
        public string Path, Name, Types = "*";
        /// <summary>each = run once per item; all = once with every item.</summary>
        public bool Each;

        public bool Matches(string[] paths)
        {
            string[] tokens = Types.Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToArray();
            if (tokens.Length == 0 || tokens.Contains("*")) return true;
            if (paths == null || paths.Length == 0) return false;
            return paths.All(p =>
            {
                if (Directory.Exists(p)) return tokens.Contains("folder");
                string kind = FileOps.Classify(p).ToString().ToLowerInvariant();
                return tokens.Contains(kind) || tokens.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant());
            });
        }
    }

    /// <summary>
    /// Scripts as actions: drop a .ps1, .py, .bat or .cmd into the scripts folder and it appears on the dial. A comment
    /// line near the top can say how: "# DragFlow: name=Upload to server; types=image,pdf; each". The dragged items are
    /// passed as arguments (and in DRAGFLOW_FILES, one per line). Scripts are the user's own code: DragFlow runs them
    /// as they are and cannot undo what they do.
    /// </summary>
    public static class Scripts
    {
        static readonly string[] Exts = { ".ps1", ".py", ".bat", ".cmd" };
        public const int TimeoutMs = 10 * 60 * 1000;

        public static string Folder { get { return AppPaths.RoamingFile("scripts"); } }

        public static List<ScriptAction> Load()
        {
            var list = new List<ScriptAction>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (string f in Directory.GetFiles(Folder).Where(f => Exts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    ScriptAction a = Read(f);
                    if (a != null && list.All(x => !string.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase))) list.Add(a);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return list;
        }

        public static ScriptAction Read(string file)
        {
            var a = new ScriptAction { Path = file, Name = System.IO.Path.GetFileNameWithoutExtension(file) };
            try
            {
                foreach (string line in File.ReadLines(file).Take(25))
                {
                    Match m = Regex.Match(line, @"^\s*(?:#|::|rem\s|//|REM\s)\s*DragFlow\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    foreach (string part in m.Groups[1].Value.Split(';'))
                    {
                        string p = part.Trim();
                        int eq = p.IndexOf('=');
                        string key = (eq < 0 ? p : p.Substring(0, eq)).Trim().ToLowerInvariant(), val = eq < 0 ? "" : p.Substring(eq + 1).Trim();
                        if (key == "name" && val.Length > 0) a.Name = val.Length > 40 ? val.Substring(0, 40) : val;
                        else if (key == "types" && val.Length > 0) a.Types = val;
                        else if (key == "each") a.Each = true;
                        else if (key == "all") a.Each = false;
                    }
                    break;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            return a;
        }

        /// <summary>The program and arguments that run a script with these items.</summary>
        public static ProcessStartInfo Command(ScriptAction a, IList<string> items)
        {
            string files = string.Join(" ", items.Select(Quote).ToArray());
            string ext = System.IO.Path.GetExtension(a.Path).ToLowerInvariant();
            ProcessStartInfo psi;
            if (ext == ".ps1") psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(a.Path) + " " + files);
            else if (ext == ".py")
            {
                string py = FileOps.FindOnPath("py.exe");
                psi = py != null ? new ProcessStartInfo(py, "-3 " + Quote(a.Path) + " " + files)
                                 : new ProcessStartInfo(FileOps.FindOnPath("python.exe") ?? "python.exe", Quote(a.Path) + " " + files);
            }
            else psi = new ProcessStartInfo("cmd.exe", "/d /c \"" + Quote(a.Path) + " " + files + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = FileOps.ParentOf(items[0]);
            psi.EnvironmentVariables["DRAGFLOW_FILES"] = string.Join("\n", items.ToArray());
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            // Only approved code runs: Python must not import a module planted next to the script, and cmd.exe must not
            // run a program of the same name lying in the dragged files' folder.
            psi.EnvironmentVariables["PYTHONSAFEPATH"] = "1";
            psi.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"] = "1";
            return psi;
        }

        static string Quote(string s) { return "\"" + s.TrimEnd('\\') + "\""; }

        // ---------- approval ----------

        static string ApprovedFile { get { return System.IO.Path.Combine(AppPaths.Local, "scripts-approved.txt"); } }

        /// <summary>A fingerprint of the script's exact contents: any change needs a new approval.</summary>
        public static string Fingerprint(string file) { return FingerprintOf(File.ReadAllBytes(file)); }

        public static string FingerprintOf(byte[] contents)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(contents)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// The text of a script as it is stored: UTF-8 (with or without a byte order mark), UTF-16, or the PC's own code page
        /// for older files. The encoding is returned so the file can be saved back the same way.
        /// </summary>
        public static string Decode(byte[] b, out Encoding enc)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); return Encoding.UTF8.GetString(b, 3, b.Length - 3); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = Encoding.Unicode; return Encoding.Unicode.GetString(b, 2, b.Length - 2); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = Encoding.BigEndianUnicode; return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
            try { string s = new UTF8Encoding(false, true).GetString(b); enc = new UTF8Encoding(false); return s; }
            catch (DecoderFallbackException) { enc = Encoding.Default; return Encoding.Default.GetString(b); }
        }

        /// <summary>The bytes to store for a script's text in this encoding. Batch files never get a byte order mark (cmd.exe would read it as part of the first command).</summary>
        public static byte[] Encode(string text, Encoding enc, string file)
        {
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if ((ext == ".cmd" || ext == ".bat") && enc is UTF8Encoding) enc = new UTF8Encoding(false);
            byte[] body = enc.GetBytes(text);
            byte[] bom = enc.GetPreamble();
            return bom.Concat(body).ToArray();
        }

        /// <summary>
        /// Has the user approved this script as it is now? A script that appears in the folder (or changes) does not run
        /// until they have looked at it once, so something else putting a file there cannot make DragFlow run it.
        /// </summary>
        public static bool Approved(string file)
        {
            try { return File.Exists(ApprovedFile) && File.ReadAllLines(ApprovedFile).Contains(Fingerprint(file)); }
            catch (Exception) { return false; }
        }

        public static void Approve(string file) { ApproveHash(Fingerprint(file)); }

        /// <summary>Approve exactly the contents that were shown to the user (not whatever the file holds a moment later).</summary>
        public static void ApproveHash(string fp)
        {
            Directory.CreateDirectory(AppPaths.Local);
            var lines = File.Exists(ApprovedFile) ? File.ReadAllLines(ApprovedFile).ToList() : new List<string>();
            if (!lines.Contains(fp)) { lines.Add(fp); File.WriteAllLines(ApprovedFile, lines.Skip(Math.Max(0, lines.Count - 500)).ToArray()); }
        }

        public static ScriptAction Find(string name)
        {
            return Load().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Run a script on the items (Cancel stops it). Returns its last line of output, or a short default.</summary>
        public static string Run(string name, string[] paths)
        {
            ScriptAction a = Load().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (a == null) throw new InvalidOperationException("The script \"" + name + "\" is no longer in the scripts folder.");
            if (!Approved(a.Path)) throw new InvalidOperationException("The script \"" + a.Name + "\" is new or was changed: approve it in the dashboard under Scripts studio first.");
            var batches = a.Each ? paths.Select(p => (IList<string>)new[] { p }).ToList() : new List<IList<string>> { paths };
            string last = null;
            Job.SetTotal(batches.Count);
            foreach (IList<string> items in batches)
            {
                Job.Step(a.Name + (a.Each ? ": " + System.IO.Path.GetFileName(items[0]) : ""));
                last = RunOnce(a, items, null) ?? last;
                Job.Advance();
            }
            Log.Write("script \"" + a.Name + "\" ran " + batches.Count + " time(s)");
            return string.IsNullOrEmpty(last) ? a.Name + ": done" : last;
        }

        /// <summary>
        /// Run a script once on test files for the dashboard: everything it printed (output and errors) and its exit code,
        /// never an exception for a failing script. It must be approved, as on the dial.
        /// </summary>
        public static string Test(string scriptFile, IList<string> items)
        {
            ScriptAction a = Read(scriptFile);
            if (a == null) throw new InvalidOperationException("The script cannot be read.");
            if (!Approved(scriptFile)) throw new InvalidOperationException("Approve the script first.");
            var all = new StringBuilder();
            try { RunOnce(a, items, all); all.AppendLine("— finished without errors"); }
            catch (InvalidOperationException ex) { all.AppendLine("— " + ex.Message); }
            return all.ToString();
        }

        static string RunOnce(ScriptAction a, IList<string> items, StringBuilder all)
        {
            using (Process p = Process.Start(Command(a, items)))
            {
                var output = new StringBuilder();
                var errors = new StringBuilder();
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data == null) return; lock (output) output.AppendLine(e.Data); if (all != null) lock (all) all.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data == null) return; lock (errors) errors.AppendLine(e.Data); if (all != null) lock (all) all.AppendLine("! " + e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                long started = Clock.Now;
                while (!p.WaitForExit(200))
                {
                    Job j = Job.Current;
                    bool cancelled = j != null && j.Cancelled;
                    if (cancelled || Clock.Now - started > TimeoutMs)
                    {
                        try { p.Kill(); } catch (Exception) { }
                        p.WaitForExit(2000);
                        if (cancelled) throw new OperationCanceledException();
                        throw new InvalidOperationException(a.Name + " took longer than ten minutes and was stopped.");
                    }
                }
                // Not an endless wait: a program the script started may keep the output pipes open long after it ended.
                p.WaitForExit(2000);
                string err = errors.ToString().Trim();
                if (p.ExitCode != 0) throw new InvalidOperationException(a.Name + " failed (exit code " + p.ExitCode + ")" + (err.Length > 0 ? ": " + LastLine(err) : "."));
                return LastLine(output.ToString());
            }
        }

        static string LastLine(string text)
        {
            string line = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
            if (line == null) return null;
            return line.Length > 200 ? line.Substring(0, 200) + "…" : line;
        }

        /// <summary>Ready-made scripts for the dashboard's New menu: name, file name, text.</summary>
        public static readonly string[][] Templates =
        {
            new[] { "Put the date in front of the name", "Date in front.ps1",
@"# DragFlow: name=Date in front; types=*; each
# Renames each file to ""2026-10-27 name.ext"" using the date it was last changed.
$f = Get-Item -LiteralPath $args[0]
$new = (Get-Date $f.LastWriteTime -Format 'yyyy-MM-dd') + ' ' + $f.Name
Rename-Item -LiteralPath $f.FullName -NewName $new
""Renamed to $new""
" },
            new[] { "Upload with curl", "Upload.ps1",
@"# DragFlow: name=Upload; types=*; each
# Sends each file to a web address with curl (built into Windows). Change the address below first.
$url = 'https://example.com/upload'
curl.exe --fail --silent --show-error -F ""file=@$($args[0])"" $url
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'Uploaded ' + (Split-Path $args[0] -Leaf)
" },
            new[] { "Resize with ImageMagick", "Resize 800.ps1",
@"# DragFlow: name=Resize to 800 px; types=image; each
# Needs ImageMagick (magick.exe) on the PATH. Saves name_800.ext next to the picture.
$in = $args[0]
$out = [IO.Path]::Combine([IO.Path]::GetDirectoryName($in), [IO.Path]::GetFileNameWithoutExtension($in) + '_800' + [IO.Path]::GetExtension($in))
magick ""$in"" -resize 800x800 ""$out""
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'Saved ' + (Split-Path $out -Leaf)
" },
            new[] { "Count lines (Python)", "Count lines.py",
@"# DragFlow: name=Count lines; types=text; all
import sys
total = 0
for name in sys.argv[1:]:
    with open(name, encoding='utf-8', errors='replace') as f:
        total += sum(1 for _ in f)
print(f'{total} lines in {len(sys.argv) - 1} file(s)')
" },
            new[] { "Copy to a backup folder (batch)", "Backup copy.cmd",
@":: DragFlow: name=Backup copy; types=*; each
@echo off
rem Copies each item to D:\Backup (change it below). Folders are copied with everything inside.
set target=D:\Backup
if exist ""%~1\*"" (robocopy ""%~1"" ""%target%\%~nx1"" /E /NFL /NDL /NJH /NJS >nul) else (copy /y ""%~1"" ""%target%\"" >nul)
echo Copied %~nx1
" }
        };

        const string Example =
@"# DragFlow: name=Show file info; types=*; all
# An example script. Scripts in this folder appear on the dial under Custom for the file types they name:
#   name=  what the dial shows      types=  * or kinds (image, pdf, video, audio, text, folder) or extensions (.png,.csv)
#   each   run once per dragged item (otherwise once with all of them)
# The dragged items arrive as arguments (and in $env:DRAGFLOW_FILES, one per line).
# The last line the script prints is shown in DragFlow's notification.
$total = 0
foreach ($f in $args) { if (Test-Path -LiteralPath $f -PathType Leaf) { $total += (Get-Item -LiteralPath $f).Length } }
""{0} item(s), {1:N0} KB"" -f $args.Count, ($total / 1KB)
";

        /// <summary>Create the scripts folder with one example the first time it is opened.</summary>
        public static void EnsureFolder()
        {
            if (Directory.Exists(Folder)) return;
            Directory.CreateDirectory(Folder);
            string example = System.IO.Path.Combine(Folder, "Show file info.ps1");
            File.WriteAllText(example, Example, new UTF8Encoding(true));
            Approve(example); // DragFlow's own example
        }
    }
}
