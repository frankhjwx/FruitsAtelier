using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private OsuWriteResult? inspectorExport;
    private MapDocument? inspectorSnapshot;
    private Guid[] inspectorTargets = [];
    private readonly List<(string Key, string Value)> inspectorRows = [];
    private Rect inspectorBounds;
    private float inspectorScroll, inspectorContentHeight;
    private bool inspectorMerge, inspectorHasSliders, inspectorOnlySliders;
    internal double? InspectorPeakRatio { get; private set; }
    internal double? InspectorMaxDs { get; private set; }
    private double inspectorPeakTime;
    internal Rect SliderInspectorViewport => inspectorBounds;

    internal void ShowSliderInspector()
    {
        AddDockPanel(DockPanel.SliderInspector, false);
        var group = PanelGroup(DockPanel.SliderInspector); group.Active = DockPanel.SliderInspector; group.Collapsed = false;
        menu = -1; contextItems.Clear();
    }
    private void ShowPatternDock()
    {
        AddDockPanel(DockPanel.PatternEditor, false);
        var group = PanelGroup(DockPanel.PatternEditor); group.Active = DockPanel.PatternEditor; group.Collapsed = false;
        ResetPatternDock(); menu = -1; contextItems.Clear();
        patternDockSnapshot = convertedSnapshot; patternDockTargets = ClipboardSelectedParentIds().ToArray();
        patternDockSliders = true; patternDockBananas = false; patternDockExisting = SelectedStreamsOnly; patternDockConvert = true;
    }
    internal static double? HorizontalSpeedRatio(MapDocument document, TimingMap.Lookup timing, double[] boundaries, ConvertedCatchObject from, ConvertedCatchObject to)
    {
        if (to.TimeMs <= from.TimeMs) return null;
        double time = from.TimeMs, distance = 0;
        int index = Array.BinarySearch(boundaries, time);
        index = index < 0 ? ~index : index + 1;
        while (time < to.TimeMs)
        {
            double end = index < boundaries.Length ? Math.Min(to.TimeMs, boundaries[index]) : to.TimeMs;
            distance += (end - time) * document.DistancePerBeat / timing.At(time).BeatLengthMs;
            time = end; index++;
        }
        return distance > 0 ? Math.Abs(to.X - from.X) / distance : null;
    }
    private void RefreshSliderInspector()
    {
        var ids = ClipboardSelectedParentIds();
        if (ReferenceEquals(inspectorSnapshot, convertedSnapshot) && ReferenceEquals(inspectorExport, playableExport)
            && inspectorTargets.Length == ids.Count && inspectorTargets.All(ids.Contains)) return;
        inspectorSnapshot = convertedSnapshot; inspectorExport = playableExport; inspectorTargets = ids.ToArray();
        inspectorRows.Clear(); InspectorPeakRatio = InspectorMaxDs = null; inspectorMerge = inspectorHasSliders = inspectorOnlySliders = false; inspectorScroll = 0;
        var tracks = Document.Tracks.Where(t => ids.Contains(t.Id)).ToArray();
        var imported = Document.ImportedSliders.Where(t => ids.Contains(t.Id)).ToArray();
        if (ids.Count == 0 || conversion is null) return;
        inspectorMerge = ObjectStructureEditing.MergeSelection(Document, ids).Error is null;
        inspectorHasSliders = tracks.Length + imported.Length > 0;
        inspectorOnlySliders = tracks.Length + imported.Length == ids.Count;
        if (!inspectorOnlySliders)
        {
            if (inspectorMerge) inspectorRows.Add(("inspector.selection", ids.Count.ToString()));
            return;
        }
        var generated = conversion.Sliders.Where(s => ids.Contains(s.SourceId)).ToArray();
        string Range(IEnumerable<double> values, string format = "0.##")
        {
            var data = values.ToArray(); if (data.Length == 0) return "—";
            double min = data.Min(), max = data.Max();
            return min == max ? min.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
                : min.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + " – " + max.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }
        var starts = tracks.Select(t => t.Nodes[0].TimeMs).Concat(imported.Select(t => t.TimeMs)).ToArray();
        var ends = tracks.Select(CurveMath.EndTimeMs).Concat(imported.Select(t => (playableExport is { } result && result.PlayableEndTimes.TryGetValue(t.Id, out var end) ? (double?)end : null)
            ?? generated.FirstOrDefault(g => g.SourceId == t.Id)?.StartTimeMs + generated.FirstOrDefault(g => g.SourceId == t.Id)?.DurationMs ?? t.TimeMs)).ToArray();
        inspectorRows.Add(("inspector.selection", ids.Count.ToString()));
        inspectorRows.Add(("inspector.type", string.Join(" / ", tracks.Select(t => t.StreamSnapDivisor is null ? "FSlider" : t.Stack is null ? "Stream" : "Stack")
            .Concat(imported.Select(_ => "Legacy Slider")).Distinct())));
        inspectorRows.Add(("inspector.start", Range(starts) + " ms"));
        inspectorRows.Add(("inspector.end", Range(ends) + " ms"));
        inspectorRows.Add(("inspector.duration", Range(ends.Zip(starts, (end, start) => end - start)) + " ms"));
        inspectorRows.Add(("inspector.spans", Range(tracks.Select(t => (double)t.SpanCount).Concat(imported.Select(t => (double)t.SpanCount)))));
        inspectorRows.Add(("inspector.controls", Range(tracks.Select(t => (double)t.Nodes.Count).Concat(imported.Select(t => (double)t.ControlPoints.Count)))));
        if (playableExport is { } exported)
        {
            var exportedTiming = new TimingMap.Lookup(exported.ReadBack);
            var sliders = exported.ReadBack.ImportedSliders.Where(s => s.SourceOrder >= 0 && s.SourceOrder < exported.ObjectSources.Count && ids.Contains(exported.ObjectSources[s.SourceOrder])).ToArray();
            inspectorRows.Add(("inspector.exportSv", Range(sliders.Select(s => exportedTiming.At(s.TimeMs).SliderVelocityMultiplier)) + (sliders.Length > 0 ? "×" : "")));
            inspectorRows.Add(("inspector.length", Range(sliders.Select(s => s.PixelLength))));
        }
        var timing = renderedTiming ?? new TimingMap.Lookup(Document);
        var boundaries = Document.TimingPoints.Where(t => t.Uninherited).Select(t => t.TimeMs).Distinct().Order().ToArray();
        var previous = new Dictionary<Guid, ConvertedCatchObject>();
        foreach (var item in playableObjects)
        {
            if (!ids.Contains(item.SourceId) || item.Kind == CatchObjectKind.Banana) continue;
            if (previous.TryGetValue(item.SourceId, out var from))
            {
                var ds = BaseDistanceRatio(from, item);
                if (ds is { } d && (InspectorMaxDs is null || d > InspectorMaxDs)) InspectorMaxDs = d;
                var speed = HorizontalSpeedRatio(Document, timing, boundaries, from, item);
                if (speed is { } v && (InspectorPeakRatio is null || v > InspectorPeakRatio)) { InspectorPeakRatio = v; inspectorPeakTime = item.TimeMs; }
            }
            previous[item.SourceId] = item;
        }
        inspectorRows.Add(("inspector.maxDs", InspectorMaxDs is { } maxDs ? maxDs.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×" : "—"));
        inspectorRows.Add(("inspector.peak", InspectorPeakRatio is { } peak ? peak.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×" : "—"));
        inspectorRows.Add(("inspector.base", $"{Document.DistancePerBeat / 100:0.##}× SV · {Document.DistancePerBeat:0.##} DPB"));
    }
    private void DrawSliderInspector(ICanvas c, Rect bounds)
    {
        if (!DockContentInputAvailable) return;
        inspectorBounds = bounds; RefreshSliderInspector();
        if (inspectorRows.Count == 0) { c.Text(L.Get("inspector.select"), bounds.X, bounds.Y + 12, 12, Muted, bounds.Width); return; }
        inspectorContentHeight = inspectorRows.Count * 48 + 330;
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, inspectorContentHeight - bounds.Height));
        float y = bounds.Y - inspectorScroll;
        int firstHit = hits.Count; c.Clip(bounds);
        foreach (var row in inspectorRows)
        { c.Text(L.Get(row.Key), bounds.X, y, 11, Muted, bounds.Width); c.Text(row.Value, bounds.X, y + 19, 13, Foreground, bounds.Width); y += 48; }
        if (InspectorPeakRatio is not null)
            Action(L.Get("inspector.locate", Time(inspectorPeakTime)), () => SeekTo(inspectorPeakTime));
        bool enabled = ClipboardInteractionReady && !notesLocked;
        if (inspectorTargets.Length == 1 && Document.ImportedSliders.Any(s => s.Id == inspectorTargets[0]))
        { LegacyConversionBounds = new(bounds.X, y, bounds.Width, 32); Action(L.Get("preview.convertSlider"), () => EditImportedSlider(inspectorTargets[0]), enabled); }
        if (inspectorHasSliders)
        {
        StreamConversionBounds = new(bounds.X, y, bounds.Width, 32);
        Action(L.Get(SelectedStreamsOnly ? "conversion.editTitle" : "conversion.title"), ShowPatternDock, enabled && inspectorOnlySliders);
        if (SelectedStreamsOnly)
        { Action(L.Get("stream.breakFruits"), BreakSelectedStreams, enabled); Action(L.Get("stream.convertBack"), ConvertStreamsBack, enabled); }
        Action(L.Get("slider.clearInternal"), ClearSelectedInternalAnchors, CanClearSliderNodes);
        Action(L.Get("randomize.title"), ShowRandomDock, enabled && Document.Tracks.Any(t => inspectorTargets.Contains(t.Id) && t.StreamSnapDivisor is null));
        }
        Action(L.Get("merge.title"), OpenMergeDialog, enabled && inspectorMerge);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--) if (hits[i].Bounds.Y < bounds.Y || hits[i].Bounds.Bottom > bounds.Bottom) hits.RemoveAt(i);
        if (inspectorContentHeight > bounds.Height)
        {
            float thumb = Math.Max(20, bounds.Height * bounds.Height / inspectorContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + inspectorScroll / (inspectorContentHeight - bounds.Height) * (bounds.Height - thumb), 3, thumb), Muted, 1);
        }
        void Action(string label, System.Action action, bool available = true)
        { Button(c, new(bounds.X, y, bounds.Width, 32), label, action, enabled: available, fontSize: 12); y += 38; }
    }
}
