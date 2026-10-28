using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>Asks for a password (twice when encrypting), hidden unless "Show" is ticked.</summary>
    public sealed class PasswordDialog : Form
    {
        readonly TextBox first = new TextBox(), second = new TextBox();
        readonly bool confirm;

        public static string Ask(string title, string prompt, bool confirm)
        {
            using (var d = new PasswordDialog(title, prompt, confirm))
                return d.ShowDialog() == DialogResult.OK ? d.first.Text : null;
        }

        PasswordDialog(string title, string prompt, bool confirm)
        {
            this.confirm = confirm;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false; TopMost = true;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(440, confirm ? 230 : 160);
            var label = new Label { Text = prompt, Left = 16, Top = 12, Width = 408, Height = 38 };
            first.SetBounds(16, 54, 408, 26); first.UseSystemPasswordChar = true;
            second.SetBounds(16, 90, 408, 26); second.UseSystemPasswordChar = true; second.Visible = confirm;
            var again = new Label { Text = L.T("Type it again"), Left = 16, Top = 118, Width = 300, Visible = false };
            var show = new CheckBox { Text = L.T("Show"), Left = 16, Top = confirm ? 124 : 88, Width = 100 };
            show.CheckedChanged += delegate { first.UseSystemPasswordChar = second.UseSystemPasswordChar = !show.Checked; };
            var warn = new Label { Text = L.T("There is no way to get the file back without this password. Keep it somewhere safe."), Left = 16, Top = 150, Width = 408, Height = 36, ForeColor = Color.DarkRed, Visible = confirm };
            int by = confirm ? 190 : 120;
            var ok = new Button { Text = "OK", Left = 248, Top = by, Width = 84, Height = 28 };
            var cancel = new Button { Text = L.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 340, Top = by, Width = 84, Height = 28 };
            ok.Click += delegate
            {
                if (confirm && first.Text.Length < Protect.MinPassword) { MessageBox.Show(this, L.F("Use at least {0} characters.", Protect.MinPassword), "DragFlow"); return; }
                if (confirm && first.Text != second.Text) { MessageBox.Show(this, L.T("The two passwords are different."), "DragFlow"); return; }
                if (first.Text.Length == 0) return;
                DialogResult = DialogResult.OK;
            };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { label, first, second, again, show, warn, ok, cancel });
            Shown += delegate { Activate(); first.Focus(); };
        }
    }

    /// <summary>A text answer (from the AI helper) with Copy, and optionally one more button.</summary>
    public sealed class AnswerWindow : Form
    {
        public AnswerWindow(string title, string text, string extraButton, Action extra)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(620, 460);
            MinimumSize = new Size(380, 260);
            TopMost = true;
            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), BorderStyle = BorderStyle.None, BackColor = SystemColors.Window };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var copy = new Button { Text = L.T("Copy"), Width = 100, Height = 30, Top = 8 };
            copy.Click += delegate { Clipboard.SetText(box.Text); copy.Text = L.T("Copied"); };
            var close = new Button { Text = L.T("Close"), Width = 100, Height = 30, Top = 8, DialogResult = DialogResult.Cancel };
            close.Click += delegate { Close(); };
            bottom.Controls.AddRange(new Control[] { copy, close });
            Button more = null;
            if (extraButton != null)
            {
                more = new Button { Text = extraButton, Width = 150, Height = 30, Top = 8 };
                more.Click += delegate { extra(); };
                bottom.Controls.Add(more);
            }
            bottom.Layout += delegate
            {
                close.Left = bottom.Width - 112; copy.Left = bottom.Width - 220;
                if (more != null) more.Left = 12;
            };
            var note = new Label { Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 6, 8, 0), ForeColor = Color.DimGray, Text = L.T("Written by AI: check anything important before relying on it.") };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
            pad.Controls.Add(box);
            Controls.Add(pad);
            Controls.Add(note);
            Controls.Add(bottom);
            CancelButton = close;
        }
    }

    /// <summary>The QR code for "To phone", with the address, what is offered and a Stop button.</summary>
    public sealed class PhoneWindow : Form
    {
        readonly PhoneTransfer transfer;
        readonly Label status = new Label();
        readonly Timer check = new Timer { Interval = 1000 };

        /// <summary>Folders are zipped first, which can take a while: that happens in the background, then the window opens.</summary>
        public static void Open(string[] paths, Action<Exception> failed)
        {
            System.Threading.Tasks.Task.Factory.StartNew(delegate { return new PhoneTransfer(paths); })
                .ContinueWith(delegate (System.Threading.Tasks.Task<PhoneTransfer> t)
                {
                    if (t.Exception != null) failed(t.Exception.InnerExceptions[0]);
                    else new PhoneWindow(t.Result).Show();
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        PhoneWindow(PhoneTransfer t)
        {
            transfer = t;
            Text = L.T("Send to phone");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            TopMost = true;
            Bitmap qr = QrCode.Encode(t.Url).ToBitmap(6);
            ClientSize = new Size(Math.Max(380, qr.Width + 40), qr.Width + 250);
            var head = new Label { Text = L.T("Scan this with the phone's camera. The phone must be on the same Wi-Fi as this PC."), Left = 16, Top = 12, Width = ClientSize.Width - 32, Height = 40 };
            var pic = new PictureBox { Image = qr, SizeMode = PictureBoxSizeMode.AutoSize, Top = 56 };
            pic.Left = (ClientSize.Width - qr.Width) / 2;
            var url = new TextBox { Text = t.Url, ReadOnly = true, Left = 16, Top = 64 + qr.Height, Width = ClientSize.Width - 32 };
            var what = new Label { Text = string.Join(", ", t.Files.Select(f => Path.GetFileName(f)).Take(4).ToArray()) + (t.Files.Count > 4 ? L.F(" and {0} more", t.Files.Count - 4) : ""), Left = 16, Top = 98 + qr.Height, Width = ClientSize.Width - 32, Height = 36, AutoEllipsis = true };
            status.SetBounds(16, 136 + qr.Height, ClientSize.Width - 32, 40);
            status.ForeColor = Color.DimGray;
            var stop = new Button { Text = L.T("Stop sharing"), Width = 130, Height = 30, Top = 184 + qr.Height };
            stop.Left = ClientSize.Width - 146;
            stop.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { head, pic, url, what, status, stop });
            CancelButton = stop;
            t.Changed += delegate { try { BeginInvoke((Action)Refresh2); } catch (Exception) { } };
            check.Tick += delegate { if (transfer.Expired) { transfer.Stop(); Refresh2(); } };
            check.Start();
            FormClosed += delegate { check.Dispose(); transfer.Stop(); qr.Dispose(); };
            Refresh2();
        }

        void Refresh2()
        {
            if (IsDisposed) return;
            status.Text = transfer.Expired
                ? L.T("Stopped: the link no longer works.")
                : L.F("Downloaded {0} time(s). Anyone on this network with the link can download these files until you close this window (at most 15 minutes).", transfer.Downloads);
        }
    }

    /// <summary>"Save what I just did as a recipe": shows the steps found and asks for a name.</summary>
    public static class SaveRecipeDialog
    {
        public static void Show()
        {
            List<RecipeRecorder.Step> steps = RecipeRecorder.Suggest(DateTime.Now);
            if (steps.Count == 0)
            {
                MessageBox.Show(L.T("Nothing to save yet: run one or more actions on some files first (within the last hour)."), "DragFlow");
                return;
            }
            string list = string.Join("\r\n", steps.Select((st, i) => (i + 1) + ". " + st.Label).ToArray());
            string name = PromptDialog.Ask(L.T("Save as recipe"), L.T("Name for a recipe of these steps:") + "  " + string.Join(" › ", steps.Select(st => st.Label).ToArray()),
                L.T("My recipe"), -1, null);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim().Replace("|", "/").Replace(">", "-");
            List<Recipe> all = RecipeStore.Load();
            if (all.Any(r => r.Name == name) &&
                MessageBox.Show(L.F("There is already a recipe called \"{0}\". Replace it?", name), "DragFlow", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            all.RemoveAll(r => r.Name == name);
            all.Add(new Recipe { Name = name, Types = RecipeRecorder.TypesFor(steps[0]), Steps = steps.Select(st => st.Id).ToList() });
            RecipeStore.Save(all);
            MessageBox.Show(L.F("Saved \"{0}\". It is on the dial under Custom for files like these:", name) + "\r\n\r\n" + list, "DragFlow");
        }
    }
}
