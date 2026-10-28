using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DragFlow
{
    /// <summary>
    /// Hands files to a phone on the same Wi-Fi: a tiny web server on this PC that answers only one secret address
    /// (128 random bits in the link), offers only the chosen files, and stops when its window closes or after 15 minutes.
    /// Nothing goes through the internet or any account.
    /// </summary>
    public sealed class PhoneTransfer : IDisposable
    {
        public static int LifetimeMs = 15 * 60 * 1000;
        /// <summary>Tests listen on this PC only, so Windows never asks about the network.</summary>
        public static bool LoopbackOnly;

        readonly List<string> files = new List<string>();
        readonly List<string> temp = new List<string>();
        string token;
        TcpListener listener;
        int connections;
        const int MaxConnections = 16, HeadDeadlineMs = 10000;
        readonly long startedAt = Clock.Now;
        volatile bool stopped;
        int badRequests;

        public string Url { get; private set; }
        public int Downloads;
        /// <summary>Raised (on a background thread) after each finished download, and when the link stops working.</summary>
        public event Action Changed;
        public IList<string> Files { get { return files; } }

        public PhoneTransfer(IList<string> paths)
        {
            try { Prepare(paths); }
            catch (Exception)
            {
                Cleanup(); // never leave zipped copies of private folders behind
                throw;
            }
            var t = new Thread(Serve) { IsBackground = true, Name = "DragFlow phone transfer" };
            t.Start();
            Log.Write("phone transfer started for " + files.Count + " file(s)");
        }

        void Prepare(IList<string> paths)
        {
            foreach (string p in paths)
            {
                if (File.Exists(p)) files.Add(p);
                else if (Directory.Exists(p))
                {
                    string zip = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N"), FileOps.NameOf(p) + ".zip");
                    Directory.CreateDirectory(Path.GetDirectoryName(zip));
                    ZipFile.CreateFromDirectory(p, zip, CompressionLevel.Fastest, true);
                    temp.Add(Path.GetDirectoryName(zip));
                    files.Add(zip);
                }
            }
            if (files.Count == 0) throw new InvalidOperationException("Nothing to send.");
            var b = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            token = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            IPAddress address = LoopbackOnly ? IPAddress.Loopback : LocalAddress();
            if (address == null) throw new InvalidOperationException("This PC is not connected to a network. Connect it to the same Wi-Fi as the phone.");
            // Only on the network the QR code points to, not on every adapter (VPN, a second network).
            listener = new TcpListener(address, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Url = "http://" + address + ":" + port + "/" + token + "/";
        }

        void Cleanup()
        {
            foreach (string d in temp) try { Directory.Delete(d, true); } catch (Exception) { }
            temp.Clear();
        }

        /// <summary>The PC's address on its home or office network (a private address with a gateway), or null.</summary>
        public static IPAddress LocalAddress()
        {
            IPAddress best = null;
            int bestScore = -1;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties props;
                try { props = ni.GetIPProperties(); } catch (NetworkInformationException) { continue; }
                bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                bool virtualAdapter = desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vethernet") || desc.Contains("vmware") || desc.Contains("virtualbox") || desc.Contains("wsl");
                foreach (UnicastIPAddressInformation a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] o = a.Address.GetAddressBytes();
                    bool priv = o[0] == 10 || (o[0] == 172 && o[1] >= 16 && o[1] <= 31) || (o[0] == 192 && o[1] == 168);
                    if (o[0] == 169 && o[1] == 254) continue; // no address from the router
                    int score = (gateway ? 4 : 0) + (priv ? 2 : 0) + (virtualAdapter ? 0 : 1) + (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 1 : 0);
                    if (score > bestScore) { best = a.Address; bestScore = score; }
                }
            }
            return best;
        }

        public bool Expired { get { return stopped || Clock.Now - startedAt > LifetimeMs; } }

        void Serve()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { break; }
                if (Expired) { client.Close(); Stop(); break; }
                // Each visitor gets its own thread (never the shared pool DragFlow's actions use), and only a few at once.
                if (Interlocked.Increment(ref connections) > MaxConnections) { Interlocked.Decrement(ref connections); client.Close(); continue; }
                TcpClient c = client;
                new Thread(delegate () { try { Handle(c); } finally { Interlocked.Decrement(ref connections); } }) { IsBackground = true, Name = "DragFlow phone client" }.Start();
            }
        }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 2000; client.SendTimeout = 60000;
                    NetworkStream s = client.GetStream();
                    string method;
                    string path = ReadRequestPath(s, out method);
                    if (path == null) return;
                    bool head = method == "HEAD";
                    if (Expired) { Send(s, "410 Gone", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("This link has stopped working."), head); return; }
                    string prefix = "/" + token + "/";
                    if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        // Browsers ask for icons by themselves; anything else is someone guessing: stop altogether after a while.
                        bool icon = path == "/favicon.ico" || path.StartsWith("/apple-touch-icon") || path == "/robots.txt";
                        if (!icon && Interlocked.Increment(ref badRequests) > 50) Stop();
                        Send(s, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"), head);
                        return;
                    }
                    string rest = path.Substring(prefix.Length);
                    int index;
                    if (rest.Length == 0) Send(s, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page()), head);
                    else if (rest.StartsWith("f/") && int.TryParse(rest.Substring(2), out index) && index >= 0 && index < files.Count) SendFile(s, files[index], head);
                    else Send(s, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"), head);
                }
                catch (Exception ex) { Log.Write("phone transfer: " + ex.Message); }
            }
        }

        /// <summary>The request's method and path, or null. The whole request head must arrive within ten seconds.</summary>
        static string ReadRequestPath(Stream s, out string method)
        {
            method = null;
            var buf = new List<byte>();
            int b;
            long deadline = Clock.Now + HeadDeadlineMs;
            while (buf.Count < 8192 && Clock.Now < deadline && (b = s.ReadByte()) >= 0)
            {
                buf.Add((byte)b);
                int n = buf.Count;
                if (n >= 4 && buf[n - 4] == 13 && buf[n - 3] == 10 && buf[n - 2] == 13 && buf[n - 1] == 10) break;
            }
            string head = Encoding.ASCII.GetString(buf.ToArray());
            string[] first = head.Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
            if (!head.EndsWith("\r\n\r\n") || first.Length < 2 || (first[0] != "GET" && first[0] != "HEAD")) return null;
            method = first[0];
            string path = first[1];
            int q = path.IndexOf('?');
            return q >= 0 ? path.Substring(0, q) : path;
        }

        static void Send(Stream s, string status, string type, byte[] body, bool headOnly)
        {
            string head = "HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                          "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(head);
            s.Write(h, 0, h.Length);
            if (!headOnly) s.Write(body, 0, body.Length);
        }

        void SendFile(Stream s, string file, bool headOnly)
        {
            if (stopped) return;
            string name = Path.GetFileName(file);
            string ascii = new string(name.Select(c => c < 32 || c > 126 || c == '"' || c == '\\' ? '_' : c).ToArray());
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                string head = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + fs.Length +
                              "\r\nContent-Disposition: attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + Uri.EscapeDataString(name) +
                              "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                byte[] h = Encoding.ASCII.GetBytes(head);
                s.Write(h, 0, h.Length);
                if (headOnly) return; // a HEAD request is not a download
                var buf = new byte[65536];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (stopped) return;
                    s.Write(buf, 0, n);
                }
            }
            Interlocked.Increment(ref Downloads);
            Action changed = Changed;
            if (changed != null) changed();
        }

        string Page()
        {
            var sb = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                .Append("<title>From ").Append(WebUtility.HtmlEncode(Environment.MachineName)).Append("</title><style>")
                .Append("body{font-family:system-ui,sans-serif;margin:0;padding:20px;background:#f4f5f8;color:#222}h1{font-size:20px}")
                .Append("a{display:block;background:#fff;border-radius:12px;padding:16px;margin:10px 0;text-decoration:none;color:#1a56db;box-shadow:0 1px 3px #0002;font-size:17px;word-break:break-all}")
                .Append("small{color:#666;display:block;margin-top:4px}</style></head><body><h1>Files from ").Append(WebUtility.HtmlEncode(Environment.MachineName)).Append("</h1>");
            for (int i = 0; i < files.Count; i++)
            {
                long size = 0;
                try { size = new FileInfo(files[i]).Length; } catch (IOException) { }
                sb.Append("<a href=\"f/").Append(i).Append("\" download>").Append(WebUtility.HtmlEncode(Path.GetFileName(files[i])))
                  .Append("<small>").Append(FileOps.FormatSize(size)).Append(" · tap to download</small></a>");
            }
            sb.Append("<p><small>Sent with DragFlow. This link stops working when the window on the PC is closed.</small></p></body></html>");
            return sb.ToString();
        }

        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            try { if (listener != null) listener.Stop(); } catch (Exception) { }
            Cleanup();
            Log.Write("phone transfer stopped after " + Downloads + " download(s)");
            Action c = Changed;
            if (c != null) c();
        }

        public void Dispose() { Stop(); }
    }
}
