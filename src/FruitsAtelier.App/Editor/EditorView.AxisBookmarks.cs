using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? cachedAxisBookmarksRaw;
    private IReadOnlyList<int> cachedAxisBookmarks = [];
    private TimingMap.Lookup? cachedAxisTimingLookup;
    private double[] cachedAxisTimingTimes = [];
    private (float Y, string Text)? axisTooltip;

    private IReadOnlyList<int> AxisBookmarks()
    {
        string? raw = OsuBeatmapReader.Setting(Document, "Editor", "Bookmarks");
        if (raw == cachedAxisBookmarksRaw) return cachedAxisBookmarks;
        cachedAxisBookmarksRaw = raw;
        return cachedAxisBookmarks = OsuTimeline.Bookmarks(Document);
    }

    private void DrawCanvasTimingMarkers(ICanvas c, List<float> occupiedLabelRows)
    {
        if (!ReferenceEquals(cachedAxisTimingLookup, renderedTiming))
        {
            cachedAxisTimingLookup = renderedTiming;
            cachedAxisTimingTimes = Document.TimingPoints.Where(point => point.Uninherited)
                .Select(point => point.TimeMs).Order().ToArray();
        }
        double[] times = cachedAxisTimingTimes;
        if (times.Length == 0) return;
        (int Count, double First, double Last, float Y)? hovered = null;
        for (int row = (int)Math.Floor(plot.Y); row <= (int)Math.Floor(plot.Bottom); row++)
        {
            double earliest = viewStart + (plot.Bottom - row - 1) / pixelsPerMs;
            double latest = viewStart + (plot.Bottom - row) / pixelsPerMs;
            int first = UpperBound(times, earliest), end = UpperBound(times, latest);
            if (first == end) continue;
            int count = end - first;
            float y = Math.Clamp(row + .5f, plot.Y + .5f, plot.Bottom - .5f);
            c.Line(plot.X, y, plot.Right, y, Error, 1.5f, .35f);
            float labelY = Math.Clamp(y - 7, plot.Y, plot.Bottom - 14);
            if (!occupiedLabelRows.Any(other => Math.Abs(other - labelY) < 14))
            {
                string label = count == 1 ? Time(times[first]) : L.Get("axis.timingCount", count);
                c.Text(label, canvas.X + 3, labelY, 10, Error, 64);
                occupiedLabelRows.Add(labelY);
            }
            if (mouseX >= canvas.X && mouseX < plot.X - 10 && Math.Abs(mouseY - y) <= 3)
                hovered = (count, times[first], times[end - 1], y);
        }
        if (hovered is { } mark)
        {
            string text = mark.Count == 1 ? L.Get("axis.timingTime", Time(mark.First))
                : L.Get("axis.timingRange", mark.Count, Time(mark.First), Time(mark.Last));
            axisTooltip = (mark.Y, text);
        }
    }

    private void DrawCanvasBookmarks(ICanvas c, List<float> occupiedLabelRows)
    {
        var bookmarks = AxisBookmarks();
        if (bookmarks.Count == 0) return;
        const uint blue = 0x4B9EF5;
        (int Count, int First, int Last, float Y)? hovered = null;
        for (int row = (int)Math.Floor(plot.Y); row <= (int)Math.Floor(plot.Bottom); row++)
        {
            double earliest = viewStart + (plot.Bottom - row - 1) / pixelsPerMs;
            double latest = viewStart + (plot.Bottom - row) / pixelsPerMs;
            int first = UpperBound(bookmarks, earliest), end = UpperBound(bookmarks, latest);
            if (first == end) continue;
            int count = end - first;
            float y = Math.Clamp(row + .5f, plot.Y + .5f, plot.Bottom - .5f);
            c.Line(plot.X, y, plot.Right, y, blue, 1.5f, .22f);
            float labelY = Math.Clamp(y - 7, plot.Y, plot.Bottom - 14);
            if (!occupiedLabelRows.Any(other => Math.Abs(other - labelY) < 14))
            {
                string label = count == 1 ? Time(bookmarks[first]) : L.Get("axis.bookmarkCount", count);
                c.Text(label, canvas.X + 3, labelY, 10, blue, 64);
                occupiedLabelRows.Add(labelY);
            }
            if (mouseX >= plot.X - 10 && mouseX < plot.X && Math.Abs(mouseY - y) <= 3)
                hovered = (count, bookmarks[first], bookmarks[end - 1], y);
        }
        if (hovered is { } mark)
        {
            string text = mark.Count == 1 ? L.Get("axis.bookmarkTime", Time(mark.First))
                : L.Get("axis.bookmarkRange", mark.Count, Time(mark.First), Time(mark.Last));
            axisTooltip = (mark.Y, text);
        }
    }

    private void DrawPendingAxisTooltip(ICanvas c)
    {
        if (axisTooltip is not { } tip) return;
        float boxWidth = Math.Min(c.MeasureText(tip.Text, 11) + 14, canvas.Width - 8);
        float x = Math.Min(plot.X + 6, canvas.Right - boxWidth - 2);
        var box = new Rect(x, Math.Clamp(tip.Y - 24, plot.Y, plot.Bottom - 24), boxWidth, 22);
        c.Clip(new(canvas.X, plot.Y, canvas.Width, plot.Height));
        c.Fill(box, Surface, 4);
        c.Text(tip.Text, box.X + 7, box.Y + 4, 11, Foreground, box.Width - 12);
        c.Unclip();
    }

    private static int UpperBound(IReadOnlyList<int> values, double time)
    {
        int lo = 0, hi = values.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (values[mid] <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(IReadOnlyList<double> values, double time)
    {
        int lo = 0, hi = values.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (values[mid] <= time) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
