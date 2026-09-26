using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static readonly int[] QuickSnapDivisors = [3, 4, 6, 8];

    private bool HandleLegacyShortcut(int key, bool shift)
    {
        switch (key)
        {
            case 65 when !shift: SelectObjects(ClipboardParents(Document).Select(p => p.Id)); return true;
            case >= 49 and <= 52 when !shift: gridSize = 4 << (key - 49); return true;
            case 77 when !shift: ForgetTemporarySnap(); divisor = QuickSnapDivisors[(Array.IndexOf(QuickSnapDivisors, divisor) + 1) % QuickSnapDivisors.Length]; return true;
            case 68 when !shift: CloneSelection(); return true;
            case 72 when !shift: MirrorSelection(); return true;
            case 38: AdjustPlaybackSpeed(1, shift); return true;
            case 40: AdjustPlaybackSpeed(-1, shift); return true;
            default: return false;
        }
    }

    private bool HandleLegacyKey(int key, bool shift)
    {
        if (shift && key is >= 49 and <= 57) { ForgetTemporarySnap(); divisor = key - 48; return true; }
        if (!shift && key is >= 49 and <= 52)
        { ChangeTool(key switch { 49 => Tool.Select, 50 => Tool.Fruit, 51 => Tool.Slider, _ => Tool.Banana }); return true; }
        if (shift && key is not (37 or 39)) return false;
        switch (key)
        {
            case 67: TogglePlayback(); return true;
            case 88: SeekTo(0); if (!AudioPlaying) TogglePlayback(); return true;
            case 90:
                double first = ClipboardParents(Document).Select(p => p.TimeMs).DefaultIfEmpty(0).Min();
                SeekTo(playhead <= first ? 0 : first); return true;
            case 35: case 86:
                double last = LastObjectEndMs();
                SeekTo(playhead >= last ? AudioReady && AudioDurationMs > 0 ? AudioDurationMs : TimelineDurationMs : last); return true;
            case 37: case 39:
                int steps = (key == 37 ? -1 : 1) * (shift ? 4 : 1);
                SeekTo(AudioPlaying
                    ? playhead + steps * TimingMap.At(Document, playhead).BeatLengthMs
                    : StepAlongBeatGrid(playhead, steps, divisor)); return true;
            case 38: case 40:
                var times = Document.TimingPoints.Select(t => t.TimeMs).Distinct().Order().ToArray();
                SeekTo(key == 38 ? times.Where(t => t < playhead).LastOrDefault(0) : times.FirstOrDefault(t => t > playhead, TimelineDurationMs)); return true;
            case 74: case 75:
                var ids = ClipboardSelectedParentIds();
                double time = ClipboardParents(Document).Where(p => ids.Contains(p.Id)).Select(p => p.TimeMs).DefaultIfEmpty(playhead).Min();
                NudgeSelection((key == 74 ? -1 : 1) * TimingMap.At(Document, time).BeatLengthMs / divisor, 0); return true;
            default: return false;
        }
    }

    private double LastObjectEndMs()
    {
        var last = ClipboardParents(Document).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).LastOrDefault();
        if (last.Id == Guid.Empty) return 0;
        EnsureConversion();
        if (playableExport?.PlayableEndTimes.TryGetValue(last.Id, out double playableEnd) == true)
            return playableEnd;
        if (Document.Tracks.FirstOrDefault(t => t.Id == last.Id) is { Nodes.Count: > 0 } track)
            return CurveMath.EndTimeMs(track);
        if (Document.ImportedSliders.FirstOrDefault(s => s.Id == last.Id) is { } slider)
            return ImportedSliderConverter.EndTimeMs(Document, slider);
        if (Document.BananaShowers.FirstOrDefault(s => s.Id == last.Id) is { } shower)
            return shower.EndTimeMs;
        return last.TimeMs;
    }

    private void CloneSelection()
    {
        if (!TryCopySnapshot(out var snapshot)) return;
        double last = ClipboardParents(snapshot!).Max(p => p.TimeMs);
        var timing = TimingMap.At(Document, last);
        var savedClipboard = objectClipboard; var savedDifficulty = clipboardDifficulty;
        double savedHead = playhead;
        try
        {
            objectClipboard = snapshot; clipboardDifficulty = difficulties[activeDifficulty];
            playhead = last + timing.BeatLengthMs * timing.Meter;
            PasteSelection();
        }
        finally { objectClipboard = savedClipboard; clipboardDifficulty = savedDifficulty; playhead = savedHead; }
    }

    private void NudgeSelection(double time, double x)
    {
        var ids = ClipboardSelectedParentIds();
        if (ids.Count == 0 || notesLocked) return;
        Edit(L.Get("editor.command.moveObjects"), () =>
        {
            if (x != 0)
                foreach (var id in Document.ImportedSliders.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArray())
                    ConvertImportedSlider(id);
            foreach (var fruit in Document.Fruits.Where(f => ids.Contains(f.Id))) { fruit.TimeMs += time; fruit.X += x; }
            foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id)))
                foreach (var node in track.Nodes) { node.TimeMs += time; node.X += x; }
            foreach (var slider in Document.ImportedSliders.Where(s => ids.Contains(s.Id)))
            { slider.TimeMs += time; slider.OriginalLine = WithClipboardTimes(slider.OriginalLine, (2, slider.TimeMs)); }
            foreach (var shower in Document.BananaShowers.Where(s => ids.Contains(s.Id)))
            { shower.TimeMs += time; shower.EndTimeMs += time; }
            Document.DurationMs = Math.Max(Document.DurationMs, ClipboardParents(Document).Select(p => p.TimeMs)
                .Concat(Document.Tracks.Select(CurveMath.EndTimeMs)).Concat(Document.BananaShowers.Select(b => b.EndTimeMs)).DefaultIfEmpty(0).Max());
            OsuBeatmapReader.Validate(Document);
            var errors = CurveMath.Validate(Document);
            if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
        });
    }

    private void MirrorSelection()
    {
        var ids = ClipboardSelectedParentIds();
        if (ids.Count == 0 || notesLocked) return;
        Edit(L.Get("shortcut.mirror"), () =>
        {
            foreach (var id in Document.ImportedSliders.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArray())
                ConvertImportedSlider(id);
            foreach (var fruit in Document.Fruits.Where(f => ids.Contains(f.Id))) fruit.X = 512 - fruit.X;
            foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id)))
                foreach (var node in track.Nodes)
                {
                    node.X = 512 - node.X;
                    node.HandleIn = node.HandleIn with { X = -node.HandleIn.X };
                    node.HandleOut = node.HandleOut with { X = -node.HandleOut.X };
                    if (node.OutgoingCurve is { } curve)
                        foreach (var point in curve.Controls) point.Offset = point.Offset with { X = -point.Offset.X };
                }
        });
    }
}
