using L = FruitsAtelier.Localization.Strings;
using System.Globalization;
using System.Text;

namespace FruitsAtelier.Core;

public sealed class OsuWriteResult
{
    public required string Text { get; init; }
    public required MapDocument ReadBack { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public double MaxTimeQuantizationMs { get; init; }
    public double MaxCoordinateQuantization { get; init; }
    public double MaxConvertedTimeErrorMs { get; init; }
    public double MaxConvertedXError { get; init; }
    public bool ObjectSequenceMatches { get; init; }
}

public static class OsuBeatmapWriter
{
    public static OsuWriteResult WriteFile(MapDocument document, string destination, bool compensateTinyDroplets = true)
    {
        if (document.SourcePath is not null && string.Equals(Path.GetFullPath(destination), Path.GetFullPath(document.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.Get("core.writer.sourceOverwrite"));
        var result = Serialize(document, compensateTinyDroplets);
        AtomicFile.Write(destination, result.Text);
        return result;
    }

    public static OsuWriteResult Serialize(MapDocument document, bool compensateTinyDroplets = true)
    {
        OsuBeatmapReader.Validate(document);
        var converted = CatchStreamConverter.Convert(document, compensateTinyDroplets);
        if (!converted.Success) throw new InvalidDataException(L.Get("core.writer.incompletePrefix") + string.Join(L.Get("core.diagnostics.separator"), converted.Diagnostics));
        var generated = converted.Sliders.Where(s => document.Tracks.Any(t => t.Id == s.SourceId)).ToArray();
        if (generated.Length != document.Tracks.Count(t => t.StreamSnapDivisor is null)) throw new InvalidDataException(L.Get("core.writer.sliderCount"));
        var diagnostics = converted.Diagnostics.ToList();
        double maxTime = 0, maxCoordinate = 0;
        var lines = new List<(double Time, int Order, Guid SourceId, string Text)>();
        var streamIds = document.Tracks.Where(t => t.StreamSnapDivisor is not null).ToDictionary(t => t.Id);
        foreach (var item in converted.Objects.Where(o => streamIds.ContainsKey(o.SourceId)))
        {
            var track = streamIds[item.SourceId];
            lines.Add((item.TimeMs, track.SourceOrder, track.Id,
                SliderFruitStream.FruitLine(track, item.EventIndex, Coordinate(item.X), Time(item.TimeMs))));
        }
        foreach (var fruit in document.Fruits)
        {
            string[] p = fruit.OriginalLine?.Split(',') ?? ["0", "192", "0", "1", "0", "0:0:0:0:"];
            // Preserve all original flags, sample columns and untouched numeric spelling.
            if (fruit.OriginalLine is null || OsuBeatmapReader.LegacyCoordinate(p[0]) != fruit.X) p[0] = Coordinate(fruit.X);
            if (fruit.OriginalLine is null || OsuBeatmapReader.Number(p[2]) != fruit.TimeMs) p[2] = Time(fruit.TimeMs);
            lines.Add((fruit.TimeMs, fruit.SourceOrder, fruit.Id, string.Join(',', p)));
        }
        foreach (var slider in document.ImportedSliders)
        {
            if (slider.OriginalLine is not null)
            {
                var original = new MapDocument();
                OsuBeatmapReader.ParseObject(original, slider.OriginalLine, slider.SourceOrder);
                if (original.ImportedSliders.Count != 1) throw new InvalidDataException(L.Get("core.writer.importedText"));
                original.ImportedSliders[0].Id = slider.Id;
                if (!slider.ContentEquals(original.ImportedSliders[0])) throw new InvalidDataException(L.Get("core.writer.importedChanged"));
                lines.Add((slider.TimeMs, slider.SourceOrder, slider.Id, slider.OriginalLine));
            }
            else
            {
                string path = slider.PathType + "|" + string.Join('|', slider.ControlPoints.Skip(1).Select(p => Coordinate(p.X) + ":" + Coordinate(p.GeometryY)));
                lines.Add((slider.TimeMs, slider.SourceOrder, slider.Id, $"{Coordinate(slider.X)},{Coordinate(slider.Y)},{Time(slider.TimeMs)},2,0,{path},{slider.SpanCount},{Number(slider.PixelLength)},{DefaultEdges(slider.SpanCount, "0")},{DefaultEdges(slider.SpanCount, "0:0")},0:0:0:0:"));
            }
        }
        foreach (var shower in document.BananaShowers)
        {
            if (shower.OriginalLine is not null)
            {
                string[] values = shower.OriginalLine.Split(',');
                if (values.Length < 6) throw new InvalidDataException(L.Get("core.writer.bananaText"));
                if (OsuBeatmapReader.Number(values[2]) != shower.TimeMs) values[2] = Time(shower.TimeMs);
                if (OsuBeatmapReader.Number(values[5]) != shower.EndTimeMs) values[5] = Time(shower.EndTimeMs);
                lines.Add((shower.TimeMs, shower.SourceOrder, shower.Id, string.Join(',', values)));
            }
            else lines.Add((shower.TimeMs, shower.SourceOrder, shower.Id, $"256,192,{Time(shower.TimeMs)},8,0,{Time(shower.EndTimeMs)},0:0:0:0:"));
        }
        foreach (var slider in generated)
        {
            if (slider.Path.Count < 2) throw new InvalidDataException(L.Get("core.writer.minimumPath"));
            var track = document.Tracks.Single(t => t.Id == slider.SourceId);
            if (slider.SpanCount != track.SpanCount) throw new InvalidDataException(L.Get("core.writer.spanMismatch"));
            var first = slider.Path[0];
            string path = "L|" + string.Join('|', slider.Path.Skip(1).Select(p => Coordinate(p.X) + ":" + Coordinate(p.GeometryY)));
            string[] values = track.OriginalLine?.Split(',') ?? ["0", "192", "0", "2", "0", "L", "1", "0", "0|0", "0:0|0:0", "0:0:0:0:"];
            if (values.Length < 8 || (OsuBeatmapReader.Integer(values[3]) & (1 | 2 | 8 | 128)) != 2)
                throw new InvalidDataException(L.Get("core.writer.originalObject"));
            int originalSpans = OsuBeatmapReader.Integer(values[6]);
            if (values.Length < 11) Array.Resize(ref values, 11);
            values[0] = Coordinate(first.X); values[1] = Coordinate(first.GeometryY); values[2] = Time(slider.StartTimeMs);
            values[5] = path; values[6] = slider.SpanCount.ToString(CultureInfo.InvariantCulture); values[7] = Number(slider.Length);
            if (track.OriginalLine is null || originalSpans != slider.SpanCount)
            {
                values[8] = ResizeEdges(values[8], slider.SpanCount, "0");
                values[9] = ResizeEdges(values[9], slider.SpanCount, "0:0");
                if (track.OriginalLine is not null)
                    diagnostics.Add(L.Get("core.writer.spanSamples", track.Name, originalSpans, slider.SpanCount));
            }
            values[10] ??= "0:0:0:0:";
            lines.Add((slider.StartTimeMs, track.SourceOrder, slider.SourceId, string.Join(',', values)));
        }
        var timing = BuildTiming(document, generated);
        var output = document.DeepClone();
        SetNumber(output, "Editor", "DistanceSpacing", document.DistanceSpacing);
        Set(output, "General", "Mode", "2");
        string? originalAudio = OsuBeatmapReader.Setting(output, "General", "AudioFilename");
        if (document.AudioPath is not null)
        {
            string? originalTarget = originalAudio is null ? null : document.SourcePath is null ? originalAudio
                : OsuBeatmapReader.ResolveResource(document.SourcePath, originalAudio);
            if (!string.Equals(originalTarget, document.AudioPath, StringComparison.OrdinalIgnoreCase))
                Set(output, "General", "AudioFilename", Path.GetFileName(document.AudioPath));
        }
        if (OsuBeatmapReader.Setting(output, "Metadata", "Title") is null) Set(output, "Metadata", "Title", document.Name);
        if (OsuBeatmapReader.Setting(output, "Metadata", "Version") is null) Set(output, "Metadata", "Version", L.Get("core.names.defaultDifficulty"));
        SetNumber(output, "Difficulty", "ApproachRate", document.ApproachRate);
        SetNumber(output, "Difficulty", "CircleSize", document.CircleSize);
        SetNumber(output, "Difficulty", "SliderMultiplier", document.SliderMultiplier);
        SetNumber(output, "Difficulty", "SliderTickRate", document.SliderTickRate);
        ReplaceData(output, "TimingPoints", timing.Select(TimingLine));
        // Rounding is monotone; sorting before it retains current parent order when distinct times collapse.
        var orderedLines = lines.OrderBy(l => l.Time).ThenBy(l => l.Order).ToArray();
        ReplaceData(output, "HitObjects", orderedLines.Select(l => l.Text));
        var text = new StringBuilder("osu file format v14\r\n");
        foreach (var section in output.OriginalSections)
        {
            if (section.Name.Length != 0) text.Append('[').Append(section.Name).Append("]\r\n");
            foreach (string line in section.Lines) text.Append(line).Append("\r\n");
        }
        string serialized = text.ToString();
        var readBack = OsuBeatmapReader.Read(serialized, document.SourcePath);
        var reconverted = CatchStreamConverter.Convert(readBack, compensateTinyDroplets);
        if (!reconverted.Success) throw new InvalidDataException(L.Get("core.writer.readBackPrefix") + string.Join(L.Get("core.diagnostics.separator"), reconverted.Diagnostics));
        var sourceIds = readBack.Fruits.Select(f => (f.Id, f.SourceOrder))
            .Concat(readBack.ImportedSliders.Select(s => (s.Id, s.SourceOrder)))
            .Concat(readBack.BananaShowers.Select(s => (s.Id, s.SourceOrder)))
            .ToDictionary(p => p.Id, p => orderedLines[p.SourceOrder].SourceId);
        bool matches = converted.Objects.Count == reconverted.Objects.Count
            && converted.Objects.Zip(reconverted.Objects).All(p => p.First.Kind == p.Second.Kind
                && p.First.SourceId == sourceIds[p.Second.SourceId]
                && (streamIds.ContainsKey(p.First.SourceId) || p.First.EventIndex == p.Second.EventIndex));
        double timeError = 0, xError = 0;
        if (matches)
        {
            foreach (var pair in converted.Objects.Zip(reconverted.Objects))
            {
                timeError = Math.Max(timeError, Math.Abs(pair.First.TimeMs - pair.Second.TimeMs));
                xError = Math.Max(xError, Math.Abs(pair.First.X - pair.Second.X));
            }
        }
        else
        {
            timeError = xError = double.NaN;
            diagnostics.Add(L.Get("core.writer.sequenceChanged", converted.Objects.Count, reconverted.Objects.Count));
        }
        diagnostics.Add(L.Get("core.writer.rounding", Number(maxTime), Number(maxCoordinate)));
        if (matches) diagnostics.Add(L.Get("core.writer.readBackError", Number(timeError), Number(xError)));
        if (document.AudioPath is not null || document.OriginalSections.Any(s => s.Name == "Events" && s.Lines.Any(l => OsuBeatmapReader.IsDataLine(l.Trim()))))
            diagnostics.Add(L.Get("core.writer.resources"));
        return new OsuWriteResult
        {
            Text = serialized, ReadBack = readBack, Diagnostics = diagnostics,
            MaxTimeQuantizationMs = maxTime, MaxCoordinateQuantization = maxCoordinate,
            MaxConvertedTimeErrorMs = timeError, MaxConvertedXError = xError, ObjectSequenceMatches = matches
        };

        string Coordinate(double value) { double rounded = Round(value); maxCoordinate = Math.Max(maxCoordinate, Math.Abs(rounded - value)); return Number(rounded); }
        string Time(double value) { double rounded = Round(value); maxTime = Math.Max(maxTime, Math.Abs(rounded - value)); return Number(rounded); }
    }

    private static List<TimingPoint> BuildTiming(MapDocument document, IReadOnlyList<GeneratedSlider> generated)
    {
        var original = document.TimingPoints.Select(t => t.DeepClone()).ToList();
        if (original.Count == 0) original.Add(new TimingPoint { TimeMs = document.TimingOffsetMs, BeatLengthMs = document.BeatLengthMs });
        if (generated.Count == 0) return original;
        if (original.Zip(original.Skip(1)).Any(p => p.First.TimeMs > p.Second.TimeMs))
            throw new InvalidDataException(L.Get("core.writer.timingOrder"));
        var emitted = new MapDocument { BeatLengthMs = document.BeatLengthMs, TimingOffsetMs = document.TimingOffsetMs };
        emitted.TimingPoints.AddRange(original);
        foreach (var group in generated.GroupBy(s => Round(s.StartTimeMs)).OrderBy(g => g.Key))
        {
            double start = group.Key;
            var first = group.First();
            if (group.Any(s => Math.Abs(s.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9))
                throw new InvalidDataException(L.Get("core.writer.svCollision"));
            var current = TimingMap.At(document, start);
            // Quantising a head across a red timing boundary changes its locked beat length.
            if (group.Any(s => Math.Abs(TimingMap.At(document, s.StartTimeMs).BeatLengthMs - current.BeatLengthMs) > 1e-9))
                throw new InvalidDataException(L.Get("core.writer.bpmBoundary"));
            bool changesSv = Math.Abs(TimingMap.At(emitted, start).SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            bool differsFromOriginal = Math.Abs(current.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            if (!changesSv && !differsFromOriginal) continue;
            if (differsFromOriginal && document.ImportedSliders.Any(s => s.TimeMs == start))
                throw new InvalidDataException(L.Get("core.writer.existingSv"));
            if (changesSv && document.ImportedSliders.Any(s => s.TimeMs >= start - 1 && s.TimeMs < start))
                throw new InvalidDataException(L.Get("core.writer.restoreInterval"));
            var currentGroup = original.Where(t => t.TimeMs <= start).GroupBy(t => t.TimeMs).LastOrDefault();
            var template = currentGroup?.LastOrDefault(t => !t.Uninherited) ?? currentGroup?.First() ?? original[0];
            double nextBoundary = original.Where(t => t.TimeMs > start).Select(t => t.TimeMs)
                .Concat(generated.Select(s => Round(s.StartTimeMs)).Where(t => t > start))
                .DefaultIfEmpty(double.PositiveInfinity).Min();
            double nextImported = document.ImportedSliders.Where(s => s.TimeMs > start && s.TimeMs < nextBoundary)
                .Select(s => s.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min();
            // Only an unchanged slider before the next independent SV boundary needs the original speed restored.
            bool needsRestore = differsFromOriginal && double.IsFinite(nextImported);
            // Keep the override through the next millisecond. Fractional restoration can truncate onto the head in stable.
            double restoreTime = start + 2;
            if (nextBoundary < restoreTime || needsRestore && (restoreTime > int.MaxValue || nextImported < restoreTime))
                throw new InvalidDataException(L.Get("core.writer.restoreInterval"));
            if (changesSv)
                OverrideInherited(emitted.TimingPoints, template, start, -100 / first.SliderVelocityMultiplier);
            if (needsRestore)
                OverrideInherited(emitted.TimingPoints, template, restoreTime, current.GenerateTicks ? -100 / current.SliderVelocityMultiplier : double.NaN);
        }
        return emitted.TimingPoints.OrderBy(t => t.TimeMs).ToList();
    }

    private static void OverrideInherited(List<TimingPoint> timing, TimingPoint template, double time, double beatLength)
    {
        // A changed head must have one effective green point, independent of duplicate-point ordering in a consumer.
        timing.RemoveAll(t => t.TimeMs == time && !t.Uninherited);
        timing.Add(Inherited(template, time, beatLength));
    }

    private static TimingPoint Inherited(TimingPoint template, double time, double beatLength) => new()
    {
        TimeMs = time, BeatLengthMs = beatLength, Uninherited = false, Meter = template.Meter,
        SampleSet = template.SampleSet, SampleIndex = template.SampleIndex, Volume = template.Volume, Effects = template.Effects
    };

    private static string TimingLine(TimingPoint point)
    {
        if (point.OriginalLine is not null && point.ContentEquals(OsuBeatmapReader.ParseTiming(point.OriginalLine, point.SourceOrder))) return point.OriginalLine;
        string[] values = [Number(point.TimeMs), Number(point.BeatLengthMs), point.Meter.ToString(CultureInfo.InvariantCulture),
            point.SampleSet.ToString(CultureInfo.InvariantCulture), point.SampleIndex.ToString(CultureInfo.InvariantCulture),
            point.Volume.ToString(CultureInfo.InvariantCulture), point.Uninherited ? "1" : "0", point.Effects.ToString(CultureInfo.InvariantCulture)];
        if (point.OriginalLine?.Split(',') is { Length: > 8 } previous) values = values.Concat(previous.Skip(8)).ToArray();
        return string.Join(',', values);
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    private static string DefaultEdges(int spans, string value) => string.Join('|', Enumerable.Repeat(value, checked(spans + 1)));
    private static string ResizeEdges(string? source, int spans, string fallback)
    {
        string[] values = Enumerable.Repeat(fallback, checked(spans + 1)).ToArray();
        if (!string.IsNullOrEmpty(source))
        {
            string[] old = source.Split('|');
            values[0] = old[0];
            if (old.Length > 1) values[^1] = old[^1];
            for (int i = 1; i < Math.Min(old.Length - 1, values.Length - 1); i++) values[i] = old[i];
        }
        return string.Join('|', values);
    }
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void SetNumber(MapDocument document, string section, string key, double value)
    {
        string? old = OsuBeatmapReader.Setting(document, section, key);
        if (old is not null && OsuBeatmapReader.Number(old) == value) return;
        Set(document, section, key, Number(value));
    }
    private static void Set(MapDocument document, string name, string key, string value)
    {
        var sections = document.OriginalSections.Where(s => s.Name == name).ToArray();
        foreach (var section in sections.Reverse())
        for (int i = section.Lines.Count - 1; i >= 0; i--)
        {
            string[] parts = section.Lines[i].Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim() == key)
            {
                if (parts[1].Trim() != value) section.Lines[i] = key + ":" + value;
                return;
            }
        }
        var target = sections.LastOrDefault();
        if (target is null) { target = new OsuSection { Name = name }; document.OriginalSections.Add(target); }
        target.Lines.Add(key + ":" + value);
    }
    private static void ReplaceData(MapDocument document, string name, IEnumerable<string> data)
    {
        var sections = document.OriginalSections.Where(s => s.Name == name).ToArray();
        var target = sections.FirstOrDefault();
        if (target is null) { target = new OsuSection { Name = name }; document.OriginalSections.Add(target); }
        foreach (var section in sections) section.Lines.RemoveAll(l => OsuBeatmapReader.IsDataLine(l.Trim()));
        target.Lines.AddRange(data);
    }
}
