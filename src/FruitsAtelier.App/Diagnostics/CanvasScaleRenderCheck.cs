using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class CanvasScaleRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/canvas-scale-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 480 };
                var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 48 };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 0 }, new Anchor { TimeMs = 2000, X = 512 }]);
                map.Tracks.Add(track);
                var editor = new EditorView(false); editor.LibrarySettings.ExperimentalSnapDivisions = true;
                editor.LoadDocument(map);
                editor.UpdateTransport(1500, 10000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint();
                var zoom = editor.ZoomSliderBounds;
                Click(zoom.X, zoom.Y + 14); Paint("minimum");
                var lockBounds = editor.CanvasScaleLockBounds;
                Click(lockBounds.X + 10, lockBounds.Y + 10); Paint("unlocked");
                for (int axis = 0; axis < 2; axis++)
                {
                    var slider = editor.CanvasAxisZoomBounds(axis);
                    editor.PointerDown(slider.X + slider.Width / 2, slider.Y + 14, 0, false, false);
                    editor.PointerMove(slider.Right, slider.Y + 14, false, false);
                    editor.PointerUp(slider.Right, slider.Y + 14, 0); Paint();
                }
                Paint("scale200"); editor.KeyDown(27, false, false); Paint();
                var plot = editor.CanvasPlotBounds;
                editor.PointerDown(plot.X + 50, plot.Y + 50, 1, false, false);
                editor.PointerMove(plot.X + 250, plot.Y + 50, false, false);
                editor.PointerUp(plot.X + 250, plot.Y + 50, 1); Paint("pan");
                var timeline = editor.ObjectTimelineBounds;
                editor.Wheel(timeline.X + 20, timeline.Y + 20, 12000, false, false, true); Paint("timeline");
                if (editor.CanvasZoom != 2 || editor.CanvasTimeZoom != 2 || editor.ObjectTimelinePixelsPerMs != 16
                    || !map.ContentEquals(editor.Document) || editor.IsDirty || editor.WantsCapture)
                    throw new InvalidOperationException("Native independent scale or timeline zoom changed content or failed its limits.");
                void Click(float x, float y)
                { editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint(); }
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height);
                        output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(frame.Pixels.Length);
                        output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Independent canvas scaling, panning and dense timeline native checks passed.");
    }
}
