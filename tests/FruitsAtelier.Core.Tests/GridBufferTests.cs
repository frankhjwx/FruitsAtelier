using FruitsAtelier.Core;

internal static class GridBufferTests
{
    public static void Equivalence()
    {
        var random = new Random(927);
        var buffer = new List<BeatGridLine>();
        for (int trial = 0; trial < 250; trial++)
        {
            var map = new MapDocument { BeatLengthMs = 333.3333333333333, TimingOffsetMs = -123.25 };
            for (int i = 0; i < trial % 23; i++)
                map.TimingPoints.Add(new TimingPoint { TimeMs = i * 1000.125 - 2100,
                    BeatLengthMs = i % 3 == 0 ? -50 : 50 + random.NextDouble() * 600,
                    Uninherited = i % 3 != 0, Meter = 1 + i % 7 });
            var lookup = new TimingMap.Lookup(map);
            foreach (var (start, end, divisor) in new (double, double, int)[] {
                (-3100, 25000, 12), (0, 1e7, 16), (-2100, -2100, 4),
                (-2100, -1099.875, 7), (random.Next(-4000, 1000), random.Next(1000, 30000), 48) })
            {
                var expected = ReferenceGrid(map, start, end, divisor).ToArray();
                lookup.FillGrid(start, end, divisor, buffer);
                if (!expected.SequenceEqual(buffer)) throw new Exception($"Grid differs in trial {trial}, [{start}, {end}], divisor {divisor}.");
                if (!expected.SequenceEqual(lookup.Grid(start, end, divisor))) throw new Exception("Owned grid differs.");
            }
        }
        var dense = new MapDocument();
        for (int i = 0; i < 11000; i++) dense.TimingPoints.Add(new TimingPoint { TimeMs = i, BeatLengthMs = 500, Uninherited = true });
        new TimingMap.Lookup(dense).FillGrid(0, 12000, 4, buffer);
        if (!ReferenceGrid(dense, 0, 12000, 4).SequenceEqual(buffer)) throw new Exception("Boundary budget changed.");
        var empty = new TimingMap.Lookup(new MapDocument());
        var owned = empty.Grid(0, 10000, 16);
        var expectedOwned = owned.ToArray();
        empty.FillGrid(0, 10000, 16, buffer);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) empty.FillGrid(i, 10000 + i, 16, buffer);
        if (GC.GetAllocatedBytesForCurrentThread() - bytes > 1024) throw new Exception("Warm grid fill allocates per frame.");
        if (!owned.SequenceEqual(expectedOwned)) throw new Exception("Reusable buffer changed owned grid output.");
    }

    private static IEnumerable<BeatGridLine> ReferenceGrid(MapDocument document, double start, double end, int divisor)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        const int maximumLines = 10000;
        var lookup = new TimingMap.Lookup(document);
        var reds = document.TimingPoints.Where(p => p.Uninherited).Select(p => p.TimeMs).Distinct().Order().ToArray();
        var boundaries = reds.Where(t => t >= start && t <= end).Take(maximumLines).ToArray();
        var lines = new SortedDictionary<double, BeatGridLine>();
        foreach (double boundary in boundaries) lines[boundary] = new(boundary, true, true, 1, true);
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
                {
                    int remainder = (int)Math.Abs(index % divisor), denominator = divisor;
                    while (remainder != 0) (denominator, remainder) = (remainder, denominator % remainder);
                    lines[time] = new(time, index % divisor == 0, false, divisor / denominator,
                        index % ((double)divisor * state.Meter) == 0);
                }
                double next = index + stride;
                if (!double.IsFinite(next) || next <= index) break;
                index = next;
            }
        }
        return lines.Values;
    }


}
