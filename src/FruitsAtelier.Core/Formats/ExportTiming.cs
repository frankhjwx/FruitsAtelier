namespace FruitsAtelier.Core;

internal static class ExportTiming
{
    private static readonly int[] divisors = [16, 12, 9, 7, 5, 24, 32, 48];
    // Compare integer timestamps using the same truncation as exported beat grids.
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
