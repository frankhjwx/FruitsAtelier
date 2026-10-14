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
        InitializePatternDock(); livePatternConvert = !patternDockExisting;
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
        inspectorRows.Add(("inspector.start", TimeRange(starts)));
        inspectorRows.Add(("inspector.end", TimeRange(ends)));
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
                if (speed is { } v && (InspectorPeakRatio is null || v > InspectorPeakRatio)) InspectorPeakRatio = v;
            }
            previous[item.SourceId] = item;
        }
        inspectorRows.Add(("inspector.maxDs", InspectorMaxDs is { } maxDs ? maxDs.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×" : "—"));
        inspectorRows.Add(("inspector.peak", InspectorPeakRatio is { } peak ? peak.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×" : "—"));
        inspectorRows.Add(("inspector.base", $"{Document.DistancePerBeat / 100:0.##}× SV · {Document.DistancePerBeat:0.##} DPB"));
    }
    private static string TimeRange(IEnumerable<double> values)
    {
        var data = values.ToArray();
        return data.Length == 0 ? "—" : data.Min() == data.Max() ? Time(data[0]) : Time(data.Min()) + " – " + Time(data.Max());
    }
    private void DrawSliderInspector(ICanvas c, Rect bounds)
    {
        if (!DockContentInputAvailable) return;
        inspectorBounds = bounds; RefreshSliderInspector();
        if (inspectorRows.Count == 0) { c.Text(L.Get("inspector.select"), bounds.X, bounds.Y + 12, 12, Muted, bounds.Width); return; }
        var horizontal = PrepareDockHorizontal(DockPanel.SliderInspector, bounds, 440);
        var viewport = horizontal.Viewport;
        float x = bounds.X - horizontal.Offset, contentWidth = horizontal.ContentWidth, cell = (contentWidth - 16) / 2;
        var actions = new List<(string Label, System.Action Action, bool Enabled, int Kind)>();
        bool enabled = ClipboardInteractionReady && !notesLocked;
        if (inspectorTargets.Length == 1 && Document.ImportedSliders.Any(s => s.Id == inspectorTargets[0]))
            actions.Add((L.Get("preview.convertSlider"), () => EditImportedSlider(inspectorTargets[0]), enabled, 1));
        if (inspectorHasSliders)
        {
            actions.Add((L.Get(SelectedStreamsOnly ? "conversion.editTitle" : "conversion.title"), ShowPatternDock, enabled && inspectorOnlySliders, 2));
            if (SelectedStreamsOnly)
            {
                actions.Add((L.Get("stream.breakFruits"), BreakSelectedStreams, enabled, 0));
                actions.Add((L.Get("stream.convertBack"), ConvertStreamsBack, enabled, 0));
            }
            actions.Add((L.Get("slider.clearInternal"), ClearSelectedInternalAnchors, CanClearSliderNodes, 0));
            actions.Add((L.Get("randomize.title"), ShowRandomDock, enabled && Document.Tracks.Any(t => inspectorTargets.Contains(t.Id) && t.StreamSnapDivisor is null), 0));
        }
        actions.Add((L.Get("merge.title"), OpenMergeDialog, enabled && inspectorMerge, 0));
        float metricsHeight = (inspectorRows.Count + 1) / 2 * 44;
        inspectorContentHeight = metricsHeight + 10 + actions.Count * 38;
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, inspectorContentHeight - viewport.Height));
        float y = bounds.Y - inspectorScroll;
        int firstHit = hits.Count; c.Clip(viewport);
        for (int i = 0; i < inspectorRows.Count; i++)
        {
            var row = inspectorRows[i]; float px = x + i % 2 * (cell + 16), py = y + i / 2 * 44;
            c.Text(L.Get(row.Key), px, py, 11, Muted, cell);
            c.Text(row.Value, px, py + 18, 13, Foreground, cell);
        }
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i]; var box = new Rect(x, y + metricsHeight + 10 + i * 38, contentWidth, 32);
            if (action.Kind == 1) LegacyConversionBounds = box;
            if (action.Kind == 2) StreamConversionBounds = box;
            Button(c, box, action.Label, action.Action, enabled: action.Enabled, fontSize: 12);
        }
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
        {
            var box = hits[i].Bounds;
            if (box.Y < viewport.Y || box.Bottom > viewport.Bottom || box.Right <= viewport.X || box.X >= viewport.Right) hits.RemoveAt(i);
        }
        if (inspectorContentHeight > viewport.Height)
        {
            float thumb = Math.Max(20, viewport.Height * viewport.Height / inspectorContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + inspectorScroll / (inspectorContentHeight - viewport.Height) * (viewport.Height - thumb), 3, thumb), Muted, 1);
        }
        DrawDockHorizontal(c, horizontal);
    }
}
