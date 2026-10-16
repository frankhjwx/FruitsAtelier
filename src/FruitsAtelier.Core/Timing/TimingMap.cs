namespace FruitsAtelier.Core;

public readonly record struct TimingState(double OffsetMs, double BeatLengthMs, double SliderVelocityMultiplier, bool GenerateTicks, int Meter = 4);
public readonly record struct BeatGridLine(double TimeMs, bool IsBeat, bool IsTimingBoundary, int Subdivision = 1, bool IsMeasure = false);

public static class TimingMap
{
    // Stable slider durations use positive source beat lengths, including sub-millisecond timing.
    public static double EffectiveBeatLength(double beatLength) => beatLength > 0 && double.IsFinite(beatLength)
        ? beatLength : Math.Clamp(beatLength, 6, 60000);

    public static TimingState At(MapDocument document, double time) => new Lookup(document).At(time);

    // A lookup owns timing values, so callers can reuse it until the document changes.
    public sealed class Lookup
    {
        private readonly double[] times;
        private readonly TimingState[] states;
        private readonly TimingState initial;
        private readonly double beatLengthMs, offsetMs, sourceMultiplier, effectiveMultiplier;
        private readonly TimingPoint[] inputPoints;
        private readonly double[]? importedHeads;
        internal double[] RedTimes { get; }

        public Lookup(MapDocument document) : this(document, true) { }

        internal Lookup(MapDocument document, bool applyMultiplierOverride)
        {
            beatLengthMs = document.BeatLengthMs; offsetMs = document.TimingOffsetMs;
            sourceMultiplier = document.SliderMultiplier; effectiveMultiplier = document.EffectiveSliderMultiplier;
            double ratio = applyMultiplierOverride ? sourceMultiplier / effectiveMultiplier : 1;
            inputPoints = document.TimingPoints.Select(p => p.DeepClone()).ToArray();
            var groups = Groups(document);
            HashSet<int>? requiredStates = null;
            if (ratio != 1)
            {
                requiredStates = [];
                importedHeads = document.ImportedSliders.Select(s => s.TimeMs).ToArray();
                var boundaries = groups.Select(g => g.TimeMs).ToArray();
                foreach (double head in importedHeads)
                {
                    int index = Array.BinarySearch(boundaries, head);
                    requiredStates.Add(index < 0 ? ~index - 1 : index);
                }
            }
            var firstRed = groups.Select(g => g.Red).FirstOrDefault(p => p is not null);
            double beatLength = EffectiveBeatLength(firstRed?.BeatLengthMs ?? document.BeatLengthMs);
            double offset = firstRed?.TimeMs ?? document.TimingOffsetMs;
            int meter = firstRed?.Meter ?? 4;
            // Only retained Legacy heads require exact compensation; FSliders solve their own legal SV.
            double sv = ratio == 1 ? 1 : 100 / SliderMultiplierEditing.ScaleBeatLength(100, ratio, requiredStates!.Contains(-1));
            bool generateTicks = true;
            initial = new(offset, beatLength, sv, generateTicks, meter);
            times = new double[groups.Length]; states = new TimingState[groups.Length];
            RedTimes = groups.Where(g => g.Red is not null).Select(g => g.TimeMs).ToArray();
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group.Red is TimingPoint red) { beatLength = EffectiveBeatLength(red.BeatLengthMs); offset = red.TimeMs; meter = red.Meter; }
                // At equal time a green point overrides red-point SV, even when its source line precedes the red.
                var difficulty = group.Green ?? group.Red;
                if (difficulty is not null)
                {
                    sv = difficulty.BeatLengthMs < 0 ? Math.Clamp(100 / -difficulty.BeatLengthMs,
                        LegacyCatchRules.MinimumSliderVelocityMultiplier, LegacyCatchRules.MaximumSliderVelocityMultiplier) : 1;
                    if (ratio != 1 && !double.IsNaN(difficulty.BeatLengthMs))
                    {
                        double magnitude = difficulty.BeatLengthMs < 0 ? Math.Clamp((float)-difficulty.BeatLengthMs, 10, 1000) : 100;
                        sv = 100 / SliderMultiplierEditing.ScaleBeatLength(magnitude, ratio, requiredStates!.Contains(i));
                    }
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

        public bool MatchesTiming(MapDocument document)
        {
            if (beatLengthMs != document.BeatLengthMs || offsetMs != document.TimingOffsetMs
                || sourceMultiplier != document.SliderMultiplier || effectiveMultiplier != document.EffectiveSliderMultiplier
                || inputPoints.Length != document.TimingPoints.Count) return false;
            if (importedHeads is not null && !importedHeads.SequenceEqual(document.ImportedSliders.Select(s => s.TimeMs))) return false;
            for (int i = 0; i < inputPoints.Length; i++)
                if (!inputPoints[i].ContentEquals(document.TimingPoints[i])) return false;
            return true;
        }

        public double Snap(double time, int divisor) => TimingMap.Snap(this, time, divisor);

        public double OffsetBeats(double time, double beats)
        {
            if (!double.IsFinite(time) || !double.IsFinite(beats)) throw new ArgumentOutOfRangeException(nameof(time));
            bool earlier = beats < 0;
            double remaining = Math.Abs(beats);
            int boundary = Array.BinarySearch(RedTimes, time);
            if (boundary < 0) boundary = ~boundary - 1;
            else if (earlier) boundary--;
            while (remaining > 0)
            {
                double next = earlier ? boundary >= 0 ? RedTimes[boundary] : double.NegativeInfinity
                    : boundary + 1 < RedTimes.Length ? RedTimes[boundary + 1] : double.PositiveInfinity;
                var state = At(earlier ? Math.BitDecrement(time) : time);
                double length = state.BeatLengthMs;
                if (!double.IsFinite(length) || length <= 0) throw new ArgumentOutOfRangeException(nameof(time));
                double available = Math.Abs(next - time) / length;
                if (remaining <= available) return time + (earlier ? -1 : 1) * remaining * length;
                remaining -= available;
                time = next;
                boundary += earlier ? -1 : 1;
            }
            return time;
        }

        public IEnumerable<BeatGridLine> Grid(double start, double end, int divisor)
        {
            var lines = new List<BeatGridLine>();
            FillGrid(start, end, divisor, lines);
            return lines;
        }

        public void FillGrid(double start, double end, int divisor, List<BeatGridLine> destination)
            => TimingMap.FillGrid(this, start, end, divisor, destination);
    }

