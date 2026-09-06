using L = FruitsAtelier.Localization.Strings;
using System.Globalization;

namespace FruitsAtelier.Core;

public static class OsuBeatmapReader
{
    public const int MaximumFileBytes = 32 * 1024 * 1024;

    public static MapDocument ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.fileLimit"));
        return Read(File.ReadAllText(path), Path.GetFullPath(path));
    }

    public static MapDocument Read(string text, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.textLimit"));
        var document = new MapDocument { IsDemo = false, SourcePath = sourcePath is null ? null : Path.GetFullPath(sourcePath) };
        var section = new OsuSection();
        document.OriginalSections.Add(section);
        using var reader = new StringReader(text);
        string? line;
        bool header = false;
        int lineNumber = 0, objectOrder = 0, timingOrder = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            string trimmed = line.Trim().TrimStart('\uFEFF');
            if (!header)
            {
                if (trimmed.Length == 0) continue;
                if (trimmed != "osu file format v14")
                    throw new InvalidDataException(L.Get("core.reader.formatVersion"));
                header = true;
                continue;
            }
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = new OsuSection { Name = trimmed[1..^1] };
                document.OriginalSections.Add(section);
                continue;
            }
            section.Lines.Add(line);
            if (!IsDataLine(trimmed)) continue;
            try
            {
                if (section.Name == "TimingPoints") document.TimingPoints.Add(ParseTiming(line, timingOrder++));
                else if (section.Name == "HitObjects") ParseObject(document, line, objectOrder++);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
            { throw new InvalidDataException(L.Get("core.reader.lineError", lineNumber, ex.Message), ex); }
        }
        if (!header) throw new InvalidDataException(L.Get("core.reader.header"));
        if (Setting(document, "General", "Mode") != "2") throw new InvalidDataException(L.Get("core.reader.mode"));
        string? unicodeTitle = Setting(document, "Metadata", "TitleUnicode");
        document.Name = !string.IsNullOrWhiteSpace(unicodeTitle) ? unicodeTitle : Setting(document, "Metadata", "Title") ?? L.Get("core.names.untitled");
        document.ApproachRate = Difficulty("ApproachRate", Difficulty("OverallDifficulty", 5));
        document.CircleSize = Difficulty("CircleSize", 5);
        document.SliderMultiplier = Difficulty("SliderMultiplier", 1.4);
        document.SliderTickRate = Difficulty("SliderTickRate", 1);
        var firstTiming = document.TimingPoints.Where(t => t.Uninherited).OrderBy(t => t.TimeMs).FirstOrDefault()
            ?? throw new InvalidDataException(L.Get("core.reader.timingRequired"));
        document.BeatLengthMs = firstTiming.BeatLengthMs;
        document.TimingOffsetMs = firstTiming.TimeMs;
        string? audio = Setting(document, "General", "AudioFilename");
        if (!string.IsNullOrWhiteSpace(audio))
        {
            ValidateOsuResourceReference(audio);
            document.AudioPath = sourcePath is null ? audio : ResolveResource(sourcePath, audio);
        }
        Validate(document);
        double last = document.Fruits.Select(f => f.TimeMs)
            .Concat(document.BananaShowers.Select(s => s.EndTimeMs))
            .Concat(document.ImportedSliders.Select(s => s.TimeMs + ImportedSliderConverter.DurationMs(document, s)))
            .DefaultIfEmpty(0).Max();
        if (!double.IsFinite(last) || last > int.MaxValue) throw new InvalidDataException(L.Get("core.reader.endRange"));
        document.DurationMs = Math.Min(int.MaxValue, Math.Max(1000, last + 2000));
        return document;

        double Difficulty(string key, double fallback) => Setting(document, "Difficulty", key) is { } value ? Number(value) : fallback;
    }

    public static string? Setting(MapDocument document, string section, string key) => document.OriginalSections
        .Where(s => s.Name == section).SelectMany(s => s.Lines).Select(line => line.Split(':', 2))
        .Where(parts => parts.Length == 2 && parts[0].Trim() == key).Select(parts => parts[1].Trim()).LastOrDefault();

    internal static bool IsDataLine(string line) => line.Length != 0 && !line.StartsWith("//", StringComparison.Ordinal);
    internal static double Number(string value, bool allowNaN = false)
    {
        double result = double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(result) && !(allowNaN && double.IsNaN(result))) throw new InvalidDataException(L.Get("core.reader.finiteNumber"));
        return result;
    }
    internal static int Integer(string value) => int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
    internal static int LegacyCoordinate(string value) => checked((int)(float)Number(value));

    internal static TimingPoint ParseTiming(string line, int order)
    {
        string[] p = line.Split(',');
        if (p.Length < 2) throw new InvalidDataException(L.Get("core.reader.timingFields"));
        bool uninherited = p.Length < 7 || Integer(p[6]) == 1;
        var point = new TimingPoint
        {
            TimeMs = Number(p[0]), BeatLengthMs = Number(p[1], !uninherited),
            Meter = p.Length > 2 ? Integer(p[2]) : 4, SampleSet = p.Length > 3 ? Integer(p[3]) : 0,
            SampleIndex = p.Length > 4 ? Integer(p[4]) : 0, Volume = p.Length > 5 ? Integer(p[5]) : 100,
            Uninherited = uninherited, Effects = p.Length > 7 ? Integer(p[7]) : 0,
            SourceOrder = order, OriginalLine = line
        };
        ValidateTiming(point);
        return point;
    }

    internal static void ParseObject(MapDocument document, string line, int order)
    {
        string[] p = line.Split(',');
        if (p.Length < 5) throw new InvalidDataException(L.Get("core.reader.objectFields"));
        double x = LegacyCoordinate(p[0]), y = LegacyCoordinate(p[1]), time = Number(p[2]);
        int type = Integer(p[3]);
        _ = Integer(p[4]);
        int kind = type & (1 | 2 | 8 | 128);
        if (kind == 1)
            document.Fruits.Add(new Fruit { X = x, TimeMs = time, SourceOrder = order, OriginalLine = line });
        else if (kind == 2)
        {
            if (p.Length < 8) throw new InvalidDataException(L.Get("core.reader.sliderFields"));
            string[] curve = p[5].Split('|');
            if (curve[0].Length != 1 || !"LBCP".Contains(curve[0][0])) throw new InvalidDataException(L.Get("core.reader.pathType"));
            var slider = new ImportedSlider
            {
                X = x, Y = y, TimeMs = time, PathType = curve[0][0], SpanCount = Integer(p[6]), PixelLength = Number(p[7]),
                SourceOrder = order, OriginalLine = line
            };
            slider.ControlPoints.Add(new(x, y));
            foreach (string control in curve.Skip(1))
            {
                string[] coords = control.Split(':');
                if (coords.Length != 2) throw new InvalidDataException(L.Get("core.reader.coordinates"));
                slider.ControlPoints.Add(new(LegacyCoordinate(coords[0]), LegacyCoordinate(coords[1])));
            }
            document.ImportedSliders.Add(slider);
        }
        else if (kind == 8)
        {
            if (p.Length < 6) throw new InvalidDataException(L.Get("core.reader.spinnerFields"));
            document.BananaShowers.Add(new BananaShower { TimeMs = time, EndTimeMs = Number(p[5]), SourceOrder = order, OriginalLine = line });
        }
        else throw new InvalidDataException(L.Get("core.reader.objectType", type));
    }

    public static void Validate(MapDocument document)
    {
        if (!double.IsFinite(document.DurationMs) || document.DurationMs <= 0 || document.DurationMs > int.MaxValue)
            throw new InvalidDataException(L.Get("core.reader.projectDuration"));
        if (!double.IsFinite(document.BeatLengthMs) || document.BeatLengthMs <= 0 || !double.IsFinite(document.TimingOffsetMs))
            throw new InvalidDataException(L.Get("core.reader.defaultTiming"));
        if (!double.IsFinite(document.ApproachRate) || document.ApproachRate is < 0 or > 10
            || !double.IsFinite(document.CircleSize) || document.CircleSize is < 0 or > 10)
            throw new InvalidDataException(L.Get("core.reader.difficultyRange"));
        if (!double.IsFinite(document.SliderMultiplier) || document.SliderMultiplier <= 0
            || !double.IsFinite(document.SliderTickRate) || document.SliderTickRate <= 0)
            throw new InvalidDataException(L.Get("core.reader.sliderSettings"));
        var ids = new HashSet<Guid>();
        foreach (var fruit in document.Fruits) { Id(fruit.Id); Time(fruit.TimeMs); X(fruit.X); }
        foreach (var track in document.Tracks)
        {
            Id(track.Id);
            if (track.Kind is not (CurveKind.Linear or CurveKind.Bezier) || track.Nodes.Count < 2)
                throw new InvalidDataException(L.Get("core.reader.incompleteCurve"));
            foreach (var node in track.Nodes) { Id(node.Id); Time(node.TimeMs); X(node.X); }
        }
        foreach (var slider in document.ImportedSliders)
        {
            Id(slider.Id); Time(slider.TimeMs); X(slider.X);
            if (!double.IsFinite(slider.Y) || !double.IsFinite(slider.PixelLength) || slider.PixelLength < 0
                || slider.SpanCount is < 1 or > 10000 || !"LBCP".Contains(slider.PathType)
                || slider.ControlPoints.Count is < 1 or > 65536
                || slider.ControlPoints.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.GeometryY)))
                throw new InvalidDataException(L.Get("core.reader.importedParameters"));
        }
        foreach (var shower in document.BananaShowers)
        { Id(shower.Id); Time(shower.TimeMs); Time(shower.EndTimeMs); if (shower.EndTimeMs < shower.TimeMs) throw new InvalidDataException(L.Get("core.reader.bananaEnd")); }
        foreach (var timing in document.TimingPoints) ValidateTiming(timing);
        var curveOnly = document.DeepClone();
        curveOnly.DurationMs = int.MaxValue;
        var errors = CurveMath.Validate(curveOnly);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(L.Get("core.diagnostics.separator"), errors));

        void Id(Guid id) { if (id == Guid.Empty || !ids.Add(id)) throw new InvalidDataException(L.Get("core.reader.uniqueId")); }
        static void Time(double time) { if (!double.IsFinite(time) || time < 0 || time > int.MaxValue) throw new InvalidDataException(L.Get("core.reader.timeRange")); }
        static void X(double x) { if (!double.IsFinite(x) || x < 0 || x > 512) throw new InvalidDataException(L.Get("core.reader.xRange")); }
    }

    private static void ValidateTiming(TimingPoint point)
    {
        if (!double.IsFinite(point.TimeMs) || point.TimeMs < -int.MaxValue || point.TimeMs > int.MaxValue
            || (point.Uninherited ? !double.IsFinite(point.BeatLengthMs) || point.BeatLengthMs <= 0
                : double.IsInfinity(point.BeatLengthMs)) || point.Meter <= 0 || point.Volume is < 0 or > 100)
            throw new InvalidDataException(L.Get("core.reader.timingValues"));
    }

    internal static string ResolveResource(string file, string resource) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, resource.Replace('/', Path.DirectorySeparatorChar)));

    internal static void ValidateOsuResourceReference(string resource)
    {
        string normalized = resource.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(resource) || normalized.Contains(':')
            || normalized.Split('/').Any(part => part == ".." || part.EndsWith(' ') || part.Length > 1 && part.EndsWith('.'))
            || normalized.Any(c => c < 32))
            throw new InvalidDataException(L.Get("core.reader.audioPath"));
    }
}
