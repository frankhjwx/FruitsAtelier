namespace FruitsAtelier.Core;

internal sealed class SliderMultiplierAnalysis
{
    internal readonly CatchConversionResult Conversion;
    internal readonly double Minimum, Maximum;
    private readonly Lazy<Details> details;
    internal bool Prepared => details.IsValueCreated;
    internal Dictionary<Guid, ConvertedCatchObject[]> Objects => details.Value.Objects;
    internal Dictionary<Guid, ImportedSlider> Imports => details.Value.Imports;
    internal Dictionary<Guid, (double Time, int Order)> Parents => details.Value.Parents;
    internal Dictionary<(Guid, int), ConvertedCatchObject> Identities => details.Value.Identities;
    internal Dictionary<(Guid, int), ConvertedCatchObject> Normal => details.Value.Normal;
    internal Dictionary<(Guid, int), ConvertedCatchObject> HardRock => details.Value.HardRock;

    internal SliderMultiplierAnalysis(OsuWriteResult baseline, CatchConversionResult conversion)
    {
        Conversion = conversion;
        details = new(() => new(baseline, conversion));
        var map = baseline.ReadBack;
        var magnitudes = map.TimingPoints.Where(p => !p.Uninherited && !double.IsNaN(p.BeatLengthMs))
            .Select(p => p.BeatLengthMs < 0 ? Math.Clamp((float)-p.BeatLengthMs, 10, 1000) : 100).ToList();
        if (map.TimingPoints.Count == 0 || map.TimingPoints.All(p => p.TimeMs > 0)
            || map.TimingPoints.GroupBy(p => p.TimeMs).Any(g => g.All(p => p.Uninherited))) magnitudes.Add(100);
        Minimum = magnitudes.Count == 0 ? double.NegativeInfinity : 10 * map.SliderMultiplier / magnitudes.Min();
        Maximum = magnitudes.Count == 0 ? double.PositiveInfinity : 1000 * map.SliderMultiplier / magnitudes.Max();
    }

    private sealed class Details
    {
        internal readonly Dictionary<Guid, ConvertedCatchObject[]> Objects;
        internal readonly Dictionary<Guid, ImportedSlider> Imports;
        internal readonly Dictionary<Guid, (double Time, int Order)> Parents;
        internal readonly Dictionary<(Guid, int), ConvertedCatchObject> Identities, Normal, HardRock;

        internal Details(OsuWriteResult baseline, CatchConversionResult conversion)
        {
            Identities = conversion.Objects.Zip(baseline.PlayableObjects)
                .ToDictionary(p => (p.First.SourceId, p.First.EventIndex), p => p.Second);
            Normal = baseline.PlayableObjects.ToDictionary(o => (o.SourceId, o.EventIndex));
            HardRock = baseline.PlayableHardRockObjects.ToDictionary(o => (o.SourceId, o.EventIndex));
            Objects = conversion.Objects.GroupBy(o => o.SourceId)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.EventIndex).ToArray());
            var map = baseline.ReadBack;
            Imports = map.ImportedSliders.ToDictionary(s => s.Id);
            Parents = map.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder))
                .Concat(map.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
                .Concat(map.BananaShowers.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
                .ToDictionary(p => p.Id, p => (p.TimeMs, p.SourceOrder));
        }
    }
}
