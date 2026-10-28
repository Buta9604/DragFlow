using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace DragFlow
{
    /// <summary>
    /// A QR code for a short text (a link), error-correction level M, byte mode. Follows the ISO 18004 construction
    /// (as in Project Nayuki's reference implementation): data and Reed-Solomon codewords, function patterns, the
    /// mask with the lowest penalty.
    /// </summary>
    public sealed class QrCode
    {
        // Error-correction level M, indexed by version (0 unused).
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };
        static readonly int[] Blocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };
        const int FormatBitsM = 0;

        public readonly int Version, Size, Mask;
        readonly bool[,] modules, isFunction; // [y, x]

        public bool this[int x, int y] { get { return modules[y, x]; } }

        public static QrCode Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            for (int ver = 1; ver <= 40; ver++)
            {
                int capacityBits = DataCodewords(ver) * 8;
                int used = 4 + (ver <= 9 ? 8 : 16) + data.Length * 8;
                if (used <= capacityBits) return new QrCode(ver, data);
            }
            throw new ArgumentException("That text is too long for a QR code.");
        }

        QrCode(int ver, byte[] data)
        {
            Version = ver;
            Size = ver * 4 + 17;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];

            // the bit stream: mode, length, bytes, terminator, padding
            var bits = new List<bool>();
            Append(bits, 4, 4);
            Append(bits, data.Length, ver <= 9 ? 8 : 16);
            foreach (byte b in data) Append(bits, b, 8);
            int capacity = DataCodewords(ver) * 8;
            Append(bits, 0, Math.Min(4, capacity - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            var codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(codewords));

            int best = 0, bestPenalty = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int p = Penalty();
                if (p < bestPenalty) { best = m; bestPenalty = p; }
                ApplyMask(m); // XOR again undoes it
            }
            Mask = best;
            ApplyMask(best);
            DrawFormatBits(best);
        }

        static void Append(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        static int RawDataModules(int ver)
        {
            int result = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int numAlign = ver / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (ver >= 7) result -= 36;
            }
            return result;
        }

        static int DataCodewords(int ver) { return RawDataModules(ver) / 8 - EccPerBlock[ver] * Blocks[ver]; }

        // ---------- error correction ----------

        static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        static byte[] Divisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] Remainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        byte[] AddEccAndInterleave(byte[] data)
        {
            int numBlocks = Blocks[Version], eccLen = EccPerBlock[Version];
            int raw = RawDataModules(Version) / 8;
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            byte[] div = Divisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new byte[datLen];
                Array.Copy(data, k, dat, 0, datLen);
                k += datLen;
                var block = new byte[shortLen + 1];
                Array.Copy(dat, block, datLen);
                byte[] ecc = Remainder(dat, div);
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks.Add(block);
            }
            var result = new List<byte>();
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        // ---------- drawing ----------

        void Set(int x, int y, bool dark) { modules[y, x] = dark; isFunction[y, x] = true; }

        void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
            Finder(3, 3); Finder(Size - 4, 3); Finder(3, Size - 4);
            int[] align = AlignmentPositions();
            int n = align.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (!((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)))
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                                Set(align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            DrawFormatBits(0); // reserved now, real bits later
            DrawVersion();
        }

        void Finder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) Set(xx, yy, dist != 2 && dist != 4);
                }
        }

        int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int numAlign = Version / 7 + 2;
            int step = (Version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
            var result = new int[numAlign];
            result[0] = 6;
            for (int i = numAlign - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        static bool Bit(int x, int i) { return ((x >> i) & 1) != 0; }

        void DrawFormatBits(int mask)
        {
            int data = FormatBitsM << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) Set(8, i, Bit(bits, i));
            Set(8, 7, Bit(bits, 6));
            Set(8, 8, Bit(bits, 7));
            Set(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(bits, i));
            Set(8, Size - 8, true);
        }

        void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                Set(a, b, bit);
                Set(b, a, bit);
            }
        }

        void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
            }
        }

        void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert && !isFunction[y, x]) modules[y, x] = !modules[y, x];
                }
        }

        /// <summary>Long runs, 2×2 blocks, finder look-alikes and an uneven dark/light balance all make a code harder to read.</summary>
        int Penalty()
        {
            int result = 0, dark = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < Size; a++)
                {
                    int run = 1;
                    for (int b = 1; b <= Size; b++)
                    {
                        bool same = b < Size && At(pass, a, b) == At(pass, a, b - 1);
                        if (same) { run++; continue; }
                        if (run >= 5) result += 3 + run - 5;
                        run = 1;
                    }
                    for (int b = 0; b + 6 < Size; b++)
                    {
                        // dark-light-dark-dark-dark-light-dark with four light modules on one side
                        bool core = At(pass, a, b) && !At(pass, a, b + 1) && At(pass, a, b + 2) && At(pass, a, b + 3) && At(pass, a, b + 4) && !At(pass, a, b + 5) && At(pass, a, b + 6);
                        if (!core) continue;
                        bool before = true, after = true;
                        for (int k = 1; k <= 4; k++)
                        {
                            if (b - k >= 0 && At(pass, a, b - k)) before = false;
                            if (b + 6 + k < Size && At(pass, a, b + 6 + k)) after = false;
                        }
                        if (before || after) result += 40;
                    }
                }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (modules[y, x]) dark++;
                    if (x + 1 < Size && y + 1 < Size)
                    {
                        bool c = modules[y, x];
                        if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1]) result += 3;
                    }
                }
            int total = Size * Size;
            int k2 = (int)((Math.Abs(dark * 20L - total * 10L) + total - 1) / total) - 1;
            result += Math.Max(0, k2) * 10;
            return result;
        }

        bool At(int pass, int a, int b) { return pass == 0 ? modules[a, b] : modules[b, a]; }

        /// <summary>Black on white with the four-module quiet zone around it.</summary>
        public Bitmap ToBitmap(int moduleSize)
        {
            int border = 4, px = (Size + border * 2) * moduleSize;
            var bmp = new Bitmap(px, px);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (modules[y, x]) g.FillRectangle(Brushes.Black, (x + border) * moduleSize, (y + border) * moduleSize, moduleSize, moduleSize);
            }
            return bmp;
        }
    }
}
