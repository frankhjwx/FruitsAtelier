using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool VersionHistoryVisible { get; private set; }
    public bool VersionHistoryBusy => versionListTask is not null || versionReadTask is not null || versionPreviewTask is not null || versionRestoreTask is not null;
    private bool VersionHistoryNeedsRedraw => versionListTask is { IsCompleted: true } || versionReadTask is { IsCompleted: true }
        || versionPreviewTask is { IsCompleted: true } || versionRestoreTask is { IsCompleted: true };
    private IReadOnlyList<WorkspaceVersion> versions = [];
    private BeatmapProject? versionProject;
    private Task<IReadOnlyList<WorkspaceVersion>>? versionListTask;
    private Task<BeatmapProject>? versionReadTask;
    private Task<(SyncPane? Current, SyncPane Historical)>? versionPreviewTask;
    private Task<ProjectDifficulty>? versionRestoreTask;
    private SyncPane? versionCurrentPane, versionHistoricalPane;
    private int versionIndex, versionDifficultyIndex, versionScroll, versionDifficultyScroll;
    private string versionError = "";
    private Rect versionListBounds, versionDifficultyBounds, versionPreviewBounds;
    private static readonly HashSet<Guid> noVersionSelection = [];

    public void ShowVersionHistory()
    {
        if (WorkspaceSession is not { } session || VersionHistoryVisible || SynchronizationBusy || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        menu = -1; contextItems.Clear(); hits.Clear(); fields.Clear();
        VersionHistoryVisible = true;
        versions = []; versionProject = null; versionError = ""; versionScroll = versionDifficultyScroll = 0;
        versionCurrentPane = versionHistoricalPane = null;
        syncShowValues = false;
        versionListTask = Task.Run(() => WorkspaceVersionHistory.List(session));
    }

    private void CloseVersionHistory()
    {
        VersionHistoryVisible = false;
        foreach (Task? task in new Task?[] { versionListTask, versionReadTask, versionPreviewTask, versionRestoreTask })
            if (task is not null) _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        versionListTask = null; versionReadTask = null; versionPreviewTask = null; versionRestoreTask = null;
        versionProject = null; versionCurrentPane = versionHistoricalPane = null;
        hits.Clear(); fields.Clear();
    }

    private void SelectVersion(int index)
    {
        if (WorkspaceSession is not { } session || versions.Count == 0 || versionRestoreTask is not null) return;
        versionIndex = Math.Clamp(index, 0, versions.Count - 1);
        int rows = Math.Max(1, (int)(versionListBounds.Height / 54));
        versionScroll = Math.Clamp(versionScroll, Math.Max(0, versionIndex - rows + 1), versionIndex);
        versionProject = null; versionError = "";
        versionCurrentPane = versionHistoricalPane = null;
        if (versionPreviewTask is { } preview) _ = preview.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        versionPreviewTask = null;
        if (versionReadTask is { } previous) _ = previous.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        var version = versions[versionIndex];
        versionReadTask = Task.Run(() => WorkspaceVersionHistory.Read(session, version));
    }

    private void SelectVersionDifficulty(int index)
    {
        if (versionProject is null || versionRestoreTask is not null) return;
        versionDifficultyIndex = Math.Clamp(index, 0, versionProject.Difficulties.Count - 1);
        int rows = Math.Max(1, (int)(versionDifficultyBounds.Height / 28));
        versionDifficultyScroll = Math.Clamp(versionDifficultyScroll, Math.Max(0, versionDifficultyIndex - rows + 1), versionDifficultyIndex);
        var historical = versionProject.Difficulties[versionDifficultyIndex].Document.DeepClone();
        Guid id = versionProject.Difficulties[versionDifficultyIndex].Id;
        var current = difficulties.FirstOrDefault(d => d.Id == id)?.History.Document.DeepClone();
        bool compensate = compensateTinyDroplets;
        versionError = ""; versionCurrentPane = versionHistoricalPane = null;
        if (versionPreviewTask is { } previous) _ = previous.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        versionPreviewTask = Task.Run(() => (current is null ? null : PrepareSyncPane(current, compensate), PrepareSyncPane(historical, compensate)));
    }

    private void PumpVersionHistory()
    {
        if (!VersionHistoryVisible) return;
        try
        {
            if (versionListTask is { IsCompleted: true } listed)
            {
                versionListTask = null; versions = listed.GetAwaiter().GetResult();
                if (versions.Count > 0) SelectVersion(0);
            }
            if (versionReadTask is { IsCompleted: true } read)
            {
                versionReadTask = null; versionProject = read.GetAwaiter().GetResult();
                int index = versionProject.Difficulties.FindIndex(d => d.Id == difficulties[activeDifficulty].Id);
                versionDifficultyScroll = 0;
                SelectVersionDifficulty(Math.Max(0, index));
            }
            if (versionPreviewTask is { IsCompleted: true } prepared)
            {
                versionPreviewTask = null;
                (versionCurrentPane, versionHistoricalPane) = prepared.GetAwaiter().GetResult();
                syncViewStart = 0;
                syncViewSpan = Math.Max(1000, Math.Max(versionHistoricalPane.Document.DurationMs, versionCurrentPane?.Document.DurationMs ?? 0));
            }
            if (versionRestoreTask is { IsCompleted: true } restored)
            {
                versionRestoreTask = null;
                ApplyHistoricalDifficulty(restored.GetAwaiter().GetResult());
            }
        }
        catch (Exception error) { versionError = L.Get("history.failed", error.Message); }
    }

    private void RestoreSelectedVersion()
    {
        if (WorkspaceSession is not { } session || versionProject is null || VersionHistoryBusy) return;
        var selected = versionProject.Difficulties[versionDifficultyIndex];
        var historical = new ProjectDifficulty { Id = selected.Id, Name = selected.Name, Document = selected.Document.DeepClone() };
        var current = CaptureProject();
        if (!current.Difficulties.Any(d => d.Id == historical.Id) && current.Difficulties.Count >= 256)
        { versionError = L.Get("project.invalid"); return; }
        versionRestoreTask = Task.Run(() =>
        {
            WorkspaceVersionHistory.ArchiveCurrent(session, current);
            return historical;
        });
    }

    private void ApplyHistoricalDifficulty(ProjectDifficulty historical)
    {
        int index = difficulties.FindIndex(d => d.Id == historical.Id);
        string? previousAudio = Document.AudioPath;
        if (index >= 0)
        {
            historical.Document.SourcePath = difficulties[index].History.Document.SourcePath;
            var entry = WorkspaceSession!.Manifest.Difficulties.FirstOrDefault(d => d.Id == historical.Id);
            Action<bool, MapDocument, MapDocument>? restoreAssociation = null;
            if (entry is not null && WorkspaceSynchronization.Target(entry) is { } source && !File.Exists(source))
            {
                var original = WorkspaceProject.SnapshotManifest(WorkspaceSession.Manifest).Difficulties.Single(d => d.Id == historical.Id);
                var local = new WorkspaceDifficulty { Id = historical.Id, Name = historical.Name };
                historical.Document.SourcePath = null;
                restoreAssociation = (redo, _, _) => RestoreVersionAssociation(historical.Id, redo ? local : original);
                RestoreVersionAssociation(historical.Id, local);
            }
            difficulties[index].History.RestoreVersion(L.Get("history.restore"), historical.Document, restoreAssociation);
        }
        else
        {
            // Deleted versions return unlinked so missing or now-owned sources cannot block recovery.
            historical.Document.SourcePath = null;
            difficulties.Add(new DifficultySession(historical));
            WorkspaceSession!.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = historical.Id, Name = historical.Name });
            index = difficulties.Count - 1;
            projectStructureDirty = true;
        }
        CloseVersionHistory();
        activeDifficulty = index; Select(Guid.Empty); convertedSnapshot = null; RevealDifficultyTab();
        syncStatuses.Remove(historical.Id); syncMerges.Remove(historical.Id); syncComparisons.Remove(historical.Id);
        resourceSnapshot = null; resourceReferences = null;
        PreloadProjectHitsounds();
        if (!WorkspaceSynchronization.Paths.Equals(previousAudio, Document.AudioPath))
        { ReleaseWaveform(); initializeTransport = !string.IsNullOrWhiteSpace(Document.AudioPath); RequestDifficultyChanged?.Invoke(); }
        SetNotice(L.Get("history.restored", historical.Name));
    }

    private void RestoreVersionAssociation(Guid id, WorkspaceDifficulty value)
    {
        var entry = WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == id);
        entry.Source = value.Source; entry.SourceHash = value.SourceHash;
        entry.ExportTarget = value.ExportTarget; entry.ExportHash = value.ExportHash;
        entry.Sync = value.Sync; entry.SyncFile = value.SyncFile;
        syncStatuses.Remove(id); syncMerges.Remove(id); syncComparisons.Remove(id);
        resourceSnapshot = null; resourceReferences = null;
    }

    private void VersionHistoryKey(int key)
    {
        if (versionRestoreTask is not null) return;
        if (key == 27) CloseVersionHistory();
        else if (key == 38) SelectVersion(versionIndex - 1);
        else if (key == 40) SelectVersion(versionIndex + 1);
        else if (key == 37) SelectVersionDifficulty(versionDifficultyIndex - 1);
        else if (key == 39) SelectVersionDifficulty(versionDifficultyIndex + 1);
    }

    private void ScrollVersionHistory(float x, float y, float delta, bool ctrl)
    {
        if (versionRestoreTask is not null) return;
        if (versionListBounds.Contains(x, y)) versionScroll = Math.Clamp(versionScroll - (int)(delta / 120) * 3, 0,
            Math.Max(0, versions.Count - (int)(versionListBounds.Height / 54)));
        else if (versionDifficultyBounds.Contains(x, y)) versionDifficultyScroll = Math.Clamp(versionDifficultyScroll - (int)(delta / 120) * 3, 0,
            Math.Max(0, (versionProject?.Difficulties.Count ?? 0) - (int)(versionDifficultyBounds.Height / 28)));
        else if (versionPreviewBounds.Contains(x, y) && versionHistoricalPane is not null)
        {
            if (ctrl) syncViewSpan = Math.Clamp(syncViewSpan / Math.Pow(1.25, delta / 120), 100, Math.Max(1000, versionHistoricalPane.Document.DurationMs * 2));
            else syncViewStart = Math.Max(0, syncViewStart - delta / 120 * syncViewSpan / 8);
        }
    }

    private void DrawVersionHistory(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), Background);
        c.Text(L.Get("history.title"), 24, 18, 23, Foreground, width - 100, true);
        Button(c, new(width - 66, 16, 42, 32), "×", () => CloseVersionHistory(), enabled: versionRestoreTask is null);
        c.Text(L.Get("history.help"), 24, 54, 12, Muted, width - 48);
        float leftWidth = Math.Clamp(width * .27f, 220, 330), rightX = leftWidth + 36, rightWidth = width - rightX - 24;
        versionListBounds = new(24, 88, leftWidth - 12, height - 180);
        c.Fill(versionListBounds, Panel);
        int visible = Math.Max(1, (int)(versionListBounds.Height / 54));
        for (int i = versionScroll; i < Math.Min(versions.Count, versionScroll + visible); i++)
        {
            int selected = i; var version = versions[i];
            var row = new Rect(versionListBounds.X + 4, versionListBounds.Y + (i - versionScroll) * 54, versionListBounds.Width - 8, 50);
            c.Fill(row, i == versionIndex ? Surface : Panel, 4);
            c.Text(version.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), row.X + 8, row.Y + 7, 12, Foreground, row.Width - 16);
            string operation = L.Get("history.operation." + (version.Operation is "save" or "delete" or "restore" or "before-sync" or "reimport" ? version.Operation : "other"));
            c.Text(L.Get("history.versionLabel", operation, L.Get(version.WorkingCopy ? "history.working" : "history.saved")), row.X + 8, row.Y + 28, 11, Muted, row.Width - 16);
            hits.Add(new(row, () => SelectVersion(selected), versionRestoreTask is null));
        }
        if (versions.Count == 0) c.Text(L.Get(versionListTask is null ? "history.empty" : "history.loading"), 34, 100, 13, Muted, leftWidth - 32);
        versionDifficultyBounds = new(rightX, 88, rightWidth, 88);
        if (versionProject is { } project)
        {
            int count = (int)(versionDifficultyBounds.Height / 28);
            for (int i = versionDifficultyScroll; i < Math.Min(project.Difficulties.Count, versionDifficultyScroll + count); i++)
            {
                int selected = i; var diff = project.Difficulties[i];
                bool deleted = !difficulties.Any(d => d.Id == diff.Id);
                Button(c, new(rightX, 88 + (i - versionDifficultyScroll) * 28, rightWidth, 26),
                    deleted ? L.Get("history.deletedName", diff.Name) : diff.Name, () => SelectVersionDifficulty(selected), i == versionDifficultyIndex, versionRestoreTask is null);
            }
            var historical = project.Difficulties[versionDifficultyIndex];
            var current = difficulties.FirstOrDefault(d => d.Id == historical.Id)?.History.Document;
            string Metadata(MapDocument? doc, string key) => doc is null ? "—" : OsuBeatmapReader.Setting(doc, "Metadata", key) ?? "—";
            foreach (var (key, row) in new[] { ("Artist", 0), ("Title", 1), ("Creator", 2), ("Version", 3) })
                c.Text(L.Get("history.field", key, Metadata(current, key), Metadata(historical.Document, key)), rightX, 184 + row * 17, 11, Foreground, rightWidth);
            versionPreviewBounds = new(rightX, 260, rightWidth, Math.Max(100, height - 384));
            float paneWidth = (rightWidth - 12) / 2;
            if (versionHistoricalPane is { } pane)
            {
                var left = new Rect(rightX, 260, paneWidth, versionPreviewBounds.Height);
                if (versionCurrentPane is { } currentPane) DrawSyncPane(c, currentPane, left, L.Get("history.current"), noVersionSelection, null, L.Get("history.current"));
                else { c.Fill(left, Panel); c.Text(L.Get("history.deleted"), left.X + 12, left.Y + 16, 14, Muted, left.Width - 24); }
                DrawSyncPane(c, pane, new(rightX + paneWidth + 12, 260, paneWidth, versionPreviewBounds.Height), L.Get("history.historical"), noVersionSelection, null,
                    versions[versionIndex].TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }
            else c.Text(L.Get("history.loading"), rightX, 270, 13, Muted, rightWidth);
            c.Text(L.Get("history.counts", historical.Document.Fruits.Count, historical.Document.Tracks.Count, historical.Document.ImportedSliders.Count,
                historical.Document.BananaShowers.Count), rightX, height - 108, 12, Muted, rightWidth);
        }
        if (versionError.Length > 0) c.Text(versionError, rightX, height - 87, 12, Error, rightWidth);
        else c.Text(L.Get("history.restoreHelp"), rightX, height - 87, 12, Muted, rightWidth);
        Button(c, new(width - 244, height - 52, 220, 36), L.Get(versionRestoreTask is null ? "history.restore" : "history.restoring"), RestoreSelectedVersion,
            enabled: versionProject is not null && !VersionHistoryBusy);
        c.Text(L.Get("history.navigation"), 24, height - 43, 11, Muted, width - 300);
    }
}
