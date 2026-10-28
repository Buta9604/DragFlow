using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragFlow
{
    /// <summary>The Tier 6 parts of the dial: the Smart slice, AI actions, protection, scripts.</summary>
    public static partial class ActionCatalog
    {
        const string GSmart = "", GLock = "";

        /// <summary>AI actions for this kind of file, when the AI helper is on ("AI" in every label: these send the file).</summary>
        static List<ActionItem> AiKids(FileKind kind, bool single)
        {
            var kids = new List<ActionItem>();
            if (!AiClient.Ready) return kids;
            if (kind == FileKind.Image)
            {
                kids.Add(Kid("ai:describe", "AI describe", Indigo));
                kids.Add(Kid("ai:name", "AI name", Indigo));
                kids.Add(Kid("ai:table", "AI table", Indigo));
            }
            else
            {
                kids.Add(Kid("ai:summary", "AI summary", Indigo));
                kids.Add(Kid("ai:name", "AI name", Indigo));
                kids.Add(Kid("ai:translate", "AI translate…", Indigo));
                if (kind == FileKind.Pdf || kind == FileKind.Word) kids.Add(Kid("ai:table", "AI table", Indigo));
            }
            kids.Add(Kid("ai:ask", "Ask AI…", Indigo));
            if (!single || kind != FileKind.Image) kids.Add(Kid("ai:email", "AI e-mail", Indigo));
            return kids;
        }

        /// <summary>The Smart slice: names from content, numbering and look-alikes for pictures, and the AI actions.</summary>
        static ActionItem Smart(MenuContext ctx, FileKind kind, bool single, params ActionItem[] first)
        {
            var g = new ActionItem("", "Smart", GSmart, Indigo).With(first);
            bool namable = kind == FileKind.Image || kind == FileKind.Pdf || kind == FileKind.Word;
            if (namable) g.Children.Add(Kid("smartname", "Smart name", Indigo));
            if (kind == FileKind.Image && !single) g.Children.Add(Kid("numberdate", "Number by date", Indigo));
            if (kind == FileKind.Image && !single) g.Children.Add(Kid("similar", "Similar photos", Indigo));
            if (!ctx.Unknown && ctx.Paths.Length <= AiClient.MaxFiles) g.Children.AddRange(AiKids(kind, single));
            return g;
        }

        /// <summary>For folders: exact copies and, with pictures inside, look-alikes and numbering by date.</summary>
        static ActionItem Duplicates(bool images)
        {
            if (!images) return Item("dupes", "Duplicates", GDupes, Pink);
            return Group("Duplicates", GDupes, Pink, Kid("dupes", "Exact copies", Pink), Kid("similar", "Similar photos", Pink), Kid("numberdate", "Number by date", Pink));
        }

        /// <summary>Encrypted files lead with Decrypt.</summary>
        static void AddDecrypt(List<ActionItem> items, MenuContext ctx)
        {
            if (ctx.Unknown || !ctx.Paths.All(p => p.EndsWith(Protect.Extension, StringComparison.OrdinalIgnoreCase))) return;
            items.Insert(0, Item("decrypt", "Decrypt", GLock, Green));
            while (items.Count > MaxSlices) items.RemoveAt(items.Count - 2); // keep More last
        }

        /// <summary>Scripts from the scripts folder that match, as Custom options.</summary>
        static IEnumerable<ActionItem> ScriptKids(string[] paths)
        {
            return Scripts.Load().Where(a => a.Matches(paths)).Select(a => Kid("script:" + a.Name, a.Name, Amber));
        }
    }
}
