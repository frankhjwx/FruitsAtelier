namespace FruitsAtelier.Core;

public readonly record struct TimingState(double OffsetMs, double BeatLengthMs, double SliderVelocityMultiplier, bool GenerateTicks, int Meter = 4);
public readonly record struct BeatGridLine(double TimeMs, bool IsBeat, bool IsTimingBoundary);

public static class TimingMap
{
    public static TimingState At(MapDocument document, double time) => new Lookup(document).At(time);

    // A lookup owns timing values, so callers can reuse it until the document changes.
    public sealed class Lookup
    {
        private readonly double[] times;
        private readonly TimingState[] states;
        private readonly TimingState initial;
        internal double[] RedTimes { get; }

        public Lookup(MapDocument document)
        {
            var groups = Groups(document);
            var firstRed = groups.Select(g => g.Red).FirstOrDefault(p => p is not null);
            double beatLength = firstRed?.BeatLengthMs ?? document.BeatLengthMs;
            double offset = firstRed?.TimeMs ?? document.TimingOffsetMs;
            int meter = firstRed?.Meter ?? 4;
            double sv = 1;
            bool generateTicks = true;
            initial = new(offset, beatLength, sv, generateTicks, meter);
            times = new double[groups.Length]; states = new TimingState[groups.Length];
            RedTimes = groups.Where(g => g.Red is not null).Select(g => g.TimeMs).ToArray();
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group.Red is TimingPoint red) { beatLength = red.BeatLengthMs; offset = red.TimeMs; meter = red.Meter; }
                // At equal time a green point overrides red-point SV, even when its source line precedes the red.
                var difficulty = group.Green ?? group.Red;
                if (difficulty is not null)
                {
                    sv = difficulty.BeatLengthMs < 0 ? Math.Clamp(100 / -difficulty.BeatLengthMs,
                        LegacyCatchRules.MinimumSliderVelocityMultiplier, LegacyCatchRules.MaximumSliderVelocityMultiplier) : 1;
                    generateTicks = !double.IsNaN(difficulty.BeatLengthMs);
                }
                times[i] = group.TimeMs;
                states[i] = new(offset, beatLength, sv, generateTicks, meter);
            }
        }

        public TimingState At(double time)
        {
            if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
            int index = Array.BinarySearch(times, time);
            if (index < 0) index = ~index - 1;
            return index < 0 ? initial : states[index];
        }

        public IEnumerable<BeatGridLine> Grid(double start, double end, int divisor)
            => TimingMap.Grid(this, start, end, divisor);
    }

    public static double Snap(MapDocument document, double time, int divisor)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        var state = At(document, time);
        double step = state.BeatLengthMs / divisor;
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(document));
        var reds = Groups(document).Where(g => g.Red is not null).Select(g => g.TimeMs).ToArray();
        double previousBoundary = reds.Where(t => t <= time).DefaultIfEmpty(double.NegativeInfinity).Last();
        double nextBoundary = reds.FirstOrDefault(t => t > time, double.PositiveInfinity);
        double index = Math.Floor((time - state.OffsetMs) / step);
        double nearest = double.NaN;
        double nearestDistance = double.PositiveInfinity;
        Consider(previousBoundary);
        Consider(nextBoundary);
        double lower = state.OffsetMs + index * step;
        double upper = state.OffsetMs + (index + 1) * step;
        if (lower >= previousBoundary && lower < nextBoundary) Consider(lower);
        if (upper >= previousBoundary && upper < nextBoundary) Consider(upper);
        if (!double.IsFinite(nearest)) throw new ArgumentOutOfRangeException(nameof(time));
        return nearest;

        void Consider(double candidate)
        {
            if (!double.IsFinite(candidate)) return;
            double distance = Math.Abs(candidate - time);
            if (distance < nearestDistance || distance == nearestDistance && candidate > nearest)
            { nearest = candidate; nearestDistance = distance; }
        }
    }

    public static IEnumerable<BeatGridLine> Grid(MapDocument document, double start, double end, int divisor)
        => new Lookup(document).Grid(start, end, divisor);

    private static IEnumerable<BeatGridLine> Grid(Lookup lookup, double start, double end, int divisor)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        const int maximumLines = 10000;
        var reds = lookup.RedTimes;
        var boundaries = reds.Where(t => t >= start && t <= end).Take(maximumLines).ToArray();
        var lines = new SortedDictionary<double, BeatGridLine>();
        foreach (double boundary in boundaries) lines[boundary] = new(boundary, true, true);
        double[] starts = new[] { start }.Concat(boundaries.Where(t => t > start && t < end)).ToArray();
        int perSegmentBudget = Math.Max(1, (maximumLines - lines.Count) / Math.Max(1, starts.Length));
        for (int segment = 0; segment < starts.Length && lines.Count < maximumLines; segment++)
        {
            double from = starts[segment];
            double to = segment + 1 < starts.Length ? starts[segment + 1] : end;
            var state = lookup.At(from);
            double step = state.BeatLengthMs / divisor;
            if (!double.IsFinite(step) || step <= 0) continue;
            double first = Math.Ceiling((from - state.OffsetMs) / step);
            double last = Math.Floor((to - state.OffsetMs) / step);
            if (!double.IsFinite(first) || !double.IsFinite(last)) continue;
            double stride = Math.Max(1, Math.Ceiling((last - first + 1) / perSegmentBudget));
            for (double index = first; index <= last && lines.Count < maximumLines;)
            {
                double time = state.OffsetMs + index * step;
                if (time >= from && time <= end && (segment + 1 == starts.Length || time < to) && !lines.ContainsKey(time))
                    lines[time] = new(time, index % divisor == 0, false);
                double next = index + stride;
                if (!double.IsFinite(next) || next <= index) break;
                index = next;
            }
        }
        return lines.Values;
    }

    private static TimingGroup[] Groups(MapDocument document) => document.TimingPoints
        .OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).GroupBy(p => p.TimeMs)
        .Select(g => new TimingGroup(g.Key, g.FirstOrDefault(p => p.Uninherited), g.LastOrDefault(p => !p.Uninherited))).ToArray();

    private sealed record TimingGroup(double TimeMs, TimingPoint? Red, TimingPoint? Green);
}
