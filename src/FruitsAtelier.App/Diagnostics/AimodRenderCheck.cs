using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class AimodRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height, int dpi = 96)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 20000 };
                for (int i = 0; i < 60; i++) map.Fruits.Add(new() { TimeMs = 1000 + i * 5, X = i % 2 == 0 ? 100 : 400 });
                var view = new EditorView(false); view.LoadDocument(map); Paint();
                view.KeyDown(65, true, true); Paint(capture: true);
                view.KeyDown(46, false, false); view.KeyDown(116, false, false); Paint();
                if (!view.AimodVisible || view.AimodErrors.Count != 59 || view.IsTestplaying || !view.Document.ContentEquals(map))
                    throw new InvalidOperationException("Native AiMod check or input isolation failed.");
                view.Wheel(width / 2, height / 2, -1200, false); Paint();
                view.KeyDown(27, false, false); Paint();
                if (view.AimodVisible || !view.Document.ContentEquals(map)) throw new InvalidOperationException("Native AiMod close changed content.");
                view.NewProject();
                void Paint(bool capture = false)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: !capture ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/aimod-native");
                        Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{locale}-{width}x{height}-{dpi}.bmp")));
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
