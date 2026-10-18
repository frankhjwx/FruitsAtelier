using FruitsAtelier.Core;

internal static class SliderControlPointsTests
{
    public static void Run()
    {
        foreach (bool precise in new[] { false, true })
        {
            SliderPathPoint[] points = [new(3, 192), new(precise ? 1.25 : 1, 193), new(2, 194)];
            Action<SliderControlPoints>[] edits = [
                p => p[0] = new(9, 8), p => p.Add(new(4, 5)), p => p.AddRange(p),
                p => p.Insert(1, new(9, 8)), p => p.InsertRange(1, p),
                p => p.Remove(points[0]), p => p.RemoveAt(1), p => p.RemoveRange(0, 2),
                p => p.RemoveAll(q => q.X < 3), p => p.Clear(), p => p.Reverse(),
                p => p.Sort((a, b) => a.X.CompareTo(b.X)), p => p.Capacity = 12,
                p => p[0] = new(.125, -0d)
            ];
            foreach (var edit in edits)
            {
                var source = new SliderControlPoints(); source.AddRange(points);
                var clone = source.Share();
                Check(source.ContentEquals(clone), "Shared points must compare equal.");
                edit(clone);
                Check(source.SequenceEqual(points), "Editing a clone changed its source.");
                var other = source.Share();
                edit(source);
                Check(other.SequenceEqual(points), "Editing the source changed a published clone.");
            }
        }
        var integers = new SliderControlPoints(); integers.AddRange([new(0, 192), new(int.MaxValue, int.MinValue)]);
        var floating = new SliderControlPoints(); floating.AddRange([new(-0d, 192), new(int.MaxValue, int.MinValue)]);
        Check(integers.ContentEquals(floating), "Packed and precise signed zero must retain numeric equality.");
        Check(BitConverter.DoubleToInt64Bits(floating[0].X) == long.MinValue, "Negative zero bits were lost.");
        double nan = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001));
        floating.Add(new(nan, double.PositiveInfinity));
        Check(BitConverter.DoubleToInt64Bits(floating[^1].X) == BitConverter.DoubleToInt64Bits(nan)
            && double.IsPositiveInfinity(floating[^1].GeometryY), "Precise fallback changed nonfinite bits.");
        var nanClone = floating.Share(); nanClone[^1] = new(double.NaN, double.PositiveInfinity);
        Check(floating.ContentEquals(nanClone), "NaN payloads must retain record numeric equality.");
        floating.Add(new(1099511627776, -.125));
        Check(floating[^1] == new SliderPathPoint(1099511627776, -.125), "Out-of-int32 and fractional coordinates must remain exact.");

        var map = new MapDocument();
        var slider = new ImportedSlider { X = 0, Y = 192, TimeMs = 1000, PixelLength = 200, PathType = 'L' };
        slider.ControlPoints.AddRange([new(0, 192), new(200.25, 192)]); map.ImportedSliders.Add(slider);
        string json = ProjectSerializer.Serialize(map);
        var restored = ProjectSerializer.Read(json);
        Check(map.ContentEquals(restored), "Legacy project point arrays must round trip through the packed collection.");
        Check(restored.ImportedSliders.Single().ControlPoints[1].X == 200.25, "Project fractional geometry was truncated.");
        var baseline = map.DeepClone(); restored.ImportedSliders.Single().ControlPoints[0] = new(7, 8);
        Check(map.ContentEquals(baseline), "Restored geometry must not share mutable project data.");

        var large = new ImportedSlider();
        long packedBefore = GC.GetAllocatedBytesForCurrentThread();
        large.ControlPoints.EnsureCapacity(200000);
        Check(GC.GetAllocatedBytesForCurrentThread() - packedBefore < 200000 * 9L,
            "Integer paths must use eight-byte coordinate storage.");
        for (int i = 0; i < 200000; i++) large.ControlPoints.Add(new(i, 192));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var largeClone = large.DeepClone();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 4096 && large.ContentEquals(largeClone), "Cloning large paths must share immutable storage.");
        Task.WaitAll(Task.Run(() => large.ControlPoints[0] = new(1, 2)), Task.Run(() => largeClone.ControlPoints[0] = new(3, 4)));
        Check(large.ControlPoints[0] == new SliderPathPoint(1, 2) && largeClone.ControlPoints[0] == new SliderPathPoint(3, 4),
            "Worker snapshots must detach independently.");
        Console.WriteLine($"Packed path clone: {allocated} bytes for 200000 points");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
