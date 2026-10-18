using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class LegacySliderVelocityEditing
{
    public static void Capture(MapDocument document)
    {
        if (document.ImportedSliders.All(s => s.SliderVelocityMultiplier is not null)) return;
        var timing = new TimingMap.Lookup(document, applyMultiplierOverride: false);
        foreach (var slider in document.ImportedSliders)
        {
            if (slider.SliderVelocityMultiplier is not null) continue;
            double actual = timing.At(slider.TimeMs).SliderVelocityMultiplier;
            slider.SliderVelocityMultiplier = slider.RandomizationBaseVelocity ?? actual;
            if (slider.RandomizationTrack is not null) slider.ProcessedSliderVelocityMultiplier = actual;
        }
    }

    public static void Apply(MapDocument document, IReadOnlySet<Guid> ids, double value)
    {
        if (!double.IsFinite(value) || value < .1 || value > 10) throw new ArgumentException(L.Get("legacySv.range"));
        // Transaction commit owns postprocessing and export validation on the detached worker.
        foreach (var slider in document.ImportedSliders.Where(s => ids.Contains(s.Id)))
            slider.SliderVelocityMultiplier = value;
    }

    internal static double? ConstraintAt(Dictionary<double, double> heads, double time)
        => heads.TryGetValue(time, out double exact) ? exact
            : heads.TryGetValue(Math.Truncate(time), out double quantized) ? quantized : null;

    internal static Dictionary<double, double> HeadVelocities(MapDocument document)
    {
        var result = new Dictionary<double, double>();
        foreach (var slider in document.ImportedSliders.Where(s => s.SliderVelocityMultiplier is not null))
        {
            double head = slider.TimeMs, sv = ImportedSliderConverter.SliderVelocityAt(document, slider);
            if (result.TryGetValue(head, out double existing) && Math.Abs(existing - sv) > 1e-9)
                throw new CatchConversionException(L.Get("core.writer.svCollision"));
            result[head] = sv;
        }
        return result;
    }
}
