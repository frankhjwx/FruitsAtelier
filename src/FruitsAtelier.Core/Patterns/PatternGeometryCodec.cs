using System.Security.Cryptography;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

internal static class PatternGeometryCodec
{
    private const double UnitsPerMs = 1.92;
    private const int MaximumObjects = 100000;

    internal static byte[] Encode(MapDocument source)
    {
        var lookup = new TimingMap.Lookup(source);
        var parents = PatternPlacement.Parents(source).OrderBy(p => p.Time).ThenBy(p => p.Order).ToArray();
        if (parents.Length is 0 or > MaximumObjects) throw Invalid();
        double origin = parents[0].Time, scale = 1000 / lookup.At(origin).BeatLengthMs;
        double end = PatternPlacement.EndTime(source, lookup);
        var red = source.TimingPoints.Where(p => p.Uninherited && p.TimeMs > origin && p.TimeMs <= end)
            .GroupBy(p => p.TimeMs).OrderBy(g => g.Key).Select(g => g.OrderBy(p => p.SourceOrder).Last()).ToArray();
        var fruits = source.Fruits.ToDictionary(p => p.Id);
        var tracks = source.Tracks.ToDictionary(p => p.Id);
        var sliders = source.ImportedSliders.ToDictionary(p => p.Id);
        var showers = source.BananaShowers.ToDictionary(p => p.Id);
        var first = Write(1);
        return first.Grid > 1 ? Write(first.Grid).Bytes : first.Bytes;

        (byte[] Bytes, long Grid) Write(long grid)
        {
            using var stream = new MemoryStream();
            using var binary = new BinaryWriter(stream);
            var writer = new GeometryWriter(binary, grid);
            binary.Write((byte)2); writer.Count(grid); writer.Count(red.Length);
            foreach (var point in red) { writer.Time((point.TimeMs - origin) * scale); writer.Time(point.BeatLengthMs * scale); }
            writer.Count(parents.Length);
            foreach (var parent in parents)
            {
                if (fruits.TryGetValue(parent.Id, out var fruit))
                {
                    binary.Write((byte)0); writer.Point((fruit.TimeMs - origin) * scale, fruit.X);
                }
                else if (tracks.TryGetValue(parent.Id, out var original))
                {
                    binary.Write((byte)1);
                    var track = original.DeepClone(); TimingEditing.TransformTrack(track, origin, 0, scale);
                    writer.Count(track.SpanCount - 1); writer.Count(track.StreamSnapDivisor ?? 0);
                    binary.Write(track.Stack is not null);
                    if (track.Stack is { } stack)
                    {
                        binary.Write(stack.StartLeft); writer.Count(stack.Points.Count);
                        foreach (var point in stack.Points) { writer.Number(point.Progress); writer.Number(point.Distance); }
                        writer.Count(stack.FruitAdjustments.Count);
                        foreach (var point in stack.FruitAdjustments) { writer.Number(point.Progress); writer.Number(point.Offset); }
                    }
                    writer.Count(track.Nodes.Count);
                    for (int i = 0; i < track.Nodes.Count; i++)
                    {
                        var node = track.Nodes[i]; writer.Point(node.TimeMs, node.X);
                        if (i == track.Nodes.Count - 1) continue;
                        if (node.OutgoingCurve is { } curve)
                        {
                            binary.Write((byte)(curve.Kind == ControlCurveKind.CircularArc ? 3 : 2));
                            if (curve.Kind == ControlCurveKind.CircularArc) writer.Number(curve.ReferenceScale);
                            writer.Count(curve.Controls.Count);
                            foreach (var control in curve.Controls) writer.Vector(control.Offset);
                        }
                        else if (CurveMath.SegmentKind(track, i) == CurveKind.Linear) binary.Write((byte)0);
                        else
                        {
                            binary.Write((byte)1); writer.Vector(node.HandleOut); writer.Vector(track.Nodes[i + 1].HandleIn);
                        }
                    }
                }
                else if (sliders.TryGetValue(parent.Id, out var slider))
                {
                    binary.Write((byte)2); writer.Point((slider.TimeMs - origin) * scale, slider.X);
                    writer.Number(slider.Y); binary.Write((byte)slider.PathType); writer.Number(slider.PixelLength);
                    writer.Count(slider.SpanCount - 1);
                    writer.Number(source.EffectiveSliderMultiplier);
                    writer.Number(Math.Clamp((float)(100 / ImportedSliderConverter.SliderVelocityAt(source, slider, lookup)), 10, 1000));
                    writer.Count(slider.ControlPoints.Count);
                    double x = slider.X, y = slider.Y;
                    foreach (var point in slider.ControlPoints)
                    { writer.Number(point.X - x); writer.Number(point.GeometryY - y); x = point.X; y = point.GeometryY; }
                }
                else
                {
                    var shower = showers[parent.Id]; binary.Write((byte)3);
                    writer.Time((shower.TimeMs - origin) * scale); writer.Time((shower.EndTimeMs - shower.TimeMs) * scale);
                    binary.Write(shower.Pattern is not null);
                    if (shower.Pattern is { } pattern)
                    { writer.Count(pattern.LengthNumerator); writer.Count(pattern.LengthDivisor); writer.Count(pattern.GapNumerator); writer.Count(pattern.GapDivisor); }
                }
            }
            binary.Flush(); return (stream.ToArray(), Math.Max(1, writer.Grid));
        }
    }

