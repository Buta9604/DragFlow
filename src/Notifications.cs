using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DragFlow
{
    /// <summary>A button on a notification.</summary>
    public sealed class NoteButton
    {
        public string Text;
        public Action Click;
        public NoteButton(string text, Action click) { Text = text; Click = click; }
    }

    /// <summary>Shows messages to the user. The real one draws small cards in the screen corner; tests record them.</summary>
    public interface INotifier
    {
        void Show(string title, string text, params NoteButton[] buttons);
    }

    /// <summary>
    /// Small cards in the bottom-right corner (above the taskbar) that never steal focus, stack up, fade after a few
    /// seconds unless the pointer is on them, and can carry buttons such as Undo, Show and Details.
    /// </summary>
    public sealed class CornerNotifier : INotifier
    {
        readonly Control ui;
        readonly List<NoteCard> cards = new List<NoteCard>();

        public CornerNotifier(Control uiThread) { ui = uiThread; }

        public void Show(string title, string text, params NoteButton[] buttons)
        {
            Log.Write("notify: " + title + " — " + text);
            Action show = delegate
            {
                var card = new NoteCard(title, text, buttons ?? new NoteButton[0]);
                card.FormClosed += delegate { cards.Remove(card); Layout(); };
                cards.Add(card);
                while (cards.Count > 4) cards[0].Close();
                Layout();
                card.Show();
            };
            if (ui.InvokeRequired) ui.BeginInvoke(show); else show();
        }

        void Layout()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int y = wa.Bottom - 12;
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                NoteCard c = cards[i];
                y -= c.Height;
                c.Location = new Point(wa.Right - c.Width - 12, y);
                y -= 8;
            }
        }
    }

    sealed class NoteCard : Form
    {
        static readonly Font BodyFont = new Font("Segoe UI", 9.5f), HeadFont = new Font("Segoe UI Semibold", 10f);
        readonly Timer timer = new Timer { Interval = 100 };
        int life;
        bool hovered;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        public NoteCard(string title, string text, NoteButton[] buttons)
        {
            Theme th = Theme.Current();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Font = BodyFont;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = th.Dark ? Color.FromArgb(43, 44, 50) : Color.White;
            ForeColor = th.Dark ? Color.FromArgb(240, 240, 245) : Color.FromArgb(30, 30, 34);
            Width = 360;
            Padding = new Padding(14, 10, 10, 10);

            var close = new Label { Text = "✕", AutoSize = true, ForeColor = th.Subtle, Cursor = Cursors.Hand, Top = 8, Left = Width - 26 };
            close.Click += delegate { Close(); };
            var head = new Label { Text = title, Font = HeadFont, AutoSize = false, Left = 14, Top = 10, Width = Width - 46, Height = 22, AutoEllipsis = true };
            var body = new Label { Text = text ?? "", Left = 14, Top = 34, Width = Width - 28, MaximumSize = new Size(Width - 28, 120), AutoSize = true, ForeColor = th.Dark ? Color.FromArgb(200, 202, 210) : Color.FromArgb(70, 72, 80) };
            Controls.AddRange(new Control[] { close, head, body });
            int h = body.Top + Math.Max(18, body.PreferredHeight) + 10;
            int x = Width - 12;
            foreach (NoteButton b in buttons.Reverse())
            {
                NoteButton nb = b;
                var btn = new Button { Text = nb.Text, AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 28, Top = h, BackColor = th.Dark ? Color.FromArgb(60, 62, 70) : Color.FromArgb(240, 242, 246), ForeColor = ForeColor };
                btn.FlatAppearance.BorderColor = th.Dark ? Color.FromArgb(80, 82, 92) : Color.FromArgb(210, 212, 220);
                btn.Click += delegate
                {
                    Close();
                    try { nb.Click(); }
                    catch (Exception ex) { Log.Error("notification button", ex); MessageBox.Show(ex.Message, "DragFlow"); }
                };
                Controls.Add(btn);
                btn.Left = x - btn.PreferredSize.Width;
                x = btn.Left - 8;
            }
            if (buttons.Length > 0) h += 28 + 10;
            Height = h;

            life = buttons.Length > 0 ? 90 : 55; // tenths of a second
            MouseEnter += delegate { hovered = true; };
            foreach (Control c in Controls) c.MouseEnter += delegate { hovered = true; };
            timer.Tick += delegate
            {
                if (!Bounds.Contains(Cursor.Position)) hovered = false;
                if (hovered) return;
                if (--life <= 0) Close();
                else if (life < 8) Opacity = life / 8.0;
            };
            Shown += delegate { timer.Start(); };
            FormClosed += delegate { timer.Dispose(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(60, 128, 128, 128))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>A small window with progress and a Cancel button, shown for jobs that take more than a moment.</summary>
    public sealed class ProgressWindow : Form
    {
        readonly Job job;
        readonly Label status = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Timer timer = new Timer { Interval = 150 };
        public volatile bool Finished;

        public ProgressWindow(Job job)
        {
            this.job = job;
            Text = "DragFlow – " + job.Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = true; ShowInTaskbar = true; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(380, 112);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);

            var title = new Label { Text = job.Title + "…", Left = 14, Top = 12, Width = 352, Font = new Font("Segoe UI Semibold", 10f) };
            status.SetBounds(14, 36, 352, 20);
            status.AutoEllipsis = true;
            bar.SetBounds(14, 60, 260, 18);
            var cancel = new Button { Text = "Cancel", Left = 284, Top = 56, Width = 82 };
            cancel.Click += delegate { job.Cancelled = true; cancel.Enabled = false; cancel.Text = "Stopping…"; };
            Controls.AddRange(new Control[] { title, status, bar, cancel });
            CancelButton = cancel;
            timer.Tick += delegate { Refresh2(); };
            Shown += delegate { timer.Start(); };
            FormClosing += delegate (object s, FormClosingEventArgs e) { if (!Finished) { e.Cancel = true; job.Cancelled = true; } };
            FormClosed += delegate { timer.Dispose(); };
        }

        void Refresh2()
        {
            if (Finished) { Close(); return; }
            status.Text = job.Status ?? "";
            if (job.Total > 0)
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Maximum = job.Total;
                bar.Value = Math.Max(0, Math.Min(job.Total, job.Done));
            }
            else bar.Style = ProgressBarStyle.Marquee;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
    }
}
