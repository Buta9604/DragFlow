using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DragFlow
{
    /// <summary>Key=value settings in %APPDATA%\DragFlow\settings.ini. Lists are stored separated by "|".</summary>
    public sealed class Settings
    {
        /// <summary>The settings the running app uses (tests may replace it).</summary>
        public static Settings Current = new Settings();

        public bool Enabled = true;
        /// <summary>0.5 (gets out of the way sooner) to 2.0 (stays longer).</summary>
        public double Sensitivity = 1.0;
        /// <summary>Extra folders offered first under "Move to".</summary>
        public List<string> Favorites = new List<string>();

        /// <summary>The dial stays hidden until this time (tray "Pause").</summary>
        public DateTime PausedUntil = DateTime.MinValue;
        /// <summary>Stay quiet while a full-screen app (game, video, slideshow) is open on any screen.</summary>
        public bool QuietWhenFullScreen = true;
        /// <summary>Program names (like "obs64" or "powerpnt") that keep the dial hidden while they run.</summary>
        public List<string> ExcludedApps = new List<string>();
        /// <summary>Folders whose items never get the dial.</summary>
        public List<string> ExcludedFolders = new List<string>();
        /// <summary>Programs besides Explorer whose drags also get the dial (e-mail attachments, browser pictures).</summary>
        public List<string> OtherApps = new List<string>();

        /// <summary>Show the "park here" shelf at the screen edge while dragging files.</summary>
        public bool ShelfEnabled = true;
        /// <summary>Keep parked items after dragging them out of the shelf.</summary>
        public bool ShelfKeepAfterDrag = false;

        /// <summary>Let the dial adapt to how you drag.</summary>
        public bool Learning = true;
        /// <summary>Put the actions you use most first.</summary>
        public bool Ranking = true;
        /// <summary>Action ids that always come first, in this order.</summary>
        public List<string> PinnedActions = new List<string>();

        /// <summary>Write file names into the diagnostic log.</summary>
        public bool LogPaths = false;
        public bool ElevationHintShown = false;
        public bool FirstRunDone = false;

        /// <summary>Actions never offered on the dial (ids or English names).</summary>
        public List<string> HiddenActions = new List<string>();
        /// <summary>Dial size: 1.0 normal, 1.25 large, 1.5 extra large.</summary>
        public double DialSize = 1.0;
        /// <summary>auto (follow Windows), light or dark. High contrast always wins.</summary>
        public string ThemeMode = "auto";
        /// <summary>auto (follow Windows' animation setting), on or off.</summary>
        public string Motion = "auto";
        /// <summary>How many times the dial has shown its "how to use me" tip.</summary>
        public int TipsShown = 0;
        /// <summary>Keyboard shortcut that opens the dial for the selected files without dragging.</summary>
        public bool HotkeyEnabled = true;
        public string Hotkey = "Ctrl+Alt+D";
        /// <summary>"DragFlow actions…" in Explorer's right-click menu.</summary>
        public bool ContextMenu = true;
        /// <summary>Where new versions are published: a folder or web address holding latest.ini. Empty = never check.</summary>
        public string UpdateFeed = "";
        public DateTime LastUpdateCheck = DateTime.MinValue;
        /// <summary>auto (Windows language) or a language code such as "de".</summary>
        public string Language = "auto";
        /// <summary>Run the folder rules.</summary>
        public bool RulesEnabled = true;
        /// <summary>Last choices in the Convert window, per kind (as convert options text).</summary>
        public string ConvertImage = "jpg", ConvertVideo = "mp4", ConvertAudio = "mp3";
        /// <summary>AI actions on the dial (also needs an API key, stored separately and encrypted).</summary>
        public bool AiEnabled = false;
        public string AiModel = AiClient.DefaultModel;
        /// <summary>Shapes drawn while dragging offer an action next to the pointer.</summary>
        public bool GesturesEnabled = true;
        /// <summary>Shape letter → action (an id or the English name on the dial), e.g. "Z=Zip|V=To PDF".</summary>
        public string Gestures = Gesture.DefaultMap;
        /// <summary>Pen and finger drags get a larger dial that waits a little longer.</summary>
        public bool TouchDial = true;
        /// <summary>Thumbnail and details of what is dragged in the dial.</summary>
        public bool Preview = true;
        /// <summary>A guard brings DragFlow back if it stops without being asked to.</summary>
        public bool KeepRunning = true;
        /// <summary>Most the AI helper may spend in a month (US dollars, estimated); 0 = no limit.</summary>
        public double AiMonthlyLimit = 0;

        public static string FilePath { get { return AppPaths.RoamingFile("settings.ini"); } }

        public bool IsPaused { get { return DateTime.Now < PausedUntil; } }

        /// <summary>Take over every value from another copy (the dashboard's Undo), keeping this object the one the app uses.</summary>
        public void CopyFrom(Settings other)
        {
            foreach (System.Reflection.FieldInfo f in typeof(Settings).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                f.SetValue(this, f.GetValue(other));
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    double d;
                    long l;
                    switch (key)
                    {
                        case "Enabled": s.Enabled = val != "0"; break;
                        case "Sensitivity":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Sensitivity = Math.Max(0.5, Math.Min(2.0, d));
                            break;
                        case "Favorites": s.Favorites = List(val); break;
                        case "PausedUntil": if (long.TryParse(val, out l) && l > 0) s.PausedUntil = new DateTime(l); break;
                        case "QuietWhenFullScreen": s.QuietWhenFullScreen = val != "0"; break;
                        case "ExcludedApps": s.ExcludedApps = List(val); break;
                        case "ExcludedFolders": s.ExcludedFolders = List(val); break;
                        case "OtherApps": s.OtherApps = List(val); break;
                        case "ShelfEnabled": s.ShelfEnabled = val != "0"; break;
                        case "ShelfKeepAfterDrag": s.ShelfKeepAfterDrag = val == "1"; break;
                        case "Learning": s.Learning = val != "0"; break;
                        case "Ranking": s.Ranking = val != "0"; break;
                        case "PinnedActions": s.PinnedActions = List(val); break;
                        case "LogPaths": s.LogPaths = val == "1"; break;
                        case "ElevationHintShown": s.ElevationHintShown = val == "1"; break;
                        case "FirstRunDone": s.FirstRunDone = val == "1"; break;
                        case "HiddenActions": s.HiddenActions = List(val); break;
                        case "DialSize":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.DialSize = Math.Max(1.0, Math.Min(1.5, d));
                            break;
                        case "ThemeMode": s.ThemeMode = val == "light" || val == "dark" ? val : "auto"; break;
                        case "Motion": s.Motion = val == "on" || val == "off" ? val : "auto"; break;
                        case "TipsShown": int.TryParse(val, out s.TipsShown); break;
                        case "HotkeyEnabled": s.HotkeyEnabled = val != "0"; break;
                        case "Hotkey": if (val.Length > 0) s.Hotkey = val; break;
                        case "ContextMenu": s.ContextMenu = val != "0"; break;
                        case "UpdateFeed": s.UpdateFeed = val; break;
                        case "LastUpdateCheck": if (long.TryParse(val, out l) && l > 0) s.LastUpdateCheck = new DateTime(l); break;
                        case "Language": s.Language = val.Length > 0 ? val : "auto"; break;
                        case "RulesEnabled": s.RulesEnabled = val != "0"; break;
                        case "ConvertImage": if (val.Length > 0) s.ConvertImage = val; break;
                        case "ConvertVideo": if (val.Length > 0) s.ConvertVideo = val; break;
                        case "ConvertAudio": if (val.Length > 0) s.ConvertAudio = val; break;
                        case "AiEnabled": s.AiEnabled = val == "1"; break;
                        case "AiModel": if (AiClient.Models.Any(m => m[0] == val)) s.AiModel = val; break;
                        case "GesturesEnabled": s.GesturesEnabled = val != "0"; break;
                        case "Gestures": s.Gestures = val; break;
                        case "TouchDial": s.TouchDial = val != "0"; break;
                        case "Preview": s.Preview = val != "0"; break;
                        case "KeepRunning": s.KeepRunning = val != "0"; break;
                        case "AiMonthlyLimit": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.AiMonthlyLimit = Math.Max(0, d); break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        static List<string> List(string val)
        {
            return val.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        static string B(bool b) { return b ? "1" : "0"; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "Enabled=" + B(Enabled),
                    "Sensitivity=" + Sensitivity.ToString("0.00", CultureInfo.InvariantCulture),
                    "Favorites=" + string.Join("|", Favorites.ToArray()),
                    "PausedUntil=" + (PausedUntil > DateTime.Now ? PausedUntil.Ticks : 0),
                    "QuietWhenFullScreen=" + B(QuietWhenFullScreen),
                    "ExcludedApps=" + string.Join("|", ExcludedApps.ToArray()),
                    "ExcludedFolders=" + string.Join("|", ExcludedFolders.ToArray()),
                    "OtherApps=" + string.Join("|", OtherApps.ToArray()),
                    "ShelfEnabled=" + B(ShelfEnabled),
                    "ShelfKeepAfterDrag=" + B(ShelfKeepAfterDrag),
                    "Learning=" + B(Learning),
                    "Ranking=" + B(Ranking),
                    "PinnedActions=" + string.Join("|", PinnedActions.ToArray()),
                    "LogPaths=" + B(LogPaths),
                    "ElevationHintShown=" + B(ElevationHintShown),
                    "FirstRunDone=" + B(FirstRunDone),
                    "HiddenActions=" + string.Join("|", HiddenActions.ToArray()),
                    "DialSize=" + DialSize.ToString("0.00", CultureInfo.InvariantCulture),
                    "ThemeMode=" + ThemeMode,
                    "Motion=" + Motion,
                    "TipsShown=" + TipsShown,
                    "HotkeyEnabled=" + B(HotkeyEnabled),
                    "Hotkey=" + Hotkey,
                    "ContextMenu=" + B(ContextMenu),
                    "UpdateFeed=" + UpdateFeed,
                    "LastUpdateCheck=" + (LastUpdateCheck > DateTime.MinValue ? LastUpdateCheck.Ticks : 0),
                    "Language=" + Language,
                    "RulesEnabled=" + B(RulesEnabled),
                    "ConvertImage=" + ConvertImage,
                    "ConvertVideo=" + ConvertVideo,
                    "ConvertAudio=" + ConvertAudio,
                    "AiEnabled=" + B(AiEnabled),
                    "AiModel=" + AiModel,
                    "GesturesEnabled=" + B(GesturesEnabled),
                    "Gestures=" + Gestures,
                    "TouchDial=" + B(TouchDial),
                    "Preview=" + B(Preview),
                    "KeepRunning=" + B(KeepRunning),
                    "AiMonthlyLimit=" + AiMonthlyLimit.ToString("0.##", CultureInfo.InvariantCulture)
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "DragFlow";

        public static bool StartsWithWindows
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (k == null || k.GetValue(RunName) == null) return false;
                return !StartupTask.DisabledInStartupApps(); // switched off in Task Manager's Startup apps counts as off
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --startup");
                    else k.DeleteValue(RunName, false);
                }
                if (value)
                    try
                    {
                        // turned on here: also undo an earlier "off" in Task Manager's Startup apps
                        using (RegistryKey a = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                            if (a != null) a.DeleteValue(RunName, false);
                    }
                    catch (Exception) { }
                StartupTask.SyncLater();
            }
        }

        /// <summary>Process name as typed by a user ("OBS64.EXE" → "obs64").</summary>
        public static string NormalizeApp(string name)
        {
            string n = Path.GetFileName((name ?? "").Trim().Trim('"'));
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.ToLowerInvariant();
        }
    }
}
