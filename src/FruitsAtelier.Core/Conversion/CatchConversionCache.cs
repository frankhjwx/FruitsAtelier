namespace FruitsAtelier.Core;

// One editor owns each cache. Results are immutable snapshots and are never shared across workers.
public sealed class CatchConversionCache
{
    private sealed record Entry(CurveTrack? Track, ImportedSlider? Imported, BananaShower? Banana,
        CatchLegacyRandom Before, CatchLegacyRandom After, GeneratedSlider? Slider, IReadOnlyList<ConvertedCatchObject> Objects);
    private readonly Dictionary<Guid, Entry> entries = new();
    private readonly HashSet<Guid> seen = new();
    private (double, double, double, double, double, double, bool) settings;
    private TimingPoint[] timing = [];
    internal void Begin(MapDocument document, bool compensation)
    {
        var current = (document.DurationMs, document.BeatLengthMs, document.TimingOffsetMs, document.ApproachRate,
            document.SliderMultiplier, document.SliderTickRate, compensation);
        if (current != settings || timing.Length != document.TimingPoints.Count
            || timing.Where((p, i) => !p.ContentEquals(document.TimingPoints[i])).Any())
        {
            entries.Clear(); settings = current;
            timing = document.TimingPoints.Select(p => p.DeepClone()).ToArray();
        }
        seen.Clear();
    }
    internal void End()
    {
        foreach (var id in entries.Keys.Where(id => !seen.Contains(id)).ToArray()) entries.Remove(id);
    }
    internal bool TryGet(CurveTrack? track, ImportedSlider? imported, BananaShower? banana, ref CatchLegacyRandom rng,
        out GeneratedSlider? slider, out IReadOnlyList<ConvertedCatchObject> objects)
    {
        Guid id = track?.Id ?? imported?.Id ?? banana!.Id; seen.Add(id);
        slider = null; objects = [];
        if (!entries.TryGetValue(id, out var entry) || !rng.SameState(entry.Before)
            || (track is not null ? entry.Track is null || !Equal(track, entry.Track)
                : imported is not null ? entry.Imported is null || !imported.ContentEquals(entry.Imported)
                : entry.Banana is null || !banana!.ContentEquals(entry.Banana))) return false;
        rng = entry.After; slider = entry.Slider; objects = entry.Objects; return true;
    }
    internal void Store(CurveTrack? track, ImportedSlider? imported, BananaShower? banana,
        CatchLegacyRandom before, CatchLegacyRandom after, GeneratedSlider? slider, IReadOnlyList<ConvertedCatchObject> objects)
    {
        entries[track?.Id ?? imported?.Id ?? banana!.Id] = new(track?.DeepClone(), imported?.DeepClone(), banana?.DeepClone(), before, after, slider, objects);
    }

    private static bool Equal(CurveTrack a, CurveTrack b)
    {
        if (a.Id != b.Id || a.Kind != b.Kind || a.Name != b.Name || a.SourceOrder != b.SourceOrder || a.SpanCount != b.SpanCount
            || a.OriginalLine != b.OriginalLine || a.CompensateTinyDroplets != b.CompensateTinyDroplets || a.Nodes.Count != b.Nodes.Count) return false;
        for (int i = 0; i < a.Nodes.Count; i++)
        {
            var x = a.Nodes[i]; var y = b.Nodes[i];
            if (x.Id != y.Id || x.TimeMs != y.TimeMs || x.X != y.X || x.HandleIn != y.HandleIn || x.HandleOut != y.HandleOut || x.OutgoingKind != y.OutgoingKind) return false;
        }
        return true;
    }
}
