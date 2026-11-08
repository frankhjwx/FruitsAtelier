using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private (Rect Bounds, string Text)? timelineBpmTooltip;
    private void DrawTimelineBpmFlags(ICanvas c, double start, double end)
    {
        timelineBpmTooltip = null;
        int lo = 0, hi = timelineBpmPoints.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (timelineBpmPoints[mid].TimeMs < start) lo = mid + 1; else hi = mid; }
        float previousRight = objectTimeline.X;
        for (int i = lo; i < timelineBpmPoints.Length && timelineBpmPoints[i].TimeMs <= end; i++)
        {
            var point = timelineBpmPoints[i];
            float x = objectTimeline.X + (float)((point.TimeMs - start) * objectTimelineScale);
            string label = L.Get("timeline.bpmValue", 60000 / TimingMap.EffectiveBeatLength(point.BeatLengthMs));
            float labelWidth = c.MeasureText(label, 10, true) + 10;
            var flag = new Rect(x, objectTimeline.Y + 1, labelWidth, 16);
            bool fits = x >= previousRight && flag.Right <= objectTimeline.Right;
            var hit = fits ? flag : new Rect(x - 3, flag.Y, 6, flag.Height);
            if (DockContentInputAvailable && hit.Contains(mouseX, mouseY)) timelineBpmTooltip = (hit, label);
            if (!fits) continue;
            c.Fill(flag, Panel, 2);
            c.Fill(flag, 0xE77785, 2, .25f);
            c.Line(x, flag.Y, x, flag.Bottom + 4, 0xE77785, 2);
            c.Text(label, x + 5, flag.Y + 2, 10, Foreground, labelWidth - 10, true);
            previousRight = flag.Right + 3;
        }
    }

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
            c.Line(x, bounds.Y + bounds.Height / 2, x, bounds.Bottom - 1, Document.BookmarkAppearanceAt(bookmark).Colour, 1, .8f);
        }
    }
}
