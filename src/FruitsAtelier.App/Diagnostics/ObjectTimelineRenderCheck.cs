using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class ObjectTimelineRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/object-timeline-native");
        Directory.CreateDirectory(folder);
        var reports = new List<object>();
        foreach (int dpi in new[] { 96, 144, 192 })
        foreach (bool stack in new[] { false, true })
        {
            var map = new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 500 };
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 1, Stack = stack ? new() : null };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 4000, X = 300 }]);
            map.Tracks.Add(track);
            var view = new EditorView(false); view.LoadDocument(map);
            view.UpdateTransport(2500, 10000, false, false, false, null, null);
            canvas.Resize(1440 * dpi / 96, 900 * dpi / 96, dpi);
            Paint();
            var bounds = view.ObjectTimelineBounds;
            float x = bounds.X + (float)((2500 - view.ObjectTimelineStartMs) * view.ObjectTimelinePixelsPerMs);
            view.PointerDown(x, bounds.Y + 27, 0, false, false);
            view.PointerUp(x, bounds.Y + 27, 0); view.KeyDown('Q', false, false); Paint();
            if (!view.Document.Tracks[0].StreamNewComboOffsets.SequenceEqual(new[] { 1500d }))
                throw new InvalidOperationException("Native timeline NC did not target the selected middle fruit.");
            view.KeyDown('Z', true, false); Paint();
            if (!view.Document.ContentEquals(map)) throw new InvalidOperationException("Native timeline NC did not undo.");
            var frames = new List<double>();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 180; i++)
            {
                canvas.Begin();
                var timer = Stopwatch.StartNew(); view.Render(canvas, 1440, 900);
                if (i >= 30) frames.Add(timer.Elapsed.TotalMilliseconds);
                canvas.End(lowLatency: true);
            }
            allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 180;
            frames.Sort();
            canvas.Begin(); view.Render(canvas, 1440, 900);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{(stack ? "stack" : "stream")}-{dpi}.bmp")));
                output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length);
                output.Write(0); output.Write(54); output.Write(40);
                output.Write(frame.Width); output.Write(-frame.Height);
                output.Write((ushort)1); output.Write((ushort)32); output.Write(0);
                output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0);
                output.Write(frame.Pixels);
            });
            reports.Add(new { dpi, stack, renderMedianMs = frames[75], renderP95Ms = frames[142], allocatedBytesPerFrame = allocated });
            view.NewProject();
            void Paint() { canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End(); }
        }
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        AppLog.Write("Object timeline native rendering, internal NC and undo passed.");
    }
}