    internal static PatternSnapshot Decode(byte[] bytes, string name)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var binary = new BinaryReader(stream);
        if (binary.ReadByte() != 2) throw Invalid();
        long grid = binary.Read7BitEncodedInt64();
        if (grid < 1 || grid > 1000000000000) throw Invalid();
        var reader = new GeometryReader(binary, grid);
        var document = new MapDocument { IsDemo = false, Name = name, BeatLengthMs = 1000 };
        int changes = reader.Count(MaximumObjects);
        if (changes > 0) document.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 1000, SourceOrder = -1 });
        for (int i = 0; i < changes; i++) document.TimingPoints.Add(new() { TimeMs = reader.Time(), BeatLengthMs = reader.Time(), SourceOrder = i });
        int count = reader.Count(MaximumObjects);
        if (count == 0) throw Invalid();
        var native = new List<(ImportedSlider Slider, double Basis)>();
        for (int i = 0; i < count; i++)
        {
            switch (binary.ReadByte())
            {
                case 0:
                    var fruit = reader.Point(); document.Fruits.Add(new() { TimeMs = fruit.TimeMs, X = fruit.X, SourceOrder = i }); break;
                case 1:
                    var track = new CurveTrack { SourceOrder = i, Kind = CurveKind.Linear, SpanCount = reader.Count(8999) + 1 };
                    int divisor = reader.Count(192); track.StreamSnapDivisor = divisor == 0 ? null : divisor;
                    if (binary.ReadBoolean())
                    {
                        var stack = new StackEnvelope { StartLeft = binary.ReadBoolean(), Points = [] };
                        int points = reader.Count(64);
                        for (int j = 0; j < points; j++) stack.Points.Add(new(reader.Number(), reader.Number()));
                        int adjustments = reader.Count(MaximumObjects);
                        for (int j = 0; j < adjustments; j++) stack.FruitAdjustments.Add(new(reader.Number(), reader.Number()));
                        track.Stack = stack;
                    }
                    int nodes = reader.Count(MaximumObjects);
                    if (nodes < 2) throw Invalid();
                    MapPoint incoming = default;
                    for (int j = 0; j < nodes; j++)
                    {
                        var point = reader.Point(); var node = new Anchor { TimeMs = point.TimeMs, X = point.X };
                        // A segment's incoming handle belongs to the next anchor.
                        node.HandleIn = incoming; incoming = default;
                        track.Nodes.Add(node);
                        if (j == nodes - 1) continue;
                        byte segmentKind = binary.ReadByte();
                        switch (segmentKind)
                        {
                            case 0: node.OutgoingKind = CurveKind.Linear; break;
                            case 1: node.OutgoingKind = CurveKind.Bezier; node.HandleOut = reader.Vector(); incoming = reader.Vector(); break;
                            case 2: case 3:
                                var curve = new ControlCurve { Kind = segmentKind == 3 ? ControlCurveKind.CircularArc : ControlCurveKind.Bezier };
                                if (segmentKind == 3) curve.ReferenceScale = reader.Number();
                                int controls = reader.Count(ControlCurveMath.MaximumControls);
                                for (int k = 0; k < controls; k++) curve.Controls.Add(new() { Offset = reader.Vector() });
                                node.OutgoingCurve = curve; break;
                            default: throw Invalid();
                        }
                    }
                    document.Tracks.Add(track); break;
                case 2:
                    var head = reader.Point();
                    var slider = new ImportedSlider { TimeMs = head.TimeMs, X = head.X, Y = reader.Number(), PathType = (char)binary.ReadByte(),
                        PixelLength = reader.Number(), SpanCount = reader.Count(8999) + 1, SourceOrder = i, PreserveNativeGeometry = true };
                    double basis = reader.Number(), magnitude = reader.Number();
                    if (basis <= 0 || magnitude is < 10 or > 1000) throw Invalid();
                    slider.SliderVelocityMultiplier = 100 / magnitude;
                    int controlsCount = reader.Count(ImportedSlider.MaximumControlPoints);
                    double x = slider.X, y = slider.Y;
                    for (int j = 0; j < controlsCount; j++) { x += reader.Number(); y += reader.Number(); slider.ControlPoints.Add(new(x, y)); }
                    native.Add((slider, basis)); document.ImportedSliders.Add(slider); break;
                case 3:
                    double start = reader.Time(), length = reader.Time();
                    var shower = new BananaShower { TimeMs = start, EndTimeMs = start + length, SourceOrder = i };
                    if (binary.ReadBoolean()) shower.Pattern = new(reader.Count(100000), reader.Count(48), reader.Count(100000), reader.Count(48));
                    document.BananaShowers.Add(shower); break;
                default: throw Invalid();
            }
        }
        if (stream.Position != stream.Length) throw Invalid();
        if (native.Count > 0)
        {
            double basis = native[0].Basis;
            if (native.Any(p => p.Basis != basis)) throw Invalid();
            document.SliderMultiplier = basis;
            document.SliderImportState = SliderImportState.Native;
        }
        document.DurationMs = Math.Max(1, PatternPlacement.EndTime(document, new TimingMap.Lookup(document)));
        OsuBeatmapReader.Validate(document);
        var errors = CurveMath.Validate(document);
        if (errors.Count > 0) throw Invalid();
        return new(new Guid(SHA256.HashData(bytes).AsSpan(0, 16)), name, new("", "", "", "", 0), document);
    }

    private sealed class GeometryWriter(BinaryWriter binary, long step)
    {
        internal long Grid;
        private double time, x;
        internal void Count(long value) => binary.Write7BitEncodedInt64(value);
        internal void Number(double value)
        {
            double rounded = Math.Round(value);
            if (double.IsFinite(value) && Math.Abs(value - rounded) <= 1e-7 && Math.Abs(rounded) <= 1000000000000)
            {
                long integer = (long)rounded; Count(((integer << 1) ^ (integer >> 63)) << 1);
            }
            else { if (!double.IsFinite(value)) throw Invalid(); binary.Write((byte)1); binary.Write(value); }
        }
        internal void Time(double value)
        {
            double ticks = value * UnitsPerMs, rounded = Math.Round(ticks);
            if (Math.Abs(ticks - rounded) <= 1e-7 && Math.Abs(rounded) <= 1000000000000)
                Grid = Gcd(Grid, Math.Abs((long)rounded));
            else Grid = 1;
            Number(ticks / step);
        }
        internal void Point(double nextTime, double nextX) { Time(nextTime - time); Number(nextX - x); time = nextTime; x = nextX; }
        internal void Vector(MapPoint value) { Time(value.TimeMs); Number(value.X); }
        private static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return a; }
    }

    private sealed class GeometryReader(BinaryReader binary, long step)
    {
        private double time, x;
        internal int Count(int maximum)
        {
            long value = binary.Read7BitEncodedInt64();
            return value < 0 || value > maximum ? throw Invalid() : (int)value;
        }
        internal double Number()
        {
            long value = binary.Read7BitEncodedInt64();
            if (value < 0 || value > 4000000000000) throw Invalid();
            double result;
            if (value == 1) result = binary.ReadDouble();
            else
            {
                if ((value & 1) != 0) throw Invalid();
                long integer = value >> 1; result = (integer >> 1) ^ -(integer & 1);
            }
            return double.IsFinite(result) ? result : throw Invalid();
        }
        internal double Time() => Number() * step / UnitsPerMs;
        internal MapPoint Point() { time += Time(); x += Number(); return new(time, x); }
        internal MapPoint Vector() => new(Time(), Number());
    }

    private static InvalidDataException Invalid() => new(L.Get("core.pattern.invalid"));
}
