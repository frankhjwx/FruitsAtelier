using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private OsuWriteResult? inspectorExport;
    private MapDocument? inspectorSnapshot;
    private Guid[] inspectorTargets = [];
    private HashSet<Guid> inspectorTargetSet = [];
    private readonly List<(string Key, string Value)> inspectorRows = [];
    private Rect inspectorBounds;
    private float inspectorScroll, inspectorContentHeight;
    private bool inspectorMerge, inspectorHasSliders, inspectorOnlySliders;
    private bool inspectorSpeedMode;
    private bool osuShowProcessedSliders;
    private readonly HashSet<int> inspectorCollapsedSections = [];
    private readonly Dictionary<int, Rect> inspectorSectionBounds = [];
    internal Rect SliderInspectorSectionBounds(int section) => inspectorSectionBounds.GetValueOrDefault(section);

    internal void SetOsuSliderDisplay(bool processed)
    {
        osuShowProcessedSliders = processed;
        osuCanvasSource = null;
        osuControlSlider = Guid.Empty; osuSelectedControl = -1;
    }

    private void OpenOsuSliderDisplayMenu()
    {
        contextItems.Clear(); menu = -1; languageMenuOpen = false;
        foreach (bool processed in new[] { false, true })
            contextItems.Add(new(L.Get(processed ? "inspector.pathProcessed" : "inspector.pathDrawn"), () =>
            { SetOsuSliderDisplay(processed); }, Active: osuShowProcessedSliders == processed));
        float w = Math.Min(300, width);
        contextBounds = new(Math.Clamp(mouseX, 0, width - w),
            Math.Clamp(mouseY + 4, 0, Math.Max(0, height - ContextMenuHeight)), w, ContextMenuHeight);
    }

    internal double? InspectorPeakRatio { get; private set; }
    internal double? InspectorMaxDs { get; private set; }
    internal Rect SliderInspectorViewport => inspectorBounds;
    internal Rect LegacySliderSvBounds => fields.FirstOrDefault(f => f.Label == L.Get("legacySv.label"))?.Bounds ?? default;

    internal void ShowSliderInspector()
    {
        AddDockPanel(DockPanel.SliderInspector, false);
        var group = PanelGroup(DockPanel.SliderInspector); group.Active = DockPanel.SliderInspector; group.Collapsed = false;
        menu = -1; contextItems.Clear();
    }
    private void ShowPatternDock()
    {
        if (IsOsuCanvas) { OpenOsuStream(); return; }
        if (!ClipboardInteractionReady || notesLocked) return;
        AddDockPanel(DockPanel.PatternEditor, false);
        var group = PanelGroup(DockPanel.PatternEditor); group.Active = DockPanel.PatternEditor; group.Collapsed = false;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        var selected = ClipboardSelectedParentIds();
        if (patternDockKind != 0 && ReferenceEquals(patternDockSnapshot, convertedSnapshot)
            && selected.Count == patternDockTargets.Length && patternDockTargets.All(selected.Contains)) return;
        ResetPatternDock();
        bool bananas = CanEditBananas;
        if (!bananas && (!CanConvertStream || selected.Any(id => !Document.Tracks.Any(t => t.Id == id)
            && !Document.ImportedSliders.Any(s => s.Id == id)))) return;
        patternDockSnapshot = convertedSnapshot; patternDockTargets = ClipboardSelectedParentIds().ToArray();
        patternDockSliders = !bananas; patternDockBananas = bananas;
        patternDockExisting = bananas ? Document.BananaShowers.Where(s => selected.Contains(s.Id)).All(s => s.Pattern is not null) : SelectedStreamsOnly;
        patternDockConvert = true;
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
            && inspectorSpeedMode == UsesSpeedDistanceSnap
            && inspectorTargets.Length == ids.Count && inspectorTargets.All(ids.Contains)) return;
        inspectorSpeedMode = UsesSpeedDistanceSnap;
        inspectorSnapshot = convertedSnapshot; inspectorExport = playableExport; inspectorTargets = ids.ToArray(); inspectorTargetSet = ids;
        var merge = ObjectStructureEditing.MergeSelection(Document, ids);
        mergeTargets = merge.Ids; mergeAllowsLinear = merge.AllowsLinear; mergeCurved = !mergeAllowsLinear; mergeError = "";
        inspectorMergeExpanded = false;
        inspectorRows.Clear(); InspectorPeakRatio = InspectorMaxDs = null; inspectorMerge = inspectorHasSliders = inspectorOnlySliders = false; inspectorScroll = 0;
        var tracks = Document.Tracks.Where(t => ids.Contains(t.Id)).ToArray();
        var imported = Document.ImportedSliders.Where(t => ids.Contains(t.Id)).ToArray();
        if (ids.Count == 0 || conversion is null) return;
        inspectorMerge = merge.Error is null;
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
        RefreshSliderSnapLabels();
        if (inspectorTargets.Length == 1 && sliderSnapLabels.TryGetValue(inspectorTargets[0], out var snapLabels))
        {
            inspectorRows.Add(("sliderSnap.startPosition", snapLabels.StartPosition));
            inspectorRows.Add(("sliderSnap.endPosition", snapLabels.EndPosition));
        }
        inspectorRows.Add(("inspector.duration", Range(ends.Zip(starts, (end, start) => end - start)) + " ms"));
        inspectorRows.Add(("inspector.spans", Range(tracks.Select(t => (double)t.SpanCount).Concat(imported.Select(t => (double)t.SpanCount)))));
        var derandomization = tracks.Where(t => t.StreamSnapDivisor is null)
            .Select(t => !t.RandomizesDroplets(Document))
            .Concat(imported.Select(s => s.RandomizationTrack is not null && Document.DerandomizeFSliderDroplets)).Distinct().ToArray();
        inspectorRows.Add(("inspector.dropletEffective", derandomization.Length == 0 ? "—"
            : derandomization.Length > 1 ? L.Get("inspector.mixed")
            : L.Get(derandomization[0] ? "droplet.mode.Derandomize" : "droplet.mode.Randomize")));
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
                double? horizontalSpeed = item.TimeMs > from.TimeMs
                    ? Math.Abs(item.X - from.X) / (item.TimeMs - from.TimeMs) : null;
                var ds = inspectorSpeedMode ? horizontalSpeed : BaseDistanceRatio(from, item);
                if (ds is { } d && (InspectorMaxDs is null || d > InspectorMaxDs)) InspectorMaxDs = d;
                var speed = inspectorSpeedMode ? horizontalSpeed : HorizontalSpeedRatio(Document, timing, boundaries, from, item);
                if (speed is { } v && (InspectorPeakRatio is null || v > InspectorPeakRatio)) InspectorPeakRatio = v;
            }
            previous[item.SourceId] = item;
        }
        string Metric(double? value) => value is { } number
            ? inspectorSpeedMode ? L.Get("ds.lazerSpeed", number)
                : number.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×" : "—";
        inspectorRows.Add(inspectorSpeedMode
            ? ("inspector.peak", Metric(InspectorPeakRatio))
            : ("inspector.maxDs", Metric(InspectorMaxDs)));
        inspectorRows.Add(("inspector.base", $"{Document.DistancePerBeat / 100:0.##}× SV · {Document.DistancePerBeat:0.##} DPB"));
    }
    private static string TimeRange(IEnumerable<double> values)
    {
        var data = values.ToArray();
        return data.Length == 0 ? "—" : data.Min() == data.Max() ? Time(data[0]) : Time(data.Min()) + " – " + Time(data.Max());
    }
    private List<(string Label, System.Action Action, bool Enabled, int Kind)> SliderInspectorActions()
    {
        var actions = new List<(string Label, System.Action Action, bool Enabled, int Kind)>();
        bool enabled = ClipboardInteractionReady && !notesLocked;
        if (inspectorTargets.Length == 1 && (Document.Tracks.Any(t => t.Id == inspectorTargets[0] && t.StreamSnapDivisor is null)
            || Document.ImportedSliders.Any(t => t.Id == inspectorTargets[0]))) Section(1, "inspector.section.timing");
        AddEndpointSnapActions(actions, enabled);
        if (inspectorTargets.Length == 1 && Document.ImportedSliders.Any(s => s.Id == inspectorTargets[0]))
            actions.Add((L.Get("legacySv.label"), () => { }, !notesLocked && drag == DragKind.None
                && draftTrack == Guid.Empty && draftBanana == Guid.Empty && !LegacyDropletRefreshBusy, 3));
        if (inspectorHasSliders) Section(2, "inspector.section.conversion");
        if (IsOsuCanvas && Document.ImportedSliders.Any(s => inspectorTargetSet.Contains(s.Id)))
            actions.Add((L.Get("inspector.pathDisplay", L.Get(osuShowProcessedSliders ? "inspector.pathProcessed" : "inspector.pathDrawn")),
                OpenOsuSliderDisplayMenu, drag == DragKind.None && osuDraftSlider is null, 0));
        if (inspectorTargets.Length == 1 && Document.Tracks.Any(s => s.Id == inspectorTargets[0] && s.StreamSnapDivisor is null))
            actions.Add((L.Get("osu.convertLegacy"), () => ConvertSelectedSliderRepresentation(inspectorTargets[0]), enabled, 0));
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
            var dropletTracks = InspectorDropletTracks();
            if (dropletTracks.Length > 0)
            {
                Section(3, "inspector.section.droplets");
                var modes = dropletTracks.Select(InspectorDropletMode).Distinct().ToArray();
                string mode = modes.Length == 1 ? L.Get("droplet.mode." + modes[0]) : L.Get("inspector.mixed");
                actions.Add((L.Get("inspector.dropletMode", mode), OpenInspectorDropletMenu, enabled, 0));
                int adjustments = dropletTracks.Sum(t => t.DropletRandomization?.Adjustments.Count ?? 0);
                actions.Add((L.Get(adjustments > 0 && dropletTracks.Where(t => t.DropletRandomization?.Adjustments.Count > 0).All(t => !t.RandomizesDroplets(Document))
                    ? "inspector.resetDropletsInactive" : "inspector.resetDroplets", adjustments), ResetInspectorDroplets, enabled && adjustments > 0, 0));
            }
        }
        Section(4, "inspector.section.structure");
        if (inspectorHasSliders)
            actions.Add((L.Get("slider.clearInternal"), ClearSelectedInternalAnchors, CanClearSliderNodes, 0));
        actions.Add((L.Get("merge.title"), ShowMergeInspector, enabled && inspectorMerge, 0));
        return actions;

        void Section(int section, string key) => actions.Add((L.Get(key), () =>
        {
            if (!inspectorCollapsedSections.Add(section)) inspectorCollapsedSections.Remove(section);
        }, true, -section));
    }
    private CurveTrack[] InspectorDropletTracks() => Document.Tracks
        .Where(t => t.StreamSnapDivisor is null && inspectorTargetSet.Contains(t.Id)).ToArray();

    private SliderDropletMode InspectorDropletMode(CurveTrack track) => track.DropletMode
        ?? (track.DropletRandomization is { Enabled: true } ? SliderDropletMode.FollowMap : SliderDropletMode.Derandomize);

    private void OpenInspectorDropletMenu()
    {
        var targets = InspectorDropletTracks().Select(t => t.Id).ToHashSet();
        var modes = InspectorDropletTracks().Select(InspectorDropletMode).Distinct().ToArray();
        menu = -1; languageMenuOpen = false; contextItems.Clear();
        foreach (var mode in Enum.GetValues<SliderDropletMode>())
            contextItems.Add(new(L.Get("droplet.mode." + mode), () =>
            {
                if (!ClipboardInteractionReady || notesLocked) return;
                Edit(L.Get("inspector.dropletMode", L.Get("droplet.mode." + mode)), () =>
                {
                    foreach (var track in Document.Tracks.Where(t => t.StreamSnapDivisor is null && targets.Contains(t.Id)))
                        track.DropletMode = mode;
                });
                LibrarySettings.DefaultDropletMode = mode;
                RequestViewPreference?.Invoke();
            }, Active: modes.Length == 1 && modes[0] == mode));
        float menuWidth = Math.Min(280, width), menuHeight = ContextMenuHeight;
        contextBounds = new(Math.Clamp(mouseX, 0, width - menuWidth),
            Math.Clamp(mouseY + 4, 0, Math.Max(0, height - menuHeight)), menuWidth, menuHeight);
    }

    private void ResetInspectorDroplets()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        var targets = InspectorDropletTracks().Select(t => t.Id).ToHashSet();
        Edit(L.Get("randomize.resetAdjustments"), () =>
        {
            foreach (var track in Document.Tracks.Where(t => t.StreamSnapDivisor is null && targets.Contains(t.Id)))
                track.DropletRandomization?.Adjustments.Clear();
        });
    }

    private void DrawSliderInspector(ICanvas c, Rect bounds)
    {
        SliderEndpointToggleBounds = SliderEndpointStartBounds = SliderEndpointEndBounds = default;
        inspectorSectionBounds.Clear();
        if (!DockContentInputAvailable) return;
        inspectorBounds = bounds; RefreshSliderInspector();
        if (inspectorRows.Count == 0) { c.Text(L.Get("inspector.select"), bounds.X, bounds.Y + 12, 12, Muted, bounds.Width); return; }
        var viewport = bounds;
        float x = bounds.X, contentWidth = bounds.Width;
        bool selectionOnly = inspectorRows.Count == 1 && inspectorRows[0].Key == "inspector.selection";
        int columns = contentWidth < 250 ? 1 : 2;
        float cell = (contentWidth - (columns - 1) * 16) / columns;
        var actions = SliderInspectorActions();
        bool collapsed = false;
        actions.RemoveAll(action =>
        {
            if (action.Kind < 0) { collapsed = inspectorCollapsedSections.Contains(-action.Kind); return false; }
            return collapsed;
        });
        if (inspectorMergeExpanded && inspectorMerge) actions.RemoveAll(a => a.Label == L.Get("merge.title"));
        bool enabled = ClipboardInteractionReady && !notesLocked;
        float metricsHeight = selectionOnly ? 32 : (inspectorRows.Count + columns - 1) / columns * 44;
        float mergeHeight = inspectorMerge && inspectorMergeExpanded && !inspectorCollapsedSections.Contains(4) ? MergeControlsHeight(c, contentWidth) : 0;
        inspectorContentHeight = metricsHeight + 10 + actions.Sum(a => a.Kind < 0 ? 32 : 38) + mergeHeight;
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, inspectorContentHeight - viewport.Height));
        float y = bounds.Y - inspectorScroll;
        int firstHit = hits.Count; c.Clip(viewport);
        for (int i = 0; i < inspectorRows.Count; i++)
        {
            var row = inspectorRows[i]; float px = x + i % columns * (cell + 16), py = y + i / columns * 44;
            if (selectionOnly)
            {
                c.Text(L.Get(row.Key), x, y + 4, 12, Muted, contentWidth - 45);
                c.Text(row.Value, x + contentWidth - 40, y + 4, 13, Foreground, 40, true);
                continue;
            }
            c.Text(L.Get(row.Key), px, py, 11, Muted, cell);
            c.Text(row.Value, px, py + 18, 13, Foreground, cell);
        }
        float actionY = y + metricsHeight + 10;
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var box = new Rect(x, actionY, contentWidth, action.Kind < 0 ? 26 : 32);
            actionY += action.Kind < 0 ? 32 : 38;
            if (action.Kind < 0)
            {
                if (box.Y >= viewport.Y && box.Bottom <= viewport.Bottom) inspectorSectionBounds[-action.Kind] = box;
                bool closed = inspectorCollapsedSections.Contains(-action.Kind);
                float tabWidth = Math.Min(contentWidth, c.MeasureText(action.Label, 11, true) + 36);
                Button(c, box with { Width = tabWidth }, (closed ? "▸ " : "▾ ") + action.Label,
                    action.Action, fontSize: 11, bold: true);
                c.Line(x + tabWidth, box.Bottom, x + contentWidth, box.Bottom, Grid);
                continue;
            }
            if (action.Kind == 1) LegacyConversionBounds = box;
            if (action.Kind == 2) StreamConversionBounds = box;
            if (box.Y >= viewport.Y && box.Bottom <= viewport.Bottom)
            {
                if (action.Kind == 4) SliderEndpointToggleBounds = box;
                if (action.Kind == 5) SliderEndpointStartBounds = box;
                if (action.Kind == 6) SliderEndpointEndBounds = box;
            }
            if (action.Kind == 3 && action.Enabled)
            {
                var slider = Document.ImportedSliders.Single(s => s.Id == inspectorTargets[0]);
                double value = slider.SliderVelocityMultiplier ?? ImportedSliderConverter.SliderVelocityAt(Document, slider);
                var targets = inspectorTargetSet.ToHashSet();
                float fieldY = box.Y;
                Field(c, box.X, ref fieldY, box.Width, action.Label, value,
                    v => LegacySliderVelocityEditing.Apply(Document, targets, v), Math.Min(110, box.Width * .55f));
            }
            else
                Button(c, box, action.Label, action.Action, enabled: action.Enabled, fontSize: 12);
        }
        if (mergeHeight > 0)
            DrawMergeControls(c, new(x, actionY, contentWidth, mergeHeight), enabled);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
        {
            var box = hits[i].Bounds;
            if (box.Y < viewport.Y || box.Bottom > viewport.Bottom || box.Right <= viewport.X || box.X >= viewport.Right) hits.RemoveAt(i);
        }
        fields.RemoveAll(f => f.Label == L.Get("legacySv.label")
            && (f.Bounds.Y < viewport.Y || f.Bounds.Bottom > viewport.Bottom));
        if (inspectorContentHeight > viewport.Height)
        {
            float thumb = Math.Max(20, viewport.Height * viewport.Height / inspectorContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + inspectorScroll / (inspectorContentHeight - viewport.Height) * (viewport.Height - thumb), 3, thumb), Muted, 1);
        }
    }
}
