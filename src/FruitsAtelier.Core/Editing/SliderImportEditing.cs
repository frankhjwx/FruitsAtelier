using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public enum SliderImportState { None, Pending, Native, Compensated }

public static class SliderImportEditing
{
    public static bool LooksCompensated(MapDocument document)
        => CompensatedSliderRatio(document) > .8;

    public static double CompensatedSliderRatio(MapDocument document)
    {
        if (document.ImportedSliders.Count == 0) return 0;
        var timing = new TimingMap.Lookup(document);
        int matching = document.ImportedSliders.Count(slider => HasDropletAnchors(document, slider, timing));
        return matching / (double)document.ImportedSliders.Count;
    }

    private static bool HasDropletAnchors(MapDocument document, ImportedSlider slider, TimingMap.Lookup timing)
    {
        if (slider.ControlPoints.Count < 2 || !HasLinearSegments(slider)) return false;
        double length = slider.GetGeometry().Distance;
        if (length <= 0) return false;
        double velocity = ImportedSliderConverter.VelocityAt(document, slider, timing);
        double tickDistance = LegacyCatchRules.TickDistance(document, velocity, timing.At(slider.TimeMs).BeatLengthMs,
            ImportedSliderConverter.SliderVelocityAt(document, slider, timing));
        var events = LegacyCatchRules.CreateNested(slider.TimeMs, length / velocity,
            velocity, tickDistance, length, slider.SpanCount, legacyImported: true);
        var anchors = new List<double> { 0 };
        double distance = 0;
        for (int i = 1; i < slider.ControlPoints.Count; i++)
        {
            var a = slider.ControlPoints[i - 1]; var b = slider.ControlPoints[i];
            distance += new System.Numerics.Vector2((float)(b.X - a.X), (float)(b.GeometryY - a.GeometryY)).Length();
            if (distance > length) break;
            if (distance > anchors[^1]) anchors.Add(distance);
        }
        bool hasDroplet = false;
        foreach (var item in events)
        {
            if (item.Kind == CatchObjectKind.Fruit) continue;
            hasDroplet = true;
            // Progress folds every traversal onto the same path, including legacy tail timing.
            double target = item.Progress * length;
            int index = anchors.BinarySearch(target);
            if (index >= 0) continue;
            index = ~index;
            double error = Math.Min(index < anchors.Count ? anchors[index] - target : double.PositiveInfinity,
                index > 0 ? target - anchors[index - 1] : double.PositiveInfinity);
            // Exported integer coordinates can shift the cumulative distance slightly.
            if (error > 1.5) return false;
        }
        return hasDroplet;
    }

    private static bool HasLinearSegments(ImportedSlider slider)
    {
        if (slider.PathType == 'L') return true;
        var points = slider.ControlPoints;
        if (slider.PathType != 'B' || points.Count < 2 || points.Count % 2 != 0) return false;
        // The exporter encodes straight Bezier segments by duplicating each internal anchor.
        for (int i = 1; i < points.Count - 1; i += 2)
            if (points[i] != points[i + 1]) return false;
        return true;
    }

    public static SliderRepresentationResult Initialize(MapDocument document, bool compensated, CancellationToken cancellation = default)
    {
        var candidate = document.DeepClone();
        if (compensated)
        {
            candidate.DerandomizeFSliderDroplets = true;
            candidate.RandomizeNewSliders = false;
            var source = candidate.DeepClone();
            var result = ImportedSliderEditing.ConvertPreservingPositions(candidate,
                candidate.ImportedSliders.Select(s => s.Id).ToArray(), cancellation, tolerance: 1);
            var tracks = result.Tracks.ToDictionary(t => t.Id);
            ImportedSliderEditing.FitPlayback(source, tracks, cancellation);
            var timing = new TimingMap.Lookup(source);
            candidate.Tracks.RemoveAll(t => tracks.ContainsKey(t.Id));
            // Source confirmation also covers mixed maps; unrecoverable parents retain their original geometry.
            candidate.ImportedSliders.Clear();
            candidate.ImportedSliders.AddRange(source.ImportedSliders);
            foreach (var slider in candidate.ImportedSliders)
            {
                if (!tracks.TryGetValue(slider.Id, out var track)) continue;
                slider.RandomizationSource = slider.DeepClone();
                slider.RandomizationTrack = track;
                slider.RandomizationTrack.DropletMode = SliderDropletMode.FollowMap;
                slider.RandomizationBaseVelocity = ImportedSliderConverter.SliderVelocityAt(source, slider, timing);
            }
        }
        else
        {
            var result = SliderRepresentationEditing.RefreshLegacyDroplets(candidate);
            if (!result.Success) return result;
        }
        cancellation.ThrowIfCancellationRequested();
        candidate.SliderImportState = compensated ? SliderImportState.Compensated : SliderImportState.Native;
        candidate.DerandomizeDroplets = candidate.DerandomizeFSliderDroplets;
        document.DerandomizeFSliderDroplets = candidate.DerandomizeFSliderDroplets;
        document.RandomizeNewSliders = candidate.RandomizeNewSliders;
        document.DerandomizeDroplets = candidate.DerandomizeDroplets;
        document.SliderImportState = candidate.SliderImportState;
        document.ImportedSliders.Clear(); document.ImportedSliders.AddRange(candidate.ImportedSliders);
        document.TimingPoints.Clear(); document.TimingPoints.AddRange(candidate.TimingPoints);
        return new(true, []);
    }

