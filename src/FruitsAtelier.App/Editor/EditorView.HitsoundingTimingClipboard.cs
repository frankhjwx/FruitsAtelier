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
            hitsoundingClipboard = []; hitsoundingStoryboardClipboard = [];
            if (hitsoundingTimingClipboard.Length > 0) RequestCopyText?.Invoke(TimingEditing.Serialize(hitsoundingTimingClipboard));
            return true;
        }
        if (key != 86 || hitsoundingTimingClipboard.Length == 0) return false;
        if (!CommitField()) return true;
        var pasted = hitsoundingTimingClipboard.Select(TimingEditing.Copy).ToArray();
        var times = pasted.Select(p => p.TimeMs).ToHashSet();
        if (Edit(L.Get("timing.edit"), () =>
        {
            Document.TimingPoints.RemoveAll(p => times.Contains(p.TimeMs));
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
        return true;
    }
}
