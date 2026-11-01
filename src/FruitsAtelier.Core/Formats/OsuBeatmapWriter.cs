using L = FruitsAtelier.Localization.Strings;
using System.Globalization;
using System.Text;

namespace FruitsAtelier.Core;

// Keep authoring and quantized read-back conversions separate; each cache belongs to one caller.
public sealed class OsuWriteCache
{
    private MapDocument? multiplierSnapshot;
    private OsuWriteResult? multiplierOutput;
    private bool multiplierCompensation;
    private string multiplierLanguage = "";
    private MapDocument? multiplierBaselineSnapshot;
    private OsuWriteResult? multiplierBaseline;
    private bool multiplierBaselineCompensation;
    private string multiplierBaselineLanguage = "";
    public OsuWriteResult? MultiplierBaseline(MapDocument document, bool compensation)
    {
        if (multiplierBaselineSnapshot is null || multiplierBaselineCompensation != compensation || multiplierBaselineLanguage != L.Language) return null;
        return multiplierBaselineSnapshot.SliderMultiplierOverride == document.SliderMultiplierOverride
            && multiplierBaselineSnapshot.ContentEquals(document, ignoreMultiplierOverride: true) ? multiplierBaseline : null;
    }
    internal void RememberBaseline(MapDocument document, OsuWriteResult output, bool compensation)
    {
        multiplierBaselineSnapshot = document.DeepClone(); multiplierBaselineSnapshot.OverrideSliderMultiplier = false;
        multiplierBaseline = output; multiplierBaselineCompensation = compensation; multiplierBaselineLanguage = L.Language;
    }
    internal OsuWriteResult? Multiplier(MapDocument document, bool compensation)
        => multiplierCompensation == compensation && multiplierLanguage == L.Language && multiplierSnapshot?.ContentEquals(document) == true ? multiplierOutput : null;
    public void RememberMultiplier(MapDocument document, OsuWriteResult output, bool compensation)
    {
        multiplierSnapshot = document.DeepClone(); multiplierOutput = output; multiplierCompensation = compensation; multiplierLanguage = L.Language;
    }
    public CatchConversionResult Convert(MapDocument document, bool compensateTinyDroplets = true)
        => CatchStreamConverter.Convert(document, compensateTinyDroplets, Source);

    internal CatchConversionCache Source { get; } = new();
    internal CatchConversionCache ReadBack { get; } = new();
    internal CatchConversionCache MultiplierReadBack { get; } = new();
    internal OsuSliderParseCache MultiplierParsedSliders { get; } = new();
    internal OsuSliderParseCache ParsedSliders { get; } = new();
    private GeneratedSlider[] timingSliders = [];
    private TimingMap.Lookup? timingInput;
    private double[] importedTimes = [];
    private List<TimingPoint>? emittedTiming;

    internal List<TimingPoint> Timing(MapDocument document, IReadOnlyList<GeneratedSlider> sliders,
        Func<MapDocument, IReadOnlyList<GeneratedSlider>, List<TimingPoint>> build)
    {
        if (emittedTiming is not null && timingInput!.MatchesTiming(document)
            && timingSliders.Length == sliders.Count
            && timingSliders.Where((slider, i) => slider.StartTimeMs != sliders[i].StartTimeMs
                || slider.SliderVelocityMultiplier != sliders[i].SliderVelocityMultiplier).Any() == false
            && importedTimes.SequenceEqual(document.ImportedSliders.Select(s => s.TimeMs))) return emittedTiming;
        var result = build(document, sliders);
        timingSliders = sliders.ToArray();
        timingInput = new(document);
        importedTimes = document.ImportedSliders.Select(s => s.TimeMs).ToArray();
        return emittedTiming = result;
    }
}

public sealed class OsuWriteResult
{
    internal SliderMultiplierAnalysis? MultiplierAnalysis { get; set; }
    public IReadOnlyList<Guid> ObjectSources { get; init; } = [];
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
    public IReadOnlyDictionary<Guid, double> PlayableEndTimes { get; init; } = new Dictionary<Guid, double>();
}

