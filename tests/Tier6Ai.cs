using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DragFlow.Tests
{
    /// <summary>A pretend Anthropic API on this PC: records each request and answers with what the test says.</summary>
    sealed class FakeApi : IDisposable
    {
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        public readonly List<string> Requests = new List<string>();
        public int Status = 200;
        public string Reply = "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\"}";
        public string Url { get { return "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1/messages"; } }

        public FakeApi()
        {
            listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        void Serve()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch (Exception) { return; }
                using (c)
                {
                    try
                    {
                        NetworkStream s = c.GetStream();
                        var head = new StringBuilder();
                        while (!head.ToString().EndsWith("\r\n\r\n")) { int b = s.ReadByte(); if (b < 0) break; head.Append((char)b); }
                        int len = 0;
                        foreach (string line in head.ToString().Split('\n'))
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(line.Substring(15).Trim());
                        var body = new byte[len];
                        int got = 0;
                        while (got < len) { int n = s.Read(body, got, len - got); if (n <= 0) break; got += n; }
                        lock (Requests) Requests.Add(head + Encoding.UTF8.GetString(body));
                        byte[] reply = Encoding.UTF8.GetBytes(Reply);
                        byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 " + Status + " X\r\nContent-Type: application/json\r\nContent-Length: " + reply.Length + "\r\nConnection: close\r\n\r\n");
                        s.Write(h, 0, h.Length); s.Write(reply, 0, reply.Length);
                    }
                    catch (Exception) { }
                }
            }
        }

        public string Last { get { lock (Requests) return Requests.Count == 0 ? null : Requests[Requests.Count - 1]; } }
        public object LastBody { get { string r = Last; return Json.Parse(r.Substring(r.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)); } }
        public static string Text(string text) { return "{\"content\":[{\"type\":\"text\",\"text\":" + Json.Str(text) + "}],\"stop_reason\":\"end_turn\"}"; }
        public void Dispose() { listener.Stop(); }
    }

    /// <summary>Tier 6: the AI helper (against a pretend API), gestures, the preview in the dial, pen and touch, the new dial entries.</summary>
    static class Tier6Ai
    {
        static void Check(bool ok, string name) { TestMain.Check(ok, name); }

        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            JsonParts();
            Ai(Make.Dir(root, "ai"));
            Shapes();
            DialPreviewAndTouch(Make.Dir(root, "dial"));
            Menus(Make.Dir(root, "menus"));
        }

        static void JsonParts()
        {
            object v = Json.Parse("{\"a\":[1,2.5,{\"b\":\"x\\\"y\\u00e9\"}],\"c\":true,\"d\":null}");
            Check(Json.Get(v, "a", 2, "b") as string == "x\"yé" && (double)Json.Get(v, "a", 1) == 2.5 && (bool)Json.Get(v, "c") && Json.Get(v, "d") == null && Json.Get(v, "zz") == null,
                "json: answers are read (strings, numbers, lists, escapes)");
            Check(Json.Str("a\"b\\c\nd\u0001") == "\"a\\\"b\\\\c\\nd\\u0001\"", "json: text is written safely");
            bool deep = false;
            try { Json.Parse(new string('[', 1000) + new string(']', 1000)); } catch (FormatException) { deep = true; }
            Check(deep, "json: a hostile, deeply nested answer is refused, not a crash");
        }

        static void Ai(string dir)
        {
            Settings.Current = new Settings { AiEnabled = true };
            Check(!AiClient.HasKey && !AiClient.Ready, "AI: off until a key is stored");
            bool refused = false;
            try { AiClient.SaveKey("hello"); } catch (ArgumentException) { refused = true; }
            const string key = "sk-ant-test-0123456789abcdefXYZ";
            AiClient.SaveKey(key);
            byte[] stored = File.ReadAllBytes(Path.Combine(AppPaths.Local, "ai-key.bin"));
            Check(refused && AiClient.Ready && !Encoding.UTF8.GetString(stored).Contains("0123456789") && !Encoding.Unicode.GetString(stored).Contains("0123456789") && AiClient.KeyHint() == "sk-ant-…fXYZ",
                "AI: the key is checked, stored encrypted, and shown only as a hint");
            Settings.Current.Save();
            Check(!File.ReadAllText(Settings.FilePath).Contains("sk-ant"), "AI: the settings file never holds the key");

            string old = AiClient.Endpoint;
            using (var api = new FakeApi())
            {
                AiClient.Endpoint = api.Url;
                try
                {
                    string photo = Make.Png(dir, "IMG_2231.png", 2400, 1600);
                    api.Reply = FakeApi.Text("2026-03-01 Beach at sunset");
                    string answer = AiClient.Ask(new[] { photo }, "Name it");
                    string req = api.Last;
                    object body = api.LastBody;
                    Check(answer == "2026-03-01 Beach at sunset", "AI: the answer's text comes back");
                    Check(req.Contains("x-api-key: " + key) && req.Contains("anthropic-version: 2026-10-21") && req.Contains("anthropic-beta: server-side-fallback-2026-07-01"),
                        "AI: the request carries the key, the API version and the fallback beta");
                    Check(Json.Get(body, "model") as string == "claude-opus-5-5" && Json.Get(body, "fallbacks") as string == "default" && Json.Get(body, "output_config", "effort") as string == "low"
                          && Json.Get(body, "temperature") == null, "AI: Claude Opus 5.5 by default, with fallbacks and low effort, no temperature");
                    Check(Json.Get(body, "messages", 0, "content", 0, "type") as string == "image" && Json.Get(body, "messages", 0, "content", 0, "source", "media_type") as string == "image/jpeg"
                          && Json.Get(body, "messages", 0, "content", 1, "text") as string == "Name it", "AI: the picture goes first as JPEG, then the instruction");
                    using (var ms = new MemoryStream(Convert.FromBase64String((string)Json.Get(body, "messages", 0, "content", 0, "source", "data"))))
                    using (Image sent = Image.FromStream(ms)) Check(Math.Max(sent.Width, sent.Height) == 1568, "AI: big pictures are made smaller before sending");

                    string pdf = Path.Combine(dir, "doc.pdf");
                    File.WriteAllText(pdf, "%PDF-1.4\n%%EOF");
                    string txt = Make.Text(dir, "notes.txt", "Buy milk");
                    AiClient.Ask(new[] { pdf, txt }, "Summarise");
                    body = api.LastBody;
                    Check(Json.Get(body, "messages", 0, "content", 0, "type") as string == "document" && Json.Get(body, "messages", 0, "content", 0, "source", "media_type") as string == "application/pdf"
                          && ((string)Json.Get(body, "messages", 0, "content", 1, "text")).Contains("Buy milk"), "AI: PDFs go as documents, text files as text");

                    Settings.Current.AiModel = "claude-haiku-5-5";
                    AiClient.Ask(new[] { txt }, "x");
                    Check(Json.Get(api.LastBody, "fallbacks") == null && !api.Last.Contains("anthropic-beta"), "AI: Haiku is asked without fallbacks (it has none)");
                    Settings.Current.AiModel = AiClient.DefaultModel;

                    string exe = Make.Text(dir, "tool.exe");
                    string why = null;
                    try { AiClient.Ask(new[] { exe }, "x"); } catch (InvalidOperationException ex) { why = ex.Message; }
                    Check(why != null && why.Contains("cannot be read"), "AI: unsupported files are refused before anything is sent");

                    api.Status = 401; api.Reply = "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}";
                    why = null;
                    try { AiClient.Ask(new[] { txt }, "x"); } catch (InvalidOperationException ex) { why = ex.Message; }
                    Check(why != null && why.StartsWith("The API key was not accepted") && why.Contains("invalid x-api-key"), "AI: errors are explained in plain words");
                    api.Status = 200; api.Reply = "{\"content\":[],\"stop_reason\":\"refusal\"}";
                    why = null;
                    try { AiClient.Ask(new[] { txt }, "x"); } catch (InvalidOperationException ex) { why = ex.Message; }
                    Check(why != null && why.Contains("declined"), "AI: a refusal is reported, not shown as an empty answer");

                    // through the dial's actions
                    var runner = new ActionRunner(new Control(), new FakeNotifier());
                    UndoLog.Clear();
                    api.Reply = FakeApi.Text("\"2026-03-01 Beach at sunset.png\"");
                    ActionResult r = runner.Execute("ai:name", new[] { photo }, new DropInfo(), new Job("AI name"));
                    string renamed = Path.Combine(dir, "2026-03-01 Beach at sunset.png");
                    Check(r.Failures.Count == 0 && File.Exists(renamed) && !File.Exists(photo), "AI name: the file is renamed (quotes and a repeated extension tidied)");
                    Check(UndoLog.UndoLast() == 1 && File.Exists(photo), "AI name: undoable");
                    api.Reply = FakeApi.Text("```csv\nItem,Amount\nCoffee,3.50\n```");
                    r = runner.Execute("ai:table", new[] { pdf }, new DropInfo(), new Job("AI table"));
                    Check(r.Output != null && r.Output.EndsWith("doc - table.csv") && File.ReadAllText(r.Output).StartsWith("Item,Amount"), "AI table: a CSV next to the file, fences removed");
                    api.Reply = FakeApi.Text("Shopping note.\n\n- milk");
                    r = runner.Execute("ai:summary", new[] { txt }, new DropInfo(), new Job("AI summary"));
                    Check(r.Output != null && r.Output.EndsWith("notes - summary.md") && Clipboard.GetText().Contains("milk"), "AI summary: saved next to the file and copied");
                    api.Reply = FakeApi.Text("Hallo");
                    r = runner.Execute("ai:translate:German", new[] { txt }, new DropInfo(), new Job("AI translate"));
                    Check(r.Output != null && r.Output.EndsWith("notes (German).txt") && File.ReadAllText(r.Output).Trim() == "Hallo", "AI translate: a translated copy, same kind of file");
                    api.Reply = FakeApi.Text("Sorry: there is no table here.");
                    r = runner.Execute("ai:table", new[] { txt }, new DropInfo(), new Job("AI table"));
                    Check(r.Output == null && r.Failures.Any(f => f.Contains("no table")), "AI: a \"Sorry:\" answer is reported, nothing is written");

                    Settings.Current.AiEnabled = false;
                    r = runner.Execute("ai:summary", new[] { txt }, new DropInfo(), new Job("AI summary"));
                    Check(r.Failures.Any(f => f.Contains("AI helper is off")), "AI: nothing is sent while the helper is off");
                }
                finally { AiClient.Endpoint = old; }
            }
            Settings.Current = new Settings { AiEnabled = true };
            string png = Make.Png(dir, "menu.png");
            Check(ActionCatalog.For(new[] { png }).First(i => i.Key == "Smart").Children.Any(k => k.Id == "ai:describe"), "AI: with the helper on, the Smart slice offers AI actions");
            AiClient.ForgetKey();
            Check(!ActionCatalog.For(new[] { png }).First(i => i.Key == "Smart").Children.Any(k => k.Id.StartsWith("ai:")), "AI: without a key there are no AI actions");
            Settings.Current = new Settings();
        }

        static string Draw(Gesture g, params int[] xy)
        {
            string found = null;
            long t = 0;
            for (int i = 0; i + 3 < xy.Length; i += 2)
            {
                double x0 = xy[i], y0 = xy[i + 1], x1 = xy[i + 2], y1 = xy[i + 3];
                double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                for (double d = 0; d <= len; d += 5)
                {
                    t += 8;
                    string s = g.Add(x0 + (x1 - x0) * d / len, y0 + (y1 - y0) * d / len, t);
                    if (s != null) found = s;
                }
            }
            return found;
        }

        static void Shapes()
        {
            Check(Draw(new Gesture(1), 0, 0, 160, 0, 10, 130, 170, 130) == "Z", "shapes: Z");
            Check(Draw(new Gesture(1), 0, 0, 70, 120, 140, 0) == "V", "shapes: V");
            Check(Draw(new Gesture(1), 0, 0, 0, 130, 130, 130) == "L", "shapes: L");
            Check(Draw(new Gesture(1), 0, 140, 0, 0, 110, 140, 110, 0) == "N", "shapes: N");
            Check(Draw(new Gesture(1), 0, 0, 600, 40) == null && Draw(new Gesture(1), 0, 0, 30, 0, 0, 25, 30, 25) == null, "shapes: a plain drag or a tiny wiggle is no shape");
            Check(Draw(new Gesture(1), 0, 0, 0, 200, 0, 0, 0, 200) == null, "shapes: back and forth is no shape");
            Check(Draw(new Gesture(1), -600, 0, 160, 0, 10, 130, 170, 130) == "Z", "shapes: a Z right after a long straight drag still counts");
            Check(Gesture.ActionFor(Gesture.DefaultMap, "z") == "Zip" && Gesture.ActionFor("Z=|V=To PDF", "Z") == null, "shapes: each shape's action comes from the settings");
            List<ActionItem> menu = ActionCatalog.For((string[])null);
            Check(Gesture.Find(menu, "Zip") != null && Gesture.Find(menu, "Zip").Id == "zip" && Gesture.Find(menu, "Wallpaper") == null, "shapes: an action is only offered when this drag's menu has it");
        }

        static void DialPreviewAndTouch(string dir)
        {
            // a shape drawn after the dial got out of the way offers its action beside the pointer
            var ov = new FakeOverlay();
            var desk = new FakeDesktop { X = 500, Y = 500 };
            var bubble = new FakeBubble();
            var settings = new Settings();
            var c = new DragController(ov, desk) { GetSettings = delegate { return settings; }, GetLearning = delegate { return new Learning(); }, Bubble = bubble };
            var s = new DragSession { OriginX = 500, OriginY = 500, FileState = 1, Menu = ActionCatalog.For((string[])null) };
            c.OnStarted(s, 0);
            long t = 0;
            Action<int, int> to = delegate (int x, int y)
            {
                double x0 = desk.X, y0 = desk.Y, len = Math.Sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0));
                for (double d = 6; d <= len; d += 6) { desk.X = (int)(x0 + (x - x0) * d / len); desk.Y = (int)(y0 + (y - y0) * d / len); t += 10; c.Tick(t); }
            };
            to(900, 500); // decisive drag: the dial stays away
            Check(!ov.Visible && c.Engine.State == IntentState.Dismissed, "shapes: (the dial got out of the way)");
            to(1060, 500); to(910, 630); to(1070, 630);
            Check(bubble.Visible && bubble.Offered != null && bubble.Offered.Id == "zip" && !bubble.Contains(desk.X, desk.Y), "shapes: a Z offers Zip next to the pointer, never under it");
            to(bubble.Bounds.X + 50, bubble.Bounds.Y + 16);
            t += 4000; c.Tick(t);
            Check(bubble.Visible, "shapes: the offer waits while the pointer is on it");
            to(1300, 900);
            t += 4000; c.Tick(t);
            Check(!bubble.Visible, "shapes: and goes away if the pointer moves on");
            settings.GesturesEnabled = false;
            c.OnStarted(new DragSession { OriginX = 500, OriginY = 500, FileState = 1, Menu = s.Menu }, t);
            desk.X = 500; desk.Y = 500;
            to(900, 500); to(1060, 500); to(910, 630); to(1070, 630);
            Check(!bubble.Visible, "shapes: off in Settings means no offers");

            // the preview reaches the dial once it is ready
            settings = new Settings();
            desk.X = 500; desk.Y = 500;
            var s2 = new DragSession { OriginX = 500, OriginY = 500, FileState = 1, Menu = s.Menu, Summary = "1 file\n2 KB" };
            c.OnStarted(s2, t);
            for (int i = 0; i < 20; i++) { t += 15; desk.X++; c.Tick(t); }
            Check(ov.Visible && ov.Thumb == null, "preview: (dial open, nothing to show yet)");
            string png = Make.Png(dir, "pic.png", 300, 200);
            s2.Thumb = Preview.Thumbnail(png, 96);
            s2.Details = Preview.Details(png);
            t += 15; c.Tick(t);
            Check(ov.Thumb != null && ov.Details == "300 × 200", "preview: Windows' thumbnail and the size appear in the dial");
            Check(RadialRenderer.DetailsLine(new RadialView { Summary = "1 file\n2 KB", Thumb = s2.Thumb, Details = "300 × 200" }) == "1 file · 2 KB · 300 × 200", "preview: the line under the dial reads naturally");
            Check(Preview.Details(Make.Text(dir, "plain.txt")) == null, "preview: nothing made up for files without details");

            // pen and touch
            var detector = new DragDetector { Probe = delegate { return new Surface { Source = SourceKind.Explorer }; } };
            DragSession started = null;
            detector.Started += delegate (DragSession d) { started = d; };
            detector.Down(100, 100, 0, true); detector.Move(120, 100, 10);
            Check(started != null && started.Touch, "touch: a pen or finger drag is recognised");
            started.FileState = 1; started.Menu = s.Menu; started.OriginX = 500; started.OriginY = 500;
            desk.X = 500; desk.Y = 500;
            c.OnStarted(started, t);
            for (int i = 0; i < 20; i++) { t += 15; desk.X++; c.Tick(t); }
            Check(ov.Visible && Math.Abs(ov.Scale - DragController.TouchSize) < 0.01, "touch: it gets the larger dial");
            settings.TouchDial = false;
            started.Touch = true;
            desk.X = 500;
            c.OnStarted(started, t);
            for (int i = 0; i < 20; i++) { t += 15; desk.X++; c.Tick(t); }
            Check(Math.Abs(ov.Scale - 1.0) < 0.01, "touch: unless turned off in Settings");
        }

        static void Menus(string dir)
        {
            Settings.Current = new Settings();
            string webp = Path.Combine(dir, "a.webp");
            File.WriteAllBytes(webp, new byte[] { 0 });
            List<ActionItem> web = ActionCatalog.For(new[] { webp });
            Check(web.Count <= ActionCatalog.MaxSlices && web.Last().Key == "More" && !web.Any(i => i.Id == "pdf") && web.First(i => i.Key == "Convert").Children.Any(k => k.Id == "pdf"),
                "menus: WebP and HEIC pictures keep More (PDF moved into Convert)");
            string img = Make.Png(dir, "b.png");
            List<ActionItem> image = ActionCatalog.For(new[] { img });
            ActionItem open = image.First(i => i.Key == "Open & share");
            Check(open.Children.Any(k => k.Id == "tophone") && open.Children.Any(k => k.Id == "encrypt") && open.Children.Count <= ActionCatalog.MaxChips, "menus: To phone and Encrypt are under Open & share");
            Check(image.First(i => i.Key == "Smart").Children.Any(k => k.Id == "smartname"), "menus: Smart name is on the Smart slice");
            Check(ActionCatalog.For(new[] { img, Make.Png(dir, "c.png") }).First(i => i.Key == "Smart").Children.Any(k => k.Id == "similar"), "menus: several pictures offer Similar photos");
            Check(ActionCatalog.For(new[] { dir }).First(i => i.Key == "Duplicates").Children.Any(k => k.Id == "similar"), "menus: folders with pictures offer Similar photos under Duplicates");
            foreach (string ext in new[] { ".pdf", ".txt", ".docx", ".mp4", ".zip", ".json", ".url", ".bin" })
            {
                string f = Make.Text(dir, "x" + ext);
                List<ActionItem> m = ActionCatalog.For(new[] { f });
                Check(m.Count <= ActionCatalog.MaxSlices && m.Last().Key == "More" && m.All(i => i.Children.Count <= ActionCatalog.MaxChips), "menus: " + ext + " fits the dial and keeps More last");
            }
        }
    }
}
