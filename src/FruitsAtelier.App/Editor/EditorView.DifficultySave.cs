using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Task<DifficultySaveResult>? difficultySaveTask;
    private Guid savingDifficultyId;
    private sealed record DifficultySaveResult(WorkspaceSession Session, Guid Id, MapDocument Baseline, string? PreviousSource);
    private sealed class DifficultySaveReviewRequired : Exception;

    private bool BeginCurrentDifficultySave()
    {
        if (WorkspaceSession is not { } original || projectStructureDirty || projectNotes != savedProjectNotes
            || ProjectName != original.Project.Name || SynchronizationBusy) return false;
        var current = difficulties[activeDifficulty];
        if (!original.Project.Difficulties.Any(d => d.Id == current.Id)) return false;
        var entry = original.Manifest.Difficulties.Single(d => d.Id == current.Id);
        if (WorkspaceSynchronization.Target(entry) is not null && !entry.ExportConfirmed) return false;
        if (WorkspaceSynchronization.Target(entry) is null && !string.IsNullOrWhiteSpace(LibrarySettings.Songs)
            && !ProjectInSongs && !original.Manifest.SongsExportOfferShown) return false;
        Guid id = current.Id;
        savingDifficultyId = id;
        string name = current.Name, songs = LibrarySettings.Songs;
        var document = current.History.Document.DeepClone();
        bool compensate = compensateTinyDroplets;
        difficultySaveTask = Task.Factory.StartNew(() =>
        {
            if (OperatingSystem.IsWindows()) Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            var project = new BeatmapProject
            {
                Name = original.Project.Name, Notes = original.Project.Notes,
                Difficulties = original.Project.Difficulties.Select(d => d.Id == id
                    ? new ProjectDifficulty { Id = id, Name = name, Document = document } : d).ToList()
            };
            var session = DetachedSession(original, project);
            var target = session.Manifest.Difficulties.Single(d => d.Id == id);
            string? previousSource = document.SourcePath;
            if (WorkspaceSynchronization.Target(target) is not null)
            {
                var status = WorkspaceSynchronization.Scan(session, songs, searchMissing: false, difficultyId: id).Difficulties.Single();
                if (status.State is not (WorkspaceSyncState.Current or WorkspaceSyncState.Changed) || target.Sync is null)
                    throw new DifficultySaveReviewRequired();
                var merge = WorkspaceSynchronization.Merge(target, document, status.Candidate!, session.Directory, compensate);
                if (merge.RequiresResolution || !merge.CanExportLocalChanges && status.State != WorkspaceSyncState.Current)
                    throw new DifficultySaveReviewRequired();
                if (merge.CanExportLocalChanges || target.Sync.LocalOverrides.Count > 0 || target.Sync.RetainedObjects.Count > 0)
                {
                    var plan = merge.CanExportLocalChanges
                        ? WorkspaceSynchronization.PlanLocalChanges(session, target, merge, new OsuWriteCache())
                        : WorkspaceSynchronization.PlanResolvedChanges(session, target, document, status.Candidate!, compensate, new OsuWriteCache());
                    if (WorkspaceSynchronization.AudioHash(merge.External.Document.AudioPath) != target.Sync.AudioHash)
                        throw new DifficultySaveReviewRequired();
                    WorkspaceVersionHistory.ArchiveCurrentDifficulty(session, project, id);
                    BeatmapResources.Copy(plan.Document, Path.GetDirectoryName(plan.Target)!, plan.Output.ReadBack);
                    string receipt = WorkspaceExportRecovery.Prepare(session, project, plan, id, difficultyOnly: true);
                    WorkspaceExport.Commit(session, plan);
                    session = WorkspaceProject.SaveDifficulty(session, id, document, name, archiveBeforeSave: false);
                    WorkspaceExportRecovery.Complete(receipt);
                    return new DifficultySaveResult(session, id, document.DeepClone(), previousSource);
                }
            }
            session = WorkspaceProject.SaveDifficulty(session, id, document, name);
            return new DifficultySaveResult(session, id, document.DeepClone(), previousSource);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return true;
    }

    private void PumpDifficultySave()
    {
        if (difficultySaveTask is not { IsCompleted: true } task) return;
        difficultySaveTask = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            WorkspaceSession = result.Session;
            if (difficulties.FirstOrDefault(d => d.Id == result.Id) is { } current)
            {
                if (result.PreviousSource != result.Baseline.SourcePath)
                    current.History.RebaseSharedMetadata(d => d.SourcePath = result.Baseline.SourcePath);
                current.History.AdoptSavedSnapshot(result.Baseline);
            }
            libraryProjectsNeedReindex = true;
            QueueLibrarySearch();
            SetNotice(L.Get("files.saved", result.Session.Directory));
        }
        catch (DifficultySaveReviewRequired) { syncDifficulty = savingDifficultyId; RefreshSynchronization(reviewResolved: true); }
        catch (Exception error) { ShowError(L.Reformat(error.Message)); }
    }
}
