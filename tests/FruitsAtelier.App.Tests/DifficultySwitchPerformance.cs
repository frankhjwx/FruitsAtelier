using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
#if WINDOWS
using FruitsAtelier.App.Audio;
#endif

internal static class DifficultySwitchPerformance
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 120;
        public override long GetTimestamp() => ticks;
        public void Advance() => ticks++;
    }
    public static int Run(string path)
    {
        using var process = Process.GetCurrentProcess();
        var timer = Stopwatch.StartNew();
        var project = BeatmapArchive.OpenProject(path, "artifacts/difficulty-switch/maps");
        Console.WriteLine($"Import {project.Difficulties.Count} difficulties: {timer.Elapsed.TotalMilliseconds:F1} ms; audio files={project.Difficulties.Select(d => d.Document.AudioPath).Distinct().Count()}");
        var clock = new Clock();
        var view = new EditorView(false, clock);
        var canvas = new RecordingCanvas();
        view.LibrarySettings.TestplayStartupDelaySeconds = 0;
#if WINDOWS
        using var hitsounds = new HitsoundPlayer();
        view.RequestPreloadHitsounds = documents => hitsounds.PreloadProject(documents);
        view.RequestPrepareHitsound = hitsounds.Prepare;
        using var audio = new AudioTransport(0);
#endif
        for (int round = 0; round < 3; round++)
        {
            timer.Restart(); view.LoadProject(project); view.CloseLibrary();
            Console.WriteLine($"Round {round + 1} open/preload: {timer.Elapsed.TotalMilliseconds:F1} ms");
            var switches = new List<double>();
            var frames = new List<double>();
            long allocated = GC.GetTotalAllocatedBytes();
            int gen2 = GC.CollectionCount(2);
            int count = project.Difficulties.Count * 2;
            for (int i = 0; i < count; i++)
            {
                int index = i < project.Difficulties.Count ? i : count - 1 - i;
                timer.Restart();
                if (!view.SwitchDifficulty(index)) throw new Exception("Difficulty switch rejected.");
                canvas.Clear(); view.Render(canvas, 1440, 900);
                switches.Add(timer.Elapsed.TotalMilliseconds);
#if WINDOWS
                timer.Restart();
                if (!audio.LoadAsync(view.Document.AudioPath!).GetAwaiter().GetResult()) throw new Exception(audio.State.Error);
                process.Refresh();
                Console.WriteLine($"Round {round + 1} switch {i + 1}: editor={switches[^1]:F1} ms; MP3 load={timer.Elapsed.TotalMilliseconds:F1} ms; private={process.PrivateMemorySize64 / 1048576} MiB; heap={GC.GetTotalMemory(false) / 1048576} MiB");
#endif
                view.UpdateTransport((view.Conversion.Objects.FirstOrDefault()?.TimeMs ?? 0) + 5000,
                    view.Document.DurationMs, true, false, false, null, view.Document.AudioPath);
                view.UpdateTransport((view.Conversion.Objects.FirstOrDefault()?.TimeMs ?? 0) + 5000,
                    view.Document.DurationMs, true, false, false, null, view.Document.AudioPath);
                view.UpdateTransport(view.PlayheadMs, 0, false, false, false, null, view.Document.AudioPath);
                view.StartTestplay();
                if (!view.IsTestplaying) throw new Exception($"Testplay failed: {view.StatusMessage}; loading={view.AudioLoading}; conversion={view.Conversion.Success}; objects={view.Conversion.Objects.Count}; error={view.ErrorVisible}; library={view.LibraryVisible}; playhead={view.PlayheadMs}");
                view.KeyDown(9, false, false); view.KeyUp(9);
                for (int frame = 0; frame < 480; frame++)
                {
                    clock.Advance(); timer.Restart(); canvas.Clear(); view.Render(canvas, 1440, 900);
                    frames.Add(timer.Elapsed.TotalMilliseconds);
                }
                view.StopTestplay();
            }
            switches.Sort(); frames.Sort();
            Console.WriteLine($"Round {round + 1}: switch median={switches[count / 2]:F2} p95={switches[(int)(count * .95)]:F2} max={switches[^1]:F2} ms; testplay frame median={frames[frames.Count / 2]:F3} p95={frames[(int)(frames.Count * .95)]:F3} max={frames[^1]:F3} ms; allocated={(GC.GetTotalAllocatedBytes() - allocated) / 1048576} MiB; Gen2={GC.CollectionCount(2) - gen2}; live heap={GC.GetTotalMemory(true) / 1048576} MiB");
            var captured = view.CaptureProject();
            if (view.IsDirty || !project.Difficulties.Zip(captured.Difficulties).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Document.ContentEquals(pair.Second.Document)))
                throw new Exception("Difficulty switching changed project content.");
        }
        view.NewProject();
        return 0;
    }
}
