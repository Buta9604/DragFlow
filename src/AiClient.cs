using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DragFlow
{
    /// <summary>
    /// The opt-in AI helper: sends files the user chose an AI action for to Anthropic's Claude (Messages API, raw HTTPS:
    /// the official C# SDK needs modern .NET, which this app does not use) and returns the text answer. The API key is
    /// stored encrypted for this Windows account only (DPAPI), never in the settings file or a backup.
    /// </summary>
    public static class AiClient
    {
        /// <summary>Tests point this at a local fake server.</summary>
        public static string Endpoint = "https://api.anthropic.com/v1/messages";
        public const string ApiVersion = "2026-10-21";
        public const string DefaultModel = "claude-opus-5-5";
        /// <summary>Model ids offered in Settings, best first, with plain-words labels.</summary>
        public static readonly string[][] Models =
        {
            new[] { "claude-opus-5-5", "Claude Opus 5.5 (best results)" },
            new[] { "claude-sonnet-5-5", "Claude Sonnet 5.5 (faster, cheaper)" },
            new[] { "claude-haiku-5-5", "Claude Haiku 5.5 (cheapest)" }
        };
        public const int MaxFiles = 20;
        // The API takes at most 32 MB per request, and base64 makes files a third bigger.
        const long MaxPdfBytes = 22L * 1024 * 1024, MaxTextBytes = 400 * 1024, MaxTotalBytes = 22L * 1024 * 1024;
        public static int TimeoutMs = 5 * 60 * 1000;

        // ---------- the key ----------

        static string KeyFile { get { return Path.Combine(AppPaths.Local, "ai-key.bin"); } }
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DragFlow AI key");

        public static bool HasKey { get { return File.Exists(KeyFile); } }

        /// <summary>True when AI actions should appear on the dial: switched on and a key is stored.</summary>
        public static bool Ready { get { return Settings.Current.AiEnabled && HasKey; } }

        public static void SaveKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) { ForgetKey(); return; }
            if (!key.StartsWith("sk-") || key.Length < 20 || key.Any(char.IsWhiteSpace)) throw new ArgumentException("That does not look like an API key (it starts with sk-ant-).");
            byte[] sealedKey = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(AppPaths.Local);
            File.WriteAllBytes(KeyFile, sealedKey);
        }

        public static void ForgetKey()
        {
            try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch (IOException) { }
        }

        static string LoadKey()
        {
            if (!HasKey) throw new InvalidOperationException("No API key is set. Add one in Settings › AI & more.");
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), Entropy, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { throw new InvalidOperationException("The stored API key cannot be read on this account. Enter it again in Settings › AI & more."); }
        }

        /// <summary>"sk-ant-…a1b2" for showing which key is stored.</summary>
        public static string KeyHint()
        {
            try { string k = LoadKey(); return k.Substring(0, Math.Min(7, k.Length)) + "…" + k.Substring(Math.Max(0, k.Length - 4)); }
            catch (Exception) { return null; }
        }

        // ---------- what can be sent ----------

        /// <summary>Why this file cannot go to the AI, or null when it can.</summary>
        public static string Unsupported(string file)
        {
            if (!File.Exists(file)) return "only files can be read, not folders";
            FileKind k = FileOps.Classify(file);
            long size = new FileInfo(file).Length;
            if (k == FileKind.Image) return null;
            if (k == FileKind.Pdf) return size > MaxPdfBytes ? "PDFs larger than 22 MB cannot be sent" : null;
            if (k == FileKind.Text || k == FileKind.Web) return size > MaxTextBytes ? "text files larger than 400 KB cannot be sent" : null;
            if (k == FileKind.Word) return FileOps.OfficeConverterFor(file) == null ? "Office or LibreOffice is needed to read this document" : null;
            return Path.GetExtension(file) + " files cannot be read by the AI";
        }

        /// <summary>The content blocks for one file (pictures are made smaller first; Office files become a PDF).</summary>
        static string Block(string file)
        {
            FileKind k = FileOps.Classify(file);
            if (k == FileKind.Image)
                return "{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/jpeg\",\"data\":\"" + Convert.ToBase64String(SmallJpeg(file)) + "\"}}";
            if (k == FileKind.Pdf) return PdfBlock(File.ReadAllBytes(file), Path.GetFileName(file));
            if (k == FileKind.Word)
            {
                string tempDir = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string copy = Path.Combine(tempDir, Path.GetFileName(file));
                    File.Copy(file, copy);
                    string pdf = FileOps.OfficeToPdf(copy);
                    return PdfBlock(File.ReadAllBytes(pdf), Path.GetFileName(file));
                }
                finally { try { Directory.Delete(tempDir, true); } catch (Exception) { } }
            }
            Encoding enc;
            string text = FileOps.ReadText(file, out enc);
            return "{\"type\":\"text\",\"text\":" + Json.Str("File \"" + Path.GetFileName(file) + "\":\n\n" + text) + "}";
        }

        static string PdfBlock(byte[] pdf, string name)
        {
            return "{\"type\":\"document\",\"title\":" + Json.Str(name) + ",\"source\":{\"type\":\"base64\",\"media_type\":\"application/pdf\",\"data\":\"" + Convert.ToBase64String(pdf) + "\"}}";
        }

        /// <summary>At most 1568 pixels on the longest side (larger only costs more), as JPEG.</summary>
        static byte[] SmallJpeg(string image)
        {
            using (Image src = FileOps.LoadImage(image))
            using (Bitmap small = FileOps.ToRgb(src, 1568))
            using (var ms = new MemoryStream())
            {
                ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                    small.Save(ms, jpeg, p);
                }
                return ms.ToArray();
            }
        }

        // ---------- asking ----------

        const string SystemPrompt =
            "You are the AI helper inside DragFlow, a Windows file tool. The user dragged files onto an action and is waiting " +
            "for one result. Answer only with what the instruction asks for: no greeting, no explanation of what you did, no " +
            "Markdown code fences unless asked. If the files do not let you do it, reply with one short sentence starting with \"Sorry:\".";

        /// <summary>Send the files and the instruction; the text answer. Honours Cancel on the current job.</summary>
        public static string Ask(IList<string> files, string instruction)
        {
            if (files.Count > MaxFiles) throw new InvalidOperationException("AI actions take at most " + MaxFiles + " files at a time.");
            AiUsage.CheckLimit(Settings.Current.AiMonthlyLimit, DateTime.Now);
            foreach (string f in files)
            {
                string why = Unsupported(f);
                if (why != null) throw new InvalidOperationException(Path.GetFileName(f) + ": " + why + ".");
            }
            // Measured before anything is encoded (pictures are made small, so they count little).
            long total = files.Sum(f => FileOps.Classify(f) == FileKind.Image ? 400 * 1024 : new FileInfo(f).Length);
            if (total > MaxTotalBytes) throw new InvalidOperationException("Together these files are too large to send at once (more than 22 MB). Try fewer.");
            Job.Step("Preparing " + FileOps.Plural(files.Count, "file", "files"));
            var blocks = files.Select(Block).ToList();
            blocks.Add("{\"type\":\"text\",\"text\":" + Json.Str(instruction) + "}");
            string model = string.IsNullOrEmpty(Settings.Current.AiModel) ? DefaultModel : Settings.Current.AiModel;
            bool fallbacks = !model.StartsWith("claude-haiku", StringComparison.Ordinal); // Haiku has no server-side fallback
            string body = "{\"model\":" + Json.Str(model) + ",\"max_tokens\":16000,\"output_config\":{\"effort\":\"low\"}," +
                          (fallbacks ? "\"fallbacks\":\"default\"," : "") +
                          "\"system\":" + Json.Str(SystemPrompt) + ",\"messages\":[{\"role\":\"user\",\"content\":[" + string.Join(",", blocks.ToArray()) + "]}]}";
            if (body.Length > 31 * 1024 * 1024) throw new InvalidOperationException("Together these files are too large to send at once. Try fewer.");
            Job.Step("Asking " + ModelName(model));
            string answer = Post(body, fallbacks);
            return ReadAnswer(answer);
        }

        public static string ModelName(string id)
        {
            string[] m = Models.FirstOrDefault(x => x[0] == id);
            return m == null ? id : m[1].Split('(')[0].Trim();
        }

        static string Post(string body, bool fallbacks)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = TimeoutMs; request.ReadWriteTimeout = TimeoutMs;
            request.Headers["x-api-key"] = LoadKey();
            request.Headers["anthropic-version"] = ApiVersion;
            if (fallbacks) request.Headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;

            string result = null;
            Exception failure = null;
            var done = new ManualResetEvent(false);
            var worker = new Thread(delegate ()
            {
                try
                {
                    using (Stream s = request.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var r = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) result = r.ReadToEnd();
                }
                catch (WebException ex) { failure = Explain(ex); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            }) { IsBackground = true, Name = "DragFlow AI request" };
            worker.Start();
            while (!done.WaitOne(250))
            {
                Job j = Job.Current;
                if (j != null && j.Cancelled) { try { request.Abort(); } catch (Exception) { } throw new OperationCanceledException(); }
            }
            if (failure != null) throw failure;
            return result;
        }

        /// <summary>An HTTP error in plain words, using the API's own message when it sent one.</summary>
        static Exception Explain(WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            if (resp == null) return new InvalidOperationException("Could not reach the AI service (" + ex.Status + "). Check the internet connection.");
            int code = (int)resp.StatusCode;
            string message = null;
            try
            {
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    message = Json.Get(Json.Parse(r.ReadToEnd()), "error", "message") as string;
            }
            catch (Exception) { }
            string plain = code == 401 ? "The API key was not accepted. Check it in Settings › AI & more."
                         : code == 403 ? "This API key may not use that model or feature."
                         : code == 429 ? "Too many AI requests just now, or the account is out of credit. Try again in a minute."
                         : code == 413 ? "The files are too large to send at once."
                         : code >= 500 ? "The AI service is busy or having trouble. Try again in a little while."
                         : "The AI service refused the request.";
            return new InvalidOperationException(plain + (message != null ? " (" + message + ")" : ""));
        }

        /// <summary>The text of an answer; refusals and errors become exceptions with a plain message.</summary>
        public static string ReadAnswer(string json)
        {
            object v = Json.Parse(json);
            // Count what was used, for the dashboard and the monthly limit. With a fallback the answer lists every attempt
            // (each billed for what it used, by its own model); otherwise the totals are at the top.
            string answerModel = Json.Get(v, "model") as string ?? Settings.Current.AiModel;
            var iterations = Json.Get(v, "usage", "iterations") as List<object>;
            if (iterations != null && iterations.Count > 0)
                foreach (object it in iterations) RecordUsage(it, Json.Get(it, "model") as string ?? answerModel);
            else RecordUsage(Json.Get(v, "usage"), answerModel);
            if (Json.Get(v, "type") as string == "error") throw new InvalidOperationException("The AI service reported a problem: " + (Json.Get(v, "error", "message") as string ?? "unknown"));
            string stop = Json.Get(v, "stop_reason") as string;
            if (stop == "refusal") throw new InvalidOperationException("The AI declined to do this for these files.");
            var content = Json.Get(v, "content") as List<object>;
            if (content == null) throw new InvalidOperationException("The AI service sent an answer DragFlow does not understand.");
            var sb = new StringBuilder();
            foreach (object block in content)
                if (Json.Get(block, "type") as string == "text") sb.Append(Json.Get(block, "text") as string);
            string text = sb.ToString().Trim();
            if (text.Length == 0) throw new InvalidOperationException("The AI sent back no text.");
            if (stop == "max_tokens") text += "\n\n(The answer was cut short because it was very long.)";
            return text;
        }

        static void RecordUsage(object usage, string model)
        {
            if (usage == null) return;
            double inTok = ((Json.Get(usage, "input_tokens") as double?) ?? 0) + ((Json.Get(usage, "cache_creation_input_tokens") as double?) ?? 0) +
                           ((Json.Get(usage, "cache_read_input_tokens") as double?) ?? 0);
            double outTok = (Json.Get(usage, "output_tokens") as double?) ?? 0;
            if (inTok + outTok > 0) AiUsage.Record(model, (long)inTok, (long)outTok, DateTime.Now);
        }

        /// <summary>Check the key and the connection with a tiny request; the answer time in plain words.</summary>
        public static string Test()
        {
            string model = string.IsNullOrEmpty(Settings.Current.AiModel) ? DefaultModel : Settings.Current.AiModel;
            bool fallbacks = !model.StartsWith("claude-haiku", StringComparison.Ordinal);
            string body = "{\"model\":" + Json.Str(model) + ",\"max_tokens\":1024,\"output_config\":{\"effort\":\"low\"}," + (fallbacks ? "\"fallbacks\":\"default\"," : "") +
                          "\"messages\":[{\"role\":\"user\",\"content\":\"Reply with the single word OK.\"}]}";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ReadAnswer(Post(body, fallbacks));
            return "Connected to " + ModelName(model) + " (answered in " + (watch.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s).";
        }

        /// <summary>A reply that only explains why it could not help ("Sorry: …").</summary>
        public static bool IsApology(string text) { return text.StartsWith("Sorry:", StringComparison.OrdinalIgnoreCase); }

        /// <summary>Markdown code fences around an answer removed (some answers come wrapped anyway).</summary>
        public static string Unfence(string text)
        {
            string t = text.Trim();
            if (!t.StartsWith("```")) return t;
            int nl = t.IndexOf('\n');
            if (nl < 0) return t.Trim('`');
            t = t.Substring(nl + 1);
            int end = t.LastIndexOf("```", StringComparison.Ordinal);
            return (end >= 0 ? t.Substring(0, end) : t).Trim();
        }
    }
}
