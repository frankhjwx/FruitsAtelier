using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly HashSet<TimingPoint> hitsoundingTimingSelection = [];
    private HitsoundingTimingLabel[]? hitsoundingTimingPreview;
    private HitsoundingTimingLabel[] hitsoundingTimingMoving = [], hitsoundingTimingStationary = [];
    private TimingPoint? hitsoundingTimingDrag;
    private bool hitsoundingTimingBox, hitsoundingTimingMoved, hitsoundingTimingAdd;
    private float hitsoundingTimingStartX;
    private double hitsoundingTimingStartTime, hitsoundingTimingEndTime, hitsoundingTimingOffset, hitsoundingTimingDelta;
    private TimingPoint[] hitsoundingTimingBefore = [];
    private Rect HitsoundingTimingStrip => new(hitsoundingGridBounds.X, hitsoundingGridBounds.Y - 28, hitsoundingGridBounds.Width, 28);
    internal Rect HitsoundingTimingHandleBounds(TimingPoint point)
    {
        var hit = hitsoundingTimingBoxes.LastOrDefault(t => t.Point == point);
        return hit.Point is null ? default : new(hit.Anchor - 4, HitsoundingTimingStrip.Y, 8, HitsoundingTimingStrip.Height);
    }
    private double HitsoundingPointerTime(float x) => HitsoundingStart + (x - hitsoundingGridBounds.X) / Math.Max(1, hitsoundingGridBounds.Width) * hitsoundingSpan;

    private bool BeginHitsoundingTimingPointer(float x, float y, int button, bool ctrl, bool shift)
    {
        var bpm = hitsoundingBpmBoxes.LastOrDefault(t => t.Bounds.Contains(x, y));
        if (bpm.Point is not null)
        {
            if (!CommitField()) return true;
            if (button == 0 && ctrl)
            {
                bool remove = hitsoundingTimingSelection.Contains(bpm.Point);
                var previous = hitsoundingTimingSelection.ToArray();
                SelectHitsoundingTimingPoint(bpm.Point);
                hitsoundingTimingSelection.UnionWith(previous);
                if (remove) hitsoundingTimingSelection.Remove(bpm.Point);
            }
            else SelectHitsoundingTimingPoint(bpm.Point);
            if (button == 2)
            {
                contextItems.Clear();
                contextItems.Add(new(L.Get("hitsounding.editTimingHelper"), () => OpenHitsoundingTimingHelper(bpm.Point)));
                contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 240)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 240, ContextMenuHeight);
            }
            return true;
        }
        bool strip = HitsoundingTimingStrip.Contains(x, y);
        var block = hitsoundingTimingBoxes.LastOrDefault(t => t.Bounds.Contains(x, y));
        if (!strip && (block.Point is null || button != 2 && !(button == 0 && ctrl))) return false;
        var hit = strip ? hitsoundingTimingBoxes.Where(t => Math.Abs(t.Anchor - x) <= 4).OrderBy(t => Math.Abs(t.Anchor - x)).ThenBy(t => t.Point.Uninherited).FirstOrDefault() : block;
        if (button == 2)
        {
            if (!CommitField()) return true;
            double time = Math.Max(0, SnapTiming().Snap(HitsoundingPointerTime(x), divisor));
            contextItems.Clear();
            if (hit.Point is { } selected)
            {
                SelectHitsoundingTimingPoint(selected, preserveSelection: true);
                contextItems.Add(new(L.Get("hitsounding.editTimingHelper"), () => OpenHitsoundingTimingHelper(selected)));
                if (!selected.Uninherited)
                    contextItems.Add(new(L.Get("editor.command.delete"), DeleteHitsoundingTimingSelection));
            }
            else contextItems.Add(new(L.Get("hitsounding.newInheritedPoint"), () => AddHitsoundingInheritedPoint(time)));
            contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 240)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 240, ContextMenuHeight);
            return true;
        }
        if (button != 0 || !CommitField()) return true;
        hitsoundingTimingStartX = x; hitsoundingTimingStartTime = hitsoundingTimingEndTime = HitsoundingPointerTime(x);
        hitsoundingTimingMoved = false; hitsoundingTimingDelta = 0;
        if (hit.Point is { } point)
        {
            hitsoundingSelectedTiming = point; hitsoundingSelected = null; hitsoundingNotesSelection = false;
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear(); SelectObjects([]);
            if (ctrl) { if (!hitsoundingTimingSelection.Add(point)) hitsoundingTimingSelection.Remove(point); }
            else if (!shift && !hitsoundingTimingSelection.Contains(point)) hitsoundingTimingSelection.Clear();
            if (!ctrl) hitsoundingTimingSelection.Add(point);
            if (!hitsoundingTimingSelection.Contains(point)) hitsoundingSelectedTiming = hitsoundingTimingSelection.LastOrDefault();
            if (!strip || hitsoundingTimingSelection.Any(p => p.Uninherited)) return true;
            if (!hitsoundingTimingSelection.Contains(point)) return true;
            hitsoundingTimingDrag = point; hitsoundingTimingOffset = point.TimeMs - hitsoundingTimingStartTime;
            hitsoundingTimingMoving = hitsoundingTimingLabels.Where(t => hitsoundingTimingSelection.Contains(t.Point)).ToArray();
            hitsoundingTimingStationary = hitsoundingTimingLabels.Where(t => !hitsoundingTimingSelection.Contains(t.Point)).ToArray();
            hitsoundingTimingPreview = (HitsoundingTimingLabel[])hitsoundingTimingLabels.Clone();
        }
        else
        {
            hitsoundingSelected = null; hitsoundingSelectedTiming = null;
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear(); SelectObjects([]);
            hitsoundingTimingBox = true; hitsoundingTimingAdd = ctrl || shift;
            hitsoundingTimingBefore = hitsoundingTimingSelection.ToArray();
            if (!hitsoundingTimingAdd) hitsoundingTimingSelection.Clear();
        }
        return true;
    }

    private bool MoveHitsoundingTimingPointer(float x)
    {
        if (hitsoundingTimingDrag is null && !hitsoundingTimingBox) return false;
        hitsoundingTimingMoved |= Math.Abs(x - hitsoundingTimingStartX) >= ContentDragThreshold;
        if (!hitsoundingTimingMoved) return true;
        hitsoundingTimingEndTime = HitsoundingPointerTime(x);
        if (hitsoundingTimingBox)
        {
            hitsoundingTimingSelection.Clear();
            if (hitsoundingTimingAdd) hitsoundingTimingSelection.UnionWith(hitsoundingTimingBefore);
            double from = Math.Min(hitsoundingTimingStartTime, hitsoundingTimingEndTime), to = Math.Max(hitsoundingTimingStartTime, hitsoundingTimingEndTime);
            int lo = 0, hi = hitsoundingTimingLabels.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (hitsoundingTimingLabels[mid].Time < from) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < hitsoundingTimingLabels.Length && hitsoundingTimingLabels[i].Time <= to; i++)
                hitsoundingTimingSelection.Add(hitsoundingTimingLabels[i].Point);
            return true;
        }
        double target = SnapTiming().Snap(hitsoundingTimingEndTime + hitsoundingTimingOffset, divisor);
        double delta = Math.Max(-hitsoundingTimingMoving[0].Time, target - hitsoundingTimingDrag!.TimeMs);
        if (delta == hitsoundingTimingDelta) return true;
        hitsoundingTimingDelta = delta;
        // Moving points retain their order; merge the two cached sequences without reconverting the map during a drag.
        int moving = 0, stationary = 0;
        for (int i = 0; i < hitsoundingTimingPreview!.Length; i++)
        {
            bool takeMoving = stationary == hitsoundingTimingStationary.Length;
            if (!takeMoving && moving < hitsoundingTimingMoving.Length)
            {
                var a = hitsoundingTimingMoving[moving]; var b = hitsoundingTimingStationary[stationary];
                takeMoving = a.Time + delta < b.Time || a.Time + delta == b.Time && a.Point.SourceOrder <= b.Point.SourceOrder;
            }
            if (takeMoving)
            { var label = hitsoundingTimingMoving[moving++]; hitsoundingTimingPreview[i] = label with { Time = label.Time + delta }; }
            else hitsoundingTimingPreview[i] = hitsoundingTimingStationary[stationary++];
        }
        return true;
    }

    private bool EndHitsoundingTimingPointer()
    {
        if (hitsoundingTimingDrag is null && !hitsoundingTimingBox) return false;
        var moving = hitsoundingTimingMoving; double delta = hitsoundingTimingDelta;
        bool commit = hitsoundingTimingDrag is not null && hitsoundingTimingMoved && delta != 0;
        CancelHitsoundingTimingPointer();
        if (commit && moving.All(t => !t.Point.Uninherited && Document.TimingPoints.Contains(t.Point) && t.Point.TimeMs == t.Time))
            Edit(L.Get("timing.edit"), () => { foreach (var label in moving) label.Point.TimeMs = label.Time + delta; });
        return true;
    }

    private void CancelHitsoundingTimingPointer()
    {
        hitsoundingTimingDrag = null; hitsoundingTimingBox = hitsoundingTimingMoved = false;
        hitsoundingTimingPreview = null; hitsoundingTimingMoving = hitsoundingTimingStationary = []; hitsoundingTimingDelta = 0;
    }

    private void DrawHitsoundingTimingMarquee(ICanvas c)
    {
        if (!hitsoundingTimingBox || !hitsoundingTimingMoved) return;
        float left = hitsoundingGridBounds.X + (float)((Math.Min(hitsoundingTimingStartTime, hitsoundingTimingEndTime) - HitsoundingStart) / hitsoundingSpan * hitsoundingGridBounds.Width);
        float span = (float)(Math.Abs(hitsoundingTimingEndTime - hitsoundingTimingStartTime) / hitsoundingSpan * hitsoundingGridBounds.Width);
        var box = new Rect(left, HitsoundingTimingStrip.Y, span, HitsoundingTimingStrip.Height);
        c.Fill(box, Accent, opacity: .15f); c.Stroke(box, Accent);
    }

    private void AddHitsoundingInheritedPoint(double time)
    {
        if (!CommitField()) return;
        var point = TimingEditing.Create(Document, time, inherited: true);
        point.SourceOrder = Document.TimingPoints.Select(p => p.SourceOrder).Where(v => v < int.MaxValue).DefaultIfEmpty(-1).Max() + 1;
        if (Edit(L.Get("timing.edit"), () => Document.TimingPoints.Add(point)))
        { hitsoundingTimingSelection.Clear(); hitsoundingTimingSelection.Add(point); hitsoundingSelectedTiming = point; inspectorConversion = null; }
    }

    private void SelectHitsoundingTimingPoint(TimingPoint point, bool preserveSelection = false)
    {
        hitsoundingSelectedTiming = point; hitsoundingSelected = null; hitsoundingNotesSelection = false;
        hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear(); SelectObjects([]);
        if (!preserveSelection || !hitsoundingTimingSelection.Contains(point)) hitsoundingTimingSelection.Clear();
        hitsoundingTimingSelection.Add(point);
    }

    private void OpenHitsoundingTimingHelper(TimingPoint point)
    {
        if (!Document.TimingPoints.Contains(point)) return;
        SelectHitsoundingTimingPoint(point);
        if (!DockPanelExists(DockPanel.HitsoundingInspector)) AddDockPanel(DockPanel.HitsoundingInspector, false);
        var group = PanelGroup(DockPanel.HitsoundingInspector);
        group.Active = DockPanel.HitsoundingInspector; group.Collapsed = false;
        hitsoundingInspectorScroll = 0; inspectorConversion = null;
    }

    private void DeleteHitsoundingTimingSelection()
    {
        if (!CommitField()) return;
        var selected = Document.TimingPoints.Where(p => !p.Uninherited && hitsoundingTimingSelection.Contains(p)).ToHashSet();
        if (selected.Count == 0) return;
        CancelHitsoundingTimingPointer();
        if (Edit(L.Get("editor.command.delete"), () => Document.TimingPoints.RemoveAll(selected.Contains)))
        { hitsoundingSelectedTiming = null; hitsoundingTimingSelection.Clear(); inspectorConversion = null; }
    }
}
