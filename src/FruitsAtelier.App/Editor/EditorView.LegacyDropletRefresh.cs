using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record LegacyDropletPrepared(MapDocument Document, OsuWriteCache Cache,
        CatchConversionResult? Conversion, OsuWriteResult? Export, IReadOnlyList<string> Diagnostics);
    private sealed record LegacyDropletJob(EditorHistory Owner, long Version, MapDocument Expected,
        bool Compensation, string Language, Task<LegacyDropletPrepared> Task);
    private LegacyDropletJob? legacyDropletJob;

    public bool LegacyDropletRefreshBusy => legacyDropletJob is not null;
    public bool LegacyDropletRefreshNeedsRedraw => legacyDropletJob?.Task.IsCompleted == true;

    private void CommitContentHistory()
    {
        PumpLegacyDropletRefresh();
        if (LegacyDropletRefreshBusy || !history.HasActiveTransaction) return;
        if (!history.TransactionNeedsLegacyDropletRefresh)
        {
            history.Commit();
            if (history.LastCommitBlockedBySectionLock) SetNotice(L.Get("collab.locked"));
            return;
        }
        if (history.RejectLockedSectionEdit()) { SetNotice(L.Get("collab.locked")); return; }
        var owner = history;
        long version = owner.ActiveTransactionVersion;
        var before = owner.CaptureTransactionStart();
        var expected = Document.DeepClone();
        var candidate = expected.DeepClone();
        bool compensation = compensateTinyDroplets;
        string language = L.Language;
        var task = Task.Run(() =>
        {
            var cache = new OsuWriteCache();
            try
            {
                var result = SliderRepresentationEditing.RefreshLegacyDroplets(candidate, compensation, before, cache);
                if (!result.Success) return new LegacyDropletPrepared(candidate, cache, null, null, result.Diagnostics);
                bool breaksChanged = EditorHistory.PrepareDetachedCommit(before, candidate);
                var conversion = cache.Convert(candidate, compensation);
                var exported = !breaksChanged && result.ValidatedOutput is { } output
                    ? output : OsuBeatmapWriter.Serialize(candidate, compensation, cache);
                if (!conversion.Success || !exported.ObjectSequenceMatches)
                    return new LegacyDropletPrepared(candidate, cache, null, null, exported.Diagnostics);
                return new LegacyDropletPrepared(candidate, cache, conversion, exported, []);
            }
            catch (Exception error)
            { return new LegacyDropletPrepared(candidate, cache, null, null, [error.Message]); }
        });
        legacyDropletJob = new(owner, version, expected, compensation, language, task);
        StatusMessage = L.Get("osu.updatingDroplets");
    }

    private void CancelLegacyDropletRefresh()
    {
        if (legacyDropletJob is not { } job) return;
        legacyDropletJob = null;
        RollbackLegacyDropletRefresh(job);
        if (StatusMessage == L.Get("osu.updatingDroplets")) StatusMessage = "";
    }

    private void RollbackLegacyDropletRefresh(LegacyDropletJob job)
    {
        if (job.Owner.ActiveTransactionVersion != job.Version) return;
        job.Owner.Cancel();
        if (!ReferenceEquals(job.Owner, history)) return;
        // Placement selects the new parent before validation; rollback can remove that parent.
        var ids = Document.Fruits.Select(f => f.Id).Concat(Document.Tracks.Select(t => t.Id))
            .Concat(Document.ImportedSliders.Select(s => s.Id)).Concat(Document.BananaShowers.Select(b => b.Id)).ToHashSet();
        if (objectSelection.Any(id => !ids.Contains(id)))
            SelectObjects(objectSelection.Where(ids.Contains), selection);
    }

    private void PumpLegacyDropletRefresh()
    {
        if (legacyDropletJob is not { } job) return;
        if (!ReferenceEquals(job.Owner, history) || job.Owner.ActiveTransactionVersion != job.Version)
        { CancelLegacyDropletRefresh(); return; }
        if (!job.Task.IsCompleted) return;
        legacyDropletJob = null;
        LegacyDropletPrepared result;
        try { result = job.Task.GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            RollbackLegacyDropletRefresh(job);
            StatusMessage = fieldError = L.Reformat(error.Message);
            return;
        }
        if (job.Compensation != compensateTinyDroplets || job.Language != L.Language
            || !job.Expected.ContentEquals(Document))
        {
            RollbackLegacyDropletRefresh(job);
            return;
        }
        if (result.Export is null || result.Conversion is null)
        {
            RollbackLegacyDropletRefresh(job);
            StatusMessage = fieldError = string.Join(L.Get("editor.diagnostics.separator"), result.Diagnostics);
            return;
        }
        if (!job.Owner.TryCommitPreparedTransaction(job.Version, job.Expected, result.Document)) return;
        editorWriteCache = result.Cache;
        convertedSnapshot = Document.DeepClone();
        convertedWithCompensation = job.Compensation;
        conversion = result.Conversion;
        playableExport = result.Export;
        playableObjects = result.Export.PlayableObjects;
        contentDragPreview = false;
        clickedCoordinate = null;
        renderedTiming = SnapTiming();
        RefreshConversionPresentation();
        if (StatusMessage == L.Get("osu.updatingDroplets")) StatusMessage = "";
    }
}
