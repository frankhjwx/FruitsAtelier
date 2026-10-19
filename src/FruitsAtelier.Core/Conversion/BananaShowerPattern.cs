using System.Buffers.Binary;
using System.Security.Cryptography;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record BananaShowerPattern(int LengthNumerator = 1, int LengthDivisor = 16, int GapNumerator = 3, int GapDivisor = 16)
{
    public void Validate()
    {
        if (LengthNumerator < 1 || GapNumerator < 0 || !Supported(LengthDivisor) || !Supported(GapDivisor))
            throw new InvalidDataException(L.Get("bananaEdit.invalid"));
        static bool Supported(int value) => value is >= 1 and <= 12 or 16 or 24 or 32 or 48;
    }

    public static IReadOnlyList<BananaShower> Segments(BananaShower shower, TimingMap.Lookup timing)
        => Layout(shower, timing).Segments;

    public static (IReadOnlyList<BananaShower> Segments, bool IsExactFit) Layout(BananaShower shower, TimingMap.Lookup timing)
    {
        if (shower.Pattern is not { } pattern) return ([shower], true);
        pattern.Validate();
        if (!double.IsFinite(shower.TimeMs) || !double.IsFinite(shower.EndTimeMs) || shower.TimeMs < 0
            || shower.EndTimeMs < shower.TimeMs || shower.EndTimeMs > int.MaxValue)
            throw new InvalidDataException(L.Get("core.conversion.bananaRange"));
        var segments = new List<BananaShower>();
        double start = shower.TimeMs;
        bool exactFit = false, skipped = false;
        int index = 0;
        Span<byte> identity = stackalloc byte[20];
        shower.Id.TryWriteBytes(identity);
        while (start < shower.EndTimeMs)
        {
            if (index >= 10000) throw new InvalidDataException(L.Get("bananaEdit.limit"));
            double fullEnd = Advance(start, (double)pattern.LengthNumerator / pattern.LengthDivisor);
            double end = Math.Min(shower.EndTimeMs, fullEnd);
            exactFit = Math.Abs(fullEnd - shower.EndTimeMs) <= .0001;
            if (end <= start) break;
            // Stable child IDs keep RNG-aware conversion caches reusable after a group edit.
            BinaryPrimitives.WriteInt32LittleEndian(identity[16..], index++);
            var child = shower.DeepClone();
            child.Id = new Guid(SHA256.HashData(identity).AsSpan(0, 16));
            child.Pattern = null; child.TimeMs = start; child.EndTimeMs = end;
            var exported = OsuBeatmapWriter.BananaTimes(child);
            if (end - start >= 2 && exported.End - exported.Start >= 2) segments.Add(child);
            else { exactFit = false; skipped = true; }
            if (end >= shower.EndTimeMs || exactFit) break;
            start = Advance(end, (double)pattern.GapNumerator / pattern.GapDivisor);
        }
        return (segments, exactFit && !skipped);

        double Advance(double time, double beats)
        {
            int boundary = Array.BinarySearch(timing.RedTimes, time);
            boundary = boundary < 0 ? ~boundary : boundary + 1;
            while (true)
            {
                double beatLength = timing.At(time).BeatLengthMs;
                if (!double.IsFinite(beatLength) || beatLength <= 0) throw new InvalidDataException(L.Get("bananaEdit.invalid"));
                double next = boundary < timing.RedTimes.Length ? timing.RedTimes[boundary] : double.PositiveInfinity;
                double available = (next - time) / beatLength;
                if (beats <= available) return time + beats * beatLength;
                beats -= available; time = next; boundary++;
            }
        }
    }
}

internal sealed class BananaShowerExpansion
{
    internal MapDocument Document { get; }
    private readonly Dictionary<Guid, Guid> parents = [];
    private BananaShowerExpansion(MapDocument source)
    {
        Document = source.DeepClone();
        var showers = Document.BananaShowers.ToArray();
        Document.BananaShowers.Clear();
        var timing = new TimingMap.Lookup(source);
        foreach (var shower in showers)
            foreach (var child in BananaShowerPattern.Segments(shower, timing))
            {
                Document.BananaShowers.Add(child);
                if (shower.Pattern is not null) parents.Add(child.Id, shower.Id);
            }
    }
    internal static BananaShowerExpansion? Create(MapDocument source)
        => source.BananaShowers.Any(s => s.Pattern is not null) ? new(source) : null;
    internal Guid Parent(Guid id) => parents.GetValueOrDefault(id, id);
    internal IReadOnlyList<ConvertedCatchObject> Remap(IReadOnlyList<ConvertedCatchObject> objects)
    {
        var counts = objects.Where(o => parents.ContainsKey(o.SourceId)).GroupBy(o => o.SourceId)
            .ToDictionary(g => g.Key, g => g.Max(o => o.EventIndex) + 1);
        var totals = new Dictionary<Guid, int>();
        var offsets = new Dictionary<Guid, int>();
        foreach (var child in Document.BananaShowers.Where(s => parents.ContainsKey(s.Id)))
        {
            Guid parent = Parent(child.Id);
            offsets[child.Id] = totals.GetValueOrDefault(parent);
            totals[parent] = offsets[child.Id] + counts.GetValueOrDefault(child.Id);
        }
        return objects.Select(o => parents.TryGetValue(o.SourceId, out var parent)
            ? o with { SourceId = parent, EventIndex = offsets[o.SourceId] + o.EventIndex } : o).ToArray();
    }
    internal CatchConversionResult Remap(CatchConversionResult result) => new()
    {
        Sliders = result.Sliders, Objects = Remap(result.Objects), Diagnostics = result.Diagnostics,
        Success = result.Success, MaxTickError = result.MaxTickError, MaxTinyError = result.MaxTinyError,
        BananaExpansion = this, BananaSourceConversion = result
    };
}
