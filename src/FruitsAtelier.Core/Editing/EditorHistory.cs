using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed class EditorHistory
{
    private sealed record Change(string Label, MapDocument Before, MapDocument After, Action<bool, MapDocument, MapDocument>? RestoreRelated);

    private readonly Stack<Change> undo = new();
    private readonly Stack<Change> redo = new();
    private readonly ImportedSliderLengthCache breakSliderLengths = new();
    private MapDocument baseline;
    private MapDocument? transactionStart;
    private long transactionVersion;
    private string transactionLabel = "";
    private Action<bool, MapDocument, MapDocument>? transactionRelated;

    public EditorHistory(MapDocument document)
    {
        Document = document.DeepClone();
        baseline = Document.DeepClone();
    }

    public MapDocument Document { get; private set; }
    public bool IsDirty => !Document.ContentEquals(baseline);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool HasActiveTransaction => transactionStart is not null;
    public long ActiveTransactionVersion => transactionStart is null ? 0 : transactionVersion;
    public MapDocument CaptureTransactionStart() => transactionStart?.DeepClone()
        ?? throw new InvalidOperationException(L.Get("core.history.activeEdit"));
    public bool TryCommitPreparedTransaction(long version, MapDocument expected, MapDocument prepared)
    {
        if (!HasActiveTransaction || version != transactionVersion || !Document.ContentEquals(expected)) return false;
        Document = prepared;
        Commit(reconcileBreaks: false);
        return true;
    }
    public static bool PrepareDetachedCommit(MapDocument before, MapDocument prepared)
    {
        var breaks = OsuTimeline.Breaks(prepared);
        OsuTimeline.ReconcileBreaks(before, prepared, new ImportedSliderLengthCache());
        return !breaks.SequenceEqual(OsuTimeline.Breaks(prepared));
    }
    public bool TransactionNeedsLegacyDropletRefresh => transactionStart is not null
        && SliderRepresentationEditing.NeedsLegacyDropletRefresh(transactionStart, Document);
    public bool TransactionChangesObjects(IEnumerable<Guid> ids)
    {
        if (transactionStart is null) return false;
        var protectedIds = ids.ToHashSet();
        if (protectedIds.Count == 0) return false;
        var unchanged = transactionStart.UnchangedObjectIds(Document).ToHashSet();
        return protectedIds.Any(id => !unchanged.Contains(id))
            || !transactionStart.HitsoundOverrides.Where(h => protectedIds.Contains(h.SourceId))
                .SequenceEqual(Document.HitsoundOverrides.Where(h => protectedIds.Contains(h.SourceId)));
    }
    public SliderRepresentationResult RefreshLegacyDroplets(bool compensateTinyDroplets = true)
        => transactionStart is not null && SliderRepresentationEditing.NeedsLegacyDropletRefresh(transactionStart, Document)
            ? SliderRepresentationEditing.RefreshLegacyDroplets(Document, compensateTinyDroplets, transactionStart)
            : new(true, []);
    public string UndoLabel => undo.TryPeek(out var change) ? change.Label : "";

    public void MarkSaved()
    {
        if (transactionStart is not null) throw new InvalidOperationException(L.Get("core.history.saveDuringEdit"));
        MarkSaved(Document);
    }

    // A background save acknowledges its frozen snapshot, including while a newer edit is active.
    public void MarkSaved(MapDocument savedDocument) => baseline = savedDocument.DeepClone();

    public void Begin(string label, Action<bool, MapDocument, MapDocument>? restoreRelated = null)
    {
        if (transactionStart is not null) throw new InvalidOperationException(L.Get("core.history.activeEdit"));
        transactionStart = Document.DeepClone();
        transactionVersion++;
        transactionLabel = label;
        transactionRelated = restoreRelated;
    }

    public void Commit() => Commit(reconcileBreaks: true);

    private void Commit(bool reconcileBreaks)
    {
        if (transactionStart is null) return;
        // Break changes belong to the same undo step as the notes that occupy them.
        if (reconcileBreaks) OsuTimeline.ReconcileBreaks(transactionStart, Document, breakSliderLengths);
        breakSliderLengths.Retain(Document);
        if (!Document.ContentEquals(transactionStart))
        {
            undo.Push(new Change(transactionLabel, transactionStart, Document.DeepClone(), transactionRelated));
            redo.Clear();
        }
        transactionStart = null;
        transactionLabel = "";
        transactionRelated = null;
    }

    public void Cancel()
    {
        if (transactionStart is null) return;
        Document = transactionStart;
        transactionStart = null;
        transactionLabel = "";
        transactionRelated = null;
    }

    public void RestoreVersion(string label, MapDocument document, Action<bool, MapDocument, MapDocument>? restoreRelated = null)
    {
        OsuBeatmapReader.Validate(document);
        Begin(label, restoreRelated);
        Document = document.DeepClone();
        // A complete historical snapshot already owns its authored break intervals.
        if (!Document.ContentEquals(transactionStart!))
        {
            undo.Push(new Change(label, transactionStart!, Document.DeepClone(), transactionRelated));
            redo.Clear();
        }
        transactionStart = null;
        transactionLabel = "";
        transactionRelated = null;
    }

    public void Undo()
    {
        if (transactionStart is not null) { Cancel(); return; }
        if (!undo.TryPop(out var change)) return;
        var previous = Document;
        Document = change.Before.DeepClone();
        try { change.RestoreRelated?.Invoke(false, previous, Document); }
        catch { Document = previous; undo.Push(change); throw; }
        redo.Push(change);
    }

    public void Redo()
    {
        if (transactionStart is not null) { Cancel(); return; }
        if (!redo.TryPop(out var change)) return;
        var previous = Document;
        Document = change.After.DeepClone();
        try { change.RestoreRelated?.Invoke(true, previous, Document); }
        catch { Document = previous; redo.Push(change); throw; }
        undo.Push(change);
    }

    public void Reset(MapDocument document)
    {
        Document = document.DeepClone();
        breakSliderLengths.Retain(Document);
        baseline = Document.DeepClone();
        undo.Clear();
        redo.Clear();
        transactionStart = null;
        transactionLabel = "";
        transactionRelated = null;
    }

    // Shared project metadata must survive unrelated difficulty-local undo steps.
    // Leave the saved baseline intact so these changes still mark the difficulty dirty.
    public void RebaseSharedMetadata(Action<MapDocument> update)
    {
        update(Document);
        foreach (var change in undo.Concat(redo)) { update(change.Before); update(change.After); }
        if (transactionStart is not null) update(transactionStart);
    }
}
