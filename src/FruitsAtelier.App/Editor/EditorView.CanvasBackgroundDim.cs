using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool canvasDimHovered, canvasDimDragging;
    private int canvasDimDragStart;
    private float canvasDimFadeFrom, canvasDimFadeTarget, canvasDimPaintedOpacity;
    private double canvasDimFadeAt;
    private double? canvasDimLeftAt;
    private double CanvasDimNowMs => timeProvider.GetTimestamp() * 1000d / timeProvider.TimestampFrequency;
    public Rect CanvasBackgroundDimBounds => new(canvas.X + 82, plot.Bottom - 40, Math.Min(320, Math.Max(0, canvas.Width - 104)), 32);
    public Rect CanvasBackgroundDimSliderBounds
    {
        get
        {
            var r = CanvasBackgroundDimBounds;
            return new(r.X + 124, r.Y + 4, Math.Max(0, r.Width - 138), r.Height - 8);
        }
    }
    private bool CanUseCanvasBackgroundDim => CanDropBackground && !TimingPageVisible && !TimeJumpVisible
        && !IsEditingText && drag == DragKind.None && !SliderHoldNeedsRedraw && !movementPanelPointer && dockPressed is null
        && menu < 0 && contextItems.Count == 0 && !panelMenuOpen && !volumePopoverOpen
        && !PatternGalleryPopupContains(mouseX, mouseY)
        && CanvasBackgroundDimBounds.Width >= 160;
    private bool PointerOverCanvasBackgroundDim => CanUseCanvasBackgroundDim && CanvasBackgroundDimBounds.Contains(mouseX, mouseY);
    internal float CanvasBackgroundDimOpacity => canvasDimFadeFrom + (canvasDimFadeTarget - canvasDimFadeFrom)
        * (float)Math.Clamp((CanvasDimNowMs - canvasDimFadeAt) / 150, 0, 1);

    public bool CanvasBackgroundDimNeedsRedraw
    {
        get
        {
            AdvanceCanvasBackgroundDim();
            return CanUseCanvasBackgroundDim && (CanvasBackgroundDimOpacity != canvasDimFadeTarget || CanvasBackgroundDimOpacity != canvasDimPaintedOpacity
                || !canvasDimHovered && canvasDimLeftAt is not null && canvasDimFadeTarget > 0);
        }
    }

    private void AdvanceCanvasBackgroundDim()
    {
        if (!CanUseCanvasBackgroundDim) { HideCanvasBackgroundDim(); return; }
        var r = CanvasBackgroundDimBounds;
        bool near = canvasDimDragging || new Rect(r.X - 18, r.Y - 12, r.Width + 36, r.Height + 24).Contains(mouseX, mouseY);
        double now = CanvasDimNowMs;
        if (near)
        {
            canvasDimLeftAt = null;
            if (canvasDimFadeTarget != 1) FadeCanvasBackgroundDim(1, now);
        }
        else if (canvasDimHovered) canvasDimLeftAt = now;
        canvasDimHovered = near;
        if (canvasDimLeftAt is { } left && now >= left + 1000 && canvasDimFadeTarget != 0)
            FadeCanvasBackgroundDim(0, left + 1000);
    }

    private void FadeCanvasBackgroundDim(float target, double at)
    {
        canvasDimFadeFrom = CanvasBackgroundDimOpacity;
        canvasDimFadeTarget = target;
        canvasDimFadeAt = at;
    }

    private bool BeginCanvasBackgroundDimPointer(float x, float y, int button)
    {
        AdvanceCanvasBackgroundDim();
        if (!CanUseCanvasBackgroundDim || !CanvasBackgroundDimBounds.Contains(x, y)) return false;
        if (button == 0 && CanvasBackgroundDimSliderBounds.Contains(x, y))
        {
            canvasDimDragStart = LibrarySettings.MappingCanvasBackgroundDim;
            canvasDimDragging = true;
            UpdateCanvasBackgroundDimDrag(x);
        }
        return true;
    }

    private bool MoveCanvasBackgroundDimPointer(float x, float y)
    {
        AdvanceCanvasBackgroundDim();
        if (canvasDimDragging) { UpdateCanvasBackgroundDimDrag(x); return true; }
        return CanUseCanvasBackgroundDim && CanvasBackgroundDimBounds.Contains(x, y);
    }

    private void UpdateCanvasBackgroundDimDrag(float x)
    {
        var slider = CanvasBackgroundDimSliderBounds;
        LibrarySettings.MappingCanvasBackgroundDim = (int)Math.Round(Math.Clamp((x - slider.X) / slider.Width, 0, 1) * 100);
    }

    private bool EndCanvasBackgroundDimPointer(float x, float y, int button)
    {
        if (!canvasDimDragging || button != 0) return false;
        mouseX = x; mouseY = y;
        UpdateCanvasBackgroundDimDrag(x);
        FinishCanvasBackgroundDimDrag();
        AdvanceCanvasBackgroundDim();
        return true;
    }

    private void FinishCanvasBackgroundDimDrag()
    {
        if (!canvasDimDragging) return;
        canvasDimDragging = false;
        if (LibrarySettings.MappingCanvasBackgroundDim != canvasDimDragStart) RequestViewPreference?.Invoke();
    }

    private void HideCanvasBackgroundDim()
    {
        FinishCanvasBackgroundDimDrag();
        canvasDimHovered = false;
        canvasDimLeftAt = null;
        canvasDimFadeFrom = canvasDimFadeTarget = 0;
    }

    private void DrawCanvasBackgroundDim(ICanvas c)
    {
        AdvanceCanvasBackgroundDim();
        float opacity = canvasDimPaintedOpacity = CanvasBackgroundDimOpacity;
        if (opacity <= 0) return;
        var r = CanvasBackgroundDimBounds;
        c.Fill(r, 0, 5, .65f * opacity);
        c.TextOpacity(L.Get("settings.canvasBackgroundDimLabel"), r.X + 10, r.Y + 8, 12, Foreground, 110, false, opacity);
        var slider = CanvasBackgroundDimSliderBounds;
        float y = slider.Y + slider.Height / 2;
        float x = slider.X + slider.Width * LibrarySettings.MappingCanvasBackgroundDim / 100f;
        c.Line(slider.X, y, slider.Right, y, Foreground, 3, .35f * opacity);
        c.Line(slider.X, y, x, y, Accent, 3, opacity);
        c.Circle(x, y, 5, Accent, opacity: opacity);
    }
}
