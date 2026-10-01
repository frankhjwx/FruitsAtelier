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
        var source = document.DeepClone(); source.SliderMultiplierOverride = null;
        var original = cache?.MultiplierBaseline(document, compensateTinyDroplets)
            ?? OsuBeatmapWriter.Serialize(source, compensateTinyDroplets, cache);
        var result = Rebase(original, multiplier);
        cache?.RememberMultiplier(document, result, compensateTinyDroplets);
        return result;
    }

    public static OsuWriteResult Rebase(OsuWriteResult before, double multiplier)
    {
        Validate(multiplier);
        var candidate = before.ReadBack.DeepClone();
        double ratio = candidate.SliderMultiplier / multiplier;
        if (ratio == 1) return before;
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
        string text = OsuBeatmapWriter.MultiplierText(candidate, ordered);
        var readBack = OsuBeatmapReader.Read(text, candidate.SourcePath, inferDuration: false);
        foreach (var slider in readBack.ImportedSliders) slider.Id = before.ObjectSources[slider.SourceOrder];
        foreach (var shower in readBack.BananaShowers) shower.Id = before.ObjectSources[shower.SourceOrder];
        var converted = CatchStreamConverter.Convert(readBack, false);
        var hardRock = converted.Success ? CatchPreviewMods.HardRock(readBack, converted) : [];
        if (!before.ObjectSequenceMatches || !converted.Success)
            throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
        var identities = before.PlayableObjects.ToDictionary(p => (p.SourceId, p.EventIndex));
        var parentFruits = before.PlayableObjects.Where(p => p.Kind == CatchObjectKind.Fruit)
            .GroupBy(p => p.SourceId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.EventIndex).ToArray());
        var fruitIdentities = new Dictionary<Guid, ConvertedCatchObject>();
        foreach (var group in readBack.Fruits.GroupBy(f => before.ObjectSources[f.SourceOrder]))
        {
            if (!parentFruits.TryGetValue(group.Key, out var originalsForParent))
                throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
            var fruits = group.OrderBy(f => f.SourceOrder).ToArray();
            if (fruits.Length != originalsForParent.Length) throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
            for (int i = 0; i < fruits.Length; i++) fruitIdentities[fruits[i].Id] = originalsForParent[i];
        }
        ConvertedCatchObject Identity(ConvertedCatchObject item)
        {
            if (!fruitIdentities.TryGetValue(item.SourceId, out var original)
                && !identities.TryGetValue((item.SourceId, item.EventIndex), out original))
                throw new InvalidDataException(L.Get("timing.sliderMultiplierPreservation"));
            return item with { SourceId = original.SourceId, EventIndex = original.EventIndex, IsStandalone = original.IsStandalone };
        }
        var playable = converted.Objects.Select(Identity).ToArray();
        var playableHardRock = hardRock.Select(Identity).ToArray();
        if (!Equivalent(before.PlayableObjects, playable)
            || !Equivalent(before.PlayableHardRockObjects, playableHardRock))
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

    private static void Validate(double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier < Minimum || multiplier > Maximum)
            throw new ArgumentException(L.Get("timing.sliderMultiplierRange"));
    }

    private static bool Equivalent(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after)
    {
        if (before.Count != after.Count) return false;
        var expected = before.ToDictionary(p => (p.SourceId, p.EventIndex));
        return after.All(item => expected.TryGetValue((item.SourceId, item.EventIndex), out var original)
            && original.Kind == item.Kind && Math.Abs(original.TimeMs - item.TimeMs) < .001
            && Math.Abs(original.X - item.X) < .001);
    }
}
