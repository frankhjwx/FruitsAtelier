using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapDocument? hitsoundingNotesClipboard;
    private Guid? hitsoundingNotesClipboardDifficulty;
    private bool hitsoundingClipboardContainsNotes;
    private sealed record HitsoundingCopiedNote(Guid Id, double Start, double End, int Spans, bool Banana,
        (double Time, CatchObjectKind Kind, EventHitsound Sound)[] Events);
    private HitsoundingCopiedNote[] hitsoundingCopiedNotes = [];

    private void CopyHitsoundingNotes()
    {
        if (!TryCopySnapshot(out var snapshot)) return;
        RefreshTimelineSources();
        var ids = ClipboardSelectedParentIds();
        var events = hitsoundingNotes.Where(n => ids.Contains(n.SourceId)).GroupBy(n => n.SourceId)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.TimeMs, n.Kind,
                hitsoundingResolver!.EventSettings(n) with { Sample = hitsoundingResolver.Describe(n), InheritedFields = InheritedSampleFields.None })).ToArray());
        hitsoundingCopiedNotes = timelineSources.Where(s => ids.Contains(s.Id)).OrderBy(s => s.Start).ThenBy(s => s.SourceOrder)
            .Select(s => new HitsoundingCopiedNote(s.Id, s.Start, s.End, s.Spans, s.IsBanana, events.GetValueOrDefault(s.Id) ?? [])).ToArray();
        hitsoundingNotesClipboard = snapshot;
        hitsoundingNotesClipboardDifficulty = difficulties[activeDifficulty].Id;
        hitsoundingClipboardContainsNotes = true;
        StatusMessage = L.Get("editor.status.objectsCopied", hitsoundingCopiedNotes.Length);
    }

    private void PasteHitsoundingNotes()
    {
        if (!hitsoundingAllowNotes || notesLocked || hitsoundingNoteGesture != HitsoundingNoteGesture.None) return;
        if (hitsoundingNotesClipboard is null || hitsoundingCopiedNotes.Length == 0) return;
        RefreshTimelineSources();
        double offset = playhead - hitsoundingCopiedNotes[0].Start;
        double from = playhead, to = hitsoundingCopiedNotes.Max(n => n.End) + offset;
        var targets = timelineSources.Where(s => s.End >= from - .5 && s.Start <= to + .5).OrderBy(s => s.Start).ThenBy(s => s.SourceOrder).ToArray();
        if (targets.Length == 0)
        {
            var previous = objectClipboard; var previousDifficulty = clipboardDifficulty;
            try
            {
                objectClipboard = hitsoundingNotesClipboard; clipboardDifficulty = hitsoundingNotesClipboardDifficulty;
                if (PasteSelection())
                {
                    hitsoundingNotesSelection = true; hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
                }
            }
            finally { objectClipboard = previous; clipboardDifficulty = previousDifficulty; }
            return;
        }
        bool matches = targets.Length == hitsoundingCopiedNotes.Length;
        var changes = new List<EventHitsound>();
        var targetIds = targets.Select(t => t.Id).ToHashSet();
        var targetEvents = hitsoundingNotes.Where(n => targetIds.Contains(n.SourceId)).GroupBy(n => n.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
        for (int i = 0; matches && i < targets.Length; i++)
        {
            var source = hitsoundingCopiedNotes[i]; var target = targets[i];
            matches = Math.Abs(source.Start + offset - target.Start) <= .5 && Math.Abs(source.End + offset - target.End) <= .5
                && source.Spans == target.Spans && source.Banana == target.IsBanana;
            var events = targetEvents.GetValueOrDefault(target.Id) ?? [];
            matches &= events.Length == source.Events.Length;
            for (int j = 0; matches && j < events.Length; j++)
            {
                var old = source.Events[j]; var next = events[j];
                matches = old.Kind == next.Kind && Math.Abs(old.Time + offset - next.TimeMs) <= 1;
                if (matches) changes.Add(old.Sound with { SourceId = next.SourceId, EventIndex = next.EventIndex, EdgeIndex = hitsoundingResolver!.EventSettings(next).EdgeIndex });
            }
        }
        if (!matches) { StatusMessage = L.Get("hitsounding.noteRhythmMismatch"); return; }
        if (targets.Any(t => !CanSelectCollaborationObject(t.Id))) return;
        if (Edit(L.Get("hitsounding.paste"), () => { foreach (var change in changes) SetHitsoundingEvent(change); }))
        {
            hitsoundingNotesSelection = true; hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
            SelectObjects(targets.Select(t => t.Id));
        }
    }

    private bool HitsoundingNotesKey(int key, bool ctrl, bool shift)
    {
        if (hitsoundingNoteGesture != HitsoundingNoteGesture.None) return key != 27;
        if (ctrl && !altHeld && key == 86 && hitsoundingClipboardContainsNotes) { PasteHitsoundingNotes(); return true; }
        if (!hitsoundingAllowNotes || !hitsoundingNotesSelection) return false;
        if (ctrl && !altHeld && key is 187 or 189 or 107 or 109)
        {
            if (objectSelection.Any(id => Document.Tracks.Any(t => t.Id == id) || Document.ImportedSliders.Any(s => s.Id == id))) UiScaleKey(key, ctrl);
            return true;
        }
        if (ctrl && key is 67 or 88)
        {
            CopyHitsoundingNotes();
            if (key == 88 && !notesLocked) DeleteSelectedObjects();
            return true;
        }
        if (!ctrl && key == 46) { if (!notesLocked) DeleteSelectedObjects(); return true; }
        if (ctrl && !shift && key == 65)
        {
            SelectObjects(timelineSources.Where(s => CanSelectCollaborationObject(s.Id)).Select(s => s.Id)); return true;
        }
        return false;
    }
}