    public static double Snap(MapDocument document, double time, int divisor)
        => new Lookup(document).Snap(time, divisor);

    private static double Snap(Lookup lookup, double time, int divisor)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        var state = lookup.At(time);
        double step = state.BeatLengthMs / divisor;
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(lookup));
        var reds = lookup.RedTimes;
        int boundary = Array.BinarySearch(reds, time);
        if (boundary < 0) boundary = ~boundary - 1;
        double previousBoundary = boundary >= 0 ? reds[boundary] : double.NegativeInfinity;
        double nextBoundary = boundary + 1 < reds.Length ? reds[boundary + 1] : double.PositiveInfinity;
        double index = Math.Floor((time - state.OffsetMs) / step);
        double nearest = double.NaN;
        double nearestDistance = double.PositiveInfinity;
        Consider(previousBoundary);
        Consider(nextBoundary);
        double lower = state.OffsetMs + index * state.BeatLengthMs / divisor;
        double upper = state.OffsetMs + (index + 1) * state.BeatLengthMs / divisor;
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

    private static void FillGrid(Lookup lookup, double start, double end, int divisor, List<BeatGridLine> lines)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        ArgumentNullException.ThrowIfNull(lines);
        lines.Clear();
        const int maximumLines = 10000;
        var reds = lookup.RedTimes;
        int boundaryStart = Array.BinarySearch(reds, start);
        if (boundaryStart < 0) boundaryStart = ~boundaryStart;
        int boundaryEnd = boundaryStart;
        while (boundaryEnd < reds.Length && reds[boundaryEnd] <= end && boundaryEnd - boundaryStart < maximumLines)
        {
            double boundary = reds[boundaryEnd++];
            lines.Add(new(boundary, true, true, 1, true));
        }
        int interiorStart = boundaryStart, interiorEnd = boundaryEnd;
        if (interiorStart < interiorEnd && reds[interiorStart] == start) interiorStart++;
        if (interiorEnd > interiorStart && reds[interiorEnd - 1] == end) interiorEnd--;
        int segments = 1 + interiorEnd - interiorStart;
        int perSegmentBudget = Math.Max(1, (maximumLines - lines.Count) / segments);
        double previousGenerated = double.NaN;
        for (int segment = 0; segment < segments && lines.Count < maximumLines; segment++)
        {
            double from = segment == 0 ? start : reds[interiorStart + segment - 1];
            double to = segment + 1 < segments ? reds[interiorStart + segment] : end;
            var state = lookup.At(from);
            double step = state.BeatLengthMs / divisor;
            if (!double.IsFinite(step) || step <= 0) continue;
            double first = Math.Ceiling((from - state.OffsetMs) / step);
            double last = Math.Floor((to - state.OffsetMs) / step);
            if (!double.IsFinite(first) || !double.IsFinite(last)) continue;
            double stride = Math.Max(1, Math.Ceiling((last - first + 1) / perSegmentBudget));
            for (double index = first; index <= last && lines.Count < maximumLines;)
            {
                double time = state.OffsetMs + index * state.BeatLengthMs / divisor;
                // Generated times are monotonic; only red boundaries and rounded adjacent times can collide.
                if (time >= from && time <= end && (segment + 1 == segments || time < to)
                    && time != previousGenerated && Array.BinarySearch(reds, boundaryStart, boundaryEnd - boundaryStart, time) < 0)
                {
                    int remainder = (int)Math.Abs(index % divisor), denominator = divisor;
                    while (remainder != 0) (denominator, remainder) = (remainder, denominator % remainder);
                    lines.Add(new(time, index % divisor == 0, false, divisor / denominator,
                        index % ((double)divisor * state.Meter) == 0));
                    previousGenerated = time;
                }
                double next = index + stride;
                if (!double.IsFinite(next) || next <= index) break;
                index = next;
            }
        }
        lines.Sort(static (a, b) => a.TimeMs.CompareTo(b.TimeMs));
    }

    private static TimingGroup[] Groups(MapDocument document) => document.TimingPoints
        .OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).GroupBy(p => p.TimeMs)
        .Select(g => new TimingGroup(g.Key, g.FirstOrDefault(p => p.Uninherited), g.LastOrDefault(p => !p.Uninherited))).ToArray();

    private sealed record TimingGroup(double TimeMs, TimingPoint? Red, TimingPoint? Green);
}
