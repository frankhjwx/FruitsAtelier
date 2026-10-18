using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class OsuCanvasRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/osu-canvas-native");
        Directory.CreateDirectory(folder);
        string[] arguments = Environment.GetCommandLineArgs();
        var configuredSkin = arguments.Contains("--osu-configured-skin") ? LibrarySettings.Load() : null;
        string? explicitSkin = Argument("--osu-skin-folder"), explicitDefault = Argument("--osu-default-skin");
        if (Argument("--osu-performance-map") is { } performanceMap)
        {
            DenseMapPerformanceCheck.Run(canvas, performanceMap, explicitSkin, explicitDefault);
            return;
        }
        if (Argument("--osu-map-folder") is { } mapFolder)
        {
            RenderMaps(mapFolder);
            return;
        }
        string? expectedSkinName = null;
        if (explicitSkin is not null)
        {
            if (!FruitsAtelier.App.Skinning.CatchSkin.TryLoad(explicitSkin, out var requested, out string error, allowEmpty: true)) throw new InvalidOperationException(error);
            expectedSkinName = requested!.Name;
        }
        if (explicitDefault is not null && !File.Exists(explicitDefault)) throw new FileNotFoundException("Explicit native-check default skin archive is missing.", explicitDefault);
        if (arguments.Contains("--osu-image-cache-check"))
        {
            VerifyImageResidency(canvas, folder, configuredSkin, explicitSkin, explicitDefault);
            return;
        }
        VerifyGradient(canvas, folder);
        if (Environment.GetCommandLineArgs().Contains("--osu-gradient-only")) return;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            foreach (bool skinned in new[] { false, true })
            {
                L.SetLanguage(locale);
                var editor = new EditorView(false);
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, SliderMultiplier = 1.4, IsDemo = false };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 80, Y = 64 }, new() { TimeMs = 1100, X = 80, Y = 64 }]);
                foreach (var (kind, x, y) in new[] { ('L', 210d, 80d), ('B', 350d, 100d), ('P', 80d, 260d), ('C', 290d, 260d) })
                {
                    var slider = new ImportedSlider { X = x, Y = y, TimeMs = 2000, PathType = kind, PixelLength = 160, SpanCount = 3 };
                    slider.ControlPoints.AddRange([new(x, y), new(x + 60, y - 55), new(x + 120, y + 10)]);
                    if (kind == 'B') slider.ControlPoints.Insert(2, slider.ControlPoints[1]);
                    map.ImportedSliders.Add(slider);
                }
                map.BananaShowers.Add(new() { TimeMs = 5000, EndTimeMs = 6500 });
                editor.LoadDocument(map);
                if (skinned)
                {
                    if (configuredSkin is { } settings)
                    {
                        editor.LibrarySettings.Workspace = settings.Workspace;
                        editor.LibrarySettings.DefaultSkin = settings.DefaultSkin;
                        editor.LibrarySettings.SelectedSkin = settings.SelectedSkin;
                    }
                    if (explicitSkin is not null || explicitDefault is not null)
                    {
                        editor.LibrarySettings.Workspace = Path.Combine(folder, "explicit-skin-workspace");
                        editor.LibrarySettings.DefaultSkin = explicitDefault;
                        editor.LibrarySettings.SelectedSkin = explicitSkin;
                    }
                    editor.InitializeSkin();
                    if (explicitSkin is not null && editor.SkinName != expectedSkinName)
                        throw new InvalidOperationException("The explicitly requested native-check skin was not loaded.");
                }
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint(); editor.KeyDown(9, false, false); editor.KeyUp(9);
                foreach (int toolKey in new[] { (int)'2', (int)'3' })
                {
                    editor.KeyDown(toolKey, false, false); editor.KeyUp(toolKey);
                    var placementPointer = editor.OsuCanvasScreen(184, 192);
                    editor.PointerMove(placementPointer.X, placementPointer.Y, false, false);
                    Paint(toolKey == '2' ? "circle-placement" : "slider-placement");
                }
                foreach (var (x, y) in new[] { (64, 192), (192, 64) })
                {
                    var control = editor.OsuCanvasScreen(x, y);
                    editor.PointerDown(control.X, control.Y, 0, false, false); editor.PointerUp(control.X, control.Y, 0);
                }
                var circularTail = editor.OsuCanvasScreen(320, 192);
                editor.PointerMove(circularTail.X, circularTail.Y, false, false);
                Paint("circular-slider-draft");
                editor.KeyDown(13, false, false); editor.KeyUp(13);
                Paint("circular-slider-committed");
                if (editor.Document.ImportedSliders.Last().PathType != 'P')
                    throw new InvalidOperationException("Native three-point slider did not commit as a circle.");
                editor.KeyDown('Z', true, false); editor.KeyUp('Z');
                editor.KeyDown('1', false, false); editor.KeyUp('1');
                var notesLock = editor.AssistButtonBounds[6];
                editor.LibrarySettings.LockSlidersToCanvas = true;
                editor.PointerMove(notesLock.X + 5, notesLock.Y + 5, false, false);
                Paint("slider-canvas-lock");
                editor.LibrarySettings.LockSlidersToCanvas = false;
                editor.PointerMove(0, 0, false, false);
                foreach (var (time, name) in new[] { (900, "approach-stack"), (1400, "late-fade"), (1900, "slider-approach"), (2500, "slider-repeat"), (5700, "spinner"), (6450, "spinner-end"), (6800, "spinner-fade") })
                {
                    editor.UpdateTransport(time, 10000, true, false, false, null, null);
                    editor.UpdateTransport(time, 10000, true, false, false, null, null);
                    Paint(name);
                }
                editor.UpdateTransport(1900, 10000, true, false, false, null, null);
                editor.KeyDown('1', true, false); editor.KeyUp('1');
                editor.KeyDown('T', false, false); editor.KeyUp('T');
                Paint();
                var selectedHead = editor.OsuCanvasScreen(350, 100);
                editor.PointerDown(selectedHead.X, selectedHead.Y, 0, false, false); editor.PointerUp(selectedHead.X, selectedHead.Y, 0);
                var coordinatePointer = editor.OsuCanvasScreen(184, 96);
                editor.PointerMove(coordinatePointer.X, coordinatePointer.Y, false, false);
                Paint("selected-controls-grid");
                var canvasBeforeTimelineZoom = editor.OsuCanvasBounds;
                double timelineScale = editor.ObjectTimelinePixelsPerMs;
                var timeline = editor.ObjectTimelineBounds;
                editor.Wheel(timeline.X + timeline.Width / 2, timeline.Y + timeline.Height / 2, 120, false, false, true);
                Paint("timeline-zoom");
                if (editor.OsuCanvasBounds != canvasBeforeTimelineZoom || editor.ObjectTimelinePixelsPerMs <= timelineScale)
                    throw new InvalidOperationException("Upper timeline zoom changed the osu canvas or did not change its time scale.");
                editor.KeyDown('3', false, false); editor.KeyUp('3');
                foreach (var point in new[] { (80, 160), (160, 220) })
                {
                    var at = editor.OsuCanvasScreen(point.Item1, point.Item2);
                    editor.PointerDown(at.X, at.Y, 0, false, false); editor.PointerUp(at.X, at.Y, 0);
                }
                var separator = editor.OsuCanvasScreen(160, 220);
                editor.PointerDoubleClick(separator.X, separator.Y, false, false);
                var pointer = editor.OsuCanvasScreen(240, 160);
                editor.PointerMove(pointer.X, pointer.Y, false, false);
                Paint("draft-controls-grid");
                editor.KeyDown(27, false, false); editor.KeyUp(27);
                editor.KeyDown('2', false, false); editor.KeyUp('2');
                var circlePointer = editor.OsuCanvasScreen(184, 96);
                editor.PointerMove(circlePointer.X, circlePointer.Y, false, false);
                Paint("circle-combo-preview");
                editor.KeyDown('1', false, false); editor.KeyUp('1');
                var preview = editor.PreviewToggleBounds;
                if (!editor.CatchPreviewVisible && preview.Width > 0)
                { editor.PointerDown(preview.X + 10, preview.Y + 10, 0, false, false); editor.PointerUp(preview.X + 10, preview.Y + 10, 0); }
                Paint("catch-preview");
                if (!editor.IsOsuCanvas || editor.IsDirty || !map.ContentEquals(editor.Document)) throw new InvalidOperationException("Standard canvas rendering changed content or mode.");
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}-{(skinned ? "skin" : "fallback")}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Standard canvas native rendering checks passed.");
        string? Argument(string name)
        {
            string? inline = arguments.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal));
            if (inline is not null) return inline[(name.Length + 1)..];
            int index = Array.IndexOf(arguments, name);
            if (index < 0) return null;
            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing native-check argument: " + name);
            return arguments[index + 1];
        }

        void RenderMaps(string source)
        {
            canvas.Resize(1440, 900, 96);
            int count = 0;
            foreach (string file in Directory.EnumerateFiles(source, "*.osu").Order())
            {
                var map = OsuBeatmapReader.ReadFile(file);
                var editor = new EditorView(false);
                editor.LoadDocument(map);
                if (!editor.Conversion.Success) throw new InvalidOperationException(string.Join("; ", editor.Conversion.Diagnostics));
                var largest = map.ImportedSliders.MaxBy(s => s.ControlPoints.Count);
                double time = largest?.TimeMs ?? map.Fruits.FirstOrDefault()?.TimeMs ?? 0;
                editor.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
                editor.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
                if (Math.Abs(editor.PlayheadMs - time) > .01) throw new InvalidOperationException("Imported map check did not seek to the dense slider.");
                Capture("catch");
                editor.ToggleOsuCanvas();
                Capture("osu");
                if (editor.IsDirty) throw new InvalidOperationException("Rendering an imported map changed its content.");
                count++;
                AppLog.Write($"Imported map native rendering passed: {Path.GetFileName(file)}");
                void Capture(string mode)
                {
                    canvas.Begin(); editor.Render(canvas, 1440, 900);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"import-{count}-{mode}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
            if (count == 0) throw new InvalidOperationException("Native map check found no beatmaps.");
            AppLog.Write($"Imported map native rendering checks passed: {count} maps, both canvases.");
        }
    }

    private static void VerifyImageResidency(D2DCanvas canvas, string folder, LibrarySettings? configured,
        string? skinFolder, string? defaultArchive)
    {
        var editor = new EditorView(false);
        editor.LibrarySettings.Workspace = Path.Combine(folder, "cache-check-workspace");
        editor.LibrarySettings.SelectedSkin = skinFolder ?? configured?.SelectedSkin;
        editor.LibrarySettings.DefaultSkin = defaultArchive ?? configured?.DefaultSkin;
        editor.InitializeSkin();
        var map = new MapDocument { DurationMs = 10000, IsDemo = false };
        for (int i = 0; i < 16; i++)
        {
            int x = 48 + i % 4 * 120, y = 48 + i / 4 * 88;
            map.Fruits.Add(new() { X = x, Y = y, TimeMs = 1000 + i, OriginalLine = $"{x},{y},{1000 + i},5,0,0:0:0:0:" });
        }
        editor.LoadDocument(map); editor.ToggleOsuCanvas();
        editor.UpdateTransport(900, 10000, true, false, false, null, null);
        canvas.Resize(2520, 1575, 168);
        for (int i = 0; i < 8; i++) Paint();
        int decoded = canvas.ImageDecodeCount;
        var timings = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            canvas.Begin(); editor.Render(canvas, 1440, 900);
            timings.Add(clock.Elapsed.TotalMilliseconds);
            canvas.End(lowLatency: true);
        }
        timings.Sort();
        int repeatedDecodes = canvas.ImageDecodeCount - decoded;
        editor.UpdateTransport(1100, 10000, true, false, false, null, null); Paint();
        editor.UpdateTransport(900, 10000, true, false, false, null, null); Paint();
        decoded = canvas.ImageDecodeCount;
        for (int i = 0; i < 30; i++)
        {
            editor.UpdateTransport(900 + i * 10, 10000, true, true, false, null, null);
            Paint();
        }
        int playbackRepeatedDecodes = canvas.ImageDecodeCount - decoded;
        string report = $"osu image residency: skin={editor.SkinName}; repeatedDecodes={repeatedDecodes}; playbackRepeatedDecodes={playbackRepeatedDecodes}; CPU median={timings[timings.Count / 2]:F2} ms; p95={timings[(int)(timings.Count * .95)]:F2} ms.";
        AppLog.Write(report);
        File.WriteAllText(Path.Combine(folder, "image-residency.txt"), report);
        if (repeatedDecodes != 0 || playbackRepeatedDecodes != 0)
            throw new InvalidOperationException("Static osu rendering repeatedly decodes unchanged images: " + report);
        void Paint() { canvas.Begin(); editor.Render(canvas, 1440, 900); canvas.End(lowLatency: true); }
    }

    private static void VerifyGradient(D2DCanvas canvas, string folder)
    {
        VerifyRasterPlacement(canvas);
        canvas.Resize(512, 256, 96);
        var dense = new System.Numerics.Vector2[8193];
        for (int i = 0; i < dense.Length; i++)
        {
            float distance = 312f * i / (dense.Length - 1);
            dense[i] = distance <= 240 ? new(80 + distance, 128) : new(320, 128 + distance - 240);
        }
        foreach (bool raster in new[] { false, true })
        {
        canvas.Begin(); canvas.Fill(new(0, 0, 512, 256), 0x202020);
        canvas.SliderBody(raster ? dense : new System.Numerics.Vector2[] { new(80, 128), new(320, 128), new(320, 200) }, 0, 0, 1,
            128, 0xFFFFFF, 0xCC2020, 1);
        canvas.End(capture: () =>
        {
            var frame = canvas.CapturePixels();
            var center = Pixel(200, 128); var edge = Pixel(200, 176); var border = Pixel(200, 183); var bend = Pixel(320, 128);
            if (center.R <= edge.R + 20 || center.G <= edge.G + 20 || center.R >= 220)
                throw new InvalidOperationException("Native slider gradient must brighten toward its centre while retaining one translucent composite.");
            if (border.R < 230 || border.G < 230 || border.B < 230)
                throw new InvalidOperationException("Native slider border must retain an independent white ring.");
            if (Math.Abs(center.R - bend.R) > 2 || Math.Abs(center.G - bend.G) > 2)
                throw new InvalidOperationException("Native slider path joins changed the gradient's centre opacity.");
            using var output = new BinaryWriter(File.Create(Path.Combine(folder, raster ? "gradient-raster-pixels.bmp" : "gradient-pixels.bmp")));
            output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
            output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
            output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
            (byte R, byte G, byte B) Pixel(int x, int y)
            {
                int offset = (y * frame.Width + x) * 4;
                return (frame.Pixels[offset + 2], frame.Pixels[offset + 1], frame.Pixels[offset]);
            }
        });
        }
        int builds = canvas.SliderRasterBuildCount;
        canvas.Begin();
        canvas.SliderBody(dense, 5, -3, 1.01f, 128, 0xFFFFFF, 0xCC2020, .4f);
        canvas.End();
        if (canvas.SliderRasterBuildCount != builds)
            throw new InvalidOperationException("Slider pan, zoom, or fading rebuilt immutable raster geometry.");
        for (uint color = 1; color <= 16; color++)
        {
            canvas.Begin(); canvas.SliderBody(dense, 0, 0, 1, 128, 0xFFFFFF, color * 1000, 1); canvas.End();
            if (canvas.CachedSliderRasterBytes > 32L * 1024 * 1024)
                throw new InvalidOperationException("Slider raster cache exceeded its native memory budget.");
        }
        var oversized = (System.Numerics.Vector2[])dense.Clone();
        oversized[0] = new(-100000, 128);
        canvas.Begin(); canvas.Fill(new(0, 0, 512, 256), 0x202020);
        canvas.SliderBody(oversized, 0, 0, 1, 128, 0xFFFFFF, 0xCC2020, 1);
        canvas.End(capture: () =>
        {
            var frame = canvas.CapturePixels();
            int centre = (128 * frame.Width + 200) * 4;
            int border = (183 * frame.Width + 200) * 4;
            if (frame.Pixels[centre + 2] < 150 || frame.Pixels[border] < 230)
                throw new InvalidOperationException("A cropped oversized slider lost its visible body or border.");
        });
        if (canvas.CachedSliderRasterBytes > 32L * 1024 * 1024)
            throw new InvalidOperationException("An oversized slider exceeded the bounded raster cache.");
        foreach (int simultaneous in new[] { 8, 16, 9, 4 })
        {
            var warmClock = System.Diagnostics.Stopwatch.StartNew();
            int stableFrames = 0, lastBuilds = -1;
            while (stableFrames < 3)
            {
                DrawSimultaneous();
                stableFrames = canvas.SliderRasterBuildCount == lastBuilds && !canvas.PendingSliderRasterBuild
                    && canvas.CachedSliderRasterTransitionCount == 0 ? stableFrames + 1 : 0;
                lastBuilds = canvas.SliderRasterBuildCount;
                if (warmClock.Elapsed.TotalSeconds > 15)
                    throw new InvalidOperationException("Simultaneous slider caches did not settle after a budget change.");
                Thread.Sleep(1);
            }
            int warmBuilds = canvas.SliderRasterBuildCount;
            for (int frame = 0; frame < 3; frame++) DrawSimultaneous();
            if (canvas.SliderRasterBuildCount != warmBuilds)
                throw new InvalidOperationException("Simultaneously visible slider bodies evicted each other from the raster cache.");
            if (canvas.CachedSliderRasterBytes > 32L * 1024 * 1024)
                throw new InvalidOperationException("Simultaneous slider bodies exceeded the raster cache budget.");
            void DrawSimultaneous()
            {
                canvas.Begin(); canvas.PrepareSliderBodies(simultaneous);
                for (uint body = 1; body <= simultaneous; body++)
                    canvas.SliderBody(dense, 0, 0, 1, 128, 0xFFFFFF, body * 12345, 1);
                canvas.End();
            }
        }
        var upgradeClock = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            canvas.Begin(); canvas.PrepareSliderBodies(8, deferred: true);
            for (uint body = 1; body <= 8; body++)
                canvas.SliderBody(dense, 0, 0, 1, 128, 0xFFFFFF, body * 54321, 1);
            canvas.End();
            if (canvas.CachedSliderRasterBytes > 32L * 1024 * 1024)
                throw new InvalidOperationException("Background slider uploads exceeded the raster cache budget.");
            if (upgradeClock.Elapsed.TotalSeconds > 15)
                throw new InvalidOperationException("Deferred slider previews did not finish upgrading.");
            Thread.Sleep(1);
        } while (canvas.PendingSliderRasterBuild || canvas.CachedSliderRasterPreviewCount > 0);
        int upgradedBuilds = canvas.SliderRasterBuildCount;
        canvas.Begin(); canvas.PrepareSliderBodies(8, deferred: true);
        for (uint body = 1; body <= 8; body++)
            canvas.SliderBody(dense, 0, 0, 1, 128, 0xFFFFFF, body * 54321, 1);
        canvas.End();
        if (canvas.SliderRasterBuildCount != upgradedBuilds)
            throw new InvalidOperationException("Completed background slider textures were rebuilt.");
        canvas.PrepareSliderBodies(0);
        VerifyRasterWorkingSetGrowth(canvas, dense);
        VerifyRasterTransitions(canvas, dense);
        AppLog.Write("Native vector/raster slider gradient, transform reuse, oversized clipping, bounded cache, and background upgrade checks passed.");
    }

    private static void VerifyRasterPlacement(D2DCanvas canvas)
    {
        var path = new System.Numerics.Vector2[8193];
        for (int i = 0; i < path.Length; i++) path[i] = new(76 + 187f * i / (path.Length - 1), 192);
        foreach (int dpi in new[] { 96, 144, 168, 192, 240, 96 })
        foreach (var (x, y, scale) in new[] { (35f, 25f, 1.2f), (-40f, -80f, 1.5f) })
        {
            canvas.Resize(512 * dpi / 96, 384 * dpi / 96, dpi);
            canvas.Begin(); canvas.Fill(new(0, 0, 512, 384), 0x101010);
            canvas.PrepareSliderBodies(4);
            canvas.SliderBody(path, x, y, scale, 50, 0xFFFFFF, 0x80B040, 1);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                int left = frame.Width, top = frame.Height, right = -1, bottom = -1;
                for (int row = 0; row < frame.Height; row++)
                for (int column = 0; column < frame.Width; column++)
                {
                    int index = (row * frame.Width + column) * 4;
                    if (frame.Pixels[index] <= 32 && frame.Pixels[index + 1] <= 32 && frame.Pixels[index + 2] <= 32) continue;
                    left = Math.Min(left, column); right = Math.Max(right, column);
                    top = Math.Min(top, row); bottom = Math.Max(bottom, row);
                }
                float radius = 50 * .921875f * scale / 2;
                double factor = 96d / dpi;
                if (Math.Abs(left * factor - (x + 76 * scale - radius)) > 2
                    || Math.Abs((right + 1) * factor - (x + 263 * scale + radius)) > 2
                    || Math.Abs(top * factor - (y + 192 * scale - radius)) > 2
                    || Math.Abs((bottom + 1) * factor - (y + 192 * scale + radius)) > 2)
                    throw new InvalidOperationException($"Slider body does not cover its authored path at DPI {dpi}, scale {scale}: pixels=({left},{top})-({right},{bottom}).");
            });
        }
        AppLog.Write("Native raster bodies align with authored path positions and radii across DPI changes, pan and zoom.");
    }

    private static void VerifyRasterTransitions(D2DCanvas canvas, System.Numerics.Vector2[] source)
    {
        double now = System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency + 1000;
        canvas.SliderRasterTimeOverride = () => now;
        try
        {
            foreach (int dpi in new[] { 96, 168 })
            {
                canvas.Resize(512 * dpi / 96, 256 * dpi / 96, dpi);
                foreach (bool reducingBudget in new[] { false, true })
                {
                    var path = (System.Numerics.Vector2[])source.Clone();
                    int bodies = 4;
                    bool deferred = !reducingBudget;
                    byte[] before = Paint();
                    deferred = true;
                    if (reducingBudget) bodies = 16;
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    while (canvas.CachedSliderRasterTransitionCount == 0)
                    {
                        Paint(); Thread.Sleep(1);
                        if (deadline.Elapsed.TotalSeconds > 10)
                            throw new InvalidOperationException("Slider raster replacement did not enter a smooth transition.");
                    }
                    byte[] beginning = Paint();
                    var samples = new List<(double Weight, byte[] Pixels)>();
                    for (int step = 1; step <= 3; step++)
                    {
                        now += 30;
                        samples.Add((step / 4d, Paint()));
                    }
                    now += 40;
                    byte[] after = Paint();
                    if (canvas.CachedSliderRasterTransitionCount != 0)
                        throw new InvalidOperationException("Finished slider transitions retained their old native textures.");
                    for (int pixel = 0; pixel < before.Length; pixel++)
                    {
                        if (Math.Abs(beginning[pixel] - before[pixel]) > 3)
                            throw new InvalidOperationException($"Replacing a slider texture changed its pixels before the transition began: reducing={reducingBudget}, byte={pixel}, old={before[pixel]}, start={beginning[pixel]}, new={after[pixel]}.");
                        foreach (var sample in samples)
                        {
                            double expected = before[pixel] * (1 - sample.Weight) + after[pixel] * sample.Weight;
                            if (Math.Abs(sample.Pixels[pixel] - expected) > 4)
                                throw new InvalidOperationException("Slider texture replacement changed opacity or position during the transition.");
                        }
                    }
                    byte[] Paint()
                    {
                        byte[] pixels = [];
                        canvas.Begin(); canvas.Fill(new(0, 0, 512, 256), 0x203040);
                        canvas.PrepareSliderBodies(bodies, deferred);
                        canvas.SliderBody(path, 0, 0, 1, 128, 0xFFFFFF, 0xCC2020, .45f);
                        canvas.End(capture: () => pixels = canvas.CapturePixels().Pixels);
                        if (canvas.CachedSliderRasterBytes > 32L * 1024 * 1024)
                            throw new InvalidOperationException("A transitioning slider exceeded the native raster memory budget.");
                        return pixels;
                    }
                }
            }
        }
        finally { canvas.SliderRasterTimeOverride = null; }
        AppLog.Write("Native slider texture upgrade and budget reduction preserve premultiplied opacity and position throughout the transition.");
    }

    private static void VerifyRasterWorkingSetGrowth(D2DCanvas canvas, System.Numerics.Vector2[] source)
    {
        var path = (System.Numerics.Vector2[])source.Clone();
        foreach (int count in new[] { 8, 16 })
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            int stable = 0, lastBuilds = -1;
            while (stable < 3)
            {
                canvas.Begin(); canvas.PrepareSliderBodies(count, deferred: true);
                for (int i = count - 1; i >= 0; i--)
                    canvas.SliderBody(path, 0, 0, 1, 128, 0xFFFFFF, (uint)(0x810000 + i * 1024), 1);
                canvas.End();
                stable = lastBuilds == canvas.SliderRasterBuildCount && !canvas.PendingSliderRasterBuild
                    && canvas.CachedSliderRasterPreviewCount == 0 && canvas.CachedSliderRasterTransitionCount == 0 ? stable + 1 : 0;
                lastBuilds = canvas.SliderRasterBuildCount;
                if (canvas.CachedSliderRasterBytes > 32L * 1024 * 1024 || deadline.Elapsed.TotalSeconds > 15)
                    throw new InvalidOperationException("Visible slider textures did not converge within the bounded cache after new bodies entered first.");
                Thread.Sleep(1);
            }
        }
        var prior = (System.Numerics.Vector2[])source.Clone();
        var next = (System.Numerics.Vector2[])source.Clone();
        canvas.Begin(); canvas.PrepareSliderBodies(4);
        canvas.SliderBody(prior, 0, 0, 1, 128, 0xFFFFFF, 0x789ABC, 1); canvas.End();
        canvas.Begin(); canvas.PrepareSliderBodies(32, deferred: true);
        canvas.SliderBody(next, 0, 0, 1, 128, 0xFFFFFF, 0x789ABC, 1); canvas.End();
        if (!canvas.SliderRasterNeedsRedraw)
            throw new InvalidOperationException("A preview awaiting release of a previous visible body did not request another paused frame.");
        var abandoned = (System.Numerics.Vector2[])source.Clone();
        canvas.Begin(); canvas.PrepareSliderBodies(4, deferred: true);
        canvas.SliderBody(abandoned, 0, 0, 1, 128, 0xFFFFFF, 0x456789, 1); canvas.End();
        var idleClock = System.Diagnostics.Stopwatch.StartNew();
        while (canvas.SliderRasterNeedsRedraw)
        {
            canvas.Begin(); canvas.End(); Thread.Sleep(1);
            if (idleClock.Elapsed.TotalSeconds > 5)
                throw new InvalidOperationException("Leaving the osu canvas retained a raster task or permanent redraw request.");
        }
    }
}
