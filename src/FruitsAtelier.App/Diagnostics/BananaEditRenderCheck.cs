using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class BananaEditRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/banana-edit/native");
        Directory.CreateDirectory(folder);
        var reports = new List<object>();
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                int width = dpi == 144 ? 1000 : 1440, height = dpi == 144 ? 700 : 900;
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
                map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 3531.25 });
                var view = new EditorView(false); view.LoadDocument(map);
                canvas.Resize(width * dpi / 96, height * dpi / 96, dpi);
                Paint(); view.KeyDown('A', true, false); view.KeyDown('F', true, false); Paint();
                var wait = Stopwatch.StartNew();
                while (!view.BananaPreviewReady && wait.Elapsed.TotalSeconds < 20) { Thread.Sleep(5); Paint(); }
                if (!view.BananaPreviewReady || view.BananaEditPreview.Count == 0 || view.IsDirty)
                    throw new InvalidOperationException("Native banana preview did not complete without editing content.");
                Capture("preview");
                var preview = view.BananaPreviewBounds;
                view.Wheel(preview.X + 30, preview.Y + 100, 240, false, false, true); Paint(); Capture("zoom");
                var field = view.BananaNumeratorBounds[1];
                view.PointerDown(field.Right + 25, field.Y + 10, 0, false, false);
                view.PointerUp(field.Right + 25, field.Y + 10, 0); Paint(); Capture("divisions");
                view.KeyDown(27, false, false); Paint();
                view.KeyDown('F', true, false); Paint();
                wait.Restart();
                while (!view.BananaPreviewReady && wait.Elapsed.TotalSeconds < 20) { Thread.Sleep(5); Paint(); }
                view.KeyDown(13, false, false); Paint();
                if (view.BananaEditVisible || view.Document.BananaShowers[0].Pattern is null)
                    throw new InvalidOperationException("Native banana Apply failed.");
                Capture("group");
                var frames = new List<double>();
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    canvas.Begin(); var timer = Stopwatch.StartNew(); view.Render(canvas, width, height);
                    if (i >= 20) frames.Add(timer.Elapsed.TotalMilliseconds);
                    canvas.End(lowLatency: true);
                }
                frames.Sort();
                reports.Add(new { locale, dpi, width, height, medianMs = frames[40], p95Ms = frames[76], allocatedBytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - allocation) / 100 });
                view.KeyDown('Z', true, false); Paint();
                if (!view.Document.ContentEquals(map)) throw new InvalidOperationException("Native banana undo failed.");
                view.NewProject();
                void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
                void Capture(string name)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length);
                        output.Write(0); output.Write(54); output.Write(40);
                        output.Write(frame.Width); output.Write(-frame.Height);
                        output.Write((ushort)1); output.Write((ushort)32); output.Write(0);
                        output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0);
                        output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write("Banana edit native preview, division lists, grouped rendering and undo passed.");
    }
}
