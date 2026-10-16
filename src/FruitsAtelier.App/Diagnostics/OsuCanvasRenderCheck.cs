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
                Paint("selected-controls-grid");
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

    private static void VerifyGradient(D2DCanvas canvas, string folder)
    {
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
        AppLog.Write("Native vector/raster slider gradient, transform reuse, oversized clipping, and bounded cache checks passed.");
    }
}
