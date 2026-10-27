using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private float OverviewHeight => LibrarySettings.OverviewTimelineHeight;
    private float ObjectTimelineHeight => LibrarySettings.ObjectTimelineHeight;
    private float TransportRowHeight => OverviewHeight;
    private float TransportScale => TransportRowHeight / 40;
    private bool OverviewTimelineCollapsed => LibrarySettings.OverviewTimelineCollapsed;
    private float TransportTop => height - 28 - (OverviewTimelineCollapsed ? 0 : TransportRowHeight + 20);
    internal Rect OverviewTimelineToggleBounds => new(width / 2 - 22, TransportTop - 22, 44, 22);
    internal Rect ObjectTimelineResizeBounds => TimingPageVisible ? default
        : new(objectTimeline.X - 42, objectTimeline.Bottom + 1, objectTimeline.Width + 42, 8);
    internal Rect OverviewTimelineResizeBounds => OverviewTimelineCollapsed ? default : new(0, TransportTop - 4, width, 8);
    internal Rect PlaybackSpeedSelectorBounds => new(VolumeButtonBounds.X - 8 - PlaybackRates.Length * 50 + 4, height - 27, PlaybackRates.Length * 50 - 4, 26);
    internal Rect TransportButtonBounds(int index) => OverviewTimelineCollapsed ? default
        : new(272 + index * 49, overview.Y + (overview.Height - 32 * TransportScale) / 2, 42, 32 * TransportScale);
    private bool resizingObjectTimeline;
    private float timelineResizeY, timelineResizeHeight, timelineResizeOriginal;
    public bool TimelinePanelResizeCursor => DockContentInputAvailable && !librarySettingsOpen && menu < 0 && contextItems.Count == 0
        && !PatternGalleryPopupContains(mouseX, mouseY) && !PatternGalleryWantsCapture
        && !OverviewTimelineToggleBounds.Contains(mouseX, mouseY)
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
        if (!OverviewTimelineCollapsed) Draw(OverviewTimelineResizeBounds);
        void Draw(Rect bounds)
        {
            bool hover = TimelinePanelResizeCursor && bounds.Contains(mouseX, mouseY);
            c.Line(bounds.X, bounds.Y + 4, bounds.Right, bounds.Y + 4, hover ? Accent : Grid, hover ? 2 : 1);
            c.Line(bounds.X + bounds.Width / 2 - 16, bounds.Y + 4, bounds.X + bounds.Width / 2 + 16, bounds.Y + 4, hover ? Accent : Muted, 2);
        }
    }

    private void ToggleOverviewTimeline()
    {
        LibrarySettings.OverviewTimelineCollapsed = !OverviewTimelineCollapsed;
        RequestViewPreference?.Invoke();
    }

    private bool OverviewTimelineTogglePointer(float x, float y, int button)
    {
        if (!DockContentInputAvailable || menu >= 0 || contextItems.Count > 0
            || PatternGalleryPopupContains(x, y) || PatternGalleryWantsCapture
            || !OverviewTimelineToggleBounds.Contains(x, y)) return false;
        if (button == 0 && CommitField()) ToggleOverviewTimeline();
        return true;
    }

    private void DrawOverviewTimelineToggle(ICanvas c)
    {
        c.Fill(OverviewTimelineToggleBounds, Surface, 5);
        Button(c, OverviewTimelineToggleBounds, OverviewTimelineCollapsed ? "⌃" : "⌄", ToggleOverviewTimeline,
            textPadding: 18, textRightPadding: 4);
    }

    private float DrawPlaybackSpeedSelector(ICanvas c)
    {
        var bounds = PlaybackSpeedSelectorBounds;
        string label = L.Get("ui.playbackSpeed");
        float labelWidth = c.MeasureText(label, 11);
        float labelX = bounds.X - labelWidth - 10;
        c.Text(label, labelX, height - 20, 11, Muted, labelWidth);
        for (int i = 0; i < PlaybackRates.Length; i++)
        {
            double speed = PlaybackRates[i];
            Button(c, new(bounds.X + i * 50, bounds.Y, 46, bounds.Height), L.Get("ui.zoomPercent", speed * 100),
                () => SetPlaybackSpeed(speed), active: PlaybackSpeed == speed, fontSize: 11, textPadding: 6, textRightPadding: 4);
        }
        return labelX;
    }
}
