using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private TimingPoint[] hitsoundingTimingClipboard = [];

    private bool HitsoundingTimingClipboardKey(int key, bool ctrl, bool shift)
    {
        if (!ctrl || shift || altHeld) return false;
        if (key == 67 && hitsoundingTimingSelection.Count > 0)
        {
            hitsoundingTimingClipboard = Document.TimingPoints.Where(hitsoundingTimingSelection.Contains)
                .OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).Select(TimingEditing.Copy).ToArray();
            hitsoundingClipboardContainsNotes = false;
            hitsoundingClipboard = []; hitsoundingStoryboardClipboard = []; hitsoundingStoryboardClipboardAudio.Clear();
            if (hitsoundingTimingClipboard.Length > 0) RequestCopyText?.Invoke(TimingEditing.Serialize(hitsoundingTimingClipboard));
            return true;
        }
        if (key != 86) return false;
        if (RequestPasteObjects is not null) RequestClipboardPaste();
        else if (hitsoundingTimingClipboard.Length > 0) PasteTimingPoints(hitsoundingTimingClipboard);
        else if (hitsoundingClipboardContainsNotes) PasteHitsoundingNotes();
        else PasteHitsoundingSelection();
        return true;
    }

    private bool TryPasteTimingClipboard(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is 0 or > 10000 || lines.Any(line => line.Split(',') is not { Length: 8 } fields
            || fields[6] is not ("0" or "1"))) return false;
        TimingPoint[] points;
        try { points = TimingEditing.Parse(text); }
        catch (ArgumentException error) { StatusMessage = error.Message; return true; }
        PasteTimingPoints(points);
        return true;
    }

    private void PasteTimingPoints(TimingPoint[] points)
    {
        if (!CommitField() || hitsoundingNoteGesture != HitsoundingNoteGesture.None
            || hitsoundingTimingDrag is not null || hitsoundingPainting || hitsoundingMarquee) return;
        var pasted = points.Select(TimingEditing.Copy).ToArray();
        var times = pasted.Select(p => (p.TimeMs, p.Uninherited)).ToHashSet();
        if (Edit(L.Get("timing.edit"), () =>
        {
            Document.TimingPoints.RemoveAll(p => times.Contains((p.TimeMs, p.Uninherited)));
            int order = Document.TimingPoints.Select(p => p.SourceOrder).Where(v => v < int.MaxValue).DefaultIfEmpty(-1).Max();
            foreach (var point in pasted) { point.SourceOrder = ++order; point.OriginalLine = null; }
            Document.TimingPoints.AddRange(pasted);
            var first = Document.TimingPoints.Where(p => p.Uninherited).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).FirstOrDefault();
            if (first is not null) { Document.BeatLengthMs = TimingMap.EffectiveBeatLength(first.BeatLengthMs); Document.TimingOffsetMs = first.TimeMs; }
        }))
        {
            hitsoundingTimingSelection.Clear(); hitsoundingTimingSelection.UnionWith(pasted);
            hitsoundingSelectedTiming = pasted[^1]; inspectorConversion = null;
        }
    }
}