public static class OsuBeatmapWriter
{
    private static readonly string[] sectionOrder = ["General", "Editor", "Metadata", "Difficulty", "Events", "TimingPoints", "Colours", "HitObjects"];
    private static readonly Dictionary<string, string[]> settingOrder = new(StringComparer.Ordinal)
    {
        ["General"] = ["AudioFilename", "AudioLeadIn", "AudioHash", "PreviewTime", "Countdown", "SampleSet", "StackLeniency", "Mode",
            "LetterboxInBreaks", "StoryFireInFront", "UseSkinSprites", "AlwaysShowPlayfield", "OverlayPosition", "SkinPreference",
            "EpilepsyWarning", "CountdownOffset", "SpecialStyle", "WidescreenStoryboard", "SamplesMatchPlaybackRate"],
        ["Editor"] = ["Bookmarks", "DistanceSpacing", "BeatDivisor", "GridSize", "TimelineZoom"],
        ["Metadata"] = ["Title", "TitleUnicode", "Artist", "ArtistUnicode", "Creator", "Version", "Source", "Tags", "BeatmapID", "BeatmapSetID"],
        ["Difficulty"] = ["HPDrainRate", "CircleSize", "OverallDifficulty", "ApproachRate", "SliderMultiplier", "SliderTickRate"],
        ["Colours"] = ["SliderTrackOverride", "SliderBorder"]
    };
    public static OsuWriteResult WriteFile(MapDocument document, string destination, bool compensateTinyDroplets = true)
    {
        if (document.SourcePath is not null && string.Equals(Path.GetFullPath(destination), Path.GetFullPath(document.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.Get("core.writer.sourceOverwrite"));
        var result = Serialize(document, compensateTinyDroplets);
        AtomicFile.Write(destination, result.Text);
        return result;
    }

    public static OsuWriteResult Serialize(MapDocument document, bool compensateTinyDroplets = true, OsuWriteCache? cache = null)
    {
        OsuBeatmapReader.Validate(document);
        if (cache?.Multiplier(document, compensateTinyDroplets) is { } cached) return cached;
        var authoring = document;
        var bananaExpansion = BananaShowerExpansion.Create(document);
        if (bananaExpansion is not null) document = bananaExpansion.Document;
        cache?.ParsedSliders.Begin();
        var converted = CatchStreamConverter.Convert(document, compensateTinyDroplets, cache?.Source);
        if (!converted.Success) throw new InvalidDataException(L.Get("core.writer.incompletePrefix") + string.Join(L.Get("core.diagnostics.separator"), converted.Diagnostics));
        var tracks = document.Tracks.ToDictionary(t => t.Id);
        var generated = converted.Sliders.Where(s => tracks.ContainsKey(s.SourceId)).ToArray();
        if (generated.Length != document.Tracks.Count(t => t.StreamSnapDivisor is null)) throw new InvalidDataException(L.Get("core.writer.sliderCount"));
        var diagnostics = converted.Diagnostics.ToList();
        double maxTime = authoring.Fruits.Zip(document.Fruits)
            .Where(p => p.First.TimeMs != p.Second.TimeMs)
            .Select(p => Math.Abs(p.First.TimeMs - QuantizeTime(p.Second.TimeMs))).DefaultIfEmpty(0).Max();
        double maxCoordinate = 0;
        var lines = new List<(double Time, int Order, Guid SourceId, string Text)>();
        var streamIds = document.Tracks.Where(t => t.StreamSnapDivisor is not null).ToDictionary(t => t.Id);
        foreach (var item in converted.Objects.Where(o => streamIds.ContainsKey(o.SourceId)))
        {
            var track = streamIds[item.SourceId];
            lines.Add((item.TimeMs, track.SourceOrder, track.Id,
                SliderFruitStream.FruitLine(track, item.EventIndex, Coordinate(item.X), Time(item.TimeMs), timeMs: item.TimeMs)));
        }
        foreach (var fruit in document.Fruits)
        {
            string[] p = fruit.OriginalLine?.Split(',') ?? ["0", "192", "0", "1", "0", "0:0:0:0:"];
            // Preserve all original flags, sample columns and untouched numeric spelling.
            if (fruit.OriginalLine is null || OsuBeatmapReader.LegacyCoordinate(p[0]) != fruit.X) p[0] = Coordinate(fruit.X);
            if (fruit.OriginalLine is null || OsuBeatmapReader.LegacyCoordinate(p[1]) != fruit.Y) p[1] = Coordinate(fruit.Y);
            if (fruit.OriginalLine is null || OsuBeatmapReader.Number(p[2]) != fruit.TimeMs) p[2] = Time(fruit.TimeMs);
            lines.Add((fruit.TimeMs, fruit.SourceOrder, fruit.Id, string.Join(',', p)));
        }
        foreach (var slider in document.ImportedSliders)
        {
            if (slider.OriginalLine is not null)
            {
                var original = new MapDocument();
                if (cache is null) OsuBeatmapReader.ParseObject(original, slider.OriginalLine, slider.SourceOrder);
                else cache.ParsedSliders.Parse(original, slider.OriginalLine, slider.SourceOrder);
                if (original.ImportedSliders.Count != 1) throw new InvalidDataException(L.Get("core.writer.importedText"));
                original.ImportedSliders[0].Id = slider.Id;
                original.ImportedSliders[0].DropletMode = slider.DropletMode;
                original.ImportedSliders[0].RandomizationSource = slider.RandomizationSource;
                original.ImportedSliders[0].RandomizationTrack = slider.RandomizationTrack;
                original.ImportedSliders[0].RandomizationBaseVelocity = slider.RandomizationBaseVelocity;
                original.ImportedSliders[0].PreserveNativeGeometry = slider.PreserveNativeGeometry;
                original.ImportedSliders[0].EndpointSnap = slider.EndpointSnap;
                original.ImportedSliders[0].SliderVelocityMultiplier = slider.SliderVelocityMultiplier;
                original.ImportedSliders[0].ProcessedSliderVelocityMultiplier = slider.ProcessedSliderVelocityMultiplier;
                if (!slider.ContentEquals(original.ImportedSliders[0])) throw new InvalidDataException(L.Get("core.writer.importedChanged"));
                string line = slider.OriginalLine;
                if (slider.PreserveNativeGeometry && slider.TimeMs != QuantizeTime(slider.TimeMs))
                {
                    // Canvas source lines retain authoring precision; exported heads must match generated SV times.
                    var values = line.Split(',');
                    values[2] = Time(slider.TimeMs);
                    line = string.Join(',', values);
                }
                if (line.Contains(",,", StringComparison.Ordinal) || line.EndsWith(',')
                    || line.Contains("|,", StringComparison.Ordinal) || line.Contains(",|", StringComparison.Ordinal)
                    || line.Contains("||", StringComparison.Ordinal) || line.EndsWith('|'))
                {
                    string[] values = line.Split(',');
                    FillSliderSamples(values, slider.SpanCount);
                    line = string.Join(',', values);
                }
                lines.Add((slider.TimeMs, slider.SourceOrder, slider.Id, line));
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
                if (Math.Max(shower.TimeMs, OsuBeatmapReader.Number(values[5])) != shower.EndTimeMs) values[5] = Time(shower.EndTimeMs);
                lines.Add((shower.TimeMs, shower.SourceOrder, shower.Id, string.Join(',', values)));
            }
            else lines.Add((shower.TimeMs, shower.SourceOrder, shower.Id, $"256,192,{Time(shower.TimeMs)},8,0,{Time(shower.EndTimeMs)},0:0:0:0:"));
        }
        foreach (var slider in generated)
        {
            if (slider.Path.Count < 2) throw new InvalidDataException(L.Get("core.writer.minimumPath"));
            var track = tracks[slider.SourceId];
            if (slider.SpanCount != track.SpanCount) throw new InvalidDataException(L.Get("core.writer.spanMismatch"));
            var first = slider.Path[0];
            var pathBuilder = new StringBuilder("B");
            for (int i = 1; i < slider.Path.Count; i++)
            {
                string point = Coordinate(slider.Path[i].X) + ":" + Coordinate(slider.Path[i].GeometryY);
                pathBuilder.Append('|').Append(point);
                // Repeated internal anchors delimit straight Bezier segments in stable.
                if (i < slider.Path.Count - 1) pathBuilder.Append('|').Append(point);
            }
            string path = pathBuilder.ToString();
            string[] values = track.OriginalLine?.Split(',') ?? ["0", "192", "0", "2", "0", "L", "1", "0", "0|0", "0:0|0:0", "0:0:0:0:"];
            if (values.Length < 8 || (OsuBeatmapReader.Integer(values[3]) & (1 | 2 | 8 | 128)) != 2)
                throw new InvalidDataException(L.Get("core.writer.originalObject"));
            int originalSpans = OsuBeatmapReader.Integer(values[6]);
            if (values.Length < 11) Array.Resize(ref values, 11);
            FillSliderSamples(values, originalSpans);
            values[0] = Coordinate(first.X); values[1] = Coordinate(first.GeometryY); values[2] = Time(slider.StartTimeMs);
            values[5] = path; values[6] = slider.SpanCount.ToString(CultureInfo.InvariantCulture); values[7] = Number(slider.Length);
            if (track.OriginalLine is null || originalSpans != slider.SpanCount)
            {
                values[8] = ResizeEdges(values[8], slider.SpanCount, "0");
                values[9] = ResizeEdges(values[9], slider.SpanCount, "0:0");
            }
            lines.Add((slider.StartTimeMs, track.SourceOrder, slider.SourceId, string.Join(',', values)));
        }
        var timingDocument = document;
        if (document.EffectiveSliderMultiplier != document.SliderMultiplier)
        {
            timingDocument = document.DeepClone();
            if (timingDocument.TimingPoints.Count == 0)
                timingDocument.TimingPoints.Add(new() { TimeMs = document.TimingOffsetMs, BeatLengthMs = document.BeatLengthMs });
            double firstSlider = generated.Select(s => QuantizeTime(s.StartTimeMs))
                .Concat(document.ImportedSliders.Select(s => s.TimeMs)).DefaultIfEmpty(double.PositiveInfinity).Min();
            SliderMultiplierEditing.RebaseTiming(timingDocument, document.EffectiveSliderMultiplier, firstSlider);
        }
        var sourceTiming = new TimingMap.Lookup(document);
        var independent = document.ImportedSliders.Where(s => (s.SliderVelocityMultiplier is not null || s.PreserveNativeGeometry)
            && Math.Abs(ImportedSliderConverter.SliderVelocityAt(document, s, sourceTiming)
                - sourceTiming.At(s.PreserveNativeGeometry ? QuantizeTime(s.TimeMs) : s.TimeMs).SliderVelocityMultiplier) > 1e-9).Select(s => s.Id).ToHashSet();
        var timingSliders = generated;
        if (independent.Count > 0)
        {
            timingDocument = timingDocument.DeepClone();
            timingDocument.ImportedSliders.RemoveAll(s => independent.Contains(s.Id));
            timingSliders = generated.Concat(converted.Sliders.Where(s => independent.Contains(s.SourceId))).ToArray();
        }
        var timing = cache is null ? BuildTiming(timingDocument, timingSliders) : cache.Timing(timingDocument, timingSliders, BuildTiming);
        if (document.HitsoundOverrides.Count > 0)
        {
            timing = timing.Select(p => p.DeepClone()).ToList();
            HitsoundCopier.ApplyToExport(document, converted.Objects, lines, timing);
        }
        if (timingSliders.Length > 0) timing = RemoveUnusedInheritedTiming(timing, converted, document);
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
        SetNumber(output, "Difficulty", "SliderMultiplier", document.EffectiveSliderMultiplier);
        SetNumber(output, "Difficulty", "SliderTickRate", document.SliderTickRate);
        ReplaceData(output, "TimingPoints", timing.Select(TimingLine));
        // Time truncation is monotone; sorting before it retains current parent order when distinct times collapse.
        // Equal time/order follows the converter's stable parent input order, including stream fruits.
        var sourcePositions = document.Fruits.Select(f => (f.Id, Time: f.TimeMs, f.SourceOrder))
            .Concat(document.Tracks.Select(t => (t.Id, Time: t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(document.ImportedSliders.Select(s => (s.Id, Time: s.TimeMs, s.SourceOrder)))
            .Concat(document.BananaShowers.Select(s => (s.Id, Time: s.TimeMs, s.SourceOrder)))
            .OrderBy(p => p.Time).ThenBy(p => p.SourceOrder)
            .Select((p, index) => (p.Id, index)).ToDictionary(p => p.Id, p => p.index);
        var orderedLines = lines.OrderBy(l => l.Time).ThenBy(l => l.Order)
            .ThenBy(l => sourcePositions[l.SourceId]).ToArray();
        ReplaceData(output, "HitObjects", orderedLines.Select(l => l.Text));
        string serialized = SectionText(output);
        var readBack = OsuBeatmapReader.Read(serialized, document.SourcePath, inferDuration: false, cache?.ParsedSliders);
        cache?.ParsedSliders.End();
        // Parser IDs change on every read. Parent identities keep unchanged geometry reusable across edits.
        foreach (var slider in readBack.ImportedSliders) slider.Id = orderedLines[slider.SourceOrder].SourceId;
        foreach (var shower in readBack.BananaShowers) shower.Id = orderedLines[shower.SourceOrder].SourceId;
        var reconverted = CatchStreamConverter.Convert(readBack, compensateTinyDroplets, cache?.ReadBack);
        if (!reconverted.Success) throw new InvalidDataException(L.Get("core.writer.readBackPrefix") + string.Join(L.Get("core.diagnostics.separator"), reconverted.Diagnostics));
        OsuBeatmapReader.SetDuration(readBack, reconverted.Sliders.Select(s => s.StartTimeMs + s.DurationMs));
        var sourceIds = readBack.Fruits.Select(f => (f.Id, f.SourceOrder))
            .Concat(readBack.ImportedSliders.Select(s => (s.Id, s.SourceOrder)))
            .Concat(readBack.BananaShowers.Select(s => (s.Id, s.SourceOrder)))
            .ToDictionary(p => p.Id, p => orderedLines[p.SourceOrder].SourceId);
        var pairs = converted.Objects.Zip(reconverted.Objects).Select(p => (First: p.First, Second: p.Second)).ToArray();
        bool matches = converted.Objects.Count == reconverted.Objects.Count
            && pairs.All(p => p.First.Kind == p.Second.Kind && p.First.SourceId == sourceIds[p.Second.SourceId]
                && (streamIds.ContainsKey(p.First.SourceId) || p.First.EventIndex == p.Second.EventIndex));
        if (!matches && converted.Objects.Count == reconverted.Objects.Count)
        {
            var authoredEvents = converted.Objects.ToDictionary(o => (o.SourceId, o.EventIndex));
            var streamEvents = readBack.Fruits.Where(f => streamIds.ContainsKey(sourceIds[f.Id]))
                .GroupBy(f => sourceIds[f.Id])
                .SelectMany(g => g.OrderBy(f => f.SourceOrder).Select((f, index) => (f.Id, Index: index)))
                .ToDictionary(p => p.Id, p => p.Index);
            // Integer slider heads can move ahead of unchanged fractional fruits; match identities in read-back order.
            pairs = reconverted.Objects.Select(item =>
            {
                int index = streamEvents.GetValueOrDefault(item.SourceId, item.EventIndex);
                authoredEvents.TryGetValue((sourceIds[item.SourceId], index), out var authored);
                return (First: authored!, Second: item);
            }).ToArray();
            matches = pairs.All(p => p.First is not null && p.First.Kind == p.Second.Kind);
        }
        double timeError = 0, xError = 0;
        if (matches)
        {
            foreach (var pair in pairs)
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
            ? pairs.Select(pair => pair.Second with
            {
                SourceId = pair.First.SourceId,
                EventIndex = pair.First.EventIndex,
                IsStandalone = pair.First.IsStandalone
            }).ToArray()
            : [];
        var playableIds = matches
            ? pairs.ToDictionary(pair => (pair.Second.SourceId, pair.Second.EventIndex), pair => (pair.First.SourceId, pair.First.EventIndex, pair.First.IsStandalone))
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
        var playableEndTimes = matches
            ? readBack.Fruits.Select(f => (SourceId: sourceIds[f.Id], EndTimeMs: f.TimeMs))
                .Concat(reconverted.Sliders.Select(s => (SourceId: sourceIds[s.SourceId], EndTimeMs: s.StartTimeMs + s.DurationMs)))
                .Concat(readBack.BananaShowers.Select(s => (SourceId: sourceIds[s.Id], EndTimeMs: s.EndTimeMs)))
                .GroupBy(item => item.SourceId).ToDictionary(group => group.Key, group => group.Max(item => item.EndTimeMs))
            : [];
        var result = new OsuWriteResult
        {
            Text = serialized, ReadBack = readBack, Diagnostics = diagnostics, ObjectSources = orderedLines.Select(l => bananaExpansion?.Parent(l.SourceId) ?? l.SourceId).ToArray(),
            MaxTimeQuantizationMs = maxTime, MaxCoordinateQuantization = maxCoordinate,
            MaxConvertedTimeErrorMs = timeError, MaxConvertedXError = xError, ObjectSequenceMatches = matches,
            PlayableObjects = bananaExpansion?.Remap(playableObjects) ?? playableObjects,
            PlayableHardRockObjects = bananaExpansion?.Remap(playableHardRockObjects) ?? playableHardRockObjects,
            PlayableEndTimes = bananaExpansion is null ? playableEndTimes : playableEndTimes
                .GroupBy(p => bananaExpansion.Parent(p.Key)).ToDictionary(g => g.Key, g => g.Max(p => p.Value))
        };
        if (matches) result.MultiplierAnalysis = new(result, reconverted);
        cache?.RememberBaseline(authoring, result, compensateTinyDroplets);
        if (document.SliderMultiplierOverride is not null) cache?.RememberMultiplier(authoring, result, compensateTinyDroplets);
        return result;

        string Coordinate(double value) { double rounded = Round(value); maxCoordinate = Math.Max(maxCoordinate, Math.Abs(rounded - value)); return Number(rounded); }
        string Time(double value) { double quantized = QuantizeTime(value); maxTime = Math.Max(maxTime, Math.Abs(quantized - value)); return Number(quantized); }
    }

    private static List<TimingPoint> BuildTiming(MapDocument document, IReadOnlyList<GeneratedSlider> generated)
    {
        var original = document.TimingPoints.Select(t => t.DeepClone()).ToList();
        if (original.Count == 0) original.Add(new TimingPoint { TimeMs = document.TimingOffsetMs, BeatLengthMs = document.BeatLengthMs });
        // Timing edits can append earlier points; stable sorting retains tied sample/SV precedence.
        original = original.OrderBy(p => p.TimeMs).ToList();
        if (generated.Count == 0) return original;
        var emitted = new MapDocument { BeatLengthMs = document.BeatLengthMs, TimingOffsetMs = document.TimingOffsetMs };
        emitted.TimingPoints.AddRange(original.Select(t => t.DeepClone()));
        var originalLookup = new TimingMap.Lookup(document);
        var orderedSliders = generated.OrderBy(s => s.StartTimeMs).ToArray();
        var heads = orderedSliders.Select(s => QuantizeTime(s.StartTimeMs)).ToArray();
        var originalTimes = original.Select(t => t.TimeMs).ToArray();
        foreach (var group in generated.GroupBy(s => QuantizeTime(s.StartTimeMs)).OrderBy(g => g.Key))
        {
            double start = group.Key;
            var first = group.First();
            if (group.Any(s => Math.Abs(s.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9))
                throw new InvalidDataException(L.Get("core.writer.svCollision"));
            var current = originalLookup.At(start);
            // Quantising a head across a red timing boundary changes its locked beat length.
            if (group.Any(s => Math.Abs(originalLookup.At(s.StartTimeMs).BeatLengthMs - current.BeatLengthMs) > 1e-9))
                throw new InvalidDataException(L.Get("core.writer.bpmBoundary"));
            bool changesSv = Math.Abs(EmittedSliderVelocityAt(emitted.TimingPoints, start) - first.SliderVelocityMultiplier) > 1e-9;
            bool differsFromOriginal = Math.Abs(current.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            if (differsFromOriginal && document.ImportedSliders.Any(s => s.TimeMs == start))
                throw new InvalidDataException(L.Get("core.writer.existingSv"));
            var template = TemplateAt(start, inclusive: true);
            int nextTiming = LowerBound(originalTimes, Math.BitIncrement(start));
            int nextHead = LowerBound(heads, Math.BitIncrement(start));
            double nextBoundary = Math.Min(nextTiming < originalTimes.Length ? originalTimes[nextTiming] : double.PositiveInfinity,
                nextHead < heads.Length ? heads[nextHead] : double.PositiveInfinity);
            double restoreTime = document.ImportedSliders
                .Select(s => s.OriginalLine is null || s.PreserveNativeGeometry ? QuantizeTime(s.TimeMs) : s.TimeMs)
                .Where(time => time > start && time < nextBoundary)
                .DefaultIfEmpty(double.PositiveInfinity).Min();
            var restoreState = originalLookup.At(double.IsFinite(restoreTime) ? restoreTime : start);
            bool needsRestore = double.IsFinite(restoreTime)
                && Math.Abs(restoreState.SliderVelocityMultiplier - first.SliderVelocityMultiplier) > 1e-9;
            if (changesSv)
                OverrideInherited(emitted.TimingPoints, template, start,
                    current.GenerateTicks || differsFromOriginal ? -100 / first.SliderVelocityMultiplier : double.NaN);
            if (needsRestore)
            {
                var restoreTemplate = TemplateAt(restoreTime, inclusive: false);
                OverrideInherited(emitted.TimingPoints, restoreTemplate, restoreTime,
                    restoreState.GenerateTicks ? -100 / restoreState.SliderVelocityMultiplier : double.NaN);
            }
        }
        // SV is locked at the head; later timing points belong to subsequent objects.
        VerifyImportedTiming(document, emitted);
        return emitted.TimingPoints.OrderBy(t => t.TimeMs).ToList();

        TimingPoint TemplateAt(double time, bool inclusive)
        {
            int last = LowerBound(originalTimes, inclusive ? Math.BitIncrement(time) : time) - 1;
            if (last < 0) return original[0];
            var first = original[last];
            for (int i = last; i >= 0 && original[i].TimeMs == original[last].TimeMs; i--)
            {
                first = original[i];
                if (!first.Uninherited) return first;
            }
            return first;
        }

        static int LowerBound(double[] values, double time)
        {
            int low = 0, high = values.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (values[middle] < time) low = middle + 1; else high = middle;
            }
            return low;
        }
    }

    private static List<TimingPoint> RemoveUnusedInheritedTiming(List<TimingPoint> points,
        CatchConversionResult converted, MapDocument document)
    {
        var ordered = points.OrderBy(p => p.TimeMs).ToArray();
        var heads = converted.Sliders.Select(s => QuantizeTime(s.StartTimeMs)).Order().ToArray();
        var intervals = converted.Sliders.Select(s => (Start: QuantizeTime(s.StartTimeMs), End: QuantizeTime(s.StartTimeMs) + s.DurationMs))
            .Concat(converted.Objects.Where(o => o.IsStandalone).Select(o => (Start: o.TimeMs, End: o.TimeMs)))
            .Concat(document.BananaShowers.Select(s => (Start: s.TimeMs, End: s.EndTimeMs)))
            .OrderBy(p => p.Start).ToArray();
        var starts = intervals.Select(p => p.Start).ToArray();
        var ends = new double[intervals.Length];
        for (int i = 0; i < ends.Length; i++) ends[i] = Math.Max(intervals[i].End, i == 0 ? double.NegativeInfinity : ends[i - 1]);
        var result = new List<TimingPoint>();
        for (int i = 0; i < ordered.Length; i++)
        {
            var point = ordered[i];
            double next = i + 1 < ordered.Length ? ordered[i + 1].TimeMs : double.PositiveInfinity;
            if (point.Uninherited || result.Count == 0 || next == point.TimeMs
                || i > 0 && ordered[i - 1].TimeMs == point.TimeMs)
            { result.Add(point); continue; }
            var previous = result[^1];
            bool svChanged = Velocity(point) != Velocity(previous)
                || double.IsNaN(point.BeatLengthMs) != double.IsNaN(previous.BeatLengthMs);
            int head = LowerBound(heads, Math.Floor(point.TimeMs) - 1);
            bool usesSv = svChanged && head < heads.Length && heads[head] < next;
            bool samplesChanged = point.SampleSet != previous.SampleSet || point.SampleIndex != previous.SampleIndex
                || point.Volume != previous.Volume;
            // Legacy edge samples can look five milliseconds ahead; slider bodies use samples throughout their lifetime.
            int last = LowerBound(starts, next) - 1;
            bool usesSamples = samplesChanged && last >= 0 && ends[last] >= Math.Floor(point.TimeMs) - 5;
            if (usesSv || usesSamples || point.Effects != previous.Effects) result.Add(point);
        }
        return result;

        static double Velocity(TimingPoint point) => point.BeatLengthMs < 0
            ? Math.Clamp(100 / -point.BeatLengthMs, LegacyCatchRules.MinimumSliderVelocityMultiplier, LegacyCatchRules.MaximumSliderVelocityMultiplier) : 1;
        static int LowerBound(double[] values, double time)
        {
            int low = 0, high = values.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (values[middle] < time) low = middle + 1; else high = middle;
            }
            return low;
        }
    }

    private static double EmittedSliderVelocityAt(List<TimingPoint> points, double time)
    {
        // Emission mutates green points after each head; query that local state without rebuilding every timing group.
        double latest = double.NegativeInfinity;
        TimingPoint? red = null, green = null;
        foreach (var point in points)
        {
            if (point.TimeMs > time || point.TimeMs < latest) continue;
            if (point.TimeMs > latest) { latest = point.TimeMs; red = green = null; }
            if (point.Uninherited)
            { if (red is null || point.SourceOrder < red.SourceOrder) red = point; }
            else if (green is null || point.SourceOrder >= green.SourceOrder) green = point;
        }
        var selected = green ?? red;
        return selected is { BeatLengthMs: < 0 } ? Math.Clamp(100 / -selected.BeatLengthMs,
            LegacyCatchRules.MinimumSliderVelocityMultiplier, LegacyCatchRules.MaximumSliderVelocityMultiplier) : 1;
    }

    private static void VerifyImportedTiming(MapDocument original, MapDocument emitted)
    {
        var before = new TimingMap.Lookup(original);
        var after = new TimingMap.Lookup(emitted);
        foreach (var slider in original.ImportedSliders)
        {
            double exportedHead = slider.OriginalLine is null || slider.PreserveNativeGeometry ? QuantizeTime(slider.TimeMs) : slider.TimeMs;
            var expected = before.At(slider.TimeMs); var actual = after.At(exportedHead);
            if (Math.Abs(expected.BeatLengthMs - actual.BeatLengthMs) <= 1e-9
                && Math.Abs(expected.SliderVelocityMultiplier - actual.SliderVelocityMultiplier) <= 1e-9
                && expected.GenerateTicks == actual.GenerateTicks) continue;
            throw new InvalidDataException(L.Get("core.writer.existingSv"));
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

    internal static (double Start, double End) BananaTimes(BananaShower shower)
    {
        var values = shower.OriginalLine?.Split(',');
        return (Effective(shower.TimeMs, 2), Effective(shower.EndTimeMs, 5));

        double Effective(double time, int field) => values is not null && values.Length > field
            && (field == 5 ? Math.Max(OsuBeatmapReader.Number(values[2]), OsuBeatmapReader.Number(values[5]))
                : OsuBeatmapReader.Number(values[field])) == time ? time : QuantizeTime(time);
    }

    public static double QuantizeTime(double value) => Math.Truncate(value);

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    private static string DefaultEdges(int spans, string value) => string.Join('|', Enumerable.Repeat(value, checked(spans + 1)));
    private static string SliderSampleBanks(string[] values)
        => values.Length > 10 && !string.IsNullOrEmpty(values[10])
            ? string.Join(':', values[10].Split(':').Take(2)) : "0:0";
    private static void FillSliderSamples(string[] values, int spans)
    {
        // Present optional columns need explicit values for stable; omitted edges inherit the object samples.
        if (values.Length > 8 && string.IsNullOrEmpty(values[8])) values[8] = DefaultEdges(spans, values[4]);
        if (values.Length > 9 && string.IsNullOrEmpty(values[9])) values[9] = DefaultEdges(spans, SliderSampleBanks(values));
        for (int column = 8; column <= 9 && column < values.Length; column++)
        {
            var edges = values[column].Split('|');
            if (edges.All(edge => edge.Length > 0)) continue;
            string fallback = column == 8 ? values[4] : SliderSampleBanks(values);
            values[column] = string.Join('|', edges.Select(edge => edge.Length == 0 ? fallback : edge));
        }
        if (values.Length > 10 && string.IsNullOrEmpty(values[10])) values[10] = "0:0:0:0:";
    }
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
    internal static string MultiplierText(MapDocument document, TimingPoint[] points)
    {
        SetNumber(document, "Difficulty", "SliderMultiplier", document.SliderMultiplier);
        ReplaceData(document, "TimingPoints", points.Select(TimingLine));
        return SectionText(document);
    }

    private static string SectionText(MapDocument document)
    {
        var text = new StringBuilder(document.LegacySliderTicks ? "osu file format v7\r\n" : "osu file format v14\r\n");
        var standard = new Queue<OsuSection>(document.OriginalSections.Where(s => Array.IndexOf(sectionOrder, s.Name) >= 0)
            .OrderBy(s => Array.IndexOf(sectionOrder, s.Name)));
        foreach (var original in document.OriginalSections)
        {
            var section = Array.IndexOf(sectionOrder, original.Name) >= 0 ? standard.Dequeue() : original;
            if (section.Name.Length != 0) text.Append("\r\n[").Append(section.Name).Append("]\r\n");
            if (settingOrder.TryGetValue(section.Name, out var keys)) WriteSettings(text, section, keys);
            else
            {
                int first = 0, end = section.Lines.Count;
                while (first < end && string.IsNullOrWhiteSpace(section.Lines[first])) first++;
                while (end > first && string.IsNullOrWhiteSpace(section.Lines[end - 1])) end--;
                for (int i = first; i < end; i++)
                {
                    string line = section.Lines[i];
                    text.Append(line).Append("\r\n");
                }
            }
        }
        return text.ToString();
    }

    private static void WriteSettings(StringBuilder text, OsuSection section, string[] keys)
    {
        string separator = section.Name switch { "Colours" => " : ", "Metadata" or "Difficulty" => ":", _ => ": " };
        foreach (string line in section.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).OrderBy(Order))
            text.Append(SettingLine(line, separator)).Append("\r\n");

        long Order(string line)
        {
            string key = line.Split(':', 2)[0].Trim();
            if (section.Name == "Colours" && key.StartsWith("Combo", StringComparison.Ordinal)
                && int.TryParse(key.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int combo)) return combo;
            int index = Array.IndexOf(keys, key);
            return index < 0 ? long.MaxValue : section.Name == "Colours" ? (long)int.MaxValue + index + 1 : index;
        }
    }

    private static string SettingLine(string line, string separator)
    {
        int colon = line.IndexOf(':');
        if (colon < 1 || !OsuBeatmapReader.IsDataLine(line.Trim())) return line;
        return line[..colon].Trim() + separator + line[(colon + 1)..].Trim();
    }

    private static void ReplaceData(MapDocument document, string name, IEnumerable<string> data)
    {
        var sections = document.OriginalSections.Where(s => s.Name == name).ToArray();
        var target = sections.FirstOrDefault();
        if (target is null) { target = new OsuSection { Name = name }; document.OriginalSections.Add(target); }
        foreach (var section in sections) section.Lines.RemoveAll(l => string.IsNullOrWhiteSpace(l) || OsuBeatmapReader.IsDataLine(l.Trim()));
        target.Lines.AddRange(data);
        target.Lines.Add("");
    }
}
