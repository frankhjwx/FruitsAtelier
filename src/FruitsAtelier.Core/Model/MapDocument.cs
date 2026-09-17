using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public readonly record struct MapPoint(double TimeMs, double X)
{
    public static MapPoint operator +(MapPoint a, MapPoint b) => new(a.TimeMs + b.TimeMs, a.X + b.X);
    public static MapPoint operator -(MapPoint a, MapPoint b) => new(a.TimeMs - b.TimeMs, a.X - b.X);
    public static MapPoint operator *(MapPoint point, double scale) => new(point.TimeMs * scale, point.X * scale);
    public static MapPoint operator *(double scale, MapPoint point) => point * scale;
    public static MapPoint Lerp(MapPoint a, MapPoint b, double u) => a * (1 - u) + b * u;
}

public sealed class Fruit
{
    internal Fruit DeepClone() => (Fruit)MemberwiseClone();
    public Guid Id { get; set; } = Guid.NewGuid();
    public double TimeMs { get; set; }
    public double X { get; set; }
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }
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

public sealed class CurveTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = L.Get("core.names.curve");
    public CurveKind Kind { get; set; } = CurveKind.Bezier;
    public int SourceOrder { get; set; } = int.MaxValue;
    public List<Anchor> Nodes { get; private set; } = new();
    internal CurveTrack DeepClone()
    {
        var copy = (CurveTrack)MemberwiseClone();
        copy.Nodes = Nodes.Select(n => n.DeepClone()).ToList();
        return copy;
    }
    public int SpanCount { get; set; } = 1;
    public string? OriginalLine { get; set; }
    public bool? CompensateTinyDroplets { get; set; }
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
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double TimeMs { get; set; }
    public char PathType { get; set; } = 'B';
    public List<SliderPathPoint> ControlPoints { get; } = new();
    public int SpanCount { get; set; } = 1;
    public double PixelLength { get; set; }

    internal ImportedSlider DeepClone()
    {
        var copy = new ImportedSlider
        {
            Id = Id, SourceOrder = SourceOrder, OriginalLine = OriginalLine, X = X, Y = Y,
            TimeMs = TimeMs, PathType = PathType, SpanCount = SpanCount, PixelLength = PixelLength
        };
        copy.ControlPoints.AddRange(ControlPoints);
        return copy;
    }

    internal bool ContentEquals(ImportedSlider other) => Id == other.Id && SourceOrder == other.SourceOrder
        && OriginalLine == other.OriginalLine && X == other.X && Y == other.Y && TimeMs == other.TimeMs
        && PathType == other.PathType && SpanCount == other.SpanCount && PixelLength == other.PixelLength
        && ControlPoints.SequenceEqual(other.ControlPoints);
}

public sealed class BananaShower
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SourceOrder { get; set; } = int.MaxValue;
    public string? OriginalLine { get; set; }
    public double TimeMs { get; set; }
    public double EndTimeMs { get; set; }

    internal BananaShower DeepClone() => (BananaShower)MemberwiseClone();
    internal bool ContentEquals(BananaShower other) => Id == other.Id && SourceOrder == other.SourceOrder
        && OriginalLine == other.OriginalLine && TimeMs == other.TimeMs && EndTimeMs == other.EndTimeMs;
}

public sealed partial class MapDocument
{
    public string Name { get; set; } = L.Get("core.names.untitled");
    public double DurationMs { get; set; } = 30_000;
    public double BeatLengthMs { get; set; } = 500;
    public double TimingOffsetMs { get; set; }
    public double ApproachRate { get; set; } = 8;
    public double CircleSize { get; set; } = 5;
    public double SliderMultiplier { get; set; } = 1.4;
    public double SliderTickRate { get; set; } = 1;
    public List<Fruit> Fruits { get; } = new();
    public List<CurveTrack> Tracks { get; } = new();
    public List<TimingPoint> TimingPoints { get; } = new();
    public List<ImportedSlider> ImportedSliders { get; } = new();
    public List<BananaShower> BananaShowers { get; } = new();

    public MapDocument DeepClone()
    {
        var copy = new MapDocument
        {
            Name = Name, DurationMs = DurationMs,
            BeatLengthMs = BeatLengthMs, TimingOffsetMs = TimingOffsetMs, ApproachRate = ApproachRate,
            CircleSize = CircleSize, SliderMultiplier = SliderMultiplier, SliderTickRate = SliderTickRate
        };
        copy.Fruits.AddRange(Fruits.Select(f => f.DeepClone()));
        copy.Tracks.AddRange(Tracks.Select(t => t.DeepClone()));
        copy.TimingPoints.AddRange(TimingPoints.Select(t => t.DeepClone()));
        copy.ImportedSliders.AddRange(ImportedSliders.Select(s => s.DeepClone()));
        copy.BananaShowers.AddRange(BananaShowers.Select(b => b.DeepClone()));
        CopyFileStateTo(copy);
        return copy;
    }

    public bool ContentEquals(MapDocument other)
    {
        if (Name != other.Name || DurationMs != other.DurationMs || BeatLengthMs != other.BeatLengthMs
            || TimingOffsetMs != other.TimingOffsetMs || ApproachRate != other.ApproachRate
            || CircleSize != other.CircleSize || SliderMultiplier != other.SliderMultiplier || SliderTickRate != other.SliderTickRate
            || Fruits.Count != other.Fruits.Count || Tracks.Count != other.Tracks.Count
            || TimingPoints.Count != other.TimingPoints.Count || ImportedSliders.Count != other.ImportedSliders.Count
            || BananaShowers.Count != other.BananaShowers.Count || !FileStateEquals(other))
            return false;
        for (int i = 0; i < Fruits.Count; i++)
        {
            var a = Fruits[i]; var b = other.Fruits[i];
            if (a.Id != b.Id || a.TimeMs != b.TimeMs || a.X != b.X || a.SourceOrder != b.SourceOrder || a.OriginalLine != b.OriginalLine) return false;
        }
        for (int i = 0; i < Tracks.Count; i++)
        {
            var a = Tracks[i]; var b = other.Tracks[i];
            if (a.Id != b.Id || a.Name != b.Name || a.Kind != b.Kind || a.SourceOrder != b.SourceOrder || a.Nodes.Count != b.Nodes.Count
                || a.SpanCount != b.SpanCount || a.OriginalLine != b.OriginalLine || a.CompensateTinyDroplets != b.CompensateTinyDroplets) return false;
            for (int j = 0; j < a.Nodes.Count; j++)
            {
                var an = a.Nodes[j]; var bn = b.Nodes[j];
                if (an.Id != bn.Id || an.TimeMs != bn.TimeMs || an.X != bn.X
                    || an.HandleIn != bn.HandleIn || an.HandleOut != bn.HandleOut || an.OutgoingKind != bn.OutgoingKind || !ControlCurve.Equal(an.OutgoingCurve, bn.OutgoingCurve)) return false;
            }
        }
        for (int i = 0; i < TimingPoints.Count; i++) if (!TimingPoints[i].ContentEquals(other.TimingPoints[i])) return false;
        for (int i = 0; i < ImportedSliders.Count; i++) if (!ImportedSliders[i].ContentEquals(other.ImportedSliders[i])) return false;
        for (int i = 0; i < BananaShowers.Count; i++) if (!BananaShowers[i].ContentEquals(other.BananaShowers[i])) return false;
        return true;
    }
}
