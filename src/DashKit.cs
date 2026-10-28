using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>The dashboard's colours: Windows 11 light or dark (following Windows), the Windows accent colour, or high contrast.</summary>
    public sealed class DashTheme
    {
        public Brush Window, Sidebar, Card, CardBorder, Text, Sub, Muted, Accent, OnAccent, Hover, Selected, Danger, Success, Warning, Field;
        public bool Dark, HighContrast;
        public string Icons;

        static SolidColorBrush B(int argb) { var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)); b.Freeze(); return b; }
        static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static bool WindowsDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && k.GetValue("AppsUseLightTheme") is int && (int)k.GetValue("AppsUseLightTheme") == 0;
            }
            catch (Exception) { return false; }
        }

        static Color? AccentColor()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k == null ? null : k.GetValue("AccentColor"); // ABGR
                    if (!(v is int)) return null;
                    int abgr = (int)v;
                    return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>The theme for the user's choice in Settings (auto, light, dark); high contrast always wins.</summary>
        public static DashTheme For(Settings s)
        {
            var t = new DashTheme();
            t.Icons = System.Drawing.FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            if (SystemParameters.HighContrast)
            {
                t.HighContrast = true;
                t.Window = t.Sidebar = t.Card = t.Field = SystemColors.WindowBrush;
                t.CardBorder = t.Text = t.Sub = t.Muted = SystemColors.WindowTextBrush;
                t.Accent = t.Selected = SystemColors.HighlightBrush; t.OnAccent = SystemColors.HighlightTextBrush;
                t.Hover = SystemColors.ControlBrush; t.Danger = t.Success = t.Warning = SystemColors.WindowTextBrush;
                return t;
            }
            t.Dark = s.ThemeMode == "dark" || (s.ThemeMode != "light" && WindowsDark());
            Color accent = AccentColor() ?? (t.Dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x67, 0xC0));
            if (t.Dark)
            {
                // a lighter accent reads better on dark
                accent = Color.FromRgb((byte)Math.Min(255, accent.R + (255 - accent.R) / 3), (byte)Math.Min(255, accent.G + (255 - accent.G) / 3), (byte)Math.Min(255, accent.B + (255 - accent.B) / 3));
                t.Window = B(unchecked((int)0xFF202020)); t.Sidebar = B(unchecked((int)0xFF202020)); t.Card = B(unchecked((int)0xFF2B2B2B)); t.CardBorder = B(unchecked((int)0xFF3A3A3A));
                t.Text = B(unchecked((int)0xFFFFFFFF)); t.Sub = B(unchecked((int)0xFFC8C8C8)); t.Muted = B(unchecked((int)0xFF9A9A9A));
                t.Hover = B(unchecked((int)0x14FFFFFF)); t.Selected = B(unchecked((int)0x24FFFFFF)); t.Field = B(unchecked((int)0xFF2F2F2F));
                t.Danger = B(unchecked((int)0xFFFF99A4)); t.Success = B(unchecked((int)0xFF6CCB5F)); t.Warning = B(unchecked((int)0xFFFCE100));
                t.OnAccent = B(Colors.Black);
            }
            else
            {
                t.Window = B(unchecked((int)0xFFF3F3F3)); t.Sidebar = B(unchecked((int)0xFFF3F3F3)); t.Card = B(unchecked((int)0xFFFFFFFF)); t.CardBorder = B(unchecked((int)0xFFE5E5E5));
                t.Text = B(unchecked((int)0xFF1B1B1B)); t.Sub = B(unchecked((int)0xFF5D5D5D)); t.Muted = B(unchecked((int)0xFF8A8A8A));
                t.Hover = B(unchecked((int)0x0A000000)); t.Selected = B(unchecked((int)0x10000000)); t.Field = B(unchecked((int)0xFFFFFFFF));
                t.Danger = B(unchecked((int)0xFFC42B1C)); t.Success = B(unchecked((int)0xFF0F7B0F)); t.Warning = B(unchecked((int)0xFF9D5D00));
                t.OnAccent = B(Colors.White);
            }
            t.Accent = B(accent);
            return t;
        }
    }

    /// <summary>An on/off switch drawn like Windows 11's (keyboard: Space; screen readers: a toggle button with a name).</summary>
    public sealed class Switch : ToggleButton
    {
        readonly DashTheme th;

        public Switch(DashTheme theme, string name)
        {
            th = theme;
            Template = new ControlTemplate(typeof(Switch)); // no chrome: OnRender draws it
            Width = 40; Height = 20; Cursor = Cursors.Hand; Focusable = true;
            Background = Brushes.Transparent;
            AutomationProperties.SetName(this, name);
            Checked += delegate { InvalidateVisual(); };
            Unchecked += delegate { InvalidateVisual(); };
            MouseEnter += delegate { InvalidateVisual(); };
            MouseLeave += delegate { InvalidateVisual(); };
        }

        protected override void OnRender(DrawingContext dc)
        {
            bool on = IsChecked == true;
            var track = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
            Brush fill = on ? th.Accent : th.Field;
            var pen = new Pen(on ? th.Accent : th.Sub, 1);
            dc.DrawRoundedRectangle(fill, pen, track, track.Height / 2, track.Height / 2);
            double r = IsMouseOver ? 7 : 6, cy = ActualHeight / 2;
            double cx = on ? ActualWidth - cy : cy;
            dc.DrawEllipse(on ? th.OnAccent : th.Sub, null, new Point(cx, cy), r, r);
            if (IsKeyboardFocused) dc.DrawRoundedRectangle(null, new Pen(th.Text, 1.5), new Rect(-3, -3, ActualWidth + 6, ActualHeight + 6), cy + 3, cy + 3);
        }
    }

    /// <summary>Small factory for the dashboard's look: cards, setting rows, buttons, fields.</summary>
    public static class UI
    {
        static DashTheme theme = DashTheme.For(new Settings());

        /// <summary>The colours in use; setting them also drops cached looks made with the previous colours.</summary>
        public static DashTheme T { get { return theme; } set { theme = value; buttonStyle = null; primaryStyle = null; } }

        public static TextBlock Text(string text, double size = 14, Brush color = null, bool bold = false)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color ?? T.Text, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        }

        public static TextBlock Sub(string text) { return Text(text, 12.5, T.Sub); }

        public static TextBlock Icon(string glyph, double size = 16, Brush color = null)
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily(T.Icons), FontSize = size, Foreground = color ?? T.Text, VerticalAlignment = VerticalAlignment.Center };
        }

        public static StackPanel Stack(double gap, params UIElement[] items)
        {
            var p = new StackPanel();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                var fe = items[i] as FrameworkElement;
                if (fe != null && p.Children.Count > 0) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom);
                p.Children.Add(items[i]);
            }
            return p;
        }

        public static StackPanel Row(double gap, params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (UIElement e in items)
            {
                if (e == null) continue;
                var fe = e as FrameworkElement;
                if (fe != null && p.Children.Count > 0) fe.Margin = new Thickness(fe.Margin.Left + gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                p.Children.Add(e);
            }
            return p;
        }

        /// <summary>A rounded card with an optional title and description.</summary>
        public static Border Card(string title, string description, params UIElement[] content)
        {
            var inner = new StackPanel();
            if (title != null) inner.Children.Add(Text(title, 15, null, true));
            if (description != null) { var d = Sub(description); d.Margin = new Thickness(0, 2, 0, 0); inner.Children.Add(d); }
            foreach (UIElement c in content ?? new UIElement[0])
            {
                if (c == null) continue;
                var fe = c as FrameworkElement;
                if (fe != null && inner.Children.Count > 0) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 12, fe.Margin.Right, fe.Margin.Bottom);
                inner.Children.Add(c);
            }
            return new Border { Background = T.Card, BorderBrush = T.CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 14, 18, 16), Child = inner, Margin = new Thickness(0, 0, 0, 10) };
        }

        /// <summary>A setting: name and explanation on the left, its control on the right.</summary>
        public static Grid Setting(string name, string description, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = Stack(2, Text(name), description == null ? null : Sub(description));
            left.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(left, 0);
            g.Children.Add(left);
            if (control != null)
            {
                var fe = control as FrameworkElement;
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
                if (fe != null && AutomationProperties.GetName(fe).Length == 0) AutomationProperties.SetName(fe, name);
            }
            return g;
        }

        public static Switch Toggle(string name, bool on, Action<bool> changed)
        {
            var s = new Switch(T, name) { IsChecked = on };
            s.Checked += delegate { changed(true); };
            s.Unchecked += delegate { changed(false); };
            return s;
        }

        static Style buttonStyle, primaryStyle;

        static Style ButtonStyle(bool primary)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
            style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? T.Accent : T.Field));
            style.Setters.Add(new Setter(Control.ForegroundProperty, primary ? T.OnAccent : T.Text));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? T.Accent : T.CardBorder));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 5, 14, 6)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88));
            style.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72));
            style.Triggers.Add(pressed);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            style.Triggers.Add(off);
            return style;
        }

        public static Button Btn(string text, Action click, bool primary = false, string glyph = null)
        {
            if (buttonStyle == null) { buttonStyle = ButtonStyle(false); primaryStyle = ButtonStyle(true); }
            object content = text;
            if (glyph != null) content = Row(8, Icon(glyph, 14, primary ? T.OnAccent : T.Text), new TextBlock { Text = text });
            var b = new Button { Content = content, Style = primary ? primaryStyle : buttonStyle, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(b, text);
            b.Click += delegate
            {
                try { click(); }
                catch (Exception ex) { Log.Error("dashboard", ex); MessageBox.Show(ex.Message, "DragFlow", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            return b;
        }

        public static TextBox Field(string value, double width, Action<string> commit)
        {
            var t = new TextBox { Text = value ?? "", Width = width, Padding = new Thickness(6, 4, 6, 4), FontSize = 13.5, Background = T.Field, Foreground = T.Text, BorderBrush = T.CardBorder, CaretBrush = T.Text };
            if (commit != null)
            {
                string last = t.Text;
                Action done = delegate { if (t.Text != last) { last = t.Text; commit(t.Text); } };
                t.LostKeyboardFocus += delegate { done(); };
                t.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter && !t.AcceptsReturn) done(); };
            }
            return t;
        }

        /// <summary>Several lines (one item per line), saved when the box is left.</summary>
        public static TextBox Lines(IEnumerable<string> lines, double height, Action<List<string>> commit)
        {
            TextBox t = Field(string.Join("\r\n", lines.ToArray()), double.NaN, delegate (string v)
            {
                commit(v.Split('\n').Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            });
            t.AcceptsReturn = true; t.Height = height; t.TextWrapping = TextWrapping.NoWrap;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            return t;
        }

        public static ComboBox Combo(IEnumerable<string> items, int selected, double width, Action<int> changed)
        {
            var c = new ComboBox { Width = width, FontSize = 13.5 };
            foreach (string i in items) c.Items.Add(i);
            c.SelectedIndex = Math.Max(-1, Math.Min(selected, c.Items.Count - 1));
            c.SelectionChanged += delegate { if (c.SelectedIndex >= 0) changed(c.SelectedIndex); };
            return c;
        }

        /// <summary>A small coloured status label ("OK", "Off", "Needs approval").</summary>
        public static Border Chip(string text, Brush color)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 2), BorderBrush = color, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = text, Foreground = color, FontSize = 12 }, VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>A number with a label, for the dashboard's tiles.</summary>
        public static Border Tile(string label, string value, Brush valueColor = null, string glyph = null)
        {
            var top = glyph == null ? (UIElement)Sub(label) : Row(6, Icon(glyph, 13, T.Sub), Sub(label));
            return new Border
            {
                Background = T.Card, BorderBrush = T.CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 10, 10), MinWidth = 160,
                Child = Stack(4, top, Text(value, 20, valueColor, true))
            };
        }

        public static WrapPanel Wrap(params UIElement[] items)
        {
            var w = new WrapPanel();
            foreach (UIElement e in items) if (e != null) w.Children.Add(e);
            return w;
        }

        public static Separator Line()
        {
            return new Separator { Background = T.CardBorder, Margin = new Thickness(0, 6, 0, 6) };
        }
    }
}
