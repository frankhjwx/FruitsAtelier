using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record SliderRepresentationResult(bool Success, IReadOnlyList<string> Diagnostics)
{
    public OsuWriteResult? ValidatedOutput { get; init; }
}

public static class SliderRepresentationEditing
{
    public static bool NeedsLegacyDropletRefresh(MapDocument before, MapDocument after)
    {
        if (after.ImportedSliders.All(s => s.PreserveNativeGeometry)) return false;
        bool settingsChanged = before.DerandomizeFSliderDroplets != after.DerandomizeFSliderDroplets
            || before.DerandomizeDropletsForHardRock != after.DerandomizeDropletsForHardRock
            || before.RandomizeDropletStrength != after.RandomizeDropletStrength
            || before.RandomizeDropletSeed != after.RandomizeDropletSeed;
        bool managed = after.DerandomizeFSliderDroplets || after.DerandomizeDropletsForHardRock
            || after.RandomizeDropletStrength != 20 || after.RandomizeDropletSeed != 1337
            || after.ImportedSliders.Any(s => s.RandomizationSource is not null);
        if (!managed) return false;
        if (settingsChanged || before.SliderTickRate != after.SliderTickRate
            || before.LegacySliderTicks != after.LegacySliderTicks || before.BeatLengthMs != after.BeatLengthMs
            || before.TimingOffsetMs != after.TimingOffsetMs || before.CircleSize != after.CircleSize
            || before.EffectiveSliderMultiplier != after.EffectiveSliderMultiplier
            || before.Fruits.Count != after.Fruits.Count || before.Tracks.Count != after.Tracks.Count
            || before.ImportedSliders.Count != after.ImportedSliders.Count || before.BananaShowers.Count != after.BananaShowers.Count
            || before.TimingPoints.Count != after.TimingPoints.Count) return true;
        if (!before.TimingPoints.Zip(after.TimingPoints).All(pair => pair.First.ContentEquals(pair.Second))) return true;
        var unchanged = before.UnchangedObjectIds(after).ToHashSet();
        return after.Fruits.Any(f => !unchanged.Contains(f.Id)) || after.Tracks.Any(t => !unchanged.Contains(t.Id))
            || after.ImportedSliders.Any(s => !unchanged.Contains(s.Id)) || after.BananaShowers.Any(s => !unchanged.Contains(s.Id));
    }

