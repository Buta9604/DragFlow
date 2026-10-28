using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DragFlow
{
    /// <summary>Several actions as one: "Resize to 1920 px › Remove photo info › Zip". Shown on the dial under Custom.</summary>
    public sealed class Recipe
    {
        public string Name = "", Types = "*";
        public List<string> Steps = new List<string>();

        /// <summary>Types: * for anything, kinds (image, video, audio, pdf, text, folder…) or extensions (.png,.jpg).</summary>
        public bool Matches(string[] paths)
        {
            string[] tokens = Types.Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToArray();
            if (tokens.Length == 0 || tokens.Contains("*")) return true;
            if (paths == null || paths.Length == 0) return false;
            return paths.All(p =>
            {
                string kind = FileOps.Classify(p).ToString().ToLowerInvariant();
                return tokens.Contains(kind) || tokens.Contains(Path.GetExtension(p).ToLowerInvariant());
            });
        }
    }

    public static class RecipeSteps
    {
        /// <summary>Steps a recipe can use. "moveto"/"copyto" take a folder after a colon.</summary>
        public static readonly string[][] Choices =
        {
            new[] { "resize:1920", "Resize to 1920 px" }, new[] { "resize:1280", "Resize to 1280 px" }, new[] { "resize:50", "Resize to 50%" },
            new[] { "shrink", "Shrink" }, new[] { "strip", "Remove photo info (EXIF)" }, new[] { "rotate:right", "Rotate right" },
            new[] { "convert:jpg", "Convert to JPG" }, new[] { "convert:png", "Convert to PNG" }, new[] { "convert:webp", "Convert to WebP" },
            new[] { "convert:avif", "Convert to AVIF" }, new[] { "pdf", "Combine into a PDF" }, new[] { "zip", "Zip" },
            new[] { "copyhere", "Copy here" }, new[] { "media:mp4", "Video to MP4" }, new[] { "media:mp3", "Audio to MP3" },
            new[] { "webpdf", "Web page or text to PDF" }, new[] { "moveto", "Move to a folder…" }, new[] { "copyto", "Copy to a folder…" }
        };

        public static string Label(string step)
        {
            string id = step, folder = null;
            if (step.StartsWith("moveto:") || step.StartsWith("copyto:")) { id = step.Substring(0, 6); folder = step.Substring(7); }
            string[] c = Choices.FirstOrDefault(x => x[0] == id);
            string recorded;
            lock (RecipeRecorder.Labels) RecipeRecorder.Labels.TryGetValue(step, out recorded);
            string label = c != null ? L.T(c[1]).TrimEnd('…') : recorded ?? step.Replace(':', ' ');
            return folder == null ? label : label + " " + FileOps.NameOf(folder);
        }
    }

    public static class RecipeStore
    {
        public static string FilePath { get { return AppPaths.RoamingFile("recipes.ini"); } }

        public static List<Recipe> Load()
        {
            var list = new List<Recipe>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (line.TrimStart().StartsWith("#") || line.Trim().Length == 0) continue;
                    string[] p = line.Split('|').Select(x => x.Trim()).ToArray();
                    if (p.Length < 3 || p[0].Length == 0) continue;
                    var steps = p[2].Split('>').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    if (steps.Count == 0) continue;
                    list.Add(new Recipe { Name = p[0], Types = p[1].Length > 0 ? p[1] : "*", Steps = steps });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return list;
        }

        public static void Save(IList<Recipe> recipes)
        {
            var sb = new StringBuilder("# DragFlow recipes (edit them in Settings > Recipes)\r\n# name | file types | step > step > step\r\n");
            foreach (Recipe r in recipes)
                sb.Append(r.Name.Replace("|", "/")).Append(" | ").Append(r.Types).Append(" | ").Append(string.Join(" > ", r.Steps.ToArray())).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }

        public static Recipe Find(string name) { return Load().FirstOrDefault(r => r.Name == name); }
    }
}

namespace DragFlow
{
    /// <summary>
    /// "Save what I just did as a recipe": remembers the last actions (in memory only) and finds the chain that ends
    /// with the latest one, where each action worked on what the one before made.
    /// </summary>
    public static class RecipeRecorder
    {
        public sealed class Step
        {
            public string Id, Label;
            public string[] Inputs, Outputs;
            public DateTime At;
        }

        static readonly List<Step> recent = new List<Step>();
        const int Keep = 20;
        public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Actions that cannot be replayed safely or usefully as a recipe step: anything with a password, anything that
        /// sends files to the AI (a recipe can run from a folder rule, where nobody picks the files), other recipes
        /// (a recipe must never run itself), and ids holding the recipe file's separators.
        /// </summary>
        public static bool Recordable(string id)
        {
            return !(id.StartsWith("encrypt") || id.StartsWith("decrypt") || id == "recycle" || id == "compare" || id.StartsWith("ai:")
                     || id == "sharelink" || id.StartsWith("script:") || id == "listcontents" || id.StartsWith("recipe:")
                     || id.Contains("|") || id.Contains(">"));
        }

        /// <summary>Remember a finished action. Actions that made nothing (in-place edits, checks) cannot feed a next step, so they are left out.</summary>
        public static void Note(string id, string label, string[] inputs, IList<string> outputs)
        {
            if (!Recordable(id) || outputs == null || outputs.Count == 0) return;
            lock (recent)
            {
                recent.Add(new Step { Id = id, Label = label, Inputs = inputs.ToArray(), Outputs = outputs == null ? new string[0] : outputs.ToArray(), At = DateTime.Now });
                while (recent.Count > Keep) recent.RemoveAt(0);
            }
            lock (Labels) Labels[id] = label;
        }

        public static void Clear() { lock (recent) recent.Clear(); }

        /// <summary>Readable names for steps recorded this session (shown in Settings › Recipes).</summary>
        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>();

        /// <summary>The chain of actions ending with the latest one, oldest first (empty when nothing was done recently).</summary>
        public static List<Step> Suggest(DateTime now)
        {
            lock (recent)
            {
                var chain = new List<Step>();
                if (recent.Count == 0 || now - recent[recent.Count - 1].At > TimeSpan.FromHours(1)) return chain;
                chain.Add(recent[recent.Count - 1]);
                for (int i = recent.Count - 2; i >= 0; i--)
                {
                    Step prev = recent[i], next = chain[0];
                    if (next.At - prev.At > MaxGap) break;
                    var made = new HashSet<string>(prev.Outputs, StringComparer.OrdinalIgnoreCase);
                    if (made.Count == 0 || !next.Inputs.All(made.Contains)) break;
                    chain.Insert(0, prev);
                }
                return chain;
            }
        }

        /// <summary>The kinds of file the first step worked on ("image", "pdf", …), as recipe file types.</summary>
        public static string TypesFor(Step first)
        {
            var kinds = first.Inputs.Select(p => Directory.Exists(p) ? "folder" : FileOps.Classify(p).ToString().ToLowerInvariant()).Distinct().ToList();
            return kinds.Count == 0 || kinds.Contains("other") || kinds.Contains("mixed") ? "*" : string.Join(",", kinds.ToArray());
        }
    }
}
