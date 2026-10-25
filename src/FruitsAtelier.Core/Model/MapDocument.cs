using L = FruitsAtelier.Localization.Strings;
using System.Text.Json.Serialization;

namespace FruitsAtelier.Core;

public readonly record struct MapPoint(double TimeMs, double X)
{
    public static MapPoint operator +(MapPoint a, MapPoint b) => new(a.TimeMs + b.TimeMs, a.X + b.X);
    public static MapPoint operator -(MapPoint a, MapPoint b) => new(a.TimeMs - b.TimeMs, a.X - b.X);
    public static MapPoint operator *(MapPoint point, double scale) => new(point.TimeMs * scale, point.X * scale);
    public static MapPoint operator *(double scale, MapPoint point) => point * scale;
    public static MapPoint Lerp(MapPoint a, MapPoint b, double u) => a * (1 - u) + b * u;
}

public sealed class Fruit : IJsonOnDeserialized
{
    private double y = 192;
    private bool hasY;
    internal Fruit DeepClone() => (Fruit)MemberwiseClone();
    internal bool ContentEquals(Fruit other) => Id == other.Id && TimeMs == other.TimeMs && X == other.X && Y == other.Y
        && SourceOrder == other.SourceOrder && OriginalLine == other.OriginalLine;
    public Guid Id { get; set; } = Guid.NewGuid();
    public double TimeMs { get; set; }
    public double X { get; set; }
    public double Y { get => y; set { y = value; hasY = true; } }
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!hasY && OriginalLine?.Split(',') is { Length: >= 2 } fields)
            y = OsuBeatmapReader.LegacyCoordinate(fields[1]);
    }
}

