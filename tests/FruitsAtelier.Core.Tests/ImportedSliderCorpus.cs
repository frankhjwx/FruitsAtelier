using System.Diagnostics;
using FruitsAtelier.Core;

internal static class ImportedSliderCorpus
{
    public static int Run(string workspace)
    {
        int total = 0, converted = 0, anchors = 0, oldAnchors = 0;
        foreach (string file in Directory.EnumerateFiles(workspace, "*.catchdiff", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) != WorkspaceProject.ManifestName && !WorkspaceProject.Within(Path.Combine(workspace, "Resources"), p)))
        {
            // Read only: never open a workspace session (which may recover writes) or save user data.
            var document = ProjectSerializer.ReadFile(file);
            var original = document.DeepClone(); var ids = original.ImportedSliders.Select(s => s.Id).ToHashSet();
            var watch = Stopwatch.StartNew(); var result = ImportedSliderEditing.ConvertAll(document); watch.Stop();
            Console.WriteLine($"{Path.GetFileName(file)}: {result.Tracks.Count}/{ids.Count} converted, {result.Tracks.Sum(t => t.Nodes.Count)} anchors, {watch.Elapsed.TotalSeconds:F2}s");
            var sourceLookup = original.ImportedSliders.ToDictionary(s => s.Id);
            var baseline = result.Tracks.ToDictionary(t => t.Id, t => OldNodeCount(original, sourceLookup[t.Id]));
            oldAnchors += baseline.Values.Sum();
            foreach (var track in result.Tracks.OrderByDescending(t => CurveMath.EndTimeMs(t) - t.Nodes[0].TimeMs).Take(3))
            {
                var slider = sourceLookup[track.Id];
                double maxError = 0;
                var geometry = new ImportedSliderGeometry(slider);
                double start = slider.TimeMs, end = ImportedSliderConverter.EndTimeMs(original, slider);
                for (int i = 0; i <= 1000; i++)
                {
                    double time = start + (end - start) * i / 1000;
                    maxError = Math.Max(maxError, Math.Abs(Math.Clamp((float)slider.X + geometry.PositionAt((CurveMath.FirstSpanTime(track, time) - start) / (track.Nodes[^1].TimeMs - start)).X, 0, 512) - CurveMath.PositionAtTime(track, time)));
                }
                if (maxError > ImportedSliderEditing.ApproximationTolerance + 0.0001) throw new Exception("Corpus trajectory error exceeded tolerance.");
                Console.WriteLine($"  long slider @{start:F1} duration={end-start:F1}ms nodes={baseline[track.Id]} -> {track.Nodes.Count} curves={track.Nodes.Count(n => n.OutgoingKind == CurveKind.Bezier)} max sampled error={maxError:F5}");
            }
            foreach (var failure in result.Failures.Select(f => (Failure: f, Slider: original.ImportedSliders.Single(s => s.Id == f.Id)))
                .OrderByDescending(pair => ImportedSliderConverter.DurationMs(original, pair.Slider)).Take(2))
                Console.WriteLine($"  failed long @{failure.Slider.TimeMs:F1}: {ImportedSliderConverter.DurationMs(original, failure.Slider):F1}ms, repeats={failure.Slider.SpanCount}");
            foreach (var group in result.Failures.GroupBy(f => f.Reason)) Console.WriteLine($"  preserved {group.Count()}: {group.Key}");
            total += ids.Count; converted += result.Tracks.Count; anchors += result.Tracks.Sum(t => t.Nodes.Count);
        }
        Console.WriteLine($"TOTAL: {converted}/{total} converted; {oldAnchors} -> {anchors} editable anchors"); return 0;
    }
    private static int OldNodeCount(MapDocument document, ImportedSlider slider)
    {
        var path = new ImportedSliderGeometry(slider); var timing = TimingMap.At(document, slider.TimeMs);
        var points = path.TimeXPoints(slider, LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, timing.SliderVelocityMultiplier));
        var pending = new Stack<(int Start, int End)>(); pending.Push((0, points.Count - 1)); int count = 2;
        while (pending.TryPop(out var range))
        {
            var a = points[range.Start]; var b = points[range.End]; double maximum = 0.001; int split = -1;
            for (int i = range.Start + 1; i < range.End; i++)
            {
                double expected = a.X + (b.X - a.X) * (points[i].TimeMs - a.TimeMs) / (b.TimeMs - a.TimeMs);
                double error = Math.Abs(points[i].X - expected);
                if (error > maximum) { maximum = error; split = i; }
            }
            if (split < 0) continue;
            count++; pending.Push((range.Start, split)); pending.Push((split, range.End));
        }
        return count;
    }
}
