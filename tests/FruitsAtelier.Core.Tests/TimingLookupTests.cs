using FruitsAtelier.Core;

internal static class TimingLookupTests
{
    public static void BoundariesAndSnapshot()
    {
        var map = new MapDocument { BeatLengthMs = 700, TimingOffsetMs = -50 };
        var fallback = new TimingMap.Lookup(map);
        if (fallback.At(-100) != new TimingState(-50, 700, 1, true)) throw new Exception("Default timing changed.");
        map.TimingPoints.AddRange([
            new() { TimeMs = 1000, BeatLengthMs = -25, Uninherited = false, SourceOrder = 0 },
            new() { TimeMs = 100, BeatLengthMs = 500, Uninherited = true, Meter = 3 },
            new() { TimeMs = 1000, BeatLengthMs = 400, Uninherited = true, Meter = 4, SourceOrder = 1 },
            new() { TimeMs = 1000, BeatLengthMs = 600, Uninherited = true, SourceOrder = 2 },
            new() { TimeMs = 2000, BeatLengthMs = double.NaN, Uninherited = false },
            new() { TimeMs = 2500, BeatLengthMs = 300, Uninherited = true, Meter = 7 }
        ]);
        var lookup = new TimingMap.Lookup(map);
        foreach (var (time, state) in new[] {
            (-100d, new TimingState(100, 500, 1, true, 3)),
            (999.999, new TimingState(100, 500, 1, true, 3)),
            (1000d, new TimingState(1000, 400, 4, true, 4)),
            (2000d, new TimingState(1000, 400, 1, false, 4)),
            (2500d, new TimingState(2500, 300, 1, true, 7)) })
            if (lookup.At(time) != state) throw new Exception($"Timing boundary mismatch at {time}.");
        var grid = lookup.Grid(900, 1200, 4).ToArray();
        if (!grid.SequenceEqual(new[] { new BeatGridLine(975, false, false), new BeatGridLine(1000, true, true),
            new BeatGridLine(1100, false, false), new BeatGridLine(1200, false, false) }))
            throw new Exception("Grid no longer resets at the red timing boundary.");
        map.TimingPoints[2].BeatLengthMs = 200;
        map.TimingPoints.Clear();
        if (lookup.At(1000).BeatLengthMs != 400 || new TimingMap.Lookup(map).At(1000) != fallback.At(1000))
            throw new Exception("A timing lookup must own its snapshot.");
    }
}
