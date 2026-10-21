using System.Diagnostics;
using FruitsAtelier.Core;
#if WINDOWS
using FruitsAtelier.App.Audio;
#endif

internal static class NewDifficultyPerformance
{
    public static int Run(string directory, string name)
    {
#if WINDOWS
        int result = 0;
        Exception? failure = null;
        var thread = new Thread(() => { try { result = RunCore(directory, name); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Copy profile failed", failure);
        return result;
#else
        return RunCore(directory, name);
#endif
    }

    private static int RunCore(string directory, string name)
    {
        var manifest = Measure("Read manifest", () => WorkspaceProject.ReadManifest(directory));
        var project = new BeatmapProject { Name = manifest.Name, Notes = manifest.Notes,
            Difficulties = manifest.Difficulties.Select(e => new ProjectDifficulty { Id = e.Id, Name = e.Name,
                FaOnly = e.FaOnly, Document = ProjectSerializer.ReadFile(Path.Combine(directory, e.File)) }).ToList() };
        int index = project.Difficulties.FindIndex(d => d.Name == name);
        if (index < 0) throw new ArgumentException("Source difficulty not found: " + name);
        var settings = LibrarySettings.Load();
        var ui = new Ui(false);
#if WINDOWS
        using var hitsounds = new HitsoundPlayer();
        ui.View.RequestPreloadHitsounds = documents => Measure("Preload project hitsounds", () =>
        {
            hitsounds.PreloadProject(documents, new[] { settings.SelectedSkin }.OfType<string>());
            return true;
        });
#endif
        ui.View.LibrarySettings.Workspace = settings.Workspace;
        ui.View.LibrarySettings.OsuRoot = settings.OsuRoot;
        Measure("Load project", () => { ui.View.LoadProject(project); return true; });
        ui.View.WorkspaceSession = new(directory, manifest, project);
        Measure("Switch source", () => ui.View.SwitchDifficulty(index));
        Measure("Source frame", () => { ui.Paint(); return true; });
#if WINDOWS
        var audio = new AudioTransport(0);
        try
        {
            if (!string.IsNullOrWhiteSpace(ui.View.Document.AudioPath))
            {
                Measure("Source audio load", () => audio.LoadAsync(ui.View.Document.AudioPath).GetAwaiter().GetResult());
                Measure("Start silent audio", () => { audio.Play(); audio.WaitForCommandsAsync().GetAwaiter().GetResult(); return true; });
                Thread.Sleep(100);
                Measure("Pause silent audio", () => { audio.Pause(); audio.WaitForCommandsAsync().GetAwaiter().GetResult(); return true; });
            }
            ui.View.RequestDifficultyChanged = () =>
            {
                Measure("Retire source audio", () => { audio.Dispose(); return true; });
                audio = new AudioTransport(0);
                if (!string.IsNullOrWhiteSpace(ui.View.Document.AudioPath))
                    Measure("Queue copied audio", () => { audio.Load(ui.View.Document.AudioPath); audio.Seek(ui.View.PlayheadMs); return true; });
            };
#endif
            Measure("Copy difficulty", () => ui.View.AddDifficulty(copyFrom: index, difficultyName: "Profile copy " + Guid.NewGuid().ToString("N")));
            Measure("Copied frame", () => { ui.Paint(); return true; });
            var snapshot = Measure("Capture project", ui.View.CaptureProject);
            if (!string.IsNullOrWhiteSpace(settings.Songs))
                Measure("Export plan (no writes)", () => WorkspaceExport.Plan(ui.View.WorkspaceSession!, snapshot.Difficulties[ui.View.ActiveDifficultyIndex],
                    settings.Songs, false, ui.View.CurrentDifficultyName, ui.View.CompensateTinyDroplets));
#if WINDOWS
        }
        finally { audio.Dispose(); }
#endif
        return 0;
    }

    private static T Measure<T>(string label, Func<T> action)
    {
        Console.WriteLine("Starting " + label);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var result = action();
        Console.WriteLine($"{label}: {watch.Elapsed.TotalMilliseconds:F2} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 1048576d:F2} MiB allocated");
        return result;
    }
}