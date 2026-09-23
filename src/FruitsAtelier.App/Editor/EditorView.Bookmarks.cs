using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Rect BookmarkToolbarBounds => new(overview.X + 8, overview.Y - 39, 246, 34);

    private void SeekBookmark(bool next)
    {
        var bookmarks = OsuTimeline.Bookmarks(Document);
        int target = next ? bookmarks.FirstOrDefault(t => t > playhead, -1) : bookmarks.LastOrDefault(t => t < playhead, -1);
        if (target >= 0) SeekTo(target);
    }

    private void ResetBookmarks() => Edit(L.Get("timeline.bookmark.reset"), () => OsuTimeline.ClearBookmarks(Document));

    private void DrawBookmarkToolbar(ICanvas c)
    {
        var panel = BookmarkToolbarBounds;
        var bridge = new Rect(overview.X, panel.Bottom, overview.Width, overview.Y - panel.Bottom);
        if (!(overview.Contains(mouseX, mouseY) || panel.Contains(mouseX, mouseY)
            || bridge.Contains(mouseX, mouseY)) || overview.Width < 260) return;
        string texture = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "bookmarks", "toolbar-panel.png");
        c.Fill(panel, 0x1D2732, 6);
        c.Image(texture, panel, source: new(40, 225, 2090, 224));
        var bookmarks = OsuTimeline.Bookmarks(Document);
        int current = (int)Math.Clamp(Math.Round(playhead), 0, int.MaxValue);
        bool[] enabled = [!bookmarks.Contains(current), bookmarks.Any(t => Math.Abs((long)t - current) < 2000),
            bookmarks.Any(t => t < playhead), bookmarks.Any(t => t > playhead), bookmarks.Count > 0];
        Action[] actions = [
            () => Edit(L.Get("timeline.bookmark.add"), () => OsuTimeline.AddBookmark(Document, current)),
            () => Edit(L.Get("timeline.bookmark.remove"), () => OsuTimeline.RemoveNearestBookmark(Document, current)),
            () => SeekBookmark(false), () => SeekBookmark(true), ResetBookmarks
        ];
        string[] hints = ["timeline.bookmark.addHint", "timeline.bookmark.removeHint", "timeline.bookmark.previousHint",
            "timeline.bookmark.nextHint", "timeline.bookmark.resetHint"];
        int hovered = -1;
        for (int i = 0; i < 5; i++)
        {
            var r = i < 4 ? new Rect(panel.X + 6 + i * 36, panel.Y + 5, 30, 24)
                : new Rect(panel.X + 150, panel.Y + 5, 90, 24);
            bool hover = r.Contains(mouseX, mouseY);
            c.Fill(r, hover ? 0x405065u : 0x19232Du, 4);
            uint ink = enabled[i] ? 0xF1F4F8u : 0x738191u;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (i is 0 or 1)
            {
                c.Circle(cx, cy, 9, ink);
                c.Line(cx - 4, cy, cx + 4, cy, 0x19232D, 2.5f);
                if (i == 0) c.Line(cx, cy - 4, cx, cy + 4, 0x19232D, 2.5f);
            }
            else if (i is 2 or 3)
            {
                float direction = i == 2 ? -1 : 1;
                c.Fill(new(cx + direction * 7 - 1.5f, cy - 8, 3, 16), ink);
                for (int row = -7; row <= 7; row++)
                {
                    float tip = cx + direction * 5;
                    float baseX = cx - direction * 5;
                    float edge = tip + (baseX - tip) * Math.Abs(row) / 7f;
                    c.Line(edge, cy + row, baseX, cy + row, ink, 1.2f);
                }
            }
            else
            {
                string label = L.Get("timeline.bookmark.resetLabel");
                float labelWidth = c.MeasureText(label, 12, true);
                float groupLeft = r.X + (r.Width - (18 + 6 + labelWidth)) / 2;
                cx = groupLeft + 9;
                c.Line(cx - 6, cy - 6, cx + 6, cy + 6, ink, 3);
                c.Line(cx + 6, cy - 6, cx - 6, cy + 6, ink, 3);
                float textX = groupLeft + 24;
                c.Text(label, textX, r.Y + 3, 12, ink, r.Right - textX - 4, true);
            }
            hits.Add(new(r, actions[i], true));
            if (hover) hovered = i;
        }
        if (hovered >= 0)
        {
            string hint = L.Get(hints[hovered]);
            float w = Math.Min(360, c.MeasureText(hint, 11) + 16);
            float x = Math.Clamp(mouseX - w / 2, overview.X, overview.Right - w);
            float y = panel.Y - 27;
            c.Fill(new(x, y, w, 23), 0x111923, 4);
            c.Text(hint, x + 8, y + 5, 11, Foreground, w - 16);
        }
    }
}
