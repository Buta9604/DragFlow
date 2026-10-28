using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DragFlow
{
    /// <summary>
    /// Password encryption for files (AES-256 with a key from PBKDF2-SHA256, and an HMAC-SHA256 so a wrong password or a
    /// changed file is detected before anything is written), and checking downloads against a published checksum.
    /// File layout: "DFENC1" | iterations (4 bytes) | salt (16) | IV (16) | AES-CBC( name length (2) | name | contents ) | HMAC (32).
    /// </summary>
    public static class Protect
    {
        public const string Extension = ".dfenc";
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("DFENC1");
        const int HeaderLength = 6 + 4 + 16 + 16, TagLength = 32;
        /// <summary>How hard the password is stretched (tests lower it to stay quick).</summary>
        public static int Iterations = 300000;
        public const int MinPassword = 8;

        public static bool IsEncrypted(string path)
        {
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var b = new byte[6];
                    return fs.Read(b, 0, 6) == 6 && b.SequenceEqual(Magic);
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// One 32-byte key from the password (so a guesser pays the full stretching once per guess, as we do), then two
        /// independent keys from it: one to encrypt, one to check.
        /// </summary>
        static void Keys(string password, byte[] salt, int iterations, out byte[] enc, out byte[] mac)
        {
            byte[] master;
            using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256))
                master = kdf.GetBytes(32);
            using (var h = new HMACSHA256(master))
            {
                enc = h.ComputeHash(Encoding.ASCII.GetBytes("DragFlow encryption key"));
                mac = h.ComputeHash(Encoding.ASCII.GetBytes("DragFlow integrity key"));
            }
            Array.Clear(master, 0, master.Length);
        }

        /// <summary>An encrypted copy next to each item (folders are zipped first). Originals are left as they are.</summary>
        public static List<string> EncryptAll(IList<string> paths, string password)
        {
            if (password == null || password.Length < MinPassword) throw new ArgumentException("Use a password of at least " + MinPassword + " characters.");
            var outputs = new List<string>();
            using (UndoLog.Begin("Encrypt"))
                Batch.Each(paths.ToList(), delegate (string p)
                {
                    string made = Directory.Exists(p) ? EncryptFolder(p, password) : Encrypt(p, password);
                    outputs.Add(made);
                });
            if (outputs.Count == 0) throw Batch.Nothing("Nothing was encrypted.");
            return outputs;
        }

        static string EncryptFolder(string folder, string password)
        {
            string temp = Path.Combine(AppPaths.LocalDir("convert"), Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                ZipFile.CreateFromDirectory(folder, temp, CompressionLevel.Optimal, true);
                return Encrypt(temp, password, FileOps.NameOf(folder) + ".zip", FileOps.ParentOf(folder));
            }
            finally { try { File.Delete(temp); } catch (Exception) { } }
        }

        public static string Encrypt(string file, string password) { return Encrypt(file, password, Path.GetFileName(file), FileOps.ParentOf(file)); }

        static string Encrypt(string file, string password, string name, string dir)
        {
            string dest = FileOps.UniquePath(dir, name, Extension);
            byte[] salt = Random(16), iv = Random(16), encKey, macKey;
            int iterations = Iterations;
            Keys(password, salt, iterations, out encKey, out macKey);
            try
            {
                using (var output = new FileStream(dest, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    output.Write(Magic, 0, Magic.Length);
                    output.Write(BitConverter.GetBytes(iterations), 0, 4);
                    output.Write(salt, 0, 16);
                    output.Write(iv, 0, 16);
                    using (Aes aes = Aes.Create())
                    {
                        aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                        var crypto = new CryptoStream(new NonClosing(output), aes.CreateEncryptor(), CryptoStreamMode.Write);
                        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
                        crypto.Write(BitConverter.GetBytes((ushort)nameBytes.Length), 0, 2);
                        crypto.Write(nameBytes, 0, nameBytes.Length);
                        using (var input = File.OpenRead(file)) Copy(input, crypto);
                        crypto.FlushFinalBlock();
                        crypto.Dispose();
                    }
                    output.Position = 0;
                    byte[] tag = Mac(output, output.Length, macKey);
                    output.Position = output.Length;
                    output.Write(tag, 0, tag.Length);
                }
            }
            catch (Exception)
            {
                try { File.Delete(dest); } catch (Exception) { }
                throw;
            }
            UndoLog.Created(dest);
            return dest;
        }

        /// <summary>The original back next to the encrypted file, under its own name. Nothing is written for a wrong password.</summary>
        public static string Decrypt(string file, string password)
        {
            using (var input = File.OpenRead(file))
            {
                if (input.Length < HeaderLength + 16 + TagLength) throw new InvalidOperationException("This is not a DragFlow encrypted file.");
                var header = new byte[HeaderLength];
                ReadExactly(input, header);
                if (!header.Take(6).SequenceEqual(Magic)) throw new InvalidOperationException("This is not a DragFlow encrypted file.");
                int iterations = BitConverter.ToInt32(header, 6);
                if (iterations < 1000 || iterations > 10000000) throw new InvalidOperationException("This encrypted file is damaged.");
                byte[] salt = header.Skip(10).Take(16).ToArray(), iv = header.Skip(26).Take(16).ToArray(), encKey, macKey;
                Keys(password ?? "", salt, iterations, out encKey, out macKey);
                long body = input.Length - TagLength;
                input.Position = 0;
                byte[] expected = Mac(input, body, macKey);
                var tag = new byte[TagLength];
                input.Position = body;
                ReadExactly(input, tag);
                if (!SameBytes(expected, tag)) throw new InvalidOperationException("Wrong password, or the file was changed since it was encrypted.");

                input.Position = HeaderLength;
                using (Aes aes = Aes.Create())
                {
                    aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var crypto = new CryptoStream(new Window(input, HeaderLength, body - HeaderLength), aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        var lenBytes = new byte[2];
                        ReadExactly(crypto, lenBytes);
                        var nameBytes = new byte[BitConverter.ToUInt16(lenBytes, 0)];
                        ReadExactly(crypto, nameBytes);
                        string name = FileOps.SafeFileName(Path.GetFileName(Encoding.UTF8.GetString(nameBytes)));
                        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(file);
                        string dest = FileOps.UniquePath(FileOps.ParentOf(file), Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
                        try
                        {
                            using (var output = new FileStream(dest, FileMode.CreateNew)) Copy(crypto, output);
                        }
                        catch (Exception)
                        {
                            try { File.Delete(dest); } catch (Exception) { }
                            throw;
                        }
                        UndoLog.Created(dest);
                        return dest;
                    }
                }
            }
        }

        static byte[] Mac(Stream s, long length, byte[] key)
        {
            using (var h = new HMACSHA256(key))
            {
                var buf = new byte[81920];
                long left = length;
                while (left > 0)
                {
                    Job.CheckCancel();
                    int n = s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (n <= 0) throw new EndOfStreamException();
                    h.TransformBlock(buf, 0, n, null, 0);
                    left -= n;
                }
                h.TransformFinalBlock(new byte[0], 0, 0);
                return h.Hash;
            }
        }

        static void Copy(Stream from, Stream to)
        {
            var buf = new byte[81920];
            int n;
            while ((n = from.Read(buf, 0, buf.Length)) > 0) { Job.CheckCancel(); to.Write(buf, 0, n); }
        }

        static void ReadExactly(Stream s, byte[] b)
        {
            int got = 0;
            while (got < b.Length)
            {
                int n = s.Read(b, got, b.Length - got);
                if (n <= 0) throw new InvalidOperationException("This encrypted file is damaged.");
                got += n;
            }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static byte[] Random(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        /// <summary>Lets the cipher stream finish without closing the file underneath.</summary>
        sealed class NonClosing : Stream
        {
            readonly Stream s;
            public NonClosing(Stream inner) { s = inner; }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { return s.Length; } }
            public override long Position { get { return s.Position; } set { throw new NotSupportedException(); } }
            public override void Flush() { s.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { s.Write(buffer, offset, count); }
        }

        /// <summary>A read-only window onto part of a stream (the cipher text between the header and the tag).</summary>
        sealed class Window : Stream
        {
            readonly Stream s;
            long left;
            public Window(Stream inner, long start, long length) { s = inner; s.Position = start; left = length; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (left <= 0) return 0;
                int n = s.Read(buffer, offset, (int)Math.Min(count, left));
                left -= n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { } // the file is closed by its owner
        }

        // ---------- checksums ----------

        /// <summary>The algorithm a checksum of this many hex digits belongs to, or null.</summary>
        public static string AlgorithmFor(string hex)
        {
            switch ((hex ?? "").Length)
            {
                case 32: return "MD5";
                case 40: return "SHA-1";
                case 64: return "SHA-256";
                case 128: return "SHA-512";
            }
            return null;
        }

        public static string Hash(string file, string algorithm)
        {
            using (HashAlgorithm h = algorithm == "MD5" ? (HashAlgorithm)MD5.Create() : algorithm == "SHA-1" ? (HashAlgorithm)SHA1.Create()
                                   : algorithm == "SHA-512" ? (HashAlgorithm)SHA512.Create() : SHA256.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// The checksum to compare a download with: one copied to the clipboard (from the download page), or one in a
        /// checksum file next to it (name.sha256, name.sha256sum, SHA256SUMS…). Null when there is none.
        /// </summary>
        public static string ExpectedChecksum(string file, string clipboard)
        {
            string fromClip = ClipboardHex(clipboard);
            if (fromClip != null) return fromClip;
            string dir = FileOps.ParentOf(file), name = Path.GetFileName(file);
            var candidates = new List<string>();
            foreach (string ext in new[] { ".sha256", ".sha256sum", ".sha512", ".sha1", ".md5" }) candidates.Add(file + ext);
            foreach (string list in new[] { "SHA256SUMS", "SHA256SUMS.txt", "sha256sums.txt", "checksums.txt", "SHA512SUMS", "MD5SUMS" }) candidates.Add(Path.Combine(dir, list));
            foreach (string c in candidates)
            {
                try
                {
                    if (!File.Exists(c) || new FileInfo(c).Length > 1024 * 1024) continue;
                    foreach (string line in File.ReadAllLines(c))
                    {
                        Match m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{32,128})\s+\*?(.+)$");
                        if (m.Success && string.Equals(Path.GetFileName(m.Groups[2].Value.Trim()), name, StringComparison.OrdinalIgnoreCase) && AlgorithmFor(m.Groups[1].Value) != null)
                            return m.Groups[1].Value.ToLowerInvariant();
                        m = Regex.Match(line, @"\(([^)]+)\)\s*=\s*([0-9a-fA-F]{32,128})"); // SHA256 (name) = hash
                        if (m.Success && string.Equals(m.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase)) return m.Groups[2].Value.ToLowerInvariant();
                    }
                    if (c.StartsWith(file, StringComparison.OrdinalIgnoreCase))
                    {
                        string only = SingleHex(File.ReadAllText(c));
                        if (only != null) return only;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        /// <summary>The hex checksum in a short text that holds exactly one (spaces and a "sha256:" prefix allowed).</summary>
        /// <summary>
        /// From the clipboard only a SHA-256 or SHA-512, or a shorter one named as a checksum ("MD5: …"): a bare 32 or 40
        /// digit code is too often something else (a GUID, a commit id) to raise an alarm about.
        /// </summary>
        static string ClipboardHex(string text)
        {
            string hex = SingleHex(text);
            if (hex == null) return null;
            if (hex.Length >= 64) return hex;
            return Regex.IsMatch(text, @"md5|sha-?1|checksum|hash", RegexOptions.IgnoreCase) ? hex : null;
        }

        static string SingleHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 400) return null;
            var found = Regex.Matches(text, @"(?<![0-9a-fA-F])[0-9a-fA-F]{32,128}(?![0-9a-fA-F])").Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant()).Where(h => AlgorithmFor(h) != null).Distinct().ToList();
            return found.Count == 1 ? found[0] : null;
        }

        /// <summary>Compare a file with a checksum; the answer in plain words, starting with "Matches" or "Does NOT match".</summary>
        public static string Verify(string file, string expected, out bool matches)
        {
            string algorithm = AlgorithmFor(expected);
            if (algorithm == null) throw new ArgumentException("That is not a checksum DragFlow knows (MD5, SHA-1, SHA-256 or SHA-512).");
            string actual = Hash(file, algorithm);
            matches = string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal);
            return matches
                ? "Matches the " + algorithm + " checksum: the file is exactly what was published."
                : "Does NOT match the " + algorithm + " checksum. The download is damaged or not the published file; do not open it. Its " + algorithm + " is " + actual.Substring(0, 16) + "…";
        }
    }
}
