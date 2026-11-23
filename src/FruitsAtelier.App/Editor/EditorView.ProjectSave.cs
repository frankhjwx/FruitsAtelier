using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly HashSet<Guid> pendingExports = [];
    private bool DifficultyIsDirty(DifficultySession difficulty)
        => difficulty.History.IsDirty || pendingExports.Contains(difficulty.Id);

    private static bool MarkPendingExports(WorkspaceSession session, IReadOnlyCollection<Guid> ids)
    {
        bool changed = false;
        foreach (var entry in session.Manifest.Difficulties.Where(d => ids.Contains(d.Id) && !d.PendingExport
            && !d.FaOnly && WorkspaceSynchronization.Target(d) is not null))
        {
            entry.PendingExport = true;
            changed = true;
        }
        return changed;
    }

    private void RestorePendingExports(WorkspaceSession session)
    {
        pendingExports.Clear();
        pendingExports.UnionWith(session.Manifest.Difficulties.Where(d => d.PendingExport && !d.FaOnly
            && WorkspaceSynchronization.Target(d) is not null).Select(d => d.Id));
    }

    private Task<DifficultySaveResult>? difficultySaveTask;
    private sealed record DifficultySaveItem(Guid Id, MapDocument Baseline, string? PreviousSource);
    private sealed record DifficultySaveResult(WorkspaceSession Session, List<DifficultySaveItem> Saved, Guid FailedId, Exception? Error);
    private sealed class DifficultySaveReviewRequired : Exception;

    private bool BeginProjectSave()
    {
        if (WorkspaceSession is not { } original || projectStructureDirty || projectNotes != savedProjectNotes
            || ProjectName != original.Project.Name || SynchronizationBusy) return false;
        var current = difficulties[activeDifficulty];
        if (!original.Project.Difficulties.Any(d => d.Id == current.Id)) return false;
        var entry = original.Manifest.Difficulties.Single(d => d.Id == current.Id);
        if (!entry.FaOnly && WorkspaceSynchronization.Target(entry) is not null && !entry.ExportConfirmed) return false;
        if (!entry.FaOnly && WorkspaceSynchronization.Target(entry) is null && !string.IsNullOrWhiteSpace(LibrarySettings.Songs)
            && !ProjectInSongs && !original.Manifest.SongsExportOfferShown) return false;
        var dirtyIds = difficulties.Where(DifficultyIsDirty).Select(d => d.Id).ToHashSet();
        var snapshots = difficulties.Where(d => d.Id == current.Id || dirtyIds.Contains(d.Id))
            .Select(d => new ProjectDifficulty { Id = d.Id, Name = d.Name, FaOnly = d.FaOnly, Document = d.History.Document.DeepClone() }).ToArray();
        MarkPendingExports(original, dirtyIds);
        RestorePendingExports(original);
        string songs = LibrarySettings.Songs;
        bool compensate = compensateTinyDroplets;
        difficultySaveTask = Task.Factory.StartNew(() =>
        {
            if (OperatingSystem.IsWindows()) Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            var session = DetachedSession(original, original.Project);
            var saved = new List<DifficultySaveItem>();
            Guid processing = current.Id;
            try
            {
                var prepared = new List<(ProjectDifficulty Difficulty, WorkspaceExportPlan? Plan)>();
                // Validate every dirty linked file before publishing any of the project snapshots.
                foreach (var difficulty in snapshots)
                {
                    var id = processing = difficulty.Id;
                    var target = session.Manifest.Difficulties.Single(d => d.Id == id);
                    var exported = difficulty.Document;
                    WorkspaceExportPlan? plan = null;
                    if (!target.FaOnly && WorkspaceSynchronization.Target(target) is not null)
                    {
                        var status = WorkspaceSynchronization.Scan(session, songs, searchMissing: false, difficultyId: id).Difficulties.Single();
                        if (status.State is not (WorkspaceSyncState.Current or WorkspaceSyncState.Changed) || target.Sync is null)
                            throw new DifficultySaveReviewRequired();
                        // Save publishes all modified linked difficulties in this project.
                        bool confirmed = target.ExportConfirmed;
                        target.ExportConfirmed = true;
                        var merge = WorkspaceSynchronization.Merge(target, exported, status.Candidate!, session.Directory, compensate);
                        if (merge.RequiresResolution || !merge.CanExportLocalChanges && status.State != WorkspaceSyncState.Current)
                            throw new DifficultySaveReviewRequired();
                        if (merge.CanExportLocalChanges || target.Sync.LocalOverrides.Count > 0 || target.Sync.RetainedObjects.Count > 0)
                        {
                            plan = merge.CanExportLocalChanges
                                ? WorkspaceSynchronization.PlanLocalChanges(session, target, merge, new OsuWriteCache())
                                : WorkspaceSynchronization.PlanResolvedChanges(session, target, exported, status.Candidate!, compensate, new OsuWriteCache());
                            if (WorkspaceSynchronization.AudioHash(merge.External.Document.AudioPath) != target.Sync.AudioHash)
                                throw new DifficultySaveReviewRequired();
                        }
                        target.ExportConfirmed = confirmed;
                    }
                    if (dirtyIds.Contains(id) || plan is not null) prepared.Add((difficulty, plan));
                }
                foreach (var item in prepared)
                {
                    processing = item.Difficulty.Id;
                    string? previousSource = item.Difficulty.Document.SourcePath;
                    var project = new BeatmapProject
                    {
                        Name = session.Project.Name, Notes = session.Project.Notes,
                        Difficulties = session.Project.Difficulties.Select(d => d.Id == processing ? item.Difficulty : d).ToList()
                    };
                    var next = DetachedSession(session, project);
                    var target = next.Manifest.Difficulties.Single(d => d.Id == processing);
                    target.PendingExport = false;
                    if (item.Plan is { } plan)
                    {
                        WorkspaceVersionHistory.ArchiveCurrentDifficulty(next, project, processing);
                        BeatmapResources.Copy(plan.Document, Path.GetDirectoryName(plan.Target)!, plan.Output.ReadBack);
                        string receipt = WorkspaceExportRecovery.Prepare(next, project, plan, processing, difficultyOnly: true);
                        WorkspaceExport.Commit(next, plan);
                        item.Difficulty.Document.SourcePath = plan.Target;
                        session = WorkspaceProject.SaveDifficulty(next, processing, item.Difficulty.Document, item.Difficulty.Name, archiveBeforeSave: false);
                        WorkspaceExportRecovery.Complete(receipt);
                    }
                    else session = WorkspaceProject.SaveDifficulty(next, processing, item.Difficulty.Document, item.Difficulty.Name);
                    saved.Add(new(processing, item.Difficulty.Document.DeepClone(), previousSource));
                }
                return new DifficultySaveResult(session, saved, Guid.Empty, null);
            }
            catch (Exception error) { return new DifficultySaveResult(session, saved, processing, error); }
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
            if (result.Saved.Count > 0) WorkspaceSession = result.Session;
            foreach (var saved in result.Saved)
            {
                if (difficulties.FirstOrDefault(d => d.Id == saved.Id) is { } difficulty)
                {
                    if (saved.PreviousSource != saved.Baseline.SourcePath)
                        difficulty.History.RebaseSharedMetadata(d => d.SourcePath = saved.Baseline.SourcePath);
                    difficulty.History.AdoptSavedSnapshot(saved.Baseline);
                }
                pendingExports.Remove(saved.Id);
            }
            if (WorkspaceSession is { } session)
                foreach (var entry in session.Manifest.Difficulties)
                    entry.PendingExport = pendingExports.Contains(entry.Id);
            libraryProjectsNeedReindex = true;
            QueueLibrarySearch();
            if (result.Error is DifficultySaveReviewRequired)
            {
                syncDifficulty = result.FailedId;
                RefreshSynchronization(SaveProject, reviewResolved: true);
            }
            else if (result.Error is { } error) ShowError(L.Reformat(error.Message));
            else SetNotice(L.Get("files.saved", result.Session.Directory));
        }
        catch (Exception error) { ShowError(L.Reformat(error.Message)); }
    }
}
