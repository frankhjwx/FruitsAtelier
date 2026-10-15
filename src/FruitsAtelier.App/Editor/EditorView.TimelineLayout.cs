using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private float OverviewHeight => LibrarySettings.OverviewTimelineHeight;
    private float ObjectTimelineHeight => LibrarySettings.ObjectTimelineHeight;
    private float TransportRowHeight => Math.Max(32, OverviewHeight);
    private float TransportTop => height - TransportRowHeight - 48;
    internal Rect ObjectTimelineResizeBounds => TimingPageVisible ? default
        : new(objectTimeline.X - 42, objectTimeline.Bottom + 1, objectTimeline.Width + 42, 8);
    internal Rect OverviewTimelineResizeBounds => new(0, TransportTop - 4, width, 8);
    internal Rect PlaybackSpeedSelectorBounds => new(VolumeButtonBounds.X - 86, height - 27, 78, 26);
    internal Rect TransportButtonBounds(int index) => new(208 + index * 49, overview.Y + (overview.Height - 32) / 2, 42, 32);
    private bool resizingObjectTimeline;
    private float timelineResizeY, timelineResizeHeight, timelineResizeOriginal;
    public bool TimelinePanelResizeCursor => DockContentInputAvailable && !librarySettingsOpen && menu < 0 && contextItems.Count == 0
        && (drag == DragKind.TimelinePanelResize || drag == DragKind.None
            && (ObjectTimelineResizeBounds.Contains(mouseX, mouseY) || OverviewTimelineResizeBounds.Contains(mouseX, mouseY)));

    private bool BeginTimelinePanelResize(float x, float y, int button)
    {
        if (button != 0 || !TimelinePanelResizeCursor) return false;
        resizingObjectTimeline = ObjectTimelineResizeBounds.Contains(x, y);
        timelineResizeY = y;
        timelineResizeHeight = resizingObjectTimeline ? ObjectTimelineHeight : OverviewHeight;
        timelineResizeOriginal = resizingObjectTimeline ? LibrarySettings.ObjectTimelineHeight : LibrarySettings.OverviewTimelineHeight;
        drag = DragKind.TimelinePanelResize;
        return true;
    }

    private void MoveTimelinePanelResize(float y)
    {
        float delta = y - timelineResizeY;
        if (resizingObjectTimeline)
            LibrarySettings.ObjectTimelineHeight = timelineResizeHeight + delta;
        else
            LibrarySettings.OverviewTimelineHeight = timelineResizeHeight - delta;
    }

    private void FinishTimelinePanelResize(bool cancel)
    {
        if (drag != DragKind.TimelinePanelResize) return;
        float current = resizingObjectTimeline ? LibrarySettings.ObjectTimelineHeight : LibrarySettings.OverviewTimelineHeight;
        if (cancel)
        {
            if (resizingObjectTimeline) LibrarySettings.ObjectTimelineHeight = timelineResizeOriginal;
            else LibrarySettings.OverviewTimelineHeight = timelineResizeOriginal;
        }
        drag = DragKind.None;
        if (!cancel && current != timelineResizeOriginal) RequestViewPreference?.Invoke();
    }

    private void DrawTimelineResizers(ICanvas c)
    {
        if (!TimingPageVisible) Draw(ObjectTimelineResizeBounds);
        Draw(OverviewTimelineResizeBounds);
        void Draw(Rect bounds)
        {
            bool hover = bounds.Contains(mouseX, mouseY);
            c.Line(bounds.X, bounds.Y + 4, bounds.Right, bounds.Y + 4, hover ? Accent : Grid, hover ? 2 : 1);
            c.Line(bounds.X + bounds.Width / 2 - 16, bounds.Y + 4, bounds.X + bounds.Width / 2 + 16, bounds.Y + 4, hover ? Accent : Muted, 2);
        }
    }

    private float DrawPlaybackSpeedSelector(ICanvas c)
    {
        var bounds = PlaybackSpeedSelectorBounds;
        string label = L.Get("ui.playbackSpeed");
        float labelWidth = c.MeasureText(label, 11);
        float labelX = bounds.X - labelWidth - 10;
        c.Text(label, labelX, height - 20, 11, Muted, labelWidth);
        c.Fill(bounds, Surface, 4);
        c.Stroke(bounds, Grid, radius: 4);
        Button(c, bounds, L.Get("ui.zoomPercent", PlaybackSpeed * 100) + " ▾", () =>
        {
            menu = -1; languageMenuOpen = false; contextItems.Clear();
            foreach (double speed in PlaybackRates)
                contextItems.Add(new((PlaybackSpeed == speed ? "✓ " : "") + L.Get("ui.zoomPercent", speed * 100), () => SetPlaybackSpeed(speed)));
            contextBounds = new(bounds.X, Math.Max(0, bounds.Y - ContextMenuHeight - 4), bounds.Width, ContextMenuHeight);
        }, fontSize: 11, textPadding: 8, textRightPadding: 8);
        return labelX;
    }
}
