using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Rect bookmarkPanelBounds;
    private readonly List<(int Time, Rect Bounds)> bookmarkTimeHits = [];
    private readonly List<(int Time, Rect Bounds)> bookmarkColourHits = [], bookmarkIntervalHits = [];
    private Rect bookmarkColourPopupBounds;
    private double bookmarkPickerHue;
    private int? bookmarkPickerHueTime;
    private float bookmarkPanelScroll, bookmarkPanelContentHeight;
    private int? bookmarkEditingTime, bookmarkColourTime;
    private (int From, int To)? bookmarkCommittedMove;
    private bool bookmarkEditingHex;
    private string bookmarkText = "", bookmarkError = "";
    private bool BookmarkPanelAvailable => DockContentInputAvailable && PanelVisible(DockPanel.Bookmarks);
    private bool BookmarkPanelFocused => BookmarkPanelAvailable && bookmarkEditingTime is not null;
    private string BookmarkInputKey => $"bookmark:{bookmarkEditingTime}:{bookmarkEditingHex}";
    internal Rect BookmarkPanelViewport => bookmarkPanelBounds;
    internal Rect BookmarkTimeBounds(int time) => bookmarkTimeHits.FirstOrDefault(hit => hit.Time == time).Bounds;
    internal Rect BookmarkColourBounds(int time) => bookmarkColourHits.FirstOrDefault(hit => hit.Time == time).Bounds;
    internal Rect BookmarkIntervalBounds(int time) => bookmarkIntervalHits.FirstOrDefault(hit => hit.Time == time).Bounds;
    public Action? RequestPasteBookmarkColour { get; set; }
    public int BookmarkColourPasteSession { get; private set; }
    private (EditorHistory Owner, long Revision, int Time)? bookmarkColourPaste;

    public void PasteBookmarkColour(string text, int session)
    {
        if (session != BookmarkColourPasteSession || bookmarkColourPaste is not { } target) return;
        bookmarkColourPaste = null;
        if (!ReferenceEquals(history, target.Owner) || history.ContentRevision != target.Revision
            || !BookmarkPanelAvailable || notesLocked || !AxisBookmarks().Contains(target.Time)) return;
        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { bookmarkError = L.Get("song.invalidHex"); return; }
        SetBookmarkPickerColour(target.Time, colour);
        bookmarkError = "";
    }

    internal void ShowBookmarkPanel()
    {
        if (!CommitField()) return;
        AddDockPanel(DockPanel.Bookmarks, false);
        var group = PanelGroup(DockPanel.Bookmarks);
        group.Active = DockPanel.Bookmarks; group.Collapsed = false;
        menu = -1; contextItems.Clear();
    }

    private void FocusBookmarkText(int time, bool hex = false)
    {
        if (!CommitField()) return;
        time = ResolveBookmarkTime(time);
        if (time < 0) return;
        bookmarkEditingTime = time; bookmarkEditingHex = hex; bookmarkError = "";
        bookmarkText = hex ? $"#{Document.BookmarkAppearanceAt(time).Colour:X6}" : Time(time);
        SelectInput(BookmarkInputKey, bookmarkText);
    }

    private bool CommitBookmarkText()
    {
        if (bookmarkEditingTime is not { } original) return true;
        if (!AxisBookmarks().Contains(original) || notesLocked)
        { bookmarkEditingTime = null; bookmarkError = ""; return true; }
        if (bookmarkEditingHex)
        {
            string hex = bookmarkText.Trim().TrimStart('#');
            if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
            { bookmarkError = L.Get("song.invalidHex"); return false; }
            Edit(L.Get("bookmark.colour"), () => OsuTimeline.SetBookmarkAppearance(Document, original,
                Document.BookmarkAppearanceAt(original) with { Colour = colour }));
        }
        else
        {
            double value;
            string input = bookmarkText.Trim();
            if (input.Contains(':'))
            {
                var parts = input.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[0], out int minutes) || minutes < 0
                    || !int.TryParse(parts[1], out int seconds) || seconds is < 0 or >= 60
                    || !int.TryParse(parts[2], out int ms) || ms is < 0 or >= 1000)
                { bookmarkError = L.Get("editor.error.timestampRequired"); return false; }
                value = minutes * 60000d + seconds * 1000 + ms;
            }
            else if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) value = double.NaN;
            if (!double.IsFinite(value) || value < 0 || value > int.MaxValue
                || (int)Math.Round(value) != original && AxisBookmarks().Contains((int)Math.Round(value)))
            { bookmarkError = L.Get("bookmark.invalid"); return false; }
            int target = (int)Math.Round(value);
            if (!Edit(L.Get("bookmark.title"), () => OsuTimeline.MoveBookmark(Document, original, target)))
            { bookmarkError = StatusMessage; return false; }
            bookmarkCommittedMove = (original, target);
            if (bookmarkColourTime == original) bookmarkColourTime = target;
        }
        bookmarkEditingTime = null; bookmarkError = "";
        return true;
    }

    private void BookmarkText(string text)
    {
        if (!BookmarkPanelFocused || notesLocked) return;
        string filtered = new(text.Where(c => char.IsAsciiDigit(c) || (bookmarkEditingHex ? char.IsAsciiHexDigit(c) || c == '#' : c is ':' or '.' or '-' or '+' or 'e' or 'E')).ToArray());
        bookmarkText = InsertInput(BookmarkInputKey, bookmarkText, filtered, 30);
        bookmarkError = "";
    }

    private void BookmarkKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { if (bookmarkEditingHex) bookmarkColourTime = null; bookmarkEditingTime = null; bookmarkError = ""; return; }
        if (key is 13 or 9) { CommitBookmarkText(); return; }
        InputKey(BookmarkInputKey, ref bookmarkText, key, ctrl, shift, 30, RequestPasteField);
    }

    private void AddPanelBookmark(int time)
    {
        if (notesLocked || !CommitField()) return;
        if (!AxisBookmarks().Contains(time)) Edit(L.Get("timeline.bookmark.add"), () => OsuTimeline.AddBookmark(Document, time));
        RevealBookmark(time);
        FocusBookmarkText(time);
    }

    private int ResolveBookmarkTime(int time)
    {
        var marks = AxisBookmarks();
        if (marks.Contains(time)) return time;
        return bookmarkCommittedMove is { } move && move.From == time && marks.Contains(move.To) ? move.To : -1;
    }

    private void RevealBookmark(int time)
    {
        int index = UpperBound(AxisBookmarks(), time) - 1;
        float row = BookmarkPanelRowHeight;
        bookmarkPanelScroll = Math.Max(0, index * row - Math.Max(0, bookmarkPanelBounds.Height - 190) / 2);
    }

    private int? BookmarkInsertionTime(int time, bool above)
    {
        var marks = AxisBookmarks();
        int index = UpperBound(marks, time) - 1;
        if (index < 0) return null;
        long low = above ? index > 0 ? marks[index - 1] : 0 : time;
        long high = above ? time : index + 1 < marks.Count ? marks[index + 1] : Math.Max((long)time + 1000, (long)Math.Min(int.MaxValue, TimelineDurationMs));
        high = Math.Min(high, int.MaxValue);
        if (above && index == 0) return time > 0 ? (int)(low + (high - low) / 2) : null;
        return high - low >= 2 ? (int)(low + (high - low) / 2) : null;
    }

    private float BookmarkPanelRowHeight => bookmarkPanelBounds.Width < 280 ? 78 : 42;

    private void SetAllBookmarkIntervals(bool enabled)
    {
        if (notesLocked || !CommitField()) return;
        var marks = AxisBookmarks();
        Edit(L.Get(enabled ? "bookmark.enableIntervals" : "bookmark.disableIntervals"), () =>
        {
            for (int i = 0; i < marks.Count; i++)
            {
                var appearance = Document.BookmarkAppearanceAt(marks[i]) with { ShowInterval = enabled && i + 1 < marks.Count };
                if (appearance == MapDocument.DefaultBookmarkAppearance) Document.BookmarkAppearances.Remove(marks[i]);
                else Document.BookmarkAppearances[marks[i]] = appearance;
            }
        });
    }

    private void NewPanelBookmark()
    {
        int free = (int)Math.Clamp(Math.Round(playhead), 0, int.MaxValue);
        while (free < int.MaxValue && AxisBookmarks().Contains(free)) free++;
        AddPanelBookmark(free);
    }

    private void DrawBookmarkPanel(ICanvas c, Rect bounds)
    {
        bookmarkPanelBounds = bounds; bookmarkTimeHits.Clear(); bookmarkColourHits.Clear(); bookmarkIntervalHits.Clear();
        bookmarkColourPopupBounds = default;
        if (!BookmarkPanelAvailable || bounds.Width < 1 || bounds.Height < 1) return;
        var marks = AxisBookmarks();
        bool compact = bounds.Width < 280;
        float y = bounds.Y;
        int firstHit = hits.Count;
        c.Clip(bounds);
        float deleteX = bounds.Right - 24, jumpX = deleteX - 54;
        float toggleX = jumpX - 42, colourX = compact ? bounds.X : toggleX - 36;
        c.Text(L.Get("ui.timeField"), bounds.X, y, 11, Muted, compact ? bounds.Width : colourX - bounds.X - 8);
        if (!compact)
        {
            c.Text(L.Get("bookmark.colour"), colourX, y, 11, Muted, 34);
            c.Text(L.Get("bookmark.interval"), toggleX, y, 11, Muted, 38);
        }
        float bodyTop = y + 22, bodyHeight = Math.Max(0, bounds.Bottom - bodyTop - 100);
        var body = new Rect(bounds.X, bodyTop, bounds.Width, bodyHeight);
        float rowHeight = BookmarkPanelRowHeight;
        bookmarkPanelContentHeight = marks.Count * rowHeight + 38;
        bookmarkPanelScroll = Math.Clamp(bookmarkPanelScroll, 0, Math.Max(0, bookmarkPanelContentHeight - bodyHeight));
        c.Clip(body);
        int first = (int)(bookmarkPanelScroll / rowHeight);
        int last = Math.Min(marks.Count, (int)Math.Ceiling((bookmarkPanelScroll + bodyHeight) / rowHeight));
        for (int i = first; i < last; i++)
        {
            int time = marks[i];
            var appearance = Document.BookmarkAppearanceAt(time);
            float top = bodyTop + i * rowHeight - bookmarkPanelScroll;
            var timeBox = new Rect(bounds.X, top + 3, compact ? bounds.Width : colourX - bounds.X - 8, 30);
            bool focused = bookmarkEditingTime == time && !bookmarkEditingHex;
            c.Fill(timeBox, Surface, 3); c.Stroke(timeBox, focused ? bookmarkError.Length > 0 ? Error : Accent : Grid, radius: 3);
            DrawInputText(c, new(timeBox.X + 6, timeBox.Y + 7, timeBox.Width - 12, 18), focused ? bookmarkText : Time(time), 12, focused, $"bookmark:{time}:False");
            if (timeBox.Y >= body.Y && timeBox.Bottom <= body.Bottom)
            {
                bookmarkTimeHits.Add((time, timeBox));
                hits.Add(new(timeBox, () => FocusBookmarkText(time), !notesLocked));
            }
            float colourY = compact ? top + 41 : top + 4;
            var colourBox = new Rect(colourX, colourY, 28, 28);
            c.Fill(colourBox, appearance.Colour, 3); c.Stroke(colourBox, Grid, radius: 3);
            hits.Add(new(colourBox, () =>
            {
                if (!CommitField()) return;
                int target = ResolveBookmarkTime(time);
                if (target >= 0) bookmarkColourTime = bookmarkColourTime == target ? null : target;
            }, !notesLocked));
            bool hasNext = i + 1 < marks.Count;
            var toggle = new Rect(toggleX, colourY, 34, 28);
            bool enabled = hasNext && !notesLocked;
            var track = new Rect(toggle.X, toggle.Y + 4, 34, 20);
            bool active = appearance.ShowInterval && hasNext;
            c.Fill(track, active ? Accent : Grid, 10, enabled ? 1 : .35f);
            c.Circle(track.X + (active ? 24 : 10), track.Y + 10, 7, Foreground, opacity: enabled ? 1 : .35f);
            hits.Add(new(toggle, () =>
            {
                if (!CommitField()) return;
                int target = ResolveBookmarkTime(time);
                if (target >= 0) Edit(L.Get("bookmark.showInterval"), () => OsuTimeline.SetBookmarkAppearance(Document, target,
                    Document.BookmarkAppearanceAt(target) with { ShowInterval = !Document.BookmarkAppearanceAt(target).ShowInterval }));
            }, enabled));
            Button(c, new(jumpX, colourY, 48, 28), L.Get("bookmark.jump"), () =>
            { int target = ResolveBookmarkTime(time); if (target >= 0) SeekTo(target); }, fontSize: 11, textPadding: 7);
            var delete = new Rect(deleteX, colourY, 24, 28);
            c.Fill(delete, delete.Contains(mouseX, mouseY) ? HoverSurface : Surface, 4);
            float cx = delete.X + 12, cy = delete.Y + 14;
            c.Line(cx - 4, cy - 4, cx + 4, cy + 4, Error, 1.8f, notesLocked ? .35f : 1);
            c.Line(cx - 4, cy + 4, cx + 4, cy - 4, Error, 1.8f, notesLocked ? .35f : 1);
            hits.Add(new(delete, () =>
            {
                if (!CommitField()) return;
                int target = ResolveBookmarkTime(time);
                if (target >= 0) Edit(L.Get("timeline.bookmark.remove"), () => OsuTimeline.ToggleBookmark(Document, target));
                if (bookmarkColourTime == target) bookmarkColourTime = null;
            }, !notesLocked));
            if (colourBox.Y >= body.Y && colourBox.Bottom <= body.Bottom)
            { bookmarkColourHits.Add((time, colourBox)); bookmarkIntervalHits.Add((time, toggle)); }
            c.Line(bounds.X, top + rowHeight - 1, bounds.Right, top + rowHeight - 1, Grid);
        }
        float addY = bodyTop + marks.Count * rowHeight - bookmarkPanelScroll + 4;
        if (addY < body.Bottom && addY + 32 > body.Y)
            Button(c, new(bounds.X, addY, bounds.Width, 32), L.Get("bookmark.new"), NewPanelBookmark, enabled: !notesLocked, fontSize: 12);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
            if (hits[i].Bounds.Y < body.Y || hits[i].Bounds.Bottom > body.Bottom) hits.RemoveAt(i);
        if (bookmarkPanelContentHeight > bodyHeight && bodyHeight > 0)
        {
            float thumb = Math.Max(18, bodyHeight * bodyHeight / bookmarkPanelContentHeight);
            c.Fill(new(bounds.Right + 4, bodyTop + bookmarkPanelScroll / (bookmarkPanelContentHeight - bodyHeight) * (bodyHeight - thumb), 3, thumb), Muted, 1);
        }
        c.Text(bookmarkError.Length > 0 ? bookmarkError : L.Get("bookmark.count", marks.Count), bounds.X, bounds.Bottom - 94, 11, bookmarkError.Length > 0 ? Error : Muted, bounds.Width);
        Button(c, new(bounds.X, bounds.Bottom - 68, bounds.Width, 32), L.Get("bookmark.enableIntervals"), () => SetAllBookmarkIntervals(true), enabled: !notesLocked && marks.Count > 1, fontSize: 11);
        Button(c, new(bounds.X, bounds.Bottom - 32, bounds.Width, 32), L.Get("bookmark.disableIntervals"), () => SetAllBookmarkIntervals(false), enabled: !notesLocked && marks.Count > 0, fontSize: 11);
        c.Unclip();
        if (bookmarkColourTime is { } selected && marks.Contains(selected)) DrawBookmarkColourPicker(c, bounds, selected);
        else if (contextItems.Count == 0)
        {
            foreach (bool enable in new[] { true, false })
            {
                var button = new Rect(bounds.X, bounds.Bottom - (enable ? 68 : 32), bounds.Width, 32);
                string label = L.Get(enable ? "bookmark.enableIntervals" : "bookmark.disableIntervals");
                if (button.Contains(mouseX, mouseY) && c.MeasureText(label, 11) > bounds.Width - 15)
                    DrawPaletteTooltip(c, label, button, left: true, above: true);
            }
        }
    }

    private void DrawBookmarkColourPicker(ICanvas c, Rect bounds, int time)
    {
        float h = Math.Min(242, bounds.Height), w = bounds.Width;
        var popup = new Rect(bounds.X, bounds.Bottom - h, w, h);
        bookmarkColourPopupBounds = popup;
        c.Fill(popup, Panel, 5); c.Stroke(popup, Grid, radius: 5);
        uint colour = Document.BookmarkAppearanceAt(time).Colour;
        var hsv = ColourToHsv(colour, bookmarkPickerHueTime == time ? bookmarkPickerHue : 0);
        bookmarkPickerHueTime = time; bookmarkPickerHue = hsv.Hue;
        c.Text(L.Get("bookmark.colourAt", Time(time)), popup.X + 8, popup.Y + 8, 12, Foreground, w - 52);
        Button(c, new(popup.Right - 36, popup.Y + 3, 32, 28), "×", () => { if (CommitField()) bookmarkColourTime = null; });
        var chart = new Rect(popup.X + 8, popup.Y + 38, Math.Max(1, w - 16), Math.Max(16, h - 122));
        DrawColourChart(c, chart, hsv.Hue, hsv.Saturation, hsv.Value);
        var hue = new Rect(chart.X, chart.Bottom + 10, chart.Width, 18);
        DrawHueTrack(c, hue, hsv.Hue);
        var hex = new Rect(chart.X, hue.Bottom + 10, chart.Width, 30);
        c.Fill(hex, Surface, 3); c.Stroke(hex, bookmarkError.Length > 0 ? Error : Grid, radius: 3);
        bool focused = bookmarkEditingTime == time && bookmarkEditingHex;
        DrawInputText(c, new(hex.X + 6, hex.Y + 7, hex.Width - 12, 18), focused ? bookmarkText : $"#{colour:X6}", 12, focused, $"bookmark:{time}:True");
        if (bookmarkError.Length > 0) c.Text(bookmarkError, popup.X + 8, popup.Bottom - 14, 11, Error, popup.Width - 16);
        hits.Add(new(popup, () => { }, true));
        hits.Add(new(chart, () => SetBookmarkPickerColour(time, SongHsv(hsv.Hue,
            Math.Clamp((mouseX - chart.X) / chart.Width, 0, 1), 1 - Math.Clamp((mouseY - chart.Y) / chart.Height, 0, 1))), !notesLocked));
        hits.Add(new(hue, () =>
        {
            if (!CommitField()) return;
            bookmarkPickerHueTime = time;
            bookmarkPickerHue = Math.Clamp((mouseX - hue.X) / hue.Width, 0, 1) * 359.99;
            SetBookmarkPickerColour(time, SongHsv(bookmarkPickerHue, hsv.Saturation, hsv.Value));
        }, !notesLocked));
        hits.Add(new(hex, () => FocusBookmarkText(time, true), !notesLocked));
        // The popup owns its full input region, including the close button above its blocker.
        hits.Add(new(new(popup.Right - 36, popup.Y + 3, 32, 28), () => { if (CommitField()) bookmarkColourTime = null; }, true));
    }

    private void SetBookmarkPickerColour(int time, uint colour)
    {
        if (CommitField()) Edit(L.Get("bookmark.colour"), () => OsuTimeline.SetBookmarkAppearance(Document, time,
            Document.BookmarkAppearanceAt(time) with { Colour = colour }));
    }

    private bool BookmarkPanelPointer(float x, float y, int button)
    {
        if (!BookmarkPanelAvailable || menu >= 0 || contextItems.Count > 0) return false;
        if (!bookmarkPanelBounds.Contains(x, y)) return !CommitBookmarkText();
        if (button == 2)
        {
            if (bookmarkColourPopupBounds.Contains(x, y)) return true;
            var colour = bookmarkColourHits.FirstOrDefault(hit => hit.Bounds.Contains(x, y));
            if (colour.Bounds.Width > 0)
            {
                if (!CommitField()) return true;
                int time = ResolveBookmarkTime(colour.Time);
                if (time < 0) return true;
                contextItems.Clear(); menu = -1;
                contextItems.Add(new(L.Get("bookmark.copyColour"), () => RequestCopyText?.Invoke($"#{Document.BookmarkAppearanceAt(time).Colour:X6}")));
                contextItems.Add(new(L.Get("bookmark.pasteColour"), () =>
                {
                    bookmarkColourPaste = (history, history.ContentRevision, time);
                    BookmarkColourPasteSession++;
                    RequestPasteBookmarkColour?.Invoke();
                }, Enabled: !notesLocked && RequestPasteBookmarkColour is not null));
                contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 240)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 240, ContextMenuHeight);
                return true;
            }
            var row = bookmarkTimeHits.FirstOrDefault(hit => hit.Bounds.Contains(x, y));
            if (row.Bounds.Width <= 0 || notesLocked || !CommitField()) return true;
            int anchor = ResolveBookmarkTime(row.Time);
            if (anchor < 0) return true;
            contextItems.Clear(); menu = -1;
            foreach (bool above in new[] { true, false })
            {
                int? insert = BookmarkInsertionTime(anchor, above);
                contextItems.Add(new(L.Get(above ? "bookmark.insertAbove" : "bookmark.insertBelow"),
                    () => { if (insert is { } target) AddPanelBookmark(target); }, Enabled: insert is not null));
            }
            contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 260)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 260, ContextMenuHeight);
            return true;
        }
        if (button == 0)
        {
            if (!CommitField()) return true;
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return true; }
        }
        return true;
    }

    private bool WheelBookmarkPanel(float x, float y, float delta)
    {
        if (!BookmarkPanelAvailable || !bookmarkPanelBounds.Contains(x, y)) return false;
        if (CommitField()) { bookmarkColourTime = null; bookmarkPanelScroll = Math.Max(0, bookmarkPanelScroll - delta / 120 * BookmarkPanelRowHeight); }
        return true;
    }

    private void DrawBookmarkIntervals(ICanvas c)
    {
        if (TimelineDurationMs <= 0) return;
        var marks = AxisBookmarks();
        float center = overview.Y + overview.Height / 2;
        float kiaiHeight = Math.Min(12, overview.Height * .6f);
        float y = center + kiaiHeight / 2 + 5;
        for (int i = 0; i + 1 < marks.Count; i++)
        {
            var appearance = Document.BookmarkAppearanceAt(marks[i]);
            if (!appearance.ShowInterval || marks[i] >= TimelineDurationMs) continue;
            float left = overview.X + (float)(marks[i] / TimelineDurationMs) * overview.Width;
            float right = overview.X + (float)(Math.Min(marks[i + 1], TimelineDurationMs) / TimelineDurationMs) * overview.Width;
            c.Fill(new(left, y, Math.Max(2, right - left), 5), appearance.Colour, opacity: .8f);
        }
    }
}
