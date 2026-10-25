using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private WorkspaceFileMonitor? fileMonitor;
    private DateTime nextMonitorConfiguration;
    private bool fileSyncPending, fileSearchMissing;
    private Task<bool>? acceptedFileCheck;
    private bool SyncInteractionActive => WantsCapture || IsEditingText || SongSetupVisible || RatingEditInProgress
        || BananaEditVisible || BananaEditVisible || StreamDialogVisible || TimingModal || DiscardConfirmationVisible || IsTestplaying;

    public void EnableFileMonitoring()
    {
        fileMonitor ??= new();
        nextMonitorConfiguration = DateTime.MinValue;
        ConfigureFileMonitoring();
    }

    public void StopFileMonitoring()
    {
        RetireStoryboard();
        acceptedFileCheck = null;
        fileMonitor?.Dispose(); fileMonitor = null;
        historyCompressionEnabled = false;
        StopHistoryCompression(); historyCompressionRoot = "";
    }

    public void CheckFilesOnActivation()
    {
        if (WorkspaceSession is null) return;
        fileSyncPending = true; fileSearchMissing = true;
    }

    private void ConfigureFileMonitoring()
    {
        if (fileMonitor is null || DateTime.UtcNow < nextMonitorConfiguration) return;
        var roots = new List<string> { LibrarySettings.Songs, LibrarySettings.Workspace };
        if (WorkspaceSession is { } session)
            roots.AddRange(session.Manifest.Difficulties.Select(WorkspaceSynchronization.Target)
                .Concat(difficulties.Select(d => d.History.Document.AudioPath)).OfType<string>()
                .Select(p => Path.GetDirectoryName(p)!).Where(p => !roots.Any(root => !string.IsNullOrWhiteSpace(root) && WorkspaceProject.Within(root, p))).ToArray());
        fileMonitor.Configure(roots, DateTime.UtcNow);
        nextMonitorConfiguration = DateTime.UtcNow.AddSeconds(5);
    }

    private void PumpFileMonitoring()
    {
        if (acceptedFileCheck is { IsCompleted: true } check)
        {
            acceptedFileCheck = null;
            if (!check.IsCompletedSuccessfully || !check.Result) fileSyncPending = true;
        }
        ConfigureFileMonitoring();
        if (fileMonitor is null || !fileMonitor.IsReady(DateTime.UtcNow)) return;
        var changes = fileMonitor.Drain(DateTime.UtcNow);
        if (storyboardDirectory is { } mediaDirectory && (changes.FullScan || changes.Paths.Any(path =>
            WorkspaceProject.Within(mediaDirectory, path) && Path.GetExtension(path).ToLowerInvariant() is ".osb" or ".png" or ".jpg" or ".jpeg" or ".mp4" or ".m4v" or ".avi" or ".flv" or ".mov" or ".mpg" or ".wmv")))
        {
            RetireStoryboard(preserveDisplay: true); RefreshStoryboard();
            // Rewritten media can keep its filename; path-keyed renderer pixels must be retired.
            if (changes.Paths.Any(path => WorkspaceProject.Within(mediaDirectory, path)
                && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".mp4" or ".m4v" or ".avi" or ".flv" or ".mov" or ".mpg" or ".wmv"))
                difficultyGraphicsReleasePending = true;
        }
        if (changes.FullScan) libraryRescanRequested = true;
        foreach (string path in changes.Paths)
        {
            if (Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                libraryPendingDirectories.Add(Path.GetDirectoryName(path)!);
            else if (!WorkspaceProject.Within(LibrarySettings.Workspace, path) && (Directory.Exists(path) || !File.Exists(path)))
                libraryPendingDirectories.Add(path);
            if (Path.GetExtension(path).Equals(".catchdiff", StringComparison.OrdinalIgnoreCase)) libraryProjectsNeedReindex = true;
        }
        if (libraryProjectsNeedReindex && LibraryVisible) QueueLibrarySearch();
        PumpPendingLibraryScan();
        if (WorkspaceSession is not { } session) return;
        var directories = session.Manifest.Difficulties.Select(WorkspaceSynchronization.Target).OfType<string>()
            .Concat(difficulties.Select(d => d.History.Document.AudioPath).OfType<string>())
            .Select(p => Path.GetDirectoryName(p)!).Append(session.Directory).Distinct(WorkspaceSynchronization.Paths).ToArray();
        var affectedPaths = changes.Paths.Where(p => directories.Any(d =>
            WorkspaceSynchronization.Paths.Equals(p, d) || WorkspaceProject.Within(d, p) || WorkspaceProject.Within(p, d)))
            .Where(p => !WorkspaceProject.Within(session.Directory, p) || WorkspaceSynchronization.Paths.Equals(p, session.Directory)
                || Path.GetExtension(p).ToLowerInvariant() is not (".catchdiff" or ".catchsync")).ToArray();
        bool affected = changes.FullScan || affectedPaths.Length > 0;
        if (!affected) return;
        var accepted = affectedPaths.Select(path => (Path: path, Hash: session.Manifest.Difficulties
            .FirstOrDefault(d => WorkspaceSynchronization.Paths.Equals(WorkspaceSynchronization.Target(d), path)) is { } entry
                ? entry.ExportHash ?? entry.SourceHash : null)).ToArray();
        if (!changes.FullScan && acceptedFileCheck is null && accepted.All(item => item.Hash is not null))
        {
            // Verify notifications against accepted bytes off-thread; a later external edit must still trigger reconciliation.
            acceptedFileCheck = Task.Run(() =>
            {
                try { return accepted.All(item => WorkspaceProject.Hash(item.Path) == item.Hash); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
            });
            return;
        }
        fileSyncPending = true;
        fileSearchMissing |= changes.FullScan || session.Manifest.Difficulties.Select(WorkspaceSynchronization.Target)
            .OfType<string>().Any(p => !File.Exists(p));
    }
}
