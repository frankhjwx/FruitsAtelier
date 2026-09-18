using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class RenderCheck
{
    internal static void ProfileMap(D2DCanvas canvas, EditorView view, string path, float dpi)
    {
        view.RequestPreloadHitsounds = null; view.RequestStopHitsounds = null;
        view.RequestScheduleHitsound = (_, _) => { }; view.RequestPrepareHitsound = _ => { };
        var document = OsuBeatmapReader.ReadFile(path);
        view.LoadDocument(document);
        canvas.Resize((int)(1440 * dpi / 96), (int)(900 * dpi / 96), dpi);
        void Draw() { canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(); }
        Draw();
        view.Wheel(view.CanvasPlotBounds.X, view.CanvasPlotBounds.Bottom,
            (float)(120 * Math.Log(.32 / view.CanvasZoom) / Math.Log(1.16)), true);
        var snap = view.SnapSliderBounds;
        view.PointerDown(snap.Right - 31, snap.Y + snap.Height / 2, 0, false, false);
        view.PointerUp(snap.Right - 31, snap.Y + snap.Height / 2, 0);
        view.StartHitsounds(89038);
        var frames = new List<double>(); var transport = new List<double>();
        var watch = new Stopwatch();
        for (int i = 0; i < 140; i++)
        {
            watch.Restart();
            view.UpdateTransport(89038 + i * 1000d / 120, document.DurationMs, true, true, false, null, document.AudioPath);
            if (i >= 20) transport.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart(); Draw();
            if (i >= 20) frames.Add(watch.Elapsed.TotalMilliseconds);
        }
        frames.Sort(); transport.Sort();
        var report = new { map = Path.GetFileName(path), timingPoints = document.TimingPoints.Count,
            startMs = 89038, snap = view.SnapDivisor, zoom = view.CanvasZoom, dpi, adapter = canvas.AdapterName,
            renderMedianMs = frames[60], renderP95Ms = frames[114], transportMedianMs = transport[60], transportP95Ms = transport[114],
            note = "Hidden Direct2D window with skin; render includes EndDraw/Present. Hitsound callbacks are silent and exclude device submission. This is not measured screen FPS." };
        string reportPath = Path.Combine(Path.GetDirectoryName(AppLog.Path)!, "playback-profile.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write($"Playback profile complete: {reportPath}");
    }

    internal static void Run(D2DCanvas canvas, EditorView view)
    {
        var cases = new List<object>();
        var repeated = new ImportedSlider { TimeMs = 0, X = 100, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 3 };
        repeated.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
        view.Document.ImportedSliders.Add(repeated);
        foreach (int dpi in new[] { 96, 144, 192 })
        foreach (var size in new[] { (1440, 900), (980, 620) })
        {
            canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PointerDown(235, 20, 0, false, false); view.PointerUp(235, 20, 0);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.PointerMove(270, 55, false, false);
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.KeyDown(27, false, false);
            view.ShowError("bad-map.osu\n" + FruitsAtelier.Localization.Strings.Get("core.reader.importedParameters"));
            canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
            view.KeyDown(13, false, false);
            cases.Add(new { dpi, widthDip = size.Item1, heightDip = size.Item2, rendered = true, reverseMarkers = true, gridSubmenu = true, errorDialog = true });
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
        var report = new
        {
            adapter = canvas.AdapterName,
            skin = view.SkinName, decodedSkinImages = canvas.LoadedImageCount,
            note = "DPI values exercise render targets and DIP layout; not OS display-setting changes. Hidden-window timing includes EndDraw/Present and is not a visible-refresh guarantee.",
            cases, zeroSizeThenRestore = true, visibleFruitCount = 1000, measuredFrames = timings.Count,
            medianFrameMs = timings[timings.Count / 2], p95FrameMs = timings[(int)(timings.Count * .95)],
            modelErrors = CurveMath.Validate(view.Document)
        };
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppLog.Path)!, "render-check.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write($"Render check passed: {path}");
    }
}
