using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed class EditorHistory
{
    private sealed record Change(string Label, MapDocument Before, MapDocument After, Action<bool, MapDocument, MapDocument>? RestoreRelated);

    private readonly Stack<Change> undo = new();
    private readonly Stack<Change> redo = new();
    private MapDocument baseline;
    private MapDocument? transactionStart;
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
    public string UndoLabel => undo.TryPeek(out var change) ? change.Label : "";

    public void MarkSaved()
    {
        if (transactionStart is not null) throw new InvalidOperationException(L.Get("core.history.saveDuringEdit"));
        baseline = Document.DeepClone();
    }

    public void Begin(string label, Action<bool, MapDocument, MapDocument>? restoreRelated = null)
    {
        if (transactionStart is not null) throw new InvalidOperationException(L.Get("core.history.activeEdit"));
        transactionStart = Document.DeepClone();
        transactionLabel = label;
        transactionRelated = restoreRelated;
    }

    public void Commit()
    {
        if (transactionStart is null) return;
        // Break changes belong to the same undo step as the notes that occupy them.
        OsuTimeline.ReconcileBreaks(transactionStart, Document);
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

    public void Undo()
    {
        if (transactionStart is not null) { Cancel(); return; }
        if (!undo.TryPop(out var change)) return;
        var previous = Document;
        Document = change.Before.DeepClone();
        change.RestoreRelated?.Invoke(false, previous, Document);
        redo.Push(change);
    }

    public void Redo()
    {
        if (transactionStart is not null) { Cancel(); return; }
        if (!redo.TryPop(out var change)) return;
        var previous = Document;
        Document = change.After.DeepClone();
        change.RestoreRelated?.Invoke(true, previous, Document);
        undo.Push(change);
    }

    public void Reset(MapDocument document)
    {
        Document = document.DeepClone();
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
