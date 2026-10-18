using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.Core;

internal static class DifficultySavePerformance
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/difficulty-save-performance", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var results = new List<object>();
        foreach (int count in new[] { 1, 6 })
        {
            var project = new BeatmapProject { Name = "Save performance" };
            for (int index = 0; index < count; index++)
            {
                var map = new MapDocument { Name = "Save performance", DurationMs = 200000 };
                map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
                for (int i = 0; i < 3000; i++) map.Fruits.Add(new() { TimeMs = i * 60, X = i % 512, SourceOrder = i });
                for (int i = 0; i < 100; i++)
                {
                    var slider = new ImportedSlider { TimeMs = i * 1800, X = 100, Y = 192, PixelLength = 500, SpanCount = 2, PathType = 'B', SourceOrder = 3000 + i };
                    for (int p = 0; p < 128; p++) slider.ControlPoints.Add(new(100 + p * 2, 192 + p % 12));
                    map.ImportedSliders.Add(slider);
                }
                map.BananaShowers.Add(new() { TimeMs = 195000, EndTimeMs = 198000 });
                project.Difficulties.Add(new() { Name = $"Diff {index}", Document = map });
            }
            foreach (bool incremental in new[] { false, true })
            {
                Console.WriteLine($"Measuring {(incremental ? "current difficulty" : "whole project")} save: {count} difficulties, 3000 fruits and 100 repeated 128-point sliders per difficulty.");
                var session = WorkspaceProject.Create(Path.Combine(root, $"{count}-{incremental}"), project, "");
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    var edited = project.Difficulties[0].Document.DeepClone(); edited.Fruits[0].X = iteration + 10;
                    var candidate = new BeatmapProject { Name = project.Name, Difficulties = project.Difficulties.Select(d => d.Id == project.Difficulties[0].Id
                        ? new ProjectDifficulty { Id = d.Id, Name = d.Name, Document = edited } : d).ToList() };
                    long allocation = GC.GetAllocatedBytesForCurrentThread();
                    var timer = Stopwatch.StartNew();
                    if (incremental) session = WorkspaceProject.SaveDifficulty(session, candidate.Difficulties[0].Id, edited, candidate.Difficulties[0].Name);
                    else WorkspaceProject.Save(session, candidate);
                    timer.Stop(); allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
                    var restored = WorkspaceProject.Open(session.Directory);
                    if (!restored.Project.Difficulties.Zip(candidate.Difficulties).All(pair => pair.First.Document.ContentEquals(pair.Second.Document)))
                        throw new Exception("Save performance fixture changed authoring content.");
                    results.Add(new { Difficulties = count, Incremental = incremental, Iteration = iteration, Milliseconds = timer.Elapsed.TotalMilliseconds, AllocatedBytes = allocation });
                    Console.WriteLine($"  iteration={iteration}, elapsed={timer.Elapsed.TotalMilliseconds:F2}ms, allocation={allocation:N0} bytes");
                }
            }
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Performance report: {Path.Combine(root, "results.json")}");
    }
}
