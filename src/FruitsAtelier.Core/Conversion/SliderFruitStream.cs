using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class SliderFruitStream
{
    public static IReadOnlyList<ConvertedCatchObject> Convert(MapDocument document, CurveTrack track)
    {
        if (track.StreamSnapDivisor is not (>= 1 and <= 16))
            throw new CatchConversionException(L.Get("stream.invalidSnap"));
        double start = track.Nodes[0].TimeMs, end = CurveMath.EndTimeMs(track);
        double step = TimingMap.At(document, start).BeatLengthMs / track.StreamSnapDivisor.Value;
        double intervals = (end - start) / step;
        if (!double.IsFinite(intervals) || intervals < 0 || intervals >= LegacyCatchRules.MaximumNestedObjects)
            throw new CatchConversionException(L.Get("stream.tooMany"));
        var result = new List<ConvertedCatchObject>();
        for (int index = 0; index <= Math.Floor(intervals + 1e-8); index++)
        {
            double time = Math.Min(end, start + index * step);
            double target = CurveMath.PositionAtTime(track, time), x = (float)target;
            result.Add(new(track.Id, index, CatchObjectKind.Fruit, time, x, target, x, 0, true));
        }
        return result;
    }

    // A stream inherits the slider's object samples; only its first fruit starts a new combo.
    public static string FruitLine(CurveTrack track, int index, string x, string time)
    {
        string[] source = track.OriginalLine?.Split(',') ?? [];
        int flags = source.Length > 3 && int.TryParse(source[3], out int value) ? value : 0;
        string sound = source.Length > 4 ? source[4] : "0";
        string sample = source.Length > 10 ? source[10] : "0:0:0:0:";
        int type = 1 | (index == 0 ? flags & (4 | 112) : 0);
        return $"{x},192,{time},{type.ToString(CultureInfo.InvariantCulture)},{sound},{sample}";
    }
}
