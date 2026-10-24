using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class ConversionDockRenderCheck
{
    private sealed class ManualTime : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }

    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int panelWidth in new[] { 240, 360 })
            {
                L.SetLanguage(locale);
                var map = new MapDocument();
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 180 }, new() { TimeMs = 2000, X = 300 }]);
                var clock = new ManualTime();
                var view = new EditorView(false, timeProvider: clock); view.LoadDocument(map); Paint(); view.KeyDown(65, true, false);
                view.ShowSliderInspector(); Paint();
                var layout = view.CaptureDockLayouts(); layout.Mapping!.Right!.Width = panelWidth;
                view.LibrarySettings.DockLayouts = layout; view.RestoreDockLayouts(); Paint("actions");
                view.KeyDown(77, true, true); Paint("merge");
                if (view.MergeDialogVisible || !view.Document.ContentEquals(map)) throw new InvalidOperationException("Merge shortcut must open the dock without changing content.");
                view.KeyDown(70, false, true); Paint("menu");
                view.KeyDown(27, false, false);
                view.LibrarySettings.LazerEditing = true;
                view.UpdateTransport(500, 10000, true, false, false, null, null); Paint();
                var plot = view.PlayfieldBounds;
                float x = plot.X + 100f / 512 * plot.Width;
                float y = plot.Bottom - (float)((1000 - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerDown(x, y, 2, false, false); view.PointerUp(x, y, 2); Paint("right-click");
                view.KeyDown(27, false, false);
                view.PointerDown(x, y, 0, false, false); Paint();
                clock.Ticks += 1000; Paint();
                view.PointerUp(x, y, 0); Paint("long-press");
                if (!view.Document.ContentEquals(map)) throw new InvalidOperationException("Opening object menus must preserve content.");
                view.KeyDown(27, false, false);
                view.LibrarySettings.LazerEditing = false;
                var sliderMap = new MapDocument(); var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]); sliderMap.Tracks.Add(track);
                view.LoadDocument(sliderMap); view.CloseLibrary(); Paint(); view.KeyDown(65, true, false); view.KeyDown(70, true, true); Paint("stream");
                if (view.StreamDialogVisible || view.StreamSnapBounds.Width <= 0) throw new InvalidOperationException("Stream shortcut must open the dock.");
                view.NewProject();

                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, 1440, 900);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/conversion-docks"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{panelWidth}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
