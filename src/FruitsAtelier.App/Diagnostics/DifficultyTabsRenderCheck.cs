using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class DifficultyTabsRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var project = BeatmapProject.FromDocuments(Enumerable.Range(0, 4).Select(_ => new MapDocument { IsDemo = false }));
                for (int i = 0; i < 4; i++) project.Difficulties[i].Name = "Difficulty " + i;
                var view = new EditorView(false); view.LoadProject(project); Paint();
                var until = DateTime.UtcNow.AddSeconds(30);
                while (view.StarRatingsRefreshing && DateTime.UtcNow < until) { Thread.Sleep(10); Paint(); }
                var source = view.DifficultyTabBounds(3); var target = view.DifficultyTabBounds(0);
                view.PointerDown(source.X + 40, 62, 0, false, false);
                view.PointerMove(target.X + 5, 62, false, false); Paint("drag");
                view.PointerUp(target.X + 5, 62, 0); Paint("reordered");
                if (view.DifficultyTabBounds(3).X >= view.DifficultyTabBounds(0).X)
                    throw new InvalidOperationException("Native difficulty drag did not reorder tabs.");
                var pin = view.DifficultyTabBounds(2);
                view.PointerDown(pin.X + 40, 62, 2, false, false); Paint("pin-menu");
                view.PointerDown(pin.X + 50, 80, 0, false, false); view.PointerUp(pin.X + 50, 80, 0); Paint("pinned");
                if (view.DifficultyTabBounds(2).X >= view.DifficultyTabBounds(3).X)
                    throw new InvalidOperationException("Pinned difficulty did not move first.");
                source = view.DifficultyTabBounds(0); target = view.DifficultyTabBounds(2);
                view.PointerDown(source.X + 40, 62, 0, false, false);
                view.PointerMove(target.X + 5, 62, false, false); Paint();
                view.PointerUp(target.X + 5, 62, 0); Paint("pin-boundary");
                if (view.DifficultyTabBounds(2).X >= view.DifficultyTabBounds(0).X || view.IsDirty || view.WantsCapture)
                    throw new InvalidOperationException("Native tab drag crossed the pin boundary or changed content.");

                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/difficulty-tabs-native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}-{dpi}.bmp")));
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
