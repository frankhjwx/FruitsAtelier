using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class TimingAlignmentRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height, int dpi)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { IsDemo = false, DurationMs = 50000 };
                map.TimingPoints.AddRange([new() { TimeMs = 670, BeatLengthMs = 254.237288135593 },
                    new() { TimeMs = 12873, BeatLengthMs = 254.237288135593 },
                    new() { TimeMs = 38297, BeatLengthMs = -100, Uninherited = false, Volume = 35 }]);
                map.Fruits.Add(new() { TimeMs = 33212, X = 215 });
                var view = new EditorView(false); view.LoadDocument(map); view.CloseLibrary();
                string captureName = "prompt";
                view.CheckTimingAlignmentOnOpen(); Paint(true);
                if (!view.TimingAlignmentPromptVisible || view.TimingAlignmentObjectCount != 1 || view.TimingAlignmentPointCount != 1)
                    throw new InvalidOperationException("Native alignment prompt did not show both issue types.");
                float right = (width + Math.Min(700, width - 40)) / 2;
                float bottom = (height + 300) / 2;
                view.PointerDown(right - 260, bottom - 40, 0, false, false); view.PointerUp(right - 260, bottom - 40, 0); Paint();
                if (view.TimingAlignmentPromptVisible || view.IsDirty) throw new InvalidOperationException("Keep changed the map.");
                view.LoadDocument(map); view.CheckTimingAlignmentOnOpen(); Paint();
                view.KeyDown(13, false, false); Paint();
                if (view.Document.Fruits[0].TimeMs != 33211 || view.Document.TimingPoints[2].TimeMs != 38296 || !view.IsDirty)
                    throw new InvalidOperationException("Native alignment action failed.");
                view.KeyDown(90, true, false); Paint();
                if (view.Document.Fruits[0].TimeMs != 33212 || view.IsDirty) throw new InvalidOperationException("Native alignment undo failed.");
                view.NewProject();
                map = new() { IsDemo = false, DurationMs = 5000 };
                map.TimingPoints.Add(new() { TimeMs = 387, BeatLengthMs = 500 });
                map.BananaShowers.Add(new() { TimeMs = 386, EndTimeMs = 1386 });
                view.LoadDocument(map); view.CheckTimingAlignmentOnOpen(); captureName = "spinner-prompt"; Paint(true);
                if (!view.TimingAlignmentPromptVisible || view.TimingAlignmentObjectCount != 1 || view.TimingAlignmentPointCount != 0)
                    throw new InvalidOperationException("Native spinner alignment prompt missing.");
                view.KeyDown(27, false, false); Paint();
                if (!view.Document.ContentEquals(map) || view.IsDirty) throw new InvalidOperationException("Keeping spinner alignment changed content.");
                view.LoadDocument(map); view.CheckTimingAlignmentOnOpen(); Paint(); view.KeyDown(13, false, false); Paint();
                if (view.Document.BananaShowers[0].TimeMs != 387 || view.Document.BananaShowers[0].EndTimeMs != 1387 || !view.IsDirty)
                    throw new InvalidOperationException("Native spinner alignment failed.");
                view.KeyDown(90, true, false); Paint();
                if (!view.Document.ContentEquals(map) || view.IsDirty) throw new InvalidOperationException("Native spinner alignment undo failed.");
                view.NewProject();

                void Paint(bool capture = false)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: !capture ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/timing-alignment-native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{captureName}-{locale}-{width}x{height}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
