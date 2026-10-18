using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private WorkspaceFileMonitor? fileMonitor;
    private DateTime nextMonitorConfiguration;
    private bool fileSyncPending, fileSearchMissing;
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
        ConfigureFileMonitoring();
        if (fileMonitor is null || !fileMonitor.IsReady(DateTime.UtcNow)) return;
        var changes = fileMonitor.Drain(DateTime.UtcNow);
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
        bool affected = changes.FullScan || changes.Paths.Any(p => directories.Any(d =>
            WorkspaceSynchronization.Paths.Equals(p, d) || WorkspaceProject.Within(d, p) || WorkspaceProject.Within(p, d)));
        if (!affected) return;
        fileSyncPending = true;
        fileSearchMissing |= changes.FullScan || session.Manifest.Difficulties.Select(WorkspaceSynchronization.Target)
            .OfType<string>().Any(p => !File.Exists(p));
    }
}
