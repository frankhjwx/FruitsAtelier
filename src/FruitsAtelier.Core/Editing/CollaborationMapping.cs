using System.Security.Cryptography;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class CollaborationMapping
{
    public sealed record Result(MapDocument Document, HitsoundResourcePlan Resources);

    public static void ValidateReferences(BeatmapProject project, Guid targetId, IReadOnlyList<CollaborationReference> references)
    {
        new MapDocument { CollaborationReferences = references.ToList() }.ValidateCollaborationReferences();
        var ids = project.Difficulties.Select(d => d.Id).ToHashSet();
        if (!ids.Contains(targetId) || references.Any(r => !ids.Contains(r.DifficultyId)))
            throw new InvalidDataException(L.Get("collab.missing"));
        if (references.Any(r => r.DifficultyId == targetId)) throw new InvalidDataException(L.Get("collab.selfReference"));
    }

    public static Result Synchronize(BeatmapProject project, Guid targetId,
        IReadOnlyList<CollaborationReference> references, bool compensateTinyDroplets = true, OsuWriteCache? outputCache = null)
    {
        ValidateReferences(project, targetId, references);
        var byId = project.Difficulties.ToDictionary(d => d.Id);
        var target = byId[targetId].Document;
        var result = target.DeepClone();
        result.CollaborationReferences = references.Count == 0 ? null : references.ToList();
        RemoveParents(result, ParentIds(target, time => references.Any(r => Contains(r, time))).ToHashSet());
        result.TimingPoints.RemoveAll(p => references.Any(r => Contains(r, p.TimeMs)));
        var origins = new Dictionary<Guid, (MapDocument Document, Guid Id, Guid Difficulty)>();
        foreach (var reference in references.OrderBy(r => r.StartMs))
        {
            var source = byId[reference.DifficultyId].Document;
            var timing = new TimingMap.Lookup(source);
            var selected = ParentIds(source, time => Contains(reference, time)).ToHashSet();
            Guid Remap(Guid id) => MappedId(reference.Id, reference.DifficultyId, id);
            foreach (var id in selected) origins.Add(Remap(id), (source, id, reference.DifficultyId));
            foreach (var fruit in source.Fruits.Where(f => selected.Contains(f.Id)))
            { var copy = fruit.DeepClone(); copy.Id = Remap(copy.Id); result.Fruits.Add(copy); }
            foreach (var track in source.Tracks.Where(t => selected.Contains(t.Id)))
            {
                var copy = track.DeepClone(); RemapTrack(copy, Remap);
                copy.DropletMode = track.RandomizesDroplets(source) ? SliderDropletMode.Randomize : SliderDropletMode.Derandomize;
                result.Tracks.Add(copy);
            }
            foreach (var slider in source.ImportedSliders.Where(s => selected.Contains(s.Id)))
            {
                var copy = slider.DeepClone(); copy.Id = Remap(copy.Id);
                if (copy.RandomizationSource is { } baseline) baseline.Id = copy.Id;
                if (copy.RandomizationTrack is { } track) RemapTrack(track, Remap);
                copy.SliderVelocityMultiplier = copy.SliderVelocityMultiplier is { } authored
                    ? authored * source.SliderMultiplier / target.SliderMultiplier
                    : timing.At(copy.TimeMs).SliderVelocityMultiplier * source.EffectiveSliderMultiplier / target.SliderMultiplier;
                if (copy.ProcessedSliderVelocityMultiplier is { } processed)
                    copy.ProcessedSliderVelocityMultiplier = processed * source.SliderMultiplier / target.SliderMultiplier;
                result.ImportedSliders.Add(copy);
            }
            foreach (var shower in source.BananaShowers.Where(s => selected.Contains(s.Id)))
            { var copy = shower.DeepClone(); copy.Id = Remap(copy.Id); result.BananaShowers.Add(copy); }
            if (source.SliderTickRate == target.SliderTickRate && source.LegacySliderTicks == target.LegacySliderTicks)
                foreach (var sound in source.HitsoundOverrides.Where(s => selected.Contains(s.SourceId)))
                    result.HitsoundOverrides.Add(sound with { SourceId = Remap(sound.SourceId) });
            AddBoundary(source, result, reference.StartMs);
            foreach (var point in source.TimingPoints.Where(p => Contains(reference, p.TimeMs)))
            {
                var copy = point.DeepClone(); ExplicitBank(copy, source); result.TimingPoints.Add(copy);
            }
            if (!references.Any(r => r.StartMs == reference.EndMs)) AddBoundary(target, result, reference.EndMs);
            result.DurationMs = Math.Max(result.DurationMs, source.DurationMs);
        }
        var ordered = result.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        result.TimingPoints.Clear(); result.TimingPoints.AddRange(ordered);
        var merged = OsuBeatmapWriter.Serialize(result, compensateTinyDroplets, outputCache);
        var currentResolver = new HitsoundResolver(result, merged.PlayableObjects);
        var edges = merged.PlayableObjects.Where(o => o.Kind == CatchObjectKind.Fruit).GroupBy(o => o.SourceId)
            .SelectMany(g => g.Select((o, i) => (Key: (o.SourceId, o.EventIndex), Edge: o.IsStandalone ? 0 : i)))
            .ToDictionary(o => o.Key, o => o.Edge);
        var resources = new List<HitsoundResourcePlan>();
        foreach (var group in merged.PlayableObjects.Where(HitsoundCopier.Audible).GroupBy(o => origins.GetValueOrDefault(o.SourceId).Document ?? target))
        {
            var source = group.Key;
            var sourceEvents = group.Select(o => o with { SourceId = origins.TryGetValue(o.SourceId, out var origin) ? origin.Id : o.SourceId }).ToArray();
            var soundSource = RetargetOverrides(source, sourceEvents, result, compensateTinyDroplets);
            var sourceResolver = new HitsoundResolver(soundSource, sourceEvents);
            bool differentRoot = !SameResourceRoot(source, result);
            string? sourceRoot = Path.GetDirectoryName(source.SourcePath ?? source.AudioPath);
            bool copyResources = differentRoot && sourceRoot is not null && sourceEvents.SelectMany(sourceResolver.Resolve)
                .Any(s => s.FilePath is { } path && WorkspaceProject.Within(sourceRoot, path));
            foreach (var pair in group.Zip(sourceEvents))
            {
                var item = pair.First; var sample = sourceResolver.Describe(pair.Second);
                if (differentRoot && !copyResources) sample = sample with { Index = 0, FileName = "" };
                if (!copyResources && AudibleSample(item, currentResolver.Describe(item)) == AudibleSample(item, sample)) continue;
                result.HitsoundOverrides.RemoveAll(o => o.SourceId == item.SourceId && o.EventIndex == item.EventIndex);
                result.HitsoundOverrides.Add(new(item.SourceId, item.EventIndex, sample,
                    edges.GetValueOrDefault((item.SourceId, item.EventIndex), -1), InheritedSampleFields.None));
            }
            if (copyResources)
            {
                var scope = group.Select(o => o.SourceId).ToHashSet();
                string resourceNamespace = origins[group.First().SourceId].Difficulty.ToString("N");
                resources.Add(HitsoundResourcePlan.CopyCollaboration(soundSource, result, scope, resourceNamespace,
                    sourceEvents, project.Difficulties.Select(d => d.Document), compensateTinyDroplets));
            }
        }
        var output = OsuBeatmapWriter.Serialize(result, compensateTinyDroplets, outputCache);
        outputCache?.RememberMultiplier(result, output, compensateTinyDroplets);
        return new(result, HitsoundResourcePlan.Combine(resources));
    }

    private static MapDocument RetargetOverrides(MapDocument source, IReadOnlyList<ConvertedCatchObject> events, MapDocument target, bool compensation)
    {
        if (source.HitsoundOverrides.Count == 0 || source.SliderTickRate == target.SliderTickRate && source.LegacySliderTicks == target.LegacySliderTicks) return source;
        var originalEvents = OsuBeatmapWriter.Serialize(source, compensation).PlayableObjects;
        var original = originalEvents.ToDictionary(o => (o.SourceId, o.EventIndex));
        var originalEdges = originalEvents.Where(o => o.Kind == CatchObjectKind.Fruit && !o.IsStandalone).GroupBy(o => o.SourceId)
            .SelectMany(g => g.Select((o, i) => (Key: (o.SourceId, o.EventIndex), Edge: i))).ToDictionary(p => p.Key, p => p.Edge);
        var result = source.DeepClone(); result.HitsoundOverrides.Clear();
        var updates = new Dictionary<(Guid, int), (EventHitsound Sound, double Distance)>();
        var edges = events.Where(o => o.Kind == CatchObjectKind.Fruit).GroupBy(o => o.SourceId)
            .SelectMany(g => g.Select((o, i) => (Item: o, Edge: o.IsStandalone ? 0 : i))).ToArray();
        foreach (var sound in source.HitsoundOverrides)
        {
            if (!original.TryGetValue((sound.SourceId, sound.EventIndex), out var previous)) continue;
            var next = previous.Kind == CatchObjectKind.Fruit && !previous.IsStandalone
                ? edges.FirstOrDefault(e => e.Item.SourceId == sound.SourceId && e.Edge == originalEdges[(sound.SourceId, sound.EventIndex)]).Item
                : events.Where(o => o.SourceId == sound.SourceId && o.Kind == previous.Kind && Math.Abs(o.TimeMs - previous.TimeMs) <= 2)
                    .MinBy(o => Math.Abs(o.TimeMs - previous.TimeMs));
            if (next is null) continue;
            var key = (next.SourceId, next.EventIndex); double distance = Math.Abs(next.TimeMs - previous.TimeMs);
            if (!updates.TryGetValue(key, out var existing) || distance < existing.Distance)
                updates[key] = (sound with { EventIndex = next.EventIndex }, distance);
        }
        result.HitsoundOverrides.AddRange(updates.Values.Select(v => v.Sound));
        return result;
    }

    private static bool Contains(CollaborationReference r, double time) => time >= r.StartMs && time < r.EndMs;
    private static HitSampleSettings AudibleSample(ConvertedCatchObject item, HitSampleSettings sample)
    {
        if (sample.Volume == 0) return new(Volume: 0);
        if (item.Kind == CatchObjectKind.Droplet)
        {
            // Tick flags select an extra tick bank, not whistle/finish/clap layers.
            bool extraBank = sample.Additions != 0 && sample.AdditionSet != sample.NormalSet;
            return sample with { Additions = extraBank ? 2 : 0, AdditionSet = extraBank ? sample.AdditionSet : sample.NormalSet, FileName = "" };
        }
        return sample.Additions == 0 ? sample with { AdditionSet = sample.NormalSet } : sample;
    }
    private static IEnumerable<Guid> ParentIds(MapDocument d, Func<double, bool> contains)
        => d.Fruits.Where(f => contains(f.TimeMs)).Select(f => f.Id)
            .Concat(d.Tracks.Where(t => contains(t.Nodes.Min(n => n.TimeMs))).Select(t => t.Id))
            .Concat(d.ImportedSliders.Where(s => contains(s.TimeMs)).Select(s => s.Id))
            .Concat(d.BananaShowers.Where(s => contains(s.TimeMs)).Select(s => s.Id));
    private static void RemoveParents(MapDocument d, HashSet<Guid> ids)
    {
        d.Fruits.RemoveAll(f => ids.Contains(f.Id)); d.Tracks.RemoveAll(t => ids.Contains(t.Id));
        d.ImportedSliders.RemoveAll(s => ids.Contains(s.Id)); d.BananaShowers.RemoveAll(s => ids.Contains(s.Id));
        d.HitsoundOverrides.RemoveAll(s => ids.Contains(s.SourceId));
    }
    private static Guid MappedId(Guid reference, Guid difficulty, Guid id)
    {
        Span<byte> input = stackalloc byte[48]; reference.TryWriteBytes(input); difficulty.TryWriteBytes(input[16..]); id.TryWriteBytes(input[32..]);
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(input, hash); return new Guid(hash[..16]);
    }
    private static void RemapTrack(CurveTrack track, Func<Guid, Guid> map)
    {
        track.Id = map(track.Id);
        foreach (var node in track.Nodes)
        {
            node.Id = map(node.Id);
            if (node.OutgoingCurve is { } curve) foreach (var control in curve.Controls) control.Id = map(control.Id);
        }
    }
    private static bool SameResourceRoot(MapDocument a, MapDocument b)
        => string.Equals(Path.GetDirectoryName(a.SourcePath ?? a.AudioPath), Path.GetDirectoryName(b.SourcePath ?? b.AudioPath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void ExplicitBank(TimingPoint point, MapDocument source)
    {
        if (point.SampleSet is >= 1 and <= 3) return;
        point.SampleSet = SongSetup.Get(source, "General", "SampleSet").ToLowerInvariant() switch { "soft" => 2, "drum" => 3, _ => 1 };
        point.OriginalLine = null;
    }
    private static void AddBoundary(MapDocument source, MapDocument result, double time)
    {
        var state = TimingMap.At(source, time);
        var points = source.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        var current = points.LastOrDefault(p => p.TimeMs <= time) ?? points.FirstOrDefault(p => p.Uninherited) ?? points.FirstOrDefault();
        var red = current?.DeepClone() ?? new TimingPoint();
        red.TimeMs = time; red.BeatLengthMs = state.BeatLengthMs; red.Meter = state.Meter;
        red.Uninherited = true; red.SourceOrder = int.MinValue; red.OriginalLine = null; ExplicitBank(red, source);
        if (!points.Any(p => p.TimeMs == time && p.Uninherited)) result.TimingPoints.Add(red);
        var green = red.DeepClone(); green.Uninherited = false; green.SourceOrder++;
        green.BeatLengthMs = state.GenerateTicks ? -100 / state.SliderVelocityMultiplier : double.NaN;
        if (!points.Any(p => p.TimeMs == time)) result.TimingPoints.Add(green);
    }
}
