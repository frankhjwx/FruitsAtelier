using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private double canvasTimeZoom = .6;
    private double canvasPanX, panStartX;
    private bool canvasScalePopup;
    private int canvasScaleAxis = -1;
    public bool UniformCanvasScale => LibrarySettings.UniformCanvasScale;
    public double CanvasTimeZoom => UniformCanvasScale ? canvasZoom : canvasTimeZoom;
    public Rect CanvasScaleLockBounds { get; private set; }
    internal Rect CanvasScalePopupBounds => new(zoomSlider.X,
        canvas.Y + 42, Math.Min(300, width - zoomSlider.X - 12), 122);
    internal Rect CanvasAxisZoomBounds(int axis) => new(CanvasScalePopupBounds.X + 44,
        CanvasScalePopupBounds.Y + 43 + axis * 40, CanvasScalePopupBounds.Width - 108, 28);
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
        canvasScalePopup = !UniformCanvasScale;
        if (UniformCanvasScale) SetCanvasScales(canvasZoom, canvasZoom, plot.Y + plot.Height / 2);
        else canvasTimeZoom = canvasZoom;
        SaveCanvasScale();
    }

    private bool CanvasScalePointerDown(float x, float y, int button)
    {
        if (TimingPageVisible || LibraryVisible || StreamDialogVisible) return false;
        if (button == 0 && CanvasScaleLockBounds.Contains(x, y)) { ToggleUniformCanvasScale(); return true; }
        if (!UniformCanvasScale && button == 0 && new Rect(zoomSlider.Right, canvas.Y + 4, CanvasScaleLockBounds.X - zoomSlider.Right, 29).Contains(x, y))
        { canvasScalePopup = !canvasScalePopup; return true; }
        if (!canvasScalePopup) return false;
        if (button == 0)
            for (int axis = 0; axis < 2; axis++)
                if (CanvasAxisZoomBounds(axis).Contains(x, y))
                { canvasScaleAxis = axis; drag = DragKind.CanvasZoom; SetCanvasAxisZoom(x); return true; }
        bool inside = CanvasScalePopupBounds.Contains(x, y);
        if (!inside) canvasScalePopup = false;
        return true;
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
        if (CanvasScaleLockBounds.Contains(mouseX, mouseY) && !canvasScalePopup)
            c.Text(L.Get(UniformCanvasScale ? "ui.uniformScale" : "ui.independentScale"),
                zoomSlider.X, canvas.Y + 42, 11, Foreground, 300);
    }

    private void DrawCanvasScalePopup(ICanvas c)
    {
        if (!canvasScalePopup || UniformCanvasScale || TimingPageVisible || LibraryVisible || librarySettingsOpen) return;
        var r = CanvasScalePopupBounds;
        c.Fill(r, Panel, 5); c.Stroke(r, Grid, radius: 5);
        c.Text(L.Get("ui.independentScale"), r.X + 12, r.Y + 12, 12, Foreground, r.Width - 24);
        for (int axis = 0; axis < 2; axis++)
        {
            var slider = CanvasAxisZoomBounds(axis);
            double value = axis == 0 ? canvasZoom : canvasTimeZoom;
            float end = slider.X + (float)((value - MinimumCanvasZoom) / (2 - MinimumCanvasZoom)) * slider.Width;
            c.Text(axis == 0 ? "X" : "Y", r.X + 14, slider.Y + 7, 12, Foreground, 24);
            c.Line(slider.X, slider.Y + 14, slider.Right, slider.Y + 14, Grid, 3);
            c.Line(slider.X, slider.Y + 14, end, slider.Y + 14, Accent, 3);
            c.Circle(end, slider.Y + 14, 5, Accent);
            c.Text(L.Get("ui.zoomPercent", value * 100), slider.Right + 8, slider.Y + 7, 11, Foreground, 52);
        }
    }
}
