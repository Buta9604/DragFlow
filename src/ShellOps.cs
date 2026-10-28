using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DragFlow
{
    /// <summary>Recycle Bin and safe "put aside" operations. DragFlow never deletes a user's file for good.</summary>
    public static class ShellOps
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT64
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom, pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        // 32-bit Windows packs this structure on 1-byte boundaries.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        struct SHFILEOPSTRUCT32
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom, pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        static extern int SHFileOperation64(ref SHFILEOPSTRUCT64 op);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        static extern int SHFileOperation32(ref SHFILEOPSTRUCT32 op);

        const uint FO_DELETE = 3;
        // Allow undo (Recycle Bin), no "are you sure", no progress or error pop-ups,
        // but DO warn if Windows would delete permanently (item too big for the Recycle Bin).
        const ushort Flags = 0x40 | 0x10 | 0x4 | 0x400 | 0x4000;

        /// <summary>
        /// Send a file or folder to the Recycle Bin. If Windows cannot (no Recycle Bin on that drive, the item is too big
        /// for it and the user said no, or an error), it is moved into a "DragFlow removed" folder on the same drive
        /// instead, so nothing is ever destroyed.
        /// </summary>
        public static void Recycle(string path) { RecycleOrPutAside(path); }

        /// <summary>The folder (on the item's own drive) things go to when there is no Recycle Bin for them.</summary>
        public const string AsideFolderName = "DragFlow removed";

        /// <summary>Like Recycle; true when the item went to the Recycle Bin, false when it was put aside instead.</summary>
        public static bool RecycleOrPutAside(string path)
        {
            bool dir = Directory.Exists(path);
            if (!dir && !File.Exists(path)) return true;
            if (HasRecycleBin(path))
            {
                string full = Path.GetFullPath(path).TrimEnd('\\') + "\0";
                bool aborted;
                int rc;
                if (IntPtr.Size == 8)
                {
                    var op = new SHFILEOPSTRUCT64 { wFunc = FO_DELETE, pFrom = full, fFlags = Flags };
                    rc = SHFileOperation64(ref op);
                    aborted = op.fAnyOperationsAborted;
                }
                else
                {
                    var op = new SHFILEOPSTRUCT32 { wFunc = FO_DELETE, pFrom = full, fFlags = Flags };
                    rc = SHFileOperation32(ref op);
                    aborted = op.fAnyOperationsAborted;
                }
                if (rc == 0 && !aborted && !Directory.Exists(path) && !File.Exists(path)) return true;
                Log.Write("Recycle Bin refused an item (code " + rc + (aborted ? ", declined" : "") + "); putting it aside instead");
            }
            PutAside(path, dir);
            return false;
        }

        static void PutAside(string path, bool dir)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            string aside = Path.Combine(root, AsideFolderName);
            try { Directory.CreateDirectory(aside); }
            catch (UnauthorizedAccessException)
            {
                aside = Path.Combine(FileOps.ParentOf(Path.GetFullPath(path)), AsideFolderName);
                Directory.CreateDirectory(aside);
            }
            string name = Path.GetFileName(path.TrimEnd('\\'));
            string dest = FileOps.UniquePath(aside, dir ? name : Path.GetFileNameWithoutExtension(name), dir ? "" : Path.GetExtension(name));
            FileOps.MoveEntry(path, dest);
        }

        static bool HasRecycleBin(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (root.StartsWith(@"\\")) return false;
                return new DriveInfo(root).DriveType == DriveType.Fixed;
            }
            catch (Exception) { return false; }
        }
    }
}
