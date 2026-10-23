using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.Core;

internal static class HistorySavePerformance
{
    public static void Run(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root);
        var results = new List<object>();
        foreach (bool addDifficulty in new[] { true, false })
        {
            string workspace = Path.Combine(root, addDifficulty ? "added" : "edited");
            var project = new BeatmapProject { Name = "History save profile" };
            for (int i = 0; i < 6; i++)
            {
                var map = new MapDocument { Name = "History save profile", IsDemo = false };
                map.TimingPoints.Add(new() { BeatLengthMs = 500, Uninherited = true });
                for (int j = 0; j < 1200; j++) map.Fruits.Add(new() { TimeMs = j * 100, X = j % 512, SourceOrder = j });
                project.Difficulties.Add(new() { Name = "Difficulty " + i, Document = map });
            }
            var session = WorkspaceProject.Create(workspace, project, "");
            string references = Path.Combine(workspace, "references"); Directory.CreateDirectory(references);
            string metadata = JsonSerializer.Serialize(new { OriginalSourceLine = new string('a', 16000), Tags = "a/b.c" });
            Console.WriteLine($"Seeding {workspace}: six difficulties, 2500 unrelated references and over 100 history rounds.");
            for (int i = 0; i < 2500; i++) File.WriteAllText(Path.Combine(references, i + ".catchdiff"), metadata);
            var ui = new Ui(false); ui.View.LibrarySettings.Workspace = workspace;
            ui.View.LoadWorkspace(session); ui.Paint();
            for (int iteration = 0; iteration < 3; iteration++)
            {
                string history = Path.Combine(workspace, ".sync-history", session.Manifest.Id.ToString("N"));
                for (int i = 0; i < 120; i++)
                {
                    string round = Path.Combine(history, DateTime.UtcNow.AddMinutes(-300 + i).ToString("yyyyMMddTHHmmssfffffff") + "-save");
                    Directory.CreateDirectory(round); File.WriteAllText(Path.Combine(round, "saved.catchproj"), "{}");
                }
                if (addDifficulty) ui.View.AddDifficulty(difficultyName: "Added " + iteration);
                else ui.View.Document.Fruits[0].X = 200 + iteration;
                var expected = ui.View.CaptureProject();
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew(); ui.View.SaveCurrentDifficulty();
                double dispatchMs = watch.Elapsed.TotalMilliseconds;
                long dispatchBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                do
                {
                    ui.Paint();
                    if (watch.Elapsed > TimeSpan.FromSeconds(90)) throw new Exception("Save profile timed out");
                    if (ui.View.SynchronizationBusy) Thread.Sleep(1);
                } while (ui.View.SynchronizationBusy);
                double completedMs = watch.Elapsed.TotalMilliseconds;
                session = ui.View.WorkspaceSession!;
                var saved = WorkspaceProject.Open(session.Directory);
                if (saved.Project.Difficulties.Count != expected.Difficulties.Count
                    || !saved.Project.Difficulties.Zip(expected.Difficulties).All(p => p.First.Document.ContentEquals(p.Second.Document)))
                    throw new Exception("Save profile did not publish the expected project");
                var result = new { Scenario = addDifficulty ? "new-difficulty-save" : "modified-difficulty-save", Iteration = iteration, DispatchMs = dispatchMs, CompletedMs = completedMs, DispatchAllocatedBytes = dispatchBytes };
                results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
            }
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
