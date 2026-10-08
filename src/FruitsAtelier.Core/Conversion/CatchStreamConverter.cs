using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static class CatchStreamConverter
{
    public const double AlignmentTolerance = 0.0001;

    public static CatchConversionResult Convert(MapDocument document, bool compensateTinyDroplets = true, CatchConversionCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sliders = new List<GeneratedSlider>();
        var objects = new List<ConvertedCatchObject>();
        var diagnostics = new List<string>();
        var parentStarts = new Dictionary<Guid, double>();
        var parentOrders = new Dictionary<Guid, int>();
        bool success = true;
        if (!double.IsFinite(document.BeatLengthMs) || document.BeatLengthMs <= 0
            || !double.IsFinite(document.SliderMultiplier) || document.SliderMultiplier <= 0
            || !double.IsFinite(document.SliderTickRate) || document.SliderTickRate <= 0)
        {
            diagnostics.Add(L.Get("core.conversion.settings"));
            return Finish();
        }

        TimingMap.Lookup? timing = null;
        cache?.Begin(document, compensateTinyDroplets);
        Dictionary<Guid, CatchLegacyRandom>? hardRockStates = document.DerandomizeFSliderDroplets && document.DerandomizeDropletsForHardRock
            ? HardRockRandomStates(document, cache, timing ??= new(document)) : null;
        var parents = document.Fruits.Select(f => new Source(f.TimeMs, f.SourceOrder, f, null, null, null))
            .Concat(document.Tracks.Select(t => new Source(t.Nodes.Count > 0 ? t.Nodes[0].TimeMs : 0, t.SourceOrder, null, t, null, null)))
            .Concat(document.ImportedSliders.Select(s => new Source(s.TimeMs, s.SourceOrder, null, null, s, null)))
            .Concat(document.BananaShowers.Select(b => new Source(b.TimeMs, b.SourceOrder, null, null, null, b)))
            .OrderBy(s => s.TimeMs).ThenBy(s => s.SourceOrder);
        var rng = new CatchLegacyRandom(1337);
        var effectRng = new CatchLegacyRandom(document.RandomizeDropletSeed);
        foreach (var source in parents)
        {
            Guid sourceId = source.Fruit?.Id ?? source.Track?.Id ?? source.ImportedSlider?.Id ?? source.BananaShower!.Id;
            parentStarts[sourceId] = source.TimeMs; parentOrders[sourceId] = source.SourceOrder;
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
                CatchLegacyRandom? hardRockBefore = source.Track is { StreamSnapDivisor: null } hrTrack && hardRockStates is not null
                    && hardRockStates.TryGetValue(hrTrack.Id, out var hrState) ? hrState : null;
                if (cache is not null && cache.TryGet(source.Track, source.ImportedSlider, source.BananaShower, ref rng, hardRockBefore, out var cachedSlider, out var cachedObjects))
                {
                    if (cachedSlider is not null) sliders.Add(cachedSlider);
                    objects.AddRange(cachedObjects); AdvanceEffectRandom(cachedObjects, ref effectRng); continue;
                }
                if (source.ImportedSlider is ImportedSlider imported)
                {
                    var candidateRng = rng;
                    var convertedImport = ImportedSliderConverter.Convert(document, imported, ref candidateRng, timing ??= new(document));
                    sliders.Add(convertedImport.Slider);
                    objects.AddRange(convertedImport.Objects);
                    AdvanceEffectRandom(convertedImport.Objects, ref effectRng);
                    rng = candidateRng;
                    cache?.Store(null, imported, null, before, rng, convertedImport.Slider, convertedImport.Objects);
                    continue;
                }
                if (source.BananaShower is BananaShower shower)
                {
                    var candidateRng = rng;
                    var bananas = ConvertBananas(shower, ref candidateRng);
                    objects.AddRange(bananas);
                    AdvanceEffectRandom(bananas, ref effectRng);
                    rng = candidateRng;
                    cache?.Store(null, null, shower, before, rng, null, bananas);
                    continue;
                }
                var track = source.Track!;
                ValidateTrack(document, track);
                if (track.StreamSnapDivisor is not null)
                {
                    var stream = SliderFruitStream.Convert(document, track, timing ??= new(document));
                    objects.AddRange(stream);
                    cache?.Store(track, null, null, before, rng, null, stream);
                    continue;
                }
                var converted = ConvertTrack(document, track, document.DerandomizeFSliderDroplets || (track.CompensateTinyDroplets ?? compensateTinyDroplets),
                    ref rng, (timing ??= new(document)).At(track.Nodes[0].TimeMs), effectRng, cache?.PositionAtTime(track), hardRockBefore);
                sliders.Add(converted.Slider);
                objects.AddRange(converted.Objects);
                AdvanceEffectRandom(converted.Objects, ref effectRng);
                cache?.Store(track, null, null, before, rng, converted.Slider, converted.Objects, hardRockBefore);
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
            // Stream fruits are independent exported parents; ties follow their own time and source order.
            Objects = objects.OrderBy(o => o.TimeMs).ThenBy(o => o.IsStandalone ? o.TimeMs : parentStarts[o.SourceId])
                .ThenBy(o => parentOrders[o.SourceId]).ToArray(),
            Diagnostics = diagnostics.ToArray(),
            Success = success && (sliders.Count > 0 || objects.Count > 0 || diagnostics.Count == 0),
            MaxTickError = objects.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet).Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max(),
            MaxTinyError = document.DerandomizeFSliderDroplets && document.DerandomizeDropletsForHardRock
                ? sliders.Select(slider => slider.MaxTinyError).DefaultIfEmpty().Max()
                : objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max()
        };
    }

    private static TrackConversion ConvertTrack(MapDocument document, CurveTrack track, bool requestCompensation,
        ref CatchLegacyRandom globalRng, TimingState timing, CatchLegacyRandom effectRng, Func<double, double>? positionAtTime = null, CatchLegacyRandom? hardRockBefore = null)
    {
        positionAtTime ??= time => CurveMath.PositionAtTime(track, time);
        double start = track.Nodes[0].TimeMs;
        double duration = track.Nodes[^1].TimeMs - start;
        double sv = timing.SliderVelocityMultiplier;
        // Inherited beat lengths round to float; leave room below the path-length limit.
        double maximumSv = Math.Min(LegacyCatchRules.MaximumSliderVelocityMultiplier,
            Math.Max(sv, LegacyCatchRules.MaximumPathLength * timing.BeatLengthMs
                / (duration * 100 * document.SliderMultiplier) * (1 - 1e-7)));
        bool randomize = !document.DerandomizeFSliderDroplets && track.DropletRandomization is { Enabled: true };
        // Native offsets must act on the base path, preserving repeat geometry and edge clamping.
        bool nativeRandomize = randomize && document.RandomizeDropletStrength == 20 && document.RandomizeDropletSeed == 1337
            && track.DropletRandomization!.Adjustments.Count == 0;
        bool compensate = !nativeRandomize && (randomize || requestCompensation);

        for (int attempt = 0; attempt < 18; attempt++)
        {
            double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, sv);
            double length = duration * velocity;
            double tickDistance = velocity * timing.BeatLengthMs / document.SliderTickRate;
            if (!double.IsFinite(velocity) || velocity <= 0 || !double.IsFinite(length) || length <= 0
                || !double.IsFinite(tickDistance) || tickDistance <= 0)
                throw new CatchConversionException(L.Get("core.conversion.timingRange"));
            var nested = LegacyCatchRules.CreateNested(start, duration, velocity, tickDistance, length, track.SpanCount, quantizeStart: true);

            // RNG follows each complete parent stream before the next parent, including overlapping streams.
            var candidateRng = globalRng;
            LegacyCatchRules.ApplyRandomSequence(nested, ref candidateRng);
            int[]? normalOffsets = null;
            if (hardRockBefore is { } hrRng)
            {
                normalOffsets = nested.Select(item => item.RawOffset).ToArray();
                LegacyCatchRules.ApplyRandomSequence(nested, ref hrRng);
            }
            var targetAtTime = randomize ? DropletRandomization.Targets(document, track, nested, positionAtTime, effectRng) : positionAtTime;
            List<MapPoint> samples;
            try { samples = Samples(track, nested, compensate, nativeRandomize ? positionAtTime : targetAtTime); }
            catch (TinyConstraintException) when (compensate)
            {
                if (sv < maximumSv)
                {
                    sv = maximumSv;
                    continue;
                }
                samples = TinyCompensationFitter.Fit(track, nested, velocity, positionAtTime, targetAtTime);
            }

            double requiredVelocity = 0;
            for (int i = 1; i < samples.Count; i++)
                requiredVelocity = Math.Max(requiredVelocity, Math.Abs(samples[i].X - samples[i - 1].X) / (samples[i].TimeMs - samples[i - 1].TimeMs));

            if (requiredVelocity > velocity * (1 + 1e-12))
            {
                if (sv < maximumSv)
                {
                    double requestedSv = requiredVelocity * timing.BeatLengthMs / (100 * document.SliderMultiplier);
                    sv = Math.Min(maximumSv,
                        Math.Max(sv * 1.01, Math.Ceiling(requestedSv * 1.01 * 1_000_000) / 1_000_000));
                    continue;
                }
                if (compensate)
                {
                    samples = TinyCompensationFitter.Fit(track, nested, velocity, positionAtTime, targetAtTime);
                }
                else throw new CatchConversionException(L.Get("core.conversion.speedLimit", velocity, requiredVelocity));
            }

            var geometry = SliderGeometry.Create(samples, velocity);
            var converted = new List<ConvertedCatchObject>(nested.Count);
            for (int index = 0; index < nested.Count; index++)
            {
                var item = nested[index];
                float pathX = (float)geometry.XAtDistance(item.Progress * length);
                float rawOffset = normalOffsets is not null ? normalOffsets[index] : item.RawOffset;
                float offset = item.Kind == CatchObjectKind.TinyDroplet ? Math.Clamp(rawOffset, -pathX, 512 - pathX) : 0;
                float effectiveX = Math.Clamp(pathX + offset, 0, 512);
                converted.Add(new(track.Id, index, item.Kind, item.TimeMs, effectiveX,
                    Math.Clamp((item.Kind == CatchObjectKind.TinyDroplet ? targetAtTime : positionAtTime)(item.TimeMs), 0, 512), pathX, offset));
            }

            double tickError = converted.Where(o => o.Kind != CatchObjectKind.TinyDroplet)
                .Select(o => Math.Abs(o.X - o.TargetX)).DefaultIfEmpty().Max();
            double tinyError = converted.Select((item, index) => (item, index)).Where(pair => pair.item.Kind == CatchObjectKind.TinyDroplet)
                .Select(pair => Math.Abs((hardRockBefore is not null
                    ? Math.Clamp(pair.item.PathX + nested[pair.index].RawOffset, 0, 512) : pair.item.X) - pair.item.TargetX)).DefaultIfEmpty().Max();
            if (tickError > AlignmentTolerance)
                throw new CatchConversionException(L.Get("core.conversion.tickError", tickError));

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

    private static Dictionary<Guid, CatchLegacyRandom> HardRockRandomStates(MapDocument document, CatchConversionCache? cache, TimingMap.Lookup timing)
    {
        var sources = document.Fruits.Select(f => new Source(f.TimeMs, f.SourceOrder, f, null, null, null)).ToList();
        foreach (var track in document.Tracks.Where(t => t.Nodes.Count >= 2))
        {
            if (track.StreamSnapDivisor is null) sources.Add(new(track.Nodes[0].TimeMs, track.SourceOrder, null, track, null, null));
            else
            {
                try
                {
                    var stream = cache?.ContextObjects(track: track) ?? SliderFruitStream.Convert(document, track, timing);
                    foreach (var item in stream) sources.Add(new(item.TimeMs, track.SourceOrder,
                        new Fruit { TimeMs = item.TimeMs, X = item.X }, null, null, null, track.Nodes[0].TimeMs));
                }
                catch (CatchConversionException) { }
            }
        }
        sources.AddRange(document.ImportedSliders.Select(slider => new Source(slider.TimeMs, slider.SourceOrder, null, null, slider, null)));
        sources.AddRange(document.BananaShowers.Select(shower => new Source(shower.TimeMs, shower.SourceOrder, null, null, null, shower)));
        var result = new Dictionary<Guid, CatchLegacyRandom>();
        var state = new CatchHardRockState();
        foreach (var source in sources.OrderBy(s => s.TimeMs).ThenBy(s => s.SourceOrder).ThenBy(s => s.ParentStartTime ?? s.TimeMs))
        {
            if (source.Fruit is { } fruit)
            {
                // HR branches on exported coordinates and timestamps before consuming RNG.
                state.Fruit((float)Math.Round(fruit.X, MidpointRounding.AwayFromZero), OsuBeatmapWriter.QuantizeTime(fruit.TimeMs));
                continue;
            }
            try
            {
                IReadOnlyList<ConvertedCatchObject>? events = cache?.ContextObjects(source.Track, source.ImportedSlider, source.BananaShower);
                if (source.Track is { } track)
                {
                    result[track.Id] = state.Random;
                    state.Slider((float)Math.Round(track.Nodes[^1].X, MidpointRounding.AwayFromZero), OsuBeatmapWriter.QuantizeTime(source.TimeMs));
                    if (events is null)
                    {
                        var at = timing.At(source.TimeMs);
                        double velocity = LegacyCatchRules.Velocity(at.BeatLengthMs, document.SliderMultiplier, at.SliderVelocityMultiplier);
                        double duration = track.Nodes[^1].TimeMs - source.TimeMs;
                        var nested = LegacyCatchRules.CreateNested(source.TimeMs, duration, velocity,
                            velocity * at.BeatLengthMs / document.SliderTickRate, duration * velocity, track.SpanCount, quantizeStart: true);
                        LegacyCatchRules.ApplyRandomSequence(nested, ref state.Random);
                        continue;
                    }
                }
                else if (source.ImportedSlider is { } imported)
                {
                    state.Slider(imported.ControlPoints.Count > 0 ? (float)imported.ControlPoints[^1].X : (float)imported.X, OsuBeatmapWriter.QuantizeTime(source.TimeMs));
                    if (events is null) { ImportedSliderConverter.Convert(document, imported, ref state.Random, timing); continue; }
                }
                else if (events is null) { ConvertBananas(source.BananaShower!, ref state.Random); continue; }
                foreach (var item in events!)
                {
                    if (item.Kind == CatchObjectKind.Banana) { state.Random.Next(); state.Random.Next(); state.Random.Next(); state.Random.Next(); }
                    else if (item.Kind is CatchObjectKind.TinyDroplet or CatchObjectKind.Droplet) state.Random.Next();
                }
            }
            catch (CatchConversionException) { }
        }
        return result;
    }

    private static void AdvanceEffectRandom(IReadOnlyList<ConvertedCatchObject> objects, ref CatchLegacyRandom random)
    {
        foreach (var item in objects)
        {
            if (item.Kind == CatchObjectKind.Banana) { random.Next(); random.Next(); random.Next(); random.Next(); }
            else if (item.Kind is CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet) random.Next();
        }
    }

    private static List<MapPoint> Samples(CurveTrack track, IReadOnlyList<NestedCatchEvent> nested, bool compensate, Func<double, double> positionAtTime)
    {
        double start = track.Nodes[0].TimeMs;
        double duration = track.Nodes[^1].TimeMs - start;
        var knots = new SortedDictionary<double, double>();
        foreach (var item in nested)
        {
            double pathTime = start + item.Progress * duration;
            double wantedX = Math.Clamp(positionAtTime(pathTime), 0, 512);
            if (compensate && item.Kind == CatchObjectKind.TinyDroplet)
                wantedX = Math.Clamp(Math.Clamp(positionAtTime(item.TimeMs), 0, 512) - item.RawOffset, 0, 512);
            else if (item.Kind != CatchObjectKind.TinyDroplet)
                wantedX = Math.Clamp(CurveMath.PositionAtTime(track, item.TimeMs), 0, 512);
            if (knots.TryGetValue(pathTime, out double existing) && Math.Abs(existing - wantedX) > AlignmentTolerance)
                throw new TinyConstraintException(L.Get("core.conversion.tinyConflict"));
            knots[pathTime] = wantedX;
        }

        // Only gameplay events constrain the exported path. Authoring anchors and
        // intermediate Bezier samples are not additional catch objects; following
        // their local slope can reject an otherwise realizable event sequence.
        return knots.Select(k => new MapPoint(k.Key, k.Value)).ToList();
    }

    private static void ValidateTrack(MapDocument document, CurveTrack track)
    {
        if (!double.IsFinite(document.RandomizeDropletStrength) || document.RandomizeDropletStrength is < 0 or > 100
            || track.DropletRandomization is { IsValid: false })
            throw new CatchConversionException(L.Get("randomize.invalid"));
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
        // Banana counts determine downstream RNG, so use the timestamps emitted by export.
        var times = OsuBeatmapWriter.BananaTimes(shower);
        int start = (int)times.Start, end = (int)times.End;
        float spacing = (float)(times.End - times.Start);
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

    private sealed record Source(double TimeMs, int SourceOrder, Fruit? Fruit, CurveTrack? Track, ImportedSlider? ImportedSlider, BananaShower? BananaShower, double? ParentStartTime = null);
    private sealed record TrackConversion(GeneratedSlider Slider, IReadOnlyList<ConvertedCatchObject> Objects);
    private sealed class TinyConstraintException(string message) : CatchConversionException(message);
}
