namespace FruitsAtelier.Core;

internal static class ExportTiming
{
    private static readonly int[] divisors = [16, 12, 9, 7, 5, 24, 32, 48];
    // Match stable's integer grid without pulling deliberately off-grid objects by more than one millisecond.
    internal static double CorrectHead(TimingMap.Lookup timing, double time)
    {
        if (!double.IsFinite(time) || time < 0) return time;
        if (timing.RedTimes.Length > 0 && time < timing.RedTimes[0]) return time;
        var state = timing.At(time);
        double integer = OsuBeatmapWriter.QuantizeTime(time);
        double best = integer, distance = double.PositiveInfinity;
        foreach (int divisor in divisors)
        {
            double grid = timing.Snap(time, divisor);
            double candidate = OsuBeatmapWriter.QuantizeTime(grid);
            if (candidate == integer) return time;
            if (Math.Abs(candidate - integer) != 1 || Math.Abs(grid - time) >= distance) continue;
            if (timing.At(grid).OffsetMs != state.OffsetMs) continue;
            best = candidate; distance = Math.Abs(grid - time);
        }
        return best == integer ? time : time + (best - integer);
    }

    internal static MapDocument Prepare(MapDocument document, out TimingMap.Lookup timing)
    {
        timing = new(document);
        var lookup = timing;
        var heads = document.Fruits.Select(f => f.TimeMs);
        if (!heads.Any(t => double.IsFinite(t) && t >= 0 && CorrectHead(lookup, t) != t)) return document;
        var occupied = document.Fruits.Select(f => OsuBeatmapWriter.QuantizeTime(f.TimeMs))
            .Concat(document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => OsuBeatmapWriter.QuantizeTime(t.Nodes[0].TimeMs)))
            .Concat(document.ImportedSliders.Select(s => OsuBeatmapWriter.QuantizeTime(s.TimeMs)))
            .Concat(document.BananaShowers.Select(s => OsuBeatmapWriter.QuantizeTime(s.TimeMs))).ToHashSet();
        var corrections = document.Fruits.Select(f => (f.Id, Original: f.TimeMs, Corrected: CorrectHead(lookup, f.TimeMs)))
            .Where(f => f.Original != f.Corrected && !occupied.Contains(OsuBeatmapWriter.QuantizeTime(f.Corrected)))
            .GroupBy(f => OsuBeatmapWriter.QuantizeTime(f.Corrected))
            .Where(g => g.Select(f => OsuBeatmapWriter.QuantizeTime(f.Original)).Distinct().Count() == 1)
            .SelectMany(g => g).ToDictionary(f => f.Id, f => f.Corrected);
        if (corrections.Count == 0) return document;
        var prepared = document.DeepClone();
        foreach (var fruit in prepared.Fruits)
            if (corrections.TryGetValue(fruit.Id, out double corrected)) fruit.TimeMs = corrected;
        return prepared;
    }
}
