using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static class ImportedSliderConverter
{
    public static double DurationMs(MapDocument document, ImportedSlider slider)
    {
        var path = new ImportedSliderGeometry(slider);
        var timing = TimingMap.At(document, slider.TimeMs);
        return path.Distance / LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, timing.SliderVelocityMultiplier) * slider.SpanCount;
    }

    public static double EndTimeMs(MapDocument document, ImportedSlider slider) => slider.TimeMs + DurationMs(document, slider);

    public static double PositionAtTime(MapDocument document, ImportedSlider slider, double time)
    {
        var path = new ImportedSliderGeometry(slider);
        var timing = TimingMap.At(document, slider.TimeMs);
        double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, timing.SliderVelocityMultiplier);
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
        var path = new ImportedSliderGeometry(slider);
        var timing = lookup.At(slider.TimeMs);
        double velocity = LegacyCatchRules.Velocity(timing.BeatLengthMs, document.SliderMultiplier, timing.SliderVelocityMultiplier);
        double duration = path.Distance / velocity;
        double tickDistance = velocity * timing.BeatLengthMs / document.SliderTickRate;
        if (!double.IsFinite(duration) || duration <= 0 || slider.TimeMs + duration * slider.SpanCount > int.MaxValue)
            throw new CatchConversionException(L.Get("core.importConverter.duration"));
        var nested = LegacyCatchRules.CreateNested(slider.TimeMs, duration, velocity, tickDistance, path.Distance, slider.SpanCount);
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
            SourceId = slider.Id, IsImported = true, SpanCount = slider.SpanCount, StartTimeMs = slider.TimeMs,
            DurationMs = duration * slider.SpanCount, Velocity = velocity, SliderVelocityMultiplier = timing.SliderVelocityMultiplier,
            TickDistance = tickDistance, Length = path.Distance, Path = path.AbsolutePoints(slider), TinyCompensationApplied = false
        }, objects);
    }
}
