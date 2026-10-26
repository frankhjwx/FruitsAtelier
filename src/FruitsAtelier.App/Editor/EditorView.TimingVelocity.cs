using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool showTimingSv, timingSvLocked = true;
    private TimingPoint[] displayedTiming = [];
    private readonly HashSet<TimingPoint> svOnlyTiming = [];
    private const uint TimingSvGreen = 0x246348, TimingLockPink = 0xF18BB8;
    internal Rect TimingSvDisplayBounds { get; private set; }
    internal Rect TimingSvLockBounds { get; private set; }
    internal bool ShowTimingSv => showTimingSv;
    internal bool TimingSvLocked => timingSvLocked;
    internal IReadOnlyList<TimingPoint> DisplayedTiming => displayedTiming;

    private void RefreshDisplayedTiming()
    {
        displayedTiming = (showTimingSv ? playableExport?.ReadBack.TimingPoints ?? [] : Document.TimingPoints)
            .OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        svOnlyTiming.Clear();
        TimingPoint? previous = null;
        foreach (var point in displayedTiming)
        {
            if (!point.Uninherited && previous is not null && SameTimingSamples(point, previous)) svOnlyTiming.Add(point);
            previous = point;
        }
    }

    private static bool SameTimingSamples(TimingPoint a, TimingPoint b) => a.SampleSet == b.SampleSet
        && a.SampleIndex == b.SampleIndex && a.Volume == b.Volume && a.Effects == b.Effects;

    private void ToggleTimingSvDisplay()
    {
        if (!CommitTimingField()) return;
        if (TimingSetupVisible && (timingUndo.Count > 0 || timingRedo.Count > 0))
        { timingError = L.Get("timing.finishDraft"); return; }
        showTimingSv = !showTimingSv;
        RefreshDisplayedTiming();
        if (TimingSetupVisible) OpenTimingSetup();
    }

    private void DrawTimingMarker(ICanvas c, TimingPoint point, float x, float top, float bottom, float opacity)
    {
        c.Line(x, top, x, bottom, point.Uninherited ? 0xEA2222u
            : showTimingSv && svOnlyTiming.Contains(point) ? TimingSvGreen : 0x7BC600u, 1, opacity);
        if (showTimingSv && timingSvLocked && svOnlyTiming.Contains(point))
            c.Line(x, top, x, bottom, TimingLockPink, 3, opacity * .35f);
    }

    private bool TimingStructureEditable(TimingEntry entry) => !entry.Derived && (!showTimingSv || !timingSvLocked || !entry.SvOnly);

    private TimingEntry CreateTimingEntry(TimingPoint point, int sourceIndex)
    {
        var original = sourceIndex < 0 ? null : Document.TimingPoints[sourceIndex];
        return new(timingNextId++, original is null ? null : TimingEditing.Copy(original), TimingEditing.Copy(point))
        {
            SourceIndex = sourceIndex, Baseline = TimingEditing.Copy(point), Derived = showTimingSv && original is null,
            SvOnly = svOnlyTiming.Contains(point)
        };
    }

    private void ApplyDisplayedTiming()
    {
        var points = new List<TimingPoint>();
        var changes = new List<(TimingPoint Before, TimingPoint After)>();
        var replacements = new Dictionary<int, TimingPoint>();
        foreach (var entry in timingEntries)
        {
            var p = entry.Point;
            var baseline = entry.Baseline;
            if (entry.Original is null && baseline is not null && TimingEditing.Serialize([p]) == TimingEditing.Serialize([baseline])) continue;
            if (entry.Original is not null && baseline is not null && TimingEditing.Serialize([p]) == TimingEditing.Serialize([baseline]))
            { replacements[entry.SourceIndex] = TimingEditing.Copy(entry.Original); continue; }
            var next = TimingEditing.Copy(entry.Original ?? p);
            if (baseline is null)
            {
                if (!next.Uninherited) next.BeatLengthMs *= Document.SliderMultiplier / Document.EffectiveSliderMultiplier;
                points.Add(next); continue;
            }
            if (p.TimeMs != baseline.TimeMs) next.TimeMs = p.TimeMs;
            if (p.BeatLengthMs != baseline.BeatLengthMs)
            {
                next.BeatLengthMs = p.Uninherited ? p.BeatLengthMs
                    : p.BeatLengthMs * Document.SliderMultiplier / Document.EffectiveSliderMultiplier;
                if (!p.Uninherited) ApplyTimingSvToSliders(baseline.TimeMs, -100 / p.BeatLengthMs);
            }
            else if (entry.Original is null) next.BeatLengthMs = p.Uninherited ? p.BeatLengthMs : -100;
            if (p.Uninherited != baseline.Uninherited) next.Uninherited = p.Uninherited;
            if (p.Meter != baseline.Meter) next.Meter = p.Meter;
            if (p.SampleSet != baseline.SampleSet) next.SampleSet = p.SampleSet;
            if (p.SampleIndex != baseline.SampleIndex) next.SampleIndex = p.SampleIndex;
            if (p.Volume != baseline.Volume) next.Volume = p.Volume;
            if (p.Effects != baseline.Effects) next.Effects = p.Effects;
            next.OriginalLine = null;
            if (entry.SourceIndex >= 0) replacements[entry.SourceIndex] = next;
            else points.Add(next);
            if (entry.Original is not null) changes.Add((entry.Original, next));
        }
        var authored = Document.TimingPoints.Select((p, i) => (Point: p, Index: i))
            .Where(item => !timingDisplayOriginals.Contains(item.Index) || replacements.ContainsKey(item.Index))
            .Select(item => replacements.GetValueOrDefault(item.Index) ?? TimingEditing.Copy(item.Point)).ToList();
        authored.AddRange(points);
        TimingEditing.Apply(Document, authored, changes, new(timingScale, timingResnap, timingLengths, timingBookmarks, timingSnap));
        if (timingEntries.Any(e => !e.Point.Uninherited && e.Baseline is not null && e.Point.BeatLengthMs != e.Baseline.BeatLengthMs))
        {
            var output = OsuBeatmapWriter.Serialize(Document, compensateTinyDroplets, editorWriteCache);
            if (!output.ObjectSequenceMatches) throw new InvalidDataException(string.Join(L.Get("core.diagnostics.separator"), output.Diagnostics));
        }
    }

    private HashSet<int> timingDisplayOriginals = [];

    private void ApplyTimingSvToSliders(double time, double sv)
    {
        double end = timingEntries.Where(e => e.Baseline is not null && e.Baseline.TimeMs > time)
            .Select(e => e.Baseline!.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min();
        bool Contains(double head) => OsuBeatmapWriter.QuantizeTime(head) >= time && OsuBeatmapWriter.QuantizeTime(head) < end;
        double authoredSv = sv * Document.EffectiveSliderMultiplier / Document.SliderMultiplier;
        foreach (var slider in Document.ImportedSliders.Where(s => Contains(s.TimeMs)))
            slider.SliderVelocityMultiplier = authoredSv;
        foreach (var track in Document.Tracks.Where(t => t.StreamSnapDivisor is null && t.Nodes.Count > 0 && Contains(t.Nodes[0].TimeMs)))
            track.SliderVelocityMultiplier = authoredSv;
    }
}
