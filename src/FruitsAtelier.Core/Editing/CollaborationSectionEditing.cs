namespace FruitsAtelier.Core;

public static class CollaborationSectionEditing
{
    public static List<CollaborationEditRange> Ranges(IEnumerable<CollaborationReference> references, double finalEnd)
        => references.OrderBy(r => r.StartMs).Select(r => new CollaborationEditRange(r.StartMs, r.EndMs, r.EndMs == finalEnd)).ToList();

    public static bool Contains(IReadOnlyList<CollaborationEditRange> ranges, double start, double end)
    {
        for (int i = 0; i < ranges.Count; i++)
        {
            var r = ranges[i];
            if (start >= r.StartMs && (start < r.EndMs || r.IncludeEnd && start == r.EndMs) && end <= r.EndMs) return true;
        }
        return false;
    }

    public static void RemoveOutside(MapDocument document, IReadOnlyList<CollaborationEditRange> ranges)
    {
        var keep = EditableParents(document, ranges);
        document.Fruits.RemoveAll(f => !keep.Contains(f.Id));
        document.Tracks.RemoveAll(t => !keep.Contains(t.Id));
        document.ImportedSliders.RemoveAll(s => !keep.Contains(s.Id));
        document.BananaShowers.RemoveAll(s => !keep.Contains(s.Id));
        document.HitsoundOverrides.RemoveAll(s => !keep.Contains(s.SourceId));
    }

    private static HashSet<Guid> EditableParents(MapDocument d, IReadOnlyList<CollaborationEditRange> ranges)
    {
        var ids = d.Fruits.Where(f => Contains(ranges, f.TimeMs, f.TimeMs)).Select(f => f.Id).ToHashSet();
        foreach (var t in d.Tracks)
            if (t.Nodes.Count > 0 && Contains(ranges, t.Nodes.Min(n => n.TimeMs), CurveMath.EndTimeMs(t))) ids.Add(t.Id);
        var lengths = new ImportedSliderLengthCache();
        var timing = new TimingMap.Lookup(d);
        foreach (var s in d.ImportedSliders)
            if (Contains(ranges, s.TimeMs, lengths.EndTimeMs(d, s, timing))) ids.Add(s.Id);
        foreach (var s in d.BananaShowers)
            if (Contains(ranges, s.TimeMs, s.EndTimeMs)) ids.Add(s.Id);
        return ids;
    }

    // Compare at the transaction boundary so paste, retiming and inspector edits obey the same interval lock.
    public static bool ChangesLockedContent(MapDocument before, MapDocument after)
    {
        if (before.CollaborationEditableRanges is not { } ranges) return false;
        var editableBefore = EditableParents(before, ranges);
        var editableAfter = EditableParents(after, ranges);
        var unchanged = before.UnchangedObjectIds(after).ToHashSet();
        bool Changed(IEnumerable<Guid> ids, HashSet<Guid> editable) => ids.Any(id => !editable.Contains(id) && !unchanged.Contains(id));
        static IEnumerable<Guid> Parents(MapDocument d) => d.Fruits.Select(f => f.Id).Concat(d.Tracks.Select(t => t.Id))
            .Concat(d.ImportedSliders.Select(s => s.Id)).Concat(d.BananaShowers.Select(s => s.Id));
        if (Changed(Parents(before), editableBefore) || Changed(Parents(after), editableAfter)) return true;
        var soundsBefore = before.HitsoundOverrides.Where(s => !editableBefore.Contains(s.SourceId));
        var soundsAfter = after.HitsoundOverrides.Where(s => !editableAfter.Contains(s.SourceId));
        if (!soundsBefore.SequenceEqual(soundsAfter)) return true;
        var timingBefore = before.TimingPoints.Where(p => !Contains(ranges, p.TimeMs, p.TimeMs)).ToArray();
        var timingAfter = after.TimingPoints.Where(p => !Contains(ranges, p.TimeMs, p.TimeMs)).ToArray();
        if (timingBefore.Length != timingAfter.Length || timingBefore.Zip(timingAfter).Any(p => !p.First.ContentEquals(p.Second))) return true;
        if (before.SliderTickRate != after.SliderTickRate || before.EffectiveSliderMultiplier != after.EffectiveSliderMultiplier
            || before.LegacySliderTicks != after.LegacySliderTicks) return true;
        bool timingChanged = before.BeatLengthMs != after.BeatLengthMs || before.TimingOffsetMs != after.TimingOffsetMs
            || before.TimingPoints.Count != after.TimingPoints.Count || before.TimingPoints.Zip(after.TimingPoints).Any(p => !p.First.ContentEquals(p.Second));
        if (!timingChanged) return false;
        var beforeLookup = new TimingMap.Lookup(before); var afterLookup = new TimingMap.Lookup(after);
        // An edit inside an allowed section may carry its inherited timing into the locked gap.
        foreach (double time in ranges.Select(r => r.IncludeEnd ? Math.BitIncrement(r.EndMs) : r.EndMs).Prepend(0))
        {
            if (Contains(ranges, time, time)) continue;
            if (beforeLookup.At(time) != afterLookup.At(time) || Samples(before, time) != Samples(after, time)) return true;
        }
        return false;
        static (int, int, int, int) Samples(MapDocument d, double time)
        {
            var p = d.TimingPoints.Where(p => p.TimeMs <= time).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).LastOrDefault();
            return (p?.SampleSet ?? 0, p?.SampleIndex ?? 0, p?.Volume ?? 100, p?.Effects ?? 0);
        }
    }
}
