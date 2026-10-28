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
        if (after.ImportedSliders.Count == 0) return false;
        bool settingsChanged = before.DerandomizeFSliderDroplets != after.DerandomizeFSliderDroplets
            || before.DerandomizeDropletsForHardRock != after.DerandomizeDropletsForHardRock
            || before.RandomizeDropletStrength != after.RandomizeDropletStrength
            || before.RandomizeDropletSeed != after.RandomizeDropletSeed;
        bool managed = after.DerandomizeFSliderDroplets || after.DerandomizeDropletsForHardRock
            || after.RandomizeDropletStrength != 20 || after.RandomizeDropletSeed != 1337
            || after.ImportedSliders.Any(s => s.RandomizationSource is not null || s.DropletMode is not null);
        var oldSliders = before.ImportedSliders.ToDictionary(s => s.Id);
        if (after.ImportedSliders.Any(s => s.DropletMode != (oldSliders.TryGetValue(s.Id, out var old) ? old.DropletMode : null))) return true;
        if (after.ImportedSliders.Any(s => s.SliderVelocityMultiplier is not null
            && (!oldSliders.TryGetValue(s.Id, out var old) || s.SliderVelocityMultiplier != old.SliderVelocityMultiplier
                || s.TimeMs != old.TimeMs || s.X != old.X || s.Y != old.Y || s.PixelLength != old.PixelLength
                || s.PathType != old.PathType || s.SpanCount != old.SpanCount || !s.ControlPoints.ContentEquals(old.ControlPoints)))) return true;
        var oldTracks = before.Tracks.ToDictionary(t => t.Id);
        var fixedHeads = after.ImportedSliders.Where(s => s.SliderVelocityMultiplier is not null).Select(s => s.TimeMs).ToHashSet();
        if (after.Tracks.Any(t => t.StreamSnapDivisor is null && t.Nodes.Count > 0
            && (fixedHeads.Contains(t.Nodes[0].TimeMs) || fixedHeads.Contains(Math.Truncate(t.Nodes[0].TimeMs)))
            && (!oldTracks.TryGetValue(t.Id, out var old) || !t.ContentEquals(old)))) return true;
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
            if (document.ImportedSliders.Count == 0) return new(true, []);
            if (!document.DerandomizeFSliderDroplets && !document.DerandomizeDropletsForHardRock
                && document.RandomizeDropletStrength == 20 && document.RandomizeDropletSeed == 1337
                && document.ImportedSliders.All(s => s.RandomizationSource is null && s.DropletMode is null)) return new(true, []);
            var candidate = document.DeepClone();
            var nativeIds = candidate.ImportedSliders.Where(s => s.PreserveNativeGeometry
                && (s.DropletMode is null ? !document.DerandomizeFSliderDroplets : s.RandomizesDroplets(document))
                && document.RandomizeDropletStrength == 20 && document.RandomizeDropletSeed == 1337
                && (s.RandomizationTrack?.DropletRandomization?.Adjustments.Count ?? 0) == 0).Select(s => s.Id).ToHashSet();
            foreach (var slider in candidate.ImportedSliders.Where(s => nativeIds.Contains(s.Id)))
            {
                // Native randomization operates directly on the authored osu! path.
                if (slider.RandomizationSource is not { } source) continue;
                slider.X = source.X; slider.Y = source.Y; slider.PathType = source.PathType;
                slider.PixelLength = source.PixelLength;
                slider.ControlPoints.Clear(); slider.ControlPoints.AddRange(source.ControlPoints);
                if (slider.OriginalLine is { } line)
                {
                    static string N(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    var fields = line.Split(',');
                    fields[0] = N(slider.X); fields[1] = N(slider.Y);
                    fields[5] = slider.PathType + string.Concat(slider.ControlPoints.Skip(1).Select(p => "|" + N(p.X) + ":" + N(p.GeometryY)));
                    fields[7] = N(slider.PixelLength);
                    slider.OriginalLine = string.Join(',', fields);
                }
                slider.SliderVelocityMultiplier ??= slider.RandomizationBaseVelocity;
                slider.ProcessedSliderVelocityMultiplier = null;
                slider.RandomizationSource = null; slider.RandomizationTrack = null; slider.RandomizationBaseVelocity = null;
            }
            var sources = candidate.ImportedSliders.Where(s => !nativeIds.Contains(s.Id)).ToArray();
            var timingLookup = new TimingMap.Lookup(document);
            var previousLookup = previous is null ? null : new TimingMap.Lookup(previous);
            var previousSliders = previous?.ImportedSliders.ToDictionary(s => s.Id);
            var timeScales = new Dictionary<Guid, double>();
            var initialVelocities = sources.ToDictionary(s => s.Id,
                s => s.SliderVelocityMultiplier ?? (s.RandomizationTrack is not null && s.RandomizationBaseVelocity is { } savedVelocity
                    ? savedVelocity : timingLookup.At(s.TimeMs).SliderVelocityMultiplier));
            foreach (var slider in sources.Where(s => s.RandomizationTrack is not null))
            {
                if (previousLookup is null || previousSliders is null || !previousSliders.TryGetValue(slider.Id, out var oldSlider)) continue;
                var oldTiming = previousLookup.At(oldSlider.TimeMs);
                var nextTiming = timingLookup.At(slider.TimeMs);
                double oldSv = oldSlider.SliderVelocityMultiplier is { } oldOwn ? oldOwn * previous!.SliderMultiplier / previous.EffectiveSliderMultiplier : oldTiming.SliderVelocityMultiplier;
                double nextSv = slider.SliderVelocityMultiplier is { } nextOwn ? nextOwn * document.SliderMultiplier / document.EffectiveSliderMultiplier : nextTiming.SliderVelocityMultiplier;
                double velocityRatio = nextSv / oldSv;
                double oldVelocity = LegacyCatchRules.Velocity(oldTiming.BeatLengthMs,
                    previous!.EffectiveSliderMultiplier, oldSv);
                double nextVelocity = LegacyCatchRules.Velocity(nextTiming.BeatLengthMs,
                    document.EffectiveSliderMultiplier, nextSv);
                double scale = oldVelocity / nextVelocity;
                timeScales[slider.Id] = scale;
                if (slider.SliderVelocityMultiplier is null) initialVelocities[slider.Id] = Math.Clamp(initialVelocities[slider.Id] * velocityRatio, .1, 10);
            }
            var inheritedHeads = candidate.TimingPoints.Where(p => !p.Uninherited)
                .GroupBy(p => p.TimeMs).ToDictionary(g => g.Key, g => g.Last());
            var originalTiming = candidate.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
            foreach (var slider in sources.Where(s => s.RandomizationTrack is not null && s.SliderVelocityMultiplier is null))
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
                baseline.EndpointSnap = slider.EndpointSnap;
                candidate.Tracks.Add(baseline);
            }
            var fitted = ImportedSliderEditing.Convert(candidate,
                sources.Where(s => s.RandomizationTrack is null).Select(s => s.Id).ToArray(), fitPlayback: document.SliderImportState == SliderImportState.None);
            if (fitted.Failures.Count > 0)
                return new(false, fitted.Failures.Select(f => f.Reason).ToArray());
            var tracks = candidate.Tracks.ToDictionary(t => t.Id);
            foreach (var slider in sources)
            {
                var baseline = tracks[slider.Id];
                baseline.SliderVelocitySeed = initialVelocities[slider.Id] * document.SliderMultiplier / document.EffectiveSliderMultiplier;
                baseline.DropletMode = slider.DropletMode ?? baseline.DropletMode ?? SliderDropletMode.FollowMap;
                baseline.DropletRandomization ??= new();
                baseline.DropletRandomization.Enabled = true;
                baselineTracks[slider.Id] = baseline.DeepClone();
            }
            var sourceIds = sources.Select(s => s.Id).ToHashSet();
            var sharedHeads = candidate.Tracks.Where(t => t.StreamSnapDivisor is null && t.Nodes.Count > 0)
                .GroupBy(t => Math.Truncate(t.Nodes[0].TimeMs))
                .Where(g => g.Count() > 1 && g.Any(t => sourceIds.Contains(t.Id))).Select(g => g.ToArray()).ToArray();
            if (sharedHeads.Length > 0)
            {
                bool settled = false;
                var fittingCache = cache?.Source ?? new CatchConversionCache();
                for (int attempt = 0; attempt < 18; attempt++)
                {
                    var probe = CatchStreamConverter.Convert(candidate, compensateTinyDroplets, fittingCache);
                    if (!probe.Success) return new(false, probe.Diagnostics);
                    var fittedSliders = probe.Sliders.ToDictionary(s => s.SourceId);
                    bool changed = false;
                    foreach (var group in sharedHeads)
                    {
                        double sv = group.Where(t => sourceIds.Contains(t.Id)).Max(t => fittedSliders[t.Id].SliderVelocityMultiplier);
                        if (group.All(t => Math.Abs(fittedSliders[t.Id].SliderVelocityMultiplier - sv) <= 1e-9)) continue;
                        // Legacy determines the shared SV; other tracks must fit it without driving it upward.
                        foreach (var track in group)
                            if (sourceIds.Contains(track.Id)) track.SliderVelocitySeed = sv;
                            else track.FixedSliderVelocityMultiplier = sv;
                        changed = true;
                    }
                    if (!changed) { settled = true; break; }
                }
                if (!settled) return new(false, [L.Get("core.conversion.iterationLimit")]);
            }
            var expected = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets, cache);
            var materialized = expected.ReadBack.ImportedSliders.ToDictionary(s => s.Id);
            candidate.Tracks.RemoveAll(t => sourceIds.Contains(t.Id));
            foreach (var source in sources)
            {
                var slider = materialized[source.Id].DeepClone();
                slider.SourceOrder = source.SourceOrder;
                slider.EndpointSnap = source.EndpointSnap;
                slider.PreserveNativeGeometry = source.PreserveNativeGeometry;
                slider.DropletMode = source.DropletMode;
                slider.ProcessedSliderVelocityMultiplier = source.SliderVelocityMultiplier is null ? null
                    : StoredVelocity(slider.SliderVelocityMultiplier!.Value);
                slider.SliderVelocityMultiplier = source.SliderVelocityMultiplier;
                slider.RandomizationSource = (source.RandomizationSource ?? source).DeepClone();
                slider.RandomizationTrack = baselineTracks[source.Id];
                slider.RandomizationBaseVelocity = initialVelocities[source.Id];
                candidate.ImportedSliders.Add(slider);
            }
            if (sources.Any(s => s.SliderVelocityMultiplier is null)) CopyEmittedTiming(candidate, expected);
            var actual = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets, cache);
            CheckEquivalent(expected.PlayableObjects, actual.PlayableObjects);
            CheckEquivalent(expected.PlayableHardRockObjects, actual.PlayableHardRockObjects);
            if (!expected.ObjectSequenceMatches || !actual.ObjectSequenceMatches)
                throw new InvalidOperationException(L.Get("core.importEditing.preservePositionsFailed"));
            document.ImportedSliders.Clear(); document.ImportedSliders.AddRange(candidate.ImportedSliders);
            if (sources.Any(s => s.SliderVelocityMultiplier is null))
            { document.TimingPoints.Clear(); document.TimingPoints.AddRange(candidate.TimingPoints); }
            return new(true, []) { ValidatedOutput = actual };

            double StoredVelocity(double velocity)
            {
                if (document.EffectiveSliderMultiplier == document.SliderMultiplier) return velocity;
                float beatLength = (float)(100 / velocity);
                // Rebase into the middle of the float interval that rounds up to the emitted green.
                // An exact boundary can round up again after the inverse multiplier calculation.
                double magnitude = ((double)beatLength + MathF.BitDecrement(beatLength)) / 2;
                return 100 / (magnitude * document.SliderMultiplier / document.EffectiveSliderMultiplier);
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or CatchConversionException)
        { return new(false, [error.Message]); }
    }

    public static SliderRepresentationResult ConvertToTrack(MapDocument document, Guid id)
    {
        try
        {
            if (!document.ImportedSliders.Any(s => s.Id == id))
                throw new ArgumentException(L.Get("core.importEditing.notFound"), nameof(id));
            var candidate = document.DeepClone();
            var batch = SliderImportEditing.ConvertFollowingMap(candidate, [id]);
            if (batch.Tracks.Count == 0) return new(false, batch.Failures.Select(f => f.Reason).ToArray());
            _ = OsuBeatmapWriter.Serialize(candidate);
            document.ImportedSliders.RemoveAll(s => s.Id == id);
            document.Tracks.Add(batch.Tracks[0]);
            return new(true, []);
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
            slider.EndpointSnap = track.EndpointSnap;
            slider.RandomizationSource = slider.DeepClone();
            slider.RandomizationTrack = track.DeepClone();
            slider.DropletMode = track.DropletMode;
            slider.RandomizationBaseVelocity = slider.SliderVelocityMultiplier;
            candidate.Tracks.RemoveAll(t => t.Id == id);
            candidate.ImportedSliders.Add(slider);
            // Compatibility objects without independent SV still depend on emitted timing.
            if (candidate.ImportedSliders.Any(s => s.SliderVelocityMultiplier is null)) CopyEmittedTiming(candidate, before);
            var after = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets);
            CheckEquivalent(before.PlayableObjects, after.PlayableObjects);
            CheckEquivalent(before.PlayableHardRockObjects, after.PlayableHardRockObjects);
            if (!before.ObjectSequenceMatches || !after.ObjectSequenceMatches)
                throw new InvalidOperationException(L.Get("core.importEditing.preservePositionsFailed"));
            document.Tracks.RemoveAll(t => t.Id == id);
            document.ImportedSliders.Add(slider);
            if (candidate.ImportedSliders.Any(s => s.SliderVelocityMultiplier is null))
            { document.TimingPoints.Clear(); document.TimingPoints.AddRange(candidate.TimingPoints); }
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
