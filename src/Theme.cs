using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>Dial colours: Windows light/dark (or the user's choice), and Windows' high-contrast colours when that is on.</summary>
    public sealed class Theme
    {
        public bool Dark, HighContrast;
        public Color Surface, Border, Text, Subtle, Shadow;
        /// <summary>In high contrast every action uses the system highlight colour instead of its own.</summary>
        public Color Accent = Color.Empty;
        public int HoverWash, HeadingWash;
        public float BorderWidth = 1;

        public static readonly Theme DarkTheme = new Theme
        {
            Dark = true,
            Surface = Color.FromArgb(250, 36, 37, 43),
            Border = Color.FromArgb(46, 255, 255, 255),
            Text = Color.FromArgb(242, 243, 247),
            Subtle = Color.FromArgb(150, 156, 168),
            Shadow = Color.FromArgb(0, 0, 0),
            HoverWash = 70,
            HeadingWash = 28
        };

        public static readonly Theme LightTheme = new Theme
        {
            Dark = false,
            Surface = Color.FromArgb(252, 252, 252, 253),
            Border = Color.FromArgb(34, 0, 0, 0),
            Text = Color.FromArgb(28, 29, 34),
            Subtle = Color.FromArgb(120, 124, 134),
            Shadow = Color.FromArgb(0, 0, 0),
            HoverWash = 46,
            HeadingWash = 20
        };

        /// <summary>Built from the user's high-contrast colours: solid, thick edges, no translucency or shadow.</summary>
        public static Theme HighContrastTheme()
        {
            Color bg = SystemColors.Window, fg = SystemColors.WindowText;
            return new Theme
            {
                HighContrast = true,
                Dark = bg.GetBrightness() < 0.5,
                Surface = Color.FromArgb(255, bg),
                Border = Color.FromArgb(255, fg),
                Text = fg,
                Subtle = SystemColors.GrayText,
                Shadow = Color.FromArgb(0, 0, 0, 0),
                Accent = SystemColors.Highlight,
                HoverWash = 255,
                HeadingWash = 90,
                BorderWidth = 2
            };
        }

        static bool WindowsUsesLight()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch (System.Security.SecurityException) { return false; }
        }

        /// <summary>The theme for the current settings.</summary>
        public static Theme For(Settings s)
        {
            if (SystemInformation.HighContrast) return HighContrastTheme();
            string mode = s == null ? "auto" : s.ThemeMode;
            if (mode == "light") return LightTheme;
            if (mode == "dark") return DarkTheme;
            return WindowsUsesLight() ? LightTheme : DarkTheme;
        }

        public static Theme Current() { return For(Settings.Current); }

        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(int action, int param, out bool value, int flags);

        /// <summary>Should the dial skip its animations? (Windows' "Animation effects" off, or the user said so.)</summary>
        public static bool ReduceMotion(Settings s)
        {
            if (s != null && s.Motion == "off") return true;
            if (s != null && s.Motion == "on") return false;
            bool on;
            try { if (SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out on, 0)) return !on; }
            catch (Exception) { }
            return false;
        }

        /// <summary>The accent to draw an action in.</summary>
        public Color AccentFor(Color own) { return Accent.IsEmpty ? own : Accent; }

        public static Color WithAlpha(Color c, double alpha)
        {
            return Color.FromArgb((int)Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                                  (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }
}
