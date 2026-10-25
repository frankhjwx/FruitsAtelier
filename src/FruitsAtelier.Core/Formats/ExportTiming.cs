namespace FruitsAtelier.Core;

internal static class ExportTiming
{
    private static readonly int[] divisors = [16, 12, 9, 7, 5, 24, 32, 48];
    // Only repair one-millisecond offsets in exported integer timestamps.
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
        return best == integer ? time : best;
    }

    internal static MapDocument Prepare(MapDocument document, out TimingMap.Lookup timing)
    {
        timing = new(document);
        var lookup = timing;
        var heads = document.Fruits.Select(f => (f.Id, Original: f.TimeMs, Corrected: CorrectHead(lookup, f.TimeMs)))
            .Concat(document.BananaShowers.Select(s => (s.Id, Original: s.TimeMs, Corrected: CorrectSpinnerHead(lookup, s.TimeMs))));
        var ends = document.BananaShowers.Select(s => (s.Id, Original: s.EndTimeMs, Corrected: CorrectHead(lookup, s.EndTimeMs)))
            .Where(s => s.Original != s.Corrected).ToDictionary(s => s.Id, s => s.Corrected);
        if (ends.Count == 0 && !heads.Any(h => h.Original != h.Corrected)) return document;
        var occupied = document.Fruits.Select(f => OsuBeatmapWriter.QuantizeTime(f.TimeMs))
            .Concat(document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => OsuBeatmapWriter.QuantizeTime(t.Nodes[0].TimeMs)))
            .Concat(document.ImportedSliders.Select(s => OsuBeatmapWriter.QuantizeTime(s.TimeMs)))
            .Concat(document.BananaShowers.Select(s => OsuBeatmapWriter.QuantizeTime(s.TimeMs))).ToHashSet();
        var corrections = heads
            .Where(f => f.Original != f.Corrected && !occupied.Contains(OsuBeatmapWriter.QuantizeTime(f.Corrected)))
            .GroupBy(f => OsuBeatmapWriter.QuantizeTime(f.Corrected))
            .Where(g => g.Select(f => OsuBeatmapWriter.QuantizeTime(f.Original)).Distinct().Count() == 1)
            .SelectMany(g => g).ToDictionary(f => f.Id, f => f.Corrected);
        if (corrections.Count == 0 && ends.Count == 0) return document;
        var prepared = document.DeepClone();
        foreach (var fruit in prepared.Fruits)
            if (corrections.TryGetValue(fruit.Id, out double corrected)) fruit.TimeMs = corrected;
        foreach (var shower in prepared.BananaShowers)
        {
            double start = corrections.GetValueOrDefault(shower.Id, shower.TimeMs);
            double end = ends.GetValueOrDefault(shower.Id, shower.EndTimeMs);
            double duration = OsuBeatmapWriter.QuantizeTime(end) - OsuBeatmapWriter.QuantizeTime(start);
            if (duration < 0 || duration == 0 && shower.EndTimeMs > shower.TimeMs) continue;
            shower.TimeMs = start; shower.EndTimeMs = end;
        }
        return prepared;
    }

    internal static double CorrectSpinnerHead(TimingMap.Lookup timing, double time)
    {
        // A spinner may begin one millisecond before the first red line; later red-line boundaries keep their active grid.
        if (double.IsFinite(time) && time >= 0 && timing.RedTimes.Length > 0
            && time < timing.RedTimes[0] && timing.RedTimes[0] - time <= 1
            && OsuBeatmapWriter.QuantizeTime(timing.RedTimes[0]) - OsuBeatmapWriter.QuantizeTime(time) == 1)
            return OsuBeatmapWriter.QuantizeTime(timing.RedTimes[0]);
        return CorrectHead(timing, time);
    }
}
