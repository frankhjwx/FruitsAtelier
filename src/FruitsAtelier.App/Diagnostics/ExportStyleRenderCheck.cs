using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class ExportStyleRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/export-style-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (760, 580), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 5000, ApproachRate = 8 };
                foreach (var (time, x) in new[] { (1000, 100), (1100, 150), (1200, 250), (1300, 350), (1400, 400) })
                    map.Fruits.Add(new() { TimeMs = time, X = x });
                var editor = new EditorView(false); editor.LoadDocument(map);
                editor.UpdateTransport(1000, 5000, true, false, false, null, null);
                canvas.Resize(size.Item1, size.Item2, 96);
                Paint(); editor.OpenSettings(); Paint();
                var r = editor.SettingsBounds;
                Click(r.X + 40, r.Y + 96 + 7 * 48);
                editor.Wheel(r.Right - 80, r.Y + 220, -1200, false); Paint("settings-off");
                var toggle = editor.ExportStyleToggleBounds;
                if (toggle.Height == 0 || toggle.Bottom >= r.Bottom - 80)
                    throw new InvalidOperationException("Export style toggle is not accessible above the footer.");
                Click(toggle.X + 10, toggle.Y + toggle.Height / 2); Paint("settings-on");
                editor.ApplySettings(persist: false); editor.KeyDown(27, false, false);
                editor.ToggleOsuCanvas(); Paint("preview");
                if (!editor.LibrarySettings.ExperimentalExportStyle || editor.IsDirty || !map.ContentEquals(editor.Document))
                    throw new InvalidOperationException("Native export style toggle failed or changed content.");
                void Click(float x, float y)
                { editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint(); }
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height);
                        output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(frame.Pixels.Length);
                        output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Experimental export style native settings and preview passed.");
    }
}
