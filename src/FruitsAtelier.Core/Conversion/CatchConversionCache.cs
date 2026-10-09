namespace FruitsAtelier.Core;

// One editor owns each cache. Results are immutable snapshots and are never shared across workers.
public sealed class CatchConversionCache
{
    private sealed record Entry(CurveTrack? Track, ImportedSlider? Imported, BananaShower? Banana,
        TimingState Timing, CatchLegacyRandom Before, CatchLegacyRandom After, GeneratedSlider? Slider, IReadOnlyList<ConvertedCatchObject> Objects, CatchLegacyRandom? HardRockBefore);
    private readonly Dictionary<Guid, Entry> entries = new();
    private sealed record TrackPositions(CurveTrack Snapshot, Dictionary<double, double> Values);
    private readonly Dictionary<Guid, TrackPositions> trackPositions = new();
    private readonly HashSet<Guid> seen = new();
    private (double, double, double, double, double, double, bool, double, int, bool, bool) settings;
    private TimingMap.Lookup? timing;
    internal void Begin(MapDocument document, bool compensation)
    {
        var current = (document.DurationMs, document.BeatLengthMs, document.TimingOffsetMs, document.ApproachRate,
            document.SliderMultiplier, document.SliderTickRate, compensation, document.RandomizeDropletStrength, document.RandomizeDropletSeed, document.DerandomizeFSliderDroplets, document.DerandomizeDropletsForHardRock);
        if (current != settings)
        {
            entries.Clear(); trackPositions.Clear(); settings = current;
        }
        if (timing is null || !timing.MatchesTiming(document)) timing = new(document);
        seen.Clear();
    }
    internal void End()
    {
        foreach (var id in entries.Keys.Where(id => !seen.Contains(id)).ToArray()) entries.Remove(id);
        foreach (var id in trackPositions.Keys.Where(id => !seen.Contains(id)).ToArray()) trackPositions.Remove(id);
    }
    internal Func<double, double> PositionAtTime(CurveTrack track)
    {
        if (!trackPositions.TryGetValue(track.Id, out var positions) || !Equal(track, positions.Snapshot, includeRandomization: false))
            trackPositions[track.Id] = positions = new(track.DeepClone(), new());
        // Incoming RNG changes tiny compensation, but not the authored target curve.
        return time =>
        {
            if (!positions.Values.TryGetValue(time, out double x))
            {
                if (positions.Values.Count >= 16384) positions.Values.Clear();
                positions.Values[time] = x = CurveMath.PositionAtTime(positions.Snapshot, time);
            }
            return x;
        };
    }
    internal bool TryGet(CurveTrack? track, ImportedSlider? imported, BananaShower? banana, ref CatchLegacyRandom rng,
        CatchLegacyRandom? hardRockBefore, out GeneratedSlider? slider, out IReadOnlyList<ConvertedCatchObject> objects)
    {
        Guid id = track?.Id ?? imported?.Id ?? banana!.Id; seen.Add(id);
        slider = null; objects = [];
        if (!entries.TryGetValue(id, out var entry) || !rng.SameState(entry.Before)
            || hardRockBefore is { } hr && (entry.HardRockBefore is not { } savedHr || !hr.SameState(savedHr))
            || (track is not null ? entry.Track is null || !Equal(track, entry.Track)
                : imported is not null ? entry.Imported is null || !Equal(imported, entry.Imported)
                : entry.Banana is null || banana!.TimeMs != entry.Banana.TimeMs || banana.EndTimeMs != entry.Banana.EndTimeMs
                    || banana.OriginalLine != entry.Banana.OriginalLine)) return false;
        // Sliders and streams lock their timing at the head; unrelated timing edits
        // cannot invalidate their geometry or nested events. Bananas use no timing.
        if (entry.Timing != TimingAt(track, imported)) return false;
        rng = entry.After; slider = entry.Slider; objects = entry.Objects; return true;
    }
    internal void Store(CurveTrack? track, ImportedSlider? imported, BananaShower? banana,
        CatchLegacyRandom before, CatchLegacyRandom after, GeneratedSlider? slider, IReadOnlyList<ConvertedCatchObject> objects, CatchLegacyRandom? hardRockBefore = null)
    {
        entries[track?.Id ?? imported?.Id ?? banana!.Id] = new(track?.DeepClone(), imported?.DeepClone(), banana?.DeepClone(),
            TimingAt(track, imported), before, after, slider, objects, hardRockBefore);
    }

    internal IReadOnlyList<ConvertedCatchObject>? ContextObjects(CurveTrack? track = null, ImportedSlider? imported = null, BananaShower? banana = null)
    {
        Guid id = track?.Id ?? imported?.Id ?? banana!.Id;
        if (!entries.TryGetValue(id, out var entry) || entry.Timing != TimingAt(track, imported)) return null;
        bool same = track is not null ? entry.Track is not null && Equal(track, entry.Track)
            : imported is not null ? entry.Imported is not null && Equal(imported, entry.Imported)
            : entry.Banana is not null && banana!.TimeMs == entry.Banana.TimeMs && banana.EndTimeMs == entry.Banana.EndTimeMs;
        return same ? entry.Objects : null;
    }

    private TimingState TimingAt(CurveTrack? track, ImportedSlider? imported)
        => track is not null ? timing!.At(track.Nodes[0].TimeMs) : imported is not null ? timing!.At(imported.TimeMs) : default;

    private static bool Equal(CurveTrack a, CurveTrack b, bool includeRandomization = true)
    {
        if (a.Id != b.Id || a.Kind != b.Kind || a.Name != b.Name || a.SourceOrder != b.SourceOrder || a.SpanCount != b.SpanCount
            || a.OriginalLine != b.OriginalLine || a.CompensateTinyDroplets != b.CompensateTinyDroplets || a.Nodes.Count != b.Nodes.Count
            || a.StreamSnapDivisor != b.StreamSnapDivisor || !StackEnvelope.Equal(a.Stack, b.Stack)
            || includeRandomization && !DropletRandomization.Equal(a.DropletRandomization, b.DropletRandomization)) return false;
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            var x = a.Nodes[i]; var y = b.Nodes[i];
            if (x.Id != y.Id || x.TimeMs != y.TimeMs || x.X != y.X || x.HandleIn != y.HandleIn || x.HandleOut != y.HandleOut || x.OutgoingKind != y.OutgoingKind || !ControlCurve.Equal(x.OutgoingCurve, y.OutgoingCurve)) return false;
        }
        return true;
    }

    // Parent order is applied by the converter before cache lookup; the incoming RNG state is checked separately.
    private static bool Equal(ImportedSlider a, ImportedSlider b) => a.Id == b.Id && a.OriginalLine == b.OriginalLine
        && a.X == b.X && a.Y == b.Y && a.TimeMs == b.TimeMs && a.PathType == b.PathType
        && a.SpanCount == b.SpanCount && a.PixelLength == b.PixelLength && a.ControlPoints.SequenceEqual(b.ControlPoints);
}
