using FruitsAtelier.Core;

internal static class ConversionTests
{
    public static void LegacyEventsAndRandom()
    {
        var document = With(Constant(0, 1000, 256));
        var result = CatchStreamConverter.Convert(document, false);
        Valid(result);
        True(result.Objects.Count == 17, "Expected head, tick, tail and fourteen tiny droplets.");
        True(result.Objects.Count(o => o.Kind == CatchObjectKind.Fruit) == 2, "LegacyLastTick created an extra fruit.");
        var tick = result.Objects.Single(o => o.Kind == CatchObjectKind.Droplet);
        Near(500, tick.TimeMs);
        var tiny = result.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).ToArray();
        double[] times = [62.5, 125, 187.5, 250, 312.5, 375, 437.5, 558, 616, 674, 732, 790, 848, 906];
        int[] offsets = [-14, -10, -2, 15, 17, 1, 0, -7, -11, -3, 14, 4, -8, 5];
        for (int i = 0; i < tiny.Length; i++)
        {
            Near(times[i], tiny[i].TimeMs);
            Near(offsets[i], tiny[i].RandomOffset);
            Near(256 + offsets[i], tiny[i].X);
        }
        Near(1000, result.Objects[^1].TimeMs);
    }

    public static void IndependentTickRate()
    {
        var track = Constant(123.4, 1000, 256);
        var document = With(track);
        document.TimingOffsetMs = -321;
        document.SliderTickRate = 4;
        var first = CatchStreamConverter.Convert(document, false);
        Valid(first);
        var ticks = first.Objects.Where(o => o.Kind == CatchObjectKind.Droplet).ToArray();
        True(ticks.Length == 7, "TickRate 4 must produce seven interior ticks over two beats.");
        for (int i = 0; i < ticks.Length; i++) Near(123.4 + (i + 1) * 125, ticks[i].TimeMs);

        document.Fruits.Add(new Fruit { TimeMs = BeatGrid.Snap(220, 0, 500, 6), X = 200 });
        var withSixthFruit = CatchStreamConverter.Convert(document, false);
        Valid(withSixthFruit);
        var unchanged = withSixthFruit.Objects.Where(o => o.SourceId == track.Id).ToArray();
        True(unchanged.SequenceEqual(first.Objects), "An independent snapped fruit changed stream ticks or NM RNG.");
        document.SliderTickRate = 6;
        var sixthTicks = CatchStreamConverter.Convert(document, false);
        Valid(sixthTicks);
        True(sixthTicks.Objects.Count(o => o.Kind == CatchObjectKind.Droplet) == 11, "TickRate 6 did not change actual slider event density.");
        Near(6, document.SliderTickRate);
    }

    public static void BezierTickAlignment()
    {
        var track = new CurveTrack { Name = "Nonlinear time curve" };
        track.Nodes.Add(new Anchor { TimeMs = 0, X = 100, HandleOut = new(300, 120) });
        track.Nodes.Add(new Anchor { TimeMs = 2000, X = 400, HandleIn = new(-500, -100) });
        var document = With(track);
        var before = document.DeepClone();
        var result = CatchStreamConverter.Convert(document);
        Valid(result);
        var slider = result.Sliders.Single();
        True(slider.SliderVelocityMultiplier is >= 0.1 and <= 10, "Generated SV exceeded legacy bounds.");
        True(slider.Path.Count > 2, "Bezier was not converted to a path.");
        True(slider.Path.All(p => p.X is >= 0 and <= 512 && p.GeometryY is >= 0 and <= 384), "Generated path left its geometric bounds.");
        double measuredLength = PathLength(slider.Path);
        Near(slider.Length, measuredLength, 0.00001);
        Near(slider.DurationMs, measuredLength / slider.Velocity, 0.00001);
        foreach (var item in result.Objects.Where(o => o.Kind != CatchObjectKind.TinyDroplet))
        {
            Near(CurveMath.PositionAtTime(track, item.TimeMs), item.X, CatchStreamConverter.AlignmentTolerance);
            Near(PathX(slider.Path, (item.TimeMs - slider.StartTimeMs) * slider.Velocity), item.PathX, CatchStreamConverter.AlignmentTolerance);
        }
        True(result.MaxTickError <= CatchStreamConverter.AlignmentTolerance, "Tick target alignment failed.");
        True(track.Nodes.Count == 2 && track.Nodes[0].HandleOut == before.Tracks[0].Nodes[0].HandleOut
            && track.Nodes[1].HandleIn == before.Tracks[0].Nodes[1].HandleIn && document.Fruits.Count == 0,
            "Conversion replaced authoring handles or baked a stream into standalone fruit.");
    }

    public static void HorizontalEndHandle()
    {
        // Near-horizontal endpoint tangent: instantaneous speed is unbounded,
        // but the actual catch events remain reachable at a legal slider velocity.
        var track = new CurveTrack { Name = "Horizontal endpoint" };
        track.Nodes.Add(new Anchor { TimeMs = 0, X = 180, HandleOut = new(80, 100) });
        track.Nodes.Add(new Anchor { TimeMs = 480, X = 180, HandleIn = new(0, 64) });
        foreach (int spans in new[] { 1, 2 })
        foreach (bool compensate in new[] { false, true })
        {
            track.SpanCount = spans;
            var document = With(track);
            var before = document.DeepClone();
            var result = CatchStreamConverter.Convert(document, compensate);
            Valid(result);
            var slider = result.Sliders.Single();
            Near(480 * spans, slider.DurationMs);
            foreach (var item in result.Objects.Where(o => o.Kind != CatchObjectKind.TinyDroplet || slider.TinyCompensationSucceeded))
                Near(CurveMath.PositionAtTime(track, item.TimeMs), item.X, CatchStreamConverter.AlignmentTolerance);
            True(document.ContentEquals(before), "Generation changed authoring handles.");
            var exported = OsuBeatmapWriter.Serialize(document, compensate);
            True(exported.ObjectSequenceMatches, "Export changed the gameplay event sequence.");
        }
    }

    public static void TinyCompensation()
    {
        var document = With(Constant(0, 1000, 256));
        var result = CatchStreamConverter.Convert(document, true);
        Valid(result);
        var slider = result.Sliders.Single();
        True(slider.TinyCompensationApplied && slider.TinyCompensationSucceeded, "Reachable tiny targets were not compensated.");
        True(result.MaxTinyError <= CatchStreamConverter.AlignmentTolerance, "Compensated tiny missed target.");
        var first = result.Objects.First(o => o.Kind == CatchObjectKind.TinyDroplet);
        Near(270, first.PathX, CatchStreamConverter.AlignmentTolerance);
        Near(-14, first.RandomOffset);
        Near(256, first.X, CatchStreamConverter.AlignmentTolerance);
        True(slider.Path.Any(p => Math.Abs(p.X - 256) > 1), "Compensation changed displayed objects without changing the derived slider.");
    }

    public static void StableSliderVelocityLimit()
    {
        var imported = With();
        imported.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = -1, Uninherited = false });
        Near(10, TimingMap.At(imported, 0).SliderVelocityMultiplier);

        var track = new CurveTrack { Kind = CurveKind.Linear, Name = "High SV", CompensateTinyDroplets = true };
        track.Nodes.Add(new Anchor { TimeMs = 0, X = 0 });
        track.Nodes.Add(new Anchor { TimeMs = 20, X = 512 });
        var result = CatchStreamConverter.Convert(With(track));
        True(!result.Success && result.Sliders.Count == 0,
            "A FSlider above stable's SV=10 limit was generated.");
    }

    public static void TinyBoundary()
    {
        var result = CatchStreamConverter.Convert(With(Constant(0, 1000, 0)), true);
        Valid(result);
        True(result.Sliders[0].TinyCompensationApplied && !result.Sliders[0].TinyCompensationSucceeded, "Unreachable X boundary was marked fully compensated.");
        True(result.Diagnostics.Count == 0, "A usable boundary-limited result exposed an internal compensation warning.");
        True(result.MaxTinyError > 0, "Positive offset at X=0 was silently removed.");
        True(result.Objects.All(o => o.X is >= 0 and <= 512 && o.PathX is >= 0 and <= 512), "Compensation emitted out-of-range coordinates.");
        True(result.MaxTickError <= CatchStreamConverter.AlignmentTolerance, "Tiny failure displaced fruit or ticks.");
    }

    public static void NumericalSamplingLimit()
    {
        var track = new CurveTrack { Kind = CurveKind.Bezier, Name = "Sharp curve", CompensateTinyDroplets = false };
        track.Nodes.Add(new Anchor { TimeMs = 0, X = 0, HandleOut = new(500, 512) });
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 512, HandleIn = new(-500, -512) });
        var result = CatchStreamConverter.Convert(With(track), false);
        Valid(result);
        True(result.Diagnostics.Count == 0, "Numerical sampling details leaked into the user-visible diagnostics.");
        True(result.MaxTickError <= CatchStreamConverter.AlignmentTolerance,
            "Accepting the smallest sampling intervals displaced an exact gameplay knot.");
    }

    public static void CompleteContextRandom()
    {
        var first = Constant(0, 1000, 256);
        var second = Constant(100, 1000, 256);
        var document = With(first, second);
        var combined = CatchStreamConverter.Convert(document, false);
        Valid(combined);
        var secondFirstTiny = combined.Objects.First(o => o.SourceId == second.Id && o.Kind == CatchObjectKind.TinyDroplet);
        Near(162.5, secondFirstTiny.TimeMs);
        Near(-18, secondFirstTiny.RandomOffset);
        var onlySecond = CatchStreamConverter.Convert(With(second), false);
        Valid(onlySecond);
        Near(-14, onlySecond.Objects.First(o => o.Kind == CatchObjectKind.TinyDroplet).RandomOffset);
        True(!combined.Objects.Select(o => o.TimeMs).Where((time, index) => index > 0 && time < combined.Objects[index - 1].TimeMs).Any(),
            "Flattened gameplay objects were not sorted after parent RNG traversal.");

        document.Fruits.Add(new Fruit { TimeMs = 50, X = 300 });
        var withFruit = CatchStreamConverter.Convert(document, false);
        Valid(withFruit);
        Near(-18, withFruit.Objects.First(o => o.SourceId == second.Id && o.Kind == CatchObjectKind.TinyDroplet).RandomOffset);
    }

    public static void TailRules()
    {
        var shortResult = CatchStreamConverter.Convert(With(Constant(0, 200, 256)), false);
        Valid(shortResult);
        Near(82, shortResult.Objects.Single(o => o.Kind == CatchObjectKind.TinyDroplet).TimeMs);
        True(shortResult.Objects.Count(o => o.Kind == CatchObjectKind.Fruit) == 2, "Legacy last tick should not be an extra fruit.");

        var exactBoundary = With(Constant(0, 510, 256));
        exactBoundary.SliderMultiplier = 5;
        var boundary = CatchStreamConverter.Convert(exactBoundary, false);
        Valid(boundary);
        True(boundary.Objects.All(o => o.Kind != CatchObjectKind.Droplet), "Tick exactly ten ms from the tail was not excluded.");
        var beyondBoundary = With(Constant(0, 511, 256));
        beyondBoundary.SliderMultiplier = 5;
        var beyond = CatchStreamConverter.Convert(beyondBoundary, false);
        Valid(beyond);
        Near(500, beyond.Objects.Single(o => o.Kind == CatchObjectKind.Droplet).TimeMs);
    }

    public static void PartialFailure()
    {
        var impossible = new CurveTrack { Kind = CurveKind.Linear, Name = "Too fast" };
        impossible.Nodes.Add(new Anchor { TimeMs = 0, X = 0 });
        impossible.Nodes.Add(new Anchor { TimeMs = 1, X = 512 });
        var valid = Constant(2000, 1000, 256);
        var document = With(impossible, valid);
        var fruit = new Fruit { TimeMs = 500, X = 120 };
        document.Fruits.Add(fruit);
        var result = CatchStreamConverter.Convert(document);
        True(!result.Success && result.Diagnostics.Count > 0, "Impossible velocity was reported as successful.");
        True(result.Sliders.Count == 1 && result.Sliders[0].SourceId == valid.Id, "One failed curve discarded valid curves.");
        True(result.Objects.Any(o => o.SourceId == fruit.Id && o.IsStandalone), "One failed curve discarded standalone fruit.");
        True(result.Objects.All(o => o.SourceId != impossible.Id), "Failure emitted fake points for the impossible curve.");
    }

    public static void FractionalTinyTiming()
    {
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.Add(new Anchor { TimeMs = 10.75, X = 150 });
        track.Nodes.Add(new Anchor { TimeMs = 1011.15, X = 350 });
        var result = CatchStreamConverter.Convert(With(track), true);
        Valid(result);
        True(result.Sliders[0].TinyCompensationSucceeded, "Fractional event times broke tiny compensation.");
        foreach (var item in result.Objects)
            Near(CurveMath.PositionAtTime(track, item.TimeMs), item.X, CatchStreamConverter.AlignmentTolerance);
    }

    private static CurveTrack Constant(double start, double duration, double x)
    {
        var track = new CurveTrack { Kind = CurveKind.Linear, Name = $"Constant {start}" };
        track.Nodes.Add(new Anchor { TimeMs = start, X = x });
        track.Nodes.Add(new Anchor { TimeMs = start + duration, X = x });
        return track;
    }

    private static MapDocument With(params CurveTrack[] tracks)
    {
        var document = new MapDocument { BeatLengthMs = 500, SliderMultiplier = 1.4, SliderTickRate = 1 };
        document.Tracks.AddRange(tracks);
        return document;
    }

    private static double PathLength(IReadOnlyList<SliderPathPoint> points)
    {
        double sum = 0;
        for (int i = 1; i < points.Count; i++) sum += Distance(points[i - 1], points[i]);
        return sum;
    }

    private static double PathX(IReadOnlyList<SliderPathPoint> points, double distance)
    {
        for (int i = 1; i < points.Count; i++)
        {
            double segment = Distance(points[i - 1], points[i]);
            if (distance <= segment) return points[i - 1].X + (points[i].X - points[i - 1].X) * distance / segment;
            distance -= segment;
        }
        return points[^1].X;
    }

    private static double Distance(SliderPathPoint a, SliderPathPoint b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.GeometryY - a.GeometryY) * (b.GeometryY - a.GeometryY));

    private static void Valid(CatchConversionResult result) => True(result.Success, string.Join("; ", result.Diagnostics));
    private static void Near(double expected, double actual, double tolerance = 1e-7)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new Exception($"Expected {expected:R}, got {actual:R}.");
    }
    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
}
