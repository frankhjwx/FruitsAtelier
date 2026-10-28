using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private double? bookmarkHoverAt;
    private bool bookmarkToolbarVisible;
    private float bookmarkToolbarPaintedOpacity;
    private readonly List<HitArea> bookmarkToolbarHits = [];
    private Rect BookmarkToolbarArea => new(canvas.X, canvas.Y, Math.Max(0, width - canvas.X - 16), Math.Max(0, overview.Bottom - canvas.Y));
    private Rect BookmarkToolbarOrigin => new(overview.X + 8, overview.Y - 39, 262, 34);
    internal Rect BookmarkToolbarBounds => PositionedOverlay(BookmarkToolbarOrigin,
        LibrarySettings.BookmarkToolbarOffsetX, 0, BookmarkToolbarArea);
    internal Rect BookmarkToolbarGrip => new(BookmarkToolbarBounds.X, BookmarkToolbarBounds.Y, 14, 34);
    private bool CanShowBookmarkToolbar => !OverviewTimelineCollapsed && overview.Width >= 276 && CanDropBackground
        && !TimingPageVisible && !TimeJumpVisible && !IsEditingText && menu < 0 && contextItems.Count == 0;
    private bool BookmarkToolbarVisible
    {
        get
        {
            var panel = BookmarkToolbarBounds;
            bool timeline = overview.Contains(mouseX, mouseY);
            bool retained = bookmarkToolbarVisible && (panel.Contains(mouseX, mouseY)
                || new Rect(Math.Min(panel.X, overview.X), Math.Min(panel.Bottom, overview.Y),
                    Math.Max(panel.Right, overview.Right) - Math.Min(panel.X, overview.X), Math.Abs(overview.Y - panel.Bottom)).Contains(mouseX, mouseY));
            if (!CanShowBookmarkToolbar)
            { bookmarkHoverAt = null; return bookmarkToolbarVisible = false; }
            if (overlayDragging == 1 || retained) return bookmarkToolbarVisible = true;
            if (!timeline) { bookmarkHoverAt = null; return bookmarkToolbarVisible = false; }
            bookmarkHoverAt ??= CanvasDimNowMs;
            return bookmarkToolbarVisible = CanvasDimNowMs - bookmarkHoverAt.Value > 250;
        }
    }
    internal float BookmarkToolbarOpacity => BookmarkToolbarVisible && bookmarkHoverAt is { } at
        ? (float)Math.Clamp((CanvasDimNowMs - at - 250) / 250, 0, 1) : 0;
    public bool BookmarkToolbarNeedsRedraw
    {
        get
        {
            float opacity = BookmarkToolbarOpacity;
            return CanShowBookmarkToolbar && (opacity != bookmarkToolbarPaintedOpacity || opacity > 0 && opacity < 1);
        }
    }
    public bool BookmarkToolbarHoverPending => bookmarkHoverAt is not null && !bookmarkToolbarVisible;

    private bool BookmarkToolbarPointerDown(float x, float y, int button)
    {
        if (!BookmarkToolbarVisible || !BookmarkToolbarBounds.Contains(x, y)) return false;
        if (button == 0)
            for (int i = bookmarkToolbarHits.Count - 1; i >= 0; i--)
                if (bookmarkToolbarHits[i].Bounds.Contains(x, y))
                { if (bookmarkToolbarHits[i].Enabled) bookmarkToolbarHits[i].Action(); break; }
        return true;
    }

    private void UpdateBookmarkHover(float x, float y)
    {
        mouseX = x; mouseY = y;
        _ = BookmarkToolbarVisible;
    }

    private void SeekBookmark(bool next)
    {
        var bookmarks = OsuTimeline.Bookmarks(Document);
        int target = next ? bookmarks.FirstOrDefault(t => t > playhead, -1) : bookmarks.LastOrDefault(t => t < playhead, -1);
        if (target >= 0) SeekTo(target);
    }

    private void ResetBookmarks() => Edit(L.Get("timeline.bookmark.reset"), () => OsuTimeline.ClearBookmarks(Document));

    private void DrawBookmarkToolbar(ICanvas c)
    {
        bookmarkToolbarHits.Clear();
        var panel = BookmarkToolbarBounds;
        float opacity = bookmarkToolbarPaintedOpacity = BookmarkToolbarOpacity;
        if (opacity <= 0) return;
        string texture = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "bookmarks", "toolbar-panel.png");
        c.Fill(panel, Chrome(0x1D2732, Panel), 6, opacity: opacity);
        if (UsesGraphiteTheme) c.Image(texture, panel, source: new(40, 225, 2090, 224), opacity: opacity);
        DrawOverlayGrip(c, BookmarkToolbarGrip, opacity);
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
            var r = i < 4 ? new Rect(panel.X + 22 + i * 36, panel.Y + 5, 30, 24)
                : new Rect(panel.X + 166, panel.Y + 5, 90, 24);
            bool hover = r.Contains(mouseX, mouseY);
            c.Fill(r, hover ? Chrome(0x405065, HoverSurface) : Chrome(0x19232D, Background), 4, opacity: opacity);
            uint ink = enabled[i] ? Chrome(0xF1F4F8, Foreground) : Chrome(0x738191, Muted);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (i is 0 or 1)
            {
                c.Circle(cx, cy, 9, ink, opacity: opacity);
                c.Line(cx - 4, cy, cx + 4, cy, Chrome(0x19232D, Background), 2.5f, opacity: opacity);
                if (i == 0) c.Line(cx, cy - 4, cx, cy + 4, Chrome(0x19232D, Background), 2.5f, opacity: opacity);
            }
            else if (i is 2 or 3)
            {
                float direction = i == 2 ? -1 : 1;
                c.Fill(new(cx + direction * 7 - 1.5f, cy - 8, 3, 16), ink, opacity: opacity);
                for (int row = -7; row <= 7; row++)
                {
                    float tip = cx + direction * 5;
                    float baseX = cx - direction * 5;
                    float edge = tip + (baseX - tip) * Math.Abs(row) / 7f;
                    c.Line(edge, cy + row, baseX, cy + row, ink, 1.2f, opacity: opacity);
                }
            }
            else
            {
                string label = L.Get("timeline.bookmark.resetLabel");
                float labelWidth = c.MeasureText(label, 12, true);
                float groupLeft = r.X + (r.Width - (18 + 6 + labelWidth)) / 2;
                cx = groupLeft + 9;
                c.Line(cx - 6, cy - 6, cx + 6, cy + 6, ink, 3, opacity: opacity);
                c.Line(cx + 6, cy - 6, cx - 6, cy + 6, ink, 3, opacity: opacity);
                float textX = groupLeft + 24;
                c.TextOpacity(label, textX, r.Y + 3, 12, ink, r.Right - textX - 4, true, opacity);
            }
            var hit = new HitArea(r, actions[i], true);
            bookmarkToolbarHits.Add(hit);
            hits.Add(hit);
            if (hover) hovered = i;
        }
        if (hovered >= 0)
        {
            string hint = L.Get(hints[hovered]);
            float w = Math.Min(BookmarkToolbarArea.Width, Math.Min(360, c.MeasureText(hint, 11) + 16));
            float x = Math.Clamp(mouseX - w / 2, BookmarkToolbarArea.X, BookmarkToolbarArea.Right - w);
            float y = panel.Y - 27 >= BookmarkToolbarArea.Y ? panel.Y - 27 : panel.Bottom + 4;
            c.Fill(new(x, y, w, 23), Chrome(0x111923, Background), 4, opacity: opacity);
            c.TextOpacity(hint, x + 8, y + 5, 11, Foreground, w - 16, false, opacity);
        }
    }
}
