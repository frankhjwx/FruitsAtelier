#if WINDOWS
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using Vortice.Direct3D11;

internal static class MediaNativePerformance
{
    private sealed record Frame(double ElapsedMs, double PositionMs, double GapMs, double UpdateMs, double RenderMs, bool Submitted,
        int StoryboardSprites, int StoryboardDrawCalls, long StoryboardTextureBytes, bool MediaPending, double PlayheadMs, bool? TargetMatchesDisplay);
    private sealed record Memory(double WorkingBytes, double PrivateBytes, double ManagedBytes, double PeakWorkingBytes);
    private sealed record MemorySample(double ElapsedMs, Memory Memory);
    private sealed record Second(int Index, int CompletedGpuFrames, double CompletedGpuFramesPerSecond, double P95GapMs, double P99GapMs, double MaximumGapMs, Memory Memory);

    public static int Run(string path, string reportPath, double? durationMs, double startMs = 0, double? seekDurationMs = null)
    {
        Exception? failure = null;
        int result = 1;
        var thread = new Thread(() => { try { result = RunCore(path, reportPath, durationMs, startMs, seekDurationMs); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Native media profile failed", failure);
        return result;
    }

    private static int RunCore(string path, string reportPath, double? requestedDurationMs, double startMs, double? seekDurationMs)
    {
        path = Path.GetFullPath(path); reportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        if (requestedDurationMs is not { } fullDurationMs || !double.IsFinite(fullDurationMs) || fullDurationMs <= 0)
            throw new ArgumentException("Pass the full audio/storyboard duration explicitly; object duration does not cover storyboard-only maps.");
        var memorySamples = new ConcurrentQueue<MemorySample>();
        using var memoryCancellation = new CancellationTokenSource();
        var lifetimeClock = Stopwatch.StartNew();
        var memoryTask = Task.Run(async () =>
        {
            while (!memoryCancellation.IsCancellationRequested)
            {
                memorySamples.Enqueue(new(lifetimeClock.Elapsed.TotalMilliseconds, ReadMemory()));
                try { await Task.Delay(50, memoryCancellation.Token); } catch (OperationCanceledException) { break; }
            }
        });
        var document = OsuBeatmapReader.ReadFile(path);
        var project = BeatmapProject.FromDocuments([document]);
        var view = new EditorView(false);
        view.InitializeLibrary(false, new LibrarySettings
        {
            Workspace = Path.Combine(Path.GetDirectoryName(reportPath)!, "isolated-workspace"),
            FirstRunSetupCompleted = true, BackgroundDim = 0, MappingCanvasBackgroundDim = 0, ForceBackgroundDim = false
        });
        view.RequestViewPreference = null;
        view.RequestScheduleHitsound = (_, _) => { };
        view.RequestPrepareHitsound = _ => { };
        view.LoadWorkspace(new(Path.GetDirectoryName(path)!, new WorkspaceManifest { Name = project.Name }, project));
        view.CloseLibrary(); view.InitializeSkin();
        bool closed = false;
        Native.WindowProc procedure = (window, message, wParam, lParam) =>
        {
            if (message == 0x0010) { closed = true; return 0; }
            return Native.DefWindowProc(window, message, wParam, lParam);
        };
        string className = "FruitsAtelier.MediaNativePerformance." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className, Cursor = Native.LoadCursor(0, (nint)32512) };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        const int logicalWidth = 1440;
        int width = int.TryParse(Environment.GetEnvironmentVariable("FRUITSATELIER_MEDIA_PROFILE_WIDTH"), out int configuredWidth) ? configuredWidth : 2520;
        int height = int.TryParse(Environment.GetEnvironmentVariable("FRUITSATELIER_MEDIA_PROFILE_HEIGHT"), out int configuredHeight) ? configuredHeight : 1575;
        float canvasDpi = 96f * width / logicalWidth;
        float logicalHeight = height * 96f / canvasDpi;
        uint dpi = Native.GetDpiForSystem();
        var rectangle = new Native.Rectangle { Right = width, Bottom = height };
        Native.AdjustWindowRectExForDpi(ref rectangle, Native.WindowStyle, false, 0, dpi);
        nint hwnd = Native.CreateWindowEx(0, className, "Fruits Atelier — real-time media acceptance", Native.WindowStyle,
            -32000, -32000, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        var frames = new List<Frame>(); var seconds = new List<Second>();
        double durationMs = fullDurationMs;
        var clock = new Stopwatch();
        string adapter = "";
        Memory baseline = ReadMemory();
        bool complete = false;
        int sceneSprites = 0;
        bool? seekConverged = null;
        long? postSeekTextureBytes = null;
        bool openingCaptured = false;
        bool previewRequested = Environment.GetEnvironmentVariable("FRUITSATELIER_MEDIA_PROFILE_PREVIEW") == "1";
        bool testplayRequested = Environment.GetEnvironmentVariable("FRUITSATELIER_MEDIA_PROFILE_TESTPLAY") == "1";
        int testplayFrames = 0;
        try
        {
            using var canvas = new D2DCanvas(hwnd, width, height, canvasDpi);
            adapter = canvas.AdapterName;
            var device = (ID3D11Device)typeof(D2DCanvas).GetField("device", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            var gpu = (ID3D11DeviceContext)typeof(D2DCanvas).GetField("immediateContext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            using var gpuEvent = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            void CompleteGpu()
            {
                gpu.End(gpuEvent); gpu.Flush();
                long began = Stopwatch.GetTimestamp();
                while (gpu.GetData(gpuEvent, IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush).Code != 0)
                {
                    if (Stopwatch.GetElapsedTime(began).TotalSeconds > 5) throw new TimeoutException("GPU event did not complete.");
                    Thread.SpinWait(64);
                }
            }
            void PumpMessages()
            {
                while (Native.PeekMessage(out var message, 0, 0, 0, 1))
                { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
            }
            void DrawPaused()
            {
                PumpMessages();
                canvas.Begin(); view.Render(canvas, logicalWidth, logicalHeight);
                canvas.End(capture: !openingCaptured && view.LibraryOpening ? () =>
                {
                    SaveBitmap(canvas.CapturePixels(), Path.ChangeExtension(reportPath, ".opening.bmp")); openingCaptured = true;
                } : null);
                CompleteGpu();
            }
            var setup = Stopwatch.StartNew();
            do { DrawPaused(); } while ((view.StarRatingsRefreshing || StoryboardLoading(view) || view.LibraryOpening || canvas.MediaNeedsRedraw) && setup.Elapsed.TotalSeconds < 30 && !closed);
            if (view.StarRatingsRefreshing || StoryboardLoading(view) || view.LibraryOpening) throw new TimeoutException("Rating/storyboard setup did not settle.");
            if (previewRequested && !view.CatchPreviewVisible)
            {
                var toggle = view.PreviewToggleBounds;
                view.PointerDown(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2, 0, false, false);
                view.PointerUp(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2, 0); DrawPaused();
            }
            sceneSprites = (typeof(EditorView).GetField("storyboardScene", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as StoryboardScene)?.SpriteCount ?? 0;
            double transportDuration = Math.Max(document.DurationMs, Math.Max(startMs + durationMs, seekDurationMs ?? 0));
            view.UpdateTransport(0, transportDuration, true, false, false, null, document.AudioPath);
            view.UpdateTransport(startMs, transportDuration, true, false, false, null, document.AudioPath);
            var segmentWarmup = Stopwatch.StartNew();
            do { DrawPaused(); } while ((segmentWarmup.Elapsed.TotalMilliseconds < 100 || !DisplayMatches(view)) && segmentWarmup.Elapsed.TotalSeconds < 5);
            DrawPaused();
            if (testplayRequested)
            {
                view.LibrarySettings.TestplayStartupDelaySeconds = 0;
                var readyForTestplay = Stopwatch.StartNew();
                while ((view.LibraryOpening || view.ConversionNeedsRedraw || StoryboardLoading(view)) && readyForTestplay.Elapsed.TotalSeconds < 10) { DrawPaused(); Thread.Sleep(1); }
                if (view.TimingAlignmentPromptVisible) { view.AnswerTimingAlignment(false); DrawPaused(); }
                Console.WriteLine($"Native testplay request: opening={view.LibraryOpening}, aggregateDiscard={view.DiscardConfirmationVisible}, timingPrompt={view.TimingAlignmentPromptVisible}, editing={view.IsEditingText}, ready={view.AudioReady}, duration={view.AudioDurationMs}, position={view.PlayheadMs}, conversion={view.Conversion.Success}");
                view.StartTestplay();
                var testplayOpening = Stopwatch.StartNew();
                while (!view.IsTestplaying && testplayOpening.Elapsed.TotalSeconds < 5) { DrawPaused(); Thread.Sleep(1); }
                if (!view.IsTestplaying) throw new Exception($"Native testplay profile did not enter gameplay: project={view.HasEditorProject}, opening={view.LibraryOpening}, error={view.ErrorVisible}, audioLoading={view.AudioLoading}, conversion={view.ConversionNeedsRedraw}, status={view.StatusMessage}");
                view.KeyDown(9, false, false); view.KeyUp(9);
            }
            baseline = ReadMemory();
            view.StartHitsounds(startMs);
            clock.Start();
            double previousMs = 0;
            double nextFrameMs = 0;
            double lastPosition = startMs;
            double[] seekFractions = [.85, .1, .6, .02, .95, .35, .75, .15];
            int secondStartFrame = 0;
            while (!closed && clock.Elapsed.TotalMilliseconds < durationMs)
            {
                PumpMessages();
                while (clock.Elapsed.TotalMilliseconds < nextFrameMs)
                { if (nextFrameMs - clock.Elapsed.TotalMilliseconds > 2) Thread.Sleep(1); else Thread.SpinWait(64); }
                double elapsedMs = clock.Elapsed.TotalMilliseconds;
                double now = seekDurationMs is { } seekLength
                    ? seekFractions[(int)(elapsedMs / 80) % 8] * seekLength
                    : startMs + elapsedMs;
                lastPosition = now;
                long updateStart = Stopwatch.GetTimestamp();
                view.UpdateTransport(now, transportDuration, true, true, false, null, document.AudioPath);
                if (seekDurationMs is not null && Math.Abs(view.PlayheadMs - now) > 1) throw new Exception("Media seek delayed the editor playhead.");
                double updateMs = Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds;
                long renderStart = Stopwatch.GetTimestamp();
                canvas.Begin(); view.Render(canvas, logicalWidth, logicalHeight); bool submitted = canvas.End();
                if (view.IsTestplaying) testplayFrames++;
                CompleteGpu();
                double completedMs = clock.Elapsed.TotalMilliseconds;
                nextFrameMs = Math.Max(nextFrameMs + 1000d / 120, completedMs);
                frames.Add(new(completedMs, now, completedMs - previousMs, updateMs, Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds, submitted,
                    canvas.StoryboardSprites, canvas.StoryboardDrawCalls, canvas.StoryboardTextureBytes, canvas.MediaNeedsRedraw,
                    view.PlayheadMs, DisplayMatches(view)));
                if (canvas.StoryboardTextureBytes > 512L * 1024 * 1024) throw new Exception("Storyboard textures exceeded their transient seek budget.");
                previousMs = completedMs;
                if (completedMs >= (seconds.Count + 1) * 1000)
                {
                    var interval = frames.Skip(secondStartFrame).ToArray();
                    double fromMs = secondStartFrame == 0 ? 0 : frames[secondStartFrame - 1].ElapsedMs;
                    double elapsed = completedMs - fromMs;
                    var gaps = interval.Select(f => f.GapMs).Order().ToArray();
                    var sample = new Second(seconds.Count, interval.Length, interval.Length * 1000 / elapsed,
                        Percentile(gaps, .95), Percentile(gaps, .99), gaps[^1], ReadMemory());
                    seconds.Add(sample); secondStartFrame = frames.Count;
                    Console.WriteLine($"{Path.GetFileName(path)} {completedMs / 1000:F1}/{durationMs / 1000:F1}s: {sample.CompletedGpuFramesPerSecond:F1} completed GPU frames/s (hidden), p99 gap {sample.P99GapMs:F2}ms, private {sample.Memory.PrivateBytes / 1048576:F1}MiB");
                }
            }
            clock.Stop(); complete = !closed && clock.Elapsed.TotalMilliseconds >= durationMs;
            if (testplayRequested && view.IsTestplaying)
            {
                canvas.Begin(); view.Render(canvas, logicalWidth, logicalHeight);
                canvas.End(capture: () => SaveBitmap(canvas.CapturePixels(), Path.ChangeExtension(reportPath, ".testplay.bmp")));
            }
            if (seekDurationMs is not null)
            {
                view.UpdateTransport(lastPosition, transportDuration, true, false, false, null, document.AudioPath);
                var convergence = Stopwatch.StartNew();
                do { DrawPaused(); } while (!DisplayMatches(view) && convergence.Elapsed.TotalSeconds < 5);
                seekConverged = DisplayMatches(view); postSeekTextureBytes = canvas.StoryboardTextureBytes;
                if (seekConverged != true || postSeekTextureBytes > 256L * 1024 * 1024) throw new Exception("Latest seek did not converge and release old texture pins.");
            }
            view.UpdateTransport(seekDurationMs is null ? startMs + durationMs / 2 : lastPosition, transportDuration, true, false, false, null, document.AudioPath);
            var snapshotWarmup = Stopwatch.StartNew();
            do { DrawPaused(); } while (snapshotWarmup.Elapsed.TotalMilliseconds < 500 || canvas.MediaNeedsRedraw && snapshotWarmup.Elapsed.TotalSeconds < 5);
            canvas.Begin(); view.Render(canvas, logicalWidth, logicalHeight);
            canvas.End(capture: () => SaveBitmap(canvas.CapturePixels(), Path.ChangeExtension(reportPath, ".bmp")));
        }
        finally
        {
            memoryCancellation.Cancel(); memoryTask.GetAwaiter().GetResult();
            memorySamples.Enqueue(new(lifetimeClock.Elapsed.TotalMilliseconds, ReadMemory()));
            view.StopFileMonitoring(); view.StopMapsetVerifier(); view.StopDiscussions();
            view.CompleteLibraryCacheWrites();
            Native.DestroyWindow(hwnd);
            GC.KeepAlive(procedure);
            var gaps = frames.Select(f => f.GapMs).Order().ToArray();
            var report = new
            {
                path, startMs, seekDurationMs, requestedDurationMs = durationMs, elapsedMs = clock.Elapsed.TotalMilliseconds, complete, previewRequested, testplayRequested, testplayFrames,
                openingCaptured,
                visible = false, clientWidth = width, clientHeight = height, logicalWidth, logicalHeight, canvasDpi, systemDpi = dpi, adapter,
                mappingMediaBounds = MappingMediaBounds(view), previewViewport = view.PreviewViewport,
                processId = Environment.ProcessId, runtime = Environment.Version.ToString(), baseline,
                completedGpuFrames = frames.Count,
                completedGpuFramesPerSecond = frames.Count * 1000 / Math.Max(1, clock.Elapsed.TotalMilliseconds),
                physicalPresentationFps = (double?)null,
                p95GapMs = Percentile(gaps, .95), p99GapMs = Percentile(gaps, .99), maximumGapMs = gaps.LastOrDefault(),
                renderAndGpuCompletionP95Ms = Percentile(frames.Select(f => f.RenderMs).Order().ToArray(), .95),
                renderAndGpuCompletionP99Ms = Percentile(frames.Select(f => f.RenderMs).Order().ToArray(), .99),
                renderAndGpuCompletionMaximumMs = frames.Select(f => f.RenderMs).DefaultIfEmpty(0).Max(),
                steadySecondMinimumCompletedGpuFrames = seconds.Where(s => s.Index >= 2).Select(s => s.CompletedGpuFramesPerSecond).DefaultIfEmpty(0).Min(),
                peakWorkingBytes = memorySamples.Max(s => Math.Max(s.Memory.WorkingBytes, s.Memory.PeakWorkingBytes)),
                peakPrivateBytes = memorySamples.Max(s => s.Memory.PrivateBytes),
                peakManagedBytes = memorySamples.Max(s => s.Memory.ManagedBytes),
                memorySampleIntervalMs = 50,
                tailFrameCount = frames.Count - seconds.Sum(s => s.CompletedGpuFrames),
                backgroundDim = view.LibrarySettings.BackgroundDim, mappingCanvasBackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim,
                forceBackgroundDim = view.LibrarySettings.ForceBackgroundDim,
                sceneSprites, maximumDrawnSprites = frames.Select(f => f.StoryboardSprites).DefaultIfEmpty(0).Max(),
                maximumDrawCalls = frames.Select(f => f.StoryboardDrawCalls).DefaultIfEmpty(0).Max(),
                peakStoryboardTextureBytes = frames.Select(f => f.StoryboardTextureBytes).DefaultIfEmpty(0).Max(),
                seekConverged, postSeekTextureBytes,
                note = "Hidden offscreen Win32 target with real EditorView and Direct2D. Every rendered frame waits for an ID3D11 event query after EndDraw/Present and Flush, proving GPU completion independently of occluded Present status. Requests are paced at 120 Hz in wall time. This is native rendering throughput and CPU+GPU completion cost, not physical or visible presentation FPS. Audio callbacks are silent; EditorWindow input dispatch and WASAPI scheduling are tested separately.",
                seconds, frames, memorySamples
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return complete ? 0 : 1;
    }

    private static bool StoryboardLoading(EditorView view) => typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) is Task;
    private static Rect MappingMediaBounds(EditorView view)
    {
        var left = (Rect)typeof(EditorView).GetField("leftPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var canvas = (Rect)typeof(EditorView).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        return new(left.Right, canvas.Y + 38, canvas.Right - left.Right, view.CanvasPlotBounds.Bottom - canvas.Y - 38);
    }
    private static bool DisplayMatches(EditorView view)
    {
        var target = typeof(EditorView).GetField("storyboardFrame", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as List<StoryboardSpriteState>;
        var display = typeof(EditorView).GetField("storyboardDisplayFrame", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as List<StoryboardSpriteState>;
        return target is not null && display is not null && target.SequenceEqual(display);
    }

    private static void SaveBitmap((int Width, int Height, byte[] Pixels) frame, string path)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
    }

    private static double Percentile(double[] sorted, double value) => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * value) - 1, 0, sorted.Length - 1)];
    private static Memory ReadMemory()
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        return new(process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false), process.PeakWorkingSet64);
    }
}
#else
internal static class MediaNativePerformance
{
    public static int Run(string path, string reportPath, double? durationMs, double startMs = 0, double? seekDurationMs = null) => throw new PlatformNotSupportedException("Native media profiling requires Windows.");
}
#endif
