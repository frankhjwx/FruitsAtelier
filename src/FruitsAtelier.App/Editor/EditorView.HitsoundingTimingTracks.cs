using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const float HitsoundingTimingRowHeight = 24;
    private const uint HitsoundingKiaiOn = 0xD89532, HitsoundingKiaiOff = 0x54728C;
    private readonly List<(Rect Bounds, TimingPoint Point)> hitsoundingBpmBoxes = [];
    private (Rect Bounds, string Text)? hitsoundingTimingTooltip;
    private sealed record HitsoundingTimingRun(double Start, double End, double Value, string Text, uint Colour);
    private HitsoundingTimingRun[][] hitsoundingTimingRuns = [];
    private HitsoundingTimingLabel[]? hitsoundingRunSource;
    private double hitsoundingRunDelta;
    private string? hitsoundingRunLanguage;

    private void RefreshHitsoundingTimingRuns()
    {
        var source = hitsoundingTimingPreview ?? hitsoundingTimingLabels;
        if (ReferenceEquals(source, hitsoundingRunSource) && hitsoundingRunDelta == hitsoundingTimingDelta && hitsoundingRunLanguage == L.Language) return;
        hitsoundingRunSource = source; hitsoundingRunDelta = hitsoundingTimingDelta; hitsoundingRunLanguage = L.Language;
        hitsoundingTimingRuns = new HitsoundingTimingRun[4][];
        for (int lane = 0; lane < 4; lane++)
        {
            var points = lane == 0 ? source.Where(p => p.Red).ToArray() : source;
            var runs = new List<HitsoundingTimingRun>();
            for (int i = 0; i < points.Length; i++)
            {
                var point = points[i];
                // Only the last point at a tied timestamp owns a non-empty interval.
                if (i + 1 < points.Length && points[i + 1].Time == point.Time) continue;
                double value = lane switch { 0 => TimingMap.EffectiveBeatLength(point.Point.BeatLengthMs), 1 => point.Index,
                    2 => point.Volume, _ => point.Point.Effects & 1 };
                string text = lane switch { 0 => TimingN(60000 / value), 1 => point.Index > 0 ? $"{point.Bank}:{point.Index}" : point.Bank,
                    2 => $"{point.Volume}%", _ => L.Get(value != 0 ? "inspector.on" : "inspector.off") };
                if (runs.Count > 0 && runs[^1].Value == value && runs[^1].Text == text) continue;
                if (runs.Count > 0) runs[^1] = runs[^1] with { End = point.Time };
                uint colour = lane == 0 ? 0xE77785u : lane == 3 ? value != 0 ? HitsoundingKiaiOn : HitsoundingKiaiOff : 0x72C686u;
                runs.Add(new(point.Time, double.PositiveInfinity, value, text, colour));
            }
            hitsoundingTimingRuns[lane] = runs.ToArray();
        }
    }

    private void DrawHitsoundingTimingTracks(ICanvas c, double start, double end)
    {
        RefreshHitsoundingTimingRuns();
        hitsoundingBpmBoxes.Clear(); hitsoundingTimingTooltip = null;
        float X(double time) => hitsoundingGridBounds.X + (float)((time - start) / hitsoundingSpan * hitsoundingGridBounds.Width);
        for (int lane = 0; lane < 4; lane++)
        {
            var runs = hitsoundingTimingRuns[lane];
            int lo = 0, hi = runs.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (runs[mid].End <= start) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < runs.Length && runs[i].Start < end; i++)
            {
                var run = runs[i];
                float left = X(Math.Max(start, run.Start)), right = X(Math.Min(end, run.End));
                if (right <= left) continue;
                var box = new Rect(left, hitsoundingBounds.Y + 53 + lane * HitsoundingTimingRowHeight, right - left, HitsoundingTimingRowHeight - 2);
                bool hovered = DockContentInputAvailable && box.Contains(mouseX, mouseY);
                c.Fill(box, Panel, 3); c.Fill(box, run.Colour, 3, hovered ? .4f : .25f); c.Stroke(box, run.Colour, 1, 3);
                if (c.MeasureText(run.Text, 11, true) <= box.Width - 10)
                    c.Text(run.Text, box.X + 6, box.Y + 4, 11, 0xFFFFFF, box.Width - 10, true);
                else if (hovered) hitsoundingTimingTooltip = (box, run.Text);
            }
        }
        // Hit regions retain the original red points even when equal BPM intervals merge visually.
        int first = 0, last = hitsoundingRedPoints.Length;
        while (first < last) { int mid = (first + last) / 2; if (hitsoundingRedPoints[mid].TimeMs < start) first = mid + 1; else last = mid; }
        for (int i = Math.Max(0, first - 1); i < hitsoundingRedPoints.Length && hitsoundingRedPoints[i].TimeMs <= end; i++)
        {
            var point = hitsoundingRedPoints[i];
            double until = i + 1 < hitsoundingRedPoints.Length ? hitsoundingRedPoints[i + 1].TimeMs : end;
            float left = X(Math.Max(start, point.TimeMs)), right = X(Math.Min(end, until));
            if (right <= left) continue;
            var box = new Rect(left, hitsoundingBounds.Y + 53, right - left, HitsoundingTimingRowHeight - 2);
            hitsoundingBpmBoxes.Add((box, point));
            if (hitsoundingSelectedTiming == point) c.Stroke(box, 0xE77785, 2.5f, 3);
        }
    }
}
