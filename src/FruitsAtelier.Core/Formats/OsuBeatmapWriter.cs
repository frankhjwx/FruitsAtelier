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
    public IReadOnlyList<ConvertedCatchObject> PlayableObjects { get; init; } = [];
    public IReadOnlyList<ConvertedCatchObject> PlayableHardRockObjects { get; init; } = [];
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
        var playableObjects = matches
            ? converted.Objects.Zip(reconverted.Objects).Select(pair => pair.Second with
            {
                SourceId = pair.First.SourceId,
                EventIndex = pair.First.EventIndex,
                IsStandalone = pair.First.IsStandalone
            }).ToArray()
            : [];
        var playableIds = matches
            ? reconverted.Objects.Zip(converted.Objects).ToDictionary(pair => (pair.First.SourceId, pair.First.EventIndex), pair => (pair.Second.SourceId, pair.Second.EventIndex, pair.Second.IsStandalone))
            : [];
        var playableHardRockObjects = matches
            ? CatchPreviewMods.HardRock(readBack, reconverted)
                .Select(item =>
                {
                    var identity = playableIds[(item.SourceId, item.EventIndex)];
                    return item with
                    {
                        SourceId = identity.SourceId,
                        EventIndex = identity.EventIndex,
                        IsStandalone = identity.IsStandalone
                    };
                }).ToArray()
            : [];
        return new OsuWriteResult
        {
            Text = serialized, ReadBack = readBack, Diagnostics = diagnostics,
            MaxTimeQuantizationMs = maxTime, MaxCoordinateQuantization = maxCoordinate,
            MaxConvertedTimeErrorMs = timeError, MaxConvertedXError = xError, ObjectSequenceMatches = matches,
            PlayableObjects = playableObjects, PlayableHardRockObjects = playableHardRockObjects
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
        emitted.TimingPoints.AddRange(original.Select(t => t.DeepClone()));
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
            var emittedState = TimingMap.At(emitted, start);
            bool changesSv = Math.Abs(emittedState.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            bool differsFromOriginal = Math.Abs(current.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            if (differsFromOriginal && document.ImportedSliders.Any(s => s.TimeMs == start))
                throw new InvalidDataException(L.Get("core.writer.existingSv"));
            var preceding = document.ImportedSliders.FirstOrDefault(s => s.TimeMs >= start - 1 && s.TimeMs < start
                && (Math.Abs(TimingMap.At(document, s.TimeMs).SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9
                    || !TimingMap.At(document, s.TimeMs).GenerateTicks));
            if (changesSv && preceding is not null)
                throw new InvalidDataException(L.Get("core.writer.restoreIntervalAt", Number(preceding.TimeMs), Number(start)));
            var currentGroup = original.Where(t => t.TimeMs <= start).GroupBy(t => t.TimeMs).LastOrDefault();
            var template = currentGroup?.LastOrDefault(t => !t.Uninherited) ?? currentGroup?.First() ?? original[0];
            // Equal-SV heads can share a window, but its restoration must clear every head in the chain.
            double restoreTime = start + 2;
            foreach (var nearby in generated.Where(s => Round(s.StartTimeMs) > start).OrderBy(s => s.StartTimeMs))
            {
                double head = Round(nearby.StartTimeMs);
                if (head >= restoreTime) break;
                if (Math.Abs(nearby.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9)
                    throw new InvalidDataException(L.Get("core.writer.restoreIntervalAt", Number(head), Number(start)));
                restoreTime = head + 2;
            }
            double nextBoundary = original.Where(t => t.TimeMs >= restoreTime).Select(t => t.TimeMs)
                .Concat(generated.Select(s => Round(s.StartTimeMs)).Where(t => t >= restoreTime))
                .DefaultIfEmpty(double.PositiveInfinity).Min();
            double nextImported = document.ImportedSliders.Where(s => s.TimeMs > start && s.TimeMs < nextBoundary)
                .Select(s => s.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min();
            var closeBoundaries = original.Where(t => t.TimeMs > start && t.TimeMs < restoreTime)
                .GroupBy(t => t.TimeMs).ToArray();
            bool normalizedBoundary = false;
            foreach (var boundary in closeBoundaries)
            {
                var state = TimingMap.At(document, boundary.Key);
                if (Math.Abs(state.BeatLengthMs - current.BeatLengthMs) > 1e-9)
                    throw new InvalidDataException(L.Get("core.writer.restoreIntervalAt", Number(boundary.Key), Number(start)));
                if (Math.Abs(state.SliderVelocityMultiplier - first.SliderVelocityMultiplier) <= 1e-9) continue;
                // Keep sample/effect events at their original times; only defer the conflicting SV change.
                var greens = emitted.TimingPoints.Where(t => t.TimeMs == boundary.Key && !t.Uninherited).ToArray();
                foreach (var green in greens) green.BeatLengthMs = -100 / first.SliderVelocityMultiplier;
                if (greens.Length == 0)
                    OverrideInherited(emitted.TimingPoints, boundary.First(), boundary.Key, -100 / first.SliderVelocityMultiplier);
                normalizedBoundary = true;
            }
            var restoreState = TimingMap.At(document, Math.BitDecrement(restoreTime));
            bool restoreDiffers = Math.Abs(restoreState.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            bool needsRestore = restoreDiffers && (normalizedBoundary || double.IsFinite(nextImported))
                && !original.Any(t => t.TimeMs == restoreTime);
            if (needsRestore && restoreTime > int.MaxValue)
                throw new InvalidDataException(L.Get("core.writer.restoreIntervalAt", Number(restoreTime), Number(start)));
            if (changesSv)
                OverrideInherited(emitted.TimingPoints, template, start,
                    current.GenerateTicks || differsFromOriginal ? -100 / first.SliderVelocityMultiplier : double.NaN);
            if (needsRestore)
            {
                var restoreGroup = original.Where(t => t.TimeMs < restoreTime).GroupBy(t => t.TimeMs).LastOrDefault();
                var restoreTemplate = restoreGroup?.LastOrDefault(t => !t.Uninherited) ?? restoreGroup?.First() ?? original[0];
                OverrideInherited(emitted.TimingPoints, restoreTemplate, restoreTime,
                    restoreState.GenerateTicks ? -100 / restoreState.SliderVelocityMultiplier : double.NaN);
            }
        }
        // An SV-only boundary can move only if unchanged sliders keep both their exact and legacy lookup states.
        VerifyImportedTiming(document, emitted, generated);
        return emitted.TimingPoints.OrderBy(t => t.TimeMs).ToList();
    }

    private static void VerifyImportedTiming(MapDocument original, MapDocument emitted, IReadOnlyList<GeneratedSlider> generated)
    {
        foreach (bool truncateTiming in new[] { false, true })
        {
            TimingMap.Lookup Lookup(MapDocument source)
            {
                if (!truncateTiming) return new(source);
                var truncated = new MapDocument { BeatLengthMs = source.BeatLengthMs, TimingOffsetMs = source.TimingOffsetMs };
                foreach (var point in source.TimingPoints.OrderBy(t => t.TimeMs))
                {
                    var copy = point.DeepClone(); copy.TimeMs = Math.Floor(copy.TimeMs);
                    copy.SourceOrder = truncated.TimingPoints.Count;
                    truncated.TimingPoints.Add(copy);
                }
                return new(truncated);
            }
            var before = Lookup(original); var after = Lookup(emitted);
            foreach (var slider in original.ImportedSliders)
            foreach (int offset in new[] { 0, 1 })
            {
                var expected = before.At(slider.TimeMs + offset); var actual = after.At(slider.TimeMs + offset);
                if (Math.Abs(expected.BeatLengthMs - actual.BeatLengthMs) <= 1e-9
                    && Math.Abs(expected.SliderVelocityMultiplier - actual.SliderVelocityMultiplier) <= 1e-9
                    && expected.GenerateTicks == actual.GenerateTicks) continue;
                double head = generated.MinBy(s => Math.Abs(Round(s.StartTimeMs) - slider.TimeMs))!.StartTimeMs;
                throw new InvalidDataException(L.Get("core.writer.restoreIntervalAt", Number(slider.TimeMs), Number(Round(head))));
            }
        }
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
