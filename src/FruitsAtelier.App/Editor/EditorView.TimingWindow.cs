using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private float timingWindowScale = 1;
    private int timingWindowCorner;
    private Rect timingWindowResizeStart;
    private (float X, float Y) timingWindowResizePointer;
    private bool timingWindowDrawing;
    private float TimingRowScale => timingWindowDrawing || !TimingSetupVisible || TimingModal ? 1 : timingWindowScale;
    public int FloatingPanelResizeCursor => timingWindowDragging || TimingWindowContains(mouseX, mouseY)
        ? TimingWindowResizeCursor : MovementPanelResizeCursor;
    private int TimingWindowResizeCursor => timingWindowDragging ? timingWindowCorner
        : TimingWindowContains(mouseX, mouseY) ? TimingWindowCorner(mouseX, mouseY) : 0;

    private Rect TimingWindowMap(Rect r)
    {
        var window = TimingSetupBounds;
        return new(window.X + (r.X - window.X) * timingWindowScale,
            window.Y + (r.Y - window.Y) * timingWindowScale, r.Width * timingWindowScale, r.Height * timingWindowScale);
    }

    private int TimingWindowCorner(float x, float y)
    {
        var r = TimingSetupBounds;
        if (!r.Contains(x, y)) return 0;
        bool left = x < r.X + 8, right = x >= r.Right - 8;
        bool top = y < r.Y + 8, bottom = y >= r.Bottom - 8;
        return top ? left ? 1 : right ? 2 : 0 : bottom ? left ? 3 : right ? 4 : 0 : 0;
    }

    private void MoveTimingWindow(float x, float y)
    {
        if (timingWindowCorner == 0)
        {
            timingWindowPosition = (Math.Clamp(x - timingWindowGrab.X, 0, Math.Max(0, width - TimingSetupBounds.Width)),
                Math.Clamp(y - timingWindowGrab.Y, 0, Math.Max(0, height - TimingSetupBounds.Height)));
            return;
        }
        var r = timingWindowResizeStart;
        bool left = timingWindowCorner is 1 or 3, top = timingWindowCorner is 1 or 2;
        float ratio = r.Height / r.Width;
        float anchorX = left ? r.Right : r.X, anchorY = top ? r.Bottom : r.Y;
        float dx = (x - timingWindowResizePointer.X) * (left ? -1 : 1);
        float dy = (y - timingWindowResizePointer.Y) * (top ? -1 : 1);
        float baseWidth = Math.Min(1000, width - 24);
        float maximum = Math.Min(baseWidth, Math.Min(left ? anchorX : width - anchorX,
            (top ? anchorY : height - anchorY) / ratio));
        float size = Math.Clamp(r.Width + (dx + dy * ratio) / (1 + ratio * ratio), Math.Min(baseWidth * .5f, maximum), maximum);
        timingWindowScale = size / baseWidth;
        timingWindowPosition = (left ? anchorX - size : anchorX, top ? anchorY - size * ratio : anchorY);
    }

    private void DrawTimingSetup(ICanvas canvas)
    {
        if (!TimingSetupVisible || TimingModal) { DrawTimingSetupContents(canvas); return; }
        var r = TimingSetupBounds;
        var scaled = new TimingScaledCanvas(canvas, r.X, r.Y, timingWindowScale);
        var pointer = (mouseX, mouseY);
        mouseX = r.X + (mouseX - r.X) / timingWindowScale;
        mouseY = r.Y + (mouseY - r.Y) / timingWindowScale;
        timingWindowDrawing = true;
        try { DrawTimingSetupContents(scaled); }
        finally { timingWindowDrawing = false; mouseX = pointer.mouseX; mouseY = pointer.mouseY; }
        for (int i = timingHitStart; i < timingHitEnd; i++) hits[i] = hits[i] with { Bounds = scaled.Map(hits[i].Bounds) };
        for (int i = 0; i < timingWindowFields.Count; i++)
        {
            var f = timingWindowFields[i];
            timingWindowFields[i] = (f.Key, scaled.Map(f.Bounds), f.Value, f.Apply);
            if (textLayouts.TryGetValue("timing:" + f.Key, out var layout))
                textLayouts["timing:" + f.Key] = (scaled.Map(layout.Content), scaled.X(layout.Left), layout.Positions,
                    layout.Widths.Select(w => w * timingWindowScale).ToArray());
        }
        timingFields.Clear(); timingFields.AddRange(timingSetupFocused ? timingWindowFields : timingBackgroundFields);
        timingListBounds = scaled.Map(timingListBounds);
        TimingSvDisplayBounds = scaled.Map(TimingSvDisplayBounds); TimingSvLockBounds = scaled.Map(TimingSvLockBounds);
        for (int corner = 1; corner <= 4; corner++)
        {
            bool left = corner is 1 or 3, top = corner is 1 or 2;
            float x = left ? r.X + 3 : r.Right - 3, y = top ? r.Y + 3 : r.Bottom - 3;
            canvas.Line(x, y, x + (left ? 7 : -7), y, Muted);
            canvas.Line(x, y, x, y + (top ? 7 : -7), Muted);
        }
    }

    private sealed class TimingScaledCanvas(ICanvas canvas, float originX, float originY, float scale) : ICanvas
    {
        internal float X(float x) => originX + (x - originX) * scale;
        private float Y(float y) => originY + (y - originY) * scale;
        internal Rect Map(Rect r) => new(X(r.X), Y(r.Y), r.Width * scale, r.Height * scale);
        public void Fill(Rect r, uint color, float radius = 0, float opacity = 1) => canvas.Fill(Map(r), color, radius * scale, opacity);
        public void Stroke(Rect r, uint color, float width = 1, float radius = 0) => canvas.Stroke(Map(r), color, width * scale, radius * scale);
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1)
            => canvas.Line(X(x1), Y(y1), X(x2), Y(y2), color, width * scale, opacity);
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1)
            => canvas.Circle(X(x), Y(y), radius * scale, color, filled, width * scale, opacity);
        public float MeasureText(string text, float size, bool bold = false) => canvas.MeasureText(text, size * scale, bold) / scale;
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false)
            => canvas.Text(text, X(x), Y(y), size * scale, color, maxWidth * scale, bold);
        public bool Image(string path, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1)
            => canvas.Image(path, Map(destination), tint, source, opacity);
        public void Clip(Rect r) => canvas.Clip(Map(r));
        public void Unclip() => canvas.Unclip();
    }
}
