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
    private float hitsoundingNotePointerX, hitsoundingNotePointerY;
    private double hitsoundingMoveMin, hitsoundingMoveMax;
    private Guid hitsoundingNoteId;
    private Guid[] hitsoundingNoteSelectionBefore = [];
    private readonly List<int> hitsoundingNoteMarqueeIndices = [];
    private (Guid Id, double Start, double End, int SourceOrder, bool IsBanana, int Spans, int StackIndex, float StackOffset, int EventIndex)[] hitsoundingNoteMoveMarkers = [];
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
        hitsoundingNoteCanCreate = hitsoundingDraw && !ctrl && !shift;
        hitsoundingNotePointerX = x; hitsoundingNotePointerY = y; hitsoundingNoteMoved = false;
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
            hitsoundingNoteMoveMarkers = timelineMarkers.Where(m => m.StackIndex < TimelineStackLimit &&
                (hitsoundingNoteGesture == HitsoundingNoteGesture.Move ? objectSelection.Contains(m.Id) : m.Id == hit.Id)).ToArray();
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
        hitsoundingNoteMoved |= Math.Abs(x - hitsoundingNotePointerX) >= ContentDragThreshold
            || Math.Abs(y - hitsoundingNotePointerY) >= ContentDragThreshold;
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.Marquee && hitsoundingNoteMoved)
        {
            double from = Math.Min(hitsoundingNoteFrom, hitsoundingNoteTo), to = Math.Max(hitsoundingNoteFrom, hitsoundingNoteTo);
            timelineMarkerRange.Fill(from, to, hitsoundingNoteMarqueeIndices);
            SelectObjects(hitsoundingNoteSelectionBefore.Concat(hitsoundingNoteMarqueeIndices.Select(i => timelineMarkers[i].Id)
                .Where(CanSelectCollaborationObject)));
        }
        return true;
    }

    private bool EndHitsoundingNotesPointer()
    {
        var gesture = hitsoundingNoteGesture;
        if (gesture == HitsoundingNoteGesture.None) return false;
        hitsoundingNoteGesture = HitsoundingNoteGesture.None;
        hitsoundingNoteMoveMarkers = [];
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
        if (hitsoundingNoteMoved && hitsoundingNoteGesture is HitsoundingNoteGesture.Move or HitsoundingNoteGesture.Start or HitsoundingNoteGesture.End)
        {
            float scale = Math.Min(64, lane.Height) / 64;
            float ghostY = lane.Y + (lane.Height - 64 * scale) / 2 + 27 * scale;
            foreach (var marker in hitsoundingNoteMoveMarkers)
            {
                if (hitsoundingNoteGesture == HitsoundingNoteGesture.Move ? !objectSelection.Contains(marker.Id) : marker.Id != hitsoundingNoteId) continue;
                double start = HitsoundingNotePreviewTime(marker.Id, marker.Start), end = HitsoundingNotePreviewTime(marker.Id, marker.End);
                float left = X(start), right = X(end);
                if (right < lane.X - 19 * scale || left > lane.Right + 19 * scale) continue;
                uint colour = marker.IsBanana ? Gold : ComboColour(marker.Id, useFallbackPalette: true, eventIndex: marker.EventIndex);
                float y = ghostY + marker.StackOffset * scale;
                if (end > start) c.Fill(new(left - 19 * scale, y - 19 * scale, right - left + 38 * scale, 38 * scale), colour, 19 * scale, .25f);
                void Ring(double time) => FruitsAtelier.App.Skinning.CatchSkin.DrawTimelineCircle(c, skin ?? defaultSkin,
                    X(time), y, 38 * scale, colour, opacity: .45f);
                Ring(start);
                if (end > start) Ring(end);
                if (marker.Spans > 1 && end > start)
                {
                    double spanDuration = (end - start) / marker.Spans;
                    int first = (int)Math.Clamp(Math.Ceiling((HitsoundingStart - start) / spanDuration), 1, marker.Spans);
                    int last = (int)Math.Clamp(Math.Floor((HitsoundingStart + hitsoundingSpan - start) / spanDuration), 0, marker.Spans - 1);
                    for (int span = first; span <= last; span++) Ring(start + span * spanDuration);
                }
            }
            return;
        }
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.Marquee && hitsoundingNoteMoved)
        {
            var box = new Rect(Math.Min(X(hitsoundingNoteFrom), X(hitsoundingNoteTo)), lane.Y,
                Math.Abs(X(hitsoundingNoteTo) - X(hitsoundingNoteFrom)), lane.Height);
            c.Fill(box, Accent, opacity: .15f); c.Stroke(box, Accent, 1); return;
        }
        if (!hitsoundingDraw || notesLocked || !lane.Contains(mouseX, mouseY)
            || hitsoundingHeadDrag || hitsoundingMarquee || hitsoundingPainting
            || hitsoundingNoteGesture is not (HitsoundingNoteGesture.None or HitsoundingNoteGesture.Marquee)
            || timelineObjects.Any(t => t.Bounds.Contains(mouseX, mouseY))) return;
        if (hitsoundingNoteGesture == HitsoundingNoteGesture.Marquee && !hitsoundingNoteCanCreate) return;
        float height = Math.Min(64, lane.Height);
        float cy = lane.Y + (lane.Height - height) / 2 + 27 * height / 64;
        FruitsAtelier.App.Skinning.CatchSkin.DrawTimelineCircle(c, skin ?? defaultSkin,
            X(HitsoundingNoteTime(mouseX)), cy, 38 * height / 64, Accent, opacity: .45f);
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
