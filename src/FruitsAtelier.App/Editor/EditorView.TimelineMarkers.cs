using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawTimelineLocationMarkers(ICanvas c, Rect bounds, double start, double end)
    {
        float X(double time) => bounds.X + (float)((time - start) / (end - start) * bounds.Width);
        float middle = bounds.Y + bounds.Height / 2;
        if (OsuTimeline.PreviewTime(Document) is int preview && preview >= start && preview <= end)
        {
            float x = X(preview);
            c.Line(x, bounds.Y + 2, x, middle, 0xFFD34A, 2, .8f);
            c.Fill(new(Math.Clamp(x - 4, bounds.X, bounds.Right - 8), bounds.Y + 2, 8, 5), 0xFFD34A);
        }
        foreach (int bookmark in AxisBookmarks())
        {
            if (bookmark < start || bookmark > end) continue;
            float x = X(bookmark);
            c.Line(x, middle, x, bounds.Bottom - 1, 0x4B9EF5, 1, .8f);
            c.Fill(new(Math.Clamp(x - 4, bounds.X, bounds.Right - 8), bounds.Bottom - 7, 8, 5), 0x4B9EF5);
        }
    }
}
