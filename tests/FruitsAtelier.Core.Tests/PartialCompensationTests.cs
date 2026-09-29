using FruitsAtelier.Core;

internal static class PartialCompensationTests
{
    public static void FractionalRepeats()
    {
        var document = new MapDocument { BeatLengthMs = 4000.0 / 13, SliderMultiplier = 2.1, SliderTickRate = 2 };
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2, CompensateTinyDroplets = true };
        track.Nodes.Add(new() { TimeMs = 7145.923076923084, X = 336 });
        track.Nodes.Add(new() { TimeMs = 7761.307692307701, X = 267 });
        document.Tracks.Add(track);
        var result = CatchStreamConverter.Convert(document);
        Check(result.Success, string.Join("; ", result.Diagnostics));
        Check(result.Sliders.Single().TinyCompensationApplied && !result.Sliders.Single().TinyCompensationSucceeded,
            "Repeated tiny targets did not exercise partial compensation.");
        Check(result.MaxTickError <= CatchStreamConverter.AlignmentTolerance, "Repeated fixed targets moved.");
        Check(result.Sliders.Single().SliderVelocityMultiplier <= 10, "Repeated compensation exceeded the SV limit.");
        Check(OsuBeatmapWriter.Serialize(document).ObjectSequenceMatches, "Fractional repeats did not survive export.");
    }

    public static void Run()
    {
        var doc = new MapDocument { BeatLengthMs = 3000, SliderMultiplier = 3.2, SliderTickRate = 1 };
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2, CompensateTinyDroplets = true };
        track.Nodes.Add(new() { TimeMs = 21309, X = 176 });
        track.Nodes.Add(new() { TimeMs = 21661.94117647038, X = 303.0115661621094 });
        doc.Tracks.Add(track);
        doc.BananaShowers.Add(new() { TimeMs = 22500, EndTimeMs = 22700 });
        var history = new EditorHistory(doc);
        doc = history.Document;
        track = doc.Tracks.Single();
        var compensated = CatchStreamConverter.Convert(doc);
        Check(compensated.Success, string.Join("; ", compensated.Diagnostics));
        history.Begin("Disable tiny compensation");
        track.CompensateTinyDroplets = false;
        history.Commit();
        var plain = CatchStreamConverter.Convert(doc);
        Check(plain.Success, "Uncompensated fixture failed.");
        Check(compensated.Sliders.Single().TinyCompensationApplied, "Compensation was discarded.");
        Check(!compensated.Sliders.Single().TinyCompensationSucceeded, "Unreachable targets were marked exact.");
        Check(compensated.MaxTinyError < plain.MaxTinyError, "Partial compensation did not improve the worst error.");
        var nested = LegacyCatchRules.CreateNested(track.Nodes[0].TimeMs, track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs,
            compensated.Sliders.Single().Velocity, compensated.Sliders.Single().TickDistance,
            compensated.Sliders.Single().Length, track.SpanCount);
        var rng = new CatchLegacyRandom(1337);
        LegacyCatchRules.ApplyRandomSequence(nested, ref rng);
        double optimum = 0;
        foreach (var a in nested)
        foreach (var b in nested)
        {
            int adjustable = (a.Kind == CatchObjectKind.TinyDroplet ? 1 : 0) + (b.Kind == CatchObjectKind.TinyDroplet ? 1 : 0);
            if (adjustable == 0) continue;
            double wantedA = CurveMath.PositionAtTime(track, a.TimeMs) - a.RawOffset;
            double wantedB = CurveMath.PositionAtTime(track, b.TimeMs) - b.RawOffset;
            double travel = Math.Abs(a.Progress - b.Progress) * compensated.Sliders.Single().Length;
            optimum = Math.Max(optimum, (Math.Abs(wantedA - wantedB) - travel) / adjustable);
        }
        Check(Math.Abs(compensated.MaxTinyError - optimum) <= CatchStreamConverter.AlignmentTolerance,
            "Partial compensation did not attain the independent pairwise error bound.");
        Check(compensated.MaxTickError <= CatchStreamConverter.AlignmentTolerance, "Fruit or ticks moved.");
        Check(compensated.Sliders.Single().SliderVelocityMultiplier <= 10, "SV exceeded stable's limit.");
        Check(compensated.Objects.Count == plain.Objects.Count, "Partial compensation changed event count.");
        foreach (var (a, b) in compensated.Objects.Zip(plain.Objects))
        {
            Check(a.TimeMs == b.TimeMs && a.Kind == b.Kind && a.EventIndex == b.EventIndex
                && a.RandomOffset == b.RandomOffset, "Partial compensation changed events or RNG.");
            if (a.Kind != CatchObjectKind.TinyDroplet) Check(Math.Abs(a.X - b.X) <= CatchStreamConverter.AlignmentTolerance, "A fixed event moved.");
        }
        history.Undo();
        var cache = new CatchConversionCache();
        Check(CatchStreamConverter.Convert(history.Document, cache: cache).Objects.SequenceEqual(compensated.Objects), "Undo lost compensation.");
        Check(CatchStreamConverter.Convert(history.Document, cache: cache).Objects.SequenceEqual(compensated.Objects), "Cached compensation changed output.");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
        Check(CatchStreamConverter.Convert(restored).Objects.SequenceEqual(compensated.Objects), "Persistence lost compensation.");
        var export = OsuBeatmapWriter.Serialize(restored);
        Check(export.ObjectSequenceMatches, "Partial compensation changed exported event sequence.");
        var readback = CatchStreamConverter.Convert(export.ReadBack).Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).ToArray();
        var targets = compensated.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).ToArray();
        Check(readback.Zip(targets).Max(p => Math.Abs(p.First.X - p.Second.TargetX)) < plain.MaxTinyError,
            "Export quantization erased the compensation improvement.");
        history.Redo();
        Check(CatchStreamConverter.Convert(history.Document, cache: cache).Objects.SequenceEqual(plain.Objects), "Redo ignored explicit compensation disable.");
        var longMap = new MapDocument { DurationMs = 100000, BeatLengthMs = 1000, SliderMultiplier = 3.2 };
        var longTrack = new CurveTrack { SpanCount = 2, CompensateTinyDroplets = true };
        longTrack.Nodes.Add(new() { TimeMs = 0, X = 256 });
        longTrack.Nodes.Add(new() { TimeMs = 40000, X = 256 });
        longMap.Tracks.Add(longTrack);
        var longResult = CatchStreamConverter.Convert(longMap);
        Check(longResult.Success && longResult.Sliders.Single().TinyCompensationApplied
            && longResult.Sliders.Single().Length <= LegacyCatchRules.MaximumPathLength,
            "Partial compensation exceeded the path length limit on a long repeat.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
