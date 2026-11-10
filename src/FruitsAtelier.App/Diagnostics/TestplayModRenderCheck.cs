using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class TestplayModRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    internal static void Run(D2DCanvas canvas, string folder)
    {
        string language = L.Language;
        try
        {
            foreach (var size in new[] { (1440, 900), (980, 620) })
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1, size.Item2, 96);
                var clock = new Clock();
                var view = new EditorView(timeProvider: clock);
                var map = new MapDocument { DurationMs = 60000, ApproachRate = 8 };
                for (int i = 0; i < 250; i++) map.Fruits.Add(new() { TimeMs = 2000 + i * 200, X = 128 + i % 3 * 128 });
                view.LoadDocument(map);
                if (Environment.GetEnvironmentVariable("FRUITS_TEST_SKIN") is { } skin) view.LoadSkin(skin);
                view.ToggleTestplayMod(CatchMods.HardRock);
                view.ToggleTestplayMod(CatchMods.Hidden);
                view.OpenTestplayTab();
                string prefix = Path.Combine(folder, $"mods-{locale}-{size.Item1}");
                TestplayRenderCheck.Capture(canvas, view, size.Item1, size.Item2, prefix + "-tab.bmp");
                var previewTab = view.DockTabBounds(EditorView.DockPanel.Preview);
                view.PointerDown(previewTab.X + 5, previewTab.Y + 5, 0, false, false);
                view.PointerUp(previewTab.X + 5, previewTab.Y + 5, 0);
                TestplayRenderCheck.Capture(canvas, view, size.Item1, size.Item2, prefix + "-preview.bmp");
                view.StartTestplay();
                clock.Milliseconds += 1200;
                TestplayRenderCheck.Capture(canvas, view, size.Item1, size.Item2, prefix + "-hidden.bmp");
                if (!view.IsTestplaying) throw new InvalidOperationException("Mod render fixture did not enter testplay.");
                view.StopTestplay();
                view.ToggleTestplayMod(CatchMods.Hidden);
                view.ToggleTestplayMod(CatchMods.Flashlight);
                view.StartTestplay();
                clock.Milliseconds += 1200;
                TestplayRenderCheck.Capture(canvas, view, size.Item1, size.Item2, prefix + "-flashlight.bmp");
                view.StopTestplay();
                if (view.IsDirty || !view.Document.ContentEquals(map)) throw new InvalidOperationException("Mod rendering edited map content.");
            }
            canvas.Resize(640, 480, 96);
            canvas.Begin();
            canvas.Fill(new(0, 0, 640, 480), 0xFFFFFF);
            canvas.Flashlight(new(0, 0, 640, 480), 320, 240, 100);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                int Sample(int x, int y) => frame.Pixels[(y * frame.Width + x) * 4];
                if (Sample(320, 240) < 250 || Sample(470, 240) > 5 || Math.Abs(Sample(440, 240) - 128) > 12)
                    throw new InvalidOperationException("Flashlight radius or smoothstep alpha is incorrect.");
            });
            AppLog.Write("Testplay mods native controls, skin HUD and flashlight pixels passed.");
        }
        finally { L.SetLanguage(language); }
    }
}
