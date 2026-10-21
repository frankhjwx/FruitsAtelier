using System.Security.Cryptography;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class CollaborationMapping
{
    public sealed record ExcludedObject(Guid DifficultyId, Guid ObjectId, bool Spinner, double StartMs, double EndMs,
        double SectionStartMs, double SectionEndMs);
    public sealed record Result(MapDocument Document, HitsoundResourcePlan Resources, IReadOnlyList<ExcludedObject> ExcludedObjects);

    public static List<CollaborationReference> SectionsFromBookmarks(MapDocument document, double? durationMs = null)
    {
        double duration = durationMs ?? document.DurationMs;
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException(L.Get("collab.invalidRange"));
        double end = Math.Clamp(Math.Ceiling(duration), 1, int.MaxValue);
        var boundaries = OsuTimeline.Bookmarks(document).Where(t => t > 0 && t < end)
            .Select(t => (double)t).Prepend(0).Append(end).Distinct().Order().ToArray();
        var sections = boundaries.Zip(boundaries.Skip(1), (start, finish) =>
            new CollaborationReference(Guid.NewGuid(), Guid.Empty, start, finish)).ToList();
        new MapDocument { CollaborationReferences = sections }.ValidateCollaborationReferences();
        return sections;
    }

    public static void ValidateReferences(BeatmapProject project, Guid targetId, IReadOnlyList<CollaborationReference> references)
    {
        new MapDocument { CollaborationReferences = references.ToList() }.ValidateCollaborationReferences();
        var ids = project.Difficulties.Select(d => d.Id).ToHashSet();
        if (!ids.Contains(targetId) || references.Any(r => r.DifficultyId != Guid.Empty && !ids.Contains(r.DifficultyId)))
            throw new InvalidDataException(L.Get("collab.missing"));
        if (references.Any(r => r.DifficultyId == targetId)) throw new InvalidDataException(L.Get("collab.selfReference"));
    }

    public static Result Synchronize(BeatmapProject project, Guid targetId,
        IReadOnlyList<CollaborationReference> references, bool compensateTinyDroplets = true, OsuWriteCache? outputCache = null,
        bool synchronizeObjectHitsounds = true)
    {
        ValidateReferences(project, targetId, references);
        var byId = project.Difficulties.ToDictionary(d => d.Id);
        var target = byId[targetId].Document;
        var result = target.DeepClone();
        result.CollaborationReferences = references.Count == 0 ? null : references.ToList();
        var assigned = references.Where(r => r.DifficultyId != Guid.Empty).ToArray();
        if (assigned.Length == 0) return new(result, HitsoundResourcePlan.Combine([]), []);
        LegacySliderVelocityEditing.Capture(result);
        var targetTiming = new TimingMap.Lookup(target);
        foreach (var track in result.Tracks.Where(t => t.Nodes.Count > 0))
            track.SliderVelocitySeed ??= targetTiming.At(track.Nodes[0].TimeMs).SliderVelocityMultiplier;
        var excluded = new List<ExcludedObject>();
        var sliderLengths = new ImportedSliderLengthCache();
        RemoveParents(result, ParentIds(target, time => assigned.Any(r => Contains(r, time))).ToHashSet());
        CopySectionSamples(target, result, assigned, byId);
        var origins = new Dictionary<Guid, (MapDocument Document, Guid Id, Guid Difficulty)>();
        foreach (var reference in assigned.OrderBy(r => r.StartMs))
        {
            var source = byId[reference.DifficultyId].Document;
            var timing = new TimingMap.Lookup(source);
            var selected = ParentIds(source, time => Contains(reference, time)).ToHashSet();
            foreach (var track in source.Tracks.Where(t => t.StreamSnapDivisor is null))
                CheckBounds(track.Id, track.Nodes[0].TimeMs, CurveMath.EndTimeMs(track), false);
            foreach (var slider in source.ImportedSliders.Where(s => s.TimeMs < reference.EndMs))
                CheckBounds(slider.Id, slider.TimeMs, sliderLengths.EndTimeMs(source, slider, timing), false);
            foreach (var shower in source.BananaShowers)
                CheckBounds(shower.Id, shower.TimeMs, shower.EndTimeMs, true);
            void CheckBounds(Guid id, double start, double end, bool spinner)
            {
                if (start >= reference.EndMs || end <= reference.StartMs || start >= reference.StartMs && end <= reference.EndMs) return;
                selected.Remove(id);
                excluded.Add(new(reference.DifficultyId, id, spinner, start, end, reference.StartMs, reference.EndMs));
            }
            Guid Remap(Guid id) => MappedId(reference.Id, reference.DifficultyId, id);
            foreach (var id in selected) origins.Add(Remap(id), (source, id, reference.DifficultyId));
            foreach (var fruit in source.Fruits.Where(f => selected.Contains(f.Id)))
            {
                var copy = fruit.DeepClone(); copy.Id = Remap(copy.Id);
                if (!synchronizeObjectHitsounds) copy.OriginalLine = WithoutObjectSamples(copy.OriginalLine);
                result.Fruits.Add(copy);
            }
            foreach (var track in source.Tracks.Where(t => selected.Contains(t.Id)))
            {
                var copy = track.DeepClone(); RemapTrack(copy, Remap);
                copy.SliderVelocitySeed ??= timing.At(track.Nodes[0].TimeMs).SliderVelocityMultiplier;
                if (!synchronizeObjectHitsounds) copy.OriginalLine = WithoutObjectSamples(copy.OriginalLine);
                copy.DropletMode = track.RandomizesDroplets(source) ? SliderDropletMode.Randomize : SliderDropletMode.Derandomize;
                result.Tracks.Add(copy);
            }
            foreach (var slider in source.ImportedSliders.Where(s => selected.Contains(s.Id)))
            {
                var copy = slider.DeepClone(); copy.Id = Remap(copy.Id);
                if (copy.RandomizationSource is { } baseline) baseline.Id = copy.Id;
                if (copy.RandomizationTrack is { } track) RemapTrack(track, Remap);
                if (!synchronizeObjectHitsounds)
                {
                    copy.OriginalLine = WithoutObjectSamples(copy.OriginalLine);
                    if (copy.RandomizationSource is { } sampleSource) sampleSource.OriginalLine = WithoutObjectSamples(sampleSource.OriginalLine);
                    if (copy.RandomizationTrack is { } sampleTrack) sampleTrack.OriginalLine = WithoutObjectSamples(sampleTrack.OriginalLine);
                }
                RetargetSlider(copy);
                result.ImportedSliders.Add(copy);

                void RetargetSlider(ImportedSlider slider)
                {
                    var sourceState = timing.At(slider.TimeMs);
                    double scale = source.SliderMultiplier * targetTiming.At(slider.TimeMs).BeatLengthMs
                        / (target.SliderMultiplier * sourceState.BeatLengthMs);
                    slider.SliderVelocityMultiplier = (slider.SliderVelocityMultiplier ?? sourceState.SliderVelocityMultiplier
                        * source.EffectiveSliderMultiplier / source.SliderMultiplier) * scale;
                    if (slider.ProcessedSliderVelocityMultiplier is { } processed) slider.ProcessedSliderVelocityMultiplier = processed * scale;
                    if (slider.RandomizationBaseVelocity is { } velocity) slider.RandomizationBaseVelocity = velocity * scale;
                    if (slider.RandomizationSource is { } original) RetargetSlider(original);
                    double effective = (slider.ProcessedSliderVelocityMultiplier ?? slider.SliderVelocityMultiplier.Value)
                        * target.SliderMultiplier / target.EffectiveSliderMultiplier;
                    if (!double.IsFinite(effective) || effective is < .1 or > 10
                        || !double.IsFinite(slider.SliderVelocityMultiplier.Value) || slider.SliderVelocityMultiplier is < .1 or > 10)
                        throw new InvalidDataException(L.Get("timing.sliderMultiplierSvLimit"));
                }
            }
            foreach (var shower in source.BananaShowers.Where(s => selected.Contains(s.Id)))
            {
                var copy = shower.DeepClone(); copy.Id = Remap(copy.Id);
                if (!synchronizeObjectHitsounds) copy.OriginalLine = WithoutObjectSamples(copy.OriginalLine);
                result.BananaShowers.Add(copy);
            }
            if (synchronizeObjectHitsounds && source.SliderTickRate == target.SliderTickRate
                && source.LegacySliderTicks == target.LegacySliderTicks && HitsoundCopier.TimingMatches(source, target))
                foreach (var sound in source.HitsoundOverrides.Where(s => selected.Contains(s.SourceId)))
                    result.HitsoundOverrides.Add(sound with { SourceId = Remap(sound.SourceId) });
            result.DurationMs = Math.Max(result.DurationMs, source.DurationMs);
        }
        var ordered = result.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        result.TimingPoints.Clear(); result.TimingPoints.AddRange(ordered);
        var merged = OsuBeatmapWriter.Serialize(result, compensateTinyDroplets, outputCache);
        if (!synchronizeObjectHitsounds)
        {
            PreserveTargetHitsounds(target, result, merged.PlayableObjects, origins.Keys.ToHashSet(), compensateTinyDroplets);
            var preserved = OsuBeatmapWriter.Serialize(result, compensateTinyDroplets, outputCache);
            outputCache?.RememberMultiplier(result, preserved, compensateTinyDroplets);
            return new(result, HitsoundResourcePlan.Combine([]), excluded);
        }
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
        return new(result, HitsoundResourcePlan.Combine(resources), excluded);
    }

    private static string? WithoutObjectSamples(string? line)
    {
        if (line is null) return null;
        var fields = line.Split(',');
        if (fields.Length < 5 || !int.TryParse(fields[3], out int type)) return line;
        fields[4] = "0";
        if ((type & 2) != 0)
        {
            Array.Resize(ref fields, Math.Max(11, fields.Length));
            fields[8] = ""; fields[9] = ""; fields[10] = "0:0:0:0:";
        }
        else
        {
            int index = (type & 8) != 0 ? 6 : 5;
            Array.Resize(ref fields, Math.Max(index + 1, fields.Length)); fields[index] = "0:0:0:0:";
        }
        return string.Join(',', fields);
    }

    private static void PreserveTargetHitsounds(MapDocument target, MapDocument result, IReadOnlyList<ConvertedCatchObject> events,
        HashSet<Guid> copiedParents, bool compensation)
    {
        var previous = OsuBeatmapWriter.Serialize(target, compensation).PlayableObjects;
        var resolver = new HitsoundResolver(target, previous);
        var current = new HitsoundResolver(result, events);
        var byPosition = previous.Where(HitsoundCopier.Audible).GroupBy(o => (o.TimeMs, o.Kind)).ToDictionary(g => g.Key, g => g.ToArray());
        var byIdentity = previous.ToDictionary(o => (o.SourceId, o.EventIndex));
        var updates = result.HitsoundOverrides.ToDictionary(o => (o.SourceId, o.EventIndex));
        var edges = events.Where(o => o.Kind == CatchObjectKind.Fruit).GroupBy(o => o.SourceId)
            .SelectMany(g => g.Select((o, i) => (Key: (o.SourceId, o.EventIndex), Edge: o.IsStandalone ? 0 : i)))
            .ToDictionary(o => o.Key, o => o.Edge);
        foreach (var item in events.Where(HitsoundCopier.Audible))
        {
            bool copied = copiedParents.Contains(item.SourceId);
            ConvertedCatchObject? original = copied
                ? byPosition.TryGetValue((item.TimeMs, item.Kind), out var matches) ? matches.MinBy(o => Math.Abs(o.X - item.X)) : null
                : byIdentity.GetValueOrDefault((item.SourceId, item.EventIndex));
            // An unknown parent has no object sample fields, so it resolves the merged timeline's defaults.
            var defaults = current.Describe(item with { SourceId = Guid.Empty });
            var sample = original is null ? defaults : resolver.Describe(original);
            var inherited = original is null ? InheritedSampleFields.All : resolver.InheritedFields(original);
            if (sample.NormalSet != defaults.NormalSet) inherited &= ~InheritedSampleFields.NormalSet;
            if (sample.AdditionSet != defaults.AdditionSet) inherited &= ~InheritedSampleFields.AdditionSet;
            if (sample.Index != defaults.Index) inherited &= ~InheritedSampleFields.Index;
            if (sample.Volume != defaults.Volume) inherited &= ~InheritedSampleFields.Volume;
            if (!copied && AudibleSample(item, sample) == AudibleSample(item, current.Describe(item))) continue;
            updates[(item.SourceId, item.EventIndex)] = new(item.SourceId, item.EventIndex, sample,
                edges.GetValueOrDefault((item.SourceId, item.EventIndex), -1), inherited);
        }
        result.HitsoundOverrides.Clear(); result.HitsoundOverrides.AddRange(updates.Values);
    }

    private static MapDocument RetargetOverrides(MapDocument source, IReadOnlyList<ConvertedCatchObject> events, MapDocument target, bool compensation)
    {
        if (source.HitsoundOverrides.Count == 0 || source.SliderTickRate == target.SliderTickRate
            && source.LegacySliderTicks == target.LegacySliderTicks && HitsoundCopier.TimingMatches(source, target)) return source;
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

    private static void CopySectionSamples(MapDocument target, MapDocument result, CollaborationReference[] references,
        Dictionary<Guid, ProjectDifficulty> difficulties)
    {
        var points = references.Select(r => r.DifficultyId).Distinct()
            .ToDictionary(id => id, id => difficulties[id].Document.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray());
        var targetPoints = target.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        var sourceTimes = points.ToDictionary(p => p.Key, p => p.Value.Select(point => point.TimeMs).ToArray());
        var targetTimes = targetPoints.Select(p => p.TimeMs).ToArray();
        var times = targetPoints.Select(p => p.TimeMs).Concat(references.SelectMany(r => new[] { r.StartMs, r.EndMs }))
            .Concat(references.SelectMany(r => points[r.DifficultyId].Where(p => Contains(r, p.TimeMs)).Select(p => p.TimeMs)))
            .Distinct().Order();
        var targetGroups = targetPoints.GroupBy(p => p.TimeMs).ToDictionary(g => g.Key, g => g.ToArray());
        var timing = new TimingMap.Lookup(target);
        result.TimingPoints.Clear();
        TimingPoint? previous = targetPoints.FirstOrDefault(p => p.Uninherited) ?? targetPoints.FirstOrDefault();
        foreach (double time in times)
        {
            var reference = references.FirstOrDefault(r => Contains(r, time));
            var source = reference is null ? target : difficulties[reference.DifficultyId].Document;
            var sourcePoints = reference is null ? targetPoints : points[reference.DifficultyId];
            int index = Array.BinarySearch(reference is null ? targetTimes : sourceTimes[reference.DifficultyId], time);
            if (index < 0) index = ~index - 1;
            else while (index + 1 < sourcePoints.Length && sourcePoints[index + 1].TimeMs == time) index++;
            var sample = (index >= 0 ? sourcePoints[index] : sourcePoints.FirstOrDefault(p => p.Uninherited))?.DeepClone() ?? new TimingPoint();
            ExplicitBank(sample, source);
            var retained = targetGroups.GetValueOrDefault(time) ?? [];
            foreach (var original in retained.Where(p => reference is null || p.Uninherited))
            {
                var copy = original.DeepClone();
                if (reference is not null && (copy.SampleSet != sample.SampleSet || copy.SampleIndex != sample.SampleIndex
                    || copy.Volume != sample.Volume || copy.Effects != sample.Effects))
                {
                    copy.SampleSet = sample.SampleSet; copy.SampleIndex = sample.SampleIndex;
                    copy.Volume = sample.Volume; copy.Effects = sample.Effects; copy.OriginalLine = null;
                }
                result.TimingPoints.Add(copy); previous = copy;
            }
            var effective = previous?.DeepClone() ?? new TimingPoint(); ExplicitBank(effective, target);
            if (effective.SampleSet == sample.SampleSet && effective.SampleIndex == sample.SampleIndex
                && effective.Volume == sample.Volume && effective.Effects == sample.Effects) continue;
            var state = timing.At(time);
            sample.TimeMs = time; sample.Uninherited = false; sample.Meter = state.Meter;
            // Section sample transitions do not carry source SV; sliders own their exported velocity.
            sample.BeatLengthMs = reference is null && !state.GenerateTicks ? double.NaN
                : reference is null ? -100 / state.SliderVelocityMultiplier : -100;
            sample.SourceOrder = int.MaxValue; sample.OriginalLine = null;
            result.TimingPoints.Add(sample); previous = sample;
        }
    }
}
