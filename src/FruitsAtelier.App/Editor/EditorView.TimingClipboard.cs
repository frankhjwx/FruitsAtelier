using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<int>? RequestTimingClipboardMenu { get; set; }
    internal Rect TimingListViewport => timingListBounds;
    private int timingContextRequest;
    private (float X, float Y, int Session, bool Copy) timingContextTarget;
    private bool timingContextMenu;
    private long? timingCopiedAt;
    private float timingCopiedPainted;
    internal float TimingCopyOpacity => timingCopiedAt is { } started
        ? (float)Math.Clamp((1 - timeProvider.GetElapsedTime(started).TotalSeconds) / .3, 0, 1) : 0;
    public bool TimingFeedbackNeedsRedraw => TimingSetupInputAvailable && timingCopiedPainted > 0;

    private void CopyTimingSelection()
    {
        if (RequestCopyText is null) return;
        var selected = VisibleTimingEntries().Where(e => timingSelected.Contains(e.Id)).Select(e => e.Point).ToArray();
        if (selected.Length == 0) return;
        RequestCopyText(TimingEditing.Serialize(selected));
        timingCopiedAt = timeProvider.GetTimestamp();
    }

    private void RequestTimingContextMenu(float x, float y)
    {
        if (!CommitTimingField()) return;
        var entries = VisibleTimingEntries();
        float offset = y - timingListBounds.Y - 32 * TimingRowScale;
        int row = (int)Math.Floor(offset / (28 * TimingRowScale));
        int capacity = Math.Max(1, (int)((timingListBounds.Height / TimingRowScale - 34) / 28));
        int index = timingScroll + row;
        bool copy = x >= timingListBounds.X + 3 * TimingRowScale && x < timingListBounds.Right - 9 * TimingRowScale
            && row >= 0 && row < capacity && index < entries.Length && timingSelected.Contains(entries[index].Id);
        timingContextTarget = (x, y, TimingInputSession, copy);
        if (RequestTimingClipboardMenu is { } request) request(timingContextRequest);
        else ShowTimingClipboardMenu("", timingContextRequest);
    }

    public void ShowTimingClipboardMenu(string text, int request)
    {
        if (request != timingContextRequest || timingContextTarget.Session != TimingInputSession || !TimingSetupInputAvailable) return;
        bool paste;
        try { paste = TimingEditing.Parse(text).Length > 0; }
        catch (ArgumentException) { paste = false; }
        contextItems.Clear(); menu = -1;
        if (timingContextTarget.Copy) contextItems.Add(new(L.Get("editor.command.copy"), CopyTimingSelection, Shortcut: "Ctrl+C"));
        int session = timingContextTarget.Session;
        if (paste) contextItems.Add(new(L.Get("editor.command.paste"), () => PasteTimingText(text, session), Shortcut: "Ctrl+V"));
        timingContextMenu = contextItems.Count > 0;
        contextBounds = new(Math.Clamp(timingContextTarget.X, 0, Math.Max(0, width - 240)),
            Math.Clamp(timingContextTarget.Y, 0, Math.Max(0, height - ContextMenuHeight)), 240, ContextMenuHeight);
    }

    private void DrawTimingCopyFeedback(ICanvas c)
    {
        timingCopiedPainted = TimingSetupInputAvailable ? TimingCopyOpacity : 0;
        if (timingCopiedPainted <= 0) return;
        string label = L.Get("timing.copied");
        float w = Math.Min(timingListBounds.Width - 16, c.MeasureText(label, 13) + 24);
        var popup = new Rect(timingListBounds.X + (timingListBounds.Width - w) / 2, timingListBounds.Bottom - 40, w, 30);
        c.Fill(popup, Surface, 4, timingCopiedPainted);
        c.StrokeOpacity(popup, Accent, 1, 4, timingCopiedPainted);
        c.TextOpacity(label, popup.X + 12, popup.Y + 6, 13, Accent, w - 24, false, timingCopiedPainted);
    }
}
