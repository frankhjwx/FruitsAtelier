using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SliderEndpointSnapRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/slider-endpoint-snap/native");
        Directory.CreateDirectory(folder);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,700\n[HitObjects]\n100,192,875,2,0,L|350:192,1,250\n");
                var editor = new EditorView(false); editor.LoadDocument(map); editor.ToggleOsuCanvas();
                editor.UpdateTransport(900, 10000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi); Paint();
                var head = editor.OsuCanvasScreen(100, 192); Click(head.X, head.Y);
                editor.ShowSliderInspector(); Paint();
                Reach(() => editor.SliderEndpointToggleBounds);
                var toggle = editor.SliderEndpointToggleBounds; Click(toggle.X + 10, toggle.Y + 10);
                if (editor.Document.ImportedSliders.Single().EndpointSnap != new SliderEndpointSnap())
                    throw new InvalidOperationException("Native Inspector did not enable endpoint snapping.");
                Reach(() => editor.SliderEndpointEndBounds);
                Paint("inspector");
                var end = editor.SliderEndpointEndBounds; Click(end.X + 10, end.Y + 10);
                Paint("menu");
                var menu = (Rect)typeof(EditorView).GetField("contextBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
                if (menu.Y < 0 || menu.Bottom > size.Item2) throw new InvalidOperationException("Endpoint menu exceeds the window.");
                Click(menu.X + 15, menu.Y + 6 + 7 * 32 + 12);
                if (editor.Document.ImportedSliders.Single().EndpointSnap?.EndDivisor != 7)
                    throw new InvalidOperationException("Native tail snap selection failed.");
                Paint("sevenths");
                Console.WriteLine($"PASS endpoint snap native {locale} {size.Item1}x{size.Item2} {dpi} DPI");

                void Reach(Func<Rect> bounds)
                {
                    for (int i = 0; i < 20 && bounds().Width == 0; i++)
                    {
                        var panel = editor.SliderInspectorViewport;
                        editor.Wheel(panel.X + 10, panel.Y + 10, -120, false, false); Paint();
                    }
                    if (bounds().Width == 0) throw new InvalidOperationException("Endpoint controls are unreachable.");
                }
                void Click(float x, float y)
                { editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint(); }
                void Paint(string? state = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: state is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{state}-{locale}-{size.Item1}-{dpi}.bmp")));
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
