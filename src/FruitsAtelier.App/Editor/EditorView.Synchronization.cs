using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SyncResult(WorkspaceSession Session, BeatmapProject Snapshot, WorkspaceManifest Manifest, string Stamp, WorkspaceSyncScan Scan, Dictionary<Guid, WorkspaceMerge> Merges, IReadOnlyList<WorkspaceClaim> Claims, Dictionary<Guid, SyncComparison> Comparisons, Dictionary<Guid, WorkspaceExportPlan> LocalExports, Dictionary<Guid, string> LocalExportErrors, bool ReviewResolved, bool Quiet, Dictionary<Guid, OsuWriteCache> WriteCaches);
    private sealed record SyncRetry(SyncResult Previous, BeatmapProject Snapshot, DateTime After);
    private SyncRetry? syncRetry;
    private Task<SyncResult>? syncTask;
    private readonly HashSet<Guid> syncSearching = [];
    private bool SynchronizationBlocksInput => workspaceSaveTask is not null && workspaceSaveBlocksInput || syncCommitTask is not null && !syncCommitAllowsEditing;
    private bool syncCommitAllowsEditing;
    private bool syncCommitCompletesSave;
    private bool SearchingReference(int index) => syncTask is not null && index >= 0 && index < difficulties.Count && syncSearching.Contains(difficulties[index].Id);
    private Task<WorkspaceSession>? syncCommitTask;
    private DateTime nextSyncCheck = DateTime.MaxValue;
    private readonly Dictionary<Guid, WorkspaceSyncStatus> syncStatuses = [];
    private readonly Dictionary<Guid, WorkspaceMerge> syncMerges = [];
    private string? syncPage;
    private Guid syncDifficulty;
    private int syncRow;
    private readonly Dictionary<string, bool> syncChoices = [];
    private readonly HashSet<string> syncRoundChoices = [];
    private Action? afterSynchronization;
    private bool syncBypass;
    private bool syncReviewRequested;
    private bool syncPreserveHistory;
    private int unreadableSyncRetries;
    private sealed class SyncSourceChangedException : Exception;
    private readonly Dictionary<WorkspaceSyncConflict, (bool Choice, IReadOnlySet<Guid> Sources)> syncRetainedReview = [];
    private static string? SyncAudioHash(string? path) => path is not null && File.Exists(path) ? WorkspaceProject.Hash(path) : null;
    private IReadOnlyList<WorkspaceClaim> syncClaims = [];
    private string syncFailure = "";
    public bool SynchronizationVisible => syncPage is not null and not "checking";
    public bool SynchronizationBusy => syncRetry is not null || workspaceSaveTask is not null || syncTask is not null || syncCommitTask is not null;
    public bool SynchronizationNeedsRedraw => syncRetry is { } retry && DateTime.UtcNow >= retry.After || workspaceSaveTask is { IsCompleted: true } || VersionHistoryNeedsRedraw || syncTask is { IsCompleted: true } || syncCommitTask is { IsCompleted: true } || SynchronizationVisible && syncPreviewTask is { IsCompleted: true }
        || fileMonitor?.IsReady(DateTime.UtcNow) == true
        || WorkspaceSession is not null && !SynchronizationVisible && (fileSyncPending || !LibraryVisible && DateTime.UtcNow >= nextSyncCheck);
    public WorkspaceSyncState DifficultySyncState(int index) => index >= 0 && index < difficulties.Count
        && syncStatuses.TryGetValue(difficulties[index].Id, out var status) ? status.State : WorkspaceSyncState.Local;
    public Action<Action<string?>>? RequestSyncFile { get; set; }
    public Action<Action<string?>>? RequestSyncAudio { get; set; }

    public void RefreshSynchronization(Action? continuation = null, bool quiet = false, bool reviewResolved = false)
        => StartSynchronization(continuation, quiet, reviewResolved);

    private void StartSynchronization(Action? continuation, bool quiet, bool reviewResolved, WorkspaceSyncScan? previousScan = null, bool searchMissing = false,
        Dictionary<Guid, OsuWriteCache>? writeCaches = null)
    {
        if (WorkspaceSession is not { } session) return;
        if (SynchronizationBusy)
        {
            if (continuation is not null) afterSynchronization = continuation;
            syncReviewRequested |= reviewResolved;
            if (!quiet) syncPage = "checking";
            return;
        }
        syncPage = null;
        syncSearching.Clear();
        foreach (var entry in session.Manifest.Difficulties)
            if (WorkspaceSynchronization.Target(entry) is { } path && !File.Exists(path)) syncSearching.Add(entry.Id);
        var snapshot = CaptureProject();
        var manifest = WorkspaceProject.SnapshotManifest(session.Manifest);
        string stamp = ManifestStamp(manifest);
        var frozen = session with { Manifest = manifest, Project = snapshot };
        writeCaches ??= [];
        string songs = LibrarySettings.Songs; bool compensate = compensateTinyDroplets;
        afterSynchronization = continuation;
        syncTask = Task.Run(() =>
        {
            var scan = previousScan ?? WorkspaceSynchronization.Scan(frozen, songs, searchMissing: searchMissing || !quiet);
            var merges = new Dictionary<Guid, WorkspaceMerge>();
            var localExports = new Dictionary<Guid, WorkspaceExportPlan>();
            var localExportErrors = new Dictionary<Guid, string>();
            foreach (var status in scan.Difficulties.Where(s => s.State is WorkspaceSyncState.Changed or WorkspaceSyncState.NeedsBaseline
                || s.State == WorkspaceSyncState.Current && (reviewResolved || WorkspaceSynchronization.HasLocalChanges(
                    frozen.Manifest.Difficulties.Single(d => d.Id == s.DifficultyId), snapshot.Difficulties.Single(d => d.Id == s.DifficultyId).Document, session.Directory)
                    || WorkspaceSynchronization.HasFieldDifferences(
                    snapshot.Difficulties.Single(d => d.Id == s.DifficultyId).Document, s.Candidate!.Document))))
            {
                var entry = frozen.Manifest.Difficulties.Single(d => d.Id == status.DifficultyId);
                var diff = snapshot.Difficulties.Single(d => d.Id == status.DifficultyId);
                merges[entry.Id] = entry.Sync is null
                    ? WorkspaceSynchronization.CompareWithoutBaseline(diff.Document, status.Candidate!, session.Directory, compensate)
                    : WorkspaceSynchronization.Merge(entry, diff.Document, status.Candidate!, session.Directory, compensate);
                if (merges[entry.Id].CanExportLocalChanges)
                {
                    try
                    {
                        if (!writeCaches.TryGetValue(entry.Id, out var cache)) writeCaches[entry.Id] = cache = new();
                        localExports[entry.Id] = WorkspaceSynchronization.PlanLocalChanges(frozen, entry, merges[entry.Id], cache);
                    }
                    catch (Exception e) { localExportErrors[entry.Id] = e.Message; }
                }
            }
            scan = scan with { Difficulties = scan.Difficulties.Select(s => s.State == WorkspaceSyncState.Current
                && merges.TryGetValue(s.DifficultyId, out var merge) && (merge.RequiresResolution || localExports.ContainsKey(s.DifficultyId))
                    ? s with { State = WorkspaceSyncState.Changed } : s).ToArray() };
            var comparisons = merges.Where(m => m.Value.Conflicts.Count > 0).ToDictionary(m => m.Key, m => PrepareSyncComparison(m.Value, compensate,
                Path.Combine(session.Directory, manifest.Difficulties.Single(d => d.Id == m.Key).File)));
            return new SyncResult(session, snapshot, manifest, stamp, scan, merges, WorkspaceAssociations.Claims(Path.GetDirectoryName(session.Directory)!), comparisons, localExports, localExportErrors, reviewResolved, quiet, writeCaches);
        });
        nextSyncCheck = DateTime.UtcNow.AddSeconds(30);
    }

    private void PumpSynchronization()
    {
        if (workspaceSaveTask is not null) return;
        if (syncRetry is { } retry && DateTime.UtcNow >= retry.After && !SyncInteractionActive)
        {
            if (!ResourceSnapshotMatches(retry.Snapshot))
                syncRetry = retry with { Snapshot = CaptureProject(), After = DateTime.UtcNow.AddMilliseconds(500) };
            else
            {
                syncRetry = null;
                bool sameSession = ReferenceEquals(WorkspaceSession, retry.Previous.Session)
                    && retry.Previous.Stamp == ManifestStamp(retry.Previous.Session.Manifest);
                StartSynchronization(afterSynchronization, true, retry.Previous.ReviewResolved || syncReviewRequested,
                    sameSession ? retry.Previous.Scan : null, writeCaches: sameSession ? retry.Previous.WriteCaches : null);
            }
            return;
        }
        if (syncCommitTask is { IsCompleted: true } committed)
        {
            bool localCommit = syncCommitAllowsEditing;
            bool completesSave = syncCommitCompletesSave;
            syncCommitAllowsEditing = false;
            syncCommitCompletesSave = false;
            syncCommitTask = null;
            try
            {
                var session = committed.GetAwaiter().GetResult();
                var continuation = afterSynchronization; afterSynchronization = null;
                // The requested difficulty has already been validated and published by the worker.
                if (completesSave && continuation == (Action)SaveCurrentDifficulty) continuation = null;
                syncPage = null;
                libraryProjectsNeedReindex = true; StartLibraryScan();
                if (session.Project.Difficulties.Count == 0) { LeaveEditor(); return; }
                Guid previousDifficulty = difficulties[activeDifficulty].Id;
                string? previousAudio = Document.AudioPath;
                string? previousAudioHash = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == previousDifficulty)?.Sync?.AudioHash;
                double previousPlayhead = playhead, previousViewStart = viewStart;
                bool previousPin = pinPlayhead;
                Guid active = syncDifficulty == Guid.Empty ? difficulties[activeDifficulty].Id : syncDifficulty;
                bool preserveView = syncPreserveHistory && WorkspaceSession?.Directory == session.Directory;
                if (preserveView)
                {
                    foreach (var diff in session.Project.Difficulties)
                    {
                        var existing = difficulties.FirstOrDefault(d => d.Id == diff.Id);
                        if (existing is null) difficulties.Add(new DifficultySession(diff));
                        else
                        {
                            if (localCommit)
                            {
                                if (existing.History.Document.SourcePath != diff.Document.SourcePath)
                                    existing.History.RebaseSharedMetadata(document => document.SourcePath = diff.Document.SourcePath);
                                existing.History.MarkSaved(diff.Document);
                            }
                            else
                            {
                                var rebase = WorkspaceSynchronization.PrepareContextRebase(existing.History.Document, diff.Document);
                                existing.History.RebaseSharedMetadata(rebase);
                                existing.History.MarkSaved();
                            }
                        }
                    }
                    WorkspaceSession = session; projectStructureDirty = false;
                    if (!localCommit) convertedSnapshot = null;
                    resourceSnapshot = null; resourceReferences = null;
                    libraryProjectsNeedReindex = true; QueueLibrarySearch(); CheckWorkspaceResources();
                }
                else
                {
                    LoadWorkspace(session);
                    playhead = previousPlayhead; viewStart = previousViewStart; pinPlayhead = previousPin;
                }
                syncPreserveHistory = false;
                syncBypass = true;
                int targetDifficulty = Math.Max(0, difficulties.FindIndex(d => d.Id == active));
                bool switched = targetDifficulty != activeDifficulty;
                try { SwitchDifficulty(targetDifficulty); }
                finally { syncBypass = false; }
                string? audioHash = session.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulties[activeDifficulty].Id)?.Sync?.AudioHash;
                // Content-only synchronization keeps the live clock and viewport; changed audio needs a position-preserving reload.
                if (!switched && (!preserveView || previousDifficulty != difficulties[activeDifficulty].Id
                    || !string.Equals(previousAudio, Document.AudioPath, StringComparison.OrdinalIgnoreCase) || previousAudioHash != audioHash))
                {
                    ReleaseWaveform();
                    initializeTransport = !string.IsNullOrWhiteSpace(Document.AudioPath);
                    RequestDifficultyChanged?.Invoke();
                }
                RefreshSynchronization(continuation);
            }
            catch (SyncSourceChangedException)
            {
                syncPreserveHistory = false;
                syncRetainedReview.Clear();
                if (syncMerges.TryGetValue(syncDifficulty, out var previous))
                    foreach (var conflict in previous.Conflicts.Where(c => c.Key != "$audio" && syncRoundChoices.Contains(c.Key)))
                        syncRetainedReview[conflict] = (syncChoices[conflict.Key], previous.ConflictSources(conflict.Key, false));
                RefreshSynchronization(reviewResolved: true);
            }
            catch (Exception e) { syncPreserveHistory = false; syncPage = "failed"; syncFailure = e.Message; syncTextScroll = 0; }
        }
        if (syncTask is { IsCompleted: true } completed && !SyncInteractionActive && !SynchronizationVisible)
        {
            syncTask = null;
            try
            {
                var result = completed.GetAwaiter().GetResult();
                bool reviewResolved = result.ReviewResolved || syncReviewRequested;
                if (!ReferenceEquals(WorkspaceSession, result.Session))
                {
                    syncPage = null;
                    if (WorkspaceSession is not null) RefreshSynchronization(afterSynchronization);
                    return;
                }
                if (result.Stamp != ManifestStamp(result.Session.Manifest))
                { syncPage = null; RefreshSynchronization(afterSynchronization); return; }
                if (!ResourceSnapshotMatches(result.Snapshot) || reviewResolved != result.ReviewResolved)
                { syncRetry = new(result, CaptureProject(), DateTime.UtcNow.AddMilliseconds(500)); return; }
                if (result.Quiet && result.Scan.Difficulties.Any(s => s.State == WorkspaceSyncState.Unavailable)
                    && unreadableSyncRetries++ < 2)
                { nextSyncCheck = DateTime.UtcNow.AddSeconds(2); return; }
                unreadableSyncRetries = 0;
                syncSearching.Clear(); syncReviewRequested = false;
                nextSyncCheck = DateTime.UtcNow.AddSeconds(30);
                syncStatuses.Clear(); syncMerges.Clear();
                syncComparisons.Clear();
                foreach (var comparison in result.Comparisons) syncComparisons[comparison.Key] = comparison.Value;
                syncClaims = result.Claims;
                foreach (var status in result.Scan.Difficulties) syncStatuses[status.DifficultyId] = status;
                foreach (var merge in result.Merges) syncMerges[merge.Key] = merge.Value;
                var automatic = result.Scan.Difficulties.Where(s => s.State == WorkspaceSyncState.Changed && !result.Merges[s.DifficultyId].RequiresResolution
                    && (!result.Merges[s.DifficultyId].CanExportLocalChanges || result.LocalExports.ContainsKey(s.DifficultyId))
                    && !(reviewResolved && result.Merges[s.DifficultyId].PreviouslyResolved.Count > 0)
                    || s.State == WorkspaceSyncState.NeedsBaseline && s.Candidate!.Hash == (result.Session.Manifest.Difficulties.Single(d => d.Id == s.DifficultyId).ExportHash
                        ?? result.Session.Manifest.Difficulties.Single(d => d.Id == s.DifficultyId).SourceHash)).ToArray();
                bool canAdd = !result.Scan.Difficulties.Any(s => s.State is WorkspaceSyncState.Ambiguous or WorkspaceSyncState.Duplicate or WorkspaceSyncState.Unavailable);
                if (automatic.Length > 0 || canAdd && result.Scan.Additions.Count > 0)
                {
                    syncCommitAllowsEditing = automatic.Length > 0 && automatic.All(s => result.LocalExports.ContainsKey(s.DifficultyId))
                        && !(canAdd && result.Scan.Additions.Count > 0);
                    syncCommitCompletesSave = syncCommitAllowsEditing && result.LocalExports.ContainsKey(syncDifficulty);
                    if (!syncCommitAllowsEditing)
                    {
                        if (AudioPlaying) RequestPausePlayback?.Invoke();
                        CancelInteraction(); hits.Clear(); fields.Clear();
                    }
                    syncPage = "checking";
                    syncPreserveHistory = true;
                    syncCommitTask = Task.Run(() =>
                    {
                        var project = result.Snapshot;
                        var session = result.Session with { Manifest = result.Manifest, Project = project };
                        var receipts = new List<string>();
                        WorkspaceHistoryFile.WriteProject(project, Path.Combine(WorkspaceSynchronization.Archive(session, "before-sync"), "current.catchproj"));
                        foreach (var status in automatic)
                        {
                            var entry = session.Manifest.Difficulties.Single(d => d.Id == status.DifficultyId);
                            var diff = project.Difficulties.Single(d => d.Id == entry.Id);
                            if (result.Merges.TryGetValue(entry.Id, out var merge) && merge.CanExportLocalChanges)
                            {
                                var plan = result.LocalExports[entry.Id];
                                if (WorkspaceProject.Hash(plan.ExistingTarget) != plan.ExpectedHash) throw new SyncSourceChangedException();
                                if (WorkspaceSynchronization.AudioHash(merge.External.Document.AudioPath) != entry.Sync!.AudioHash) throw new SyncSourceChangedException();
                                BeatmapResources.Copy(plan.Document, Path.GetDirectoryName(plan.Target)!, plan.Output.ReadBack);
                                receipts.Add(WorkspaceExportRecovery.Prepare(session, project, plan, entry.Id));
                                try { WorkspaceExport.Commit(session, plan); }
                                catch (IOException) when (!File.Exists(plan.ExistingTarget) || WorkspaceProject.Hash(plan.ExistingTarget) != plan.ExpectedHash)
                                { throw new SyncSourceChangedException(); }
                            }
                            else
                            {
                                if (status.State == WorkspaceSyncState.Changed && merge is not null)
                                    diff.Document = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>());
                                WorkspaceSynchronization.Accept(session, entry, status.Candidate!, diff.Document, compensateTinyDroplets);
                            }
                            diff.Name = OsuBeatmapReader.Setting(diff.Document, "Metadata", "Version") ?? diff.Name;
                        }
                        if (canAdd)
                            foreach (var addition in result.Scan.Additions)
                            {
                                WorkspaceAssociations.EnsureImport(session, LibrarySettings.Workspace, addition.Path);
                                project.Difficulties.AddRange(BeatmapProject.FromDocuments([addition.Document]).Difficulties);
                            }
                        WorkspaceProject.Save(session, project, archiveBeforeSave: false);
                        foreach (string receipt in receipts.Distinct()) WorkspaceExportRecovery.Complete(receipt);
                        return WorkspaceProject.Open(session.Directory);
                    });
                    return;
                }
                syncPage = null;
                StatusMessage = result.LocalExportErrors.GetValueOrDefault(difficulties[activeDifficulty].Id) ?? L.Get("sync.complete");
                int target = syncDifficulty == Guid.Empty ? activeDifficulty : difficulties.FindIndex(d => d.Id == syncDifficulty);
                if (reviewResolved && target >= 0 && syncMerges.TryGetValue(difficulties[target].Id, out var review) && review.Conflicts.Count > 0)
                { BeginSyncReview(target); return; }
                if (target >= 0 && (!LibraryVisible || reviewResolved || afterSynchronization is not null)
                    && ShowSyncProblem(target, reviewResolved || afterSynchronization is not null)) return;
                var continuation = afterSynchronization; afterSynchronization = null;
                syncBypass = true;
                try { continuation?.Invoke(); }
                finally { syncBypass = false; }
            }
            catch (Exception e) { syncPreserveHistory = false; syncPage = "failed"; syncFailure = e.Message; syncTextScroll = 0; }
        }
        if (WorkspaceSession is not null && !SynchronizationBusy && !SynchronizationVisible && !SyncInteractionActive
            && (fileSyncPending || !LibraryVisible && DateTime.UtcNow >= nextSyncCheck))
        {
            bool searchMissing = fileSearchMissing;
            fileSyncPending = fileSearchMissing = false;
            StartSynchronization(null, true, false, searchMissing: searchMissing);
        }
    }

    private bool ShowSyncProblem(int index, bool includeMissing = false)
    {
        if (SearchingReference(index)) return false;
        var state = DifficultySyncState(index);
        if (!includeMissing && state == WorkspaceSyncState.Missing) return false;
        if (state is WorkspaceSyncState.Local or WorkspaceSyncState.Current) return false;
        BeginSyncReview(index);
        return true;
    }

    private void BeginSyncReview(int index)
    {
        syncDifficulty = difficulties[index].Id; syncTab = null; syncVisualKey = null; syncRow = 0; syncChoices.Clear(); syncRoundChoices.Clear();
        if (syncMerges.TryGetValue(syncDifficulty, out var merge))
        {
            foreach (string key in merge.PreviouslyResolved) syncChoices[key] = false;
            foreach (var conflict in merge.Conflicts)
                if (syncRetainedReview.TryGetValue(conflict, out var retained)
                    && retained.Sources.SetEquals(merge.ConflictSources(conflict.Key, false)))
                { syncChoices[conflict.Key] = retained.Choice; syncRoundChoices.Add(conflict.Key); }
        }
        syncRetainedReview.Clear();
        syncPage = "resolve";
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        CancelInteraction(); hits.Clear(); fields.Clear();
    }

    private void CancelSynchronization()
    {
        if (syncPage == "checking") return;
        bool activeBlocked = syncPage == "failed" || syncDifficulty == difficulties[activeDifficulty].Id && DifficultySyncState(activeDifficulty) is not (WorkspaceSyncState.Current or WorkspaceSyncState.Local);
        syncPage = null; afterSynchronization = null; syncDifficulty = Guid.Empty;
        if (activeBlocked) { LibraryVisible = true; QueueLibrarySearch(); }
    }

    public void ShowDeleteDifficulty(int index, bool localOnly = false)
    {
        if (index < 0 || index >= difficulties.Count || SynchronizationBusy) return;
        if (WorkspaceSession is null)
        { BeginWorkspaceSave(saved => { if (saved) ShowDeleteDifficulty(index, localOnly); }); return; }
        syncDifficulty = difficulties[index].Id;
        if (DifficultySyncState(index) is WorkspaceSyncState.Duplicate or WorkspaceSyncState.Ambiguous) { ShowSyncProblem(index); return; }
        syncPage = localOnly ? "deleteLocal" : "delete"; menu = -1; contextItems.Clear(); hits.Clear(); fields.Clear();
    }

    private void DeleteSyncDifficulty()
    {
        if (WorkspaceSession is not { } session) return;
        var project = CaptureProject(); Guid id = syncDifficulty;
        bool localOnly = syncPage == "deleteLocal";
        session = DetachedSession(session, project);
        syncPage = "checking";
        syncCommitTask = Task.Run(() =>
        {
            if (localOnly) return WorkspaceAssociations.ReimportDifficulty(session, project, id, compensateTinyDroplets);
            WorkspaceAssociations.DeleteDifficulty(session, project, id);
            return project.Difficulties.Count == 0 ? session with { Project = project } : WorkspaceProject.Open(session.Directory);
        });
    }

    private void ResolveSync(bool? useExternal)
    {
        if (WorkspaceSession is not { } session || !syncStatuses.TryGetValue(syncDifficulty, out var status) || status.Candidate is not { } external) return;
        var project = CaptureProject(); var diff = project.Difficulties.Single(d => d.Id == syncDifficulty);
        session = DetachedSession(session, project);
        var entry = session.Manifest.Difficulties.Single(d => d.Id == diff.Id);
        var choices = new Dictionary<string, bool>(syncChoices);
        syncComparisons.TryGetValue(diff.Id, out var compared);
        syncMerges.TryGetValue(diff.Id, out var fieldReview);
        bool fieldsOnly = fieldReview is not null && fieldReview.Conflicts.Count > 0
            && fieldReview.Conflicts.All(c => !c.Key.StartsWith('$'));
        syncPreserveHistory = fieldsOnly;
        syncPage = "checking";
        syncCommitTask = Task.Run(() =>
        {
            try
            {
                if (WorkspaceProject.Hash(external.Path) != external.Hash
                    || compared is not null && SyncAudioHash(external.Document.AudioPath) != compared.ExternalAudioHash)
                    throw new SyncSourceChangedException();
            }
            catch (IOException) { throw new SyncSourceChangedException(); }
            WorkspaceHistoryFile.WriteProject(project, Path.Combine(WorkspaceSynchronization.Archive(session, "resolution"), "current.catchproj"));
            if (fieldsOnly) diff.Document = WorkspaceSynchronization.Resolve(fieldReview!,
                fieldReview!.Conflicts.ToDictionary(c => c.Key, c => useExternal ?? choices.GetValueOrDefault(c.Key)));
            else if (useExternal is true) diff.Document = fieldReview is not null
                ? WorkspaceSynchronization.ResolveExternal(fieldReview) : external.Document.DeepClone();
            else if (useExternal is false) diff.Document.AudioPath = WorkspaceSynchronization.LocalAudioVersion(entry, diff.Document, session.Directory);
            else if (useExternal is null && syncMerges.TryGetValue(diff.Id, out var merge)) diff.Document = WorkspaceSynchronization.Resolve(merge, choices);
            diff.Document.SourcePath = external.Path;
            syncMerges.TryGetValue(diff.Id, out var review);
            var decisions = review?.Conflicts.ToDictionary(c => c.Key, c => useExternal ?? choices.GetValueOrDefault(c.Key));
            WorkspaceSynchronization.Accept(session, entry, external, diff.Document, compensateTinyDroplets, retainLocalFields: useExternal is false, review: review, choices: decisions);
            diff.Name = OsuBeatmapReader.Setting(diff.Document, "Metadata", "Version") ?? diff.Name;
            WorkspaceProject.Save(session, project);
            return WorkspaceProject.Open(session.Directory);
        });
    }

    private void RelinkSync(string path)
    {
        if (WorkspaceSession is not { } session) return;
        try
        {
            WorkspaceAssociations.EnsureOwner(session, syncDifficulty, path);
            var candidate = WorkspaceSynchronization.ReadStable(path);
            var entry = session.Manifest.Difficulties.Single(d => d.Id == syncDifficulty);
            var local = difficulties.Single(d => d.Id == syncDifficulty).History.Document;
            syncStatuses[syncDifficulty] = new(syncDifficulty, WorkspaceSyncState.NeedsBaseline, candidate, []);
            if (entry.Sync is not null)
            {
                syncMerges[syncDifficulty] = WorkspaceSynchronization.Merge(entry, local, candidate, session.Directory, compensateTinyDroplets);
                syncStatuses[syncDifficulty] = syncStatuses[syncDifficulty] with { State = WorkspaceSyncState.Changed };
            }
            else syncMerges[syncDifficulty] = WorkspaceSynchronization.CompareWithoutBaseline(local, candidate, session.Directory, compensateTinyDroplets);
            syncComparisons[syncDifficulty] = PrepareSyncComparison(syncMerges[syncDifficulty], compensateTinyDroplets, Path.Combine(session.Directory, entry.File));
            BeginSyncReview(difficulties.FindIndex(d => d.Id == syncDifficulty));
        }
        catch (Exception e) { ShowError(e.Message); }
    }

    private void RestoreSync()
    {
        if (WorkspaceSession is not { } session) return;
        var project = CaptureProject(); var diff = project.Difficulties.Single(d => d.Id == syncDifficulty);
        session = DetachedSession(session, project);
        var entry = session.Manifest.Difficulties.Single(d => d.Id == diff.Id);
        string? target = WorkspaceSynchronization.Target(entry);
        if (target is null) return;
        syncPage = "checking";
        syncCommitTask = Task.Run(() =>
        {
            WorkspaceAssociations.EnsureOwner(session, diff.Id, target);
            WorkspaceHistoryFile.WriteProject(project, Path.Combine(WorkspaceSynchronization.Archive(session, "restore"), "current.catchproj"));
            string folder = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(folder))
            {
                if (!Directory.Exists(LibrarySettings.Songs) || !WorkspaceProject.Within(LibrarySettings.Songs, target)) throw new IOException(L.Get("sync.unavailable"));
                Directory.CreateDirectory(folder);
            }
            diff.Document.AudioPath = WorkspaceSynchronization.LocalAudioVersion(entry, diff.Document, session.Directory);
            var output = OsuBeatmapWriter.Serialize(diff.Document, compensateTinyDroplets);
            FruitsAtelier.App.Platform.BeatmapResources.Copy(diff.Document, folder, output.ReadBack);
            using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file)) writer.Write(output.Text);
            var external = WorkspaceSynchronization.ReadStable(target);
            WorkspaceSynchronization.Accept(session, entry, external, diff.Document, compensateTinyDroplets);
            WorkspaceProject.Save(session, project);
            return WorkspaceProject.Open(session.Directory);
        });
    }

    private void DrawSynchronization(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        if (syncPage == "resolve" && syncMerges.TryGetValue(syncDifficulty, out var visualMerge) && visualMerge.Conflicts.Count > 0
            && syncComparisons.TryGetValue(syncDifficulty, out var comparison))
        { DrawSyncComparison(c, visualMerge, comparison); return; }
        float w = Math.Min(820, width - 32), h = Math.Min(500, height - 48), x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .8f);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("sync.title"), x + 20, y + 18, 20, Foreground, w - 40, true);
        if (syncPage == "failed")
        {
            syncCanvasBounds = new(x + 20, y + 80, w - 40, Math.Max(23, h - 154));
            var lines = WrapSyncText(c, syncFailure.Replace("\r", ""), syncCanvasBounds.Width);
            syncTextMaxScroll = Math.Max(0, (int)(lines.Length * 23 - syncCanvasBounds.Height));
            syncTextScroll = Math.Clamp(syncTextScroll, 0, syncTextMaxScroll);
            c.Clip(syncCanvasBounds);
            for (int line = 0; line < lines.Length; line++)
            {
                float textY = syncCanvasBounds.Y + line * 23 - syncTextScroll;
                if (textY + 23 < syncCanvasBounds.Y || textY > syncCanvasBounds.Bottom) continue;
                c.Text(lines[line].Text, syncCanvasBounds.X, textY, 15, Error, syncCanvasBounds.Width);
            }
            c.Unclip();
            Button(c, new(x + 20, y + h - 58, 220, 36), L.Get("sync.refresh"), () => RefreshSynchronization());
            Button(c, new(x + w - 150, y + h - 58, 130, 36), L.Get("mac.cancel"), CancelSynchronization);
            return;
        }
        var difficulty = difficulties.FirstOrDefault(d => d.Id == syncDifficulty);
        c.Text(difficulty?.Name ?? "", x + 20, y + 52, 16, Accent, w - 40);
        if (syncPage is "delete" or "deleteLocal")
        {
            bool localOnly = syncPage == "deleteLocal";
            c.Text(L.Get(localOnly ? "sync.deleteLocalHelp" : "sync.deleteHelp"), x + 20, y + 92, 14, Foreground, w - 40);
            string? path = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == syncDifficulty) is { } entry ? WorkspaceSynchronization.Target(entry) : null;
            c.Text(path ?? L.Get("sync.local"), x + 20, y + 130, 12, Muted, w - 40);
            Button(c, new(x + 20, y + h - 58, 220, 36), L.Get(localOnly ? "sync.deleteLocal" : "sync.delete"), DeleteSyncDifficulty);
        }
        else if (syncStatuses.TryGetValue(syncDifficulty, out var status))
        {
            string stateKey = "sync.state." + status.State;
            c.Text(L.Get(stateKey), x + 20, y + 82, 14, Foreground, w - 40);
            if (status.State is WorkspaceSyncState.Missing or WorkspaceSyncState.Unavailable or WorkspaceSyncState.Ambiguous)
            {
                var candidates = status.Candidates;
                if (candidates.Count > 0)
                {
                    syncRow = Math.Clamp(syncRow, 0, candidates.Count - 1);
                    c.Text(candidates[syncRow], x + 20, y + 120, 12, Muted, w - 40);
                    Button(c, new(x + 20, y + 164, 180, 34), L.Get("sync.link"), () => RelinkSync(candidates[syncRow]));
                    Navigation(c, x, y + 210, candidates.Count);
                }
                Button(c, new(x + 20, y + h - 154, 220, 36), L.Get("sync.chooseFile"), () => RequestSyncFile?.Invoke(path => { if (path is not null) RelinkSync(path); }));
                Button(c, new(x + 252, y + h - 154, 200, 36), L.Get("sync.refresh"), () => RefreshSynchronization());
                Button(c, new(x + 20, y + h - 106, 220, 36), L.Get("sync.restore"), RestoreSync, enabled: status.State == WorkspaceSyncState.Missing);
                Button(c, new(x + 20, y + h - 58, 220, 36), L.Get("sync.delete"), () => syncPage = "delete", enabled: status.State == WorkspaceSyncState.Missing);
            }
            else if (status.State == WorkspaceSyncState.Duplicate && WorkspaceSession is { } session)
            {
                string target = WorkspaceSynchronization.Target(session.Manifest.Difficulties.Single(d => d.Id == syncDifficulty))!;
                var claims = syncClaims.Where(claim => WorkspaceSynchronization.Paths.Equals(Path.GetDirectoryName(claim.Target), Path.GetDirectoryName(target))).ToArray();
                if (claims.Length > 0)
                {
                    syncRow = Math.Clamp(syncRow, 0, claims.Length - 1); var keep = claims[syncRow];
                    c.Text(keep.Project, x + 20, y + 120, 12, Foreground, w - 40);
                    c.Text(keep.Name + " · " + keep.DifficultyId, x + 20, y + 150, 12, Muted, w - 40);
                    Navigation(c, x, y + 200, claims.Length);
                    c.Text(L.Get("sync.keepHelp"), x + 20, y + 246, 13, Muted, w - 40);
                    Button(c, new(x + 20, y + 284, 220, 34), L.Get("sync.inspect"), () => RequestOpenExternalPath?.Invoke(keep.AuthoringFile!), enabled: keep.AuthoringFile is not null);
                    Button(c, new(x + 20, y + h - 58, 220, 36), L.Get("sync.keepOnly"), () =>
                    {
                        var project = CaptureProject(); syncPage = "checking"; syncDifficulty = keep.DifficultyId;
                        syncCommitTask = Task.Run(() => WorkspaceAssociations.KeepOnly(session, project, target, keep));
                    });
                }
            }
            else if (status.State == WorkspaceSyncState.AudioMissing)
            {
                c.Text(status.Candidate?.Document.AudioPath ?? "", x + 20, y + 120, 12, Muted, w - 40);
                Button(c, new(x + 20, y + h - 106, 240, 36), L.Get("sync.chooseAudio"), () => RequestSyncAudio?.Invoke(path =>
                {
                    if (path is null) return;
                    var current = difficulties.Single(d => d.Id == syncDifficulty);
                    current.History.Begin(L.Get("sync.chooseAudio")); current.History.Document.AudioPath = path; current.History.Commit();
                    ResolveSync(false);
                }));
                Button(c, new(x + 20, y + h - 58, 220, 36), L.Get("sync.refresh"), () => RefreshSynchronization());
            }
            else if (status.Candidate is not null)
            {
                Button(c, new(x + 20, y + h - 58, 200, 36), L.Get("sync.allLocal"), () => ResolveSync(false));
                Button(c, new(x + 232, y + h - 58, 200, 36), L.Get("sync.allExternal"), () => ResolveSync(true));
            }
        }
        Button(c, new(x + w - 150, y + h - 58, 130, 36), L.Get("mac.cancel"), CancelSynchronization);
    }
    private void ChooseSyncItem(WorkspaceMerge merge, string key, bool external)
    {
        syncChoices[key] = external;
        syncRoundChoices.Add(key);
        syncPreviewRevision++; syncResultPane = null;
        if (syncTab == "Objects")
            for (int offset = 1; offset < merge.Conflicts.Count; offset++)
            {
                int next = (syncRow + offset) % merge.Conflicts.Count;
                if (SyncCategory(merge.Conflicts[next].Key) == syncTab && !syncChoices.ContainsKey(merge.Conflicts[next].Key))
                { syncRow = next; break; }
            }
    }
    private void Navigation(ICanvas c, float x, float y, int count)
    {
        Button(c, new(x + 20, y, 64, 30), "‹", () => syncRow--, enabled: syncRow > 0);
        c.Text(L.Get("sync.page", syncRow + 1, count), x + 100, y + 6, 12, Muted, 120);
        Button(c, new(x + 230, y, 64, 30), "›", () => syncRow++, enabled: syncRow + 1 < count);
    }
    private void InspectSync(WorkspaceMerge merge)
    {
        if (WorkspaceSession is not { } session) return;
        try
        {
            string folder = Path.Combine(LibrarySettings.Workspace, ".sync-history", "reviews");
            WorkspaceProject.RejectLinks(folder); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, syncDifficulty.ToString("N") + ".txt");
            var text = new System.Text.StringBuilder();
            foreach (var conflict in merge.Conflicts)
                text.AppendLine(conflict.Key).AppendLine(L.Get("sync.chooseLocal")).AppendLine(conflict.Local)
                    .AppendLine(L.Get("sync.chooseExternal")).AppendLine(conflict.External).AppendLine();
            text.AppendLine(L.Get("sync.allLocal")).AppendLine(ProjectSerializer.Serialize(merge.Local));
            text.AppendLine(L.Get("sync.allExternal")).AppendLine(merge.External.Text);
            File.WriteAllText(path, text.ToString()); RequestOpenExternalPath?.Invoke(path);
        }
        catch (Exception e) { ShowError(e.Message); }
    }
    private static void DrawSyncValue(ICanvas c, string value, float x, float y, float w)
    {
        var lines = value.Split('\n');
        for (int i = 0; i < Math.Min(4, lines.Length); i++) c.Text(lines[i], x, y + i * 21, 12, Foreground, w);
    }
    private static WorkspaceSession DetachedSession(WorkspaceSession session, BeatmapProject project) => session with
    {
        Project = project,
        Manifest = WorkspaceProject.SnapshotManifest(session.Manifest)
    };
    private static string ManifestStamp(WorkspaceManifest manifest) => string.Join('\n', manifest.Difficulties.Select(e =>
        string.Join('|', e.Id, e.Source, e.SourceHash, e.ExportTarget, e.ExportHash, e.Sync?.AudioHash, e.Sync?.Path)))
        + manifest.SongsRoot + manifest.SourceDirectory + manifest.ExternalSourceDirectory;
}
