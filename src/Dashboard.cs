using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragFlow
{
    /// <summary>What the dashboard needs from the running app (the tray app gives the real one; tests a fake).</summary>
    public interface IDashHost
    {
        Settings Settings { get; }
        /// <summary>Apply changed settings: shortcut, right-click entry, rules, tray icon.</summary>
        void Apply();
        ActionRunner Runner { get; }
        bool HookOk { get; }
        int RulesWatching { get; }
        void ReportProblem();
        void CheckForUpdates();
        void ForgetRuleMemory();
        void ShowClassicSettings();
    }

    /// <summary>
    /// The dashboard: one window for everything, with a sidebar of pages, search, and changes that save at once with an
    /// Undo for the last one. Built with WPF (part of Windows) in code, opened from the tray.
    /// </summary>
    public sealed partial class DashboardWindow : Window
    {
        sealed class Page
        {
            public string Key, Title, Glyph, Subtitle, Keywords;
            public Func<UIElement> Build;
        }

        static DashboardWindow open;
        readonly IDashHost host;
        readonly List<Page> pages = new List<Page>();
        readonly ListBox nav = new ListBox();
        readonly TextBox search = new TextBox();
        readonly ContentControl body = new ContentControl();
        readonly TextBlock title = new TextBlock(), subtitle = new TextBlock();
        readonly Border toast = new Border();
        readonly TextBlock toastText = new TextBlock();
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        Dictionary<string, string> undoFiles;
        Page current;

        Settings S { get { return host.Settings; } }

        public static void Open(IDashHost host)
        {
            if (open != null)
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return;
            }
            open = new DashboardWindow(host);
            // The window runs inside the tray app: an error in a switch or field must never end DragFlow.
            open.Dispatcher.UnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Log.Error("dashboard", e.Exception);
                MessageBox.Show(e.Exception.Message, "DragFlow", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(open); // typing works inside the tray app
            open.Closed += delegate { open = null; };
            open.Show();
            open.Activate();
        }

        internal DashboardWindow(IDashHost host) : this(host, null) { }

        internal DashboardWindow(IDashHost host, string startPage)
        {
            this.host = host;
            UI.T = DashTheme.For(host.Settings);
            if (!UI.T.HighContrast)
                try { Resources.MergedDictionaries.Add(DashStyles.Load(UI.T)); } // drop-downs, fields and lists in the theme's colours
                catch (Exception ex) { Log.Error("dashboard styles", ex); }
            Title = "DragFlow";
            // as big as it is meant to be, but never bigger than the screen (large text scaling, small laptops)
            Rect area = SystemParameters.WorkArea;
            Width = Math.Min(1120, area.Width * 0.92); Height = Math.Min(760, area.Height * 0.9);
            MinWidth = Math.Min(860, Width); MinHeight = Math.Min(560, Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = UI.T.Window;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            try
            {
                using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath))
                    Icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, null);
            }
            catch (Exception) { }
            Closing += delegate (object s, System.ComponentModel.CancelEventArgs e) { if (canLeave != null && !canLeave()) e.Cancel = true; };
            SourceInitialized += delegate { DarkTitleBar(UI.T.Dark); };
            DefinePages();
            Content = Shell();
            Go(pages.FirstOrDefault(p => p.Key == startPage) ?? pages[0]);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        void DarkTitleBar(bool dark)
        {
            try { int on = dark ? 1 : 0; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); } catch (Exception) { }
        }

        void Add(string key, string title, string glyph, string subtitle, string keywords, Func<UIElement> build)
        {
            pages.Add(new Page { Key = key, Title = L.T(title), Glyph = glyph, Subtitle = L.T(subtitle), Keywords = (title + " " + keywords).ToLowerInvariant(), Build = build });
        }

        void DefinePages()
        {
            Add("home", "Home", "", "How DragFlow is doing, and what it did lately", "status health activity undo pause on off", HomePage);
            Add("general", "General", "", "Starting, keeping it running, how the dial behaves", "start windows keep running guard sensitivity favorites move to language log", GeneralPage);
            Add("dial", "Dial", "", "What the dial offers, in what order, and how it looks", "actions order pin hide size colours theme dark light animations preview", DialPage);
            Add("ai", "AI and keys", "", "Your Anthropic key, the model, and what AI has cost", "api key anthropic claude model usage cost limit test connection", AiPage);
            Add("scripts", "Scripts studio", "", "Your own scripts as dial actions", "powershell python batch script editor template approve test custom actions", ScriptsPage);
            Add("rules", "Rules", "", "Things DragFlow does by itself when files arrive", "rule folder watch downloads schedule daily weekly older bigger", RulesPage);
            Add("recipes", "Recipes", "", "Several actions as one", "recipe steps record chain", RecipesPage);
            Add("shapes", "Shapes and shortcuts", "", "Shapes drawn while dragging, the keyboard shortcut, right-click", "gesture shape z v l n hotkey shortcut ctrl alt right-click send to touch pen", ShapesPage);
            Add("quiet", "Apps and quiet", "", "Where the dial stays hidden, and the shelf", "quiet excluded programs folders full screen pause shelf other apps outlook chrome", QuietPage);
            Add("profiles", "Profiles", "", "Different actions first for some folders or programs", "profile folder program first hide move", ProfilesPage);
            Add("backup", "Backup and updates", "", "Your settings on every PC, backups, new versions", "backup restore onedrive folder sync update version", BackupPage);
            Add("privacy", "Privacy and data", "", "What DragFlow keeps, and clearing it", "privacy data clear history learning undo key delete", PrivacyPage);
            Add("help", "Help", "", "Keyboard help, the log, reporting a problem", "help log diagnostics report problem reset keyboard classic", HelpPage);
        }

        UIElement Shell()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // sidebar
            var side = new DockPanel { Background = UI.T.Sidebar, Margin = new Thickness(0) };
            var brand = UI.Row(10, new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = UI.T.Accent, Child = UI.Icon("", 13, UI.T.OnAccent) },
                                   UI.Stack(0, UI.Text("DragFlow", 15, null, true), UI.Text(L.F("Version {0}", Program.Version), 11.5, UI.T.Muted)));
            ((UIElement)((StackPanel)brand).Children[0]).SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            ((Border)((StackPanel)brand).Children[0]).Child.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            brand.Margin = new Thickness(18, 18, 12, 14);
            DockPanel.SetDock(brand, Dock.Top);
            side.Children.Add(brand);

            search.Margin = new Thickness(14, 0, 14, 10);
            search.Padding = new Thickness(8, 6, 8, 6);
            search.Background = UI.T.Field; search.Foreground = UI.T.Text; search.BorderBrush = UI.T.CardBorder; search.CaretBrush = UI.T.Text;
            search.FontSize = 13.5;
            AutomationProperties.SetName(search, L.T("Find a setting"));
            search.VerticalContentAlignment = VerticalAlignment.Center;
            var hint = new TextBlock { Text = L.T("Find a setting"), Foreground = UI.T.Muted, FontSize = 13.5, Margin = new Thickness(24, 0, 0, 10), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var searchBox = new Grid();
            searchBox.Children.Add(search);
            searchBox.Children.Add(hint);
            search.TextChanged += delegate { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; FillNav(); };
            search.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && nav.Items.Count > 0) { nav.SelectedIndex = 0; e.Handled = true; }
                if (e.Key == Key.Escape) search.Text = "";
            };
            DockPanel.SetDock(searchBox, Dock.Top);
            side.Children.Add(searchBox);

            nav.Background = Brushes.Transparent; nav.BorderThickness = new Thickness(0); nav.Margin = new Thickness(8, 0, 8, 8);
            nav.ItemContainerStyle = NavItemStyle();
            ScrollViewer.SetHorizontalScrollBarVisibility(nav, System.Windows.Controls.ScrollBarVisibility.Disabled);
            nav.SelectionChanged += delegate { var p = nav.SelectedItem as ListBoxItem; if (p != null && p.Tag is Page && p.Tag != current) Go((Page)p.Tag); };
            AutomationProperties.SetName(nav, L.T("Pages"));
            side.Children.Add(nav);
            FillNav();
            Grid.SetColumn(side, 0);
            root.Children.Add(side);

            // content
            var right = new Grid { Background = UI.T.Window };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            title.FontSize = 28; title.FontWeight = FontWeights.SemiBold; title.Foreground = UI.T.Text;
            subtitle.FontSize = 13.5; subtitle.Foreground = UI.T.Sub; subtitle.Margin = new Thickness(0, 2, 0, 0);
            var head = UI.Stack(0, title, subtitle);
            head.Margin = new Thickness(32, 22, 32, 14);
            right.Children.Add(head);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Content = body, Focusable = false };
            body.Margin = new Thickness(32, 0, 32, 32);
            body.MaxWidth = 960; body.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(scroll, 1);
            right.Children.Add(scroll);

            // "Saved · Undo" toast
            toastText.Foreground = UI.T.Text; toastText.VerticalAlignment = VerticalAlignment.Center; toastText.FontSize = 13.5;
            var undo = UI.Btn(L.T("Undo"), UndoLast);
            undo.Margin = new Thickness(16, 0, 0, 0);
            undoButton = undo;
            toast.Child = UI.Row(0, UI.Icon("", 14, UI.T.Success), toastText, undo);
            ((FrameworkElement)toastText).Margin = new Thickness(8, 0, 0, 0);
            toast.Background = UI.T.Card; toast.BorderBrush = UI.T.CardBorder; toast.BorderThickness = new Thickness(1); toast.CornerRadius = new CornerRadius(8);
            toast.Padding = new Thickness(14, 8, 8, 8); toast.HorizontalAlignment = HorizontalAlignment.Center; toast.VerticalAlignment = VerticalAlignment.Bottom;
            toast.Margin = new Thickness(0, 0, 0, 20); toast.Visibility = Visibility.Collapsed;
            Grid.SetRow(toast, 1);
            right.Children.Add(toast);
            toastTimer.Tick += delegate { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };

            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            return root;
        }

        /// <summary>The list row look from DashStyles (plain rows in high contrast).</summary>
        Style Rows() { return TryFindResource("Rows") as Style; }

        Style NavItemStyle()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(cp);
            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, UI.T.Selected, "bd"));
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, UI.T.Hover, "bd"));
            template.Triggers.Add(over);
            template.Triggers.Add(sel);
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(UIElement.FocusableProperty, true));
            return style;
        }

        void FillNav()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            nav.Items.Clear();
            foreach (Page p in pages)
            {
                if (q.Length > 0 && !q.Split(' ').All(w => p.Keywords.Contains(w) || p.Title.ToLowerInvariant().Contains(w))) continue;
                var item = new ListBoxItem { Tag = p, Content = UI.Row(12, UI.Icon(p.Glyph, 15, p == current ? UI.T.Accent : UI.T.Text), UI.Text(p.Title, 13.5)) };
                AutomationProperties.SetName(item, p.Title);
                nav.Items.Add(item);
                if (p == current) item.IsSelected = true;
            }
            if (nav.Items.Count == 0) nav.Items.Add(new ListBoxItem { IsEnabled = false, Content = UI.Sub(L.T("Nothing matches. Try another word.")) });
        }

        /// <summary>Set by a page with unsaved work (the script editor): asks before leaving; false means stay.</summary>
        Func<bool> canLeave;

        void Go(Page p)
        {
            if (canLeave != null && p != current && !canLeave()) { FillNav(); return; }
            canLeave = null;
            current = p;
            title.Text = p.Title;
            subtitle.Text = p.Subtitle;
            lastFailed = false;
            try { body.Content = p.Build(); }
            catch (Exception ex)
            {
                lastFailed = true;
                Log.Error("dashboard page " + p.Key, ex);
                body.Content = UI.Card(L.T("This page could not be shown"), ex.Message);
            }
            if (search.Text.Length == 0) FillNav();
        }

        /// <summary>Show a page again (after something changed underneath it).</summary>
        internal void Refresh() { if (current != null) Go(current); }

        internal IEnumerable<string> PageKeys { get { return pages.Select(p => p.Key); } }

        internal void ShowPage(string key) { Page p = pages.FirstOrDefault(x => x.Key == key); if (p != null) Go(p); }

        /// <summary>The page shown failed to build (tests check every page).</summary>
        internal bool PageFailed { get { return lastFailed; } }
        bool lastFailed;

        internal int Matches(string query) { search.Text = query; return nav.Items.OfType<ListBoxItem>().Count(i => i.Tag is Page); }

        // ---------- saving with Undo ----------

        static string[] Files { get { return new[] { Settings.FilePath, RuleStore.FilePath, RecipeStore.FilePath, Profiles.FilePath }; } }

        /// <summary>Make a change and save it at once; the toast offers to undo it (all settings files are put back as they were).</summary>
        internal void Change(string what, Action apply)
        {
            Dictionary<string, string> before;
            try
            {
                before = new Dictionary<string, string>();
                foreach (string f in Files) before[f] = File.Exists(f) ? File.ReadAllText(f) : null;
                apply();
                S.Save();
                host.Apply();
            }
            catch (Exception ex)
            {
                Log.Error("dashboard change", ex);
                MessageBox.Show(L.T("That could not be saved:") + " " + ex.Message, "DragFlow", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            undoFiles = before;
            ShowToast(what, true);
        }

        /// <summary>A change that Undo cannot reverse (the registry, the stored key): just say it is done.</summary>
        internal void Saved(string what) { undoFiles = null; ShowToast(what, false); }

        void ShowToast(string what, bool undoable)
        {
            toastText.Text = what;
            undoButton.Visibility = undoable ? Visibility.Visible : Visibility.Collapsed;
            toast.Visibility = Visibility.Visible;
            toastTimer.Stop(); toastTimer.Start();
        }

        Button undoButton;

        internal void UndoLast()
        {
            if (undoFiles == null) return;
            foreach (var kv in undoFiles)
            {
                try
                {
                    if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                    else File.WriteAllText(kv.Key, kv.Value);
                }
                catch (Exception ex) { Log.Error("dashboard undo", ex); }
            }
            undoFiles = null;
            S.CopyFrom(Settings.Load());
            host.Apply();
            toast.Visibility = Visibility.Collapsed;
            Refresh();
        }

        /// <summary>A switch that saves its setting when flipped.</summary>
        Switch Flip(string name, bool on, Action<bool> set)
        {
            return UI.Toggle(name, on, delegate (bool v) { Change(L.F("{0}: {1}", name, v ? L.T("on") : L.T("off")), delegate { set(v); }); });
        }
    }
}
