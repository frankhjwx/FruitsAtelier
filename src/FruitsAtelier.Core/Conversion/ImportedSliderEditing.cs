using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed record ImportedSliderEditResult(CurveTrack Track, IReadOnlyList<string> Diagnostics);
public sealed record SliderConversionFailure(Guid Id, double TimeMs, string Reason);
public sealed record SliderBatchConversionResult(IReadOnlyList<CurveTrack> Tracks, IReadOnlyList<SliderConversionFailure> Failures);

public static class ImportedSliderEditing
{
    public const double ApproximationTolerance = ImportedCurveFitter.Tolerance;

    public static ImportedSliderEditResult ConvertToTrack(MapDocument document, Guid sliderId)
    {
        if (!document.ImportedSliders.Any(s => s.Id == sliderId))
            throw new ArgumentException(L.Get("core.importEditing.notFound"), nameof(sliderId));
        var result = Convert(document, [sliderId]);
        if (result.Tracks.Count == 0) throw new InvalidOperationException(result.Failures[0].Reason);
        return new(result.Tracks[0], []);
    }

    public static SliderBatchConversionResult ConvertAll(MapDocument document, CancellationToken cancellation = default)
        => Convert(document, document.ImportedSliders.Select(s => s.Id).ToArray(), cancellation);

    private static SliderBatchConversionResult Convert(MapDocument document, IReadOnlyCollection<Guid> ids, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var wanted = ids.ToHashSet();
        var sources = document.ImportedSliders.Where(s => wanted.Contains(s.Id)).ToArray();
        var failures = new List<SliderConversionFailure>();
        var candidates = new Dictionary<Guid, CurveTrack>();
        foreach (var slider in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var path = new ImportedSliderGeometry(slider);
                var timing = TimingMap.At(document, slider.TimeMs);
                double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, timing.SliderVelocityMultiplier);
                if (!double.IsFinite(velocity) || velocity <= 0 || path.Distance <= 0)
                    throw new InvalidOperationException(L.Get("core.importEditing.invalidPath"));
                var track = new CurveTrack
                {
                    Id = slider.Id, SourceOrder = slider.SourceOrder, OriginalLine = slider.OriginalLine,
                    Name = L.Get("core.names.importedSlider", slider.PathType, slider.TimeMs),
                    SpanCount = slider.SpanCount, CompensateTinyDroplets = true
                };
                ImportedCurveFitter.Fit(track, path.TimeXPoints(slider, velocity), velocity);
                candidates.Add(slider.Id, track);
            }
            catch (Exception error) when (error is CatchConversionException or InvalidOperationException or ArgumentException)
            { failures.Add(new(slider.Id, slider.TimeMs, error.Message)); }
        }
        if (candidates.Count == 0) return new([], failures);
        cancellation.ThrowIfCancellationRequested();
        var before = CatchStreamConverter.Convert(document).Objects.ToLookup(o => o.SourceId);
        var candidate = document.DeepClone();
        var cache = new CatchConversionCache();
        while (candidates.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            candidate.ImportedSliders.Clear();
            candidate.ImportedSliders.AddRange(document.ImportedSliders.Where(s => !candidates.ContainsKey(s.Id)));
            candidate.Tracks.RemoveAll(t => wanted.Contains(t.Id)); candidate.Tracks.AddRange(candidates.Values);
            var generated = CatchStreamConverter.Convert(candidate, cache: cache);
            var sliders = generated.Sliders.ToDictionary(s => s.SourceId);
            var objects = generated.Objects.ToLookup(o => o.SourceId);
            var rejected = new List<(Guid Id, string Reason)>();
            foreach (var (id, track) in candidates)
            {
                if (!sliders.TryGetValue(id, out var slider) || !slider.TinyCompensationSucceeded)
                    rejected.Add((id, L.Get("core.importEditing.alignmentFailed")));
            }
            // Restore failed parents before checking downstream events: they consume RNG too.
            if (rejected.Count == 0)
                foreach (var (id, track) in candidates)
                {
                    var original = before[id].ToArray(); var edited = objects[id].ToArray();
                    if (original.Length == 0 || original.Length != edited.Length)
                    { rejected.Add((id, L.Get("core.importEditing.sequenceChanged"))); continue; }
                    for (int i = 0; i < original.Length; i++)
                    {
                        var a = original[i]; var b = edited[i];
                        if (a.Kind != b.Kind || a.EventIndex != b.EventIndex || Math.Abs(a.TimeMs - b.TimeMs) > 0.000001)
                        { rejected.Add((id, L.Get("core.importEditing.sequenceChanged"))); break; }
                        if (a.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet
                            && Math.Abs(a.X - b.X) > ApproximationTolerance + CatchStreamConverter.AlignmentTolerance)
                        { rejected.Add((id, L.Get("core.importEditing.fitFailed"))); break; }
                    }
                }
            if (rejected.Count == 0) break;
            foreach (var (id, reason) in rejected)
            {
                candidates.Remove(id);
                failures.Add(new(id, sources.First(s => s.Id == id).TimeMs, reason));
            }
        }
        cancellation.ThrowIfCancellationRequested();
        // Publish only validated replacements; preserve failed originals and leave history to the caller.
        document.ImportedSliders.RemoveAll(s => candidates.ContainsKey(s.Id));
        document.Tracks.AddRange(candidates.Values);
        return new(candidates.Values.ToArray(), failures);
    }
}
