using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool HitsoundingPageVisible { get; private set; }
    private double hitsoundingSpan = 4000;
    private double hitsoundingHeadRatio = .3;
    internal const float HitsoundingTagWidth = 36;
    private double HitsoundingStart => playhead - hitsoundingSpan * hitsoundingHeadRatio;
    private readonly List<BeatGridLine> hitsoundingGrid = [];
    private readonly List<(Rect Bounds, float Anchor, uint Color, string Bank, int Index, int Volume, TimingPoint Point)> hitsoundingTimingBoxes = [];
    private CatchConversionResult? hitsoundingConversion;
    private HitsoundResolver? hitsoundingResolver;
    private sealed record SoundTag(double Time, int Lane, string Label, int Index, double Next, Hitsound[] Sounds, double NeighbourDistance = double.PositiveInfinity, int Column = 0, int Columns = 1, string WidthText = "", ConvertedCatchObject? Note = null, StoryboardSample? Storyboard = null);
    private readonly HitsoundingAudioCache hitsoundingAudio = new();
    private bool HitsoundingPrecise => hitsoundingDockWorkspace.PreciseHitsounds;
    private SoundTag[][] hitsoundingTagLanes = [];
    private SoundTag[] hitsoundingStoryboardTags = [];
    private StoryboardScene? hitsoundingStoryboardSource;
    private readonly record struct HitsoundingTimingLabel(double Time, bool Red, string Bank, int Index, int Volume, TimingPoint Point);
    private HitsoundingTimingLabel[] hitsoundingTimingLabels = [];
    private ConvertedCatchObject[] hitsoundingNotes = [];
    private Dictionary<(Guid, int), ConvertedCatchObject> hitsoundingNoteLookup = [];
    private HashSet<(Guid, int)> hitsoundingAudibleNotes = [];
    private (double Time, int First, int Last)[] hitsoundingAdditionLinks = [];
    private double[] hitsoundingAdditionTimes = [];
    private TimingPoint[] hitsoundingRedPoints = [], hitsoundingGreenPoints = [];
    private Rect hitsoundingBounds, hitsoundingGridBounds;
    private int hitsoundingBank = 2, hitsoundingIndex = 1;
    private int[] hitsoundingSets = [0];
    private string[] hitsoundingLibraryFiles = [];
    private string? hitsoundingLibraryRoot;
    private string? hitsoundingDragFile;
    private DockGroup? hitsoundingSplitDrag, hitsoundingSplitOwner;
    private Rect hitsoundingSplitBounds, hitsoundingDockBounds, hitsoundingLibraryBounds;
    private readonly List<(Rect Bounds, string Path)> hitsoundingFileRows = [];
    private readonly List<(Rect Bounds, string Name)> hitsoundingSlots = [];
    private int hitsoundingLibraryScroll;
    private static readonly string[] HitsoundingNames = ["hitnormal", "hitwhistle", "hitfinish", "hitclap", "slidertick"];
    private static readonly uint[] HitsoundingColors = [0xB5A7CD, 0x56B9AA, 0x51BDD2, 0xE0BD62, 0xD56EAB, 0x829DC7, 0xA57CC7];
    private static uint HitsoundingBankColor(int bank) => bank == 2 ? 0x65BF8Au : bank == 3 ? 0xDC747Bu : 0xE0B55Fu;
    private static string BankCode(int bank) => bank == 2 ? "S" : bank == 3 ? "D" : "N";
    private static DockWorkspace CreateHitsoundingWorkspace()
    {
        var state = new DockWorkspace(); ResetHitsoundingWorkspace(state); return state;
    }
    private static void ResetHitsoundingWorkspace(DockWorkspace state)
    {
        state.Left.Panels.Clear(); state.Left.Panels.Add(DockPanel.HitsoundingConfig);
        state.Left.Active = DockPanel.HitsoundingConfig;
        state.Left.Width = 340; state.Left.Collapsed = false; state.Left.Split = .58f;
        state.Right.Panels.Clear(); state.Right.Panels.Add(DockPanel.HitsoundingInspector);
        state.Right.Active = DockPanel.HitsoundingInspector; state.Right.Width = 270; state.Right.Collapsed = false;
    }
    internal void ShowHitsoundingPage()
    {
        if (!CommitCollaborationField() || !PrepareFileOperation()) return;
        CancelInteraction(); tool = Tool.Select; TimingPageVisible = false; HitsoundingPageVisible = true; panelMenuOpen = false;
        ResetHitsounds(); RefreshHitsoundingLibrary(); SaveMapPanel();
    }
    private void RefreshHitsoundingSnapshot()
    {
        var current = GetHitsoundConversion();
        if (ReferenceEquals(current, hitsoundingConversion)) return;
        hitsoundingConversion = current;
        hitsoundingTimingSelection.RemoveWhere(p => !Document.TimingPoints.Contains(p));
        var resolver = hitsoundingResolver = new HitsoundResolver(Document, current.Objects, inspectResources: false);
        var tags = new List<SoundTag>();
        hitsoundingNotes = current.Objects.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.Banana).ToArray();
        hitsoundingNoteLookup = hitsoundingNotes.ToDictionary(n => (n.SourceId, n.EventIndex));
        if (hitsoundingSelected is { } selected && !hitsoundingNoteLookup.ContainsKey((selected.Id, selected.Event))) hitsoundingSelected = null;
        hitsoundingRedPoints = overviewTiming.Where(p => p.Uninherited).ToArray();
        hitsoundingGreenPoints = overviewTiming.Where(p => !p.Uninherited).ToArray();
        var sets = new SortedSet<int>(Document.TimingPoints.Select(p => Math.Max(0, p.SampleIndex))) { 0 };
        for (int i = 0; i < current.Objects.Count; i++)
        {
            var item = current.Objects[i];
            var settings = resolver.Describe(item);
            foreach (var group in resolvedHitsounds[i].Where(s => s.Volume > 0).GroupBy(sound =>
                sound.Name switch { "hitwhistle" => 2, "hitfinish" => 3, "hitclap" => 4, "slidertick" => 5, _ => 1 }))
            {
                string label = string.Join(" ", group.Select(sound => BankCode(sound.SampleSet)).Distinct());
                tags.Add(new(item.TimeMs, group.Key, label, settings.Index, double.PositiveInfinity, group.ToArray(), Note: item));
            }
        }
        hitsoundingTagLanes = ArrangeHitsoundingTags(tags).GroupBy(t => t.Lane).Select(g => g.ToArray()).ToArray();
        hitsoundingAudibleNotes = tags.Where(t => t.Note is not null).Select(t => (t.Note!.SourceId, t.Note.EventIndex)).ToHashSet();
        hitsoundingAdditionLinks = tags.Where(t => t.Lane is >= 2 and <= 4 && t.Note is not null)
            .GroupBy(t => (t.Note!.SourceId, t.Note.EventIndex)).Where(g => g.Count() > 1)
            .Select(g => (g.First().Time, g.Min(t => t.Lane), g.Max(t => t.Lane))).OrderBy(g => g.Time).ToArray();
        hitsoundingAdditionTimes = hitsoundingAdditionLinks.Select(l => l.Time).ToArray();
        hitsoundingTimingLabels = overviewTiming.Select(point =>
        {
            var sample = resolver.Describe(new ConvertedCatchObject(Guid.Empty, 0, CatchObjectKind.Fruit, point.TimeMs - 5, 0, 0, 0, 0, true));
            return new HitsoundingTimingLabel(point.TimeMs, point.Uninherited, BankCode(point.SampleSet > 0 ? point.SampleSet : sample.NormalSet), point.SampleIndex, point.Volume, point);
        }).ToArray();
        hitsoundingFallbackFiles.Clear();
        foreach (string folder in HitsoundSkinFolders)
            if (Directory.Exists(folder))
                foreach (string path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                    .Take(20000).OrderBy(p => Path.GetExtension(p).ToLowerInvariant() switch { ".wav" => 0, ".ogg" => 1, _ => 2 }))
                    if (HitsoundResolver.IsSkinSample(Path.GetFileName(path))) hitsoundingFallbackFiles.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        hitsoundingMapFiles.Clear();
        string? root = Path.GetDirectoryName(Document.SourcePath ?? Document.AudioPath);
        if (root is not null && Directory.Exists(root))
            foreach (string path in Directory.EnumerateFiles(root).Take(20000).OrderBy(p => Path.GetExtension(p).ToLowerInvariant() switch { ".wav" => 0, ".ogg" => 1, _ => 2 }))
            {
                if (IsHitsoundingAudio(path)) hitsoundingMapFiles.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        hitsoundingSets = sets.ToArray();
        if (!sets.Contains(hitsoundingIndex)) hitsoundingIndex = hitsoundingSets[0];
        hitsoundingMapPaths.Clear(); hitsoundingMapPaths.UnionWith(hitsoundingMapFiles.Values);
    }
    private void ZoomHitsounding(double factor, float anchorX)
    {
        double fraction = Math.Clamp((anchorX - hitsoundingGridBounds.X) / Math.Max(1, hitsoundingGridBounds.Width), 0, 1);
        double anchorTime = HitsoundingStart + fraction * hitsoundingSpan;
        hitsoundingSpan = Math.Clamp(hitsoundingSpan * factor, 250, 120000);
        double start = anchorTime - fraction * hitsoundingSpan;
        hitsoundingHeadRatio = Math.Clamp((playhead - start) / hitsoundingSpan, .05, .95);
    }
    private void DrawHitsoundingPage(ICanvas c)
    {
        RefreshHitsoundingSnapshot();
        hitsoundingSoundHits.Clear(); hitsoundingStoryboardHits.Clear();
        hitsoundingPreciseAuditionButton = null;
        hitsoundingFileRows.Clear(); hitsoundingSlots.Clear();
        hitsoundingSplitBounds = hitsoundingLibraryBounds = hitsoundingDockBounds = hitsoundingSampleBounds = hitsoundingInspectorBounds = default;
        objectTimeline = default; zoomSlider = default;
        c.Fill(CanvasToolbarBounds, Panel);
        float toolsX = CanvasToolbarBounds.X + 8;
        float toolScale = Math.Min(1, Math.Max(0, CanvasToolbarBounds.Width - 174) / 298);
        Button(c, new(toolsX, 88, 88 * toolScale, 28), L.Get("hitsounding.noteBased"), () => hitsoundingDockWorkspace.PreciseHitsounds = false, !HitsoundingPrecise, fontSize: 11);
        Button(c, new(toolsX + 92 * toolScale, 88, 88 * toolScale, 28), L.Get("hitsounding.precise"), () => hitsoundingDockWorkspace.PreciseHitsounds = true, HitsoundingPrecise, fontSize: 11);
        c.Line(toolsX + 188 * toolScale, 92, toolsX + 188 * toolScale, 112, Grid);
        Button(c, new(toolsX + 196 * toolScale, 88, 102 * toolScale, 28), L.Get("hitsounding.showText"),
            () => hitsoundingDockWorkspace.ShowHitsoundText = !hitsoundingDockWorkspace.ShowHitsoundText,
            hitsoundingDockWorkspace.ShowHitsoundText, fontSize: 11);
        snapSlider = new(CanvasToolbarBounds.Right - 118, 88, 106, 29);
        c.Text(L.Get("ui.snap"), snapSlider.X - 40, 97, 11, Muted, 40);
        float snapStart = snapSlider.X + 7, snapEnd = snapSlider.Right - 31;
        c.Line(snapStart, 103, snapEnd, 103, Accent, 2);
        c.Circle(snapStart + Array.IndexOf(SnapDivisors, divisor) / (float)(SnapDivisors.Length - 1) * (snapEnd - snapStart), 103, 6, Accent);
        c.Text(L.Get("ui.snapDivisor", divisor), snapSlider.Right - 28, 97, 10, Foreground, 40);
        float right = DockWorkspaceState.Right.Collapsed || DockWorkspaceState.Right.Panels.Count == 0 ? width : rightPanel.X;
        hitsoundingBounds = new(leftPanel.Right + 8, 130, Math.Max(40, right - leftPanel.Right - 16), Math.Max(100, TransportTop - 138));
        var r = hitsoundingBounds;
        float labelWidth = Math.Min(146, r.Width * .32f);
        double start = HitsoundingStart, end = start + hitsoundingSpan;
        float gridX = r.X + labelWidth, gridWidth = Math.Max(10, r.Width - labelWidth);
        float X(double time) => gridX + (float)((time - start) / hitsoundingSpan * gridWidth);
        hitsoundingTimingBoxes.Clear();
        var timingLabels = hitsoundingTimingPreview ?? hitsoundingTimingLabels;
        int timingLow = 0, timingHigh = timingLabels.Length;
        while (timingLow < timingHigh) { int mid = (timingLow + timingHigh) / 2; if (timingLabels[mid].Time < start) timingLow = mid + 1; else timingHigh = mid; }
        for (int i = Math.Max(0, timingLow - 1); i < timingLabels.Length && timingLabels[i].Time <= end; i++)
        {
            var point = timingLabels[i];
            double until = i + 1 < timingLabels.Length ? timingLabels[i + 1].Time : Math.Max(end, TimelineDurationMs);
            if (until < start || until < point.Time) continue;
            float anchor = X(point.Time), left = Math.Max(gridX, anchor), rightEdge = Math.Min(gridX + gridWidth, X(until));
            hitsoundingTimingBoxes.Add((new(left, r.Y + 53, Math.Max(0, rightEdge - left), 36), anchor,
                point.Red ? 0xE77785u : 0x72C686u, point.Bank, point.Index, point.Point.Volume, point.Point));
        }
        float top = r.Y + 122;
        hitsoundingGridBounds = new(gridX, top, gridWidth, Math.Max(7, r.Bottom - top));
        var g = hitsoundingGridBounds; float laneHeight = g.Height / 7;
        c.Fill(r, Chrome(0x191E26, Background), 6); c.StrokeOpacity(r, Grid, 1, 6, .65f); c.Clip(r);
        for (int lane = 0; lane < 7; lane++)
            c.Fill(new(g.X, top + lane * laneHeight, g.Width, laneHeight), Panel, opacity: lane % 2 == 0 ? .5f : .22f);
        SamplesetBounds = AdditionsBounds = default;
        renderedTiming!.FillGrid(start, end, divisor, hitsoundingGrid);
        float lastNumber = -1000;
        foreach (var tick in hitsoundingGrid)
        {
            float x = X(tick.TimeMs); var style = GridStyle(tick);
            c.Line(x, r.Y + 46 - style.Height, x, r.Y + 46, style.Color, style.Width);
            c.Line(x, g.Y, x, g.Bottom, style.Color, 1, tick.IsBeat ? .15f : .045f);
            if (tick.IsBeat && x > lastNumber + 24)
            {
                var timing = renderedTiming.At(tick.TimeMs);
                int beat = (int)Math.Round((tick.TimeMs - timing.OffsetMs) / timing.BeatLengthMs);
                c.Text(((beat % timing.Meter + timing.Meter) % timing.Meter + 1).ToString(), x + 3, r.Y + 16, 10, Foreground, 28);
                lastNumber = x;
            }
            if (tick.IsMeasure) c.Text(Time(tick.TimeMs), x + 3, r.Y, 10, Muted, 82);
        }
        c.Clip(new(g.X, r.Y, g.Width, r.Height));
        TimingPoint? hoveredTiming = null;
        if (DockContentInputAvailable && menu < 0 && contextItems.Count == 0 && !hitsoundingTimingBox)
        {
            hoveredTiming = HitsoundingTimingStrip.Contains(mouseX, mouseY)
                ? hitsoundingTimingBoxes.Where(t => Math.Abs(t.Anchor - mouseX) <= 4).OrderBy(t => Math.Abs(t.Anchor - mouseX)).ThenBy(t => t.Point.Uninherited).FirstOrDefault().Point
                : hitsoundingTimingBoxes.LastOrDefault(t => t.Bounds.Contains(mouseX, mouseY)).Point;
        }
        foreach (var tag in hitsoundingTimingBoxes)
        {
            c.Line(tag.Anchor, g.Y, tag.Anchor, g.Bottom, tag.Color, 1, .45f);
            bool selectedTiming = hitsoundingTimingSelection.Contains(tag.Point);
            bool hovered = hoveredTiming == tag.Point && !selectedTiming && hitsoundingSelectedTiming != tag.Point;
            c.Line(tag.Anchor, tag.Bounds.Bottom + 5, tag.Anchor, g.Y - 1, selectedTiming ? Accent : tag.Color, selectedTiming ? 5 : hovered ? 4 : 3, .95f);
            c.Fill(tag.Bounds, Panel, 6);
            c.Fill(tag.Bounds, tag.Color, 6, hovered ? .55f : .35f);
            c.StrokeOpacity(tag.Bounds, tag.Color, selectedTiming || hitsoundingSelectedTiming == tag.Point ? 2.5f : 1, 6, 1);
            string bankLabel = tag.Index > 0 ? $"{tag.Bank}:{tag.Index}" : tag.Bank;
            string volumeLabel = $"{tag.Volume}%";
            float bankWidth = c.MeasureText(bankLabel, 13, true), volumeWidth = c.MeasureText(volumeLabel, 10, true);
            c.Clip(tag.Bounds);
            c.Text(bankLabel, tag.Bounds.X + 6, tag.Bounds.Y + 2, 13, 0xFFFFFF, bankWidth + 1, true);
            c.Text(volumeLabel, tag.Bounds.X + 6, tag.Bounds.Y + 20, 10, 0xFFFFFF, volumeWidth + 1, true);
            c.Unclip();
        }
        DrawHitsoundingTimingMarquee(c);
        c.Unclip();
        for (int lane = 0; lane < 7; lane++)
        {
            float y = top + lane * laneHeight;
            c.Fill(new(r.X, y, labelWidth, laneHeight), Panel);
            c.Fill(new(r.X + 9, y + (laneHeight - 20) / 2, 3, 20), lane is >= 1 and <= 5 ? Muted : HitsoundingColors[lane], 1.5f);
            c.Text(L.Get("hitsounding.lane" + lane), r.X + 20, y + (laneHeight - 16) / 2, 12, hitsoundingMuted[lane] ? Muted : Foreground, labelWidth - (lane > 0 ? 54 : 24), true);
            if (lane > 0) DrawHitsoundingMute(c, new(r.X + labelWidth - 28, y + (laneHeight - 24) / 2, 24, 24), lane);
            c.Line(r.X, y, r.Right, y, Grid, opacity: .55f);
        }
        c.Clip(g);
        float notesHeight = Math.Min(64, laneHeight);
        var notesViewport = new Rect(g.X, top + (laneHeight - notesHeight) / 2, g.Width, notesHeight);
        c.Clip(notesViewport);
        DrawTimelineObjects(c, notesViewport, start, g.Width / hitsoundingSpan, includePlacement: false);
        int first = FirstAfter(hitsoundingNotes, start - .001);
        for (int i = first; i < hitsoundingNotes.Length && hitsoundingNotes[i].TimeMs <= end; i++)
        {
            var item = hitsoundingNotes[i];
            float x = X(item.TimeMs), y = notesViewport.Y + 27 * notesHeight / 64;
            if (item.Kind == CatchObjectKind.Droplet) c.Circle(x, y, Math.Max(2, 4 * notesHeight / 64), Foreground, false, 1.5f);
            if (item.Kind == CatchObjectKind.Droplet)
                hitsoundingSoundHits.Add((new(x - 5, y - 5, 10, 10), item, 0));
            float radius = (item.Kind == CatchObjectKind.Droplet ? 5 : 19) * notesHeight / 64;
            if (hitsoundingSelected == (item.SourceId, item.EventIndex)) c.Circle(x, y, radius + 3, Accent, false, 2);
            float flash = hitsoundingAudibleNotes.Contains((item.SourceId, item.EventIndex)) ? HitsoundingFlash(item.TimeMs) : 0;
            if (flash > 0)
            {
                c.Circle(x, y, radius + 2 * notesHeight / 64, Foreground, false, 3 * notesHeight / 64, flash * .4f);
                c.Circle(x, y, radius, Foreground, false, 2 * notesHeight / 64, flash);
            }
        }
        c.Unclip();
        if (!ReferenceEquals(hitsoundingStoryboardSource, storyboardScene))
        {
            hitsoundingStoryboardSource = storyboardScene;
            hitsoundingStoryboardTags = storyboardScene?.Samples
                .Where(s => s.Layer is not (StoryboardLayer.Fail or StoryboardLayer.Pass) && s.Volume > 0)
                .OrderBy(s => s.TimeMs).Select(s => new SoundTag(s.TimeMs, 6, Path.GetFileNameWithoutExtension(s.Path), 0, double.PositiveInfinity, [new(CatchObjectKind.Fruit, s.Path, s.Volume / 100f, "storyboard")], Storyboard: s)).ToArray() ?? [];
            hitsoundingStoryboardTags = ArrangeHitsoundingTags(hitsoundingStoryboardTags);
        }
        for (int i = LowerBound(hitsoundingAdditionTimes, start); i < hitsoundingAdditionLinks.Length && hitsoundingAdditionLinks[i].Time <= end; i++)
        {
            var link = hitsoundingAdditionLinks[i];
            c.Line(X(link.Time), top + (link.First + .5f) * laneHeight, X(link.Time), top + (link.Last + .5f) * laneHeight, Muted, 1, .6f);
        }
        float bankLabelWidth = Math.Max(c.MeasureText("N", 11, true), Math.Max(c.MeasureText("S", 11, true), c.MeasureText("D", 11, true)));
        float digitLabelWidth = 0;
        foreach (string digit in HitsoundingDigits) digitLabelWidth = Math.Max(digitLabelWidth, c.MeasureText(digit, 11, true));
        (Rect Bounds, Hitsound Sound)? selectedAudition = null;
        foreach (var laneTags in hitsoundingTagLanes) DrawTags(laneTags);
        DrawTags(hitsoundingStoryboardTags);
        if (!hitsoundingErase)
            foreach (var pending in hitsoundingPaint.Values)
                if (hitsoundingNoteLookup.TryGetValue((pending.SourceId, pending.EventIndex), out var note))
                {
                    float y = top + (hitsoundingPaintLane + .5f) * laneHeight - 16;
                    c.Fill(new(X(note.TimeMs) - 9, y, 18, 32), HitsoundingBankColor(pending.Sample.AdditionSet), 4, .5f);
                    c.StrokeOpacity(new(X(note.TimeMs) - 9, y, 18, 32), Foreground, 1, 4, .5f);
                }
        if (!hitsoundingHeadDrag && !hitsoundingMarquee && hitsoundingMoveSource is null && hitsoundingGridBounds.Contains(mouseX, mouseY))
        {
            int lane = (int)((mouseY - top) / laneHeight);
            if (lane is >= 2 and <= 4 && HitsoundingPlacementNoteAt(mouseX, mouseY) is { Kind: CatchObjectKind.Fruit } candidate
                && (hitsoundingResolver!.Describe(candidate).Additions & (1 << (lane - 1))) == 0)
            {
                float ghostY = top + (lane + .5f) * laneHeight - 16;
                c.Fill(new(X(candidate.TimeMs) - 9, ghostY, 18, 32), HitsoundingBankColor(nextAdditionBank > 0 ? nextAdditionBank : hitsoundingResolver!.Describe(candidate).AdditionSet), 4, .5f);
                c.StrokeOpacity(new(X(candidate.TimeMs) - 9, ghostY, 18, 32), Foreground, 1, 4, .5f);
            }
        }
        if (hitsoundingMoveActive && hitsoundingMoveSource is { } moving && hitsoundingMoveTarget is { } target)
        {
            float y = top + (moving.Lane + .5f) * laneHeight - 16;
            var source = hitsoundingNoteLookup[(moving.Id, moving.Event)];
            c.Fill(new(X(target.TimeMs) - 9, y, 18, 32), HitsoundingBankColor(hitsoundingResolver!.Describe(source).AdditionSet), 4, .5f);
            c.StrokeOpacity(new(X(target.TimeMs) - 9, y, 18, 32), Foreground, 1, 4, .5f);
        }
        if (hitsoundingStoryboardMove is not null && hitsoundingStoryboardMoved)
        {
            float y = top + 6.5f * laneHeight - 16;
            c.Fill(new(X(hitsoundingStoryboardMoveTime), y, 36, 32), HitsoundingColors[6], 4, .5f);
            c.StrokeOpacity(new(X(hitsoundingStoryboardMoveTime), y, 36, 32), Foreground, 1, 4, .5f);
        }
        if (hitsoundingMarquee && hitsoundingMarqueeMoved)
        {
            UpdateHitsoundingMarquee();
            foreach (var hit in hitsoundingSoundHits)
                if (hitsoundingMultiSelection.Contains((hit.Note.SourceId, hit.Note.EventIndex, hit.Lane))) c.Stroke(hit.Bounds, Foreground, 2, 4);
            foreach (var hit in hitsoundingStoryboardHits)
                if (hitsoundingStoryboardSelection.Contains(hit.Sample)) c.Stroke(hit.Bounds, Foreground, 2, 4);
            var selectionBox = HitsoundingMarqueeBounds;
            c.Fill(selectionBox, Accent, opacity: .15f); c.Stroke(selectionBox, Accent, 1);
        }
        for (int lane = 1; lane <= 6; lane++)
            if (hitsoundingMuted[lane])
                c.Fill(new(g.X, top + lane * laneHeight, g.Width, laneHeight), Panel, opacity: .65f);
        if (selectedAudition is { } audition) DrawHitsoundingPreciseAudition(c, audition.Bounds, audition.Sound);
        c.Unclip();
        float head = X(playhead); c.Line(head, r.Y, head, g.Bottom, HitsoundingHeadHovered(mouseX, mouseY) || hitsoundingHeadDrag ? 0xFFF0A6u : 0xE9CA64u, 2);
        c.Unclip();
        void DrawTags(SoundTag[] tags)
        {
            double margin = HitsoundingTagWidth / 2 / g.Width * hitsoundingSpan;
            int firstTag = LowerTag(tags, start - margin);
            if (HitsoundingPrecise)
            {
                while (firstTag > 0 && tags[firstTag - 1].Next >= start) firstTag--;
            }
            for (int i = firstTag; i < tags.Length && tags[i].Time <= end + margin; i++)
            {
                var tag = tags[i]; float x = X(tag.Time);
                if (tag.Lane is >= 2 and <= 4 && tag.Note is { } eventNote
                    && hitsoundingPaint.TryGetValue((eventNote.SourceId, eventNote.EventIndex), out var pending)
                    && (pending.Sample.Additions & (1 << (tag.Lane - 1))) == 0) continue;
                float h = Math.Clamp(laneHeight - 2, 24, 36), y = top + tag.Lane * laneHeight + (laneHeight - h) / 2;
                double neighbourPixels = tag.NeighbourDistance / hitsoundingSpan * g.Width;
                float preferredWidth = Math.Clamp(Math.Max(c.MeasureText("N", 11, true), c.MeasureText(tag.WidthText, 11, true)) + 8, 18, HitsoundingTagWidth);
                if (tag.Lane <= 5) preferredWidth = Math.Clamp(Math.Max(bankLabelWidth, digitLabelWidth * tag.WidthText.Length) + 8, 18, HitsoundingTagWidth);
                double clusterWidth = Math.Min(preferredWidth * tag.Columns,
                    neighbourPixels > 4 ? neighbourPixels - 4 : neighbourPixels * .75);
                float slotWidth = (float)(clusterWidth / tag.Columns);
                float tagWidth = Math.Max(0, slotWidth - (tag.Columns > 1 ? Math.Min(2, slotWidth * .1f) : 0));
                float center = x + (tag.Column + .5f - tag.Columns / 2f) * slotWidth;
                var box = new Rect(center - tagWidth / 2, y, tagWidth, h);
                AudioWaveform? wave = null;
                if (HitsoundingPrecise)
                {
                    double duration = 0;
                    foreach (var sound in tag.Sounds)
                    {
                        var candidate = hitsoundingAudio.Get(sound, RequestWaveform);
                        if (candidate is not null && (wave is null || candidate.DurationMs > wave.DurationMs)) wave = candidate;
                        duration = Math.Max(duration, hitsoundingAudio.Duration(sound));
                    }
                    double to = Math.Min(tag.Time + duration, tag.Next);
                    if (duration > 0 && to < start) continue;
                    box = new(x, y, duration > 0 ? Math.Max(1, X(to) - x) : HitsoundingTagWidth, h);
                }
                uint color = tag.Lane is >= 1 and <= 5 ? HitsoundingBankColor(tag.Sounds[0].SampleSet) : HitsoundingColors[tag.Lane];
                bool auditionSelected = HitsoundingPrecise && !hitsoundingMarquee && box.Right > g.X && box.X < g.Right
                    && (tag.Note is { } selectedNote && hitsoundingMultiSelection.Count == 1 && hitsoundingMultiSelection.Contains((selectedNote.SourceId, selectedNote.EventIndex, tag.Lane))
                        || tag.Storyboard is { } selectedSample && hitsoundingStoryboardSelection.Count == 1 && hitsoundingStoryboardSelection.Contains(selectedSample));
                if (auditionSelected) selectedAudition = (box, tag.Sounds[0]);
                if (hitsoundingMuted[tag.Lane]) color = Muted;
                float flash = hitsoundingMuted[tag.Lane] ? 0 : HitsoundingFlash(tag.Time);
                if (flash > 0)
                {
                    c.Fill(new(box.X - 5, box.Y - 5, box.Width + 10, box.Height + 10), color, 8, flash * .35f);
                    c.Fill(new(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), color, 6, flash * .6f);
                }
                c.Fill(box, Panel, 4);
                c.Fill(box, color, 4, HitsoundingPrecise ? .4f : .6f);
                if (auditionSelected && HitsoundingAuditionKey(tag.Sounds[0]) == hitsoundingAuditionPath)
                {
                    c.Fill(box, Surface, 4);
                    c.Clip(box); c.Fill(new(box.X, box.Y, box.Width * HitsoundingAuditionProgress, box.Height), SelectedSurface, 4); c.Unclip();
                    c.Stroke(box, Accent, radius: 4);
                }
                if (flash > 0) c.Fill(box, 0xFFFFFF, 4, flash * .65f);
                c.Stroke(box, color, radius: Math.Min(4, box.Width / 3));
                if (flash > 0) c.StrokeOpacity(box, 0xFFFFFF, 2, Math.Min(4, box.Width / 3), flash);
                if (tag.Note is { } note)
                {
                    hitsoundingSoundHits.Add((box, note, tag.Lane));
                    if (!hitsoundingMarquee && hitsoundingMultiSelection.Contains((note.SourceId, note.EventIndex, tag.Lane))) c.Stroke(box, Foreground, 2, 4);
                }
                if (tag.Storyboard is { } sb)
                {
                    hitsoundingStoryboardHits.Add((box, sb));
                    if (!hitsoundingMarquee && hitsoundingStoryboardSelection.Contains(sb)) c.Stroke(box, Foreground, 2, 4);
                }
                if (hitsoundingDockWorkspace.ShowHitsoundText && HitsoundingPrecise)
                {
                    string label = tag.Label + (tag.Index > 0 ? " " + tag.Index : "");
                    if (c.MeasureText(label, 11, true) <= box.Width - 8) c.Text(label, box.X + 4, box.Y + 3, 11, 0xFFFFFF, box.Width - 8, true);
                }
                else if (hitsoundingDockWorkspace.ShowHitsoundText)
                {
                    DrawHitsoundingLabel(c, box, tag.Label, tag.Index,
                        tag.Lane <= 5 ? Math.Max(bankLabelWidth, digitLabelWidth * (tag.Index > 0 ? tag.Index.ToString().Length : 0)) : 0);
                }
                if (HitsoundingPrecise && wave is not null && box.Width > 1)
                {
                    float maximum = Math.Max(.000001f, wave.Peak(0, wave.DurationMs));
                    float step = box.Width < 30 ? 1 : 2;
                    float padding = Math.Min(2, box.Width * .1f);
                    for (float px = Math.Max(g.X, box.X + padding); px < Math.Min(g.Right, box.Right - padding); px += step)
                    {
                        double t = (px - x) / g.Width * hitsoundingSpan;
                        float peak = Math.Clamp(wave.Peak(t, t + step / g.Width * hitsoundingSpan) / maximum, 0, 1) * 7;
                        c.Line(px, box.Bottom - 9 - peak, px, box.Bottom - 9 + peak, color, opacity: .9f);
                    }
                }
                c.Line(x, box.Bottom, x, box.Bottom + 4, color, tag.Lane == 5 ? 2 : 1);
            }
        }
    }
    private float HitsoundingFlash(double time) => AudioPlaying && playhead >= time && playhead < time + 280
        ? (float)Math.Pow(1 - Math.Max(0, playhead - time - 45) / 235, 1.4) : 0;
    private static readonly string[] HitsoundingDigits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];
    private void DrawHitsoundingLabel(ICanvas c, Rect box, string bank, int index, float minimumWidth = 0)
    {
        const float fontSize = 11;
        float available = Math.Max(0, box.Width - 6);
        if (available <= 0) return;
        float rowHeight = (box.Height - 6) / (index > 0 ? 2 : 1);
        string indexText = index > 0 ? index.ToString() : "";
        float required = minimumWidth > 0 ? minimumWidth : Math.Max(c.MeasureText(bank, fontSize, true), c.MeasureText(indexText, fontSize, true));
        if (rowHeight < fontSize * 1.3f || required > available + .01f) return;
        DrawLine(bank, box.Y + 3);
        if (index > 0) DrawLine(indexText, box.Y + 3 + rowHeight);

        void DrawLine(string text, float rowY)
        {
            float width = c.MeasureText(text, fontSize, true);
            c.Text(text, box.X + (box.Width - width) / 2, rowY + (rowHeight - fontSize * 1.3f) / 2,
                fontSize, 0xFFFFFF, available + .1f, true);
        }
    }
    private static SoundTag[] ArrangeHitsoundingTags(IEnumerable<SoundTag> source)
    {
        var result = new List<SoundTag>();
        foreach (var lane in source.GroupBy(t => t.Lane))
        {
            var moments = lane.GroupBy(t => t.Time).OrderBy(t => t.Key).ToArray();
            for (int i = 0; i < moments.Length; i++)
            {
                double distance = Math.Min(i > 0 ? moments[i].Key - moments[i - 1].Key : double.PositiveInfinity,
                    i + 1 < moments.Length ? moments[i + 1].Key - moments[i].Key : double.PositiveInfinity);
                var tags = moments[i].GroupBy(t => (t.Label, t.Index, t.Note?.SourceId, t.Note?.EventIndex, t.Storyboard))
                    .Select(g => g.First() with { Sounds = g.SelectMany(t => t.Sounds).Distinct().ToArray() }).ToArray();
                string widthText = tags.SelectMany(t => new[] { t.Label, t.Index > 0 ? t.Index.ToString() : "" }).MaxBy(t => t.Length) ?? "";
                for (int column = 0; column < tags.Length; column++)
                    result.Add(tags[column] with { Next = i + 1 < moments.Length ? moments[i + 1].Key : double.PositiveInfinity, NeighbourDistance = distance, Column = column, Columns = tags.Length, WidthText = widthText });
            }
        }
        return result.OrderBy(t => t.Time).ThenBy(t => t.Lane).ToArray();
    }
    private static int LowerTag(SoundTag[] tags, double time)
    {
        int lo = 0, hi = tags.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (tags[mid].Time < time) lo = mid + 1; else hi = mid; }
        return lo;
    }
}
