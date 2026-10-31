using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class DenseMapPerformanceCheck
{
    internal static void Run(D2DCanvas canvas, string path, string? skinFolder, string? defaultArchive)
    {
        long allocated = GC.GetTotalAllocatedBytes();
        var clock = Stopwatch.StartNew();
        var map = OsuBeatmapReader.ReadFile(path);
        double readMs = clock.Elapsed.TotalMilliseconds;
        var view = new EditorView(false);
        if (skinFolder is not null || defaultArchive is not null)
        {
            if (skinFolder is not null) view.LibrarySettings.SelectedSkin = skinFolder;
            if (defaultArchive is not null) view.LibrarySettings.DefaultSkin = defaultArchive;
            view.InitializeSkin();
        }
        clock.Restart();
        view.LoadDocument(map);
        _ = view.Conversion;
        double loadMs = clock.Elapsed.TotalMilliseconds;
        var slider = map.ImportedSliders.MaxBy(s => s.ControlPoints.Count);
        double time = slider?.TimeMs ?? map.Fruits.FirstOrDefault()?.TimeMs ?? 0;
        var requestedTime = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--osu-performance-time=", StringComparison.Ordinal));
        if (requestedTime is not null) time = double.Parse(requestedTime.Split('=', 2)[1], System.Globalization.CultureInfo.InvariantCulture);
        view.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
        view.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
        canvas.Resize(2520, 1575, 168);
        var readyClock = Stopwatch.StartNew();
        Paint();
        while (view.StarRatingsRefreshing && readyClock.Elapsed.TotalSeconds < 60)
        {
            Paint(); Thread.Sleep(1);
        }
        if (view.StarRatingsRefreshing) throw new InvalidOperationException("Dense map ratings did not finish within the check budget.");
        double backgroundReadyMs = readyClock.Elapsed.TotalMilliseconds;
        var phases = new List<object>();
        foreach (bool standard in new[] { false, true })
        {
            if (view.IsOsuCanvas != standard) view.ToggleOsuCanvas();
            view.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
            for (int i = 0; i < 8; i++) Paint();
            foreach (string gesture in new[] { "pointer", "seek", "zoom", "playback" })
            {
                view.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
                var cpu = new List<double>(); var frames = new List<double>();
                var slowFrames = new List<object>();
                int frameCount = gesture == "playback" ? 1200 : 120;
                long phaseAllocated = GC.GetTotalAllocatedBytes(precise: true);
                long phaseThreadAllocated = GC.GetAllocatedBytesForCurrentThread();
                int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                using var phaseProcess = Process.GetCurrentProcess();
                TimeSpan phaseCpu = phaseProcess.TotalProcessorTime;
                var phaseClock = Stopwatch.StartNew();
                for (int i = 0; i < frameCount; i++)
                {
                    clock.Restart();
                    int rasterBuilds = canvas.SliderRasterBuildCount;
                    var plot = view.CanvasPlotBounds;
                    if (gesture == "pointer") view.PointerMove(plot.X + plot.Width * (.25f + i % 30 / 60f), plot.Y + plot.Height / 2, false, false);
                    else if (gesture == "seek") view.UpdateTransport(time + i % 60, map.DurationMs, true, false, false, null, map.AudioPath);
                    else if (gesture == "playback") view.UpdateTransport(time + i * 1000d / 60, map.DurationMs, true, true, false, null, map.AudioPath);
                    else if (standard) view.ZoomOsuCanvasAt(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, i % 2 == 0 ? 1.01 : 1 / 1.01);
                    else view.Wheel(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, i % 2 == 0 ? 1 : -1, true, true);
                    canvas.Begin(); view.Render(canvas, 1440, 900);
                    double render = clock.Elapsed.TotalMilliseconds;
                    canvas.End(lowLatency: true);
                    if (i >= 20)
                    {
                        double frameMs = clock.Elapsed.TotalMilliseconds;
                        cpu.Add(render); frames.Add(frameMs);
                        if (frameMs >= 16.667) slowFrames.Add(new { frame = i, frameMs, rasterBuilds = canvas.SliderRasterBuildCount - rasterBuilds });
                    }
                }
                cpu.Sort(); frames.Sort();
                int median = cpu.Count / 2, p95 = (int)(cpu.Count * .95);
                phaseProcess.Refresh();
                phases.Add(new { mode = standard ? "osu" : "catch", gesture, frameCount,
                    allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - phaseAllocated,
                    threadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - phaseThreadAllocated,
                    gen0Collections = GC.CollectionCount(0) - gen0, gen1Collections = GC.CollectionCount(1) - gen1,
                    gen2Collections = GC.CollectionCount(2) - gen2,
                    processCpuMs = (phaseProcess.TotalProcessorTime - phaseCpu).TotalMilliseconds,
                    elapsedMs = phaseClock.Elapsed.TotalMilliseconds,
                    workingMiB = phaseProcess.WorkingSet64 / 1048576d, privateMiB = phaseProcess.PrivateMemorySize64 / 1048576d,
                    managedMiB = GC.GetTotalMemory(false) / 1048576d,
                    cpuMedianMs = cpu[median], cpuP95Ms = cpu[p95], frameMedianMs = frames[median], frameP95Ms = frames[p95], frameMaxMs = frames[^1], slowFrames });
                AppLog.Write($"Dense map {(standard ? "osu" : "catch")} {gesture}: CPU p95={cpu[p95]:F2} ms; native frame p95={frames[p95]:F2} ms; max={frames[^1]:F2} ms.");
            }
        }
        if (view.IsDirty) throw new InvalidOperationException("Dense map interaction check changed content.");
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var report = new { path, timeMs = time, readMs, loadMs, backgroundReadyMs, dpi = 168, adapter = canvas.AdapterName,
            measurement = "CPU submission and DXGI Present; GPU completion and physical display latency are not measured. Phase allocations include diagnostic collection overhead and background workers.",
            skin = view.SkinName, workingMiB = process.WorkingSet64 / 1048576d,
            peakWorkingMiB = process.PeakWorkingSet64 / 1048576d, privateMiB = process.PrivateMemorySize64 / 1048576d,
            managedMiB = GC.GetTotalMemory(false) / 1048576d, allocatedMiB = (GC.GetTotalAllocatedBytes() - allocated) / 1048576d, phases };
        string output = Path.GetFullPath("artifacts/tests/dense-map-performance.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write($"Dense map native performance check completed. Peak working set={report.peakWorkingMiB:F1} MiB. Report={output}");
        void Paint() { canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(lowLatency: true); }
    }
}
