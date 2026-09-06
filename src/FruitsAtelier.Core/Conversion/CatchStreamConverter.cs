using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static class CatchStreamConverter
{
    public const double AlignmentTolerance = 0.0001;
    private const double samplingTolerance = 0.02;
    private const int maximumSamples = 30_000;

    public static CatchConversionResult Convert(MapDocument document, bool compensateTinyDroplets = true, CatchConversionCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sliders = new List<GeneratedSlider>();
        var objects = new List<ConvertedCatchObject>();
        var diagnostics = new List<string>();
        bool success = true;
        if (!double.IsFinite(document.BeatLengthMs) || document.BeatLengthMs <= 0
            || !double.IsFinite(document.SliderMultiplier) || document.SliderMultiplier <= 0
            || !double.IsFinite(document.SliderTickRate) || document.SliderTickRate <= 0)
        {
            diagnostics.Add(L.Get("core.conversion.settings"));
            return Finish();
        }

        cache?.Begin(document, compensateTinyDroplets);
        var parents = document.Fruits.Select(f => new Source(f.TimeMs, f.SourceOrder, f, null, null, null))
            .Concat(document.Tracks.Select(t => new Source(t.Nodes.Count > 0 ? t.Nodes[0].TimeMs : 0, t.SourceOrder, null, t, null, null)))
            .Concat(document.ImportedSliders.Select(s => new Source(s.TimeMs, s.SourceOrder, null, null, s, null)))
            .Concat(document.BananaShowers.Select(b => new Source(b.TimeMs, b.SourceOrder, null, null, null, b)))
            .OrderBy(s => s.TimeMs).ThenBy(s => s.SourceOrder);
        var rng = new CatchLegacyRandom(1337);
        foreach (var source in parents)
        {
            if (source.Fruit is Fruit fruit)
            {
                if (!double.IsFinite(fruit.TimeMs) || fruit.TimeMs < 0 || fruit.TimeMs > int.MaxValue
                    || !double.IsFinite(fruit.X) || fruit.X < 0 || fruit.X > 512)
                {
                    diagnostics.Add(L.Get("core.conversion.fruitSkipped"));
                    success = false;
                    continue;
                }
                double x = (float)fruit.X;
                objects.Add(new(fruit.Id, 0, CatchObjectKind.Fruit, fruit.TimeMs, x, fruit.X, x, 0, true));
                continue;
            }

            try
            {
                var before = rng;
                if (cache is not null && cache.TryGet(source.Track, source.ImportedSlider, source.BananaShower, ref rng, out var cachedSlider, out var cachedObjects))
                {
                    if (cachedSlider is not null) sliders.Add(cachedSlider);
                    objects.AddRange(cachedObjects); continue;
                }
                if (source.ImportedSlider is ImportedSlider imported)
                {
                    var candidateRng = rng;
                    var convertedImport = ImportedSliderConverter.Convert(document, imported, ref candidateRng);
                    sliders.Add(convertedImport.Slider);
                    objects.AddRange(convertedImport.Objects);
                    rng = candidateRng;
                    cache?.Store(null, imported, null, before, rng, convertedImport.Slider, convertedImport.Objects);
                    continue;
                }
                if (source.BananaShower is BananaShower shower)
                {
                    var candidateRng = rng;
                    var bananas = ConvertBananas(shower, ref candidateRng);
                    objects.AddRange(bananas);
                    rng = candidateRng;
                    cache?.Store(null, null, shower, before, rng, null, bananas);
                    continue;
                }
                var track = source.Track!;
                ValidateTrack(document, track);
                bool requireCompensation = track.CompensateTinyDroplets == true;
                var converted = ConvertTrack(document, track, track.CompensateTinyDroplets ?? compensateTinyDroplets,
                    requireCompensation, ref rng);
                sliders.Add(converted.Slider);
                objects.AddRange(converted.Objects);
                cache?.Store(track, null, null, before, rng, converted.Slider, converted.Objects);
            }
            catch (CatchConversionException error)
            {
                success = false;
                string name = source.Track?.Name ?? (source.ImportedSlider is not null ? L.Get("core.conversion.importedName") : L.Get("core.conversion.bananaName"));
                diagnostics.Add(L.Get("core.conversion.sourceError", name, source.TimeMs, error.Message));
            }
        }

        cache?.End();
        if (!success)
            diagnostics.Add(L.Get("core.conversion.incomplete"));
        return Finish();

        CatchConversionResult Finish() => new()
        {
            Sliders = sliders.ToArray(),
            Objects = objects.OrderBy(o => o.TimeMs).ToArray(),
            Diagnostics = diagnostics.ToArray(),
            Success = success && (sliders.Count > 0 || objects.Count > 0 || diagnostics.Count == 0),
            MaxTickError = objects.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet).Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max(),
            MaxTinyError = objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max()
        };
    }

    private static TrackConversion ConvertTrack(MapDocument document, CurveTrack track, bool requestCompensation,
        bool requireCompensation, ref CatchLegacyRandom globalRng)
    {
        double start = track.Nodes[0].TimeMs;
        double duration = track.Nodes[^1].TimeMs - start;
        var timing = TimingMap.At(document, start);
        double sv = timing.SliderVelocityMultiplier;
        bool compensate = requestCompensation;

        for (int attempt = 0; attempt < 18; attempt++)
        {
            double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, sv);
            double length = duration * velocity;
            double tickDistance = velocity * timing.BeatLengthMs / document.SliderTickRate;
            if (!double.IsFinite(velocity) || velocity <= 0 || !double.IsFinite(length) || length <= 0
                || !double.IsFinite(tickDistance) || tickDistance <= 0)
                throw new CatchConversionException(L.Get("core.conversion.timingRange"));
            var nested = LegacyCatchRules.CreateNested(start, duration, velocity, tickDistance, length, track.SpanCount);

            // RNG follows each complete parent stream before the next parent, including overlapping streams.
            var candidateRng = globalRng;
            LegacyCatchRules.ApplyRandomSequence(nested, ref candidateRng);
            List<MapPoint> samples;
            try { samples = Samples(track, nested, compensate); }
            catch (TinyConstraintException) when (compensate && !requireCompensation)
            {
                compensate = false;
                sv = timing.SliderVelocityMultiplier;
                continue;
            }
            catch (TinyConstraintException error) when (compensate)
            {
                throw new CatchConversionException(L.Get("core.conversion.tinyRequired", error.Message));
            }

            double requiredVelocity = 0;
            for (int i = 1; i < samples.Count; i++)
                requiredVelocity = Math.Max(requiredVelocity, Math.Abs(samples[i].X - samples[i - 1].X) / (samples[i].TimeMs - samples[i - 1].TimeMs));

            if (requiredVelocity > velocity * (1 + 1e-12))
            {
                if (sv < LegacyCatchRules.MaximumSliderVelocityMultiplier)
                {
                    double requestedSv = requiredVelocity * timing.BeatLengthMs / (100 * document.SliderMultiplier);
                    sv = Math.Min(LegacyCatchRules.MaximumSliderVelocityMultiplier,
                        Math.Max(sv * 1.01, Math.Ceiling(requestedSv * 1.01 * 1_000_000) / 1_000_000));
                    continue;
                }
                if (compensate)
                {
                    if (requireCompensation)
                        throw new CatchConversionException(L.Get("core.conversion.tinyRequired", L.Get("core.conversion.tinySpeedFallback")));
                    compensate = false;
                    sv = timing.SliderVelocityMultiplier;
                    continue;
                }
                throw new CatchConversionException(L.Get("core.conversion.speedLimit", velocity, requiredVelocity));
            }

            var geometry = SliderGeometry.Create(samples, velocity);
            var converted = new List<ConvertedCatchObject>(nested.Count);
            for (int index = 0; index < nested.Count; index++)
            {
                var item = nested[index];
                float pathX = (float)geometry.XAtDistance(item.Progress * length);
                float offset = item.Kind == CatchObjectKind.TinyDroplet ? Math.Clamp(item.RawOffset, -pathX, 512 - pathX) : 0;
                float effectiveX = Math.Clamp(pathX + offset, 0, 512);
                converted.Add(new(track.Id, index, item.Kind, item.TimeMs, effectiveX,
                    CurveMath.PositionAtTime(track, item.TimeMs), pathX, offset));
            }

            double tickError = converted.Where(o => o.Kind != CatchObjectKind.TinyDroplet)
                .Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max();
            double tinyError = converted.Where(o => o.Kind == CatchObjectKind.TinyDroplet)
                .Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max();
            if (tickError > AlignmentTolerance)
                throw new CatchConversionException(L.Get("core.conversion.tickError", tickError));
            if (requireCompensation && tinyError > AlignmentTolerance)
                throw new CatchConversionException(L.Get("core.conversion.tinyRequired", L.Get("core.conversion.tinyBoundary")));

            globalRng = candidateRng;
            return new(new GeneratedSlider
            {
                SourceId = track.Id, StartTimeMs = start, DurationMs = duration * track.SpanCount, SpanCount = track.SpanCount, Velocity = velocity,
                SliderVelocityMultiplier = sv, TickDistance = tickDistance, Length = length,
                Path = geometry.Points.ToArray(), TinyCompensationApplied = compensate,
                TinyCompensationSucceeded = compensate && tinyError <= AlignmentTolerance,
                MaxTickError = tickError, MaxTinyError = tinyError
            }, converted);
        }
        throw new CatchConversionException(L.Get("core.conversion.iterationLimit"));
    }

    private static List<MapPoint> Samples(CurveTrack track, IReadOnlyList<NestedCatchEvent> nested, bool compensate)
    {
        double start = track.Nodes[0].TimeMs;
        double duration = track.Nodes[^1].TimeMs - start;
        var knots = new SortedDictionary<double, double>();
        foreach (var item in nested)
        {
            double pathTime = start + item.Progress * duration;
            double wantedX = CurveMath.PositionAtTime(track, pathTime);
            if (compensate && item.Kind == CatchObjectKind.TinyDroplet)
                wantedX = Math.Clamp(CurveMath.PositionAtTime(track, item.TimeMs) - item.RawOffset, 0, 512);
            else if (item.Kind != CatchObjectKind.TinyDroplet)
                wantedX = CurveMath.PositionAtTime(track, item.TimeMs);
            if (knots.TryGetValue(pathTime, out double existing) && Math.Abs(existing - wantedX) > AlignmentTolerance)
                throw new TinyConstraintException(L.Get("core.conversion.tinyConflict"));
            knots[pathTime] = wantedX;
        }

        double[] knotTimes = knots.Keys.ToArray();
        double[] offsets = knots.Select(k => k.Value - CurveMath.PositionAtTime(track, k.Key)).ToArray();
        double[] divisions = knotTimes.Concat(track.Nodes.Select(n => n.TimeMs)).Distinct().Order().ToArray();
        var samples = new List<MapPoint> { new(divisions[0], Evaluate(divisions[0])) };
        for (int i = 1; i < divisions.Length; i++)
            Subdivide(samples[^1], new(divisions[i], Evaluate(divisions[i])), 0);
        return samples;

        double Evaluate(double time)
        {
            int index = Array.BinarySearch(knotTimes, time);
            if (index >= 0) return knots[time];
            index = ~index;
            double adjustment = index <= 0 ? offsets[0] : index >= offsets.Length ? offsets[^1]
                : offsets[index - 1] + (offsets[index] - offsets[index - 1]) * (time - knotTimes[index - 1]) / (knotTimes[index] - knotTimes[index - 1]);
            return Math.Clamp(CurveMath.PositionAtTime(track, time) + adjustment, 0, 512);
        }

        void Subdivide(MapPoint a, MapPoint b, int depth)
        {
            double interval = b.TimeMs - a.TimeMs;
            var middle = new MapPoint(a.TimeMs + interval / 2, Evaluate(a.TimeMs + interval / 2));
            double quarter = Evaluate(a.TimeMs + interval / 4);
            double threeQuarter = Evaluate(a.TimeMs + interval * 0.75);
            double error = Math.Max(Math.Abs(middle.X - (a.X + b.X) / 2),
                Math.Max(Math.Abs(quarter - (a.X * 0.75 + b.X * 0.25)), Math.Abs(threeQuarter - (a.X * 0.25 + b.X * 0.75))));
            bool canSplit = depth < 24 && interval > 0.0000001
                && middle.TimeMs > a.TimeMs && middle.TimeMs < b.TimeMs;
            if (canSplit && samples.Count < maximumSamples && (interval > 25 || error > samplingTolerance))
            {
                Subdivide(a, middle, depth + 1);
                Subdivide(middle, b, depth + 1);
                return;
            }
            // Every gameplay event is already a division endpoint, so accepting a numerically
            // indivisible interval only relaxes the visual curve between exact object knots.
            samples.Add(b);
        }
    }

    private static void ValidateTrack(MapDocument document, CurveTrack track)
    {
        var validationDocument = new MapDocument
        {
            DurationMs = document.DurationMs, BeatLengthMs = document.BeatLengthMs,
            TimingOffsetMs = document.TimingOffsetMs, ApproachRate = document.ApproachRate,
            SliderMultiplier = document.SliderMultiplier, SliderTickRate = document.SliderTickRate
        };
        validationDocument.Tracks.Add(track);
        var errors = CurveMath.Validate(validationDocument);
        if (errors.Count > 0) throw new CatchConversionException(errors[0]);
        if (CurveMath.EndTimeMs(track) > int.MaxValue) throw new CatchConversionException(L.Get("core.conversion.timeRange"));
    }

    private static IReadOnlyList<ConvertedCatchObject> ConvertBananas(BananaShower shower, ref CatchLegacyRandom rng)
    {
        if (!double.IsFinite(shower.TimeMs) || !double.IsFinite(shower.EndTimeMs) || shower.TimeMs < 0
            || shower.EndTimeMs < shower.TimeMs || shower.EndTimeMs > int.MaxValue)
            throw new CatchConversionException(L.Get("core.conversion.bananaRange"));
        int start = (int)shower.TimeMs, end = (int)shower.EndTimeMs;
        float spacing = (float)(shower.EndTimeMs - shower.TimeMs);
        while (spacing > 100) spacing /= 2;
        var result = new List<ConvertedCatchObject>();
        if (spacing <= 0) return result;
        for (float time = start; time <= end;)
        {
            if (result.Count >= LegacyCatchRules.MaximumNestedObjects) throw new CatchConversionException(L.Get("core.conversion.bananaLimit"));
            float x = (float)(rng.NextDouble() * 512);
            rng.Next(); rng.Next(); rng.Next();
            result.Add(new(shower.Id, result.Count, CatchObjectKind.Banana, time, x, x, 0, x));
            float next = time + spacing;
            if (next <= time) throw new CatchConversionException(L.Get("core.conversion.bananaPrecision"));
            time = next;
        }
        return result;
    }

    private sealed record Source(double TimeMs, int SourceOrder, Fruit? Fruit, CurveTrack? Track, ImportedSlider? ImportedSlider, BananaShower? BananaShower);
    private sealed record TrackConversion(GeneratedSlider Slider, IReadOnlyList<ConvertedCatchObject> Objects);
    private sealed class TinyConstraintException(string message) : CatchConversionException(message);
}
