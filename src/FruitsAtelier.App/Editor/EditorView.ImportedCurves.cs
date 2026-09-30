using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawImportedCurves(ICanvas c, float left, float fieldWidth, float bottom, double originTime,
        double pixelsPerTime, double visibleStart, double visibleEnd)
    {
        foreach (var slider in conversion!.Sliders.Where(s => s.IsImported))
        {
            if (slider.StartTimeMs > visibleEnd || slider.StartTimeMs + slider.DurationMs < visibleStart || slider.Path.Count < 2) continue;
            double spanDuration = slider.DurationMs / slider.SpanCount;
            float opacity = IsObjectSelected(slider.SourceId) ? 1 : 0.5f;
            double[] distances = new double[slider.Path.Count];
            for (int i = 1; i < distances.Length; i++)
            {
                var a = slider.Path[i - 1]; var b = slider.Path[i];
                distances[i] = distances[i - 1] + Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.GeometryY - a.GeometryY, 2));
            }
            if (distances[^1] <= 0) continue;
            for (int span = 0; span < slider.SpanCount; span++)
            {
                double start = slider.StartTimeMs + span * spanDuration;
                if (start > visibleEnd || start + spanDuration < visibleStart) continue;
                (float X, float Y)? previous = null;
                for (int n = 0; n < slider.Path.Count; n++)
                {
                    int index = span % 2 == 0 ? n : slider.Path.Count - 1 - n;
                    double progress = distances[index] / distances[^1];
                    if (span % 2 != 0) progress = 1 - progress;
                    double time = start + progress * spanDuration;
                    float x = left + (float)(slider.Path[index].X / 512) * fieldWidth;
                    float y = bottom - (float)((time - originTime) * pixelsPerTime);
                    if (previous is { } p) c.Line(p.X, p.Y, x, y, Purple, IsObjectSelected(slider.SourceId) ? 2.6f : 2, opacity);
                    previous = (x, y);
                }
            }
        }
    }
}
