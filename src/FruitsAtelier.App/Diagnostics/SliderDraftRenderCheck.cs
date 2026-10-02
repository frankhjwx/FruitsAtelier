using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Diagnostics;

internal static class SliderDraftRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height)
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var view = new EditorView(false);
            view.LoadDocument(new MapDocument { DurationMs = 10000 });
            view.SetSliderEditingMode(mode);
            Paint(); view.KeyDown('B', false, false); Paint();
            var bounds = view.PlayfieldBounds;
            float x = bounds.X + bounds.Width * .25f, y = bounds.Bottom - bounds.Height * .25f;
            Click(x, y);
            for (int i = 0; i < 20; i++)
            {
                view.PointerMove(x + bounds.Width * (.1f + i * .01f), y - bounds.Height * (.1f + i * .01f), false, false);
                Paint();
            }
            Click(x + bounds.Width * .4f, y - bounds.Height * .4f);
            view.KeyDown(13, false, false);
            if (!view.ConversionRefreshing) throw new InvalidOperationException("Native slider finish did not defer conversion.");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            do { Paint(); Thread.Sleep(1); } while (view.ConversionRefreshing && DateTime.UtcNow < deadline);
            if (view.ConversionRefreshing || !view.Conversion.Success || view.Document.Tracks.Count != 1)
                throw new InvalidOperationException("Native slider draft did not complete.");
            var expected = OsuBeatmapWriter.Serialize(view.Document, view.CompensateTinyDroplets);
            if (!expected.ObjectSequenceMatches) throw new InvalidOperationException("Native slider draft did not export correctly.");
            view.KeyDown('Z', true, false); Paint();
            if (view.Document.Tracks.Count != 0) throw new InvalidOperationException("Native slider undo failed.");
            view.NewProject();
            void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
            void Click(float px, float py)
            {
                view.PointerDown(px, py, 0, false, true); Paint();
                view.PointerUp(px, py, 0); Paint();
            }
        }
        AppLog.Write($"Slider draft rendering passed: {width}x{height}, both modes, background completion and undo.");
    }
}