    public static SliderBatchConversionResult ConvertFollowingMap(MapDocument document, IReadOnlyCollection<Guid> ids,
        CancellationToken cancellation = default)
    {
        var wanted = ids.ToHashSet();
        var candidate = document.DeepClone();
        var sources = candidate.ImportedSliders.Where(s => wanted.Contains(s.Id)).ToArray();
        var tracks = new List<CurveTrack>();
        foreach (var slider in sources.Where(s => s.RandomizationTrack is not null))
        {
            cancellation.ThrowIfCancellationRequested();
            var track = slider.RandomizationTrack!.DeepClone();
            TimingEditing.TransformTrack(track, track.Nodes[0].TimeMs, slider.TimeMs, 1);
            track.OriginalLine = slider.OriginalLine;
            track.SourceOrder = slider.SourceOrder;
            track.EndpointSnap = slider.EndpointSnap;
            track.DropletMode ??= SliderDropletMode.FollowMap;
            tracks.Add(track);
        }
        candidate.ImportedSliders.RemoveAll(s => tracks.Any(t => t.Id == s.Id));
        candidate.Tracks.AddRange(tracks);
        var fitted = ImportedSliderEditing.Convert(candidate,
            sources.Where(s => s.RandomizationTrack is null).Select(s => s.Id).ToArray(), cancellation, fitPlayback: false);
        foreach (var track in fitted.Tracks) track.DropletMode = SliderDropletMode.FollowMap;
        tracks.AddRange(fitted.Tracks);
        cancellation.ThrowIfCancellationRequested();
        var output = OsuBeatmapWriter.Serialize(candidate);
        if (!output.ObjectSequenceMatches) throw new InvalidOperationException(L.Get("core.importEditing.preservePositionsFailed"));
        var failures = fitted.Failures.ToList();
        if (document.SliderImportState == SliderImportState.Compensated)
        {
            var original = OsuBeatmapWriter.Serialize(document);
            var beforeNm = original.PlayableObjects.GroupBy(o => o.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
            var beforeHr = original.PlayableHardRockObjects.GroupBy(o => o.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
            var cache = new OsuWriteCache();
            while (tracks.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var afterNm = output.PlayableObjects.GroupBy(o => o.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
                var afterHr = output.PlayableHardRockObjects.GroupBy(o => o.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
                var rejected = tracks.Where(t => !Matches(beforeNm, afterNm, t.Id) || !Matches(beforeHr, afterHr, t.Id)).ToArray();
                if (rejected.Length == 0)
                {
                    // A changed parent may also affect later HR offsets. Validate the complete map.
                    if (beforeNm.Count == afterNm.Count && beforeHr.Count == afterHr.Count
                        && beforeNm.Keys.All(id => Matches(beforeNm, afterNm, id) && Matches(beforeHr, afterHr, id))) break;
                    rejected = tracks.ToArray();
                }
                foreach (var track in rejected)
                {
                    tracks.Remove(track);
                    failures.Add(new(track.Id, track.Nodes[0].TimeMs, L.Get("core.importEditing.preservePositionsFailed")));
                }
                candidate = document.DeepClone();
                var remaining = tracks.Select(t => t.Id).ToHashSet();
                candidate.ImportedSliders.RemoveAll(s => remaining.Contains(s.Id));
                candidate.Tracks.AddRange(tracks);
                output = OsuBeatmapWriter.Serialize(candidate, cache: cache);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        var accepted = tracks.Select(t => t.Id).ToHashSet();
        document.ImportedSliders.RemoveAll(s => accepted.Contains(s.Id));
        document.Tracks.AddRange(tracks);
        return new(tracks, failures);
    }

    private static bool Matches(Dictionary<Guid, ConvertedCatchObject[]> expected, Dictionary<Guid, ConvertedCatchObject[]> actual, Guid id)
        => expected.TryGetValue(id, out var before) && actual.TryGetValue(id, out var after)
            && before.Length == after.Length && before.Zip(after).All(p => p.First.Kind == p.Second.Kind
                && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= 1e-6
                && Math.Abs(p.First.X - p.Second.X) <= ImportedSliderEditing.ExportPositionTolerance);
}
