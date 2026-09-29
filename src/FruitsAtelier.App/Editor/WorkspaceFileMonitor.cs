using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

/// <summary>Coalesces notifications; callers must inspect content before applying changes.</summary>
public sealed class WorkspaceFileMonitor : IDisposable
{
    private readonly object gate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly HashSet<string> pending = new(WorkspaceSynchronization.Paths);
    private string[] roots = [];
    private bool fullScan, disposed;
    private DateTime changedAt, retryAt;
    public bool IsReady(DateTime now) { lock (gate) return (fullScan || pending.Count > 0) && now - changedAt >= TimeSpan.FromMilliseconds(750); }

    public void Configure(IEnumerable<string> paths, DateTime now)
    {
        var requested = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath)
            .Distinct(WorkspaceSynchronization.Paths).OrderBy(p => p, WorkspaceSynchronization.Paths).ToArray();
        if (disposed || roots.SequenceEqual(requested, WorkspaceSynchronization.Paths) && now < retryAt) return;
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear(); roots = requested; retryAt = now.AddSeconds(30);
        foreach (string root in roots)
        {
            if (!Directory.Exists(root)) continue;
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new(root) { IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                watcher.Created += Changed; watcher.Changed += Changed; watcher.Deleted += Changed;
                watcher.Renamed += (_, e) => { Record(e.OldFullPath); Record(e.FullPath); };
                watcher.Error += (_, _) => Invalidate();
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { watcher?.Dispose(); Invalidate(); }
        }
    }

    private void Changed(object sender, FileSystemEventArgs e) => Record(e.FullPath);
    public void Record(string path)
    {
        // Recovery archives and index/database writes are not source changes.
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".sync-history")) return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".db" or ".db-wal" or ".db-shm" or ".db-journal" or ".tmp" or ".log" or ".json") return;
        lock (gate)
        {
            if (disposed) return;
            changedAt = DateTime.UtcNow;
            if (pending.Count >= 2048) { fullScan = true; pending.Clear(); }
            if (!fullScan) pending.Add(path);
        }
    }
    public void Invalidate()
    {
        lock (gate) { if (disposed) return; fullScan = true; pending.Clear(); changedAt = DateTime.UtcNow; }
    }
    public (bool FullScan, string[] Paths) Drain(DateTime now)
    {
        lock (gate)
        {
            if (!IsReady(now)) return (false, []);
            var result = (fullScan, pending.ToArray());
            fullScan = false; pending.Clear(); return result;
        }
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; pending.Clear(); fullScan = false; }
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear();
    }
}
