using L = FruitsAtelier.Localization.Strings;
using System.Globalization;

namespace FruitsAtelier.Core;

public static class OsuBeatmapReader
{
    public const int MaximumFileBytes = 32 * 1024 * 1024;

    public static MapDocument ReadFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.fileLimit"));
            return Read(File.ReadAllText(path), Path.GetFullPath(path));
        }
        catch (InvalidDataException error)
        { throw new InvalidDataException(L.Get("core.reader.fileError", Path.GetFileName(path), L.Localized(error.Message)), error); }
    }

    public static MapDocument Read(string text, string? sourcePath = null)
        => Read(text, sourcePath, inferDuration: true);

    internal static MapDocument Read(string text, string? sourcePath, bool inferDuration, OsuSliderParseCache? sliderCache = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.textLimit"));
        var document = new MapDocument { IsDemo = false, SourcePath = sourcePath is null ? null : Path.GetFullPath(sourcePath),
            ImportedContentHash = sourcePath is null || !inferDuration ? null : WorkspaceSynchronization.Digest(text) };
        var section = new OsuSection();
        document.OriginalSections.Add(section);
        using var reader = new StringReader(text);
        string? line;
        bool header = false;
        bool lazer = false;
        int lineNumber = 0, objectOrder = 0, timingOrder = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            string trimmed = line.Trim().TrimStart('\uFEFF');
            if (!header)
            {
                if (trimmed.Length == 0) continue;
                if (trimmed is not ("osu file format v7" or "osu file format v8" or "osu file format v9" or "osu file format v10" or "osu file format v11"
                    or "osu file format v12" or "osu file format v13" or "osu file format v14" or "osu file format v128"))
                    throw new InvalidDataException(L.Get("core.reader.formatVersion"));
                lazer = trimmed == "osu file format v128";
                document.LegacySliderTicks = trimmed == "osu file format v7";
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
                else if (section.Name == "HitObjects")
                {
                    if (sliderCache is null) ParseObject(document, line, objectOrder++, lazer);
                    else sliderCache.Parse(document, line, objectOrder++, lazer);
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
            { throw new InvalidDataException(L.Get("core.reader.lineError", lineNumber, ex.Message), ex); }
        }
        if (!header) throw new InvalidDataException(L.Get("core.reader.header"));
        if (Setting(document, "General", "Mode") is not (null or "0" or "2")) throw new InvalidDataException(L.Get("core.reader.mode"));
        string? unicodeTitle = Setting(document, "Metadata", "TitleUnicode");
        document.Name = !string.IsNullOrWhiteSpace(unicodeTitle) ? unicodeTitle : Setting(document, "Metadata", "Title") ?? L.Get("core.names.untitled");
        document.ApproachRate = Difficulty("ApproachRate", Difficulty("OverallDifficulty", 5));
        document.CircleSize = Difficulty("CircleSize", 5);
        document.SliderMultiplier = Difficulty("SliderMultiplier", 1.4);
        document.SliderTickRate = Difficulty("SliderTickRate", 1);
        if (Setting(document, "Editor", "DistanceSpacing") is { } spacing
            && double.TryParse(spacing, NumberStyles.Float, CultureInfo.InvariantCulture, out double distanceSpacing)
            && double.IsFinite(distanceSpacing)) document.DistanceSpacing = Math.Clamp(distanceSpacing, .1, 6);
        var firstTiming = document.TimingPoints.Where(t => t.Uninherited).OrderBy(t => t.TimeMs).FirstOrDefault()
            ?? throw new InvalidDataException(L.Get("core.reader.timingRequired"));
        document.BeatLengthMs = TimingMap.EffectiveBeatLength(firstTiming.BeatLengthMs);
        document.TimingOffsetMs = firstTiming.TimeMs;
        string? audio = Setting(document, "General", "AudioFilename");
        if (!string.IsNullOrWhiteSpace(audio))
        {
            ValidateOsuResourceReference(audio);
            document.AudioPath = sourcePath is null ? audio : ResolveResource(sourcePath, audio);
        }
        LegacySliderVelocityEditing.Capture(document);
        Validate(document);
        if (inferDuration) SetDuration(document, document.ImportedSliders.Select(s => ImportedSliderConverter.EndTimeMs(document, s)));
        return document;

        double Difficulty(string key, double fallback) => Setting(document, "Difficulty", key) is { } value ? Number(value) : fallback;
    }

    internal static void SetDuration(MapDocument document, IEnumerable<double> sliderEnds)
    {
        double last = document.Fruits.Select(f => f.TimeMs)
            .Concat(document.BananaShowers.Select(s => s.EndTimeMs))
            .Concat(sliderEnds)
            .Concat(OsuTimeline.Breaks(document).Select(b => (double)b.EndMs))
            .Concat(OsuTimeline.Bookmarks(document).Select(b => (double)b))
            .DefaultIfEmpty(0).Max();
        if (!double.IsFinite(last) || last > int.MaxValue) throw new InvalidDataException(L.Get("core.reader.endRange"));
        document.DurationMs = Math.Min(int.MaxValue, Math.Max(1000, last + 2000));
    }

    public static string? Setting(MapDocument document, string section, string key) => document.OriginalSections
        .Where(s => s.Name == section).SelectMany(s => s.Lines).Select(line => line.Split(':', 2))
        .Where(parts => parts.Length == 2 && parts[0].Trim() == key).Select(parts => parts[1].Trim()).LastOrDefault();

    internal static bool IsDataLine(string line) => line.Length != 0 && !line.StartsWith("//", StringComparison.Ordinal);
    internal static double Number(string value, bool allowNaN = false)
        => Number(value.AsSpan(), allowNaN);

    internal static double Number(ReadOnlySpan<char> value, bool allowNaN = false)
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

    internal static void ParseObject(MapDocument document, string line, int order, bool lazer = false)
    {
        string[] p = line.Split(',');
        if (p.Length < 5) throw new InvalidDataException(L.Get("core.reader.objectFields"));
        double x = Coordinate(p[0]), y = Coordinate(p[1]), time = Number(p[2]);
        int type = Integer(p[3]);
        _ = Integer(p[4]);
        int kind = type & (1 | 2 | 8 | 128);
        if (kind == 1)
            document.Fruits.Add(new Fruit { X = x, Y = y, TimeMs = time, SourceOrder = order, OriginalLine = line });
        else if (kind == 2)
        {
            if (p.Length < 8) throw new InvalidDataException(L.Get("core.reader.sliderFields"));
            ReadOnlySpan<char> curve = p[5].AsSpan();
            if (curve.Length == 0 || curve.Length > 1 && curve[1] != '|' || !"LBCP".Contains(curve[0])) throw new InvalidDataException(L.Get("core.reader.pathType"));
            var slider = new ImportedSlider
            {
                X = x, Y = y, TimeMs = time, PathType = curve[0], SpanCount = Integer(p[6]), PixelLength = Number(p[7]),
                SourceOrder = order, OriginalLine = line
            };
            int controlCount = 1;
            foreach (char character in curve) if (character == '|') controlCount++;
            if (controlCount > ImportedSlider.MaximumControlPoints) throw new InvalidDataException(L.Get("core.reader.importedParameters"));
            slider.ControlPoints.Capacity = controlCount;
            slider.ControlPoints.Add(new(x, y));
            int cursor = 2;
            while (cursor <= curve.Length)
            {
                var control = curve[cursor..];
                int separator = control.IndexOf('|');
                if (separator >= 0) control = control[..separator];
                int colon = control.IndexOf(':');
                if (colon < 0 || control[(colon + 1)..].Contains(':')) throw new InvalidDataException(L.Get("core.reader.coordinates"));
                slider.ControlPoints.Add(new(Coordinate(control[..colon]), Coordinate(control[(colon + 1)..])));
                if (separator < 0) break;
                cursor += separator + 1;
            }
            document.ImportedSliders.Add(slider);
        }
        else if (kind == 8)
        {
            if (p.Length < 6) throw new InvalidDataException(L.Get("core.reader.spinnerFields"));
            document.BananaShowers.Add(new BananaShower { TimeMs = time, EndTimeMs = Math.Max(time, Number(p[5])), SourceOrder = order, OriginalLine = line });
        }
        else throw new InvalidDataException(L.Get("core.reader.objectType", type));

        int Coordinate(ReadOnlySpan<char> value)
        {
            // v128 retains fractional coordinates; truncating them would change the imported path.
            float coordinate = (float)Number(value);
            if (lazer && coordinate != MathF.Truncate(coordinate))
                throw new InvalidDataException(L.Get("core.reader.lazerCoordinates"));
            return checked((int)coordinate);
        }
    }

    public static void Validate(MapDocument document)
    {
        if (!Enum.IsDefined(document.SliderImportState)) throw new InvalidDataException(L.Get("core.project.schema"));
        document.ValidateCollaborationReferences();
        if (!double.IsFinite(document.RandomizeDropletStrength) || document.RandomizeDropletStrength is < 0 or > 100)
            throw new InvalidDataException(L.Get("randomize.invalid"));
        if (document.DistanceSnapRatios.Count > DistanceSnap.MaximumPresets || document.DistanceSnapRatios.Any(r => !double.IsFinite(r) || r < 0))
            throw new InvalidDataException(L.Get("core.distanceSnap.range"));
        if (!double.IsFinite(document.DistanceSpacing) || document.DistanceSpacing is < .1 or > 6)
            throw new InvalidDataException(L.Get("core.distanceSpacing.range"));
        if (!double.IsFinite(document.OsuDistanceSpacing) || document.OsuDistanceSpacing is < .1 or > 3.2)
            throw new InvalidDataException(L.Get("core.distanceSpacing.range"));
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
        if (document.DistancePerBeatOverride is { } dpb && (!double.IsFinite(dpb) || dpb < 32))
            throw new InvalidDataException(L.Get("core.dpb.range"));
        if (document.SliderMultiplierOverride is { } sv && (!double.IsFinite(sv) || sv < SliderMultiplierEditing.Minimum || sv > SliderMultiplierEditing.Maximum))
            throw new InvalidDataException(L.Get("timing.sliderMultiplierRange"));
        if (document.HitsoundOverrides.Any(o => o is null || o.EventIndex < 0 || o.Sample is null
            || (o.InheritedFields & ~InheritedSampleFields.All) != 0
            || o.Sample.NormalSet is < 1 or > 3 || o.Sample.AdditionSet is < 1 or > 3 || o.Sample.Index < 0
            || o.Sample.Volume is < 0 or > 100 || (o.Sample.Additions & ~14) != 0
            || o.Sample.FileName is null || Path.IsPathRooted(o.Sample.FileName)
            || o.Sample.FileName.Replace('\\','/').Split('/').Contains("..")
            || o.Sample.FileName.Any(c => char.IsControl(c) || c is ',' or ':')))
            throw new InvalidDataException(L.Get("copier.invalidSamples"));
        if (document.HitsoundOverrides.Select(o => (o.SourceId, o.EventIndex)).Distinct().Count() != document.HitsoundOverrides.Count)
            throw new InvalidDataException(L.Get("copier.invalidSamples"));
        var ids = new HashSet<Guid>();
        foreach (var fruit in document.Fruits) { Id(fruit.Id); Time(fruit.TimeMs); X(fruit.X); if (!double.IsFinite(fruit.Y) || Math.Abs(fruit.Y) > int.MaxValue) throw new InvalidDataException(L.Get("core.reader.coordinates")); }
        foreach (var track in document.Tracks)
        {
            Id(track.Id);
            track.EndpointSnap?.Validate();
            if (track.DropletMode is { } mode && !Enum.IsDefined(mode) || track.DropletRandomization is { IsValid: false }) throw new InvalidDataException(L.Get("randomize.invalid"));
            if (track.Stack is { } stack && (track.StreamSnapDivisor is null || !stack.IsValid))
                throw new InvalidDataException(L.Get("stack.invalid"));
            if (track.StreamSnapDivisor is not (null or >= 1 and <= 16 or 24 or 32 or 48)) throw new InvalidDataException(L.Get("stream.invalidSnap"));
            if (track.Kind is not (CurveKind.Linear or CurveKind.Bezier) || track.Nodes.Count < 2)
                throw new InvalidDataException(L.Get("core.reader.incompleteCurve"));
            foreach (var node in track.Nodes) { Id(node.Id); Time(node.TimeMs); }
            foreach (double offset in track.StreamNewComboOffsets) Time(offset);
        }
        foreach (var slider in document.ImportedSliders)
        {
            slider.EndpointSnap?.Validate();
            if (slider.RandomizationBaseVelocity is { } velocity && (!double.IsFinite(velocity) || velocity is < .1 or > 10))
                throw new InvalidDataException(L.Get("core.reader.importedParameters"));
            if (slider.RandomizationSource is { } source)
            {
                if (source.RandomizationSource is not null || source.RandomizationTrack is not null || source.Id != slider.Id)
                    throw new InvalidDataException(L.Get("core.reader.importedParameters"));
                var baseline = new MapDocument();
                baseline.ImportedSliders.Add(source);
                Validate(baseline);
            }
            if (slider.RandomizationTrack is { } sourceTrack)
            {
                if (sourceTrack.Id != slider.Id || slider.RandomizationSource is null)
                    throw new InvalidDataException(L.Get("core.reader.importedParameters"));
                var baseline = new MapDocument();
                baseline.Tracks.Add(sourceTrack);
                Validate(baseline);
            }
            Id(slider.Id); Time(slider.TimeMs); X(slider.X);
            if (!double.IsFinite(slider.Y) || !double.IsFinite(slider.PixelLength) || slider.PixelLength < 0
                || slider.SpanCount is < 1 or > 10000 || !"LBCP".Contains(slider.PathType)
                || slider.ControlPoints.Count is < 1 or > ImportedSlider.MaximumControlPoints
                || slider.ControlPoints.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.GeometryY)))
                throw new InvalidDataException(L.Get("core.reader.importedParameters"));
        }
        foreach (var shower in document.BananaShowers)
        { Id(shower.Id); Time(shower.TimeMs); Time(shower.EndTimeMs); shower.Pattern?.Validate(); if (shower.EndTimeMs < shower.TimeMs) throw new InvalidDataException(L.Get("core.reader.bananaEnd")); }
        if (document.Tracks.Any(t => InvalidSv(t.SliderVelocityMultiplier) || InvalidSv(t.SliderVelocityMultiplier * document.SliderMultiplier / document.EffectiveSliderMultiplier)) || document.ImportedSliders.Any(s => InvalidSv(s.SliderVelocityMultiplier) || InvalidSv(s.ProcessedSliderVelocityMultiplier * document.SliderMultiplier / document.EffectiveSliderMultiplier)))
            throw new InvalidDataException(L.Get("legacySv.range"));
        foreach (var timing in document.TimingPoints) ValidateTiming(timing);
        var errors = CurveMath.Validate(document, int.MaxValue);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(L.Get("core.diagnostics.separator"), errors));

        void Id(Guid id) { if (id == Guid.Empty || !ids.Add(id)) throw new InvalidDataException(L.Get("core.reader.uniqueId")); }
        static bool InvalidSv(double? value) => value is { } sv && (!double.IsFinite(sv) || sv < .1 || sv > 10);
        static void Time(double time) { if (!double.IsFinite(time) || time < 0 || time > int.MaxValue) throw new InvalidDataException(L.Get("core.reader.timeRange")); }
        static void X(double x) { if (!double.IsFinite(x) || x < 0 || x > 512) throw new InvalidDataException(L.Get("core.reader.xRange")); }
    }

    private static void ValidateTiming(TimingPoint point)
    {
        if (!double.IsFinite(point.TimeMs) || point.TimeMs < -int.MaxValue || point.TimeMs > int.MaxValue
            || (point.Uninherited ? !double.IsFinite(point.BeatLengthMs)
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
