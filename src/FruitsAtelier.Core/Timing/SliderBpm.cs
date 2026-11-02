using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class SliderBpm
{
    public static readonly IReadOnlyList<double> Presets = Array.AsReadOnly(new[] { .125, .25, .5, 1, 2, 4, 8 });

    public static void Validate(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new InvalidDataException(L.Get("aspire.invalidMultiplier"));
    }

    public static TimingState Apply(MapDocument document, TimingState timing, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0) throw new CatchConversionException(L.Get("aspire.invalidMultiplier"));
        double beatLength = timing.BeatLengthMs / (document.EnableSliderBpmMultiplier ? multiplier : 1);
        if (!double.IsFinite(beatLength) || beatLength <= 0) throw new CatchConversionException(L.Get("aspire.invalidMultiplier"));
        return timing with { BeatLengthMs = beatLength };
    }
}
