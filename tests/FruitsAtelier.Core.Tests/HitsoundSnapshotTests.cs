using FruitsAtelier.Core;

internal static class HitsoundSnapshotTests
{
    public static void Run()
    {
        var map = new MapDocument();
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 3, Volume = 45 });
        var slider = new ImportedSlider { OriginalLine = "0,192,0,2,8,L|100:192,1,100,2|4,1:2|3:1,2:3:7:70:ignored.wav" };
        slider.ControlPoints.AddRange([new(0, 192), new(100, 192)]); map.ImportedSliders.Add(slider);
        var events = Enumerable.Range(0, 256).Select(i => new ConvertedCatchObject(slider.Id, i,
            i is 0 or 255 ? CatchObjectKind.Fruit : CatchObjectKind.Droplet, i, 0, 0, 0, 0)).ToArray();
        var expected = new HitsoundResolver(map, events);
        slider.OriginalLine = slider.OriginalLine.Replace("L|100:192", "B|" + string.Join('|', Enumerable.Repeat("100:192", 20000)));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var actual = new HitsoundResolver(map, events);
        var samples = events.Select(actual.Describe).ToArray();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024 * 1024, "Nested hitsounds must not repeatedly allocate slider geometry text.");
        for (int i = 0; i < events.Length; i++) Check(samples[i] == expected.Describe(events[i]), "Geometry length changed edge or inherited sample fields.");
        Check(samples[0].NormalSet == 1 && samples[0].AdditionSet == 2 && samples[0].Additions == 2,
            "Head edge sample fields were not preserved.");
        Check(samples[^1].NormalSet == 3 && samples[^1].AdditionSet == 1 && samples[^1].Additions == 4,
            "Tail edge sample fields were not preserved.");
        Check(samples[1].Index == 3 && samples[1].Volume == 45, "Slider index and volume must follow timing.");
        Console.WriteLine($"Large slider hitsound sample allocation: {allocated} bytes for {events.Length} events");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
