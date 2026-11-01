using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SliderBpmRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/slider-bpm/native");
        Directory.CreateDirectory(folder);
        string language = L.Language;
        const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { EnableSliderBpmMultiplier = true, DurationMs = 10000 };
                var track = new CurveTrack { BpmMultiplier = 4, CompensateTinyDroplets = false };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 200 }]);
                map.Tracks.Add(track);
                var editor = new EditorView(false); editor.LoadDocument(map);
                canvas.Resize(size.Item1, size.Item2, 96); Paint();
                for (int i = 0; editor.LibraryOpening && i < 1000; i++) { Thread.Sleep(5); Paint(); }
                var box = editor.BpmMultiplierBounds; Click(box.X + 10, box.Y + 10); Paint("presets");
                var menu = (Rect)typeof(EditorView).GetField("contextBounds", hidden)!.GetValue(editor)!;
                Click(menu.X + 20, menu.Y + 6 + 7 * 32 + 12);
                Paint("custom");
                box = editor.BpmMultiplierBounds; Click(box.Right - 20, box.Y + 12);
                editor.KeyDown(65, true, false);
                foreach (char c in "3.5") editor.TextInput(c);
                editor.KeyDown(13, false, false); Paint();
                if (editor.Document.DefaultSliderBpmMultiplier != 3.5)
                    throw new InvalidOperationException("Native custom BPM input failed.");
                if (!editor.DisplayedTiming.Any(p => p.Uninherited && p.TimeMs == 1000) || editor.ShowTimingSv)
                    throw new InvalidOperationException("Aspire red lines are hidden.");
                editor.ShowSongDock(); Paint();
                typeof(EditorView).GetField("songTab", hidden)!.SetValue(editor, 4); Paint("settings");
                editor.ShowSliderInspector();
                typeof(EditorView).GetMethod("SelectObjects", hidden)!.Invoke(editor, [new[] { track.Id }, track.Id]);
                Paint("inspector");
                Console.WriteLine($"PASS native BPM {locale} {size.Item1}x{size.Item2}");

                void Click(float x, float y) { editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint(); }
                void Paint(string? state = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: state is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{state}-{locale}-{size.Item1}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
