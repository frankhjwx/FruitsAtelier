using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private DiscussionImage? discussionPreview;
    private float discussionPreviewZoom = 1, discussionPreviewX, discussionPreviewY, discussionPreviewPointerX, discussionPreviewPointerY;
    private bool discussionPreviewDragging, discussionPreviewMoved, discussionPreviewCanPan;
    private float discussionPreviewPressX, discussionPreviewPressY;
    private Rect discussionPreviewViewport, discussionPreviewBounds;
    internal bool DiscussionImageVisible => discussionPreview is not null && !LibraryVisible;
    internal float DiscussionImageZoom => discussionPreviewZoom;
    internal Rect DiscussionImageViewport => discussionPreviewViewport;
    internal Rect DiscussionImagePreviewBounds => discussionPreviewBounds;

    private void OpenDiscussionImage(DiscussionImage image)
    {
        CancelInteraction(); discussionPreview = image;
        discussionPreviewZoom = 1; discussionPreviewX = discussionPreviewY = 0;
    }

    private void CloseDiscussionImage() { discussionPreview = null; discussionPreviewDragging = false; }

    private float DiscussionImageScale => discussionPreviewZoom;

    private void ClampDiscussionImagePan()
    {
        if (discussionPreview is not { } image) return;
        float x = Math.Max(0, (image.Width * DiscussionImageScale - discussionPreviewViewport.Width) / 2);
        float y = Math.Max(0, (image.Height * DiscussionImageScale - discussionPreviewViewport.Height) / 2);
        discussionPreviewX = Math.Clamp(discussionPreviewX, -x, x);
        discussionPreviewY = Math.Clamp(discussionPreviewY, -y, y);
    }

    private void DrawDiscussionImagePreview(ICanvas c)
    {
        if (!DiscussionImageVisible || discussionPreview is not { } image) return;
        c.Fill(new(0, 0, width, height), 0x080B10, opacity: .85f);
        float panelWidth = Math.Min(width - 32, Math.Max(360, width * .45f));
        float panelHeight = Math.Min(height - 32, Math.Max(260, height * .45f));
        discussionPreviewBounds = new((width - panelWidth) / 2, (height - panelHeight) / 2, panelWidth, panelHeight);
        c.Fill(discussionPreviewBounds, 0x1A2028, 5); c.Stroke(discussionPreviewBounds, Grid, radius: 5);
        discussionPreviewViewport = new(discussionPreviewBounds.X + 16, discussionPreviewBounds.Y + 36,
            Math.Max(1, panelWidth - 32), Math.Max(1, panelHeight - 52));
        ClampDiscussionImagePan();
        float w = image.Width * DiscussionImageScale, h = image.Height * DiscussionImageScale;
        var bounds = new Rect(discussionPreviewViewport.X + (discussionPreviewViewport.Width - w) / 2 + discussionPreviewX,
            discussionPreviewViewport.Y + (discussionPreviewViewport.Height - h) / 2 + discussionPreviewY, w, h);
        c.Clip(discussionPreviewViewport); c.Image(image.Path, bounds); c.Unclip();
        c.Text(L.Get("ui.zoomPercent", DiscussionImageScale * 100), discussionPreviewBounds.X + 16, discussionPreviewBounds.Y + 10,
            12, Muted, panelWidth - 32);
    }

    private void DiscussionImagePointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        discussionPreviewDragging = true; discussionPreviewPointerX = x; discussionPreviewPointerY = y;
        discussionPreviewPressX = x; discussionPreviewPressY = y; discussionPreviewMoved = false;
        discussionPreviewCanPan = discussionPreviewViewport.Contains(x, y);
    }

    private void DiscussionImagePointerMove(float x, float y)
    {
        if (!discussionPreviewDragging) return;
        if (!discussionPreviewMoved && Math.Abs(x - discussionPreviewPressX) + Math.Abs(y - discussionPreviewPressY) < ContentDragThreshold) return;
        discussionPreviewMoved = true;
        if (!discussionPreviewCanPan) return;
        discussionPreviewX += x - discussionPreviewPointerX; discussionPreviewY += y - discussionPreviewPointerY;
        discussionPreviewPointerX = x; discussionPreviewPointerY = y; ClampDiscussionImagePan();
    }

    private void DiscussionImagePointerUp(float x, float y, int button)
    {
        if (button != 0 || !discussionPreviewDragging) return;
        DiscussionImagePointerMove(x, y);
        discussionPreviewDragging = false;
        if (!discussionPreviewMoved) CloseDiscussionImage();
    }

    private void DiscussionImageWheel(float x, float y, float delta, bool ctrl)
    {
        if (!ctrl || !discussionPreviewViewport.Contains(x, y)) return;
        float previous = discussionPreviewZoom;
        discussionPreviewZoom = Math.Clamp(previous * (float)Math.Pow(1.2, Math.Clamp(delta / 120, -8, 8)), .1f, 16);
        float ratio = discussionPreviewZoom / previous;
        float cursorX = x - (discussionPreviewViewport.X + discussionPreviewViewport.Width / 2);
        float cursorY = y - (discussionPreviewViewport.Y + discussionPreviewViewport.Height / 2);
        discussionPreviewX = cursorX - (cursorX - discussionPreviewX) * ratio;
        discussionPreviewY = cursorY - (cursorY - discussionPreviewY) * ratio;
        ClampDiscussionImagePan();
    }
}
