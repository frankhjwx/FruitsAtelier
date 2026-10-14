using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class DockHorizontal
    {
        public Rect Viewport, Track;
        public float ContentWidth, Offset;
    }
    private readonly Dictionary<DockPanel, DockHorizontal> dockHorizontal = [];
    private DockHorizontal? dockHorizontalDrag;
    private float dockHorizontalGrab;
    private DockHorizontal PrepareDockHorizontal(DockPanel panel, Rect bounds, float minimumWidth)
    {
        if (!dockHorizontal.TryGetValue(panel, out var state)) dockHorizontal[panel] = state = new();
        state.ContentWidth = Math.Max(bounds.Width, minimumWidth);
        state.Offset = Math.Clamp(state.Offset, 0, state.ContentWidth - bounds.Width);
        state.Track = minimumWidth > bounds.Width ? new(bounds.X, bounds.Bottom - 12, bounds.Width, 12) : default;
        state.Viewport = bounds with { Height = bounds.Height - state.Track.Height };
        return state;
    }
    private void DrawDockHorizontal(ICanvas c, DockHorizontal state)
    {
        if (state.Track.Width <= 0) return;
        var track = state.Track;
        float thumb = track.Width * track.Width / state.ContentWidth;
        c.Fill(track, Background, 3);
        c.Fill(new(track.X + state.Offset / state.ContentWidth * track.Width, track.Y + 3, thumb, 6), Muted, 3);
    }
    private bool BeginDockHorizontal(float x, float y, int button)
    {
        if (button != 0 || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        foreach (var pair in dockHorizontal)
        {
            var state = pair.Value;
            if (!PanelVisible(pair.Key) || !state.Track.Contains(x, y)) continue;
            float thumb = state.Track.Width * state.Track.Width / state.ContentWidth;
            float start = state.Track.X + state.Offset / state.ContentWidth * state.Track.Width;
            dockHorizontalDrag = state;
            dockHorizontalGrab = x >= start && x < start + thumb ? x - start : thumb / 2;
            MoveDockHorizontal(x); return true;
        }
        return false;
    }
    private void MoveDockHorizontal(float x)
    {
        if (dockHorizontalDrag is not { } state) return;
        state.Offset = Math.Clamp((x - state.Track.X - dockHorizontalGrab) * state.ContentWidth / state.Track.Width, 0, state.ContentWidth - state.Track.Width);
    }
    private bool WheelDockHorizontal(float x, float y, float delta, bool shift)
    {
        if (!DockContentInputAvailable) return false;
        foreach (var pair in dockHorizontal)
        {
            var state = pair.Value;
            if (PanelVisible(pair.Key) && state.Track.Width > 0 && (state.Track.Contains(x, y) || shift && state.Viewport.Contains(x, y)))
            { state.Offset = Math.Clamp(state.Offset - delta / 120 * 48, 0, state.ContentWidth - state.Track.Width); return true; }
        }
        return false;
    }
}
