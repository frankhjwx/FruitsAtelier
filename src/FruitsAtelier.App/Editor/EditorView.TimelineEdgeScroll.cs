namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private long timelineEdgeScrollTimestamp;
    private double TimelineDragScrollDirection
    {
        get
        {
            if (!dragMoved || drag != DragKind.Objects || !objectDragTimeline || selectionScaleSide != 0
                || mouseY < objectTimeline.Y || mouseY > objectTimeline.Bottom) return 0;
            const double edge = 24;
            return mouseX < objectTimeline.X + edge ? -Math.Clamp((objectTimeline.X + edge - mouseX) / edge, 0, 1)
                : mouseX > objectTimeline.Right - edge ? Math.Clamp((mouseX - objectTimeline.Right + edge) / edge, 0, 1) : 0;
        }
    }

    public bool TimelineDragScrollNeedsRedraw
    {
        get
        {
            double direction = TimelineDragScrollDirection;
            return direction > 0 && playhead < TimelineDurationMs || direction < 0 && playhead > 0;
        }
    }

    private void AdvanceTimelineDragScroll()
    {
        if (drag != DragKind.Objects || !objectDragTimeline) return;
        long now = timeProvider.GetTimestamp();
        double elapsed = Math.Clamp(timeProvider.GetElapsedTime(timelineEdgeScrollTimestamp, now).TotalSeconds, 0, .05);
        timelineEdgeScrollTimestamp = now;
        if (!TimelineDragScrollNeedsRedraw || elapsed == 0) return;
        double previousView = viewStart, previousTime = playhead;
        ScrollBoxTo(playhead + TimelineDragScrollDirection * 600 * elapsed / objectTimelineScale);
        ContinueDragAfterNavigation(previousView, previousTime, shiftHeld, placementCtrl);
        if (IsLazerEditing)
        {
            // Presentation follow must not apply this same seek to the drag a second time.
            lastLazerPresentationTime = LazerPresentationTime;
            lastLazerViewStart = PresentationViewStart;
        }
    }
}
