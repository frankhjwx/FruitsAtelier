using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private CatchConversionResult? importedCurveSnapshot;
    private readonly Dictionary<GeneratedSlider, double[]> importedCurveDistances = [];
    private readonly Dictionary<Guid, ImportedSlider> importedCurveSources = [];
    private readonly Dictionary<GeneratedSlider, IReadOnlyList<SliderPathPoint>> importedCurvePaths = [];
    private (float Left, float Width, float Bottom, double Origin, double Scale, double Start, double End) importedCurveView;
    private readonly Dictionary<(GeneratedSlider Slider, int Span), System.Numerics.Vector2[]> importedCurveScreens = [];

    private IReadOnlyList<SliderPathPoint> ImportedCurvePath(GeneratedSlider slider)
    {
        if (!ReferenceEquals(importedCurveSnapshot, conversion))
        {
            importedCurveSnapshot = conversion;
            importedCurveDistances.Clear();
            importedCurveScreens.Clear();
            importedCurveSources.Clear();
            importedCurvePaths.Clear();
            var snapshot = convertedSnapshot ?? Document;
            if (snapshot.SliderImportState == SliderImportState.Native)
                foreach (var source in snapshot.ImportedSliders)
                    if (source.RandomizationSource is { } original) importedCurveSources[source.Id] = original;
        }
        if (!importedCurvePaths.TryGetValue(slider, out var path))
            importedCurvePaths[slider] = path = importedCurveSources.TryGetValue(slider.SourceId, out var source)
                ? ImportedSliderConverter.Path(source) : slider.Path;
        return path;
    }

    private double[] ImportedCurveDistances(GeneratedSlider slider)
    {
        var path = ImportedCurvePath(slider);
        if (importedCurveDistances.TryGetValue(slider, out var distances)) return distances;
        distances = new double[path.Count];
        for (int i = 1; i < distances.Length; i++)
        {
            var a = path[i - 1]; var b = path[i];
            distances[i] = distances[i - 1] + Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.GeometryY - a.GeometryY, 2));
        }
        importedCurveDistances.Add(slider, distances);
        return distances;
    }

    private void DrawImportedCurves(ICanvas c, float left, float fieldWidth, float bottom, double originTime,
        double pixelsPerTime, double visibleStart, double visibleEnd)
    {
        var view = (left, fieldWidth, bottom, originTime, pixelsPerTime, visibleStart, visibleEnd);
        if (view != importedCurveView) { importedCurveView = view; importedCurveScreens.Clear(); }
        foreach (var slider in conversion!.Sliders.Where(s => s.IsImported))
        {
            if (slider.StartTimeMs > visibleEnd || slider.StartTimeMs + slider.DurationMs < visibleStart || slider.Path.Count < 2) continue;
            double spanDuration = slider.DurationMs / slider.SpanCount;
            var path = ImportedCurvePath(slider);
            bool selected = IsObjectSelected(slider.SourceId);
            float opacity = selected ? 1 : .5f;
            var distances = ImportedCurveDistances(slider);
            if (distances[^1] <= 0 || spanDuration <= 0) continue;
            for (int span = 0; span < slider.SpanCount; span++)
            {
                double start = slider.StartTimeMs + span * spanDuration;
                if (start > visibleEnd || start + spanDuration < visibleStart) continue;
                if (importedCurveScreens.TryGetValue((slider, span), out var cached))
                {
                    c.Polyline(cached, 0, 0, 1, Purple, selected ? 2.6f : 2, opacity);
                    continue;
                }
                double from = Math.Clamp((visibleStart - start) / spanDuration, 0, 1);
                double to = Math.Clamp((visibleEnd - start) / spanDuration, 0, 1);
                bool reverse = span % 2 != 0;
                int first = Math.Max(0, LowerBound(distances, distances[^1] * (reverse ? 1 - to : from)) - 1);
                int last = Math.Min(distances.Length - 1, LowerBound(distances, distances[^1] * (reverse ? 1 - from : to)) + 1);
                (float X, float Y)? previous = null;
                var screen = new List<System.Numerics.Vector2>();
                for (int n = first; n <= last; n++)
                {
                    int index = reverse ? last - (n - first) : n;
                    double progress = distances[index] / distances[^1];
                    if (span % 2 != 0) progress = 1 - progress;
                    double time = start + progress * spanDuration;
                    float x = left + (float)(path[index].X / 512) * fieldWidth;
                    float y = bottom - (float)((time - originTime) * pixelsPerTime);
                    if (previous is { } p)
                    {
                        float dx = x - p.X, dy = y - p.Y;
                        if (n < last && dx * dx + dy * dy < .0625f) continue;
                    }
                    screen.Add(new(x, y));
                    previous = (x, y);
                }
                cached = screen.ToArray(); importedCurveScreens.Add((slider, span), cached);
                c.Polyline(cached, 0, 0, 1, Purple, selected ? 2.6f : 2, opacity);
            }
        }
    }

}
