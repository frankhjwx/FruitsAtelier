using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed record ImportedSliderEditResult(CurveTrack Track, IReadOnlyList<string> Diagnostics);
public sealed record SliderConversionFailure(Guid Id, double TimeMs, string Reason);
public sealed record SliderBatchConversionResult(IReadOnlyList<CurveTrack> Tracks, IReadOnlyList<SliderConversionFailure> Failures);

public static class ImportedSliderEditing
{
    public const double ApproximationTolerance = ImportedCurveFitter.Tolerance;
    private const double PlaybackPositionTolerance = 1;

    public static ImportedSliderEditResult ConvertToTrack(MapDocument document, Guid sliderId, CatchConversionCache? cache = null, bool derandomizeDroplets = true)
    {
        if (!document.ImportedSliders.Any(s => s.Id == sliderId))
            throw new ArgumentException(L.Get("core.importEditing.notFound"), nameof(sliderId));
        var result = document.SliderImportState != SliderImportState.None || document.ImportedSliders.Any(s => s.Id == sliderId && s.RandomizationTrack is not null)
            ? SliderImportEditing.ConvertFollowingMap(document, [sliderId])
            : derandomizeDroplets ? Convert(document, [sliderId], cache: cache)
            : ConvertPreservingPositions(document, [sliderId], default, cache);
        if (result.Tracks.Count == 0) throw new InvalidOperationException(result.Failures[0].Reason);
        return new(result.Tracks[0], []);
    }

    public static SliderBatchConversionResult ConvertAll(MapDocument document, CancellationToken cancellation = default, bool derandomizeDroplets = true)
        => document.SliderImportState != SliderImportState.None || document.ImportedSliders.Any(s => s.RandomizationTrack is not null)
            ? SliderImportEditing.ConvertFollowingMap(document, document.ImportedSliders.Select(s => s.Id).ToArray(), cancellation)
            : derandomizeDroplets ? Convert(document, document.ImportedSliders.Select(s => s.Id).ToArray(), cancellation)
            : ConvertPreservingPositions(document, document.ImportedSliders.Select(s => s.Id).ToArray(), cancellation);

