using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool hitsoundingPanning;
    private float hitsoundingPanX;
    private double hitsoundingPanTime;
    private long hitsoundingScrollTimestamp;
    private double hitsoundingNavigationStart, hitsoundingNavigationSpan;
    private int hitsoundingNavigationDivisor;
    private bool HitsoundingNavigationGesture => hitsoundingMarquee || hitsoundingMoveSource is not null
        || hitsoundingStoryboardMove is not null || hitsoundingNoteGesture != HitsoundingNoteGesture.None
        || hitsoundingTimingDrag is not null || hitsoundingTimingBox;
    private bool HitsoundingNavigationMoved => hitsoundingMarqueeMoved && hitsoundingMarquee
        || hitsoundingMoveActive || hitsoundingStoryboardMoved && hitsoundingStoryboardMove is not null
        || hitsoundingNoteMoved && hitsoundingNoteGesture != HitsoundingNoteGesture.None
        || hitsoundingTimingMoved && (hitsoundingTimingDrag is not null || hitsoundingTimingBox);
    private void RememberHitsoundingNavigation()
    {
        hitsoundingNavigationStart = HitsoundingStart;
        hitsoundingNavigationSpan = hitsoundingSpan;
        hitsoundingNavigationDivisor = divisor;
        hitsoundingScrollTimestamp = timeProvider.GetTimestamp();
    }
    private void ContinueHitsoundingNavigation(bool startGesture = false)
    {
        bool changed = hitsoundingNavigationStart != HitsoundingStart || hitsoundingNavigationSpan != hitsoundingSpan
            || hitsoundingNavigationDivisor != divisor;
        RememberHitsoundingNavigation();
        if (!changed || !HitsoundingNavigationGesture || !startGesture && !HitsoundingNavigationMoved) return;
        if (hitsoundingNoteGesture != HitsoundingNoteGesture.None) hitsoundingNoteMoved = true;
        if (hitsoundingMoveSource is not null) hitsoundingMoveActive = true;
        if (hitsoundingStoryboardMove is not null) hitsoundingStoryboardMoved = true;
        if (hitsoundingMarquee) hitsoundingMarqueeMoved = true;
        if (hitsoundingTimingDrag is not null || hitsoundingTimingBox) hitsoundingTimingMoved = true;
        MoveHitsoundingCanvasPointer(mouseX, mouseY);
    }
    private double HitsoundingScrollDirection
    {
        get
        {
            if (!HitsoundingPageVisible || !DockContentInputAvailable || IsEditingText || DiscardConfirmationVisible
                || menu >= 0 || contextItems.Count > 0 || !HitsoundingNavigationMoved || hitsoundingPanning
                || mouseY < HitsoundingTimingStrip.Y || mouseY > hitsoundingGridBounds.Bottom) return 0;
            const double edge = 24;
            return mouseX < hitsoundingGridBounds.X + edge ? -Math.Clamp((hitsoundingGridBounds.X + edge - mouseX) / edge, 0, 1)
                : mouseX > hitsoundingGridBounds.Right - edge ? Math.Clamp((mouseX - hitsoundingGridBounds.Right + edge) / edge, 0, 1) : 0;
        }
    }
    private bool HitsoundingScrollNeedsRedraw => HitsoundingScrollDirection > 0 && playhead < TimelineDurationMs
        || HitsoundingScrollDirection < 0 && playhead > 0;
    private void AdvanceHitsoundingNavigation()
    {
        long now = timeProvider.GetTimestamp();
        double elapsed = Math.Clamp(timeProvider.GetElapsedTime(hitsoundingScrollTimestamp, now).TotalSeconds, 0, .05);
        hitsoundingScrollTimestamp = now;
        if (HitsoundingScrollNeedsRedraw && elapsed > 0)
            SeekTo(playhead + HitsoundingScrollDirection * 600 * elapsed * hitsoundingSpan / Math.Max(1, hitsoundingGridBounds.Width));
        ContinueHitsoundingNavigation();
    }
    private bool HitsoundingNavigationKey(int key, bool ctrl, bool shift)
    {
        bool handled = true;
        if (ctrl && !altHeld && key is 38 or 40) AdjustPlaybackSpeed(key == 38 ? 1 : -1, shift);
        else if (ctrl && !altHeld && !shift && key == 77) HandleLegacyShortcut(key, false);
        else if (!ctrl && !altHeld && shift && key is >= 49 and <= 57)
        { if (IsLazerEditing) HandleLazerKey(key, false, true); else HandleLegacyKey(key, true); }
        else if (ctrl && !altHeld && key == 66)
        {
            if (!HitsoundingNavigationGesture && !hitsoundingPanning)
                Edit(L.Get("timeline.bookmark"), () =>
                {
                    int time = (int)Math.Clamp(Math.Round(playhead), 0, int.MaxValue);
                    if (shift) OsuTimeline.RemoveNearestBookmark(Document, time); else OsuTimeline.AddBookmark(Document, time);
                });
        }
        else if (IsLazerEditing && !ctrl && !shift && altHeld && key is 37 or 39) SeekBookmark(key == 39);
        else if (ctrl && !altHeld && key is 37 or 39)
        {
            if (IsLazerEditing && shift) SeekLazerSamplePoint(key == 37 ? -1 : 1);
            else if (!shift && !IsLazerEditing) SeekBookmark(key == 39);
            else if (!shift)
            {
                var times = ClipboardParents(Document).Select(p => p.TimeMs);
                double target = key == 37 ? times.Where(t => t < playhead).DefaultIfEmpty(double.NaN).Max()
                    : times.Where(t => t > playhead).DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(target)) SeekLazerSmoothlyTo(target);
            }
        }
        else if (!ctrl && !altHeld && key is 37 or 39)
        { if (IsLazerEditing) SeekLazerStep(key == 37 ? -1 : 1, shift); else HandleLegacyKey(key, shift); }
        else if (!ctrl && !altHeld && !shift && key is 38 or 40 or 90 or 86 or 35)
        { if (IsLazerEditing) HandleLazerKey(key, false, false); else HandleLegacyKey(key, false); }
        else if (!ctrl && !altHeld && !shift && key == 36) SeekTo(0);
        else if (!ctrl && !altHeld && !shift && key == 88) HandleLegacyKey(key, false);
        else if (!ctrl && !altHeld && !shift && key is 32 or 67) TogglePlayback();
        else if (!ctrl && !altHeld && !shift && key is 187 or 189)
            ZoomHitsounding(key == 187 ? .8 : 1.25, hitsoundingGridBounds.X + hitsoundingGridBounds.Width * .3f);
        else if (!ctrl && !altHeld && !shift && key is 74 or 75)
        { if (!HitsoundingNavigationGesture && !hitsoundingPanning) NudgeHitsoundingSelection(key == 74 ? -1 : 1); }
        else handled = false;
        if (handled)
        {
            ContinueHitsoundingNavigation(startGesture: true);
            if (hitsoundingPanning) { hitsoundingPanX = mouseX; hitsoundingPanTime = playhead; }
        }
        return handled;
    }
    private void NudgeHitsoundingSelection(int direction)
    {
        if (notesLocked || !CommitField()) return;
        if (hitsoundingNotesSelection && hitsoundingAllowNotes)
        { HandleLegacyKey(direction < 0 ? 74 : 75, false); return; }
        var sources = hitsoundingMultiSelection.Where(k => k.Lane is >= 2 and <= 4 && !hitsoundingLocked[k.Lane]
            && hitsoundingNoteLookup.ContainsKey((k.Id, k.Event))).ToArray();
        var samples = hitsoundingLocked[6] ? [] : hitsoundingStoryboardSelection.ToArray();
        double start = sources.Select(k => hitsoundingNoteLookup[(k.Id, k.Event)].TimeMs)
            .Concat(samples.Select(s => s.TimeMs)).DefaultIfEmpty(double.NaN).Min();
        if (double.IsNaN(start)) return;
        double delta = direction * SnapTiming().At(start).BeatLengthMs / divisor;
        if (start + delta < 0) return;
        var copies = sources.Select(k =>
        {
            var note = hitsoundingNoteLookup[(k.Id, k.Event)];
            return new HitsoundingCopy(note.TimeMs - start, k.Lane, hitsoundingResolver!.Describe(note),
                HitsoundingTimingIndex(note.TimeMs), hitsoundingResolver.EventSettings(note).InheritedFields.HasFlag(InheritedSampleFields.Index));
        }).ToArray();
        var moved = samples.Select(s => s with { TimeMs = s.TimeMs + delta }).ToArray();
        if (sources.Length == 0) EditHitsoundingStoryboard(samples, moved, move: true, retainSelection: true);
        else TransferHitsounding(copies, start + delta, storyboard: moved, moveSources: sources, storyboardRemove: samples);
    }
}
