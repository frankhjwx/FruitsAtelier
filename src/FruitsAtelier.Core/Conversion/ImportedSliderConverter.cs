using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static class ImportedSliderConverter
{
    public static IReadOnlyList<SliderPathPoint> Path(ImportedSlider slider)
        => slider.GetGeometry().AbsolutePoints();

    public static double VelocityAt(MapDocument document, double timeMs, TimingMap.Lookup? lookup = null)
    {
        var timing = lookup?.At(timeMs) ?? TimingMap.At(document, timeMs);
        return LegacyCatchRules.Velocity(timing.BeatLengthMs, document.EffectiveSliderMultiplier, timing.SliderVelocityMultiplier);
    }

    public static double SliderVelocityAt(MapDocument document, ImportedSlider slider, TimingMap.Lookup? lookup = null)
    {
        double? stored = slider.ProcessedSliderVelocityMultiplier ?? slider.SliderVelocityMultiplier;
        double sv = stored is { } own
            ? own * document.SliderMultiplier / document.EffectiveSliderMultiplier
            : (lookup?.At(slider.TimeMs) ?? TimingMap.At(document, slider.TimeMs)).SliderVelocityMultiplier;
        if (!double.IsFinite(sv) || sv < .1 - 1e-12 || sv > 10 + 1e-12)
            throw new CatchConversionException(L.Get("timing.sliderMultiplierSvLimit"));
        if (stored is { } original && document.EffectiveSliderMultiplier != document.SliderMultiplier)
            sv = 100 / SliderMultiplierEditing.ScaleBeatLength(100 / original,
                document.SliderMultiplier / document.EffectiveSliderMultiplier, requireRepresentable: false);
        return Math.Clamp(sv, .1, 10);
    }

    public static double VelocityAt(MapDocument document, ImportedSlider slider, TimingMap.Lookup? lookup = null)
    {
        lookup ??= new TimingMap.Lookup(document);
        var state = lookup.At(slider.TimeMs);
        return LegacyCatchRules.Velocity(state.BeatLengthMs, document.EffectiveSliderMultiplier, SliderVelocityAt(document, slider, lookup));
    }

    public static double DurationMs(MapDocument document, ImportedSlider slider, TimingMap.Lookup? lookup = null)
    {
        var path = slider.GetGeometry();
        return path.Distance / VelocityAt(document, slider, lookup) * slider.SpanCount;
    }

    public static double EndTimeMs(MapDocument document, ImportedSlider slider) => slider.TimeMs + DurationMs(document, slider);

    public static double PositionAtTime(MapDocument document, ImportedSlider slider, double time)
    {
        var path = slider.GetGeometry();
        var lookup = new TimingMap.Lookup(document);
        var timing = lookup.At(slider.TimeMs);
        double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.EffectiveSliderMultiplier, SliderVelocityAt(document, slider, lookup));
        double spanDuration = path.Distance / velocity;
        if (spanDuration <= 0) return Math.Clamp((float)slider.X, 0, 512);
        double spanProgress = Math.Clamp((time - slider.TimeMs) / spanDuration, 0, slider.SpanCount);
        int span = Math.Min(slider.SpanCount - 1, (int)spanProgress);
        double progress = spanProgress - span;
        if (span % 2 == 1) progress = 1 - progress;
        return Math.Clamp(Math.Clamp((float)slider.X, 0, 512) + path.PositionAt(progress).X, 0, 512);
    }

    internal static (GeneratedSlider Slider, IReadOnlyList<ConvertedCatchObject> Objects) Convert(
        MapDocument document, ImportedSlider slider, ref CatchLegacyRandom rng, TimingMap.Lookup lookup)
    {
        if (!double.IsFinite(slider.TimeMs) || slider.TimeMs < 0 || slider.TimeMs > int.MaxValue
            || !double.IsFinite(slider.X) || !double.IsFinite(slider.Y) || !double.IsFinite(slider.PixelLength)
            || slider.PixelLength < 0 || slider.SpanCount is < 1 or > 9000
            || slider.ControlPoints.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.GeometryY)))
            throw new CatchConversionException(L.Get("core.importConverter.parameters"));
        var path = slider.GetGeometry();
        var timing = lookup.At(slider.TimeMs);
        double sv = SliderVelocityAt(document, slider, lookup);
        double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.EffectiveSliderMultiplier, sv);
        double duration = path.Distance / velocity;
        double tickDistance = LegacyCatchRules.TickDistance(document, velocity, timing.BeatLengthMs, sv);
        if (!double.IsFinite(duration) || duration < 0 || slider.TimeMs + duration * slider.SpanCount > int.MaxValue)
            throw new CatchConversionException(L.Get("core.importConverter.duration"));
        var nested = LegacyCatchRules.CreateNested(slider.TimeMs, duration, velocity, tickDistance, path.Distance, slider.SpanCount, legacyImported: true);
        LegacyCatchRules.ApplyRandomSequence(nested, ref rng);
        var objects = new List<ConvertedCatchObject>(nested.Count);
        for (int i = 0; i < nested.Count; i++)
        {
            var item = nested[i];
            float pathX = Math.Clamp((float)slider.X, 0, 512) + path.PositionAt(item.Progress).X;
            float offset = item.Kind == CatchObjectKind.TinyDroplet ? Math.Clamp(item.RawOffset, -pathX, 512 - pathX) : 0;
            float x = Math.Clamp(pathX + offset, 0, 512);
            objects.Add(new(slider.Id, i, item.Kind, item.TimeMs, x, x, pathX, offset));
        }
        return (new GeneratedSlider
        {
            ImportedGeometry = path, ImportedOffsets = nested.Select(item => item.RawOffset).ToArray(),
            SourceId = slider.Id, IsImported = true, SpanCount = slider.SpanCount, StartTimeMs = slider.TimeMs,
            DurationMs = duration * slider.SpanCount, Velocity = velocity, SliderVelocityMultiplier = sv,
            TickDistance = tickDistance, Length = path.Distance, Path = path.AbsolutePoints(), TinyCompensationApplied = false
        }, objects);
    }
}