public sealed class Anchor
{
    internal Anchor DeepClone()
    {
        var copy = (Anchor)MemberwiseClone();
        copy.OutgoingCurve = OutgoingCurve?.DeepClone();
        return copy;
    }
    public Guid Id { get; set; } = Guid.NewGuid();
    public double TimeMs { get; set; }
    public double X { get; set; }
    public MapPoint HandleIn { get; set; }
    public MapPoint HandleOut { get; set; }
    public CurveKind? OutgoingKind { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ControlCurve? OutgoingCurve { get; set; }
}

public enum CurveKind { Linear, Bezier }
public enum SliderDropletMode { FollowMap, Derandomize, Randomize }

public sealed class CurveTrack
{
    internal bool ContentEquals(CurveTrack other)
    {
        var a = this; var b = other;
        if (a.Id != b.Id || a.Name != b.Name || a.Kind != b.Kind || a.SourceOrder != b.SourceOrder || a.Nodes.Count != b.Nodes.Count
            || a.DropletMode != b.DropletMode || a.SpanCount != b.SpanCount || a.OriginalLine != b.OriginalLine || a.CompensateTinyDroplets != b.CompensateTinyDroplets
            || a.SliderVelocityMultiplier != b.SliderVelocityMultiplier
            || a.EndpointSnap != b.EndpointSnap || a.StreamSnapDivisor != b.StreamSnapDivisor || !StackEnvelope.Equal(a.Stack, b.Stack)
            || a.StreamNewComboOffsets.Count != b.StreamNewComboOffsets.Count
            || !DropletRandomization.Equal(a.DropletRandomization, b.DropletRandomization)) return false;
        for (int j = 0; j < a.StreamNewComboOffsets.Count; j++)
            if (a.StreamNewComboOffsets[j] != b.StreamNewComboOffsets[j]) return false;
        for (int j = 0; j < a.Nodes.Count; j++)
        {
            var an = a.Nodes[j]; var bn = b.Nodes[j];
            if (an.Id != bn.Id || an.TimeMs != bn.TimeMs || an.X != bn.X
                || an.HandleIn != bn.HandleIn || an.HandleOut != bn.HandleOut || an.OutgoingKind != bn.OutgoingKind || !ControlCurve.Equal(an.OutgoingCurve, bn.OutgoingCurve)) return false;
        }
        return true;
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = L.Get("core.names.curve");
    public CurveKind Kind { get; set; } = CurveKind.Bezier;
    public int SourceOrder { get; set; } = int.MaxValue;
    public List<Anchor> Nodes { get; private set; } = new();
    public CurveTrack DeepClone()
    {
        var copy = (CurveTrack)MemberwiseClone();
        copy.Nodes = Nodes.Select(n => n.DeepClone()).ToList();
        copy.Stack = Stack?.DeepClone();
        copy.StreamNewComboOffsets = StreamNewComboOffsets.ToList();
        copy.DropletRandomization = DropletRandomization?.DeepClone();
        return copy;
    }
    public int SpanCount { get; set; } = 1;
    public string? OriginalLine { get; set; }
    public bool? CompensateTinyDroplets { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? StreamSnapDivisor { get; set; }
    public List<double> StreamNewComboOffsets { get; private set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StackEnvelope? Stack { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DropletRandomization? DropletRandomization { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SliderDropletMode? DropletMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SliderEndpointSnap? EndpointSnap { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SliderVelocityMultiplier { get; set; }

    internal double? SliderVelocitySeed { get; set; }
    internal double? FixedSliderVelocityMultiplier { get; set; }

    public bool RandomizesDroplets(MapDocument document) => DropletMode switch
    {
        SliderDropletMode.FollowMap => !document.DerandomizeFSliderDroplets,
        SliderDropletMode.Randomize => true,
        SliderDropletMode.Derandomize => false,
        // Missing modes retain the global override used by older project files.
        _ => !document.DerandomizeFSliderDroplets && DropletRandomization is { Enabled: true }
    };
}

public sealed class TimingPoint
{
    public double TimeMs { get; set; }
    public double BeatLengthMs { get; set; } = 500;
    public int Meter { get; set; } = 4;
    public int SampleSet { get; set; }
    public int SampleIndex { get; set; }
    public int Volume { get; set; } = 100;
    public int Effects { get; set; }
    public bool Uninherited { get; set; } = true;
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }

    internal TimingPoint DeepClone() => (TimingPoint)MemberwiseClone();
    internal bool ContentEquals(TimingPoint other) => TimeMs == other.TimeMs && BeatLengthMs.Equals(other.BeatLengthMs)
        && Meter == other.Meter && SampleSet == other.SampleSet && SampleIndex == other.SampleIndex
        && Volume == other.Volume && Effects == other.Effects && Uninherited == other.Uninherited
        && SourceOrder == other.SourceOrder && OriginalLine == other.OriginalLine;
}

public sealed class ImportedSlider
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SliderEndpointSnap? EndpointSnap { get; set; }
    private sealed record GeometryMemo(WeakReference<ImportedSliderGeometry> Geometry, SliderControlPoints Controls,
        long X, long Y, long Length, char PathType);
    private GeometryMemo? geometryMemo;

    internal ImportedSliderGeometry GetGeometry()
    {
        long x = BitConverter.DoubleToInt64Bits(X), y = BitConverter.DoubleToInt64Bits(Y), length = BitConverter.DoubleToInt64Bits(PixelLength);
        if (geometryMemo is { } memo && memo.X == x && memo.Y == y && memo.Length == length
            && memo.PathType == PathType && memo.Controls.ContentEquals(ControlPoints)
            && memo.Geometry.TryGetTarget(out var cached)) return cached;
        var geometry = new ImportedSliderGeometry(this);
        geometryMemo = new(new(geometry), ControlPoints.Share(), x, y, length, PathType);
        return geometry;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImportedSlider? RandomizationSource { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CurveTrack? RandomizationTrack { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RandomizationBaseVelocity { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PreserveNativeGeometry { get; set; }
    public const int MaximumControlPoints = 262144;
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double TimeMs { get; set; }
    public char PathType { get; set; } = 'B';
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SliderVelocityMultiplier { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ProcessedSliderVelocityMultiplier { get; set; }
    public SliderControlPoints ControlPoints { get; private set; } = new();
    public int SpanCount { get; set; } = 1;
    public double PixelLength { get; set; }

    internal ImportedSlider DeepClone()
    {
        var copy = new ImportedSlider
        {
            Id = Id, SourceOrder = SourceOrder, OriginalLine = OriginalLine, X = X, Y = Y, EndpointSnap = EndpointSnap,
            TimeMs = TimeMs, PathType = PathType, SpanCount = SpanCount, PixelLength = PixelLength,
            RandomizationSource = RandomizationSource?.DeepClone(), RandomizationTrack = RandomizationTrack?.DeepClone(),
            RandomizationBaseVelocity = RandomizationBaseVelocity, PreserveNativeGeometry = PreserveNativeGeometry,
            SliderVelocityMultiplier = SliderVelocityMultiplier, ProcessedSliderVelocityMultiplier = ProcessedSliderVelocityMultiplier
        };
        copy.ControlPoints = ControlPoints.Share();
        copy.geometryMemo = geometryMemo;
        return copy;
    }

    internal bool ContentEquals(ImportedSlider other) => Id == other.Id && SourceOrder == other.SourceOrder
        && OriginalLine == other.OriginalLine && X == other.X && Y == other.Y && TimeMs == other.TimeMs
        && PathType == other.PathType && SpanCount == other.SpanCount && PixelLength == other.PixelLength
        && SliderVelocityMultiplier == other.SliderVelocityMultiplier && ProcessedSliderVelocityMultiplier == other.ProcessedSliderVelocityMultiplier
        && RandomizationBaseVelocity == other.RandomizationBaseVelocity
        && PreserveNativeGeometry == other.PreserveNativeGeometry && EndpointSnap == other.EndpointSnap
        && ControlPoints.ContentEquals(other.ControlPoints)
        && (ReferenceEquals(RandomizationTrack, other.RandomizationTrack)
            || RandomizationTrack is not null && other.RandomizationTrack is not null && RandomizationTrack.ContentEquals(other.RandomizationTrack))
        && (ReferenceEquals(RandomizationSource, other.RandomizationSource)
            || RandomizationSource is not null && other.RandomizationSource is not null && RandomizationSource.ContentEquals(other.RandomizationSource));

}

public sealed class BananaShower
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }
    public double TimeMs { get; set; }
    public double EndTimeMs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BananaShowerPattern? Pattern { get; set; }

    internal BananaShower DeepClone() => (BananaShower)MemberwiseClone();
    internal bool ContentEquals(BananaShower other) => Id == other.Id && SourceOrder == other.SourceOrder
        && OriginalLine == other.OriginalLine && TimeMs == other.TimeMs && EndTimeMs == other.EndTimeMs && Pattern == other.Pattern;
}

public sealed partial class MapDocument
{
    public string Name { get; set; } = L.Get("core.names.untitled");
    public double DurationMs { get; set; } = 30_000;
    public double BeatLengthMs { get; set; } = 500;
    public double TimingOffsetMs { get; set; }
    public double ApproachRate { get; set; } = 8;
    public double CircleSize { get; set; } = 5;
    public double SliderMultiplier { get; set; } = 1.92;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OverrideSliderMultiplier { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SliderMultiplierOverride { get; set; }
    [JsonIgnore]
    public double EffectiveSliderMultiplier => SliderMultiplierOverride ?? SliderMultiplier;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DistancePerBeatOverride { get; set; }
    [JsonIgnore]
    public double DistancePerBeat => DistancePerBeatOverride ?? 100 * SliderMultiplier;
    public double SliderTickRate { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacySliderTicks { get; set; }
    public double DistanceSpacing { get; set; } = 1;
    public double OsuDistanceSpacing { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DerandomizeDroplets { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SliderImportState SliderImportState { get; set; }
    public bool DistanceSnapCollinear { get; set; } = true;
    public double RandomizeDropletStrength { get; set; } = 20;
    public int RandomizeDropletSeed { get; set; } = 1337;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RandomizeNewSliders { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DerandomizeFSliderDroplets { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DerandomizeDropletsForHardRock { get; set; }
    public List<double> DistanceSnapRatios { get; } = new();
    public List<EventHitsound> HitsoundOverrides { get; } = new();
    public List<Fruit> Fruits { get; } = new();
    public List<CurveTrack> Tracks { get; } = new();
    public List<TimingPoint> TimingPoints { get; } = new();
    public List<ImportedSlider> ImportedSliders { get; } = new();
    public List<BananaShower> BananaShowers { get; } = new();

    public MapDocument DeepClone()
    {
        var copy = new MapDocument
        {
            Name = Name, DurationMs = DurationMs, CollaborationReferences = CollaborationReferences?.ToList(),
            CollaborationEditableRanges = CollaborationEditableRanges?.ToList(),
            BeatLengthMs = BeatLengthMs, TimingOffsetMs = TimingOffsetMs, ApproachRate = ApproachRate,
            CircleSize = CircleSize, SliderMultiplier = SliderMultiplier, OverrideSliderMultiplier = OverrideSliderMultiplier,
            SliderMultiplierOverride = SliderMultiplierOverride,
            DistancePerBeatOverride = DistancePerBeatOverride,
            DistanceSnapCollinear = DistanceSnapCollinear, SliderImportState = SliderImportState,
            RandomizeDropletStrength = RandomizeDropletStrength, RandomizeDropletSeed = RandomizeDropletSeed,
            DerandomizeFSliderDroplets = DerandomizeFSliderDroplets, DerandomizeDropletsForHardRock = DerandomizeDropletsForHardRock, RandomizeNewSliders = RandomizeNewSliders,
            SliderTickRate = SliderTickRate, LegacySliderTicks = LegacySliderTicks, DistanceSpacing = DistanceSpacing, OsuDistanceSpacing = OsuDistanceSpacing, DerandomizeDroplets = DerandomizeDroplets
        };
        copy.DistanceSnapRatios.AddRange(DistanceSnapRatios);
        copy.HitsoundOverrides.AddRange(HitsoundOverrides);
        copy.Fruits.AddRange(Fruits.Select(f => f.DeepClone()));
        copy.Tracks.AddRange(Tracks.Select(t => t.DeepClone()));
        copy.TimingPoints.AddRange(TimingPoints.Select(t => t.DeepClone()));
        copy.ImportedSliders.AddRange(ImportedSliders.Select(s => s.DeepClone()));
        copy.BananaShowers.AddRange(BananaShowers.Select(b => b.DeepClone()));
        CopyFileStateTo(copy);
        return copy;
    }

    public IEnumerable<Guid> UnchangedObjectIds(MapDocument other)
    {
        return Unchanged(Fruits, other.Fruits, f => f.Id, (a, b) => a.ContentEquals(b))
            .Concat(Unchanged(Tracks, other.Tracks, t => t.Id, (a, b) => a.ContentEquals(b)))
            .Concat(Unchanged(ImportedSliders, other.ImportedSliders, s => s.Id, (a, b) => a.ContentEquals(b)))
            .Concat(Unchanged(BananaShowers, other.BananaShowers, s => s.Id, (a, b) => a.ContentEquals(b)));

        static IEnumerable<Guid> Unchanged<T>(IEnumerable<T> before, IEnumerable<T> after,
            Func<T, Guid> id, Func<T, T, bool> equal)
        {
            var current = after.ToDictionary(id);
            foreach (var item in before)
                if (current.TryGetValue(id(item), out var restored) && equal(item, restored)) yield return id(item);
        }
    }

    public bool ContentEquals(MapDocument other)
        => ContentEquals(other, ignoreMultiplierOverride: false);

    internal bool ContentEquals(MapDocument other, bool ignoreMultiplierOverride)
        => ContentEquals(other, ignoreMultiplierOverride, ignoreOsuDistanceSpacing: false);

    public bool ContentEqualsIgnoringOsuDistanceSpacing(MapDocument other)
        => ContentEquals(other, ignoreMultiplierOverride: false, ignoreOsuDistanceSpacing: true);

    private bool ContentEquals(MapDocument other, bool ignoreMultiplierOverride, bool ignoreOsuDistanceSpacing)
    {
        if (!CollaborationReferencesEqual(other)
            || Name != other.Name || DurationMs != other.DurationMs || BeatLengthMs != other.BeatLengthMs
            || TimingOffsetMs != other.TimingOffsetMs || ApproachRate != other.ApproachRate
            || CircleSize != other.CircleSize || SliderMultiplier != other.SliderMultiplier
            || (!ignoreMultiplierOverride && (OverrideSliderMultiplier != other.OverrideSliderMultiplier
                || SliderMultiplierOverride != other.SliderMultiplierOverride))
            || DistancePerBeatOverride != other.DistancePerBeatOverride || SliderTickRate != other.SliderTickRate
            || LegacySliderTicks != other.LegacySliderTicks
            || DistanceSnapCollinear != other.DistanceSnapCollinear
            || RandomizeDropletStrength != other.RandomizeDropletStrength || RandomizeDropletSeed != other.RandomizeDropletSeed
            || DerandomizeFSliderDroplets != other.DerandomizeFSliderDroplets || DerandomizeDropletsForHardRock != other.DerandomizeDropletsForHardRock || RandomizeNewSliders != other.RandomizeNewSliders
            || !DistanceSnapRatios.SequenceEqual(other.DistanceSnapRatios)
            || DistanceSpacing != other.DistanceSpacing || (!ignoreOsuDistanceSpacing && OsuDistanceSpacing != other.OsuDistanceSpacing)
            || DerandomizeDroplets != other.DerandomizeDroplets || SliderImportState != other.SliderImportState
            || !HitsoundOverrides.SequenceEqual(other.HitsoundOverrides)
            || Fruits.Count != other.Fruits.Count || Tracks.Count != other.Tracks.Count
            || TimingPoints.Count != other.TimingPoints.Count || ImportedSliders.Count != other.ImportedSliders.Count
            || BananaShowers.Count != other.BananaShowers.Count || !FileStateEquals(other))
            return false;
        for (int i = 0; i < Fruits.Count; i++)
        {
            var a = Fruits[i]; var b = other.Fruits[i];
            if (!a.ContentEquals(b)) return false;
        }
        for (int i = 0; i < Tracks.Count; i++)
        {
            var a = Tracks[i]; var b = other.Tracks[i];
            if (!a.ContentEquals(b)) return false;
        }
        for (int i = 0; i < TimingPoints.Count; i++) if (!TimingPoints[i].ContentEquals(other.TimingPoints[i])) return false;
        for (int i = 0; i < ImportedSliders.Count; i++) if (!ImportedSliders[i].ContentEquals(other.ImportedSliders[i])) return false;
        for (int i = 0; i < BananaShowers.Count; i++) if (!BananaShowers[i].ContentEquals(other.BananaShowers[i])) return false;
        return true;
    }
}
