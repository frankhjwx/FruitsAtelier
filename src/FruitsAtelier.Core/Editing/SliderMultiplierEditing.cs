using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class SliderMultiplierEditing
{
    public const double Minimum = .4, Maximum = 3.6;

    public static void Apply(MapDocument document, double multiplier, bool compensateTinyDroplets = true)
    {
        Validate(multiplier);
        if (multiplier == document.EffectiveSliderMultiplier) return;
        var source = document.DeepClone(); source.SliderMultiplierOverride = null;
        var before = OsuBeatmapWriter.Serialize(source, compensateTinyDroplets);
        _ = Rebase(before, multiplier);
        document.SliderMultiplierOverride = multiplier == document.SliderMultiplier ? null : multiplier;
    }

    internal static OsuWriteResult Serialize(MapDocument document, double multiplier, bool compensateTinyDroplets, OsuWriteCache? cache)
    {
        Validate(multiplier);
        if (cache?.Multiplier(document, compensateTinyDroplets) is { } cached) return cached;
        var original = cache?.MultiplierBaseline(document, compensateTinyDroplets);
        if (original is null)
        {
            var source = document.DeepClone(); source.SliderMultiplierOverride = null;
            original = OsuBeatmapWriter.Serialize(source, compensateTinyDroplets, cache);
        }
        var result = Rebase(original, multiplier, cache);
        cache?.RememberMultiplier(document, result, compensateTinyDroplets);
        return result;
    }

    public static OsuWriteResult Rebase(OsuWriteResult before, double multiplier)
        => Rebase(before, multiplier, null);

    private static OsuWriteResult Rebase(OsuWriteResult before, double multiplier, OsuWriteCache? cache)
    {
        Validate(multiplier);
        if (multiplier == before.ReadBack.SliderMultiplier) return before;
        var candidate = Candidate(before, multiplier);
        string text = OsuBeatmapWriter.MultiplierText(candidate, candidate.TimingPoints.ToArray());
        MapDocument readBack;
        cache?.MultiplierParsedSliders.Begin();
        try { readBack = OsuBeatmapReader.Read(text, candidate.SourcePath, inferDuration: false, cache?.MultiplierParsedSliders); }
        finally { cache?.MultiplierParsedSliders.End(); }
        foreach (var slider in readBack.ImportedSliders) slider.Id = before.ObjectSources[slider.SourceOrder];
        foreach (var shower in readBack.BananaShowers) shower.Id = before.ObjectSources[shower.SourceOrder];
        var converted = CatchStreamConverter.Convert(readBack, false, cache?.MultiplierReadBack);
        var hardRock = converted.Success ? CatchPreviewMods.HardRock(readBack, converted) : [];
        return Finish(before, text, readBack, converted, hardRock);
    }

    public static void CheckLimits(OsuWriteResult before, double multiplier)
    {
        Validate(multiplier);
        if (multiplier == before.ReadBack.SliderMultiplier) return;
        if (before.MultiplierAnalysis is { } analysis && (multiplier < analysis.Minimum || multiplier > analysis.Maximum))
            throw new InvalidDataException(L.Get("timing.sliderMultiplierSvLimit"));
    }

    public static bool TryRebase(OsuWriteResult before, double multiplier, out OsuWriteResult? result, double budgetMs = 4)
    {
        result = null;
        CheckLimits(before, multiplier);
        if (multiplier == before.ReadBack.SliderMultiplier) { result = before; return true; }
        if (before.MultiplierAnalysis is not { } analysis) return false;
        if (double.IsFinite(budgetMs) && !analysis.Prepared) return false;
        if (double.IsFinite(budgetMs) && analysis.Conversion.Objects.Count > 4096) return false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var candidate = Candidate(before, multiplier);
        var timing = new TimingMap.Lookup(candidate);
        var changed = new Dictionary<Guid, IReadOnlyList<ConvertedCatchObject>>();
        var sliders = new List<GeneratedSlider>(analysis.Conversion.Sliders.Count);
        foreach (var slider in analysis.Conversion.Sliders)
        {
            if (watch.Elapsed.TotalMilliseconds > budgetMs) return false;
            var import = analysis.Imports[slider.SourceId];
            var state = timing.At(import.TimeMs);
            double velocity = LegacyCatchRules.Velocity(state.BeatLengthMs, multiplier, state.SliderVelocityMultiplier);
            if (velocity == slider.Velocity) { sliders.Add(slider); continue; }
            if (slider.ImportedGeometry is not { } path || slider.ImportedOffsets is not { } offsets) return false;
            var previous = analysis.Objects[slider.SourceId];
            if (double.IsFinite(budgetMs) && previous.Length > 1024) return false;
            double duration = path.Distance / velocity;
            if (!double.IsFinite(duration) || duration <= 0 || import.TimeMs + duration * import.SpanCount > int.MaxValue) return false;
            double tickDistance = velocity * state.BeatLengthMs / candidate.SliderTickRate;
            List<NestedCatchEvent> nested;
            try { nested = LegacyCatchRules.CreateNested(import.TimeMs, duration, velocity, tickDistance, path.Distance, import.SpanCount); }
            catch (CatchConversionException) { return false; }
            if (nested.Count != previous.Length || offsets.Count != previous.Length) return false;
            var objects = new ConvertedCatchObject[nested.Count];
            for (int i = 0; i < nested.Count; i++)
            {
                var item = nested[i];
                // Equal kinds/counts consume identical RNG, including all downstream parents.
                if (item.Kind != previous[i].Kind || Math.Abs(item.TimeMs - previous[i].TimeMs) >= .001) return false;
                float pathX = Math.Clamp((float)import.X, 0, 512) + path.PositionAt(item.Progress).X;
                float offset = item.Kind == CatchObjectKind.TinyDroplet ? Math.Clamp(offsets[i], -pathX, 512 - pathX) : 0;
                float x = Math.Clamp(pathX + offset, 0, 512);
                if (Math.Abs(x - previous[i].X) >= .001) return false;
                objects[i] = new(import.Id, i, item.Kind, item.TimeMs, x, x, pathX, offset);
            }
            changed[import.Id] = objects;
            sliders.Add(new()
            {
                SourceId = slider.SourceId, IsImported = true, SpanCount = slider.SpanCount, StartTimeMs = slider.StartTimeMs,
                DurationMs = duration * slider.SpanCount, Velocity = velocity, SliderVelocityMultiplier = state.SliderVelocityMultiplier,
                TickDistance = tickDistance, Length = slider.Length, Path = slider.Path, TinyCompensationApplied = false
            });
        }
        if (watch.Elapsed.TotalMilliseconds > budgetMs) return false;
        var converted = new CatchConversionResult
        {
            Success = true, Diagnostics = analysis.Conversion.Diagnostics, Sliders = sliders,
            Objects = analysis.Objects.SelectMany(p => changed.GetValueOrDefault(p.Key) ?? p.Value)
                .OrderBy(o => o.TimeMs).ThenBy(o => o.IsStandalone ? o.TimeMs : analysis.Parents[o.SourceId].Time)
                .ThenBy(o => analysis.Parents[o.SourceId].Order).ToArray()
        };
        var hardRock = CatchPreviewMods.HardRock(candidate, converted);
        if (watch.Elapsed.TotalMilliseconds > budgetMs) return false;
        string text = OsuBeatmapWriter.MultiplierText(candidate, candidate.TimingPoints.ToArray());
        try { result = Finish(before, text, candidate, converted, hardRock, reuseIdentities: true); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static MapDocument Candidate(OsuWriteResult before, double multiplier)
    {
        CheckLimits(before, multiplier);
        var candidate = before.ReadBack.DeepClone();
        double ratio = candidate.SliderMultiplier / multiplier;
        candidate.SliderMultiplier = multiplier;
        var points = candidate.TimingPoints;
        var originals = points.Select(p => p.DeepClone()).ToArray();
        foreach (var point in points.Where(p => !p.Uninherited))
        {
            if (double.IsNaN(point.BeatLengthMs)) continue;
            double magnitude = point.BeatLengthMs < 0 ? Math.Clamp((float)-point.BeatLengthMs, 10, 1000) : 100;
            point.BeatLengthMs = BeatLength(magnitude);
        }
        // Red points reset inherited SV. Compensate each reset without regenerating slider geometry.
        foreach (var group in originals.GroupBy(p => p.TimeMs))
        {
            if (group.Any(p => !p.Uninherited)) continue;
            var point = group.First().DeepClone();
            point.Uninherited = false; point.BeatLengthMs = BeatLength(100); point.OriginalLine = null;
            points.Add(point);
        }
        if (points.Count == 0 || points.All(p => p.TimeMs > 0))
            points.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = BeatLength(100) });
        var ordered = points.OrderBy(p => p.TimeMs).ThenBy(p => p.Uninherited ? 0 : 1).ThenBy(p => p.SourceOrder).ToArray();
        candidate.TimingPoints.Clear(); candidate.TimingPoints.AddRange(ordered);
        return candidate;

        double BeatLength(double magnitude)
        {
            double scaled = magnitude / ratio;
            if (scaled < 10 || scaled > 1000) throw new InvalidDataException(L.Get("timing.sliderMultiplierSvLimit"));
            float rounded = (float)scaled;
            // Stable rounds inherited beat lengths to float; avoid shortening exact-integer spans.
            if (rounded < scaled) rounded = MathF.BitIncrement(rounded);
            return -rounded;
        }
    }

    private static OsuWriteResult Finish(OsuWriteResult before, string text, MapDocument readBack,
        CatchConversionResult converted, IReadOnlyList<ConvertedCatchObject> hardRock, bool reuseIdentities = false)
    {
        if (!before.ObjectSequenceMatches || !converted.Success)
            throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
        var analysis = reuseIdentities ? before.MultiplierAnalysis : null;
        var identities = analysis?.Normal ?? before.PlayableObjects.ToDictionary(p => (p.SourceId, p.EventIndex));
        var parentFruits = analysis is not null ? [] : before.PlayableObjects.Where(p => p.Kind == CatchObjectKind.Fruit)
            .GroupBy(p => p.SourceId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.EventIndex).ToArray());
        var fruitIdentities = new Dictionary<Guid, ConvertedCatchObject>();
        foreach (var group in (analysis is not null ? [] : readBack.Fruits).GroupBy(f => before.ObjectSources[f.SourceOrder]))
        {
            if (!parentFruits.TryGetValue(group.Key, out var originalsForParent))
                throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
            var fruits = group.OrderBy(f => f.SourceOrder).ToArray();
            if (fruits.Length != originalsForParent.Length) throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
            for (int i = 0; i < fruits.Length; i++) fruitIdentities[fruits[i].Id] = originalsForParent[i];
        }
        ConvertedCatchObject Identity(ConvertedCatchObject item)
        {
            if (analysis is not null)
            {
                var identity = analysis.Identities[(item.SourceId, item.EventIndex)];
                return item with { SourceId = identity.SourceId, EventIndex = identity.EventIndex, IsStandalone = identity.IsStandalone };
            }
            if (!fruitIdentities.TryGetValue(item.SourceId, out var original)
                && !identities.TryGetValue((item.SourceId, item.EventIndex), out original))
                throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
            return item with { SourceId = original.SourceId, EventIndex = original.EventIndex, IsStandalone = original.IsStandalone };
        }
        var playable = converted.Objects.Select(Identity).ToArray();
        var playableHardRock = hardRock.Select(Identity).ToArray();
        if (!Equivalent(before.PlayableObjects, playable, analysis?.Normal)
            || !Equivalent(before.PlayableHardRockObjects, playableHardRock, analysis?.HardRock))
            throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
        OsuBeatmapReader.SetDuration(readBack, converted.Sliders.Select(s => s.StartTimeMs + s.DurationMs));
        var ends = readBack.Fruits.Select(f => (Id: before.ObjectSources[f.SourceOrder], End: f.TimeMs))
            .Concat(converted.Sliders.Select(s => (Id: s.SourceId, End: s.StartTimeMs + s.DurationMs)))
            .Concat(readBack.BananaShowers.Select(s => (Id: s.Id, End: s.EndTimeMs)))
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.Max(p => p.End));
        return new()
        {
            Text = text, ReadBack = readBack, ObjectSources = before.ObjectSources, Diagnostics = before.Diagnostics,
            ObjectSequenceMatches = true, PlayableObjects = playable,
            PlayableHardRockObjects = playableHardRock, PlayableEndTimes = ends,
            MaxTimeQuantizationMs = before.MaxTimeQuantizationMs, MaxCoordinateQuantization = before.MaxCoordinateQuantization,
            MaxConvertedTimeErrorMs = before.MaxConvertedTimeErrorMs,
            MaxConvertedXError = before.MaxConvertedXError
        };

    }

    private static void Validate(double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier < Minimum || multiplier > Maximum)
            throw new ArgumentException(L.Get("timing.sliderMultiplierRange"));
    }

    private static bool Equivalent(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after,
        Dictionary<(Guid, int), ConvertedCatchObject>? cached = null)
    {
        if (before.Count != after.Count) return false;
        var expected = cached ?? before.ToDictionary(p => (p.SourceId, p.EventIndex));
        return after.All(item => expected.TryGetValue((item.SourceId, item.EventIndex), out var original)
            && original.Kind == item.Kind && Math.Abs(original.TimeMs - item.TimeMs) < .001
            && Math.Abs(original.X - item.X) < .001);
    }
}
