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
    private Rect CanvasControlsArea => new(canvas.X, canvas.Y, canvas.Width, Math.Max(0, plot.Bottom - canvas.Y));
    private bool CanvasControlsHasDiscussion => PanelVisible(DockPanel.Discussion) && !IsOsuCanvas;
    private float CanvasControlsScale => LibrarySettings.CanvasControlsScale;
    private float CanvasControlsAvailableWidth => Math.Min(336, Math.Max(0, canvas.Width - 104));
    private float CanvasControlsBaseWidth => CanvasControlsAvailableWidth - Math.Max(0, CanvasControlsAvailableWidth - 154) / 2;
    private float CanvasControlsBaseHeight => CanvasControlsHasDiscussion ? 84 : 56;
    private Rect CanvasControlsOrigin => new(canvas.X + 82, plot.Bottom - 8 - CanvasControlsBaseHeight * CanvasControlsScale,
        CanvasControlsBaseWidth * CanvasControlsScale, CanvasControlsBaseHeight * CanvasControlsScale);
    internal Rect CanvasControlsPanelBounds => PositionedOverlay(CanvasControlsOrigin,
        LibrarySettings.CanvasControlsOffsetX, LibrarySettings.CanvasControlsOffsetY, CanvasControlsArea);
    private Rect CanvasControlsGrip => new(CanvasControlsPanelBounds.X, CanvasControlsPanelBounds.Y, 16 * CanvasControlsScale, CanvasControlsPanelBounds.Height);
    public Rect CanvasBackgroundDimBounds => new(CanvasControlsPanelBounds.X + 16 * CanvasControlsScale,
        CanvasControlsPanelBounds.Y + (CanvasControlsHasDiscussion ? 28 : 0) * CanvasControlsScale, CanvasControlsPanelBounds.Width - 16 * CanvasControlsScale, 28 * CanvasControlsScale);
    internal Rect CanvasMediaToggleBounds => new(CanvasBackgroundDimBounds.X, CanvasBackgroundDimBounds.Bottom, CanvasBackgroundDimBounds.Width, 28 * CanvasControlsScale);
    internal Rect CanvasDiscussionToggleBounds => CanvasControlsHasDiscussion
        ? new(CanvasBackgroundDimBounds.X, CanvasControlsPanelBounds.Y, CanvasBackgroundDimBounds.Width, 28 * CanvasControlsScale) : default;
    private Rect CanvasOverlayControlsBounds => CanvasControlsPanelBounds;
    public Rect CanvasBackgroundDimSliderBounds
    {
        get
        {
            var r = CanvasBackgroundDimBounds;
            return new(r.X + 124 * CanvasControlsScale, r.Y + 4 * CanvasControlsScale, Math.Max(0, r.Width - 138 * CanvasControlsScale), r.Height - 8 * CanvasControlsScale);
        }
    }
    private bool CanUseCanvasBackgroundDim => CanDropBackground && !TimingPageVisible && !TimeJumpVisible
        && !IsEditingText && drag == DragKind.None && !SliderHoldNeedsRedraw && !movementPanelPointer && dockPressed is null
        && menu < 0 && contextItems.Count == 0 && !panelMenuOpen && !volumePopoverOpen
        && !PatternGalleryPopupContains(mouseX, mouseY)
        && CanvasControlsAvailableWidth >= 176;
    private bool PointerOverCanvasBackgroundDim => CanUseCanvasBackgroundDim && (CanvasOverlayControlsBounds.Contains(mouseX, mouseY) || CanvasControlsGrip.Contains(mouseX, mouseY));
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
        var r = CanvasOverlayControlsBounds;
        bool near = canvasDimDragging || overlayDragging == 2 || new Rect(r.X - 18, r.Y - 12, r.Width + 36, r.Height + 24).Contains(mouseX, mouseY);
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
        if (!CanUseCanvasBackgroundDim || !CanvasOverlayControlsBounds.Contains(x, y)) return false;
        if (button == 0 && CanvasMediaToggleBounds.Contains(x, y)) { ToggleVideoStoryboard(); return true; }
        if (button == 0 && CanvasDiscussionToggleBounds.Contains(x, y))
        {
            LibrarySettings.ShowCanvasDiscussionAnnotations = !LibrarySettings.ShowCanvasDiscussionAnnotations;
            discussionQuoteDrag = null;
            ClearDiscussionTextSelection();
            RequestViewPreference?.Invoke();
            return true;
        }
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
        return CanUseCanvasBackgroundDim && CanvasOverlayControlsBounds.Contains(x, y);
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
        float scale = CanvasControlsScale;
        var panel = CanvasControlsPanelBounds;
        c.Fill(panel, 0, 5 * scale, .65f * opacity);
        var grip = CanvasControlsGrip;
        for (int row = -1; row <= 1; row++)
        for (int col = 0; col < 2; col++)
            c.Circle(grip.X + (6 + col * 4) * scale, grip.Y + grip.Height / 2 + row * 5 * scale, scale, Muted, opacity: opacity);
        var toggle = CanvasDiscussionToggleBounds;
        if (toggle.Width > 0)
            c.TextOpacity((LibrarySettings.ShowCanvasDiscussionAnnotations ? "✓ " : "") + L.Get("discussion.showCanvasAnnotations"),
                toggle.X + 10 * scale, toggle.Y + 6 * scale, 12 * scale, Foreground, toggle.Width - 20 * scale, false, opacity);
        var media = CanvasMediaToggleBounds;
        var check = new Rect(media.X + 9 * scale, media.Y + 6 * scale, 16 * scale, 16 * scale);
        c.StrokeOpacity(check, Foreground, scale, 2 * scale, opacity);
        if (LibrarySettings.EnableVideoStoryboard)
        {
            c.Line(check.X + 3 * scale, check.Y + 8 * scale, check.X + 7 * scale, check.Y + 12 * scale, Foreground, 2 * scale, opacity);
            c.Line(check.X + 7 * scale, check.Y + 12 * scale, check.X + 13 * scale, check.Y + 4 * scale, Foreground, 2 * scale, opacity);
        }
        c.TextOpacity(L.Get("settings.enableVideoStoryboard"), media.X + 33 * scale, media.Y + 6 * scale, 12 * scale, Foreground, media.Width - 39 * scale, false, opacity);
        var r = CanvasBackgroundDimBounds;
        c.TextOpacity(L.Get("settings.canvasBackgroundDimLabel"), r.X + 10 * scale, r.Y + 6 * scale, 12 * scale, Foreground, 110 * scale, false, opacity);
        var slider = CanvasBackgroundDimSliderBounds;
        float y = slider.Y + slider.Height / 2;
        float x = slider.X + slider.Width * LibrarySettings.MappingCanvasBackgroundDim / 100f;
        c.Line(slider.X, y, slider.Right, y, Foreground, 3 * scale, .35f * opacity);
        c.Line(slider.X, y, x, y, Accent, 3 * scale, opacity);
        c.Circle(x, y, 5 * scale, Accent, opacity: opacity);
        for (int corner = 1; corner <= 4; corner++)
        {
            bool left = corner is 1 or 3, top = corner is 1 or 2;
            float cx = left ? panel.X + 2 : panel.Right - 2, cy = top ? panel.Y + 2 : panel.Bottom - 2;
            c.Line(cx, cy, cx + (left ? 4 : -4), cy, Muted, opacity: opacity);
            c.Line(cx, cy, cx, cy + (top ? 4 : -4), Muted, opacity: opacity);
        }
    }
}
