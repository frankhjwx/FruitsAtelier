using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly HashSet<(Guid Id, int Event, int Lane)> hitsoundingMultiSelection = [];
    private bool hitsoundingMarquee, hitsoundingPasteConfirmation;
    private (float X, float Y) hitsoundingMarqueeStart, hitsoundingMarqueeEnd;
    private sealed record HitsoundingCopy(double Offset, int Lane, HitSampleSettings Sample);
    private HitsoundingCopy[] hitsoundingClipboard = [];
    private Rect HitsoundingMarqueeBounds => new(Math.Min(hitsoundingMarqueeStart.X, hitsoundingMarqueeEnd.X),
        Math.Min(hitsoundingMarqueeStart.Y, hitsoundingMarqueeEnd.Y), Math.Abs(hitsoundingMarqueeEnd.X - hitsoundingMarqueeStart.X),
        Math.Abs(hitsoundingMarqueeEnd.Y - hitsoundingMarqueeStart.Y));
    private void UpdateHitsoundingMarquee()
    {
        var r = HitsoundingMarqueeBounds;
        hitsoundingMultiSelection.Clear();
        if (r.Width < 3 && r.Height < 3) return;
        foreach (var hit in hitsoundingSoundHits)
            if (hit.Lane is >= 1 and <= 4 && hit.Note.Kind == CatchObjectKind.Fruit
                && hit.Bounds.Right > r.X && hit.Bounds.X < r.Right && hit.Bounds.Bottom > r.Y && hit.Bounds.Y < r.Bottom)
                hitsoundingMultiSelection.Add((hit.Note.SourceId, hit.Note.EventIndex, hit.Lane));
    }
    private void CopyHitsoundingSelection()
    {
        var entries = hitsoundingMultiSelection.Where(k => k.Lane is >= 1 and <= 4 && hitsoundingNoteLookup.ContainsKey((k.Id, k.Event)))
            .Select(k => (Note: hitsoundingNoteLookup[(k.Id, k.Event)], k.Lane)).OrderBy(e => e.Note.TimeMs).ToArray();
        if (entries.Length == 0) return;
        double start = entries[0].Note.TimeMs;
        hitsoundingClipboard = entries.Select(e => new HitsoundingCopy(e.Note.TimeMs - start, e.Lane, hitsoundingResolver!.Describe(e.Note))).ToArray();
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
        if (changes.Count > 0) Edit(L.Get("assist.soundChange"), () => { foreach (var change in changes) SetHitsoundingEvent(change); });
        hitsoundingMultiSelection.RemoveWhere(k => k.Lane is >= 2 and <= 4);
    }
    private void PasteHitsoundingSelection()
    {
        if (hitsoundingClipboard.Length == 0 || !CommitField()) return;
        var targets = new List<(HitsoundingCopy Copy, ConvertedCatchObject Note)>();
        foreach (var copy in hitsoundingClipboard)
        {
            double time = playhead + copy.Offset;
            int first = FirstAfter(hitsoundingNotes, time - 1.001);
            for (int i = first; i < hitsoundingNotes.Length && hitsoundingNotes[i].TimeMs <= time + 1; i++)
                if (hitsoundingNotes[i].Kind == CatchObjectKind.Fruit) targets.Add((copy, hitsoundingNotes[i]));
        }
        if (targets.Count == 0) { StatusMessage = L.Get("hitsounding.noPasteTargets"); return; }
        bool mismatch = targets.Any(t =>
        {
            var sample = hitsoundingResolver!.Describe(t.Note);
            return sample.Index != t.Copy.Sample.Index || (t.Copy.Lane == 1 ? sample.NormalSet != t.Copy.Sample.NormalSet : sample.AdditionSet != t.Copy.Sample.AdditionSet);
        });
        var document = Document;
        void Apply(bool adjust)
        {
            if (!ReferenceEquals(document, Document)) return;
            var changes = new Dictionary<(Guid, int), EventHitsound>();
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
                    inherited &= ~InheritedSampleFields.Index;
                    if (copy.Lane == 1) { sample = sample with { NormalSet = copy.Sample.NormalSet }; inherited &= ~InheritedSampleFields.NormalSet; }
                    else { sample = sample with { AdditionSet = copy.Sample.AdditionSet }; inherited &= ~InheritedSampleFields.AdditionSet; }
                }
                changes[key] = setting with { Sample = sample, InheritedFields = inherited };
            }
            if (Edit(L.Get("hitsounding.paste"), () => { foreach (var setting in changes.Values) SetHitsoundingEvent(setting); }))
            {
                hitsoundingMultiSelection.Clear();
                foreach (var target in targets) hitsoundingMultiSelection.Add((target.Note.SourceId, target.Note.EventIndex, target.Copy.Lane));
            }
        }
        if (!mismatch) { Apply(false); return; }
        ShowDiscardConfirmation(answer => { if (answer is 6 or 7) Apply(answer == 6); });
        hitsoundingPasteConfirmation = true;
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
