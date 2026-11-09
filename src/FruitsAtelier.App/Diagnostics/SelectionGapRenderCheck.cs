using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SelectionGapRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/selection-gap/native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { IsDemo = false, DurationMs = 10000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1250, X = 400 }]);
                var editor = new EditorView(false); editor.LoadDocument(map);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint(); editor.KeyDown('A', true, false); Paint("gap");
                if (editor.SelectedObjectGapMs != 250 || editor.MovementOverlayBounds is null)
                    throw new InvalidOperationException("Native DS Indicator interval is missing.");
                editor.ToggleOsuCanvas(); Paint("osu");
                if (editor.SelectedObjectGapMs is not null) throw new InvalidOperationException("osu canvas shows a selection gap.");
                editor.OpenSettings(); Paint();
                var settings = editor.SettingsBounds;
                Click(settings.X + 60, settings.Y + 78 + 2 * Math.Min(48, (settings.Height - 108) / 10) + 12);
                var stack = editor.TestplayFruitStackBounds;
                if (canvas.MeasureText(L.Get("testplay.showFruitStack"), 13) > stack.Width - 36)
                    throw new InvalidOperationException("Fruit stack label is clipped.");
                Click(stack.X + 12, stack.Y + 12); Paint("appearance-off");
                if (!editor.LibrarySettings.ShowTestplayFruitStack) throw new InvalidOperationException("Settings draft applied early.");
                editor.ApplySettings(Path.Combine(folder, "settings.json")); Paint();
                if (editor.LibrarySettings.ShowTestplayFruitStack || editor.IsDirty || !editor.Document.ContentEquals(map))
                    throw new InvalidOperationException("Native stack preference failed or changed map content.");
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
    }
}
