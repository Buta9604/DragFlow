using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragFlow
{
    /// <summary>
    /// A small code editor: line numbers and colours for PowerShell, Python and batch (comments, strings, keywords,
    /// variables). Colours are applied a moment after typing stops.
    /// </summary>
    public sealed class ScriptEditor : Grid
    {
        readonly RichTextBox box = new RichTextBox();
        readonly TextBlock gutter = new TextBlock();
        readonly DispatcherTimer idle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        string language = ".ps1";
        bool painting;
        const double LineHeight = 19;

        static readonly Dictionary<string, string[]> Keywords = new Dictionary<string, string[]>
        {
            { ".ps1", new[] { "if", "else", "elseif", "foreach", "for", "while", "do", "function", "param", "return", "switch", "try", "catch", "finally", "in", "exit", "throw", "begin", "process", "end" } },
            { ".py", new[] { "def", "class", "import", "from", "if", "elif", "else", "for", "while", "return", "try", "except", "finally", "with", "as", "in", "not", "and", "or", "None", "True", "False", "print", "lambda", "pass", "break", "continue" } },
            { ".bat", new[] { "if", "else", "for", "in", "do", "set", "echo", "call", "goto", "exit", "not", "exist", "setlocal", "endlocal", "shift" } }
        };

        public ScriptEditor()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mono = new FontFamily("Cascadia Mono, Consolas");
            gutter.FontFamily = mono; gutter.FontSize = 13; gutter.Foreground = UI.T.Muted; gutter.TextAlignment = TextAlignment.Right;
            gutter.LineHeight = LineHeight; gutter.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            gutter.Padding = new Thickness(8, 5, 10, 5); gutter.MinWidth = 40;
            box.FontFamily = mono; box.FontSize = 13; box.Background = UI.T.Field; box.Foreground = UI.T.Text; box.BorderThickness = new Thickness(0);
            box.CaretBrush = UI.T.Text; box.AcceptsTab = true; box.AcceptsReturn = true;
            box.Document = new FlowDocument { PageWidth = 3000, PagePadding = new Thickness(4, 5, 4, 5), LineHeight = LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            System.Windows.Automation.AutomationProperties.SetName(box, L.T("Script"));
            Grid.SetColumn(box, 1);
            Children.Add(gutter);
            Children.Add(box);
            Background = UI.T.Field;
            box.TextChanged += delegate { if (painting) return; idle.Stop(); idle.Start(); UpdateGutter(); };
            idle.Tick += delegate { idle.Stop(); Paint(); };
        }

        public string Language { set { language = value == ".cmd" ? ".bat" : value; Paint(); } }

        public string Text
        {
            get
            {
                string t = new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
                return t.EndsWith("\r\n") ? t.Substring(0, t.Length - 2) : t;
            }
            set { Fill(value ?? "", 0); }
        }

        void UpdateGutter()
        {
            int lines = Math.Max(1, box.Document.Blocks.Count);
            var sb = new StringBuilder();
            for (int i = 1; i <= lines; i++) sb.Append(i).Append(i < lines ? "\n" : "");
            gutter.Text = sb.ToString();
        }

        void Paint()
        {
            int caret = new TextRange(box.Document.ContentStart, box.CaretPosition).Text.Length;
            Fill(Text, caret);
        }

        /// <summary>Rebuild the document line by line with coloured pieces, then put the caret back.</summary>
        void Fill(string text, int caret)
        {
            painting = true;
            box.IsUndoEnabled = false; // rebuilding for colours is not an edit: Ctrl+Z must never undo it
            try
            {
                FlowDocument doc = box.Document;
                doc.Blocks.Clear();
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                foreach (string line in lines)
                {
                    var p = new Paragraph { Margin = new Thickness(0) };
                    foreach (var piece in Pieces(line)) p.Inlines.Add(new Run(piece.Key) { Foreground = piece.Value });
                    doc.Blocks.Add(p);
                }
                // caret: each line ends with two characters ("\r\n") in TextRange's count
                int remaining = caret, index = 0;
                foreach (Paragraph p in doc.Blocks.OfType<Paragraph>())
                {
                    int len = lines[index].Length;
                    if (remaining <= len || index == lines.Length - 1)
                    {
                        box.CaretPosition = Position(p, Math.Min(remaining, len));
                        break;
                    }
                    remaining -= len + 2;
                    index++;
                }
            }
            finally { painting = false; box.IsUndoEnabled = true; }
            UpdateGutter();
        }

        static TextPointer Position(Paragraph p, int chars)
        {
            foreach (Run r in p.Inlines.OfType<Run>())
            {
                if (chars <= r.Text.Length) return r.ContentStart.GetPositionAtOffset(chars) ?? r.ContentEnd;
                chars -= r.Text.Length;
            }
            return p.ContentEnd;
        }

        /// <summary>A line cut into (text, colour) pieces.</summary>
        IEnumerable<KeyValuePair<string, Brush>> Pieces(string line)
        {
            Brush comment = UI.T.Dark ? Brushes.LightGreen : Brushes.ForestGreen, str = UI.T.Dark ? new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)) : Brushes.Brown;
            Brush kw = UI.T.Accent, variable = UI.T.Dark ? new SolidColorBrush(Color.FromRgb(0xC5, 0x86, 0xC0)) : Brushes.Purple;
            string trimmed = line.TrimStart();
            if (language == ".bat" && (trimmed.StartsWith("::") || Regex.IsMatch(trimmed, @"^@?rem(\s|$)", RegexOptions.IgnoreCase)))
                return new[] { new KeyValuePair<string, Brush>(line, comment) };
            var result = new List<KeyValuePair<string, Brush>>();
            string[] words = Keywords.ContainsKey(language) ? Keywords[language] : new string[0];
            var plain = new StringBuilder();
            Action flush = delegate { if (plain.Length > 0) { result.Add(new KeyValuePair<string, Brush>(plain.ToString(), UI.T.Text)); plain.Length = 0; } };
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '#' && language != ".bat") { flush(); result.Add(new KeyValuePair<string, Brush>(line.Substring(i), comment)); return result; }
                if (c == '"' || (c == '\'' && language != ".bat"))
                {
                    int end = line.IndexOf(c, i + 1);
                    if (end < 0) end = line.Length - 1;
                    flush(); result.Add(new KeyValuePair<string, Brush>(line.Substring(i, end - i + 1), str)); i = end + 1; continue;
                }
                if ((c == '$' && language == ".ps1") || (c == '%' && language == ".bat"))
                {
                    Match m = Regex.Match(line.Substring(i), language == ".ps1" ? @"^\$[\w:]+" : @"^%[~\w]+%?");
                    if (m.Success) { flush(); result.Add(new KeyValuePair<string, Brush>(m.Value, variable)); i += m.Length; continue; }
                }
                if (char.IsLetter(c))
                {
                    Match m = Regex.Match(line.Substring(i), @"^[\w-]+");
                    bool boundary = i == 0 || !char.IsLetterOrDigit(line[i - 1]);
                    if (boundary && words.Contains(m.Value, language == ".py" ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase))
                    { flush(); result.Add(new KeyValuePair<string, Brush>(m.Value, kw)); i += m.Length; continue; }
                    plain.Append(m.Value); i += m.Length; continue;
                }
                plain.Append(c); i++;
            }
            flush();
            return result;
        }
    }

    /// <summary>Dashboard page: Scripts studio.</summary>
    public sealed partial class DashboardWindow
    {
        string scriptFile;

        UIElement ScriptsPage()
        {
            List<ScriptAction> all = Scripts.Load();
            if (scriptFile != null && !File.Exists(scriptFile)) scriptFile = null;
            if (scriptFile == null && all.Count > 0) scriptFile = all[0].Path;

            var list = new StackPanel();
            foreach (ScriptAction a in all)
            {
                ScriptAction s = a;
                bool ok = Scripts.Approved(s.Path);
                var row = UI.Setting(s.Name, Path.GetExtension(s.Path).TrimStart('.').ToUpperInvariant() + " · " + s.Types + (s.Each ? " · " + L.T("one item at a time") : ""),
                    ok ? UI.Chip(L.T("Approved"), UI.T.Success) : UI.Chip(L.T("Needs approval"), UI.T.Warning));
                var item = new Border { Child = row, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(4), Cursor = System.Windows.Input.Cursors.Hand,
                                        Background = string.Equals(s.Path, scriptFile, StringComparison.OrdinalIgnoreCase) ? UI.T.Selected : Brushes.Transparent };
                item.MouseLeftButtonUp += delegate
                {
                    if (canLeave != null && !canLeave()) return;
                    canLeave = null; scriptFile = s.Path; Refresh();
                };
                list.Children.Add(item);
            }
            if (all.Count == 0) list.Children.Add(UI.Sub(L.T("No scripts yet. Start from a template below.")));

            var templates = UI.Combo(Scripts.Templates.Select(t => L.T(t[0])), -1, 260, delegate { });
            var create = UI.Btn(L.T("Create"), delegate
            {
                if (templates.SelectedIndex < 0) { MessageBox.Show(L.T("Choose a template first."), "DragFlow"); return; }
                string[] t = Scripts.Templates[templates.SelectedIndex];
                Directory.CreateDirectory(Scripts.Folder);
                string path = FileOps.UniquePath(Scripts.Folder, Path.GetFileNameWithoutExtension(t[1]), Path.GetExtension(t[1]));
                File.WriteAllBytes(path, Scripts.Encode(t[2].Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true), path));
                Scripts.Approve(path); // made here, from DragFlow's own template
                scriptFile = path;
                Refresh();
            }, true, "");
            var folder = UI.Btn(L.T("Open the scripts folder"), delegate { Scripts.EnsureFolder(); System.Diagnostics.Process.Start("explorer.exe", "\"" + Scripts.Folder + "\""); }, false, "");
            var listCard = UI.Card(L.T("Your scripts"), L.T("PowerShell, Python and batch files in the scripts folder appear on the dial under Custom for the file types they name. A new or changed script runs only after you approve it."),
                list, UI.Row(8, templates, create), folder);
            if (scriptFile == null) return UI.Stack(0, listCard, CustomActionsCard());

            ScriptAction current = Scripts.Read(scriptFile);
            var editor = new ScriptEditor { Height = 320 };
            byte[] loadedBytes = File.ReadAllBytes(scriptFile);
            Encoding encoding;
            string loadedText = Scripts.Decode(loadedBytes, out encoding).Replace("\r\n", "\n").Replace("\n", "\r\n");
            string loadedHash = Scripts.FingerprintOf(loadedBytes);
            editor.Text = loadedText;
            editor.Language = Path.GetExtension(scriptFile).ToLowerInvariant();
            var output = new TextBox { IsReadOnly = true, Height = 130, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, Background = UI.T.Field, Foreground = UI.T.Text,
                                       BorderBrush = UI.T.CardBorder, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
            bool approved = Scripts.Approved(scriptFile);
            string file = scriptFile;
            Func<bool> dirty = delegate { return editor.Text != loadedText; };
            Action save = delegate
            {
                if (!dirty()) return; // never rewrite a file that was not changed
                bool wasApproved = Scripts.Approved(file);
                byte[] bytes = Scripts.Encode(editor.Text, encoding, file); // the file's own encoding (no byte order mark for batch files)
                File.WriteAllBytes(file, bytes);
                loadedText = editor.Text; loadedHash = Scripts.FingerprintOf(bytes);
                if (wasApproved) Scripts.ApproveHash(loadedHash); // your edit of a script you had approved stays approved
                output.Text = L.T("Saved.") + (wasApproved ? "" : " " + L.T("Approve it before it can run.")) + "\r\n";
            };
            canLeave = delegate
            {
                if (!dirty()) return true;
                MessageBoxResult answer = MessageBox.Show(L.F("Save your changes to {0}?", Path.GetFileName(file)), "DragFlow", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return false;
                if (answer == MessageBoxResult.Yes) save();
                return true;
            };
            var buttons = UI.Row(8,
                UI.Btn(L.T("Save"), delegate { save(); Refresh(); }, true, ""),
                approved ? null : UI.Btn(L.T("Approve"), delegate
                {
                    if (dirty()) { MessageBox.Show(L.T("Save your changes first, then approve."), "DragFlow"); return; }
                    if (MessageBox.Show(L.T("Approve this script? DragFlow runs it as it is now when you pick it on the dial. Only approve scripts you wrote or trust."), "DragFlow", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    Scripts.ApproveHash(loadedHash); // exactly what is shown here
                    Refresh();
                }, false, ""),
                UI.Btn(L.T("Test on a file…"), delegate
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Title = L.T("Run the script on which file?"), Multiselect = true };
                    if (d.ShowDialog(this) != true) return;
                    save();
                    if (!Scripts.Approved(file)) { output.Text = L.T("Approve the script first: a script runs only after you have approved it.") + "\r\n"; return; }
                    output.Text = L.T("Running…") + "\r\n";
                    string[] picked = d.FileNames;
                    Task.Factory.StartNew(delegate { return Scripts.Test(file, picked); })
                        .ContinueWith(delegate (Task<string> t) { output.Text = t.Exception != null ? t.Exception.InnerExceptions[0].Message : t.Result; }, TaskScheduler.FromCurrentSynchronizationContext());
                }, false, ""),
                UI.Btn(L.T("Delete"), delegate
                {
                    if (MessageBox.Show(L.F("Move \"{0}\" to the Recycle Bin?", Path.GetFileName(file)), "DragFlow", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    canLeave = null; ShellOps.Recycle(file); scriptFile = null; Refresh();
                }, false, ""));
            var editCard = UI.Card(current == null ? Path.GetFileName(scriptFile) : current.Name,
                Path.GetFileName(scriptFile) + " · " + L.T("The first line names it and its file types, for example: # DragFlow: name=Upload; types=image,pdf; each"),
                new Border { Child = editor, BorderBrush = UI.T.CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) },
                buttons, output);
            return UI.Stack(0, listCard, editCard, CustomActionsCard());
        }

        UIElement CustomActionsCard()
        {
            return UI.Card(L.T("Programs on the dial"), L.T("Custom actions start a program with the dragged files (for example Open in VS Code). They are kept in actions.ini."),
                UI.Row(8,
                    UI.Btn(L.T("Edit custom actions"), delegate { CustomActions.EnsureFile(); System.Diagnostics.Process.Start("notepad.exe", "\"" + CustomActions.FilePath + "\""); }),
                    UI.Btn(L.T("Open the classic table"), delegate { host.ShowClassicSettings(); })));
        }
    }
}
