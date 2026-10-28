using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Windows.Forms;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace DragFlow
{
    /// <summary>Windows' own Share window (nearby sharing, Phone Link, mail, chat apps…) for desktop programs.</summary>
    public static class ShareSheet
    {
        [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDataTransferManagerInterop
        {
            DataTransferManager GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        static readonly Guid DtmIid = new Guid(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

        static T Wait<T>(IAsyncOperation<T> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(5);
            if (op.Status != AsyncStatus.Completed) throw new InvalidOperationException("Windows could not open the file for sharing.");
            return op.GetResults();
        }

        /// <summary>Show the Share window for these items. Must run on the UI thread. Throws if Windows refuses.</summary>
        public static void Show(IList<string> paths, int nearX, int nearY)
        {
            // The share window needs a window of ours to belong to: a tiny invisible one near the pointer.
            var host = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Size = new System.Drawing.Size(1, 1), Location = new System.Drawing.Point(nearX, nearY), Opacity = 0.01, TopMost = true
            };
            host.Show();
            try
            {
                var interop = (IDataTransferManagerInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(DataTransferManager));
                Guid iid = DtmIid;
                DataTransferManager dtm = interop.GetForWindow(host.Handle, ref iid);
                dtm.DataRequested += delegate (DataTransferManager sender, DataRequestedEventArgs e)
                {
                    DataRequest req = e.Request;
                    DataRequestDeferral deferral = req.GetDeferral();
                    try
                    {
                        var items = new List<IStorageItem>();
                        foreach (string p in paths.Take(50))
                        {
                            if (Directory.Exists(p)) items.Add(Wait(StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(p))));
                            else items.Add(Wait(StorageFile.GetFileFromPathAsync(Path.GetFullPath(p))));
                        }
                        req.Data.Properties.Title = paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : paths.Count + " items";
                        req.Data.Properties.Description = "Shared from DragFlow";
                        req.Data.SetStorageItems(items);
                    }
                    catch (Exception ex)
                    {
                        req.FailWithDisplayText(ex.Message);
                    }
                    finally { deferral.Complete(); }
                };
                interop.ShowShareUIForWindow(host.Handle);
            }
            finally
            {
                // Keep the owner alive while the share window is up, then tidy it away.
                var t = new System.Windows.Forms.Timer { Interval = 120000 };
                t.Tick += delegate { t.Dispose(); host.Close(); };
                t.Start();
            }
        }
    }
}
