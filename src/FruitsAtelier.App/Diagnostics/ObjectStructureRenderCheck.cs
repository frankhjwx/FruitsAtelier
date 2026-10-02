using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class ObjectStructureRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        private long time;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => time;
        public void Advance() => time += 1000;
    }

    internal static void Run(D2DCanvas canvas, int width, int height)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var clock = new Clock(); var view = new EditorView(timeProvider: clock);
                var document = new MapDocument();
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 1500, X = 300 }]);
                document.Tracks.Add(track);
                view.LoadDocument(document); Paint();
                view.KeyDown(65, true, false); view.KeyDown(70, true, true); Paint();
                if (!view.StreamDialogVisible || view.StreamBreakIntoFruits) throw new InvalidOperationException("Native stream toggle default failed.");
                float dialogWidth = Math.Min(700, width - 32);
                float toggleX = (width - dialogWidth) / 2 + 100, toggleY = (height - Math.Min(510, height - 32)) / 2 + 114;
                Click(toggleX, toggleY); view.KeyDown(13, false, false); Paint();
                if (view.Document.Tracks.Count != 0 || view.Document.Fruits.Count != 5) throw new InvalidOperationException("Native stream toggle did not break fruits.");
                view.KeyDown(77, true, true); Paint();
                if (!view.MergeDialogVisible) throw new InvalidOperationException("Native merge dialog failed to open.");
                view.KeyDown(46, false, false); view.KeyDown(116, false, false); Paint();
                if (!view.MergeDialogVisible || view.IsTestplaying || view.Document.Fruits.Count != 5)
                    throw new InvalidOperationException("Native merge dialog passed through input.");
                view.KeyDown(39, false, false); view.KeyDown(13, false, false); Paint();
                if (view.MergeDialogVisible || view.Document.Tracks.Count != 1) throw new InvalidOperationException("Native merge confirmation failed.");
                view.KeyDown(65, true, true); Paint();
                if (view.Document.Tracks.Single().Nodes.Count != 2) throw new InvalidOperationException("Native clear shortcut failed.");
                view.KeyDown(90, true, false); view.KeyDown(90, true, false); Paint();
                if (view.Document.Fruits.Count != 5) throw new InvalidOperationException("Native structure undo failed.");
                view.KeyDown(90, true, false); Paint();
                view.UpdateTransport(1000, 30000, true, false, false, null, null); Paint();
                var field = view.PlayfieldBounds; var plot = view.CanvasPlotBounds;
                float x = field.X + 100f / 512 * field.Width;
                float y = plot.Bottom - (float)((1000 - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerDown(x, y, 0, false, false); clock.Advance(); Paint();
                view.PointerUp(x, y, 0); Paint();
                if (view.StreamConversionBounds.Width == 0) throw new InvalidOperationException("Native slider actions failed to open.");
                Click(view.StreamConversionBounds.X + 40, view.StreamConversionBounds.Y + 15); Paint();
                if (!view.StreamDialogVisible) throw new InvalidOperationException("Native slider actions failed to route.");
                view.KeyDown(27, false, false); Paint();
                void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
                void Click(float x, float y) { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint(); }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
