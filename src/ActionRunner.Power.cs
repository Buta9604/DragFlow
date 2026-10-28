using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Tier 6 actions: smart names, similar photos, protection, to phone, share link, scripts and the AI helper.</summary>
    public sealed partial class ActionRunner
    {
        /// <summary>Passwords handed from the dialog to the background job; never part of an action id, log or recipe.</summary>
        static readonly Dictionary<string, string> secrets = new Dictionary<string, string>();

        static string Keep(string secret)
        {
            string key = Guid.NewGuid().ToString("N");
            lock (secrets) secrets[key] = secret;
            return key;
        }

        static string Take(string key)
        {
            lock (secrets)
            {
                string s;
                if (!secrets.TryGetValue(key, out s)) throw new InvalidOperationException("The password is no longer available; try again.");
                secrets.Remove(key);
                return s;
            }
        }

        /// <summary>The ones that ask something first (on the UI thread), then carry on in the background.</summary>
        bool RunPowerOnUi(string id, string label, string[] paths, DropInfo info)
        {
            if (id == "encrypt")
            {
                string pw = PasswordDialog.Ask(L.T("Encrypt"), L.T("Password for the encrypted copy (at least 8 characters). The original is not changed."), true);
                if (pw != null) Background("encrypt:#" + Keep(pw), L.T("Encrypt"), paths, info);
                return true;
            }
            if (id == "decrypt")
            {
                string pw = PasswordDialog.Ask(L.T("Decrypt"), L.F("Password for {0}", Path.GetFileName(paths[0])), false);
                if (pw != null) Background("decrypt:#" + Keep(pw), L.T("Decrypt"), paths, info);
                return true;
            }
            if (id == "numberdate")
            {
                string name = PromptDialog.Ask(L.T("Number by date"), L.T("Name for the photos; they get 001, 002… in the order they were taken"), FileOps.NameOf(FileOps.ParentOf(paths[0])), -1, null);
                if (!string.IsNullOrWhiteSpace(name)) Background("numberdate:" + name.Trim(), L.T("Number by date"), paths, info);
                return true;
            }
            if (id.StartsWith("script:"))
            {
                ScriptAction a = Scripts.Find(id.Substring(7));
                if (a != null && !Scripts.Approved(a.Path))
                {
                    // what is shown is what gets approved: the bytes are read once
                    byte[] bytes = File.ReadAllBytes(a.Path);
                    Encoding enc;
                    string[] lines = Scripts.Decode(bytes, out enc).Replace("\r\n", "\n").Split('\n');
                    string head = string.Join(Environment.NewLine, lines.Take(40).ToArray()) +
                                  (lines.Length > 40 ? Environment.NewLine + L.F("… and {0} more lines (read them all in the dashboard's Scripts studio)", lines.Length - 40) : "");
                    string twoLines = Environment.NewLine + Environment.NewLine;
                    if (MessageBox.Show(L.F("\"{0}\" is a new or changed script. Run it?", a.Name) + twoLines + a.Path + twoLines + head,
                            "DragFlow", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return true;
                    Scripts.ApproveHash(Scripts.FingerprintOf(bytes));
                }
                return false; // runs in the background
            }
            if (id == "tophone")
            {
                PhoneWindow.Open(paths, delegate (Exception ex) { notifier.Show(L.T("Could not send to the phone"), ex.Message); });
                return true;
            }
            if (id == "ai:translate")
            {
                string lang = PromptDialog.Ask(L.T("Translate"), L.T("Into which language?"), System.Globalization.CultureInfo.CurrentUICulture.Parent.EnglishName, -1, null);
                if (!string.IsNullOrWhiteSpace(lang)) Background("ai:translate:" + lang.Trim().Replace(":", " "), L.T("AI translate"), paths, info);
                return true;
            }
            if (id == "ai:ask")
            {
                string q = PromptDialog.Ask(L.T("Ask AI"), L.F("Your question about {0}", paths.Length == 1 ? Path.GetFileName(paths[0]) : FileOps.Plural(paths.Length, "file", "files")), "", -1, null);
                if (!string.IsNullOrWhiteSpace(q)) Background("ai:ask:" + q.Trim(), L.T("Ask AI"), paths, info);
                return true;
            }
            return false;
        }

        /// <summary>The background work for the Tier 6 actions; false when the id is not one of them.</summary>
        bool WorkPower(string id, string arg, string[] paths, ActionResult r, out string output, out string message, out List<string> outs)
        {
            output = null; message = null; outs = null;
            if (id == "smartname")
            {
                int untouched;
                int n = FileOps.SmartRename(paths, out untouched);
                message = n == 0 ? "Nothing to name them by: no date taken, title or invoice details found"
                                 : FileOps.Plural(n, "file", "files") + " renamed by content" + (untouched > 0 ? " · " + untouched + " had nothing to go by" : "");
            }
            else if (id.StartsWith("numberdate:")) message = FileOps.Plural(FileOps.NumberByDate(paths, arg), "photo", "photos") + " numbered in the order they were taken";
            else if (id == "similar")
            {
                string folder;
                int n = FileOps.MoveSimilar(paths, out folder);
                message = n == 0 ? "No look-alike pictures found" : FileOps.Plural(n, "look-alike", "look-alikes") + " moved into \"Similar photos\"; the best copy of each stayed";
                if (n > 0) output = folder;
            }
            else if (id.StartsWith("encrypt:#"))
            {
                outs = Protect.EncryptAll(paths, Take(arg.Substring(1)));
                output = outs[0];
                message = FileOps.Plural(outs.Count, "encrypted copy", "encrypted copies") + " saved. The originals are unchanged: delete them yourself if only the encrypted copy should remain.";
            }
            else if (id.StartsWith("decrypt:#"))
            {
                string pw = Take(arg.Substring(1));
                outs = new List<string>();
                List<string> made = outs;
                Batch.Each(paths.Where(Protect.IsEncrypted).ToList(), delegate (string p) { made.Add(Protect.Decrypt(p, pw)); });
                if (outs.Count == 0) throw Batch.Nothing("Nothing was decrypted.");
                output = outs[0];
            }
            else if (id == "sharelink")
            {
                output = OneDriveShare.CopyIn(paths);
                string copy = output;
                message = "Copied into OneDrive. In the Share window choose \"Copy link\" (OneDrive may take a moment to upload it).";
                r.ShowAfter = delegate { OneDriveShare.OpenShare(copy); };
            }
            else if (id.StartsWith("script:")) message = Scripts.Run(arg, paths);
            else if (id.StartsWith("ai:")) message = Ai(id, arg, paths, r, out output, out outs);
            else return false;
            return true;
        }

        /// <summary>Checksum: compare with one copied from the download page (or a checksum file next to it); otherwise copy the SHA-256.</summary>
        string Checksum(string file)
        {
            string clip = OnUi(delegate { return Clipboard.ContainsText() ? Clipboard.GetText() : null; });
            string expected = Protect.ExpectedChecksum(file, clip);
            if (expected != null)
            {
                bool ok;
                string verdict = Protect.Verify(file, expected, out ok);
                if (!ok) throw new InvalidOperationException(verdict);
                return verdict;
            }
            string hash = Protect.Hash(file, "SHA-256");
            OnUi(delegate { Clipboard.SetText(hash); });
            return "SHA-256 copied: " + hash.Substring(0, 16) + "… (copy a published checksum first to check a download against it)";
        }

        // ---------- AI ----------

        static string AiOut(string file, string suffix, string ext)
        {
            return FileOps.UniquePath(FileOps.ParentOf(file), Path.GetFileNameWithoutExtension(file) + suffix, ext);
        }

        static void Save(string path, string text)
        {
            File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n", new UTF8Encoding(true));
            UndoLog.Created(path);
        }

        string Ai(string id, string arg, string[] paths, ActionResult r, out string output, out List<string> outs)
        {
            output = null; outs = null;
            if (!AiClient.Ready) throw new InvalidOperationException("The AI helper is off. Turn it on and add an API key in Settings › AI & more.");
            var files = paths.Where(File.Exists).ToList();
            if (files.Count == 0) throw new InvalidOperationException("AI actions work on files, not folders.");
            if (id == "ai:name")
            {
                var moves = new List<KeyValuePair<string, string>>();
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Batch.Each(files, delegate (string f)
                {
                    string answer = AiClient.Ask(new[] { f }, "Suggest a short, descriptive file name for this file based on its content (what it is, who or what it is about, and a date from the content first as YYYY-MM-DD if it has one). " +
                                                         "Reply with the name only: no extension, no quotes, at most 60 characters, words separated by spaces.");
                    if (AiClient.IsApology(answer)) throw new InvalidOperationException(answer.Substring(6).Trim());
                    string name = FileOps.SafeFileName(AiClient.Unfence(answer).Split('\n')[0]);
                    if (name.EndsWith(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - Path.GetExtension(f).Length);
                    if (name.Length == 0) return;
                    string dest = Path.Combine(FileOps.ParentOf(f), name + Path.GetExtension(f));
                    for (int i = 1; ((File.Exists(dest) || Directory.Exists(dest)) && !string.Equals(dest, f, StringComparison.OrdinalIgnoreCase)) || taken.Contains(dest); i++)
                        dest = Path.Combine(FileOps.ParentOf(f), name + " (" + i + ")" + Path.GetExtension(f));
                    taken.Add(dest);
                    moves.Add(new KeyValuePair<string, string>(f, dest));
                });
                if (moves.Count == 0) throw Batch.Nothing("No names came back.");
                int n = FileOps.ApplyMoves(moves);
                outs = moves.Select(m => m.Value).ToList();
                output = outs[0];
                return FileOps.Plural(n, "file", "files") + " named by AI";
            }
            if (id == "ai:summary")
            {
                outs = new List<string>();
                List<string> made = outs;
                Batch.Each(files, delegate (string f)
                {
                    string answer = AiClient.Ask(new[] { f }, "Summarise this file for someone deciding whether to read it: a one-line gist, then up to 8 short bullet points of the key facts, numbers, dates and decisions. Use Markdown. Write in the file's own language.");
                    string dest = AiOut(f, " - summary", ".md");
                    Save(dest, "# " + Path.GetFileName(f) + "\n\n" + AiClient.Unfence(answer));
                    made.Add(dest);
                });
                if (outs.Count == 0) throw Batch.Nothing("No summary came back.");
                output = outs[0];
                string first = File.ReadAllText(output);
                OnUi(delegate { Clipboard.SetText(first); });
                return FileOps.Plural(outs.Count, "summary", "summaries") + " saved next to the files (and the first one copied)";
            }
            if (id == "ai:describe")
            {
                var pictures = files.Where(f => FileOps.Classify(f) == FileKind.Image).ToList();
                if (pictures.Count == 0) throw new InvalidOperationException("Describe works on pictures.");
                string answer = AiClient.Ask(pictures, "Describe each picture in one or two sentences (what is shown, where, notable details and any text in it), then give 5 to 8 search keywords. " +
                                                       "Format: for each picture in order, a line \"## <its number>\", the description, and a line \"Keywords: a, b, c\".");
                var sb = new StringBuilder("# " + L.T("Picture descriptions") + "\n");
                string[] parts = AiClient.Unfence(answer).Split(new[] { "## " }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string body = parts[i].Trim();
                    int nl = body.IndexOf('\n');
                    // "## 2" then the text; an answer without those headers is kept whole
                    bool numbered = nl > 0 && body.Substring(0, nl).Trim().All(char.IsDigit);
                    string text = numbered ? body.Substring(nl + 1).Trim() : body;
                    sb.Append("\n## ").Append(i < pictures.Count ? Path.GetFileName(pictures[i]) : "?").Append("\n\n").Append(text).Append("\n");
                }
                output = FileOps.UniquePath(FileOps.ParentOf(pictures[0]), pictures.Count == 1 ? Path.GetFileNameWithoutExtension(pictures[0]) + " - description" : "Picture descriptions", ".md");
                Save(output, sb.ToString());
                return FileOps.Plural(pictures.Count, "picture", "pictures") + " described in \"" + Path.GetFileName(output) + "\"";
            }
            if (id.StartsWith("ai:translate:"))
            {
                string lang = arg.Substring("translate:".Length);
                outs = new List<string>();
                List<string> made = outs;
                Batch.Each(files, delegate (string f)
                {
                    string answer = AiClient.Ask(new[] { f }, "Translate the full text of this file into " + lang + ". Keep the structure (headings, lists, paragraphs, tables as Markdown). Reply with the translation only.");
                    if (AiClient.IsApology(answer)) throw new InvalidOperationException(answer.Substring(6).Trim());
                    bool plain = FileOps.Classify(f) == FileKind.Text;
                    string dest = AiOut(f, " (" + FileOps.SafeFileName(lang) + ")", plain ? Path.GetExtension(f) : ".md");
                    Save(dest, AiClient.Unfence(answer));
                    made.Add(dest);
                });
                if (outs.Count == 0) throw Batch.Nothing("No translation came back.");
                output = outs[0];
                return FileOps.Plural(outs.Count, "translation", "translations") + " into " + lang + " saved";
            }
            if (id == "ai:table")
            {
                outs = new List<string>();
                List<string> made = outs;
                Batch.Each(files, delegate (string f)
                {
                    string answer = AiClient.Ask(new[] { f }, "Find the main table in this file (or the list of items, amounts or rows it contains) and return it as CSV: a header row, comma-separated, values with commas or quotes in double quotes. Reply with the CSV only.");
                    if (AiClient.IsApology(answer)) throw new InvalidOperationException(answer.Substring(6).Trim());
                    string dest = AiOut(f, " - table", ".csv");
                    Save(dest, AiClient.Unfence(answer));
                    made.Add(dest);
                });
                if (outs.Count == 0) throw Batch.Nothing("No table came back.");
                output = outs[0];
                return FileOps.Plural(outs.Count, "table", "tables") + " saved as CSV (opens in Excel)";
            }
            if (id.StartsWith("ai:ask:"))
            {
                string question = arg.Substring("ask:".Length);
                string answer = AiClient.Unfence(AiClient.Ask(files, question));
                string title = L.T("Ask AI") + " – " + (files.Count == 1 ? Path.GetFileName(files[0]) : FileOps.Plural(files.Count, "file", "files"));
                r.ShowAfter = delegate { new AnswerWindow(title, question + "\r\n\r\n" + answer, null, null).Show(); };
                return L.T("Answer ready");
            }
            if (id == "ai:email")
            {
                string answer = AiClient.Unfence(AiClient.Ask(files, "Write a short, friendly e-mail that sends these files as attachments: a subject line starting with \"Subject:\", then the message, " +
                                                                    "which says in a sentence or two what each file is (from its content). Sign off without a name. Write in the files' language."));
                string[] attach = files.ToArray();
                r.ShowAfter = delegate
                {
                    try { Clipboard.SetText(answer); } catch (Exception ex) { Log.Error("clipboard", ex); }
                    new AnswerWindow(L.T("E-mail draft (copied)"), answer, L.T("New e-mail with the files"), delegate
                    {
                        // Mail programs keep this call open until the message window closes.
                        var t = new System.Threading.Thread(delegate () { try { OpenWith.Email(attach); } catch (Exception ex) { Log.Error("e-mail", ex); } }) { IsBackground = true };
                        t.SetApartmentState(System.Threading.ApartmentState.STA);
                        t.Start();
                    }).Show();
                };
                return L.T("E-mail draft ready and copied");
            }
            throw new InvalidOperationException("Unknown AI action " + id);
        }
    }

    /// <summary>"Share link": a copy inside OneDrive, then Windows' Share window for it, which offers "Copy link" for OneDrive files.</summary>
    public static class OneDriveShare
    {
        public static string Folder()
        {
            foreach (string v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string d = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
            }
            return null;
        }

        /// <summary>Copy the items into "OneDrive\DragFlow shared" (items already in OneDrive are shared as they are). Returns the first.</summary>
        public static string CopyIn(IList<string> paths)
        {
            string od = Folder();
            if (od == null) throw new InvalidOperationException("OneDrive is not set up on this PC. Sign in to OneDrive first, or use Share… or To phone instead.");
            string root = od.TrimEnd('\\') + "\\";
            if (paths.All(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase))) return paths[0];
            string target = Path.Combine(od, "DragFlow shared");
            if (!Directory.Exists(target)) UndoLog.EnsureFolder(target);
            List<string> copies = FileOps.CopyTo(paths.Where(p => !p.StartsWith(root, StringComparison.OrdinalIgnoreCase)).ToList(), target);
            return copies[0];
        }

        public static void OpenShare(string path)
        {
            Point p = Cursor.Position;
            try { ShareSheet.Show(new[] { path }, p.X, p.Y); }
            catch (Exception ex)
            {
                Log.Error("share link", ex);
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
        }
    }
}
