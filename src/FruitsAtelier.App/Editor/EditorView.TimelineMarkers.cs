using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawTimelinePreviewMarker(ICanvas c, Rect bounds, double start, double end)
    {
        if (OsuTimeline.PreviewTime(Document) is int preview && preview >= start && preview <= end)
        {
            float x = bounds.X + (float)((preview - start) / (end - start) * bounds.Width);
            c.Line(x, bounds.Y - 3, x, bounds.Bottom + 2, 0xFFD34A, 2, .8f);
        }
    }

    private void DrawTimelineBookmarks(ICanvas c, Rect bounds, double start, double end)
    {
        foreach (int bookmark in AxisBookmarks())
        {
            if (bookmark < start || bookmark > end) continue;
            float x = bounds.X + (float)((bookmark - start) / (end - start) * bounds.Width);
            c.Line(x, bounds.Y + bounds.Height / 2, x, bounds.Bottom - 1, 0x4B9EF5, 1, .8f);
        }
    }
}