    public static SliderRepresentationResult RefreshLegacyDroplets(MapDocument document, bool compensateTinyDroplets = true,
        MapDocument? previous = null, OsuWriteCache? cache = null)
    {
        try
        {
            if (document.ImportedSliders.All(s => s.PreserveNativeGeometry)) return new(true, []);
            if (!document.DerandomizeFSliderDroplets && !document.DerandomizeDropletsForHardRock
                && document.RandomizeDropletStrength == 20 && document.RandomizeDropletSeed == 1337
                && document.ImportedSliders.All(s => s.RandomizationSource is null)) return new(true, []);
            var candidate = document.DeepClone();
            var sources = candidate.ImportedSliders.Where(s => !s.PreserveNativeGeometry).ToArray();
            var timingLookup = new TimingMap.Lookup(document);
            var previousLookup = previous is null ? null : new TimingMap.Lookup(previous);
            var previousSliders = previous?.ImportedSliders.ToDictionary(s => s.Id);
            var timeScales = new Dictionary<Guid, double>();
            var initialVelocities = sources.ToDictionary(s => s.Id,
                s => s.RandomizationTrack is not null && s.RandomizationBaseVelocity is { } savedVelocity
                    ? savedVelocity : timingLookup.At(s.TimeMs).SliderVelocityMultiplier);
            foreach (var slider in sources.Where(s => s.RandomizationTrack is not null))
            {
                if (previousLookup is null || previousSliders is null || !previousSliders.TryGetValue(slider.Id, out var oldSlider)) continue;
                var oldTiming = previousLookup.At(oldSlider.TimeMs);
                var nextTiming = timingLookup.At(slider.TimeMs);
                double velocityRatio = nextTiming.SliderVelocityMultiplier / oldTiming.SliderVelocityMultiplier;
                double oldVelocity = LegacyCatchRules.Velocity(oldTiming.BeatLengthMs,
                    previous!.EffectiveSliderMultiplier, oldTiming.SliderVelocityMultiplier);
                double nextVelocity = LegacyCatchRules.Velocity(nextTiming.BeatLengthMs,
                    document.EffectiveSliderMultiplier, nextTiming.SliderVelocityMultiplier);
                double scale = oldVelocity / nextVelocity;
                timeScales[slider.Id] = scale;
                initialVelocities[slider.Id] = Math.Clamp(initialVelocities[slider.Id] * velocityRatio, .1, 10);
            }
            var inheritedHeads = candidate.TimingPoints.Where(p => !p.Uninherited)
                .GroupBy(p => p.TimeMs).ToDictionary(g => g.Key, g => g.Last());
            var originalTiming = candidate.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
            foreach (var slider in sources.Where(s => s.RandomizationTrack is not null))
            {
                // Fitting must restart from its authored SV, rather than a previous effect's generated SV.
                if (!inheritedHeads.TryGetValue(slider.TimeMs, out var point))
                {
                    int low = 0, high = originalTiming.Length;
                    while (low < high)
                    {
                        int middle = (low + high) / 2;
                        if (originalTiming[middle].TimeMs <= slider.TimeMs) low = middle + 1; else high = middle;
                    }
                    point = low > 0 ? originalTiming[low - 1].DeepClone() : new();
                    point.TimeMs = slider.TimeMs; point.Uninherited = false; point.OriginalLine = null;
                    candidate.TimingPoints.Add(point);
                    inheritedHeads[slider.TimeMs] = point;
                }
                point.BeatLengthMs = -100 / initialVelocities[slider.Id];
            }
            var baselineTracks = new Dictionary<Guid, CurveTrack>();
            var restoredIds = sources.Where(s => s.RandomizationTrack is not null).Select(s => s.Id).ToHashSet();
            candidate.ImportedSliders.RemoveAll(s => restoredIds.Contains(s.Id));
            foreach (var slider in sources)
            {
                if (slider.RandomizationTrack is not { } saved) continue;
                var baseline = saved.DeepClone();
                TimingEditing.TransformTrack(baseline, baseline.Nodes[0].TimeMs, slider.TimeMs, timeScales.GetValueOrDefault(slider.Id, 1));
                baseline.OriginalLine = slider.OriginalLine;
                baseline.SourceOrder = slider.SourceOrder;
                candidate.Tracks.Add(baseline);
            }
            var fitted = ImportedSliderEditing.Convert(candidate,
                sources.Where(s => s.RandomizationTrack is null).Select(s => s.Id).ToArray());
            if (fitted.Failures.Count > 0)
                return new(false, fitted.Failures.Select(f => f.Reason).ToArray());
            var tracks = candidate.Tracks.ToDictionary(t => t.Id);
            foreach (var slider in sources)
            {
                var baseline = tracks[slider.Id];
                baselineTracks[slider.Id] = baseline.DeepClone();
                baseline.DropletMode = SliderDropletMode.FollowMap;
                baseline.DropletRandomization ??= new();
                baseline.DropletRandomization.Enabled = true;
            }
            var expected = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets, cache);
            var materialized = expected.ReadBack.ImportedSliders.ToDictionary(s => s.Id);
            var sourceIds = sources.Select(s => s.Id).ToHashSet();
            candidate.Tracks.RemoveAll(t => sourceIds.Contains(t.Id));
            foreach (var source in sources)
            {
                var slider = materialized[source.Id].DeepClone();
                slider.SourceOrder = source.SourceOrder;
                slider.RandomizationSource = (source.RandomizationSource ?? source).DeepClone();
                slider.RandomizationTrack = baselineTracks[source.Id];
                slider.RandomizationBaseVelocity = initialVelocities[source.Id];
                candidate.ImportedSliders.Add(slider);
            }
            CopyEmittedTiming(candidate, expected);
            var actual = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets, cache);
            CheckEquivalent(expected.PlayableObjects, actual.PlayableObjects);
            CheckEquivalent(expected.PlayableHardRockObjects, actual.PlayableHardRockObjects);
            if (!expected.ObjectSequenceMatches || !actual.ObjectSequenceMatches)
                throw new InvalidOperationException(L.Get("core.importEditing.preservePositionsFailed"));
            document.ImportedSliders.Clear(); document.ImportedSliders.AddRange(candidate.ImportedSliders);
            document.TimingPoints.Clear(); document.TimingPoints.AddRange(candidate.TimingPoints);
            return new(true, []) { ValidatedOutput = actual };
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or CatchConversionException)
        { return new(false, [error.Message]); }
    }

    public static SliderRepresentationResult ConvertToTrack(MapDocument document, Guid id, bool derandomize = true)
    {
        try
        {
            var candidate = document.DeepClone();
            var result = ImportedSliderEditing.ConvertToTrack(candidate, id, derandomizeDroplets: derandomize);
            _ = OsuBeatmapWriter.Serialize(candidate);
            document.ImportedSliders.RemoveAll(s => s.Id == id);
            document.Tracks.Add(result.Track);
            return new(true, result.Diagnostics);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or CatchConversionException)
        { return new(false, [error.Message]); }
    }

    public static SliderRepresentationResult ConvertToLegacy(MapDocument document, Guid id, bool compensateTinyDroplets = true)
    {
        try
        {
            var track = document.Tracks.FirstOrDefault(t => t.Id == id);
            if (track is null || track.StreamSnapDivisor is not null)
                throw new ArgumentException(L.Get("core.importEditing.notFound"), nameof(id));
            var before = OsuBeatmapWriter.Serialize(document, compensateTinyDroplets);
            var candidate = document.DeepClone();
            var slider = before.ReadBack.ImportedSliders.Single(s => s.Id == id).DeepClone();
            slider.SourceOrder = track.SourceOrder;
            candidate.Tracks.RemoveAll(t => t.Id == id);
            candidate.ImportedSliders.Add(slider);
            // Generated geometry depends on the emitted SV and its restoration boundaries.
            CopyEmittedTiming(candidate, before);
            var after = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets);
            CheckEquivalent(before.PlayableObjects, after.PlayableObjects);
            CheckEquivalent(before.PlayableHardRockObjects, after.PlayableHardRockObjects);
            if (!before.ObjectSequenceMatches || !after.ObjectSequenceMatches)
                throw new InvalidOperationException(L.Get("core.importEditing.preservePositionsFailed"));
            document.Tracks.RemoveAll(t => t.Id == id);
            document.ImportedSliders.Add(slider);
            document.TimingPoints.Clear();
            document.TimingPoints.AddRange(candidate.TimingPoints);
            return new(true, []);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or CatchConversionException)
        { return new(false, [error.Message]); }
    }

    private static void CopyEmittedTiming(MapDocument candidate, OsuWriteResult output)
    {
        var timing = output.ReadBack.DeepClone();
        if (candidate.SliderMultiplierOverride is not null)
            SliderMultiplierEditing.RebaseTiming(timing, candidate.SliderMultiplier,
                timing.ImportedSliders.Select(s => s.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min());
        candidate.TimingPoints.Clear();
        candidate.TimingPoints.AddRange(timing.TimingPoints.Select(p => p.DeepClone()));
    }

    private static void CheckEquivalent(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after)
    {
        if (before.Count != after.Count || !before.Zip(after).All(pair =>
            pair.First.SourceId == pair.Second.SourceId && pair.First.EventIndex == pair.Second.EventIndex
            && pair.First.Kind == pair.Second.Kind && Math.Abs(pair.First.TimeMs - pair.Second.TimeMs) < 1e-6
            && Math.Abs(pair.First.X - pair.Second.X) <= CatchStreamConverter.AlignmentTolerance))
            throw new InvalidOperationException(L.Get("core.importEditing.preservePositionsFailed"));
    }
}
