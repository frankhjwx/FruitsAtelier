using L = FruitsAtelier.Localization.Strings;
using System.Globalization;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapDocument? objectClipboard;

    private bool ClipboardInteractionReady => draftTrack == Guid.Empty && draftBanana == Guid.Empty && drag == DragKind.None && editField < 0;

    public bool CanCopySelection
    {
        get
        {
            if (!ClipboardInteractionReady) return false;
            var ids = ClipboardSelectedParentIds();
            return ids.Count > 0 && ids.IsSubsetOf(Document.Fruits.Select(f => f.Id)
                .Concat(Document.Tracks.Where(t => t.Nodes.Count >= 2).Select(t => t.Id))
                .Concat(Document.ImportedSliders.Select(s => s.Id)).Concat(Document.BananaShowers.Select(s => s.Id)));
        }
    }

    public bool CanPasteSelection => ClipboardInteractionReady && objectClipboard is not null;

    public bool CopySelection()
    {
        if (!TryCopySnapshot(out var snapshot)) return false;
        objectClipboard = snapshot;
        StatusMessage = L.Get("editor.status.objectsCopied", ClipboardParents(snapshot!).Count());
        return true;
    }

    public bool CutSelection()
    {
        if (!TryCopySnapshot(out var snapshot)) return false;
        var ids = ClipboardParents(snapshot!).Select(p => p.Id).ToHashSet();
        if (!Edit(L.Get("editor.command.cutObjects"), () =>
        {
            Document.Fruits.RemoveAll(f => ids.Contains(f.Id));
            Document.Tracks.RemoveAll(t => ids.Contains(t.Id));
            Document.ImportedSliders.RemoveAll(s => ids.Contains(s.Id));
            Document.BananaShowers.RemoveAll(s => ids.Contains(s.Id));
        })) return false;
        objectClipboard = snapshot;
        Select(Guid.Empty);
        StatusMessage = L.Get("editor.status.objectsCut", ids.Count);
        return true;
    }

    public bool PasteSelection()
    {
        if (!CanPasteSelection)
        {
            StatusMessage = ClipboardInteractionReady ? L.Get("editor.error.clipboardEmpty") : L.Get("editor.status.finishBeforePaste");
            return false;
        }
        Guid[] pastedIds = [];
        if (!Edit(L.Get("editor.command.pasteObjects"), () =>
        {
            if (!double.IsFinite(playhead) || playhead < 0 || playhead > int.MaxValue)
                throw new InvalidOperationException(L.Get("editor.error.pasteTimeRange"));
            var pasted = objectClipboard!.DeepClone();
            var parents = ClipboardParents(pasted).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
            if (parents.Length == 0) throw new InvalidOperationException(L.Get("editor.error.clipboardInvalid"));
            double ShiftTime(double time) => playhead + (time - parents[0].TimeMs);
            int firstOrder = NextClipboardSourceOrder(parents.Length);
            var replacements = parents.Select((parent, index) => (parent.Id, NewId: Guid.NewGuid(), Order: firstOrder + index))
                .ToDictionary(p => p.Id);
            pastedIds = parents.Select(p => replacements[p.Id].NewId).ToArray();
            double end = playhead;
            foreach (var fruit in pasted.Fruits)
            {
                var replacement = replacements[fruit.Id];
                fruit.Id = replacement.NewId; fruit.SourceOrder = replacement.Order; fruit.TimeMs = ShiftTime(fruit.TimeMs);
                IncludeEnd(fruit.TimeMs);
            }
            foreach (var track in pasted.Tracks)
            {
                var replacement = replacements[track.Id];
                track.Id = replacement.NewId; track.SourceOrder = replacement.Order;
                foreach (var node in track.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs = ShiftTime(node.TimeMs);
                    if (node.OutgoingCurve is { } curve) foreach (var point in curve.Controls) point.Id = Guid.NewGuid(); }
                IncludeEnd(CurveMath.EndTimeMs(track));
            }
            foreach (var slider in pasted.ImportedSliders)
            {
                var replacement = replacements[slider.Id];
                slider.Id = replacement.NewId; slider.SourceOrder = replacement.Order; slider.TimeMs = ShiftTime(slider.TimeMs);
                slider.OriginalLine = WithClipboardTimes(slider.OriginalLine, (2, slider.TimeMs));
                double sliderEnd;
                try { sliderEnd = ImportedSliderConverter.EndTimeMs(Document, slider); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { throw new InvalidOperationException(L.Get("editor.error.sliderPasteFailed", error.Message), error); }
                if (sliderEnd <= slider.TimeMs) throw new InvalidOperationException(L.Get("editor.error.zeroDurationSlider"));
                IncludeEnd(sliderEnd);
            }
            foreach (var shower in pasted.BananaShowers)
            {
                var replacement = replacements[shower.Id];
                shower.Id = replacement.NewId; shower.SourceOrder = replacement.Order;
                shower.TimeMs = ShiftTime(shower.TimeMs); shower.EndTimeMs = ShiftTime(shower.EndTimeMs);
                shower.OriginalLine = WithClipboardTimes(shower.OriginalLine, (2, shower.TimeMs), (5, shower.EndTimeMs));
                IncludeEnd(shower.EndTimeMs);
            }

            pasted.DurationMs = Math.Max(Document.DurationMs, end);
            OsuBeatmapReader.Validate(pasted);
            var errors = CurveMath.Validate(pasted);
            if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
            Document.Fruits.AddRange(pasted.Fruits);
            Document.Tracks.AddRange(pasted.Tracks);
            Document.ImportedSliders.AddRange(pasted.ImportedSliders);
            Document.BananaShowers.AddRange(pasted.BananaShowers);
            Document.DurationMs = pasted.DurationMs;

            void IncludeEnd(double objectEnd)
            {
                if (!double.IsFinite(objectEnd) || objectEnd < playhead || objectEnd > int.MaxValue)
                    throw new InvalidOperationException(L.Get("editor.error.pasteEndRange"));
                end = Math.Max(end, objectEnd);
            }
        })) return false;
        SelectObjects(pastedIds, pastedIds[0]);
        tool = Tool.Select;
        StatusMessage = L.Get("editor.status.objectsPasted", pastedIds.Length);
        return true;
    }

    private bool TryCopySnapshot(out MapDocument? snapshot)
    {
        snapshot = null;
        if (!CanCopySelection)
        {
            StatusMessage = ClipboardInteractionReady ? L.Get("editor.error.incompleteSelection") : L.Get("editor.status.finishBeforeCopy");
            return false;
        }
        var selected = new MapDocument
        {
            DurationMs = Document.DurationMs, BeatLengthMs = Document.BeatLengthMs, TimingOffsetMs = Document.TimingOffsetMs,
            ApproachRate = Document.ApproachRate, CircleSize = Document.CircleSize,
            SliderMultiplier = Document.SliderMultiplier, SliderTickRate = Document.SliderTickRate
        };
        var ids = ClipboardSelectedParentIds();
        selected.Fruits.AddRange(Document.Fruits.Where(f => ids.Contains(f.Id)));
        selected.Tracks.AddRange(Document.Tracks.Where(t => ids.Contains(t.Id)));
        selected.ImportedSliders.AddRange(Document.ImportedSliders.Where(s => ids.Contains(s.Id)));
        selected.BananaShowers.AddRange(Document.BananaShowers.Where(s => ids.Contains(s.Id)));
        try
        {
            OsuBeatmapReader.Validate(selected);
            snapshot = selected.DeepClone();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            StatusMessage = L.Get("editor.error.copyFailed", error.Message);
            return false;
        }
    }

    private HashSet<Guid> ClipboardSelectedParentIds()
    {
        if (objectSelection.Count > 0) return [.. objectSelection];
        Guid id = SelectedFruit?.Id ?? SelectedTrack?.Id ?? SelectedImportedSlider?.Id ?? SelectedBananaShower?.Id ?? Guid.Empty;
        return id == Guid.Empty ? [] : [id];
    }

    private int NextClipboardSourceOrder(int count)
    {
        // int.MaxValue denotes unordered authored objects, so reserve it instead of assigning tied paste orders.
        int largest = ClipboardParents(Document).Select(p => p.SourceOrder).Where(order => order < int.MaxValue)
            .DefaultIfEmpty(-1).Max();
        if ((long)largest + count >= int.MaxValue)
            throw new InvalidOperationException(L.Get("editor.error.pasteOrderCapacity"));
        return largest + 1;
    }

    private static IEnumerable<(Guid Id, double TimeMs, int SourceOrder)> ClipboardParents(MapDocument document)
        => document.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder))
            .Concat(document.Tracks.Select(t => (t.Id, t.Nodes.Count > 0 ? t.Nodes[0].TimeMs : 0, t.SourceOrder)))
            .Concat(document.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
            .Concat(document.BananaShowers.Select(s => (s.Id, s.TimeMs, s.SourceOrder)));

    private static string? WithClipboardTimes(string? originalLine, params (int Index, double Time)[] updates)
    {
        if (originalLine is null) return null;
        string[] values = originalLine.Split(',');
        foreach (var update in updates)
        {
            if (update.Index >= values.Length) throw new InvalidDataException(L.Get("editor.error.originalTimeMissing"));
            // Imported-object export checks the raw line against its model, including shifted times.
            values[update.Index] = update.Time.ToString("R", CultureInfo.InvariantCulture);
        }
        return string.Join(',', values);
    }
}
