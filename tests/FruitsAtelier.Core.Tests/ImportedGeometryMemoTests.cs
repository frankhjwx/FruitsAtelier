using FruitsAtelier.Core;

internal static class ImportedGeometryMemoTests
{
    public static void Run()
    {
        var source = Slider();
        var geometry = source.GetGeometry();
        var clone = source.DeepClone();
        Check(ReferenceEquals(geometry, clone.GetGeometry()), "Snapshots must share unchanged immutable geometry.");
        var path = geometry.AbsolutePoints();
        Check(ReferenceEquals(path, clone.GetGeometry().AbsolutePoints()), "Snapshots must share readonly absolute points.");
        try { ((IList<SliderPathPoint>)path)[0] = new(8, 9); throw new Exception("Absolute geometry must be readonly."); }
        catch (NotSupportedException) { }
        clone.ControlPoints[1] = new(130, 240);
        Check(!ReferenceEquals(geometry, clone.GetGeometry()) && ReferenceEquals(geometry, source.GetGeometry()),
            "Direct point edits must invalidate only the owning snapshot's geometry.");

        Action<ImportedSlider>[] mutations = [s => s.X += 1, s => s.Y += 1, s => s.PathType = 'L', s => s.PixelLength += 1,
            s => s.ControlPoints.Add(new(180, 250)), s => s.ControlPoints.Insert(1, new(90, 140)),
            s => s.ControlPoints.RemoveAt(1), s => s.ControlPoints.Reverse(),
            s => { s.ControlPoints.Clear(); s.ControlPoints.AddRange([new(80, 192), new(180, 192)]); }];
        foreach (var mutate in mutations)
        {
            var slider = Slider(); var before = slider.GetGeometry(); mutate(slider);
            var actual = slider.GetGeometry(); var expected = new ImportedSliderGeometry(slider);
            Check(!ReferenceEquals(before, actual), "A geometry input mutation reused stale geometry.");
            Check(actual.Distance == expected.Distance && actual.AbsolutePoints().SequenceEqual(expected.AbsolutePoints()),
                "Memoized geometry differs from the uncached construction oracle.");
        }
        var metadata = source.DeepClone(); metadata.TimeMs += 100; metadata.SpanCount = 3; metadata.SourceOrder = 8; metadata.OriginalLine = "metadata";
        Check(ReferenceEquals(geometry, metadata.GetGeometry()), "Timing and metadata must not invalidate path geometry.");
        var map = new MapDocument(); map.ImportedSliders.Add(source);
        string json = ProjectSerializer.Serialize(map);
        Check(!json.Contains("geometryMemo", StringComparison.OrdinalIgnoreCase) && map.ContentEquals(ProjectSerializer.Read(json)),
            "Derived geometry must not enter the persisted schema.");

        var fresh = Slider().GetGeometry();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(fresh.AbsolutePoints)).ToArray(); Task.WaitAll(tasks);
        Check(tasks.All(t => ReferenceEquals(tasks[0].Result, t.Result)), "Workers must publish one immutable absolute path.");
        var cached = source.DeepClone();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { _ = cached.GetGeometry(); _ = ImportedSliderConverter.DurationMs(map, cached); _ = ImportedSliderConverter.Path(cached); }
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024 * 1024,
            "Repeated duration and path queries must not rebuild geometry.");
    }
    private static ImportedSlider Slider()
    {
        var slider = new ImportedSlider { X = 80, Y = 192, TimeMs = 1000, PathType = 'B', PixelLength = 200 };
        slider.ControlPoints.AddRange([new(80, 192), new(120, 100), new(180, 200)]); return slider;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
