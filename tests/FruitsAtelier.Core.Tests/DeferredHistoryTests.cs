using System.Text.Json;
using FruitsAtelier.Core;

internal static class DeferredHistoryTests
{
    public static void Run()
    {
        string workspace = Path.GetFullPath("artifacts/tests/deferred-history-" + Guid.NewGuid().ToString("N"));
        var document = new MapDocument { Name = "Deferred history" };
        document.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        var project = BeatmapProject.FromDocuments([document]);
        var session = WorkspaceProject.Create(workspace, project, "");
        string history = Path.Combine(workspace, ".sync-history", session.Manifest.Id.ToString("N"));
        string snapshot = ProjectSerializer.Serialize(project);
        for (int i = 0; i < WorkspaceStorage.VersionsPerProject + 5; i++)
        {
            string round = Path.Combine(history, DateTime.UtcNow.AddMinutes(-200 + i).ToString("yyyyMMddTHHmmssfffffff") + "-save");
            Directory.CreateDirectory(round); File.WriteAllText(Path.Combine(round, "current.catchproj"), snapshot);
        }
        string pinned = Directory.GetDirectories(history).Order().First();
        string reference = Path.Combine(workspace, "live.catchdiff");
        File.WriteAllText(reference, JsonSerializer.Serialize(new { AudioPath = Path.Combine(pinned, "audio.mp3") }));
        string broken = Path.Combine(workspace, "broken.catchdiff"); File.WriteAllText(broken, "{invalid");
        project.Difficulties[0].Document.Fruits[0].X = 200;
        session = WorkspaceProject.SaveDifficulty(session, project.Difficulties[0].Id, project.Difficulties[0].Document, project.Difficulties[0].Name);
        Check(Directory.GetDirectories(history).Length == 106 && WorkspaceStorage.HistoryMaintenancePending(workspace), "Save queues maintenance without reading unrelated references or pruning");
        Check(WorkspaceProject.Open(session.Directory).Project.Difficulties[0].Document.Fruits[0].X == 200, "Save publishes while history is over its limit");
        try { WorkspaceStorage.Clean(workspace, compactLegacy: false); throw new Exception("Broken references accepted"); }
        catch (JsonException) { Check(Directory.Exists(pinned) && WorkspaceStorage.HistoryMaintenancePending(workspace), "Unreadable references preserve history and pending maintenance"); }
        File.Delete(broken);
        int checkpoints = 0;
        try { WorkspaceStorage.Clean(workspace, compactLegacy: false, shouldContinue: () => ++checkpoints < 15); throw new Exception("Maintenance did not yield"); }
        catch (OperationCanceledException) { Check(Directory.GetDirectories(history).Length == 106 && WorkspaceStorage.HistoryMaintenancePending(workspace), "Pausing a reference scan retains all rounds and its retry request"); }
        string receipt = Path.Combine(workspace, ".sync-history", "pending", "export.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receipt)!); File.WriteAllText(receipt, "{}");
        Check(WorkspaceStorage.Clean(workspace, compactLegacy: false).RecoveryPending && WorkspaceStorage.HistoryMaintenancePending(workspace), "Pending recovery defers queued maintenance");
        File.Delete(receipt);
        bool requestedDuringMaintenance = false;
        WorkspaceStorage.Clean(workspace, compactLegacy: false, shouldContinue: () =>
        {
            if (!requestedDuringMaintenance) { requestedDuringMaintenance = true; WorkspaceStorage.RequestHistoryMaintenance(session); }
            return true;
        });
        Check(WorkspaceStorage.HistoryMaintenancePending(workspace), "A newer request survives completion of an older maintenance pass");
        WorkspaceStorage.Clean(workspace, compactLegacy: false);
        Check(!WorkspaceStorage.HistoryMaintenancePending(workspace) && Directory.GetDirectories(history).Length <= 100 && Directory.Exists(pinned), "Idle maintenance enforces the limit and preserves referenced rounds");
        Check(WorkspaceVersionHistory.Read(session, WorkspaceVersionHistory.List(session).First()).Difficulties.Count == 1, "Newest saved recovery remains readable");
        Console.WriteLine("PASS deferred history maintenance, yielding, reference protection and recovery");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
