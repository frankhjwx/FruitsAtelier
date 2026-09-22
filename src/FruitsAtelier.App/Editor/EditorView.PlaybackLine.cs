using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action? RequestViewPreference { get; set; }
    private double playbackLineDragRatio, playbackLineDragViewStart;
    private bool playbackLineDragPinned;
    private float playbackLineGrabOffset;
    internal Rect PlaybackLineHandleBounds => new(plot.X - 5, Screen(new(playhead, 0)).Y - 8, 15, 16);

    private bool BeginPlaybackLineDrag(float x, float y)
    {
        if (!plot.Contains(x, y) || !PlaybackLineHandleBounds.Contains(x, y)) return false;
        if (AudioPlaying || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return true;
        playbackLineDragRatio = playbackLineFromBottom;
        playbackLineDragViewStart = viewStart;
        playbackLineDragPinned = pinPlayhead;
        playbackLineGrabOffset = y - Screen(new(playhead, 0)).Y;
        drag = DragKind.PlaybackLine;
        return true;
    }

    private void MovePlaybackLine(float y)
    {
        if (AudioPlaying) { CancelPlaybackLineDrag(); return; }
        playbackLineFromBottom = Math.Clamp((plot.Bottom - y + playbackLineGrabOffset) / plot.Height, .05, .95);
        FollowPlayhead();
    }

    private void FinishPlaybackLineDrag()
    {
        drag = DragKind.None;
        if (LibrarySettings.PlaybackLineFromBottom == playbackLineFromBottom) return;
        LibrarySettings.PlaybackLineFromBottom = playbackLineFromBottom;
        RequestViewPreference?.Invoke();
    }

    private void CancelPlaybackLineDrag()
    {
        if (drag != DragKind.PlaybackLine) return;
        playbackLineFromBottom = playbackLineDragRatio;
        viewStart = playbackLineDragViewStart;
        pinPlayhead = playbackLineDragPinned;
        drag = DragKind.None;
    }
}
