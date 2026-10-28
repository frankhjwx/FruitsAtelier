using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int overlayDragging;
    private float overlayPointerX, overlayPointerY;
    private int canvasControlsCorner;
    private Rect canvasControlsResizeStart;
    private int CanvasControlsResizeCursor => canvasControlsCorner != 0 ? canvasControlsCorner
        : CanUseCanvasBackgroundDim ? CanvasControlsCornerAt(mouseX, mouseY) : 0;

    private int CanvasControlsCornerAt(float x, float y)
    {
        var r = CanvasControlsPanelBounds;
        for (int corner = 1; corner <= 4; corner++)
        {
            float cx = corner is 1 or 3 ? r.X : r.Right, cy = corner is 1 or 2 ? r.Y : r.Bottom;
            if (new Rect(cx - 6, cy - 6, 12, 12).Contains(x, y)) return corner;
        }
        return 0;
    }

    private static Rect PositionedOverlay(Rect origin, float dx, float dy, Rect area) => new(
        Math.Clamp(origin.X + (float.IsFinite(dx) ? dx : 0), area.X, Math.Max(area.X, area.Right - origin.Width)),
        Math.Clamp(origin.Y + (float.IsFinite(dy) ? dy : 0), area.Y, Math.Max(area.Y, area.Bottom - origin.Height)),
        origin.Width, origin.Height);

    private void DrawOverlayGrip(ICanvas c, Rect r, float opacity = 1)
    {
        c.Fill(r, Chrome(0x1D2732, Panel), 4, opacity);
        for (int row = -1; row <= 1; row++)
        for (int col = 0; col < 2; col++)
            c.Circle(r.X + 5 + col * 4, r.Y + r.Height / 2 + row * 5, 1, Chrome(0xAAB8C8, Muted), opacity: opacity);
    }

    private bool BeginOverlayDrag(float x, float y, int button)
    {
        if (button != 0 || menu >= 0 || contextItems.Count > 0) return false;
        canvasControlsCorner = 0;
        if (BookmarkToolbarVisible && BookmarkToolbarGrip.Contains(x, y)) overlayDragging = 1;
        else if (CanUseCanvasBackgroundDim)
        {
            var r = CanvasControlsPanelBounds;
            canvasControlsCorner = CanvasControlsCornerAt(x, y);
            if (canvasControlsCorner == 0 && !CanvasControlsGrip.Contains(x, y)) return false;
            canvasControlsResizeStart = r;
            overlayDragging = 2;
        }
        else return false;
        overlayPointerX = x; overlayPointerY = y;
        return true;
    }

    private bool MoveOverlayDrag(float x, float y)
    {
        if (overlayDragging == 0) return false;
        mouseX = x; mouseY = y;
        float dx = x - overlayPointerX, dy = y - overlayPointerY;
        if (canvasControlsCorner != 0)
        {
            var r = canvasControlsResizeStart;
            bool left = canvasControlsCorner is 1 or 3, top = canvasControlsCorner is 1 or 2;
            float anchorX = left ? r.Right : r.X, anchorY = top ? r.Bottom : r.Y;
            float ratio = CanvasControlsBaseHeight / CanvasControlsBaseWidth;
            float maximum = Math.Min(CanvasControlsBaseWidth, Math.Min(left ? anchorX - CanvasControlsArea.X : CanvasControlsArea.Right - anchorX,
                (top ? anchorY - CanvasControlsArea.Y : CanvasControlsArea.Bottom - anchorY) / ratio));
            float size = Math.Clamp(r.Width + (dx * (left ? -1 : 1) + dy * (top ? -1 : 1) * ratio) / (1 + ratio * ratio),
                Math.Min(CanvasControlsBaseWidth * .5f, maximum), maximum);
            LibrarySettings.CanvasControlsScale = size / CanvasControlsBaseWidth;
            LibrarySettings.CanvasControlsOffsetX = (left ? anchorX - size : anchorX) - CanvasControlsOrigin.X;
            LibrarySettings.CanvasControlsOffsetY = (top ? anchorY - size * ratio : anchorY) - CanvasControlsOrigin.Y;
            return true;
        }
        if (overlayDragging == 1)
        {
            var before = BookmarkToolbarBounds;
            var after = PositionedOverlay(before, dx, 0, BookmarkToolbarArea);
            LibrarySettings.BookmarkToolbarOffsetX = after.X - BookmarkToolbarOrigin.X;
        }
        else
        {
            var before = CanvasControlsPanelBounds;
            var after = PositionedOverlay(before, dx, dy, CanvasControlsArea);
            LibrarySettings.CanvasControlsOffsetX = after.X - CanvasControlsOrigin.X;
            LibrarySettings.CanvasControlsOffsetY = after.Y - CanvasControlsOrigin.Y;
        }
        overlayPointerX = x; overlayPointerY = y;
        return true;
    }

    private void FinishOverlayDrag()
    {
        if (overlayDragging == 0) return;
        overlayDragging = 0;
        canvasControlsCorner = 0;
        RequestViewPreference?.Invoke();
    }
}
