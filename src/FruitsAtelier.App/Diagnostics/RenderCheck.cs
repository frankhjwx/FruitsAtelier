using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class RenderCheck
{
    private static void CheckSongSetup(D2DCanvas canvas, EditorView view, int width, int height)
    {
        string language = FruitsAtelier.Localization.Strings.Language;
        var before = view.Document.DeepClone();
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                FruitsAtelier.Localization.Strings.SetLanguage(locale);
                void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
                void Click(float x, float y) { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint(); }
                Paint();
                var button = view.SongSetupButtonBounds;
                Click(button.X + 10, button.Y + 10);
                if (!view.SongSetupVisible) throw new InvalidOperationException("Song Setup did not open from the header.");
                var dialog = view.SongSetupBounds;
                for (int tab = 0; tab < 4; tab++)
                {
                    Click(dialog.X + 32 + tab * 140, dialog.Y + 65);
                    foreach (var field in view.SongSetupFieldBounds.Values)
                        if (field.X < dialog.X || field.Right > dialog.Right || field.Bottom > dialog.Bottom - 60)
                            throw new InvalidOperationException("Song Setup field exceeds its dialog bounds.");
                    if (tab == 2)
                    {
                        Click(dialog.X + 32, dialog.Y + 124);
                        view.PointerDown(dialog.X + 450, dialog.Y + 220, 0, false, false);
                        view.PointerMove(dialog.X + 550, dialog.Y + 270, false, false);
                        Paint();
                        view.PointerUp(dialog.X + 550, dialog.Y + 270, 0);
                        if (view.WantsCapture) throw new InvalidOperationException("Color picker retained pointer capture.");
                    }
                }
                view.KeyDown(27, false, false); Paint();
                if (view.SongSetupVisible || !before.ContentEquals(view.Document))
                    throw new InvalidOperationException("Cancelling Song Setup changed map content.");
            }
        }
        finally
        {
            FruitsAtelier.Localization.Strings.SetLanguage(language);
            canvas.Begin(); view.Render(canvas, width, height); canvas.End();
        }
    }

    private static object MeasureIndependentInput(nint window, double updatesPerSecond)
    {
        double now = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
        ConvertedCatchObject Note(double time) => new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, time, 256, 256, 256, 0);
        int catches = 0, ticks = 0;
        var session = new CatchTestplaySession(new CatchTestplay([Note(50), Note(10000)], 5, 0),
            new CatchTestplayClock(0, 1, now, false), 0, false, false, 37, 39, 16, TimeProvider.System, 5, [],
            _ => Interlocked.Increment(ref catches));
        var samples = new System.Collections.Concurrent.ConcurrentQueue<double>();
        var intervals = new System.Collections.Concurrent.ConcurrentQueue<double>();
        long previousTick = 0;
        using var input = new TestplayInputThread(window, session, () => throw new InvalidOperationException(), diagnostic: true, updatesPerSecond: updatesPerSecond);
        input.CheckKeyProcessed = samples.Enqueue;
        input.CheckTick = () =>
        {
            long at = Stopwatch.GetTimestamp();
            if (previousTick != 0) intervals.Enqueue((at - previousTick) * 1000d / Stopwatch.Frequency);
            previousTick = at;
            Interlocked.Increment(ref ticks);
        };
        // Deliberately do not pump the owner window: gameplay and sound callbacks must continue.
        Thread.Sleep(100);
        if (Volatile.Read(ref catches) != 1 || Volatile.Read(ref ticks) < 2)
            throw new InvalidOperationException("Gameplay waited for the blocked UI thread.");
        input.PostCheckKey(39, true);
        Thread.Sleep(40);
        input.PostCheckKey(39, false);
        Thread.Sleep(20);
        double stopped = session.X;
        Thread.Sleep(20);
        if (stopped <= 256 || Math.Abs(session.X - stopped) > .001)
            throw new InvalidOperationException("Independent input failed movement or release.");
        while (samples.TryDequeue(out _)) { }
        for (int i = 0; i < 200; i++) { input.PostCheckKey(16, i % 2 == 0); Thread.Sleep(2); }
        if (!SpinWait.SpinUntil(() => samples.Count == 200, 2000))
            throw new InvalidOperationException("Independent input lost queued transitions.");
        var ordered = samples.Order().ToArray();
        var steps = intervals.Order().ToArray();
        return new { targetHz = updatesPerSecond, samples = ordered.Length, medianMs = ordered[100], p95Ms = ordered[190], maxMs = ordered[^1],
            updateMedianMs = steps[steps.Length / 2], updateP95Ms = steps[(int)(steps.Length * .95)],
            ticks = Volatile.Read(ref ticks), caughtDuringUiStall = catches,
            note = "Synthetic messages to the dedicated input thread while the UI is blocked; excludes keyboard hardware and display latency." };
    }
    private static object MeasureTestplaySubmission(D2DCanvas canvas, EditorView view, nint window)
    {
        var project = view.CaptureProject();
        var sound = view.RequestHitsound;
        try
        {
            view.RequestHitsound = _ => { };
            var map = new MapDocument();
            map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 0 }, new Fruit { TimeMs = 60000, X = 512 }]);
            view.LoadDocument(map); view.CloseLibrary(); view.StartTestplay();
            var keyWatch = Stopwatch.StartNew();
            if (!Native.PostMessage(window, 0x0100, (nuint)view.LibrarySettings.TestplayRightKey, 0))
                throw new InvalidOperationException("Could not post testplay input.");
            canvas.WaitForFrameOrInput();
            if (!Native.PeekMessage(out var key, window, 0x0100, 0x0100, 1))
                throw new InvalidOperationException("Frame wait consumed or lost a key event.");
            Native.DispatchMessage(ref key);
            double queuedKeyDispatchMs = keyWatch.Elapsed.TotalMilliseconds;
            var frames = new List<double>();
            var total = Stopwatch.StartNew();
            while (frames.Count < 120 && total.Elapsed.TotalSeconds < 5)
            {
                if (!canvas.TryAcquireFrame())
                {
                    canvas.WaitForFrameOrInput();
                    while (Native.PeekMessage(out var pending, window, 0, 0, 1)) Native.DispatchMessage(ref pending);
                    continue;
                }
                var watch = Stopwatch.StartNew();
                canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(lowLatency: true);
                frames.Add(watch.Elapsed.TotalMilliseconds);
            }
            view.KeyUp(view.LibrarySettings.TestplayRightKey);
            if (frames.Count < 30) throw new InvalidOperationException("Insufficient low-latency frames for native check.");
            frames.Sort();
            return new { measuredFrames = frames.Count, queuedKeyDispatchMs,
                medianSubmissionMs = frames[frames.Count / 2], p95SubmissionMs = frames[(int)(frames.Count * .95)],
                note = "Hidden native window; injected Win32 key dispatch and CPU/GPU submission, not physical keyboard-to-display latency." };
        }
        finally { view.StopTestplay(); view.LoadProject(project); view.RequestHitsound = sound; }
    }

    private static void CheckTestplay(D2DCanvas canvas, EditorView view, nint window, int width, int height)
    {
        var project = view.CaptureProject();
        var toggle = view.RequestTogglePlayback; var pause = view.RequestPausePlayback;
        var seek = view.RequestSeek; var hitsound = view.RequestHitsound;
        var volumePreference = view.RequestAudioPreference;
        var updateCheck = view.RequestUpdateCheck;
        var updateStatus = view.UpdateStatus;
        int[] volumes = [view.LibrarySettings.MasterVolume, view.LibrarySettings.SongVolume, view.LibrarySettings.HitsoundVolume];
        int startupDelay = view.LibrarySettings.TestplayStartupDelaySeconds;
        string language = FruitsAtelier.Localization.Strings.Language;
        try
        {
            view.LibrarySettings.TestplayStartupDelaySeconds = 0;
            view.RequestTogglePlayback = () => { }; view.RequestPausePlayback = () => { };
            view.RequestSeek = _ => { }; view.RequestHitsound = _ => { };
            view.RequestAudioPreference = () => { };
            view.RequestUpdateCheck = () => { };
            var map = new MapDocument();
            map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 5000, X = 256 }]);
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                view.LoadDocument(map); view.CloseLibrary();
                FruitsAtelier.Localization.Strings.SetLanguage(locale);
                view.UpdateTransport(1000, 6000, true, false, false, null, null);
                view.KeyDown(116, false, false);
                view.UpdateTransport(1001, 6000, true, true, false, null, null);
                if (!view.IsTestplaying || view.TestplayCombo != 1) throw new InvalidOperationException("Native testplay failed to start or catch fruit.");
                view.KeyDown(9, false, false); view.KeyDown(9, false, false);
                if (!view.TestplayAutoplay) throw new InvalidOperationException("Held Tab failed to enable autoplay once.");
                view.KeyUp(9); view.KeyDown(9, false, false); view.KeyUp(9);
                if (view.TestplayAutoplay) throw new InvalidOperationException("Tab failed to restore manual control.");
                view.KeyDown(80, true, false); view.KeyUp(80);
                double pausedTime = view.PlayheadMs;
                view.UpdateTransport(pausedTime, 6000, true, false, false, null, null);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                if (!view.TestplayPaused || !view.IsTestplaying) throw new InvalidOperationException("Native testplay failed to pause.");
                view.KeyDown(80, true, false); view.KeyUp(80);
                view.UpdateTransport(pausedTime + 1, 6000, true, true, false, null, null);
                foreach (int key in new[] { view.LibrarySettings.TestplayLeftKey, view.LibrarySettings.TestplayRightKey })
                {
                    view.KeyDown(key, false, false);
                    view.UpdateTransport(view.PlayheadMs + 80, 6000, true, true, false, null, null);
                    canvas.Begin(); view.Render(canvas, width, height);
                    string image = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png");
                    if (!canvas.CatcherImage(image, new(20, 100, 64, 64), 0xFFFFFF, 1, false, key == view.LibrarySettings.TestplayLeftKey))
                        throw new InvalidOperationException("Mirrored native image failed to draw.");
                    canvas.End(lowLatency: true);
                    view.KeyUp(key);
                }
                view.KeyDown(27, false, false);
                if (view.IsTestplaying || view.PlayheadMs != 1000) throw new InvalidOperationException("Native testplay failed to return.");
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.KeyDown(27, false, false);
                if (view.LibraryVisible) throw new InvalidOperationException("Repeated testplay Escape left the editor.");
                view.KeyUp(27);
                view.StartTestplay();
                view.UpdateTransport(1400, 6000, true, true, false, null, null);
                double exitTime = view.PlayheadMs;
                view.KeyDown(113, false, false);
                if (view.IsTestplaying || Math.Abs(view.PlayheadMs - exitTime) > 100) throw new InvalidOperationException("Native F2 failed to retain position.");
                var streamMap = new MapDocument();
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 2000, X = 400 }]);
                streamMap.Tracks.Add(track);
                view.LoadDocument(streamMap); view.CloseLibrary();
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.KeyDown(65, true, false); view.KeyDown(70, true, true);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                if (!view.StreamDialogVisible) throw new InvalidOperationException("Native stream dialog did not open.");
                view.KeyDown(39, false, false); view.KeyDown(13, false, false);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                if (view.StreamDialogVisible || view.Document.Tracks[0].StreamSnapDivisor != 5)
                    throw new InvalidOperationException("Native stream confirmation failed.");
                view.UpdateTransport(1000, 6000, true, false, false, null, null);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                var field = view.PlayfieldBounds; var plot = view.CanvasPlotBounds;
                float headX = field.X + 100f / 512 * field.Width;
                float headY = plot.Bottom - (float)((1000 - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerDown(headX, headY, 0, false, false);
                Thread.Sleep(350);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                if (!view.SliderHoldNeedsRedraw || view.StreamConversionBounds.Width != 0)
                    throw new InvalidOperationException("Native slider hold did not show progress.");
                Thread.Sleep(700);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerUp(headX, headY, 0);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                if (view.StreamConversionBounds.Width == 0 || view.WantsCapture)
                    throw new InvalidOperationException("Native slider hold did not expose actions or release capture.");
                view.KeyDown(27, false, false);
                view.MarkSaved(); view.ShowLibrary();
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerDown(width - 160, 20, 0, false, false); view.PointerUp(width - 160, 20, 0);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerDown(40, 238, 0, false, false); view.PointerUp(40, 238, 0);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                foreach (int binding in new[] { 186, 222, 219, 221, 8, 17, 18, 96, 111, 121 })
                {
                    view.PointerDown(254, 200, 0, false, false); view.PointerUp(254, 200, 0);
                    if (!view.CapturingTestplayKey) throw new InvalidOperationException("Native binding capture did not open.");
                    var down = new Native.Message { Window = window, Id = binding is 18 or 121 ? 0x0104u : 0x0100u, WParam = (nuint)binding };
                    Native.DispatchMessage(ref down);
                    if (view.CapturingTestplayKey) throw new InvalidOperationException($"Native key {binding} did not bind.");
                    var up = new Native.Message { Window = window, Id = down.Id + 1, WParam = (nuint)binding };
                    Native.DispatchMessage(ref up);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                }
                view.KeyDown(27, false, false); view.LoadProject(project); view.CloseLibrary();
                view.OpenVolumeDialog();
                if (!view.VolumeDialogVisible) throw new InvalidOperationException("Native volume dialog did not open.");
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                for (int channel = 0; channel < 3; channel++)
                {
                    var bounds = view.VolumeSliderBounds(channel);
                    if (bounds.Bottom >= height) throw new InvalidOperationException("Volume control is outside the window.");
                    float x = bounds.X + bounds.Width * (channel + 1) / 4;
                    view.PointerDown(x, bounds.Y + 12, 0, false, false);
                    if (!view.WantsCapture) throw new InvalidOperationException("Volume slider did not capture.");
                    view.PointerUp(x, bounds.Y + 12, 0);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                }
                if (view.LibrarySettings.MasterVolume != 25 || view.LibrarySettings.SongVolume != 50 || view.LibrarySettings.HitsoundVolume != 75)
                    throw new InvalidOperationException("Native volume controls did not update percentages.");
                view.KeyDown(27, false, false);
                if (view.VolumeDialogVisible) throw new InvalidOperationException("Native volume dialog did not close.");
                var dsRatios = view.Document.DistanceSnapRatios.ToArray();
                view.Document.DistanceSnapRatios.Clear();
                view.Document.DistanceSnapRatios.AddRange([.75, 1.25, 2.5]);
                view.OpenDistanceSnapDialog();
                if (!view.DistanceSnapDialogVisible) throw new InvalidOperationException("Native distance snap dialog did not open.");
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                var dsDocument = view.Document.DeepClone();
                var dsPointer = view.DistanceSnapPointerBounds[0];
                var dsSlider = view.DistanceSnapTrackBounds;
                view.PointerDown(dsPointer.X + 8, dsPointer.Y + 8, 0, false, false);
                view.PointerMove(dsSlider.X + dsSlider.Width * .7f, dsSlider.Y + 16, true, false);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerUp(dsSlider.X + dsSlider.Width * .7f, dsSlider.Y + 16, 0);
                var dsPreview = view.DistanceSnapPreviewBounds;
                view.PointerDown(dsPreview.X + 40, dsPreview.Bottom - 12, 0, false, false);
                view.PointerUp(dsPreview.X + 40, dsPreview.Bottom - 12, 0);
                view.PointerDown(dsPreview.X + 70, dsPreview.Bottom - 40, 0, false, false);
                view.PointerUp(dsPreview.X + 70, dsPreview.Bottom - 40, 0);
                view.KeyDown(70, false, false); view.KeyDown(116, false, false);
                view.Wheel(width / 2, height / 2, -120, false);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.KeyDown(27, false, false);
                if (view.DistanceSnapDialogVisible || view.IsTestplaying || !dsDocument.ContentEquals(view.Document))
                    throw new InvalidOperationException("Native distance snap modal did not isolate input or close.");
                view.Document.DistanceSnapRatios.Clear(); view.Document.DistanceSnapRatios.AddRange(dsRatios);
                view.OpenSettings();
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerDown(40, 286, 0, false, false); view.PointerUp(40, 286, 0);
                foreach (var phase in new[] { UpdatePhase.Unsupported, UpdatePhase.Checking, UpdatePhase.Available, UpdatePhase.Downloading, UpdatePhase.Ready, UpdatePhase.Failed })
                {
                    view.UpdateStatus = new(phase, "0.8.2", 42);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                }
                view.KeyDown(27, false, false);
                view.KeyDown(27, false, false); view.CloseLibrary();
            }
        }
        finally
        {
            view.StopTestplay(); view.LoadProject(project); view.CloseLibrary();
            view.RequestTogglePlayback = toggle; view.RequestPausePlayback = pause;
            view.RequestSeek = seek; view.RequestHitsound = hitsound;
            view.RequestAudioPreference = volumePreference;
            view.RequestUpdateCheck = updateCheck;
            view.UpdateStatus = updateStatus;
            view.LibrarySettings.MasterVolume = volumes[0]; view.LibrarySettings.SongVolume = volumes[1]; view.LibrarySettings.HitsoundVolume = volumes[2];
            view.LibrarySettings.TestplayStartupDelaySeconds = startupDelay;
            view.ApplyAudioVolume();
            FruitsAtelier.Localization.Strings.SetLanguage(language);
        }
    }

    private readonly record struct ProfileFrame(double TimeMs, double TransportMs, double DrawingMs, double TotalMs,
        long AllocatedBytes, int Gen0, int Gen1, int Gen2, int ImageDecodes);
    internal static void ProfileMap(D2DCanvas canvas, EditorView view, string path, float dpi, double startMs)
    {
        if (!double.IsFinite(startMs) || startMs < 0) throw new ArgumentOutOfRangeException(nameof(startMs));
        view.RequestPreloadHitsounds = null; view.RequestStopHitsounds = null;
        view.RequestScheduleHitsound = (_, _) => { }; view.RequestPrepareHitsound = _ => { };
        var document = OsuBeatmapReader.ReadFile(path);
        var documents = new[] { document }.Concat(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.osu")
            .Where(file => !Path.GetFullPath(file).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            .Select(OsuBeatmapReader.ReadFile)).ToArray();
        var project = BeatmapProject.FromDocuments(documents);
        view.LoadWorkspace(new(Path.GetDirectoryName(path)!, new WorkspaceManifest { Name = project.Name }, project));
        var resourcesWatch = Stopwatch.StartNew();
        view.CheckWorkspaceResources();
        AppLog.Write($"Playback profile resource check: {resourcesWatch.Elapsed.TotalMilliseconds:F2}ms");
        canvas.Resize((int)(1440 * dpi / 96), (int)(900 * dpi / 96), dpi);
        void Draw() { canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(); }
        Draw();
        var cases = new List<object>();
        for (int mode = 0; mode < 3; mode++)
        {
            if (mode == 1)
            {
                var toggle = view.PreviewToggleBounds;
                view.PointerDown(toggle.X + 10, toggle.Y + 10, 0, false, false); view.PointerUp(toggle.X + 10, toggle.Y + 10, 0);
                Draw();
            }
            if (mode == 2)
            {
                float left = view.PreviewResizeBounds.X + 20, width = 1440 - left - 16;
                float x = left + 66 + 2 * (width - 66) / 3 + 12;
                view.PointerDown(x, 193, 0, false, false); view.PointerUp(x, 193, 0);
                Draw();
            }
            view.StartHitsounds(startMs);
            var frames = new List<double>(); var transport = new List<double>();
            var drawing = new List<double>();
            var samples = new List<ProfileFrame>(3620);
            var watch = new Stopwatch();
            for (int i = 0; i < 3620; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2), decodes = canvas.ImageDecodeCount;
                watch.Restart();
                view.UpdateTransport(startMs + i * 1000d / 120, document.DurationMs, true, true, false, null, document.AudioPath);
                double transportMs = watch.Elapsed.TotalMilliseconds;
                if (i >= 20) transport.Add(transportMs);
                watch.Restart(); canvas.Begin(); view.Render(canvas, 1440, 900);
                double drawingMs = watch.Elapsed.TotalMilliseconds;
                if (i >= 20) drawing.Add(drawingMs);
                canvas.End();
                double totalMs = watch.Elapsed.TotalMilliseconds;
                if (i >= 20) frames.Add(totalMs);
                samples.Add(new(startMs + i * 1000d / 120, transportMs, drawingMs, totalMs,
                    GC.GetAllocatedBytesForCurrentThread() - allocated, GC.CollectionCount(0) - gen0,
                    GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, canvas.ImageDecodeCount - decodes));
            }
            frames.Sort(); transport.Sort(); drawing.Sort();
            cases.Add(new { preview = mode == 0 ? "closed" : mode == 1 ? "NM" : "HR", ar = view.PreviewApproachRate,
                renderMedianMs = frames[1800], renderP95Ms = frames[3420], renderMaxMs = frames[^1],
                drawingMedianMs = drawing[1800], drawingP95Ms = drawing[3420],
                transportMedianMs = transport[1800], transportP95Ms = transport[3420], transportMaxMs = transport[^1], samples });
            AppLog.Write($"Playback profile case {mode} complete: drawing max {drawing[^1]:F2}ms");
        }
        var comparison = Stopwatch.StartNew();
        var snapshot = document.DeepClone();
        comparison.Restart();
        for (int i = 0; i < 100; i++) _ = document.ContentEquals(snapshot);
        double compareMs = comparison.Elapsed.TotalMilliseconds / 100;
        var report = new { map = Path.GetFileName(path), difficulties = documents.Length, timingPoints = document.TimingPoints.Count,
            sourceLines = document.OriginalSections.Sum(s => s.Lines.Count), compareMs,
            startMs, snap = view.SnapDivisor, zoom = view.CanvasZoom, dpi, adapter = canvas.AdapterName, cases,
            note = "Hidden Direct2D window with skin; render includes EndDraw/Present. Hitsound callbacks are silent and exclude device submission. This is not measured screen FPS." };
        string reportPath = Path.Combine(Path.GetDirectoryName(AppLog.Path)!, "playback-profile.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write($"Playback profile complete: {reportPath}");
    }

    private static void CheckDistanceFields(D2DCanvas canvas, EditorView view)
    {
        var original = view.CaptureProject();
        string language = FruitsAtelier.Localization.Strings.Language;
        try
        {
            foreach (string current in FruitsAtelier.Localization.Strings.AvailableLanguages)
            {
                FruitsAtelier.Localization.Strings.SetLanguage(current);
                var map = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, IsDemo = false };
                map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 1500, X = 240 }]);
                view.LoadDocument(map); view.CloseLibrary();
                canvas.Resize(1440, 900, 96);
                Paint();
                view.Wheel(view.CanvasPlotBounds.X, view.CanvasPlotBounds.Bottom, -2400, true);
                Paint();
                var field = view.PlayfieldBounds;
                float x = field.X + 240f / 512 * field.Width;
                float y = view.CanvasPlotBounds.Bottom - (float)((1500 - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                var input = view.PreviousDistanceFieldBounds ?? throw new InvalidOperationException("DS input missing.");
                view.PointerDown(input.X + 8, input.Y + 8, 0, false, false);
                view.PointerUp(input.X + 8, input.Y + 8, 0); Paint();
                view.KeyDown('A', true, false);
                view.TextInput('0'); view.TextInput('.'); view.TextInput('5'); Paint();
                if (Math.Abs(view.Document.Fruits[1].X - 170) > .001) throw new InvalidOperationException("DS preview did not move fruit before confirmation.");
                view.KeyDown(13, false, false); Paint();
                if (Math.Abs(view.Document.Fruits[1].X - 170) > .001) throw new InvalidOperationException("DS input did not move fruit.");
                view.KeyDown('Z', true, false); Paint();
                if (Math.Abs(view.Document.Fruits[1].X - 240) > .001) throw new InvalidOperationException("DS input undo failed.");
                view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                input = view.XCoordinateFieldBounds ?? throw new InvalidOperationException("X input missing.");
                double xEditPlayhead = view.PlayheadMs;
                view.PointerDown(input.X + 8, input.Y + 8, 0, false, false);
                view.PointerUp(input.X + 8, input.Y + 8, 0); Paint();
                if (!view.IsEditingText || view.WantsCapture || view.PlayheadMs != xEditPlayhead)
                    throw new InvalidOperationException("X row click reached the canvas.");
                view.KeyDown('A', true, false);
                view.TextInput('6'); view.TextInput('0'); view.TextInput('0'); Paint();
                if (view.DistanceSliderBounds is not null || Math.Abs(view.Document.Fruits[1].X - 512) > .001)
                    throw new InvalidOperationException("X input did not clamp without a slider.");
                view.KeyDown(13, false, false); Paint();
                view.KeyDown('Z', true, false); Paint();
                if (Math.Abs(view.Document.Fruits[1].X - 240) > .001) throw new InvalidOperationException("X input undo failed.");
            }
        }
        finally
        {
            FruitsAtelier.Localization.Strings.SetLanguage(language);
            view.LoadProject(original); view.CloseLibrary();
        }
        void Paint() { canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(); }
    }

    private static void CheckWorkspaceSave(D2DCanvas canvas, EditorView view)
    {
        var original = view.CaptureProject();
        string songs = view.LibrarySettings.Songs, language = FruitsAtelier.Localization.Strings.Language;
        try
        {
            view.LibrarySettings.Songs = Path.GetFullPath("artifacts/render-save-songs");
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                FruitsAtelier.Localization.Strings.SetLanguage(locale);
                view.NewProject(); view.CloseLibrary(); view.SaveCurrentDifficulty();
                if (!view.DiscardConfirmationVisible || view.IsDirty || view.WorkspaceSession is null)
                    throw new InvalidOperationException("Workspace save did not precede the Songs export offer.");
                canvas.Resize(size.Item1, size.Item2, 96);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                view.KeyDown(27, false, false);
                if (view.ExportVisible || view.DiscardConfirmationVisible)
                    throw new InvalidOperationException("Dismissing the Songs offer did not finish the workspace save.");
            }
        }
        finally
        {
            view.LibrarySettings.Songs = songs;
            FruitsAtelier.Localization.Strings.SetLanguage(language);
            view.LoadProject(original); view.CloseLibrary();
        }
    }

    internal static void Run(D2DCanvas canvas, EditorView view, nint window)
    {
        CheckWorkspaceSave(canvas, view);
        LibraryDropCheck.Run(view, window);
        CheckDistanceFields(canvas, view);
        string thumbnailPath = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png");
        var thumbnailWait = Stopwatch.StartNew();
        bool thumbnailReady = false;
        while (!thumbnailReady && thumbnailWait.Elapsed.TotalSeconds < 5)
        {
            canvas.Begin(); thumbnailReady = canvas.Thumbnail(thumbnailPath, new(20, 20, 76, 60)); canvas.End();
            if (!thumbnailReady) Thread.Sleep(10);
        }
        if (!thumbnailReady) throw new InvalidOperationException("Background thumbnail decoding did not produce a drawable bitmap.");
        var cases = new List<object>();
        var repeated = new ImportedSlider { TimeMs = 0, X = 100, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 3 };
        repeated.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
        view.Document.ImportedSliders.Add(repeated);
        view.Document.Fruits.AddRange([new Fruit { TimeMs = 10000, X = 50 }, new Fruit { TimeMs = 10100, X = 450 }]);
        foreach (int dpi in new[] { 96, 144, 192 })
        foreach (var size in new[] { (1440, 900), (980, 620) })
        {
            canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            CheckSongSetup(canvas, view, size.Item1, size.Item2);
            if (!view.MovementAnalysisEnabled)
            {
                view.PointerDown(235, 20, 0, false, false); view.PointerUp(235, 20, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                view.PointerDown(235, 329, 0, false, false);
                view.PointerUp(235, 329, 0);
                if (!view.MovementAnalysisEnabled) throw new InvalidOperationException("Movement analysis menu did not enable connections.");
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            }
            view.SetModifiers(true, false);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            var spacing = view.SnapSliderBounds;
            view.PointerDown(spacing.X + 30, spacing.Y + 12, 0, false, false);
            view.PointerUp(spacing.X + 30, spacing.Y + 12, 0);
            view.SetModifiers(false, false);
            view.KeyDown(90, true, false);
            foreach (int key in new[] { 81, 87, 69, 82, 84, 89, 76 })
            {
                view.KeyDown(key, false, false);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                view.KeyDown(key, false, false);
            }
            var toggle = view.PreviewToggleBounds;
            var timeDisplay = view.TimeDisplayBounds;
            view.PointerDown(timeDisplay.X + 10, timeDisplay.Y + 10, 0, false, false);
            view.PointerUp(timeDisplay.X + 10, timeDisplay.Y + 10, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PasteTimeJumpText("00:01:234", view.TimeJumpSession);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.KeyDown(13, false, false);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PointerDown(toggle.X + 10, toggle.Y + 10, 0, false, false); view.PointerUp(toggle.X + 10, toggle.Y + 10, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            var splitter = view.PreviewResizeBounds;
            view.PointerDown(splitter.X + 4, splitter.Y + 20, 0, false, false);
            view.PointerMove(splitter.X - 32, splitter.Y + 20, false, false);
            view.PointerUp(splitter.X - 32, splitter.Y + 20, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            float previewLeft = view.PreviewResizeBounds.X + 20, previewWidth = size.Item1 - previewLeft - 16;
            for (int mod = 0; mod < 3; mod++)
            {
                float modX = previewLeft + 66 + mod * (previewWidth - 66) / 3 + 12;
                view.PointerDown(modX, 193, 0, false, false); view.PointerUp(modX, 193, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            }
            for (int mode = 0; mode < 3; mode++)
            {
                float modeX = previewLeft + 66 + mode * (previewWidth - 66) / 3 + 12;
                view.PointerDown(modeX, 224, 0, false, false); view.PointerUp(modeX, 224, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            }
            foreach (double time in new[] { 10032d, 10080d, 10200d, 11500d, 10032d })
            {
                view.UpdateTransport(time, 15000, true, false, false, null, null);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            }
            toggle = view.PreviewToggleBounds;
            view.PointerDown(toggle.X + 10, toggle.Y + 10, 0, false, false); view.PointerUp(toggle.X + 10, toggle.Y + 10, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.UpdateTransport(10000, 15000, true, true, false, null, null);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            var plot = view.CanvasPlotBounds;
            double startMs = view.ViewStartMs;
            view.PointerDown(plot.X + 4, plot.Y + 10, 0, false, false);
            view.PointerMove(plot.Right - 12, plot.Bottom - 40, false, false);
            view.UpdateTransport(10100, 15000, true, true, false, null, null);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            if (Math.Abs(view.ViewStartMs - startMs - 100) > .001 || !view.AudioPlaying)
                throw new InvalidOperationException("Canvas marquee stopped following playback.");
            view.Wheel(plot.Right - 12, plot.Bottom - 40, -120, false);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            if (Math.Abs(view.PlayheadMs - 10500) > .001 || !view.WantsCapture)
                throw new InvalidOperationException("Marquee wheel navigation failed.");
            view.PointerMove(plot.Right - 12, plot.Y, false, false);
            if (!view.MarqueeScrollNeedsRedraw) throw new InvalidOperationException("Marquee edge did not request redraw.");
            double beforeScroll = view.PlayheadMs;
            Thread.Sleep(25);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            if (view.PlayheadMs <= beforeScroll || view.PlayheadMs - beforeScroll > 60 / view.PixelsPerMs + .001)
                throw new InvalidOperationException("Marquee edge scroll speed is invalid.");
            view.PointerUp(plot.Right - 12, plot.Y, 0);
            if (view.MarqueeScrollNeedsRedraw) throw new InvalidOperationException("Marquee edge scroll continued after release.");
            view.UpdateTransport(10100, 15000, true, false, false, null, null);
            view.PointerDown(235, 20, 0, false, false); view.PointerUp(235, 20, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PointerMove(270, 55, false, false);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.KeyDown(27, false, false);
            view.ShowError("bad-map.osu\n" + FruitsAtelier.Localization.Strings.Get("core.reader.importedParameters"));
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.KeyDown(13, false, false);
            view.ShowWorkspaceExport();
            string exportLanguage = FruitsAtelier.Localization.Strings.Language;
            foreach (string language in FruitsAtelier.Localization.Strings.AvailableLanguages)
            {
                FruitsAtelier.Localization.Strings.SetLanguage(language);
                for (int mode = 0; mode < 3; mode++)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                    view.KeyDown(40, false, false);
                }
            }
            FruitsAtelier.Localization.Strings.SetLanguage(exportLanguage);
            view.KeyDown(27, false, false);
            var editorProject = view.CaptureProject();
            view.MarkSaved(); view.ShowLibrary();
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PointerMove(size.Item1 - 400, 30, false, false);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PointerDown(size.Item1 - 230, 30, 0, false, false); view.PointerUp(size.Item1 - 230, 30, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.KeyDown(27, false, false); view.LoadProject(editorProject); view.CloseLibrary();
            CheckTestplay(canvas, view, window, size.Item1, size.Item2);
            cases.Add(new { dpi, widthDip = size.Item1, heightDip = size.Item2, rendered = true, previewDrawer = true, previewMods = true, reverseMarkers = true, gridSubmenu = true, errorDialog = true, exportOverlay = true, libraryNavigation = true, testplay = true });
        }
        canvas.Resize(0, 0, 96);
        canvas.Resize(1440, 900, 96);
        view.Document.Fruits.Clear();
        for (int i = 0; i < 1000; i++)
            view.Document.Fruits.Add(new Fruit { TimeMs = 100 + i * 5, X = 16 + i * 73 % 480 });
        var timings = new List<double>();
        for (int i = 0; i < 65; i++)
        {
            var timer = Stopwatch.StartNew();
            canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End();
            timer.Stop();
            if (i >= 5) timings.Add(timer.Elapsed.TotalMilliseconds);
        }
        timings.Sort();
        var testplaySubmission = MeasureTestplaySubmission(canvas, view, window);
        var testplayInput = new { baseline = MeasureIndependentInput(window, 1000), current = MeasureIndependentInput(window, 2000) };
        var report = new
        {
            adapter = canvas.AdapterName,
            skin = view.SkinName, decodedSkinImages = canvas.LoadedImageCount,
            note = "DPI values exercise render targets and DIP layout; not OS display-setting changes. Hidden-window timing includes EndDraw/Present and is not a visible-refresh guarantee.",
            cases, backgroundThumbnail = thumbnailReady, zeroSizeThenRestore = true, visibleFruitCount = 1000, measuredFrames = timings.Count,
            medianFrameMs = timings[timings.Count / 2], p95FrameMs = timings[(int)(timings.Count * .95)],
            testplaySubmission,
            testplayInput,
            modelErrors = CurveMath.Validate(view.Document)
        };
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppLog.Path)!, "render-check.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write($"Render check passed: {path}");
    }
}
