using FruitsAtelier.App.Editor;

static class WorkspaceFileMonitorTests
{
    public static void Run()
    {
        using var monitor = new WorkspaceFileMonitor();
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/file-monitor", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "map.osu");
        for (int i = 0; i < 100; i++) monitor.Record(path);
        Check(!monitor.IsReady(DateTime.UtcNow), "notifications wait for quiet time");
        var burst = monitor.Drain(DateTime.UtcNow.AddSeconds(2));
        Check(burst.Paths.SequenceEqual(new[] { path }), "save burst coalesces by path");
        Check(!monitor.IsReady(DateTime.UtcNow.AddSeconds(2)), "drain consumes batch");
        monitor.Record(Path.Combine(root, ".sync-history", "saved.catchdiff"));
        monitor.Record(Path.Combine(root, "library.db-wal"));
        monitor.Record(Path.Combine(root, "memory.json"));
        Check(!monitor.IsReady(DateTime.UtcNow.AddSeconds(2)), "internal writes ignored");
        for (int i = 0; i < 2100; i++) monitor.Record(Path.Combine(root, i + ".osu"));
        Check(monitor.Drain(DateTime.UtcNow.AddSeconds(2)).FullScan, "bounded queue falls back to reconciliation");
        monitor.Configure(new[] { root }, DateTime.UtcNow);
        File.WriteAllText(path, "first");
        Await(() => monitor.IsReady(DateTime.UtcNow), "native create event");
        Check(monitor.Drain(DateTime.UtcNow).Paths.Contains(path), "created path reported");
        File.WriteAllText(path, "second");
        Await(() => monitor.IsReady(DateTime.UtcNow), "native modify event");
        Check(monitor.Drain(DateTime.UtcNow).Paths.Contains(path), "modified path reported");
        string audio = Path.Combine(root, "audio.mp3"); File.WriteAllBytes(audio, [1, 2, 3]);
        Await(() => monitor.IsReady(DateTime.UtcNow), "native audio event");
        Check(monitor.Drain(DateTime.UtcNow).Paths.Contains(audio), "resource changes included");
        string moved = Path.Combine(root, "renamed.osu");
        File.Move(path, moved);
        Await(() => monitor.IsReady(DateTime.UtcNow), "native rename event");
        var rename = monitor.Drain(DateTime.UtcNow);
        Check(rename.Paths.Contains(path) && rename.Paths.Contains(moved), "rename retains old and new paths");
        File.Delete(moved);
        Await(() => monitor.IsReady(DateTime.UtcNow), "native delete event");
        monitor.Drain(DateTime.UtcNow);
        monitor.Invalidate();
        Check(monitor.Drain(DateTime.UtcNow.AddSeconds(2)).FullScan, "lost notifications request reconciliation");
        monitor.Dispose(); monitor.Record(path);
        Check(!monitor.IsReady(DateTime.UtcNow.AddSeconds(2)), "disposed monitor releases and rejects notifications");
    }

    internal static void Await(Func<bool> condition, string message)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < until) Thread.Sleep(20);
        Check(condition(), message);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
