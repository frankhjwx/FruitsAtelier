using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly HashSet<(Guid Id, int Event, int Lane)> hitsoundingMultiSelection = [];
    private bool hitsoundingMarquee, hitsoundingPasteConfirmation;
    private (Guid Id, int Event, int Lane)? hitsoundingMoveSource;
    private ConvertedCatchObject? hitsoundingMoveTarget;
    private (float X, float Y) hitsoundingMoveStart;
    private bool hitsoundingMoveActive;
    private (float X, float Y) hitsoundingMarqueeStart, hitsoundingMarqueeEnd;
    private sealed record HitsoundingCopy(double Offset, int Lane, HitSampleSettings Sample, int TimingIndex, bool InheritIndex);
    private HitsoundingCopy[] hitsoundingClipboard = [];
    private readonly HashSet<(Guid Id, int Event)> hitsoundingClipboardSources = [];
    private double hitsoundingClipboardPlayhead;
    private double hitsoundingMarqueeStartTime;
    private bool hitsoundingMarqueeMoved;
    private void SyncHitsoundingNoteSelection(ConvertedCatchObject? preferred = null)
    {
        var selected = preferred is not null && hitsoundingMultiSelection.Any(k => k.Id == preferred.SourceId && k.Event == preferred.EventIndex)
            ? preferred : hitsoundingMultiSelection.Select(k => hitsoundingNoteLookup.GetValueOrDefault((k.Id, k.Event))).LastOrDefault(n => n is not null);
        SelectObjects(hitsoundingMultiSelection.Select(k => k.Id), selected?.SourceId ?? Guid.Empty);
        hitsoundingSelected = selected is null ? null : (selected.SourceId, selected.EventIndex);
        hitsoundingSelectedTiming = null;
        if (selected is not null) PickSoundEdge(selected);
        StatusMessage = L.Get("editor.status.objectsSelected", objectSelection.Count + hitsoundingStoryboardSelection.Count);
    }
    private float HitsoundingMarqueeStartX => hitsoundingGridBounds.X
        + (float)((hitsoundingMarqueeStartTime - HitsoundingStart) / hitsoundingSpan * hitsoundingGridBounds.Width);
    private Rect HitsoundingMarqueeBounds => new(Math.Min(HitsoundingMarqueeStartX, hitsoundingMarqueeEnd.X),
        Math.Min(hitsoundingMarqueeStart.Y, hitsoundingMarqueeEnd.Y), Math.Abs(hitsoundingMarqueeEnd.X - HitsoundingMarqueeStartX),
        Math.Abs(hitsoundingMarqueeEnd.Y - hitsoundingMarqueeStart.Y));
    private void UpdateHitsoundingMarquee()
    {
        if (!hitsoundingMarqueeMoved) return;
        var r = HitsoundingMarqueeBounds;
        hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
        double end = HitsoundingStart + (hitsoundingMarqueeEnd.X - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan;
        double from = Math.Min(hitsoundingMarqueeStartTime, end), to = Math.Max(hitsoundingMarqueeStartTime, end);
        float laneHeight = hitsoundingGridBounds.Height / 7;
        float tagHeight = Math.Clamp(laneHeight - 2, 24, 36);
        // Time-range candidates retain selected sounds after playback carries them offscreen.
        foreach (var tags in hitsoundingTagLanes.Append(hitsoundingStoryboardTags))
        {
            if (tags.Length == 0) continue;
            int lane = tags[0].Lane;
            if (hitsoundingLocked[lane]) continue;
            float y = hitsoundingGridBounds.Y + (lane + .5f) * laneHeight - tagHeight / 2;
            if (y >= r.Bottom || y + tagHeight <= r.Y) continue;
            for (int i = LowerTag(tags, from); i < tags.Length && tags[i].Time <= to; i++)
            {
                var tag = tags[i];
                if (tag.Storyboard is { } sample) hitsoundingStoryboardSelection.Add(sample);
                else if (lane is >= 1 and <= 4 && tag.Note is { Kind: CatchObjectKind.Fruit } note)
                    hitsoundingMultiSelection.Add((note.SourceId, note.EventIndex, lane));
            }
        }
        // Visible rectangles preserve partial-tag selection in both display modes.
        foreach (var hit in hitsoundingStoryboardHits)
        {
            if (Intersects(hit.Bounds, r)) hitsoundingStoryboardSelection.Add(hit.Sample);
            else hitsoundingStoryboardSelection.Remove(hit.Sample);
        }
        foreach (var hit in hitsoundingSoundHits)
            if (hit.Lane is >= 1 and <= 4 && hit.Note.Kind == CatchObjectKind.Fruit)
            {
                var key = (hit.Note.SourceId, hit.Note.EventIndex, hit.Lane);
                if (Intersects(hit.Bounds, r)) hitsoundingMultiSelection.Add(key);
                else hitsoundingMultiSelection.Remove(key);
            }
    }
    private int HitsoundingPlacementAdditionBank(ConvertedCatchObject note) => hitsoundingBrush;
    private void CycleHitsoundingSampleSet()
    {
        int bank = hitsoundingBrush;
        if (hitsoundingNotesSelection)
        {
            var first = hitsoundingNotes.FirstOrDefault(n => objectSelection.Contains(n.SourceId));
            if (first is not null) bank = hitsoundingResolver!.Describe(first).NormalSet;
        }
        else
        {
            var first = hitsoundingMultiSelection.Where(k => k.Lane is >= 1 and <= 4 && hitsoundingNoteLookup.ContainsKey((k.Id, k.Event)))
                .OrderBy(k => hitsoundingNoteLookup[(k.Id, k.Event)].TimeMs).ThenBy(k => k.Lane).FirstOrDefault();
            if (first.Id != Guid.Empty)
            {
                var sample = hitsoundingResolver!.Describe(hitsoundingNoteLookup[(first.Id, first.Event)]);
                bank = first.Lane == 1 ? sample.NormalSet : sample.AdditionSet;
            }
        }
        SetHitsoundingBrush(bank switch { 2 => 1, 1 => 3, _ => 2 });
    }
    private void CopyHitsoundingSelection()
    {
        hitsoundingClipboardContainsNotes = false;
        var entries = hitsoundingMultiSelection.Where(k => k.Lane is >= 1 and <= 4 && hitsoundingNoteLookup.ContainsKey((k.Id, k.Event)))
            .Select(k => (Note: hitsoundingNoteLookup[(k.Id, k.Event)], k.Lane)).OrderBy(e => e.Note.TimeMs).ToArray();
        hitsoundingClipboard = []; hitsoundingStoryboardClipboard = [];
        hitsoundingClipboardSources.Clear();
        hitsoundingClipboardPlayhead = playhead;
        hitsoundingClipboardSources.UnionWith(entries.Select(e => (e.Note.SourceId, e.Note.EventIndex)));
        if (entries.Length == 0 && hitsoundingStoryboardSelection.Count == 0) return;
        double start = Math.Min(entries.FirstOrDefault().Note?.TimeMs ?? double.PositiveInfinity, hitsoundingStoryboardSelection.Select(s => s.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min());
        hitsoundingStoryboardClipboard = hitsoundingStoryboardSelection.Select(s => s with { TimeMs = s.TimeMs - start }).ToArray();
        hitsoundingClipboard = entries.Select(e => new HitsoundingCopy(e.Note.TimeMs - start, e.Lane, hitsoundingResolver!.Describe(e.Note), HitsoundingTimingIndex(e.Note.TimeMs), hitsoundingResolver.EventSettings(e.Note).InheritedFields.HasFlag(InheritedSampleFields.Index))).ToArray();
    }
    private void DeleteHitsoundingAt(float x, float y)
    {
        if (!CommitField()) return;
        var sound = hitsoundingSoundHits.LastOrDefault(h => h.Bounds.Contains(x, y));
        var storyboard = hitsoundingStoryboardHits.LastOrDefault(h => h.Bounds.Contains(x, y));
        if (sound.Note is null && storyboard.Sample.Path is null)
        {
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
            hitsoundingSelected = null; hitsoundingSelectedTiming = null; SelectObjects([]);
            return;
        }
        if (storyboard.Sample is { Path: not null } sample && !hitsoundingStoryboardSelection.Contains(sample))
        {
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear(); hitsoundingStoryboardSelection.Add(sample);
            hitsoundingSelected = null; hitsoundingSelectedTiming = null; SelectObjects([]);
        }
        else if (sound.Note is { } note && !hitsoundingMultiSelection.Contains((note.SourceId, note.EventIndex, sound.Lane)))
        {
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
            hitsoundingMultiSelection.Add((note.SourceId, note.EventIndex, sound.Lane)); SelectHitsoundingNote(note, seek: false);
        }
        DeleteHitsoundingSelection();
    }

    private void DeleteHitsoundingSelection()
    {
        var changes = new List<EventHitsound>();
        foreach (var group in hitsoundingMultiSelection.Where(k => k.Lane is >= 2 and <= 4).GroupBy(k => (k.Id, k.Event)))
        {
            if (!hitsoundingNoteLookup.TryGetValue(group.Key, out var note)) continue;
            var setting = hitsoundingResolver!.EventSettings(note);
            int mask = group.Aggregate(0, (value, k) => value | (1 << (k.Lane - 1)));
            if ((setting.Sample.Additions & mask) != 0)
                changes.Add(setting with { Sample = setting.Sample with { Additions = setting.Sample.Additions & ~mask } });
        }
        void ApplyNotes() { foreach (var change in changes) SetHitsoundingEvent(change); }
        if (hitsoundingStoryboardSelection.Count > 0) EditHitsoundingStoryboard(hitsoundingStoryboardSelection.ToArray(), [], editNotes: changes.Count > 0 ? ApplyNotes : null);
        else if (changes.Count > 0) Edit(L.Get("assist.soundChange"), ApplyNotes);
        hitsoundingMultiSelection.RemoveWhere(k => k.Lane is >= 2 and <= 4);
    }
    private void PasteHitsoundingSelection()
    {
        if (!CommitField()) return;
        double destination = !AudioPlaying && playhead == hitsoundingClipboardPlayhead && hitsoundingMultiSelection.Count > 0 && !hitsoundingMultiSelection.Any(k => hitsoundingClipboardSources.Contains((k.Id, k.Event)))
            ? hitsoundingMultiSelection.Select(k => hitsoundingNoteLookup.GetValueOrDefault((k.Id, k.Event))?.TimeMs ?? playhead).Min() : playhead;
        var samples = hitsoundingStoryboardClipboard.Select(s => s with { TimeMs = s.TimeMs + destination }).ToArray();
        if (hitsoundingClipboard.Length == 0) { if (samples.Length > 0) EditHitsoundingStoryboard([], samples); return; }
        TransferHitsounding(hitsoundingClipboard, destination, storyboard: samples);
    }
    private int HitsoundingTimingIndex(double time) => hitsoundingResolver!.Describe(
        new(Guid.Empty, 0, CatchObjectKind.Fruit, time, 0, 0, 0, 0, true)).Index;
    private void TransferHitsounding(HitsoundingCopy[] copied, double targetTime, (Guid Id, int Event, int Lane)? moveSource = null, ConvertedCatchObject? moveTarget = null, StoryboardSample[]? storyboard = null)
    {
        var targets = new List<(HitsoundingCopy Copy, ConvertedCatchObject Note)>();
        foreach (var copy in copied)
        {
            if (hitsoundingLocked[copy.Lane]) continue;
            double time = targetTime + copy.Offset;
            int first = FirstAfter(hitsoundingNotes, time - 1.001);
            for (int i = first; i < hitsoundingNotes.Length && hitsoundingNotes[i].TimeMs <= time + 1; i++)
                if (hitsoundingNotes[i].Kind == CatchObjectKind.Fruit && (moveTarget is null || hitsoundingNotes[i].SourceId == moveTarget.SourceId && hitsoundingNotes[i].EventIndex == moveTarget.EventIndex)) targets.Add((copy, hitsoundingNotes[i]));
        }
        if (targets.Count == 0 && storyboard is { Length: > 0 }) { EditHitsoundingStoryboard([], storyboard); return; }
        if (targets.Count == 0) { StatusMessage = L.Get("hitsounding.noPasteTargets"); return; }
        bool mismatch = targets.Any(t => HitsoundingTimingIndex(t.Note.TimeMs) != t.Copy.TimingIndex);
        var document = Document;
        void Apply(bool adjust)
        {
            if (!ReferenceEquals(document, Document)) return;
            var changes = new Dictionary<(Guid, int), EventHitsound>();
            if (moveSource is { } source && hitsoundingNoteLookup.TryGetValue((source.Id, source.Event), out var sourceNote))
            {
                var original = hitsoundingResolver!.EventSettings(sourceNote);
                changes[(source.Id, source.Event)] = original with { Sample = original.Sample with { Additions = original.Sample.Additions & ~(1 << (source.Lane - 1)) } };
            }
            foreach (var (copy, note) in targets)
            {
                var key = (note.SourceId, note.EventIndex);
                var setting = changes.GetValueOrDefault(key) ?? hitsoundingResolver!.EventSettings(note);
                var sample = setting.Sample;
                var inherited = setting.InheritedFields;
                if (copy.Lane > 1) sample = sample with { Additions = sample.Additions | (1 << (copy.Lane - 1)) };
                if (adjust)
                {
                    sample = sample with { Index = copy.Sample.Index };
                    bool inheritIndex = copy.Sample.Index == 0 || copy.InheritIndex && HitsoundingTimingIndex(note.TimeMs) == copy.TimingIndex;
                    inherited = inheritIndex ? inherited | InheritedSampleFields.Index : inherited & ~InheritedSampleFields.Index;
                    if (copy.Lane == 1) { sample = sample with { NormalSet = copy.Sample.NormalSet }; inherited &= ~InheritedSampleFields.NormalSet; }
                    else { sample = sample with { AdditionSet = copy.Sample.AdditionSet }; inherited &= ~InheritedSampleFields.AdditionSet; }
                }
                changes[key] = setting with { Sample = sample, InheritedFields = inherited };
            }
            if (storyboard is { Length: > 0 })
            {
                EditHitsoundingStoryboard([], storyboard, editNotes: () => { foreach (var setting in changes.Values) SetHitsoundingEvent(setting); });
                hitsoundingMultiSelection.Clear();
                return;
            }
            if (Edit(L.Get(moveSource is null ? "hitsounding.paste" : "hitsounding.move"), () => { foreach (var setting in changes.Values) SetHitsoundingEvent(setting); }))
            {
                hitsoundingMultiSelection.Clear();
                foreach (var target in targets) hitsoundingMultiSelection.Add((target.Note.SourceId, target.Note.EventIndex, target.Copy.Lane));
            }
        }
        if (!mismatch) { Apply(true); return; }
        ShowDiscardConfirmation(answer => { if (answer is 6 or 7) Apply(answer == 6); });
        hitsoundingPasteConfirmation = true;
    }
    private bool MoveHitsoundingSound(float x, float y)
    {
        if (hitsoundingMoveSource is not { } source) return false;
        if (Math.Abs(x - hitsoundingMoveStart.X) > ContentDragThreshold || Math.Abs(y - hitsoundingMoveStart.Y) > ContentDragThreshold) hitsoundingMoveActive = true;
        hitsoundingMoveTarget = hitsoundingMoveActive ? HitsoundingPlacementNoteAt(x, y) : null;
        if (hitsoundingMoveTarget?.Kind != CatchObjectKind.Fruit) hitsoundingMoveTarget = null;
        return true;
    }
    private bool EndHitsoundingSoundMove()
    {
        if (hitsoundingMoveSource is not { } source) return false;
        var target = hitsoundingMoveTarget;
        bool moved = hitsoundingMoveActive;
        hitsoundingMoveSource = null; hitsoundingMoveTarget = null; hitsoundingMoveActive = false;
        if (!hitsoundingNoteLookup.TryGetValue((source.Id, source.Event), out var note)) return true;
        if (!moved) return true;
        if (target is null || target.SourceId == source.Id && target.EventIndex == source.Event) return true;
        TransferHitsounding([new(0, source.Lane, hitsoundingResolver!.Describe(note), HitsoundingTimingIndex(note.TimeMs), hitsoundingResolver.EventSettings(note).InheritedFields.HasFlag(InheritedSampleFields.Index))], target.TimeMs, source, target);
        return true;
    }
    private void DrawHitsoundingPasteConfirmation(ICanvas c)
    {
        float w = Math.Min(640, width - 32), x = (width - w) / 2, y = (height - 230) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .65f);
        c.Fill(new(x, y, w, 230), Panel, 8); c.Stroke(new(x, y, w, 230), Accent, 2, 8);
        c.Text(L.Get("hitsounding.pasteMismatch"), x + 20, y + 24, 18, Foreground, w - 40, true);
        c.Text(L.Get("hitsounding.pasteMismatchHelp"), x + 20, y + 64, 12, Muted, w - 40);
        Button(c, new(x + 20, y + 110, w - 40, 32), L.Get("hitsounding.pasteAdjust"), () => AnswerDiscard(6));
        Button(c, new(x + 20, y + 146, w - 40, 32), L.Get("hitsounding.pasteKeep"), () => AnswerDiscard(7));
        Button(c, new(x + 20, y + 182, w - 40, 32), L.Get("mac.cancel"), () => AnswerDiscard(2));
    }
}
