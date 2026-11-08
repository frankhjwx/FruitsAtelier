using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum HitsoundingNoteGesture { None, Marquee, Move, Start, End }
    private HitsoundingNoteGesture hitsoundingNoteGesture;
    private bool hitsoundingNotesSelection, hitsoundingNoteMoved, hitsoundingNoteCanCreate;
    private double hitsoundingNoteFrom, hitsoundingNoteTo, hitsoundingNoteOriginalStart, hitsoundingNoteOriginalEnd;
    private float hitsoundingNotePointerX;
    private double hitsoundingMoveMin, hitsoundingMoveMax;
    private Guid hitsoundingNoteId;
    private Guid[] hitsoundingNoteSelectionBefore = [];
    private Rect HitsoundingNotesLane => new(hitsoundingGridBounds.X, hitsoundingGridBounds.Y,
        hitsoundingGridBounds.Width, hitsoundingGridBounds.Height / 7);
    internal Rect HitsoundingNotesLaneBounds => HitsoundingNotesLane;
    private double HitsoundingNoteTime(float x) => Math.Clamp(SnapTiming().Snap(HitsoundingPointerTime(x), divisor), 0, EditableDurationMs);

    private bool BeginHitsoundingNotesPointer(float x, float y, int button, bool ctrl, bool shift)
    {
        if (!hitsoundingAllowNotes || !HitsoundingNotesLane.Contains(x, y)) return false;
        if (button is not (0 or 2) || !CommitField() || notesLocked) return true;
        var hit = timelineObjects.LastOrDefault(t => t.Bounds.Contains(x, y) && CanSelectCollaborationObject(t.Id));
        if (button == 2)
        {
            if (hit.Id == Guid.Empty) return true;
            CancelInteraction();
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
            hitsoundingSelected = null; hitsoundingNotesSelection = true;
            if (!objectSelection.Contains(hit.Id)) SelectObjects([hit.Id], hit.Id);
            DeleteSelectedObjects();
            return true;
        }
        hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear(); hitsoundingTimingSelection.Clear();
        hitsoundingSelected = null; hitsoundingSelectedTiming = null;
        if (!hitsoundingNotesSelection) SelectObjects([]);
        hitsoundingNotesSelection = true;
        hitsoundingNoteSelectionBefore = objectSelection.ToArray();
        hitsoundingNoteFrom = hitsoundingNoteTo = HitsoundingNoteTime(x);
        hitsoundingNoteCanCreate = !ctrl && !shift;
        hitsoundingNotePointerX = x; hitsoundingNoteMoved = false;
        if (hit.Id != Guid.Empty)
        {
            if (ctrl)
            {
                var ids = objectSelection.ToHashSet();
                if (!ids.Add(hit.Id)) ids.Remove(hit.Id);
                SelectObjects(ids); return true;
            }
            if (!objectSelection.Contains(hit.Id)) SelectObjects(shift ? objectSelection.Append(hit.Id) : [hit.Id], hit.Id);
            var source = timelineSources.First(t => t.Id == hit.Id);
            var selectedSources = timelineSources.Where(t => objectSelection.Contains(t.Id)).ToArray();
            hitsoundingMoveMin = -selectedSources.Min(t => t.Start);
            hitsoundingMoveMax = Math.Max(0, EditableDurationMs - selectedSources.Max(t => t.End));
            hitsoundingNoteId = hit.Id;
            hitsoundingNoteOriginalStart = source.Start; hitsoundingNoteOriginalEnd = source.End;
            double scale = hitsoundingGridBounds.Width / hitsoundingSpan;
            float head = hitsoundingGridBounds.X + (float)((source.Start - HitsoundingStart) * scale);
            float tail = hitsoundingGridBounds.X + (float)((source.End - HitsoundingStart) * scale);
            bool slider = !source.IsBanana && source.End > source.Start;
            hitsoundingNoteGesture = slider && Math.Abs(x - tail) <= 10 ? HitsoundingNoteGesture.End
                : slider && Math.Abs(x - head) <= 10 ? HitsoundingNoteGesture.Start : HitsoundingNoteGesture.Move;
            if (slider && IsReadOnlySlider(hit.Id)) hitsoundingNoteGesture = HitsoundingNoteGesture.None;
        }
        else
        {
            hitsoundingNoteId = Guid.Empty;
            hitsoundingNoteGesture = HitsoundingNoteGesture.Marquee;
            if (!ctrl && !shift) { hitsoundingNoteSelectionBefore = []; SelectObjects([]); }
        }
        return true;
    }

    private bool MoveHitsoundingNotesPointer(float x, float y)
    {
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.None) return false;
        hitsoundingNoteTo = HitsoundingNoteTime(x);
        hitsoundingNoteMoved |= Math.Abs(x - hitsoundingNotePointerX) >= ContentDragThreshold;
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.Marquee && hitsoundingNoteMoved)
        {
            double from = Math.Min(hitsoundingNoteFrom, hitsoundingNoteTo), to = Math.Max(hitsoundingNoteFrom, hitsoundingNoteTo);
            SelectObjects(hitsoundingNoteSelectionBefore.Concat(timelineVisibleMarkers
                .Where(t => t.Start <= to && t.End >= from && CanSelectCollaborationObject(t.Id)).Select(t => t.Id)));
        }
        return true;
    }

    private bool EndHitsoundingNotesPointer()
    {
        var gesture = hitsoundingNoteGesture;
        if (gesture == HitsoundingNoteGesture.None) return false;
        hitsoundingNoteGesture = HitsoundingNoteGesture.None;
        if (gesture == HitsoundingNoteGesture.Marquee && !hitsoundingNoteMoved && hitsoundingNoteCanCreate)
        {
            double start = Math.Min(hitsoundingNoteFrom, hitsoundingNoteTo), end = Math.Max(hitsoundingNoteFrom, hitsoundingNoteTo);
            Guid id = Guid.NewGuid();
            if (Edit(L.Get("hitsounding.createNote"), () =>
            {
                Document.Fruits.Add(new() { Id = id, TimeMs = start, X = 256, Y = 192 });
                Document.DurationMs = Math.Max(Document.DurationMs, end);
            })) SelectObjects([id], id);
        }
        else if (hitsoundingNoteMoved && gesture is HitsoundingNoteGesture.Start or HitsoundingNoteGesture.End)
        {
            EditSliderRepresentation(L.Get("hitsounding.resizeNote"), () =>
                SetSliderEndpointTime(hitsoundingNoteId, gesture == HitsoundingNoteGesture.End, hitsoundingNoteTo));
        }
        else if (hitsoundingNoteMoved && gesture == HitsoundingNoteGesture.Move)
        {
            double delta = HitsoundingNoteMoveDelta;
            var ids = objectSelection.ToHashSet();
            var sources = timelineSources.Where(s => ids.Contains(s.Id)).ToArray();
            if (sources.Length > 0)
            {
                Edit(L.Get("hitsounding.moveNotes"), () =>
                {
                    foreach (var fruit in Document.Fruits.Where(f => ids.Contains(f.Id))) fruit.TimeMs += delta;
                    foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id))) TimingEditing.TransformTrack(track, 0, delta, 1);
                    foreach (var slider in Document.ImportedSliders.Where(s => ids.Contains(s.Id)))
                    { slider.TimeMs += delta; OsuSyncSliderLine(slider); }
                    foreach (var banana in Document.BananaShowers.Where(s => ids.Contains(s.Id)))
                    { banana.TimeMs += delta; banana.EndTimeMs += delta; }
                });
            }
        }
        return true;
    }

    private void DrawHitsoundingNotePreview(ICanvas c)
    {
        if (!hitsoundingAllowNotes) return;
        float X(double time) => hitsoundingGridBounds.X + (float)((time - HitsoundingStart) / hitsoundingSpan * hitsoundingGridBounds.Width);
        var lane = HitsoundingNotesLane;
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.Marquee && hitsoundingNoteMoved)
        {
            var box = new Rect(Math.Min(X(hitsoundingNoteFrom), X(hitsoundingNoteTo)), lane.Y,
                Math.Abs(X(hitsoundingNoteTo) - X(hitsoundingNoteFrom)), lane.Height);
            c.Fill(box, Accent, opacity: .15f); c.Stroke(box, Accent, 1); return;
        }
    }
    private double HitsoundingNoteMoveDelta => Math.Clamp(hitsoundingNoteTo - hitsoundingNoteFrom, hitsoundingMoveMin, hitsoundingMoveMax);
    private double HitsoundingNotePreviewTime(Guid id, double time)
    {
        if (!hitsoundingNoteMoved) return time;
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.Move && objectSelection.Contains(id)) return time + HitsoundingNoteMoveDelta;
        if (id != hitsoundingNoteId || hitsoundingNoteGesture is not (HitsoundingNoteGesture.Start or HitsoundingNoteGesture.End)) return time;
        double start = hitsoundingNoteGesture == HitsoundingNoteGesture.Start ? Math.Min(hitsoundingNoteTo, hitsoundingNoteOriginalEnd - 1) : hitsoundingNoteOriginalStart;
        double end = hitsoundingNoteGesture == HitsoundingNoteGesture.End ? Math.Max(hitsoundingNoteTo, hitsoundingNoteOriginalStart + 1) : hitsoundingNoteOriginalEnd;
        return start + (time - hitsoundingNoteOriginalStart) * (end - start) / (hitsoundingNoteOriginalEnd - hitsoundingNoteOriginalStart);
    }
}
