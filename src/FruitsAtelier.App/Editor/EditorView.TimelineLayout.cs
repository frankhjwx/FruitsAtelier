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
    internal Rect ObjectTimelineResizeBounds => TimingPageVisible ? default
        : new(objectTimeline.X - 42, objectTimeline.Bottom + 1, objectTimeline.Width + 42, 8);
    internal Rect OverviewTimelineResizeBounds => new(0, TransportTop - (OverviewTimelineCollapsed ? 8 : 4), width, 8);
    internal Rect PlaybackSpeedSelectorBounds => new(VolumeButtonBounds.X - 8 - PlaybackRates.Length * 50 + 4, height - 27, PlaybackRates.Length * 50 - 4, 26);
    internal Rect TransportButtonBounds(int index) => OverviewTimelineCollapsed ? default
        : new(272 + index * 49, overview.Y + (overview.Height - 32 * TransportScale) / 2, 42, 32 * TransportScale);
    private bool resizingObjectTimeline;
    private float timelineResizeY, timelineResizeHeight, timelineResizeOriginal;
    public bool TimelinePanelResizeCursor => DockContentInputAvailable && !librarySettingsOpen && menu < 0 && contextItems.Count == 0
        && !PatternGalleryPopupContains(mouseX, mouseY) && !PatternGalleryWantsCapture
        && (drag == DragKind.TimelinePanelResize || drag == DragKind.None
            && (ObjectTimelineResizeBounds.Contains(mouseX, mouseY) || OverviewTimelineResizeBounds.Contains(mouseX, mouseY)));

    private bool BeginTimelinePanelResize(float x, float y, int button)
    {
        if (button != 0 || !TimelinePanelResizeCursor) return false;
        if (OverviewTimelineCollapsed && OverviewTimelineResizeBounds.Contains(x, y)) return true;
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
            bool hover = TimelinePanelResizeCursor && bounds.Contains(mouseX, mouseY);
            float y = bounds == OverviewTimelineResizeBounds && OverviewTimelineCollapsed ? bounds.Bottom - 1 : bounds.Y + 4;
            c.Line(bounds.X, y, bounds.Right, y, hover ? Accent : Grid, hover ? 2 : 1);
            c.Line(bounds.X + bounds.Width / 2 - 16, y, bounds.X + bounds.Width / 2 + 16, y, hover ? Accent : Muted, 2);
        }
    }

    private void ToggleOverviewTimeline()
    {
        LibrarySettings.OverviewTimelineCollapsed = !OverviewTimelineCollapsed;
        RequestViewPreference?.Invoke();
    }

    private bool TogglePaneSeparator(float x, float y)
    {
        if (!DockContentInputAvailable || librarySettingsOpen || menu >= 0 || contextItems.Count > 0 || languageMenuOpen
            || PatternGalleryPopupContains(x, y) || PatternGalleryWantsCapture
            || drag is not (DragKind.None or DragKind.TimelinePanelResize or DragKind.PreviewResize)) return false;
        if (OverviewTimelineResizeBounds.Contains(x, y))
        {
            if (!CommitField()) return true;
            FinishTimelinePanelResize(cancel: true);
            ToggleOverviewTimeline();
            return true;
        }
        if (TimingPageVisible) return false;
        foreach (bool left in new[] { true, false })
        {
            var group = left ? DockWorkspaceState.Left : DockWorkspaceState.Right;
            if (group.Panels.Count == 0 || !DockCollapseBounds(left).Contains(x, y)) continue;
            if (!CommitField()) return true;
            drag = DragKind.None;
            group.Collapsed = !group.Collapsed;
            SaveDockLayoutIfChanged();
            return true;
        }
        return false;
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