    internal static SliderBatchConversionResult ConvertPreservingPositions(MapDocument document, IReadOnlyCollection<Guid> ids,
        CancellationToken cancellation, CatchConversionCache? cache = null, double tolerance = CatchStreamConverter.AlignmentTolerance)
    {
        cancellation.ThrowIfCancellationRequested();
        var wanted = ids.ToHashSet();
        // Capture the full-map RNG once, before replacing any parents.
        var original = CatchStreamConverter.Convert(document, cache: cache).Objects
            .Where(o => wanted.Contains(o.SourceId)).GroupBy(o => o.SourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.EventIndex).ToArray());
        var candidates = new List<CurveTrack>();
        var failures = new List<SliderConversionFailure>();
        foreach (var source in document.ImportedSliders.Where(s => wanted.Contains(s.Id)))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                double duration = ImportedSliderConverter.DurationMs(document, source) / source.SpanCount;
                if (!double.IsFinite(duration) || duration < CurveMath.MinimumAnchorSpacingMs
                    || !original.TryGetValue(source.Id, out var objects))
                    throw new InvalidOperationException(L.Get("core.importEditing.invalidPath"));
                var track = new CurveTrack
                {
                    Id = source.Id, EndpointSnap = source.EndpointSnap, SourceOrder = source.SourceOrder, OriginalLine = source.OriginalLine,
                    Name = L.Get("core.names.importedSlider", source.PathType, source.TimeMs),
                    SpanCount = source.SpanCount, Kind = CurveKind.Linear, CompensateTinyDroplets = true
                };
                track.Nodes.Add(new Anchor { TimeMs = source.TimeMs, X = objects[0].X });
                track.Nodes.Add(new Anchor { TimeMs = source.TimeMs + duration, X = objects.First(o => o.Kind == CatchObjectKind.Fruit && o.EventIndex > 0).X });
                var points = objects.Select(o => new MapPoint(CurveMath.FirstSpanTime(track, o.TimeMs), o.X))
                    .OrderBy(p => p.TimeMs).ToArray();
                if (tolerance > CatchStreamConverter.AlignmentTolerance)
                    points = points.GroupBy(p => Math.Round(p.TimeMs, 6))
                        .Select(g => new MapPoint(g.First().TimeMs, (g.Min(p => p.X) + g.Max(p => p.X)) / 2)).ToArray();
                track.Nodes.Clear();
                foreach (var point in points)
                {
                    if (track.Nodes.Count > 0 && point.TimeMs - track.Nodes[^1].TimeMs < CurveMath.MinimumAnchorSpacingMs)
                    {
                        if (Math.Abs(point.X - track.Nodes[^1].X) > tolerance)
                            throw new InvalidOperationException(L.Get("core.conversion.tinyConflict"));
                        continue;
                    }
                    track.Nodes.Add(new Anchor { TimeMs = point.TimeMs, X = point.X, OutgoingKind = CurveKind.Linear });
                }
                candidates.Add(track);
            }
            catch (Exception error) when (error is CatchConversionException or InvalidOperationException or ArgumentException)
            { failures.Add(new(source.Id, source.TimeMs, error.Message)); }
        }
        var candidate = document.DeepClone();
        cache ??= new CatchConversionCache();
        var recoveryAttempts = new Dictionary<Guid, int>();
        while (candidates.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var convertedIds = candidates.Select(t => t.Id).ToHashSet();
            candidate.ImportedSliders.Clear();
            candidate.ImportedSliders.AddRange(document.ImportedSliders.Where(s => !convertedIds.Contains(s.Id)));
            candidate.Tracks.RemoveAll(t => wanted.Contains(t.Id)); candidate.Tracks.AddRange(candidates);
            var generated = CatchStreamConverter.Convert(candidate, cache: cache);
            var actual = generated.Objects.Where(o => convertedIds.Contains(o.SourceId)).GroupBy(o => o.SourceId)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.EventIndex).ToArray());
            bool removed = false;
            foreach (var track in candidates.ToArray())
            {
                var expected = original[track.Id];
                if (actual.TryGetValue(track.Id, out var items) && items.Length == expected.Length
                    && items.Zip(expected).All(pair => pair.First.Kind == pair.Second.Kind
                        && Math.Abs(pair.First.TimeMs - pair.Second.TimeMs) <= 1e-6
                        && Math.Abs(pair.First.X - pair.Second.X) <= tolerance
                        && (tolerance > CatchStreamConverter.AlignmentTolerance
                            || Math.Abs(pair.First.X - pair.First.TargetX) <= tolerance))) continue;
                if (tolerance > CatchStreamConverter.AlignmentTolerance && items is not null && items.Length == expected.Length
                    && items.Zip(expected).All(p => p.First.Kind == p.Second.Kind && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= 1e-6)
                    && recoveryAttempts.GetValueOrDefault(track.Id) < 8)
                {
                    // Shared repeat geometry may retain residual offsets. Recover against playback,
                    // correcting the shared target instead of treating each traversal as another effect.
                    var corrections = items.Zip(expected).GroupBy(p => Math.Round(CurveMath.FirstSpanTime(track, p.First.TimeMs), 6))
                        .ToDictionary(g => g.Key, g => g.Average(p => p.Second.X - p.First.X));
                    foreach (var node in track.Nodes)
                        node.X = Math.Clamp(node.X + corrections.GetValueOrDefault(Math.Round(node.TimeMs, 6)), 0, 512);
                    recoveryAttempts[track.Id] = recoveryAttempts.GetValueOrDefault(track.Id) + 1;
                    removed = true;
                    continue;
                }
                candidates.Remove(track); removed = true;
                failures.Add(new(track.Id, track.Nodes[0].TimeMs, L.Get("core.importEditing.preservePositionsFailed")));
            }
            if (!removed) break;
        }
        cancellation.ThrowIfCancellationRequested();
        var accepted = candidates.Select(t => t.Id).ToHashSet();
        document.ImportedSliders.RemoveAll(s => accepted.Contains(s.Id));
        document.Tracks.AddRange(candidates);
        return new(candidates, failures);
    }

    internal static SliderBatchConversionResult Convert(MapDocument document, IReadOnlyCollection<Guid> ids, CancellationToken cancellation = default, CatchConversionCache? cache = null,
        bool fitPlayback = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var wanted = ids.ToHashSet();
        var sources = document.ImportedSliders.Where(s => wanted.Contains(s.Id)).ToArray();
        var failures = new List<SliderConversionFailure>();
        var candidates = new Dictionary<Guid, CurveTrack>();
        var paths = new Dictionary<Guid, (ImportedSliderGeometry Path, double Velocity)>();
        var fallbackLevel = new Dictionary<Guid, int>();
        foreach (var slider in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var path = slider.GetGeometry();
                var timing = TimingMap.At(document, slider.TimeMs);
                double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.EffectiveSliderMultiplier, ImportedSliderConverter.SliderVelocityAt(document, slider));
                if (!double.IsFinite(velocity) || velocity <= 0 || path.Distance <= 0)
                    throw new InvalidOperationException(L.Get("core.importEditing.invalidPath"));
                if (path.Distance / velocity < .001) throw new InvalidOperationException(L.Get("core.importEditing.zeroDuration"));
                var track = new CurveTrack
                {
                    Id = slider.Id, EndpointSnap = slider.EndpointSnap, SourceOrder = slider.SourceOrder, OriginalLine = slider.OriginalLine,
                    Name = L.Get("core.names.importedSlider", slider.PathType, slider.TimeMs),
                    SpanCount = slider.SpanCount, CompensateTinyDroplets = true
                };
                paths.Add(slider.Id, (path, velocity));
                fallbackLevel[slider.Id] = 0;
                try { ImportedCurveFitter.Fit(track, path.TimeXPoints(slider, velocity), velocity); }
                catch (InvalidOperationException)
                {
                    Approximate(track, slider, path, velocity);
                    fallbackLevel[slider.Id] = 2;
                }
                candidates.Add(slider.Id, track);
            }
            catch (Exception error) when (error is CatchConversionException or InvalidOperationException or ArgumentException)
            { failures.Add(new(slider.Id, slider.TimeMs, error.Message)); }
        }
        if (candidates.Count == 0) return new([], failures);
        cancellation.ThrowIfCancellationRequested();
        var candidate = document.DeepClone();
        cache ??= new CatchConversionCache();
        while (candidates.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            candidate.ImportedSliders.Clear();
            candidate.ImportedSliders.AddRange(document.ImportedSliders.Where(s => !candidates.ContainsKey(s.Id)));
            candidate.Tracks.RemoveAll(t => wanted.Contains(t.Id)); candidate.Tracks.AddRange(candidates.Values);
            var generated = CatchStreamConverter.Convert(candidate, cache: cache);
            var sliders = generated.Sliders.ToDictionary(s => s.SourceId);
            bool changed = false;
            foreach (var (id, track) in candidates.ToArray())
            {
                var source = sources.First(s => s.Id == id);
                var (path, velocity) = paths[id];
                double duration = path.Distance / velocity * source.SpanCount;
                if (sliders.TryGetValue(id, out var result)
                    && result.StartTimeMs == source.TimeMs && result.SpanCount == source.SpanCount
                    && Math.Abs(result.DurationMs - duration) <= 0.000001)
                {
                    // Persist the actual fallback policy for a newly imported track.
                    // Generation of existing tracks leaves their saved preference intact.
                    if (!result.TinyCompensationApplied) track.CompensateTinyDroplets = false;
                    continue;
                }
                changed = true;
                if (fallbackLevel[id] == 0)
                {
                    // Keep the fitted shape; legacy random tiny offsets need not match the target.
                    track.CompensateTinyDroplets = false; fallbackLevel[id] = 1;
                }
                else if (fallbackLevel[id] == 1)
                {
                    // Dense or speed-limited curves use a bounded linear approximation at the same times.
                    Approximate(track, source, path, velocity); fallbackLevel[id] = 2;
                }
                else
                {
                    candidates.Remove(id);
                    failures.Add(new(id, source.TimeMs, L.Get("core.importEditing.invalidPath")));
                }
            }
            if (!changed) break;
        }
        cancellation.ThrowIfCancellationRequested();
        if (fitPlayback) FitPlayback(document, candidates, cancellation);
        // Publish duration-preserving replacements atomically; invalid sources remain available to repair.
        document.ImportedSliders.RemoveAll(s => candidates.ContainsKey(s.Id));
        document.Tracks.AddRange(candidates.Values);
        return new(candidates.Values.ToArray(), failures);
    }

    internal static void FitPlayback(MapDocument document, Dictionary<Guid, CurveTrack> candidates, CancellationToken cancellation)
    {
        if (candidates.Count == 0) return;
        // Exported compensation bends the osu! path away from the visible tiny droplets.
        // Prefer a simpler curve fitted to playback only when every event still agrees.
        var original = CatchStreamConverter.Convert(document).Objects
            .Where(o => candidates.ContainsKey(o.SourceId)).GroupBy(o => o.SourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.EventIndex).ToArray());
        var sources = document.ImportedSliders.ToDictionary(s => s.Id);
        var fitted = new Dictionary<Guid, CurveTrack>();
        foreach (var (id, track) in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!original.TryGetValue(id, out var events)) continue;
            var points = events.Select(o => new MapPoint(CurveMath.FirstSpanTime(track, o.TimeMs), o.X))
                .GroupBy(p => p.TimeMs).OrderBy(g => g.Key)
                .Select(g => new MapPoint(g.Key, g.Average(p => p.X))).ToArray();
            if (points.Length < 2) continue;
            var smooth = track.DeepClone(); smooth.Nodes.Clear();
            try
            {
                double velocity = ImportedSliderConverter.VelocityAt(document, sources[id]);
                var start = points[0]; var end = points[^1];
                if (points.All(p => Math.Abs(p.X - (start.X + (end.X - start.X)
                    * (p.TimeMs - start.TimeMs) / (end.TimeMs - start.TimeMs))) <= PlaybackPositionTolerance))
                {
                    smooth.Kind = CurveKind.Linear;
                    smooth.Nodes.Add(new Anchor { TimeMs = start.TimeMs, X = start.X, OutgoingKind = CurveKind.Linear });
                    smooth.Nodes.Add(new Anchor { TimeMs = end.TimeMs, X = end.X, OutgoingKind = CurveKind.Linear });
                }
                else ImportedCurveFitter.Fit(smooth, points, velocity, tolerance: .5);
            }
            catch (InvalidOperationException) { continue; }
            if (smooth.Nodes.Count >= track.Nodes.Count) continue;
            smooth.DropletMode = SliderDropletMode.Derandomize;
            fitted[id] = smooth;
        }
        if (fitted.Count == 0) return;
        var candidate = document.DeepClone();
        candidate.ImportedSliders.RemoveAll(s => candidates.ContainsKey(s.Id));
        candidate.Tracks.AddRange(candidates.Select(p => fitted.GetValueOrDefault(p.Key, p.Value)));
        var actual = CatchStreamConverter.Convert(candidate).Objects
            .Where(o => fitted.ContainsKey(o.SourceId)).GroupBy(o => o.SourceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.EventIndex).ToArray());
        foreach (var (id, track) in fitted)
        {
            cancellation.ThrowIfCancellationRequested();
            if (actual.TryGetValue(id, out var events) && events.Length == original[id].Length
                && events.Zip(original[id]).All(p => p.First.Kind == p.Second.Kind
                    && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= 1e-6
                    && Math.Abs(p.First.X - p.Second.X) <= PlaybackPositionTolerance)) candidates[id] = track;
        }
    }

    private static void Approximate(CurveTrack track, ImportedSlider source, ImportedSliderGeometry path, double velocity)
    {
        double duration = path.Distance / velocity;
        if (!double.IsFinite(duration) || duration < .001)
            throw new InvalidOperationException(L.Get("core.importEditing.zeroDuration"));
        track.Nodes.Clear(); track.Kind = CurveKind.Linear; track.CompensateTinyDroplets = false;
        int intervals = (int)Math.Clamp(Math.Floor(duration / .001), 1, Math.Min(2048, Math.Max(1, path.TimeXPoints(source, velocity).Count - 1)));
        double previousX = Math.Clamp((float)source.X + path.PositionAt(0).X, 0, 512);
        double step = duration / intervals;
        for (int i = 0; i <= intervals; i++)
        {
            double time = source.TimeMs + duration * i / intervals;
            double x = Math.Clamp((float)source.X + path.PositionAt(i / (double)intervals).X, 0, 512);
            // Numerical headroom also prevents float path rounding from exceeding the source speed.
            if (i > 0) x = Math.Clamp(x, previousX - velocity * step * .999999, previousX + velocity * step * .999999);
            track.Nodes.Add(new Anchor { TimeMs = time, X = x, OutgoingKind = CurveKind.Linear }); previousX = x;
        }
    }
}
