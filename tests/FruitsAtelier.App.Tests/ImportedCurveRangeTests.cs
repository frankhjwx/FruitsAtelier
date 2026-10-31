using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class ImportedCurveRangeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private delegate void DrawCurves(ICanvas canvas, float left, float width, float bottom, double origin,
        double scale, double start, double end);

    internal static void Run(bool performance)
    {
        var ui = new Ui(false); ui.LoadDocument(new MapDocument { IsDemo = false });
        var draw = typeof(EditorView).GetMethod("DrawImportedCurves", Private)!.CreateDelegate<DrawCurves>(ui.View);
        uint purple = (uint)typeof(EditorView).GetProperty("Purple", Private)!.GetValue(ui.View)!;
        var sliders = Enumerable.Range(0, performance ? 50000 : 400).Select(i => Slider(i * 1000.25, 700.125, 3, i)).ToList();
        sliders.Insert(5, Slider(-100, 60000000, 100000, 7));
        sliders.Insert(0, Slider(5000, 1300, 1, 8));
        sliders.Insert(2, Slider(5000, 1300, 1, 9));
        sliders.Reverse();
        Publish(sliders);
        var actual = new RecordingCanvas();
        foreach (double start in new[] { -101d, -100, 0, 4999.75, 5000, 5100, 6000, 6300, 120000, 60000000d })
        {
            actual.Clear(); draw(actual, 13, 487, 900, start, .625, start, start + 100);
            var expected = Reference(sliders, start, start + 100, purple);
            Check(actual.Lines.SequenceEqual(expected), $"Imported paths differ from full scan at {start}.");
        }
        var replacement = new[] { Slider(5000, 300, 2, 11) };
        Publish(replacement);
        typeof(EditorView).GetMethod("ImportedCurvePath", Private)!.Invoke(ui.View, [replacement[0]]);
        if (typeof(EditorView).GetField("importedCurveRange", Private)?.GetValue(ui.View) is { } retiredRange)
            Check(((double[])retiredRange.GetType().GetField("starts", Private)!.GetValue(retiredRange)!).Length == 0,
                "Path lookup retained the previous conversion's body range before drawing.");
        actual.Clear(); draw(actual, 13, 487, 900, 5000, .625, 5000, 5100);
        Check(actual.Lines.SequenceEqual(Reference(replacement, 5000, 5100, purple)), "Replacement retained old imported paths.");
        Publish(sliders);
        actual.Clear(); draw(actual, 13, 487, 900, 5000, .625, 5000, 5100);
        Check(actual.Lines.SequenceEqual(Reference(sliders, 5000, 5100, purple)), "Restoring the previous conversion retained replacement paths.");
        var selected = (HashSet<Guid>)typeof(EditorView).GetField("objectSelection", Private)!.GetValue(ui.View)!;
        selected.Add(sliders[0].SourceId);
        selected.Add(sliders[^1].SourceId);
        selected.Add(sliders.First(slider => slider.StartTimeMs == 5000).SourceId);
        actual.Clear(); draw(actual, 13, 487, 900, 5000, .625, 5000, 5100);
        Check(actual.Lines.SequenceEqual(Reference(sliders, 5000, 5100, purple, selected)), "Selection changed path coordinates or paint order/style.");
        selected.Clear();
        if (performance)
        {
            var samples = new double[120]; var bytes = new long[samples.Length];
            var canvas = new CountCanvas();
            long coldBytes = GC.GetAllocatedBytesForCurrentThread(), coldStart = Stopwatch.GetTimestamp();
            Publish(sliders);
            draw(canvas, 13, 487, 900, 5000, .625, 5000, 5100);
            Console.WriteLine($"Imported curve cold: CPU={Stopwatch.GetElapsedTime(coldStart).TotalMilliseconds:F4}ms allocation={GC.GetAllocatedBytesForCurrentThread() - coldBytes}B");
            long indexBytes = 0;
            foreach (var (name, elementBytes) in new[] { ("importedCurveSliders", 8), ("importedCurveOrder", 4) })
                if (typeof(EditorView).GetField(name, Private)?.GetValue(ui.View) is Array array) indexBytes += array.LongLength * elementBytes;
            if (typeof(EditorView).GetField("importedCurveRange", Private)?.GetValue(ui.View) is { } range)
                foreach (string name in new[] { "starts", "maximumEnds" })
                    if (range.GetType().GetField(name, Private)!.GetValue(range) is double[] array) indexBytes += array.LongLength * 8;
            Console.WriteLine($"Imported curve index retained array payload={indexBytes}B (array/object headers and reusable visible-list capacity excluded).");
            for (int i = -20; i < samples.Length; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                draw(canvas, 13, 487, 900, 5000, .625, 5000, 5100);
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (i < 0) continue;
                samples[i] = elapsed; bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            Array.Sort(samples); Array.Sort(bytes);
            Console.WriteLine($"Imported curve warm: sliders={sliders.Count} repeats=100000 CPU p50={samples[60]:F4}ms p95={samples[114]:F4}ms allocation p50={bytes[60]}B; counting canvas excludes GPU submission.");
        }
        ui.View.NewProject();
        Console.WriteLine("PASS imported curve exact full-scan line equivalence, tied starts, overlaps, repeat boundaries and conversion replacement/restoration");

        void Publish(IReadOnlyList<GeneratedSlider> source) => typeof(EditorView).GetField("conversion", Private)!.SetValue(ui.View,
            new CatchConversionResult { Sliders = source, Objects = [], Diagnostics = [], Success = true });
    }

    private static GeneratedSlider Slider(double start, double duration, int spans, int shape) => new()
    {
        SourceId = Guid.NewGuid(), IsImported = true, StartTimeMs = start, DurationMs = duration, SpanCount = spans,
        Velocity = 1, SliderVelocityMultiplier = 1, TickDistance = 10, Length = 100, TinyCompensationApplied = false,
        Path = [new(20 + shape % 11, 30), new(140 + shape % 13, 42), new(240 + shape % 17, 80)]
    };

    // Independent full scan retains the original drawing arithmetic and chronological repeat traversal.
    private static List<RecordingCanvas.Segment> Reference(IReadOnlyList<GeneratedSlider> sliders, double fromTime, double toTime, uint purple,
        HashSet<Guid>? selected = null)
    {
        var result = new List<RecordingCanvas.Segment>();
        foreach (var slider in sliders)
        {
            if (!slider.IsImported || slider.StartTimeMs > toTime || slider.StartTimeMs + slider.DurationMs < fromTime || slider.Path.Count < 2) continue;
            var path = slider.Path; var distances = new double[path.Count];
            for (int i = 1; i < path.Count; i++) distances[i] = distances[i - 1] + Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].GeometryY - path[i - 1].GeometryY, 2));
            double duration = slider.DurationMs / slider.SpanCount;
            for (int span = 0; span < slider.SpanCount; span++)
            {
                double start = slider.StartTimeMs + span * duration;
                if (start > toTime || start + duration < fromTime) continue;
                double from = Math.Clamp((fromTime - start) / duration, 0, 1), to = Math.Clamp((toTime - start) / duration, 0, 1);
                bool reverse = span % 2 != 0;
                int first = Math.Max(0, Bound(distances[^1] * (reverse ? 1 - to : from)) - 1);
                int last = Math.Min(path.Count - 1, Bound(distances[^1] * (reverse ? 1 - from : to)) + 1);
                (float X, float Y)? previous = null;
                for (int n = first; n <= last; n++)
                {
                    int index = reverse ? last - (n - first) : n;
                    double progress = distances[index] / distances[^1];
                    if (reverse) progress = 1 - progress;
                    float x = 13 + (float)(path[index].X / 512) * 487;
                    float y = 900 - (float)((start + progress * duration - fromTime) * .625);
                    if (previous is { } p)
                    {
                        float dx = x - p.X, dy = y - p.Y;
                        if (n < last && dx * dx + dy * dy < .0625f) continue;
                        bool active = selected?.Contains(slider.SourceId) == true;
                        result.Add(new(p.X, p.Y, x, y, purple, active ? 1 : .5f, active ? 2.6f : 2));
                    }
                    previous = (x, y);
                }
                int Bound(double value) { int i = 0; while (i < distances.Length && distances[i] < value) i++; return i; }
            }
        }
        return result;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class CountCanvas : ICanvas
    {
        public void Fill(Rect r, uint c, float radius = 0, float opacity = 1) { }
        public void Stroke(Rect r, uint c, float width = 1, float radius = 0) { }
        public void Line(float x, float y, float x2, float y2, uint c, float width = 1, float opacity = 1) { }
        public void Circle(float x, float y, float radius, uint c, bool filled = true, float width = 1, float opacity = 1) { }
        public void Text(string text, float x, float y, float size, uint c, float maxWidth = 10000, bool bold = false) { }
        public bool Image(string path, Rect r, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1) => false;
        public void Clip(Rect r) { }
        public void Unclip() { }
    }
}
