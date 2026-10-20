using System.Diagnostics;
using System.Runtime.CompilerServices;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class ProjectMemoryPerformance
{
    public static int Run(string folder)
    {
        var view = new EditorView(false);
        var canvas = new RecordingCanvas();
        Report("baseline");
        Open(view, folder);
        Report("loaded");
        var timer = Stopwatch.StartNew();
        do
        {
            canvas.Clear(); view.Render(canvas, 1440, 900);
            Thread.Sleep(20);
        } while (view.StarRatingsRefreshing && timer.Elapsed.TotalMinutes < 5);
        if (view.StarRatingsRefreshing) throw new TimeoutException("Difficulty ratings did not finish.");
        canvas.Clear(); view.Render(canvas, 1440, 900);
        Report("settled editing");
        view.ShowLibrary();
        canvas.Clear(); view.Render(canvas, 1440, 900);
        Report("returned to library");
        Collect(); Report("live library");
        GC.KeepAlive(view);
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Open(EditorView view, string folder)
    {
        var maps = Directory.GetFiles(folder, "*.osu").Order().Select(OsuBeatmapReader.ReadFile).ToArray();
        view.LoadProject(BeatmapProject.FromDocuments(maps));
        view.CloseLibrary();
    }

    private static void Collect() { System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect(2, GCCollectionMode.Aggressive, true, true); GC.WaitForPendingFinalizers(); GC.Collect(2, GCCollectionMode.Aggressive, true, true); }
    private static void Report(string phase)
    {
        using var p = Process.GetCurrentProcess();
        Console.WriteLine($"{phase}: working={p.WorkingSet64 / 1048576d:F1}, private={p.PrivateMemorySize64 / 1048576d:F1}, peak={p.PeakWorkingSet64 / 1048576d:F1}, managed={GC.GetTotalMemory(false) / 1048576d:F1} MiB");
    }
}
