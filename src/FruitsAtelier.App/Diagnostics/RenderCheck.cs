using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class RenderCheck
{
    private static void CheckPaletteHints(D2DCanvas canvas, EditorView view, int width, int height)
    {
        string language = FruitsAtelier.Localization.Strings.Language;
        var original = view.Document.DeepClone();
        bool dirty = view.IsDirty;
        void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                FruitsAtelier.Localization.Strings.SetLanguage(locale); Paint();
                foreach (var button in view.ToolButtonBounds.ToArray())
                {
                    view.PointerMove(button.X + button.Width / 2, button.Y + button.Height / 2, false, false); Paint();
                }
                var last = view.AssistButtonBounds[^1];
                view.Wheel(last.X + 10, view.CanvasPlotBounds.Bottom - 10, -1200, false, false); Paint();
                foreach (int index in new[] { 5, 6 })
                {
                    var button = view.AssistButtonBounds[index];
                    view.PointerMove(button.X + 10, button.Y + 10, false, false); Paint();
                    if (index != 6) continue;
                    bool locked = view.DropletSelectionLocked;
                    for (int i = 0; i < 2; i++)
                    {
                        view.PointerMove(button.X - 20, button.Y + 16, false, false); Paint();
                        view.PointerDown(button.X - 20, button.Y + 16, 0, false, false);
                        view.PointerUp(button.X - 20, button.Y + 16, 0); Paint();
                        if (view.DropletSelectionLocked != (i == 0 ? !locked : locked))
                            throw new InvalidOperationException("Native droplet lock flyout did not toggle.");
                    }
                }
            }
            if (!original.ContentEquals(view.Document) || dirty != view.IsDirty)
                throw new InvalidOperationException("Palette hints or droplet locking changed the document.");
        }
        finally { FruitsAtelier.Localization.Strings.SetLanguage(language); view.PointerMove(0, 0, false, false); }
    }

    private static void CheckBookmarkCache(D2DCanvas canvas)
    {
        string toolbar = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "bookmarks", "toolbar-panel.png");
        string scene = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "assist", "clap.png");
        canvas.Begin();
        if (!canvas.Image(toolbar, new(0, 0, 246, 34))) throw new InvalidOperationException("Toolbar texture unavailable.");
        for (uint tint = 0; tint < 14; tint++)
            if (!canvas.Image(scene, new(0, 40, 32, 32), 0xFFFF00 + tint)) throw new InvalidOperationException("Cache pressure fixture unavailable.");
        int decodes = canvas.ImageDecodeCount;
        canvas.Image(toolbar, new(0, 0, 246, 34));
        canvas.End();
        if (canvas.ImageDecodeCount != decodes)
            throw new InvalidOperationException("Scene cache pressure evicted the independent bookmark toolbar.");
    }

    private static void CheckAnimatedTextResources(D2DCanvas canvas)
    {
        for (int batch = 0; batch < 16; batch++)
        {
            canvas.Begin();
            for (int i = 0; i < 128; i++)
            {
                int frame = batch * 128 + i;
                float size = 12 + frame * .007f;
                float width = canvas.MeasureText("123", size, true);
                canvas.Text("123", 10, 10, size, 0x800000u + (uint)frame, width + 10, true);
            }
            canvas.End();
            if (canvas.CachedTextFormatCount > D2DCanvas.TextFormatLimit)
                throw new InvalidOperationException("Animated text retained unbounded native formats.");
        }
        AppLog.Write($"Animated text resource check: 2048 sizes, retained formats={canvas.CachedTextFormatCount}");
    }
    private static void CheckTimingSetup(D2DCanvas canvas, EditorView view, int width, int height)
    {
        string language = FruitsAtelier.Localization.Strings.Language;
        string? audioPath = view.Document.AudioPath;
        var decoder = view.RequestWaveform;
        view.Document.AudioPath = "render-waveform.wav";
        var peaks = Enumerable.Range(0, 12000).Select(i => (float)(.15 + .65 * Math.Abs(Math.Sin(i * .007)))).ToArray();
        view.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(peaks, 1));
        var original = view.Document.DeepClone();
        void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
        try
        {
            foreach (string lang in new[] { "en", "zh-CN" })
            {
                FruitsAtelier.Localization.Strings.SetLanguage(lang); Paint();
                view.KeyDown(114, false, false); Paint();
                if (!view.TimingPageVisible) throw new InvalidOperationException("F3 failed to open timing page.");
                var svRow = view.TimingSliderMultiplierBounds;
                if (svRow.Bottom > height || view.TimingFields.Any(f => f.Key == "page.sliderMultiplier"))
                    throw new InvalidOperationException("Base SV must start locked and fit the Timing panel.");
                view.PointerDown(svRow.X + 8, svRow.Y + 10, 0, false, false);
                view.PointerUp(svRow.X + 8, svRow.Y + 10, 0); Paint();
                var svField = view.TimingFields.Single(f => f.Key == "page.sliderMultiplier");
                if (!view.Document.OverrideSliderMultiplier || svField.Bounds.Bottom != svRow.Bottom)
                    throw new InvalidOperationException("Base SV override did not unlock its row.");
                double previousSv = view.Document.EffectiveSliderMultiplier;
                view.PointerDown(svField.Bounds.Right + 14, svField.Bounds.Y + 10, 0, false, true);
                view.PointerUp(svField.Bounds.Right + 14, svField.Bounds.Y + 10, 0); Paint();
                var svWait = Stopwatch.StartNew();
                while (view.SliderMultiplierValidationBusy && svWait.ElapsedMilliseconds < 10000)
                { Thread.Sleep(1); Paint(); }
                if (Math.Abs(view.Document.EffectiveSliderMultiplier - Math.Round(previousSv + .01, 2)) > 1e-9)
                    throw new InvalidOperationException($"Base SV Ctrl arrow did not step by 0.01: {previousSv} -> {view.Document.EffectiveSliderMultiplier}; {view.StatusMessage}");
                view.KeyDown(90, true, false); view.KeyDown(90, true, false); Paint();
                if (!view.Document.ContentEquals(original))
                    throw new InvalidOperationException("Base SV native undo did not restore the difficulty.");
                AppLog.Write($"Timing base SV native check passed: {lang}, {width}x{height}");
                view.PointerMove(350, height - 55, false, false); Paint();
                int decodes = canvas.ImageDecodeCount;
                for (int frame = 0; frame < 12; frame++)
                {
                    view.UpdateTransport(1000 + frame * 10, 10000, true, true, false, null, view.Document.AudioPath);
                    Paint();
                }
                if (canvas.ImageDecodeCount != decodes)
                    throw new InvalidOperationException("Hover playback repeatedly decodes cached images.");
                view.UpdateTransport(1000, 10000, true, false, false, null, null);
                view.KeyDown(117, false, false); Paint();
                if (!view.TimingSetupVisible) throw new InvalidOperationException("F6 failed to open timing setup.");
                var r = view.TimingSetupBounds;
                float tabWidth = Math.Clamp(r.Width * .39f, 280, 360) / 3;
                for (int tab = 0; tab < 3; tab++)
                {
                    view.PointerDown(r.X + 22 + tab * tabWidth, r.Y + 65, 0, false, false);
                    view.PointerUp(r.X + 22 + tab * tabWidth, r.Y + 65, 0); Paint();
                    if (view.TimingFields.Any(f => f.Bounds.Bottom > r.Bottom - 188))
                        throw new InvalidOperationException("Timing properties overlap apply options.");
                }
                view.KeyDown(27, false, false); view.KeyDown(112, false, false); Paint();
                if (view.TimingSetupVisible || view.TimingPageVisible || !view.Document.ContentEquals(original))
                    throw new InvalidOperationException("Timing navigation or Cancel changed the map.");
            }
        }
        finally
        {
            view.Document.AudioPath = audioPath; view.RequestWaveform = decoder;
            FruitsAtelier.Localization.Strings.SetLanguage(language); Paint();
        }
    }

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
        var audition = view.RequestAuditionHitsound;
        var menuLoop = view.RequestTestplayMenuLoop;
        var volumePreference = view.RequestAudioPreference;
        var updateCheck = view.RequestUpdateCheck;
        var updateRestart = view.RequestUpdateRestart;
        var updateStatus = view.UpdateStatus;
        int[] volumes = [view.LibrarySettings.MasterVolume, view.LibrarySettings.SongVolume, view.LibrarySettings.HitsoundVolume];
        double startupDelay = view.LibrarySettings.TestplayStartupDelaySeconds;
        string language = FruitsAtelier.Localization.Strings.Language;
        try
        {
            view.LibrarySettings.TestplayStartupDelaySeconds = 0;
            view.RequestTogglePlayback = () => { }; view.RequestPausePlayback = () => { };
            view.RequestSeek = _ => { }; view.RequestHitsound = _ => { };
            view.RequestAuditionHitsound = _ => { };
            view.RequestTestplayMenuLoop = _ => { };
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
                view.KeyDown(114, false, false); view.KeyDown(114, false, false);
                if (view.PlaybackSpeed != 1.5) throw new InvalidOperationException("Held F3 failed to switch autoplay speed once.");
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.KeyUp(114); view.KeyDown(114, false, false); view.KeyUp(114);
                if (view.PlaybackSpeed != 1) throw new InvalidOperationException("F3 failed to restore normal speed.");
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
                if (!view.TestplayPauseMenuVisible) throw new InvalidOperationException("Escape did not open the native pause menu.");
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.KeyDown(38, false, false); view.KeyUp(38);
                view.KeyDown(13, false, false); view.KeyUp(13);
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
                view.KeyDown(65, true, false); view.OpenStackDialog();
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                if (!view.StreamDialogVisible) throw new InvalidOperationException("Native stack dialog did not open.");
                var graph = view.StackGraphBounds;
                view.PointerDown(graph.X + graph.Width * .25f, graph.Bottom - graph.Height * 24 / 32, 0, false, false);
                view.PointerMove(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * .5f, false, false);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerUp(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * .5f, 0);
                var numeric = view.StackDistanceFieldBounds;
                view.PointerDown(numeric.X + 10, numeric.Y + 12, 0, false, false);
                view.PointerUp(numeric.X + 10, numeric.Y + 12, 0);
                view.KeyDown(65, true, false); foreach (char digit in "15.25") view.TextInput(digit);
                view.KeyDown(13, false, false);
                view.KeyDown(90, true, false); view.KeyDown(89, true, false);
                view.KeyDown(13, false, false);
                if (view.Document.Tracks[0].Stack is not { } envelope || !envelope.Points.Any(p => p.Distance == 15.25))
                    throw new InvalidOperationException("Native stack envelope drag or confirmation failed.");
                view.KeyDown(90, true, false); view.KeyDown(89, true, false);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.KeyDown(65, true, false); view.OpenStackDialog();
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                var fruitPreview = view.StackPreviewBounds;
                var editedFruit = view.Conversion.Objects[2];
                double stackStart = view.Document.Tracks[0].Nodes[0].TimeMs;
                float fruitPadding = (float)(CatchSize.FruitRadius(view.Document.CircleSize) / 512 * fruitPreview.Width) + 2;
                float fruitX = fruitPreview.X + (float)(editedFruit.X / 512) * fruitPreview.Width;
                float fruitY = fruitPreview.Bottom - fruitPadding - (float)((editedFruit.TimeMs - stackStart) * CatchScrollTiming.PixelsPerMs(view.Document.ApproachRate, fruitPreview.Width));
                view.PointerDown(fruitX, fruitY, 0, false, false);
                view.PointerMove(fruitX + 12, fruitY - 20, false, false);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                view.PointerUp(fruitX + 12, fruitY - 20, 0); view.KeyDown(13, false, false);
                if (view.Document.Tracks[0].Stack!.FruitAdjustments.Count != 1
                    || view.Conversion.Objects[2].TimeMs != editedFruit.TimeMs)
                    throw new InvalidOperationException("Native individual stack fruit drag failed.");
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
                view.PointerDown(view.SettingsBounds.X + 40, view.SettingsBounds.Y + 286, 0, false, false);
                view.PointerUp(view.SettingsBounds.X + 40, view.SettingsBounds.Y + 286, 0);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                foreach (int binding in new[] { 186, 222, 219, 221, 8, 17, 18, 96, 111, 121 })
                {
                    view.PointerDown(view.SettingsBounds.X + 238, view.SettingsBounds.Y + 200, 0, false, false);
                    view.PointerUp(view.SettingsBounds.X + 238, view.SettingsBounds.Y + 200, 0);
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
                view.OpenVolumePopover();
                Thread.Sleep(130);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                for (int channel = 0; channel < 3; channel++)
                {
                    var bar = view.VolumeBarBounds(channel);
                    float x = bar.X + bar.Width / 2;
                    float y = bar.Bottom - bar.Height * (channel + 1) / 4;
                    if (bar.Bottom >= height || bar.Right >= width)
                        throw new InvalidOperationException("Volume bar is outside the window.");
                    view.PointerDown(x, y, 0, false, false);
                    if (!view.WantsCapture) throw new InvalidOperationException("Volume bar did not capture.");
                    view.PointerUp(x, y, 0);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                }
                if (view.LibrarySettings.MasterVolume != 25 || view.LibrarySettings.SongVolume != 50 || view.LibrarySettings.HitsoundVolume != 75)
                    throw new InvalidOperationException("Native volume bars did not update percentages.");
                for (int channel = 0; channel < 3; channel++)
                {
                    var bar = view.VolumeBarBounds(channel);
                    view.PointerMove(bar.X + 4, bar.Y + 4, false, false);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                    view.Wheel(bar.X + 4, bar.Y + 4, -120, false);
                    int[] hoveredVolumes = [view.LibrarySettings.MasterVolume, view.LibrarySettings.SongVolume, view.LibrarySettings.HitsoundVolume];
                    if (hoveredVolumes[channel] != (channel + 1) * 25 - 5)
                        throw new InvalidOperationException("Native volume wheel did not adjust the hovered bar.");
                    view.SetModifiers(true, false);
                    view.KeyDown(38, false, false); view.KeyUp(38);
                    view.SetModifiers(false, false);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                }
                if (view.LibrarySettings.MasterVolume != 25 || view.LibrarySettings.SongVolume != 50 || view.LibrarySettings.HitsoundVolume != 75)
                    throw new InvalidOperationException("Native Alt+Up did not adjust the hovered volume channel.");
                view.KeyDown(27, false, false);
                if (view.VolumePopoverVisible) throw new InvalidOperationException("Native volume popover did not close.");
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
                var settings = view.SettingsBounds;
                foreach (int category in new[] { 0, 1, 2, 3, 4, 5 })
                {
                    float sx = settings.X + 40, sy = settings.Y + 96 + category * 48;
                    view.PointerDown(sx, sy, 0, false, false); view.PointerUp(sx, sy, 0);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                    if (category == 1)
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(20);
                        while (view.StorageBusy && DateTime.UtcNow < deadline)
                        { Thread.Sleep(10); canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
                        if (view.StorageBusy) throw new InvalidOperationException("Native storage accounting did not complete.");
                        view.Wheel(settings.X + 300, settings.Y + 200, -2400, false);
                        canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                    }
                    if (category == 3)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            var bar = view.VolumeSliderBounds(channel);
                            float vx = bar.X + bar.Width * (channel + 1) / 4, vy = bar.Y + 12;
                            view.PointerDown(vx, vy, 0, false, false);
                            view.PointerUp(vx, vy, 0);
                        }
                        if (view.LibrarySettings.MasterVolume != 25 || view.LibrarySettings.SongVolume != 50 || view.LibrarySettings.HitsoundVolume != 75)
                            throw new InvalidOperationException("Settings volume controls did not update shared percentages.");
                    }
                    if (category == 2)
                    {
                        var selector = view.SettingsSkinSelectorBounds;
                        view.PointerDown(selector.X + 8, selector.Y + 8, 0, false, false);
                        view.PointerUp(selector.X + 8, selector.Y + 8, 0);
                        canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                        view.KeyDown(27, false, false);
                    }
                }
                foreach (var phase in new[] { UpdatePhase.Unsupported, UpdatePhase.Checking, UpdatePhase.Available, UpdatePhase.Downloading, UpdatePhase.Ready, UpdatePhase.Failed })
                {
                    view.UpdateStatus = new(phase, "0.8.2", 42);
                    canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                }
                bool restartPrepared = false;
                view.RequestUpdateRestart = () => restartPrepared = view.PrepareFileOperation();
                view.UpdateStatus = new(UpdatePhase.Ready, "0.9.1", 100);
                canvas.Begin(); view.Render(canvas, width, height); canvas.End();
                float restartX = view.SettingsBounds.X + 230 + 230;
                float restartY = view.SettingsBounds.Y + 310;
                view.PointerDown(restartX, restartY, 0, false, false);
                view.PointerUp(restartX, restartY, 0);
                if (!restartPrepared) throw new InvalidOperationException("Update restart from Settings could not prepare saving.");
                view.KeyDown(27, false, false);
                view.KeyDown(27, false, false); view.CloseLibrary();
            }
        }
        finally
        {
            view.StopTestplay(); view.LoadProject(project); view.CloseLibrary();
            view.RequestTogglePlayback = toggle; view.RequestPausePlayback = pause;
            view.RequestSeek = seek; view.RequestHitsound = hitsound;
            view.RequestAuditionHitsound = audition;
            view.RequestTestplayMenuLoop = menuLoop;
            view.RequestAudioPreference = volumePreference;
            view.RequestUpdateCheck = updateCheck;
            view.RequestUpdateRestart = updateRestart;
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
                view.Wheel(view.CanvasPlotBounds.X, view.CanvasPlotBounds.Bottom, -2400, false, false, true);
                view.UpdateTransport(1500, 10000, true, false, false, null, null);
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
                var sliderMap = new MapDocument { DurationMs = 6000, IsDemo = false };
                var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 280 };
                slider.ControlPoints.AddRange([new(120, 192), new(400, 192)]);
                sliderMap.ImportedSliders.Add(slider);
                view.LoadDocument(sliderMap); Paint();
                view.Wheel(view.CanvasPlotBounds.X, view.CanvasPlotBounds.Bottom, -2400, false, false, true); Paint();
                var lockButton = view.AssistButtonBounds[6];
                view.PointerMove(lockButton.X + 10, lockButton.Y + 10, false, false); Paint();
                view.PointerDown(lockButton.X - 20, lockButton.Y + 16, 0, false, false);
                view.PointerUp(lockButton.X - 20, lockButton.Y + 16, 0); Paint();
                if (view.DropletSelectionLocked) throw new InvalidOperationException("Could not unlock native droplet fixture.");
                var tiny = OsuBeatmapWriter.Serialize(sliderMap).PlayableObjects.First(o => o.Kind == CatchObjectKind.TinyDroplet && o.TimeMs > 1400);
                view.UpdateTransport(tiny.TimeMs, 6000, true, false, false, null, null); Paint();
                field = view.PlayfieldBounds;
                x = field.X + (float)tiny.X / 512 * field.Width;
                y = view.CanvasPlotBounds.Bottom - (float)((tiny.TimeMs - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                if (view.XCoordinateFieldBounds is not null || view.IsDirty || !sliderMap.ContentEquals(view.Document))
                    throw new InvalidOperationException("Native droplet readout changed first-click selection or content.");
                view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                if (view.XCoordinateFieldBounds is null) throw new InvalidOperationException("Native selected droplet X field is missing.");
                view.PointerMove(lockButton.X + 10, lockButton.Y + 10, false, false); Paint();
                view.PointerDown(lockButton.X - 20, lockButton.Y + 16, 0, false, false);
                view.PointerUp(lockButton.X - 20, lockButton.Y + 16, 0); Paint();
                view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                if (!view.DropletSelectionLocked || view.XCoordinateFieldBounds is not null)
                    throw new InvalidOperationException("Native droplet lock retained child selection.");
                foreach (bool included in new[] { true, false })
                {
                    view.PointerDown(235, 20, 0, false, false); view.PointerUp(235, 20, 0); Paint();
                    view.PointerDown(235, 397, 0, false, false); view.PointerUp(235, 397, 0); Paint();
                    if (view.MovementIncludesTinyDroplets != included || !sliderMap.ContentEquals(view.Document))
                        throw new InvalidOperationException("Native tiny movement display toggle failed.");
                }
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
                canvas.Resize(size.Item1, size.Item2, 96);
                var deadline = Stopwatch.StartNew();
                while (view.SynchronizationBusy && deadline.Elapsed.TotalSeconds < 15)
                { canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End(); Thread.Sleep(5); }
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
        CheckBookmarkCache(canvas);
        CheckAnimatedTextResources(canvas);
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
            ObjectStructureRenderCheck.Run(canvas, size.Item1, size.Item2);
            AimodRenderCheck.Run(canvas, size.Item1, size.Item2);
            SynchronizationRenderCheck.Run(canvas, size.Item1, size.Item2);
            SliderDraftRenderCheck.Run(canvas, size.Item1, size.Item2);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            CheckPaletteHints(canvas, view, size.Item1, size.Item2);
            CheckSongSetup(canvas, view, size.Item1, size.Item2);
            CheckTimingSetup(canvas, view, size.Item1, size.Item2);
            if (!view.MovementAnalysisEnabled)
            {
                view.PointerDown(235, 20, 0, false, false); view.PointerUp(235, 20, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                view.PointerDown(235, 363, 0, false, false);
                view.PointerUp(235, 363, 0);
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
            if (size.Item1 >= 1400)
            {
                var curveToggle = view.DifficultyCurveToggleBounds;
                view.PointerDown(curveToggle.X + 10, curveToggle.Y + 10, 0, false, false);
                view.PointerUp(curveToggle.X + 10, curveToggle.Y + 10, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                var curve = view.DifficultyCurveGraphBounds;
                if (!view.DifficultyCurveVisible || curve.Width < 100
                    || view.DifficultyCurvePanelBounds.Y != 84
                    || view.CanvasPlotBounds.Width < EditorView.MinimumPlayfieldWidth)
                    throw new InvalidOperationException("Difficulty curve sidebar did not fit beside Catch preview.");
                view.PointerDown(curve.X + curve.Width / 2, curve.Y + 2, 0, false, false);
                view.PointerMove(curve.X + curve.Width / 2, curve.Bottom - 2, false, false);
                view.PointerUp(curve.X + curve.Width / 2, curve.Bottom - 2, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                curveToggle = view.DifficultyCurveToggleBounds;
                view.PointerDown(curveToggle.X + 10, curveToggle.Y + 10, 0, false, false);
                view.PointerUp(curveToggle.X + 10, curveToggle.Y + 10, 0);
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            }
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
