using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace DragFlow
{
    public sealed class ActionItem
    {
        /// <summary>Id runs the action; Key is the English name (used by the code and in settings); Label is what is shown.</summary>
        public string Id, Key, Label, Glyph;
        public Color Accent;
        public readonly List<ActionItem> Children = new List<ActionItem>();

        public ActionItem(string id, string label, string glyph, Color accent)
        {
            Id = id; Key = label; Label = L.T(label); Glyph = glyph; Accent = accent;
        }

        public bool IsParent { get { return Children.Count > 0; } }

        public ActionItem With(params ActionItem[] kids)
        {
            Children.AddRange(kids.Where(k => k != null));
            return this;
        }

        /// <summary>Does a user-typed key ("rename", "Convert", "convert:png") name this item?</summary>
        public bool Matches(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return false;
            return string.Equals(Id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Label, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Key, key, StringComparison.OrdinalIgnoreCase)
                || (Id.Length > 0 && Id.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Everything known about a drag that decides the menu.</summary>
    public sealed class MenuContext
    {
        public string[] Paths;
        public FileKind Kind = FileKind.Other;
        /// <summary>Folder the items are in.</summary>
        public string Folder;
        /// <summary>Folders open in other Explorer windows and tabs.</summary>
        public List<string> OpenFolders = new List<string>();
        public bool ShelfEnabled = true;
        /// <summary>For drags out of other apps: the kind guessed before the file exists.</summary>
        public FileKind? KindHint;
        /// <summary>Program the drag came from ("explorer", "outlook"…).</summary>
        public string App = "explorer";

        public bool Unknown { get { return Paths == null || Paths.Length == 0; } }

        public static MenuContext ForPaths(string[] paths)
        {
            var c = new MenuContext { Paths = paths, ShelfEnabled = Settings.Current.ShelfEnabled };
            if (!c.Unknown)
            {
                c.Kind = FileOps.KindOf(paths);
                c.Folder = FileOps.ParentOf(paths[0]);
            }
            return c;
        }
    }

    /// <summary>Decides which slices (at most 9) and sub-options (at most 8) to offer for what is being dragged.</summary>
    public static partial class ActionCatalog
    {
        public const int MaxSlices = 9, MaxChips = 10;
        /// <summary>How much use moves a slice forward (one use from the same folder scores 3; one elsewhere scores 1).</summary>
        public const double SliceRankThreshold = 4.0, ChipRankThreshold = 2.0;

        static readonly Color Red = Color.FromArgb(235, 87, 87);
        static readonly Color Blue = Color.FromArgb(66, 133, 244);
        static readonly Color Green = Color.FromArgb(52, 190, 120);
        static readonly Color Amber = Color.FromArgb(245, 170, 40);
        static readonly Color Purple = Color.FromArgb(170, 100, 235);
        static readonly Color Slate = Color.FromArgb(130, 150, 170);
        static readonly Color Teal = Color.FromArgb(40, 190, 200);
        static readonly Color Pink = Color.FromArgb(236, 96, 160);
        static readonly Color Indigo = Color.FromArgb(100, 116, 240);
        static readonly Color Orange = Color.FromArgb(240, 120, 60);

        // Segoe Fluent Icons / MDL2 Assets code points
        const string GRename = "\uE8AC", GPdf = "\uE8A5", GConvert = "\uE895", GResize = "\uE73F", GZip = "\uE7B8",
            GMore = "\uE712", GSort = "\uE8CB", GRotate = "\uE7AD", GText = "\uE8C8", GMove = "\uE72A",
            GDupes = "\uE762", GExtract = "\uE896", GNewFolder = "\uE8F4", GPictures = "\uE8B9", GCount = "\uE8EF",
            GCustom = "\uE945", GShare = "\uE72D", GTrim = "\uE8C6", GVolume = "\uE767";

        /// <summary>Every action and slice name the dial can show (for Settings > Actions).</summary>
        public static readonly string[] AllNames =
        {
            "To JPG", "To PDF", "Convert", "Resize", "Edit", "Tools", "Compare", "Recycle Bin", "List contents", "Copy text", "Move to", "Rename", "Open & share", "Zip", "Extract here", "Extract to folder",
            "To images", "Combine", "Combine to PDF", "Shrink", "Thumbnail", "Trim", "Even volume", "Count", "Organize", "Duplicates", "New folder",
            "To picture", "Copy link", "Copy here", "Copy image", "Wallpaper", "Strip EXIF", "Copy file", "Copy path", "SHA-256", "Terminal",
            "Size report", "Desktop shortcut", "Properties", "Open with…", "Share…", "Email", "Print", "Shelf", "Choose…",
            "Smart", "Smart name", "Number by date", "Similar photos", "Exact copies", "To phone", "Share link", "Encrypt…", "Decrypt", "Checksum",
            "AI name", "AI summary", "AI describe", "AI translate…", "AI table", "Ask AI…", "AI e-mail"
        };

        /// <summary>Extra "Move to" folders from settings; set by the app at startup and when settings change.</summary>
        public static List<string> Favorites = new List<string>();

        static bool? ocr, word;
        static bool HasOcr { get { if (ocr == null) ocr = WinRt.OcrAvailable(); return ocr.Value; } }
        static bool HasWord { get { if (word == null) word = FileOps.WordAvailable(); return word.Value; } }

        /// <summary>Checking for OCR, Word and ffmpeg is slow the first time; done on a background thread at startup.</summary>
        public static void WarmUp()
        {
            bool unused = HasOcr | HasWord | FileOps.FindFfmpeg() != null;
            Converter.WarmUp();
            WebCapture.FindEdge();
        }

        static ActionItem Item(string id, string label, string glyph, Color c) { return new ActionItem(id, label, glyph, c); }
        static ActionItem Group(string label, string glyph, Color c, params ActionItem[] kids) { return new ActionItem("", label, glyph, c).With(kids); }
        static ActionItem Kid(string id, string label, Color c) { return new ActionItem(id, label, "", c); }

        static ActionItem Rename() { return Item("rename", "Rename", GRename, Amber); }
        static ActionItem Zip() { return Item("zip", "Zip", GZip, Purple); }
        static ActionItem CopyPath() { return Kid("copypath", "Copy path", Slate); }
        static ActionItem CopyFile() { return Kid("copyfile", "Copy file", Slate); }
        static ActionItem CopyHere() { return Kid("copyhere", "Copy here", Slate); }
        static ActionItem Hash() { return Kid("sha256", "Checksum", Slate); }
        static ActionItem Props(bool single) { return single ? Kid("properties", "Properties", Slate) : null; }

        /// <summary>The menu for these paths (tests and simple callers).</summary>
        public static List<ActionItem> For(string[] paths) { return Build(MenuContext.ForPaths(paths)); }

        public static List<ActionItem> Build(MenuContext ctx)
        {
            List<ActionItem> items;
            string[] paths = ctx.Paths;
            if (ctx.Unknown)
                items = ctx.KindHint.HasValue && ctx.KindHint.Value != FileKind.Other ? ForKind(ctx.KindHint.Value, ctx, true, null) : Generic(false, ctx, true);
            else items = ForKind(ctx.Kind, ctx, paths.Length == 1, paths);

            AddCustom(items, paths);
            AddEveryday(items, ctx);
            AddDecrypt(items, ctx);
            FileKind kind = ctx.KindHint ?? ctx.Kind;
            Profile profile = Profiles.For(ctx.Folder, kind, ctx.App);
            Arrange(items, ctx, profile, Settings.Current, Usage.Current);
            return items;
        }

        static List<ActionItem> ForKind(FileKind k, MenuContext ctx, bool single, string[] paths)
        {
            bool ff = FileOps.FindFfmpeg() != null;
            switch (k)
            {
                case FileKind.Image: return Images(ctx, single);
                case FileKind.Folder: return Folders(ctx, paths);
                case FileKind.Archive: return Archives(ctx, single);
                case FileKind.Pdf: return Pdfs(ctx, single);
                case FileKind.Video: return ff ? Videos(ctx, single) : Generic(true, ctx, single);
                case FileKind.Audio: return ff ? Audio(ctx, single) : Generic(true, ctx, single);
                case FileKind.Word: return !ctx.Unknown && ctx.Paths.All(p => FileOps.OfficeConverterFor(p) != null) ? Word(ctx, single) : Generic(true, ctx, single);
                case FileKind.Text: return Texts(ctx, single);
                case FileKind.Mixed: return Mixed(ctx, paths);
                case FileKind.Web: return Web(ctx, single);
            }
            return Generic(true, ctx, single);
        }

        /// <summary>The format all dragged files share ("webp"), or null when they differ or it is unknown.</summary>
        static string SharedExt(MenuContext ctx)
        {
            if (ctx.Unknown) return null;
            var exts = ctx.Paths.Select(p => Path.GetExtension(p).TrimStart('.').ToLowerInvariant()).Distinct().ToList();
            if (exts.Count != 1) return null;
            string e = exts[0];
            return e == "jpeg" || e == "jfif" ? "jpg" : e == "tif" ? "tiff" : e == "heif" ? "heic" : e;
        }

        /// <summary>Convert to every picture format this PC can write except the one the files already are, then "Options…".</summary>
        static ActionItem ConvertImages(MenuContext ctx)
        {
            string own = SharedExt(ctx);
            string[] order = { "jpg", "png", "webp", "avif", "heic", "ico", "gif", "tiff", "bmp" };
            var writable = Converter.ImageFormats();
            var g = new ActionItem("", "Convert", GConvert, Blue);
            foreach (string id in order)
            {
                if (g.Children.Count >= MaxChips - 1) break;
                Converter.Format f = writable.FirstOrDefault(x => x.Id == id);
                if (f == null || id == own) continue;
                g.Children.Add(Kid("convert:" + id, f.Label, Blue));
            }
            if (WebFormats(ctx) && g.Children.Count < MaxChips - 1) g.Children.Add(Kid("pdf", "PDF", Blue));
            g.Children.Add(Kid("convertdlg", "Options…", Blue));
            return g;
        }

        static ActionItem ConvertMedia(MenuContext ctx, string[] formats)
        {
            string own = SharedExt(ctx);
            var g = new ActionItem("", "Convert", GConvert, Blue);
            foreach (string f in formats)
            {
                if (g.Children.Count >= MaxChips - 1) break;
                if (f == own || !Converter.CanWriteMedia(f)) continue;
                g.Children.Add(Kid("media:" + f, f.ToUpperInvariant(), Blue));
            }
            g.Children.Add(Kid("convertdlg", "Options…", Blue));
            return g;
        }

        /// <summary>Pictures in formats many programs cannot open (WebP, HEIC, AVIF…): "To JPG" is the likely wish.</summary>
        static bool WebFormats(MenuContext ctx)
        {
            return !ctx.Unknown && ctx.Paths.All(p => !Directory.Exists(p) && FileOps.NeedsWic(p));
        }

        static List<ActionItem> Images(MenuContext ctx, bool single)
        {
            bool web = WebFormats(ctx);
            return Slices(
                web ? Item("convert:jpg", "To JPG", GPictures, Blue) : null,
                web ? null : Item("pdf", "To PDF", GPdf, Red),
                ConvertImages(ctx),
                Group("Resize", GResize, Green, Kid("shrink", "Shrink", Green), Kid("resize:50", "50%", Green), Kid("resize:1920", "1920 px", Green), Kid("resize:1280", "1280 px", Green)),
                Group("Edit", GRotate, Teal,
                    web ? null : Kid("rotate:left", "Left", Teal), web ? null : Kid("rotate:right", "Right", Teal), web ? null : Kid("rotate:flip", "Flip", Teal),
                    Kid("crop:1:1", "Square crop", Teal), Kid("crop:16:9", "16:9 crop", Teal), Kid("watermark", "Watermark…", Teal),
                    Kid("gray", "Grayscale", Teal), Kid("border", "Border", Teal),
                    single ? null : Kid("collage", "Collage", Teal), single || FileOps.FindFfmpeg() == null ? null : Kid("animgif", "Animated GIF", Teal)),
                Smart(ctx, FileKind.Image, single, HasOcr ? Kid("ocr", "Copy text", Indigo) : null),
                MoveTo(ctx),
                Rename(),
                OpenShare(ctx, single),
                Group("More", GMore, Slate,
                    CopyHere(),
                    single ? Kid("copyimage", "Copy image", Slate) : null,
                    single ? Kid("wallpaper", "Wallpaper", Slate) : null,
                    Kid("strip", "Strip EXIF", Slate), Kid("zip", "Zip", Slate), CopyFile(), CopyPath(), Props(single)));
        }

        static List<ActionItem> Folders(MenuContext ctx, string[] paths)
        {
            bool images = paths != null && FileOps.HasImagesQuick(paths);
            bool single = paths == null || paths.Length == 1;
            return Slices(
                Group("Rename", GRename, Amber, Kid("rename", single ? "Folder" : "Folders", Amber), Kid("rename-contents", "Contents", Amber)),
                Zip(),
                images ? Item("pdf", "Images to PDF", GPdf, Red) : null,
                images ? ConvertImages(ctx) : null,
                Group("Organize", GSort, Teal, Kid("sort", "By type", Teal), Kid("sortdate", "By date", Teal), Kid("flatten", "Flatten", Teal), Kid("removeempty", "Remove empty", Teal)),
                Duplicates(images),
                MoveTo(ctx),
                Group("More", GMore, Slate, CopyPath(), Kid("terminal", "Terminal", Slate), Kid("sizereport", "Size report", Slate), Kid("listcontents", "List contents", Slate), CopyHere(),
                    single ? null : Kid("newfolder", "New folder", Slate), Kid("shortcut", "Desktop shortcut", Slate), Props(single)));
        }

        static List<ActionItem> Archives(MenuContext ctx, bool single)
        {
            return Slices(
                Item("extract", "Extract here", GExtract, Green),
                Item("extractfolder", "Extract to folder", GNewFolder, Blue),
                Rename(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), CopyFile(), CopyPath(), Hash(), Props(single)));
        }

        static List<ActionItem> Pdfs(MenuContext ctx, bool single)
        {
            return Slices(
                Group("To images", GPictures, Red, Kid("pdfimages", "PNG", Red), Kid("pdfimages:jpg", "JPG", Red)),
                Smart(ctx, FileKind.Pdf, single, HasOcr ? Kid("ocr-pdf", "Copy text", Indigo) : null),
                single ? null : Item("pdf", "Combine", GPdf, Orange),
                Rename(), Zip(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), CopyFile(), CopyPath(), Hash(), Props(single)));
        }

        static List<ActionItem> Videos(MenuContext ctx, bool single)
        {
            return Slices(
                ConvertMedia(ctx, new[] { "mp4", "webm", "mov", "mkv", "gif", "mp3", "m4a" }),
                Item("ff:small", "Shrink", GResize, Green),
                Item("ff:thumb", "Thumbnail", GPictures, Pink),
                single ? Item("trim", "Trim", GTrim, Teal) : null,
                Group("Tools", GCustom, Orange,
                    single ? Kid("vt:size", "Fit in size…", Orange) : null, Kid("vt:rotate", "Rotate", Orange), single ? Kid("vt:speed", "Speed…", Orange) : null,
                    single ? null : Kid("vt:merge", "Merge", Orange), single ? Kid("vt:sheet", "Contact sheet", Orange) : null,
                    single && HasSrt(ctx) ? Kid("vt:subs", "Burn in subtitles", Orange) : null),
                Rename(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), Kid("zip", "Zip", Slate), CopyFile(), CopyPath(), Props(single)));
        }

        static List<ActionItem> Audio(MenuContext ctx, bool single)
        {
            return Slices(
                ConvertMedia(ctx, new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus" }),
                Item("ff:normalize", "Even volume", GVolume, Green),
                single ? Item("trim", "Trim", GTrim, Teal) : null,
                Group("Tools", GCustom, Orange, Kid("at:fade", "Fade in and out", Orange), single ? null : Kid("at:merge", "Merge", Orange),
                    single ? Kid("at:speed", "Speed…", Orange) : null, Kid("at:silence", "Trim silence", Orange)),
                Rename(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), Kid("zip", "Zip", Slate), CopyFile(), CopyPath(), Props(single)));
        }

        static List<ActionItem> Word(MenuContext ctx, bool single)
        {
            return Slices(Item("word2pdf", "To PDF", GPdf, Red), Smart(ctx, FileKind.Word, single), Rename(), Zip(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), CopyFile(), CopyPath(), Hash(), Props(single)));
        }

        static List<ActionItem> Texts(MenuContext ctx, bool single)
        {
            return Slices(
                Item("copytext", "Copy text", GText, Indigo),
                WebCapture.FindEdge() != null ? Item("webpdf", "To PDF", GPdf, Red) : null,
                Group("Tools", GCustom, Orange, Kid("textstats", "Count", Orange), Kid("tt:crlf", "Windows line endings", Orange), Kid("tt:lf", "Unix line endings", Orange), Kid("tt:utf8", "To UTF-8", Orange),
                    AllExt(ctx, ".json") ? Kid("tt:json", "Tidy JSON", Orange) : null, Kid("tt:sort", "Sort lines", Orange), Kid("tt:dedupe", "Remove duplicate lines", Orange),
                    AllExt(ctx, ".md") ? Kid("tt:mdhtml", "Markdown to HTML", Orange) : null),
                Smart(ctx, FileKind.Text, single),
                Rename(), Zip(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), CopyFile(), CopyPath(), Hash(), Props(single)));
        }

        /// <summary>Saved web pages and internet shortcuts: keep them as PDF or as one long picture.</summary>
        static List<ActionItem> Web(MenuContext ctx, bool single)
        {
            bool edge = WebCapture.FindEdge() != null;
            bool shortcut = !ctx.Unknown && ctx.Paths.All(p => p.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".website", StringComparison.OrdinalIgnoreCase));
            return Slices(
                edge ? Item("webpdf", "To PDF", GPdf, Red) : null,
                edge ? Group("To picture", GPictures, Pink, Kid("webimg:jpg", "JPG", Pink), Kid("webimg:png", "PNG", Pink)) : null,
                shortcut ? Item("copylink", "Copy link", GText, Indigo) : null,
                Rename(), Zip(), MoveTo(ctx), OpenShare(ctx, single),
                Group("More", GMore, Slate, CopyHere(), CopyFile(), CopyPath(), Props(single)));
        }

        static List<ActionItem> Generic(bool known, MenuContext ctx, bool single)
        {
            return Slices(Rename(), Zip(), MoveTo(ctx), known ? OpenShare(ctx, single) : null,
                Group("More", GMore, Slate, known ? CopyHere() : null, known ? CopyFile() : null, CopyPath(), Hash(),
                    known ? Kid("shortcut", "Desktop shortcut", Slate) : null, known ? Props(single) : null));
        }

        /// <summary>Different kinds together: what makes sense for all of them, plus "Combine into PDF" for pictures and PDFs.</summary>
        static List<ActionItem> Mixed(MenuContext ctx, string[] paths)
        {
            bool printable = paths.Any(p => { FileKind k = FileOps.Classify(p); return k == FileKind.Image || k == FileKind.Pdf; });
            bool images = paths.Any(p => FileOps.Classify(p) == FileKind.Image);
            return Slices(
                printable ? Item("pdf", "Combine to PDF", GPdf, Red) : null,
                Zip(), MoveTo(ctx), Rename(),
                Item("newfolder", "New folder", GNewFolder, Teal),
                images ? ConvertImages(ctx) : null,
                Group("Share", GShare, Pink, Kid("share", "Share…", Pink), Kid("tophone", "To phone", Pink), Kid("email", "Email", Pink), Kid("encrypt", "Encrypt…", Pink), CopyFile()),
                Group("More", GMore, Slate, CopyHere(), CopyPath(), Kid("shortcut", "Desktop shortcut", Slate)));
        }

        /// <summary>Open with the default or a recent app, pick another, share, e-mail or print.</summary>
        static ActionItem OpenShare(MenuContext ctx, bool single)
        {
            string ext = ctx.Unknown ? null : Path.GetExtension(ctx.Paths[0]).ToLowerInvariant();
            var g = new ActionItem("", "Open & share", GShare, Pink);
            string def = ext == null ? null : OpenWith.DefaultAppName(ext);
            g.Children.Add(Kid("open", def != null && def.Length <= 16 ? def : "Open", Pink));
            if (ext != null)
                foreach (OpenWith.App app in OpenWith.RecentApps(ext, 1))
                    g.Children.Add(Kid("app:" + app.Path, app.Name, Pink));
            if (single) g.Children.Add(Kid("openwith", "Open with…", Pink));
            g.Children.Add(Kid("share", "Share…", Pink));
            g.Children.Add(Kid("tophone", "To phone", Pink));
            if (OneDriveShare.Folder() != null) g.Children.Add(Kid("sharelink", "Share link", Pink));
            g.Children.Add(Kid("email", "Email", Pink));
            g.Children.Add(Kid("encrypt", "Encrypt…", Pink));
            if (ext != null && OpenWith.CanPrint(ext)) g.Children.Add(Kid("print", "Print", Pink));
            return g;
        }

        /// <summary>
        /// "Move to" targets, most useful first: the shelf, profile and favourite folders, folders open in other Explorer
        /// windows, recently used targets, then Desktop / Documents / Downloads / Pictures, and "Choose…" last.
        /// Holding Ctrl when dropping copies instead.
        /// </summary>
        public static ActionItem MoveTo(MenuContext ctx)
        {
            var g = new ActionItem("", "Move to", GMove, Blue);
            if (ctx.ShelfEnabled) g.Children.Add(Kid("park", "Shelf", Blue));
            Profile profile = Profiles.For(ctx.Folder, ctx.KindHint ?? ctx.Kind, ctx.App);
            var targets = new List<string>();
            if (profile != null) targets.AddRange(profile.MoveTo);
            targets.AddRange(Favorites);
            targets.AddRange(ctx.OpenFolders.Take(2));
            targets.AddRange(Usage.Current.RecentTargets(4).Take(2));
            targets.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            targets.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            targets.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            targets.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

            string source = ctx.Folder == null ? null : ctx.Folder.TrimEnd('\\');
            var chosen = new List<string>();
            int room = MaxChips - g.Children.Count - 1;
            foreach (string raw in targets)
            {
                if (chosen.Count >= room) break;
                if (string.IsNullOrEmpty(raw)) continue;
                string t = raw.TrimEnd('\\');
                if (chosen.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
                if (source != null && string.Equals(t, source, StringComparison.OrdinalIgnoreCase)) continue;
                if (ctx.Paths != null && ctx.Paths.Any(p => string.Equals(t, p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || t.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                if (!SafeExists(t)) continue;
                chosen.Add(t);
            }
            var names = chosen.Select(c => FileOps.NameOf(c)).ToList();
            for (int i = 0; i < chosen.Count; i++)
            {
                string label = names[i];
                if (string.IsNullOrEmpty(label)) label = chosen[i];
                else if (names.Count(n => string.Equals(n, names[i], StringComparison.OrdinalIgnoreCase)) > 1)
                    label += " (" + FileOps.NameOf(FileOps.ParentOf(chosen[i])) + ")";
                g.Children.Add(Kid("moveto:" + chosen[i], label, Blue));
            }
            g.Children.Add(Kid("movechoose", "Choose…", Blue));
            return g;
        }

        static bool SafeExists(string dir)
        {
            try { return Directory.Exists(dir); } catch (Exception) { return false; }
        }

        static bool AllExt(MenuContext ctx, string ext)
        {
            return !ctx.Unknown && ctx.Paths.All(p => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        static bool HasSrt(MenuContext ctx)
        {
            // FilterLikely never waits for ffmpeg (the dial is opening); the tool itself explains if the filter is missing.
            return !ctx.Unknown && ctx.Paths.Length == 1 && File.Exists(Path.ChangeExtension(ctx.Paths[0], ".srt")) && FileOps.FilterLikely("subtitles");
        }

        /// <summary>Under More, for any known selection: Compare (two files) and the Recycle Bin.</summary>
        static void AddEveryday(List<ActionItem> items, MenuContext ctx)
        {
            if (ctx.Unknown) return;
            ActionItem more = items.FirstOrDefault(i => i.Key == "More");
            if (more == null) return;
            if (ctx.Paths.Length == 2 && ctx.Paths.All(File.Exists) && more.Children.Count < MaxChips) more.Children.Add(Kid("compare", "Compare", Slate));
            if (more.Children.Count < MaxChips) more.Children.Add(Kid("recycle", "Recycle Bin", Slate));
        }

        static void AddCustom(List<ActionItem> items, string[] paths)
        {
            List<CustomAction> custom = CustomActions.Load().Where(a => a.Matches(paths)).Take(MaxChips).ToList();
            List<Recipe> recipes = RecipeStore.Load().Where(rc => rc.Matches(paths)).ToList();
            List<ActionItem> scripts = ScriptKids(paths).ToList();
            if (custom.Count == 0 && recipes.Count == 0 && scripts.Count == 0) return;
            var kids = recipes.Select(rc => Kid("recipe:" + rc.Name, rc.Name, Amber))
                .Concat(scripts)
                .Concat(custom.Select(a => Kid("custom:" + a.Label, a.Label, Amber))).Take(MaxChips).ToArray();
            ActionItem more = items.FirstOrDefault(i => i.Key == "More");
            if (items.Count < MaxSlices)
            {
                items.Insert(more == null ? items.Count : items.IndexOf(more), new ActionItem("", "Custom", GCustom, Amber).With(kids));
            }
            else if (more != null)
            {
                more.Children.InsertRange(0, kids);
                while (more.Children.Count > MaxChips) more.Children.RemoveAt(more.Children.Count - 1);
            }
        }

        static List<ActionItem> Slices(params ActionItem[] items)
        {
            return items.Where(i => i != null && (!i.IsParent || i.Children.Count > 0) && !(i.Id == "" && i.Children.Count == 0))
                        .Select(i => { while (i.Children.Count > MaxChips) i.Children.RemoveAt(i.Children.Count - 1); return i; })
                        .Take(MaxSlices).ToList();
        }

        // ---------- profiles, pinning and ranking ----------

    }
}
