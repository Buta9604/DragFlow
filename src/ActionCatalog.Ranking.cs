using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace DragFlow
{
    /// <summary>Profiles, hidden and pinned actions, and ranking by use.</summary>
    public static partial class ActionCatalog
    {
        static bool IsTail(ActionItem i) { return i.Key == "More" || i.Key == "Custom"; }

        /// <summary>Score of a slice: its own uses plus those of its options.</summary>
        static double ScoreOf(ActionItem i, FileKind kind, string folder, Usage usage)
        {
            if (!i.IsParent) return usage.Score(kind, folder, i.Id);
            return i.Children.Sum(c => usage.Score(kind, folder, c.Id));
        }

        /// <summary>
        /// Order the dial: a folder profile's actions first, then pinned ones, then what you use most for this kind of
        /// file from this folder (only once it has been used a few times), then the rest as designed. "More" and
        /// "Custom" stay last. Options inside a slice are ordered by use too.
        /// </summary>
        public static void Arrange(List<ActionItem> items, MenuContext ctx, Profile profile, Settings settings, Usage usage)
        {
            if (profile != null)
            {
                items.RemoveAll(i => profile.Hide.Any(i.Matches));
                foreach (ActionItem i in items) i.Children.RemoveAll(c => profile.Hide.Any(c.Matches));
                items.RemoveAll(i => i.Id == "" && i.Children.Count == 0);
            }
            if (settings.HiddenActions.Count > 0)
            {
                items.RemoveAll(i => settings.HiddenActions.Any(i.Matches));
                foreach (ActionItem i in items) i.Children.RemoveAll(c => settings.HiddenActions.Any(c.Matches));
                items.RemoveAll(i => i.Id == "" && i.Children.Count == 0);
            }
            FileKind kind = ctx.KindHint ?? ctx.Kind;
            string folder = ctx.Folder;

            var tail = items.Where(IsTail).ToList();
            var body = items.Where(i => !IsTail(i)).ToList();
            var ordered = new List<ActionItem>();
            Action<IEnumerable<string>> takeMatching = delegate (IEnumerable<string> keys)
            {
                foreach (string key in keys)
                {
                    ActionItem hit = body.FirstOrDefault(i => i.Matches(key) || i.Children.Any(c => c.Matches(key)));
                    if (hit != null) { ordered.Add(hit); body.Remove(hit); }
                }
            };
            if (profile != null) takeMatching(profile.First);
            takeMatching(settings.PinnedActions);

            if (settings.Ranking && usage != null)
            {
                var scored = body.Select((it, idx) => new { it, idx, s = ScoreOf(it, kind, folder, usage) }).ToList();
                var strong = scored.Where(x => x.s >= SliceRankThreshold).OrderByDescending(x => x.s).ThenBy(x => x.idx).Select(x => x.it).ToList();
                ordered.AddRange(strong);
                body = body.Where(b => !strong.Contains(b)).ToList();

                foreach (ActionItem g in items.Where(i => i.IsParent))
                {
                    var kids = g.Children;
                    var fixedFirst = kids.Where(k => k.Id == "park").ToList();
                    var fixedLast = kids.Where(k => k.Id == "movechoose").ToList();
                    var middle = kids.Where(k => k.Id != "park" && k.Id != "movechoose")
                        .Select((k, idx) => new { k, idx, s = usage.Score(kind, folder, k.Id) })
                        .OrderByDescending(x => x.s >= ChipRankThreshold ? x.s : 0).ThenBy(x => x.idx).Select(x => x.k).ToList();
                    kids.Clear();
                    kids.AddRange(fixedFirst); kids.AddRange(middle); kids.AddRange(fixedLast);
                }
            }
            ordered.AddRange(body);
            ordered.AddRange(tail);
            items.Clear();
            items.AddRange(ordered.Take(MaxSlices));
        }
    }
}
