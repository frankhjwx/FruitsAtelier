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
        if (!history.TransactionNeedsLegacyDropletRefresh) { history.Commit(); return; }
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
        if (job.Owner.ActiveTransactionVersion == job.Version) job.Owner.Cancel();
        if (StatusMessage == L.Get("osu.updatingDroplets")) StatusMessage = "";
    }

    private void PumpLegacyDropletRefresh()
    {
        if (legacyDropletJob is not { } job) return;
        if (!ReferenceEquals(job.Owner, history) || job.Owner.ActiveTransactionVersion != job.Version)
        { CancelLegacyDropletRefresh(); return; }
        if (!job.Task.IsCompleted) return;
        legacyDropletJob = null;
        var result = job.Task.GetAwaiter().GetResult();
        if (job.Compensation != compensateTinyDroplets || job.Language != L.Language
            || !job.Expected.ContentEquals(Document))
        {
            if (job.Owner.ActiveTransactionVersion == job.Version) job.Owner.Cancel();
            return;
        }
        if (result.Export is null || result.Conversion is null)
        {
            job.Owner.Cancel();
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
