using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DragFlow
{
    /// <summary>
    /// Talks to Explorer through the Shell's COM interfaces to learn exactly which items are selected:
    /// in the Explorer tab the drag started in (Windows 11 windows share one handle across tabs), and on the desktop
    /// (asked of the desktop's own folder view, so names never have to be guessed).
    /// </summary>
    public static class ShellInterop
    {
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IOleServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser
        {
            [PreserveSig] int GetWindow(out IntPtr hwnd);
            [PreserveSig] int ContextSensitiveHelp(bool enter);
            [PreserveSig] int InsertMenusSB(IntPtr a, IntPtr b);
            [PreserveSig] int SetMenuSB(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int RemoveMenusSB(IntPtr a);
            [PreserveSig] int SetStatusTextSB(IntPtr text);
            [PreserveSig] int EnableModelessSB(bool enable);
            [PreserveSig] int TranslateAcceleratorSB(IntPtr msg, ushort id);
            [PreserveSig] int BrowseObject(IntPtr pidl, uint flags);
            [PreserveSig] int GetViewStateStream(uint mode, out IntPtr stream);
            [PreserveSig] int GetControlWindow(uint id, out IntPtr hwnd);
            [PreserveSig] int SendControlMsg(uint id, uint msg, IntPtr w, IntPtr l, out IntPtr ret);
            [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView view);
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellView
        {
            [PreserveSig] int GetWindow(out IntPtr hwnd);
            [PreserveSig] int ContextSensitiveHelp(bool enter);
            [PreserveSig] int TranslateAccelerator(IntPtr msg);
            [PreserveSig] int EnableModeless(bool enable);
            [PreserveSig] int UIActivate(uint state);
            [PreserveSig] int Refresh();
            [PreserveSig] int CreateViewWindow(IntPtr prev, IntPtr fs, IntPtr browser, IntPtr rect, out IntPtr hwnd);
            [PreserveSig] int DestroyViewWindow();
            [PreserveSig] int GetCurrentInfo(IntPtr fs);
            [PreserveSig] int AddPropertySheetPages(uint reserved, IntPtr fn, IntPtr lparam);
            [PreserveSig] int SaveViewState();
            [PreserveSig] int SelectItem(IntPtr pidl, uint flags);
            [PreserveSig] int GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IShellWindows
        {
            // vtable slots we never call, in order: get_Count, Item, _NewEnum, Register, RegisterPending, Revoke, OnNavigate, OnActivated
            void _0(); void _1(); void _2(); void _3(); void _4(); void _5(); void _6(); void _7();
            [return: MarshalAs(UnmanagedType.IDispatch)]
            object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object loc, [MarshalAs(UnmanagedType.Struct)] ref object locRoot,
                int swClass, out int hwnd, int options);
        }

        static Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        static Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        static Guid IID_IDispatch = new Guid("00020400-0000-0000-C000-000000000046");
        static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

        static IShellBrowser BrowserOf(object comObject)
        {
            var sp = comObject as IOleServiceProvider;
            if (sp == null) return null;
            object o;
            if (sp.QueryService(ref SID_STopLevelBrowser, ref IID_IShellBrowser, out o) != 0) return null;
            return o as IShellBrowser;
        }

        /// <summary>The tab window (ShellTabWindowClass) an Explorer window object belongs to, or zero.</summary>
        public static IntPtr TabOf(object explorerWindow)
        {
            try
            {
                IShellBrowser b = BrowserOf(explorerWindow);
                IntPtr h;
                return b != null && b.GetWindow(out h) == 0 ? h : IntPtr.Zero;
            }
            catch (Exception) { return IntPtr.Zero; }
        }

        /// <summary>Full paths of the items selected on the desktop, or null.</summary>
        public static string[] DesktopSelection()
        {
            var windows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows));
            object loc = 0 /* CSIDL_DESKTOP */, root = null;
            int hwnd;
            object disp = windows.FindWindowSW(ref loc, ref root, 8 /* SWC_DESKTOP */, out hwnd, 1 /* SWFO_NEEDDISPATCH */);
            if (disp == null) return null;
            IShellBrowser browser = BrowserOf(disp);
            if (browser == null) return null;
            IShellView view;
            if (browser.QueryActiveShellView(out view) != 0 || view == null) return null;
            object fv;
            if (view.GetItemObject(0 /* SVGIO_BACKGROUND */, ref IID_IDispatch, out fv) != 0 || fv == null) return null;
            dynamic folderView = fv;
            var list = new List<string>();
            foreach (dynamic it in folderView.SelectedItems()) list.Add((string)it.Path);
            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>The tab under a point inside an Explorer window: the ShellTabWindowClass ancestor, else the first tab.</summary>
        public static IntPtr TabAt(IntPtr pointWindow, IntPtr root)
        {
            for (IntPtr h = pointWindow; h != IntPtr.Zero && h != root; h = Native.GetParent(h))
                if (Native.ClassOf(h) == "ShellTabWindowClass") return h;
            return Native.FindWindowEx(root, IntPtr.Zero, "ShellTabWindowClass", null);
        }
    }
}
