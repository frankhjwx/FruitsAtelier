using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class KiaiTimingEditing
{
    public static MapDocument? Prepare(MapDocument document, bool compensateTinyDroplets = true)
    {
        var (ordered, transitions, moved) = Analyze(document);
        if (moved.Count == 0) return null;
        var candidate = document.DeepClone();
        TimingPoint? Current(double time)
        {
            int low = 0, high = ordered.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (ordered[middle].TimeMs <= time) low = middle + 1; else high = middle;
            }
            return low > 0 ? ordered[low - 1] : ordered.FirstOrDefault(p => p.Uninherited);
        }
        int nextOrder = ordered.Select(p => p.SourceOrder).DefaultIfEmpty(-1).Max() + 1;
        bool KiaiAt(double time)
        {
            int low = 0, high = transitions.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (transitions[middle].Time <= time) low = middle + 1; else high = middle;
            }
            return low > 0 && transitions[low - 1].Kiai;
        }
        foreach (var point in candidate.TimingPoints)
        {
            int effects = (point.Effects & ~1) | (KiaiAt(point.TimeMs) ? 1 : 0);
            if (point.Effects != effects) { point.Effects = effects; point.OriginalLine = null; }
        }
        foreach (var transition in transitions.Where(t => moved.Contains(t.Point)))
        {
            var active = Current(transition.Time);
            var point = active?.DeepClone() ?? new TimingPoint();
            point.TimeMs = transition.Time; point.Uninherited = false; point.OriginalLine = null;
            // Reuse the source value so an effect-only line preserves exact SV and tick suppression.
            point.BeatLengthMs = active is { Uninherited: false } ? active.BeatLengthMs : -100;
            point.Effects = (point.Effects & ~1) | (transition.Kiai ? 1 : 0);
            point.SourceOrder = nextOrder++;
            candidate.TimingPoints.Add(point);
        }
        var pureEffectTimes = moved.Where(point =>
        {
            var previousPoint = Current(Math.BitDecrement(point.TimeMs));
            return !point.Uninherited && previousPoint is not null
                && point.BeatLengthMs.Equals(previousPoint.Uninherited ? -100 : previousPoint.BeatLengthMs)
                && point.SampleSet == previousPoint.SampleSet && point.SampleIndex == previousPoint.SampleIndex
                && point.Volume == previousPoint.Volume && point.Meter == previousPoint.Meter
                && (point.Effects & ~1) == (previousPoint.Effects & ~1);
        }).Select(p => p.TimeMs).ToHashSet();
        candidate.TimingPoints.RemoveAll(p => !p.Uninherited && pureEffectTimes.Contains(p.TimeMs));
        candidate.TimingPoints.Sort((a, b) => a.TimeMs != b.TimeMs ? a.TimeMs.CompareTo(b.TimeMs) : a.SourceOrder.CompareTo(b.SourceOrder));
        ValidateGameplay(document, candidate, compensateTinyDroplets);
        return candidate;
    }

    public static int CountOffsets(MapDocument document) => Analyze(document).Moved.Count;

    private static (TimingPoint[] Ordered, List<(TimingPoint Point, double Time, bool Kiai)> Transitions,
        HashSet<TimingPoint> Moved) Analyze(MapDocument document)
    {
        var ordered = document.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        var duplicateTimes = ordered.GroupBy(p => p.TimeMs).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var occupied = ordered.Select(p => p.TimeMs).ToHashSet();
        var timing = new TimingMap.Lookup(document);
        var transitions = new List<(TimingPoint Point, double Time, bool Kiai)>();
        bool previous = false;
        foreach (var point in ordered)
        {
            bool kiai = (point.Effects & 1) != 0;
            if (kiai == previous) continue;
            transitions.Add((point, point.TimeMs, kiai)); previous = kiai;
        }
        var moved = new HashSet<TimingPoint>();
        for (int i = 0; i < transitions.Count; i++)
        {
            var transition = transitions[i];
            if (duplicateTimes.Contains(transition.Time)) continue;
            double corrected = ExportTiming.CorrectHead(timing, transition.Time);
            if (corrected == transition.Time || occupied.Contains(corrected)
                || i > 0 && corrected <= transitions[i - 1].Time
                || i + 1 < transitions.Count && corrected >= transitions[i + 1].Time) continue;
            transitions[i] = transition with { Time = corrected };
            moved.Add(transition.Point);
            occupied.Add(corrected);
        }
        return (ordered, transitions, moved);
    }

    private static void ValidateGameplay(MapDocument document, MapDocument candidate, bool compensateTinyDroplets)
    {
        var conversion = CatchStreamConverter.Convert(document, compensateTinyDroplets);
        var after = CatchStreamConverter.Convert(candidate, compensateTinyDroplets);
        if (!conversion.Success || !after.Success || !Equivalent(document, conversion.Objects, candidate, after.Objects))
            throw new InvalidDataException(L.Get("mv.timingUnsafe"));
        var beforeExport = OsuBeatmapWriter.Serialize(document, compensateTinyDroplets);
        var afterExport = OsuBeatmapWriter.Serialize(candidate, compensateTinyDroplets);
        if (!Equivalent(beforeExport.ReadBack, beforeExport.PlayableObjects, afterExport.ReadBack, afterExport.PlayableObjects)
            || !Equivalent(beforeExport.ReadBack, beforeExport.PlayableHardRockObjects, afterExport.ReadBack, afterExport.PlayableHardRockObjects))
            throw new InvalidDataException(L.Get("mv.timingUnsafe"));
    }

    private static bool Equivalent(MapDocument before, IReadOnlyList<ConvertedCatchObject> original,
        MapDocument after, IReadOnlyList<ConvertedCatchObject> candidate)
    {
        if (original.Count != candidate.Count) return false;
        var beforeSamples = new HitsoundResolver(before, original);
        var afterSamples = new HitsoundResolver(after, candidate);
        for (int i = 0; i < original.Count; i++)
        {
            var a = original[i]; var b = candidate[i];
            if (a.Kind != b.Kind || a.TimeMs != b.TimeMs || a.X != b.X
                || beforeSamples.Describe(a) != afterSamples.Describe(b)) return false;
        }
        return true;
    }
}
