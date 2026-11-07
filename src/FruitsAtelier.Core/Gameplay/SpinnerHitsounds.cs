using System.Globalization;

namespace FruitsAtelier.Core;

/// <summary>Audio-only end events; they do not participate in Catch conversion or judgement.</summary>
public static class SpinnerHitsounds
{
    public static bool IsEndEvent(ConvertedCatchObject item) => item.EventIndex >= int.MaxValue - 9999;

    public static ConvertedCatchObject[] Events(MapDocument document)
    {
        if (document.BananaShowers.Count == 0) return [];
        var timing = new TimingMap.Lookup(document);
        return document.BananaShowers.SelectMany(shower => BananaShowerPattern.Segments(shower, timing)
            .Select((segment, index) => new ConvertedCatchObject(shower.Id, int.MaxValue - index,
                CatchObjectKind.Fruit, OsuBeatmapWriter.BananaTimes(segment).End, 256, 256, 256, 0, true)))
            .OrderBy(item => item.TimeMs).ToArray();
    }

    public static void SetSample(BananaShower shower, EventHitsound setting)
    {
        var sample = setting.Sample;
        var fields = setting.InheritedFields;
        sample = sample with {
            NormalSet = fields.HasFlag(InheritedSampleFields.NormalSet) ? 0 : sample.NormalSet,
            AdditionSet = fields.HasFlag(InheritedSampleFields.AdditionSet) ? 0 : sample.AdditionSet,
            Index = fields.HasFlag(InheritedSampleFields.Index) ? 0 : sample.Index,
            Volume = fields.HasFlag(InheritedSampleFields.Volume) ? 0 : sample.Volume };
        var p = shower.OriginalLine?.Split(',') ??
            ["256", "192", shower.TimeMs.ToString(CultureInfo.InvariantCulture), "8", "0", shower.EndTimeMs.ToString(CultureInfo.InvariantCulture), "0:0:0:0:"];
        Array.Resize(ref p, Math.Max(p.Length, 7));
        p[4] = sample.Additions.ToString(CultureInfo.InvariantCulture);
        p[6] = HitsoundCopier.SampleText(sample);
        shower.OriginalLine = string.Join(',', p);
    }
}
