using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DragFlow
{
    /// <summary>
    /// Turns whatever was dropped into real files. Explorer drops are paths already. Drags out of other programs
    /// (e-mail attachments, pictures from a browser) carry "virtual files" that only exist inside the drop; those are
    /// saved into Downloads\DragFlow first, so the action has a file to work on and its result lands somewhere visible.
    /// </summary>
    public static class VirtualFiles
    {
        /// <summary>Tests point this elsewhere.</summary>
        public static string OverrideDir;

        public static string StagingDir
        {
            get { return OverrideDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DragFlow"); }
        }

        public static bool CanProvideFiles(System.Windows.Forms.IDataObject data)
        {
            if (data == null) return false;
            return data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("FileGroupDescriptorW") || data.GetDataPresent(DataFormats.Bitmap);
        }

        /// <summary>The dropped items as file paths; <paramref name="saved"/> lists files DragFlow had to create.</summary>
        public static string[] FromDrop(System.Windows.Forms.IDataObject data, out List<string> saved)
        {
            saved = new List<string>();
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) return files;
            if (data.GetDataPresent("FileGroupDescriptorW"))
            {
                saved = SaveVirtual(data);
                if (saved.Count > 0) return saved.ToArray();
            }
            var bmp = data.GetData(DataFormats.Bitmap) as Image;
            if (bmp != null)
            {
                Directory.CreateDirectory(StagingDir);
                saved.Add(FileOps.SaveBitmap(bmp, StagingDir, "Picture " + DateTime.Now.ToString("yyyy-MM-dd HHmmss")));
                return saved.ToArray();
            }
            return null;
        }

        public sealed class Descriptor
        {
            public string Name;
            public bool IsFolder;
            public long Size;
        }

        const int DescriptorSize = 592, NameOffset = 72;

        /// <summary>Read a FILEGROUPDESCRIPTORW block.</summary>
        public static List<Descriptor> ParseDescriptors(byte[] block)
        {
            var list = new List<Descriptor>();
            if (block == null || block.Length < 4) return list;
            int count = BitConverter.ToInt32(block, 0);
            for (int i = 0; i < count; i++)
            {
                int at = 4 + i * DescriptorSize;
                if (at + DescriptorSize > block.Length) break;
                uint flags = BitConverter.ToUInt32(block, at);
                // FD_ATTRIBUTES / FD_FILESIZE say which fields are filled in; some programs leave the flags at zero.
                bool hasAttrs = flags == 0 || (flags & 0x4) != 0, hasSize = flags == 0 || (flags & 0x40) != 0;
                uint attrs = hasAttrs ? BitConverter.ToUInt32(block, at + 36) : 0;
                uint high = hasSize ? BitConverter.ToUInt32(block, at + 64) : 0, low = hasSize ? BitConverter.ToUInt32(block, at + 68) : 0;
                string name = Encoding.Unicode.GetString(block, at + NameOffset, 520);
                int nul = name.IndexOf('\0');
                if (nul >= 0) name = name.Substring(0, nul);
                list.Add(new Descriptor { Name = name, IsFolder = (attrs & 0x10) != 0, Size = ((long)high << 32) | low });
            }
            return list;
        }

        /// <summary>A safe relative path from a name another program gave us: no drive, no "..", no forbidden characters.</summary>
        public static string SafeRelative(string name)
        {
            var parts = (name ?? "").Replace('/', '\\').Split('\\')
                .Select(p => new string(p.Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim().TrimEnd('.'))
                .Where(p => p.Length > 0 && p != "." && p != "..").ToArray();
            return parts.Length == 0 ? "Attachment" : string.Join("\\", parts);
        }

        static byte[] ReadStream(object o)
        {
            var ms = o as MemoryStream;
            if (ms != null) return ms.ToArray();
            var s = o as Stream;
            if (s == null) return null;
            using (var copy = new MemoryStream()) { s.CopyTo(copy); return copy.ToArray(); }
        }

        static List<string> SaveVirtual(System.Windows.Forms.IDataObject data)
        {
            var saved = new List<string>();
            List<Descriptor> items = ParseDescriptors(ReadStream(data.GetData("FileGroupDescriptorW")));
            if (items.Count == 0) return saved;
            Directory.CreateDirectory(StagingDir);
            var com = data as ComDataObject;
            short cf = (short)DataFormats.GetFormat("FileContents").Id;
            var tops = new List<string>();
            // Each top-level name in this drop gets its own fresh name, so an earlier drop is never written into.
            var topMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string batchRoot = StagingDir;
            for (int i = 0; i < items.Count; i++)
            {
                string rel = SafeRelative(items[i].Name);
                string[] parts = rel.Split('\\');
                string top = parts[0];
                bool nested = parts.Length > 1 || items[i].IsFolder;
                string mapped;
                if (!topMap.TryGetValue(top, out mapped))
                {
                    mapped = nested ? FileOps.UniquePath(batchRoot, top, "")
                                    : FileOps.UniquePath(batchRoot, Path.GetFileNameWithoutExtension(top), Path.GetExtension(top));
                    topMap[top] = mapped;
                    tops.Add(mapped);
                }
                string target = parts.Length > 1 ? Path.Combine(mapped, string.Join("\\", parts.Skip(1).ToArray())) : mapped;
                if (!Path.GetFullPath(target).StartsWith(Path.GetFullPath(batchRoot) + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (items[i].IsFolder) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                byte[] bytes = com != null ? ContentsAt(com, cf, i) : null;
                if (bytes == null && items.Count == 1) bytes = ReadStream(data.GetData("FileContents"));
                if (bytes == null) continue;
                if (items[i].Size > 0 && bytes.Length > items[i].Size) Array.Resize(ref bytes, (int)items[i].Size); // memory blocks can be padded
                File.WriteAllBytes(target, bytes);
            }
            saved.AddRange(tops.Where(t => File.Exists(t) || Directory.Exists(t)));
            Log.Write("saved " + saved.Count + " dropped item(s) from another app");
            return saved;
        }

        [DllImport("ole32.dll")]
        static extern void ReleaseStgMedium(ref STGMEDIUM medium);
        [DllImport("kernel32.dll")]
        static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")]
        static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")]
        static extern UIntPtr GlobalSize(IntPtr h);

        static byte[] ContentsAt(ComDataObject com, short cf, int index)
        {
            var fmt = new FORMATETC { cfFormat = cf, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = index, ptd = IntPtr.Zero, tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL };
            STGMEDIUM med;
            try { com.GetData(ref fmt, out med); }
            catch (Exception) { return null; }
            try
            {
                if (med.tymed == TYMED.TYMED_ISTREAM)
                {
                    var stream = (IStream)Marshal.GetObjectForIUnknown(med.unionmember);
                    try
                    {
                        using (var ms = new MemoryStream())
                        {
                            var buf = new byte[81920];
                            IntPtr readPtr = Marshal.AllocCoTaskMem(4);
                            try
                            {
                                while (true)
                                {
                                    stream.Read(buf, buf.Length, readPtr);
                                    int n = Marshal.ReadInt32(readPtr);
                                    if (n <= 0) break;
                                    ms.Write(buf, 0, n);
                                }
                            }
                            finally { Marshal.FreeCoTaskMem(readPtr); }
                            return ms.ToArray();
                        }
                    }
                    finally { Marshal.ReleaseComObject(stream); }
                }
                if (med.tymed == TYMED.TYMED_HGLOBAL)
                {
                    IntPtr p = GlobalLock(med.unionmember);
                    try
                    {
                        var bytes = new byte[(long)GlobalSize(med.unionmember).ToUInt64()];
                        Marshal.Copy(p, bytes, 0, bytes.Length);
                        return bytes;
                    }
                    finally { GlobalUnlock(med.unionmember); }
                }
                return null;
            }
            finally { ReleaseStgMedium(ref med); }
        }
    }
}
