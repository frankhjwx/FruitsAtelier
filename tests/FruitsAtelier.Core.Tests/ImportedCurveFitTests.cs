using FruitsAtelier.Core;

internal static class ImportedCurveFitTests
{
    public static void SparseAndBounded()
    {
        // A long smooth movement should remain two editable anchors with handles.
        var points = Enumerable.Range(0, 1001).Select(i =>
        {
            double u = i / 1000d;
            return new MapPoint(1000 + 20000 * u, 100 + 300 * (3 * u * u - 2 * u * u * u));
        }).ToArray();
        var smooth = Fit(points, 1);
        Check(smooth.Nodes.Count == 2 && smooth.Nodes[0].OutgoingKind == CurveKind.Bezier, "Smooth long curve was not represented sparsely.");
        Check(smooth.Nodes[0].TimeMs == 1000 && smooth.Nodes[^1].TimeMs == 21000, "Endpoints moved to a grid.");
        Verify(points, smooth);
        var corners = new MapPoint[] { new(123.45, 100), new(1000, 400), new(2200, 60), new(3000, 60), new(4000, 512), new(6000, 512), new(8000, 200) };
        var angular = Fit(corners, 1);
        Check(angular.Nodes.Count == corners.Length, "Reversals, plateau or boundary transitions were lost."); Verify(corners, angular);
        var random = new Random(12345);
        for (int fixture = 0; fixture < 25; fixture++)
        {
            var wave = Enumerable.Range(0, 501).Select(i => new MapPoint(500 + i * 20,
                256 + 180 * Math.Sin(i / 100d + fixture) + random.NextDouble() * 0.02)).ToArray();
            Verify(wave, Fit(wave, 1));
        }
    }
    public static void BatchPreservesFailuresAndCancellation()
    {
        var document = new MapDocument { DurationMs = 20000, SliderMultiplier = 5, SliderTickRate = 2 };
        var good = new ImportedSlider { TimeMs = 100, X = 100, Y = 100, PathType = 'L', PixelLength = 200, SourceOrder = 1 };
        good.ControlPoints.AddRange([new(100, 100), new(250, 100)]);
        var repeated = new ImportedSlider { TimeMs = 2000, X = 100, Y = 100, PathType = 'L', PixelLength = 300, SpanCount = 3, SourceOrder = 2 };
        repeated.ControlPoints.AddRange([new(100, 100), new(300, 100)]);
        document.ImportedSliders.AddRange([good, repeated]);
        var original = document.DeepClone();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { ImportedSliderEditing.ConvertAll(document, cancellation.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { Check(document.ContentEquals(original), "Cancellation changed the source."); }
        var result = ImportedSliderEditing.ConvertAll(document);
        Check(result.Tracks.Count == 1 && result.Tracks[0].Id == good.Id, "Valid slider was not converted.");
        Check(result.Failures.Count == 1 && result.Failures[0].Id == repeated.Id && ReferenceEquals(document.ImportedSliders.Single(), repeated), "Failed original was not preserved.");
        var before = CatchStreamConverter.Convert(original).Objects.Where(o => o.SourceId == repeated.Id);
        var after = CatchStreamConverter.Convert(document).Objects.Where(o => o.SourceId == repeated.Id);
        Check(before.SequenceEqual(after), "Failed slider's RNG context changed.");
    }
    private static CurveTrack Fit(IReadOnlyList<MapPoint> points, double velocity)
    {
        var track = new CurveTrack(); ImportedCurveFitter.Fit(track, points, velocity);
        var document = new MapDocument { DurationMs = points[^1].TimeMs + 1000 }; document.Tracks.Add(track);
        Check(CurveMath.Validate(document).Count == 0, "Fitted handles or anchors are invalid."); return track;
    }
    private static void Verify(IReadOnlyList<MapPoint> points, CurveTrack track)
    {
        for (int i = 0; i + 1 < points.Count; i++)
            for (int sample = 0; sample <= 8; sample++)
            {
                var original = MapPoint.Lerp(points[i], points[i + 1], sample / 8d);
                Check(Math.Abs(CurveMath.PositionAtTime(track, original.TimeMs) - original.X) <= ImportedSliderEditing.ApproximationTolerance + 1e-8,
                    "Fitted curve exceeded the continuous-interval tolerance.");
            }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
