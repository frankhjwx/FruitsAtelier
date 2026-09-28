using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SyncResult(WorkspaceSession Session, BeatmapProject Snapshot, WorkspaceManifest Manifest, string Stamp, WorkspaceSyncScan Scan, Dictionary<Guid, WorkspaceMerge> Merges, IReadOnlyList<WorkspaceClaim> Claims, Dictionary<Guid, SyncComparison> Comparisons, bool ReviewResolved);
    private Task<SyncResult>? syncTask;
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
    private bool syncPreserveHistory;
    private IReadOnlyList<WorkspaceClaim> syncClaims = [];
    private string syncFailure = "";
    public bool SynchronizationVisible => syncPage is not null and not "checking";
    public bool SynchronizationBusy => syncTask is not null || syncCommitTask is not null;
    public bool SynchronizationNeedsRedraw => syncTask is { IsCompleted: true } || syncCommitTask is { IsCompleted: true } || SynchronizationVisible && syncPreviewTask is { IsCompleted: true }
        || WorkspaceSession is not null && !LibraryVisible && DateTime.UtcNow >= nextSyncCheck;
    public WorkspaceSyncState DifficultySyncState(int index) => index >= 0 && index < difficulties.Count
        && syncStatuses.TryGetValue(difficulties[index].Id, out var status) ? status.State : WorkspaceSyncState.Local;
    public Action<Action<string?>>? RequestSyncFile { get; set; }
    public Action<Action<string?>>? RequestSyncAudio { get; set; }

    public void RefreshSynchronization(Action? continuation = null, bool quiet = false, bool reviewResolved = false)
    {
        if (WorkspaceSession is not { } session) return;
        if (SynchronizationBusy)
        {
            if (continuation is not null) afterSynchronization = continuation;
            if (!quiet) syncPage = "checking";
            return;
        }
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        CancelInteraction(); syncPage = "checking"; hits.Clear(); fields.Clear();
        var snapshot = CaptureProject();
        var manifest = WorkspaceProject.SnapshotManifest(session.Manifest);
        string stamp = ManifestStamp(manifest);
        var frozen = session with { Manifest = manifest, Project = snapshot };
        string songs = LibrarySettings.Songs; bool compensate = compensateTinyDroplets;
        afterSynchronization = continuation;
        syncTask = Task.Run(() =>
        {
            var scan = WorkspaceSynchronization.Scan(frozen, songs);
            var merges = new Dictionary<Guid, WorkspaceMerge>();
            foreach (var status in scan.Difficulties.Where(s => s.State is WorkspaceSyncState.Changed or WorkspaceSyncState.NeedsBaseline
                || reviewResolved && s.State == WorkspaceSyncState.Current))
            {
                var entry = frozen.Manifest.Difficulties.Single(d => d.Id == status.DifficultyId);
                var diff = snapshot.Difficulties.Single(d => d.Id == status.DifficultyId);
                merges[entry.Id] = entry.Sync is null
                    ? WorkspaceSynchronization.CompareWithoutBaseline(diff.Document, status.Candidate!, session.Directory, compensate)
                    : WorkspaceSynchronization.Merge(entry, diff.Document, status.Candidate!, session.Directory, compensate);
            }
            var comparisons = merges.Where(m => m.Value.Conflicts.Count > 0).ToDictionary(m => m.Key, m => PrepareSyncComparison(m.Value, compensate,
                Path.Combine(session.Directory, manifest.Difficulties.Single(d => d.Id == m.Key).File)));
            return new SyncResult(session, snapshot, manifest, stamp, scan, merges, WorkspaceAssociations.Claims(Path.GetDirectoryName(session.Directory)!), comparisons, reviewResolved);
        });
        nextSyncCheck = DateTime.UtcNow.AddSeconds(30);
    }

    private void PumpSynchronization()
    {
        if (syncCommitTask is { IsCompleted: true } committed)
        {
            syncCommitTask = null;
            try
            {
                var session = committed.GetAwaiter().GetResult();
                var continuation = afterSynchronization; afterSynchronization = null;
                syncPage = null;
                libraryProjectsNeedReindex = true; StartLibraryScan();
                if (session.Project.Difficulties.Count == 0) { LeaveEditor(); return; }
                Guid active = syncDifficulty == Guid.Empty ? difficulties[activeDifficulty].Id : syncDifficulty;
                if (syncPreserveHistory && WorkspaceSession?.Directory == session.Directory)
                {
                    foreach (var diff in session.Project.Difficulties)
                    {
                        var existing = difficulties.FirstOrDefault(d => d.Id == diff.Id);
                        if (existing is null) difficulties.Add(new DifficultySession(diff));
                        else
                        {
                            var before = existing.History.Document.DeepClone();
                            existing.History.RebaseSharedMetadata(snapshot => WorkspaceSynchronization.RebaseContext(snapshot, before, diff.Document));
                            existing.History.MarkSaved();
                        }
                    }
                    WorkspaceSession = session; projectStructureDirty = false;
                    convertedSnapshot = null; resourceSnapshot = null; resourceReferences = null;
                    libraryProjectsNeedReindex = true; QueueLibrarySearch(); CheckWorkspaceResources();
                }
                else LoadWorkspace(session);
                syncPreserveHistory = false;
                syncBypass = true;
                try { SwitchDifficulty(Math.Max(0, difficulties.FindIndex(d => d.Id == active))); }
                finally { syncBypass = false; }
                ReleaseWaveform(); RequestDifficultyChanged?.Invoke();
                RefreshSynchronization(continuation);
            }
            catch (Exception e) { syncPreserveHistory = false; syncPage = "failed"; syncFailure = e.Message; }
        }
        if (syncTask is { IsCompleted: true } completed)
        {
            syncTask = null;
            try
            {
                var result = completed.GetAwaiter().GetResult();
                if (!ReferenceEquals(WorkspaceSession, result.Session))
                {
                    syncPage = null;
                    if (WorkspaceSession is not null) RefreshSynchronization(afterSynchronization);
                    return;
                }
                if (!ResourceSnapshotMatches(result.Snapshot) || result.Stamp != ManifestStamp(result.Session.Manifest))
                { syncPage = null; RefreshSynchronization(afterSynchronization); return; }
                syncStatuses.Clear(); syncMerges.Clear();
                syncComparisons.Clear();
                foreach (var comparison in result.Comparisons) syncComparisons[comparison.Key] = comparison.Value;
                syncClaims = result.Claims;
                foreach (var status in result.Scan.Difficulties) syncStatuses[status.DifficultyId] = status;
                foreach (var merge in result.Merges) syncMerges[merge.Key] = merge.Value;
                var automatic = result.Scan.Difficulties.Where(s => s.State == WorkspaceSyncState.Changed && !result.Merges[s.DifficultyId].RequiresResolution
                    && !(result.ReviewResolved && result.Merges[s.DifficultyId].PreviouslyResolved.Count > 0)
                    || s.State == WorkspaceSyncState.NeedsBaseline && s.Candidate!.Hash == (result.Session.Manifest.Difficulties.Single(d => d.Id == s.DifficultyId).ExportHash
                        ?? result.Session.Manifest.Difficulties.Single(d => d.Id == s.DifficultyId).SourceHash)).ToArray();
                bool canAdd = !result.Scan.Difficulties.Any(s => s.State is WorkspaceSyncState.Ambiguous or WorkspaceSyncState.Duplicate or WorkspaceSyncState.Unavailable);
                if (automatic.Length > 0 || canAdd && result.Scan.Additions.Count > 0)
                {
                    if (AudioPlaying) RequestPausePlayback?.Invoke();
                    syncPage = "checking";
                    syncPreserveHistory = true;
                    syncCommitTask = Task.Run(() =>
                    {
                        var project = result.Snapshot;
                        var session = result.Session with { Manifest = result.Manifest, Project = project };
                        ProjectSerializer.WriteFile(project, Path.Combine(WorkspaceSynchronization.Archive(session, "before-sync"), "current.catchproj"));
                        foreach (var status in automatic)
                        {
                            var entry = session.Manifest.Difficulties.Single(d => d.Id == status.DifficultyId);
                            var diff = project.Difficulties.Single(d => d.Id == entry.Id);
                            if (status.State == WorkspaceSyncState.Changed && result.Merges.TryGetValue(entry.Id, out var merge))
                                diff.Document = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>());
                            WorkspaceSynchronization.Accept(session, entry, status.Candidate!, diff.Document, compensateTinyDroplets);
                            diff.Name = OsuBeatmapReader.Setting(diff.Document, "Metadata", "Version") ?? diff.Name;
                        }
                        if (canAdd)
                            foreach (var addition in result.Scan.Additions)
                            {
                                WorkspaceAssociations.EnsureImport(session, LibrarySettings.Workspace, addition.Path);
                                project.Difficulties.AddRange(BeatmapProject.FromDocuments([addition.Document]).Difficulties);
                            }
                        WorkspaceProject.Save(session, project);
                        return WorkspaceProject.Open(session.Directory);
                    });
                    return;
                }
                syncPage = null;
                StatusMessage = L.Get("sync.complete");
                int target = syncDifficulty == Guid.Empty ? activeDifficulty : difficulties.FindIndex(d => d.Id == syncDifficulty);
                if (result.ReviewResolved && target >= 0 && syncMerges.TryGetValue(difficulties[target].Id, out var review) && review.Conflicts.Count > 0)
                { BeginSyncReview(target); return; }
                if (target >= 0 && ShowSyncProblem(target)) return;
                var continuation = afterSynchronization; afterSynchronization = null;
                syncBypass = true;
                try { continuation?.Invoke(); }
                finally { syncBypass = false; }
            }
            catch (Exception e) { syncPreserveHistory = false; syncPage = "failed"; syncFailure = e.Message; }
        }
        if (WorkspaceSession is not null && !LibraryVisible && !SynchronizationBusy && !DiscardConfirmationVisible && !RatingEditInProgress
            && !IsEditingText && !SongSetupVisible && DateTime.UtcNow >= nextSyncCheck) RefreshSynchronization(quiet: true);
    }

    private bool ShowSyncProblem(int index)
    {
        var state = DifficultySyncState(index);
        if (state is WorkspaceSyncState.Local or WorkspaceSyncState.Current) return false;
        BeginSyncReview(index);
        return true;
    }

    private void BeginSyncReview(int index)
    {
        syncDifficulty = difficulties[index].Id; syncRow = 0; syncChoices.Clear(); syncRoundChoices.Clear();
        if (syncMerges.TryGetValue(syncDifficulty, out var merge))
            foreach (string key in merge.PreviouslyResolved) syncChoices[key] = false;
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

    public void ShowDeleteDifficulty(int index)
    {
        if (index < 0 || index >= difficulties.Count || SynchronizationBusy) return;
        if (WorkspaceSession is null && !SaveWorkspace()) return;
        syncDifficulty = difficulties[index].Id;
        if (DifficultySyncState(index) is WorkspaceSyncState.Duplicate or WorkspaceSyncState.Ambiguous) { ShowSyncProblem(index); return; }
        syncPage = "delete"; menu = -1; contextItems.Clear(); hits.Clear(); fields.Clear();
    }

    private void DeleteSyncDifficulty()
    {
        if (WorkspaceSession is not { } session) return;
        var project = CaptureProject(); Guid id = syncDifficulty;
        syncPage = "checking";
        syncCommitTask = Task.Run(() =>
        {
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
        syncPage = "checking";
        syncCommitTask = Task.Run(() =>
        {
            ProjectSerializer.WriteFile(project, Path.Combine(WorkspaceSynchronization.Archive(session, "resolution"), "current.catchproj"));
            if (useExternal is true) diff.Document = external.Document.DeepClone();
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
            ProjectSerializer.WriteFile(project, Path.Combine(WorkspaceSynchronization.Archive(session, "restore"), "current.catchproj"));
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
            c.Text(syncFailure, x + 20, y + 80, 14, Error, w - 40);
            Button(c, new(x + 20, y + h - 58, 220, 36), L.Get("sync.refresh"), () => RefreshSynchronization());
            Button(c, new(x + w - 150, y + h - 58, 130, 36), L.Get("mac.cancel"), CancelSynchronization);
            return;
        }
        var difficulty = difficulties.FirstOrDefault(d => d.Id == syncDifficulty);
        c.Text(difficulty?.Name ?? "", x + 20, y + 52, 16, Accent, w - 40);
        if (syncPage == "delete")
        {
            c.Text(L.Get("sync.deleteHelp"), x + 20, y + 92, 14, Foreground, w - 40);
            string? path = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == syncDifficulty) is { } entry ? WorkspaceSynchronization.Target(entry) : null;
            c.Text(path ?? L.Get("sync.local"), x + 20, y + 130, 12, Muted, w - 40);
            Button(c, new(x + 20, y + h - 58, 220, 36), L.Get("sync.delete"), DeleteSyncDifficulty);
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
        for (int offset = 1; offset < merge.Conflicts.Count; offset++)
        {
            int next = (syncRow + offset) % merge.Conflicts.Count;
            if (!syncChoices.ContainsKey(merge.Conflicts[next].Key)) { syncRow = next; break; }
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
