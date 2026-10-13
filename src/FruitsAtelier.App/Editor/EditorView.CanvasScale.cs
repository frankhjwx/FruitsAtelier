using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private double canvasTimeZoom = .6;
    private double canvasPanX, panStartX;
    private int canvasScaleAxis = -1;
    public bool UniformCanvasScale => LibrarySettings.UniformCanvasScale;
    public double CanvasTimeZoom => UniformCanvasScale ? canvasZoom : canvasTimeZoom;
    public Rect CanvasScaleLockBounds { get; private set; }
    internal Rect CanvasAxisZoomBounds(int axis) => new(zoomSlider.X + 12,
        zoomSlider.Y + axis * 15, Math.Max(1, zoomSlider.Width - 12), 15);
    private float CanvasObjectWidth => Math.Min(Playfield.Width, FullPlayfieldWidth);
    private double MaximumCanvasZoom => UniformCanvasScale ? 1 : 2;
    private double MaximumObjectTimelineScale => LibrarySettings.ExperimentalSnapDivisions ? 16 : 1.5;
    private double CanvasPixelsPerMs => CatchScrollTiming.PixelsPerMs(Document.ApproachRate, FullPlayfieldWidth * CanvasTimeZoom);

    private void SaveCanvasScale()
    {
        LibrarySettings.CanvasZoom = canvasZoom;
        LibrarySettings.CanvasTimeZoom = canvasTimeZoom;
        RequestViewPreference?.Invoke();
    }

    private void ClampCanvasPan()
    {
        double limit = Math.Max(0, (FullPlayfieldWidth * canvasZoom * (512 + PlayfieldPadding * 2) / 512 - plot.Width) / 2);
        canvasPanX = UniformCanvasScale ? 0 : Math.Clamp(canvasPanX, -limit, limit);
    }

    private void SetCanvasScales(double horizontal, double vertical, float anchorY)
    {
        anchorY = Math.Clamp(anchorY, plot.Y, plot.Bottom);
        double anchorTime = viewStart + (plot.Bottom - anchorY) / pixelsPerMs;
        canvasZoom = Math.Clamp(horizontal, MinimumCanvasZoom, MaximumCanvasZoom);
        canvasTimeZoom = UniformCanvasScale ? canvasZoom : Math.Clamp(vertical, MinimumCanvasZoom, 2);
        ClampCanvasPan();
        pixelsPerMs = CanvasPixelsPerMs;
        viewStart = anchorTime - (plot.Bottom - anchorY) / pixelsPerMs;
        ClampView();
    }

    private void ToggleUniformCanvasScale()
    {
        LibrarySettings.UniformCanvasScale = !UniformCanvasScale;
        if (UniformCanvasScale) SetCanvasScales(canvasZoom, canvasZoom, plot.Y + plot.Height / 2);
        else canvasTimeZoom = canvasZoom;
        SaveCanvasScale();
    }

    private bool CanvasScalePointerDown(float x, float y, int button)
    {
        if (TimingPageVisible || LibraryVisible || StreamDialogVisible || menu >= 0 || contextItems.Count > 0) return false;
        if (button == 0 && CanvasScaleLockBounds.Contains(x, y)) { ToggleUniformCanvasScale(); return true; }
        if (!UniformCanvasScale && button == 0)
            for (int axis = 0; axis < 2; axis++)
                if (CanvasAxisZoomBounds(axis).Contains(x, y))
                { canvasScaleAxis = axis; drag = DragKind.CanvasZoom; SetCanvasAxisZoom(x); return true; }
        return !UniformCanvasScale && zoomSlider.Contains(x, y);
    }

    private void SetCanvasAxisZoom(float x)
    {
        var r = CanvasAxisZoomBounds(canvasScaleAxis);
        double value = MinimumCanvasZoom + Math.Clamp((x - r.X) / r.Width, 0, 1) * (2 - MinimumCanvasZoom);
        if (!AudioPlaying) pinPlayhead = false;
        SetCanvasScales(canvasScaleAxis == 0 ? value : canvasZoom,
            canvasScaleAxis == 1 ? value : canvasTimeZoom, plot.Y + plot.Height / 2);
    }

    private void DrawCanvasScaleLock(ICanvas c)
    {
        CanvasScaleLockBounds = new(zoomSlider.Right + (rightPanel.X < 900 ? 32 : 48), canvas.Y + 6, 22, 26);
        float x = CanvasScaleLockBounds.X + 5, y = CanvasScaleLockBounds.Y + 11;
        uint color = UniformCanvasScale ? Muted : Accent;
        c.Stroke(new(x, y, 12, 10), color, 1.5f, 2);
        float offset = UniformCanvasScale ? 0 : 4;
        c.Line(x + 2 + offset, y, x + 2 + offset, y - 6, color, 1.5f);
        c.Line(x + 2 + offset, y - 6, x + 10 + offset, y - 6, color, 1.5f);
        c.Line(x + 10 + offset, y - 6, x + 10 + offset, y - (UniformCanvasScale ? 0 : 3), color, 1.5f);
        if (menu < 0 && contextItems.Count == 0 && CanvasScaleLockBounds.Contains(mouseX, mouseY))
            c.Text(L.Get(UniformCanvasScale ? "ui.uniformScale" : "ui.independentScale"),
                zoomSlider.X, canvas.Y + 42, 11, Foreground, 300);
    }

    private void DrawCanvasAxisZoom(ICanvas c)
    {
        for (int axis = 0; axis < 2; axis++)
        {
            var slider = CanvasAxisZoomBounds(axis);
            double value = axis == 0 ? canvasZoom : canvasTimeZoom;
            float end = slider.X + (float)((value - MinimumCanvasZoom) / (2 - MinimumCanvasZoom)) * slider.Width;
            float y = slider.Y + slider.Height / 2;
            c.Text(axis == 0 ? "X" : "Y", zoomSlider.X, slider.Y + 1, 9, Muted, 10);
            c.Line(slider.X, y, slider.Right, y, Grid, 2);
            c.Line(slider.X, y, end, y, Accent, 2);
            c.Circle(end, y, 3.5f, Accent);
            c.Text(L.Get("ui.zoomPercent", value * 100), slider.Right + 4, slider.Y + 1, 9, Foreground,
                CanvasScaleLockBounds.X - slider.Right - 4);
        }
    }
}
