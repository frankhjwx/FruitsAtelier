using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class LegacyDropletModeRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/legacy-droplet-modes/native");
        Directory.CreateDirectory(folder);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n256,192,1000,2,0,L|256:392,1,200\n");
                var editor = new EditorView(false); editor.LoadDocument(map); editor.ToggleOsuCanvas();
                editor.UpdateTransport(1000, 6000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi); Paint();
                var head = editor.OsuCanvasScreen(256, 192); Click(head.X, head.Y);
                editor.ShowSliderInspector(); Paint();
                foreach (var mode in new[] { SliderDropletMode.Randomize, SliderDropletMode.Derandomize, SliderDropletMode.FollowMap })
                {
                    for (int i = 0; i < 25 && editor.SliderDropletModeBounds.Width == 0; i++)
                    {
                        var panel = editor.SliderInspectorViewport;
                        editor.Wheel(panel.X + 10, panel.Y + 10, -120, false, false); Paint();
                    }
                    var button = editor.SliderDropletModeBounds;
                    if (button.Width == 0) throw new InvalidOperationException("Legacy droplet mode is unreachable.");
                    Click(button.X + 10, button.Y + 10);
                    var menu = (Rect)typeof(EditorView).GetField("contextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                    if (menu.Y < 0 || menu.Bottom > size.Item2) throw new InvalidOperationException("Legacy droplet menu exceeds the window.");
                    Paint("menu");
                    Click(menu.X + 15, menu.Y + 6 + (int)mode * 32 + 12);
                    if (editor.Document.ImportedSliders.Single().DropletMode != mode)
                        throw new InvalidOperationException("Native Legacy droplet mode selection failed: " + editor.StatusMessage);
                    Paint(mode.ToString());
                }
                Console.WriteLine($"PASS Legacy droplet modes native {locale} {size.Item1}x{size.Item2} {dpi} DPI");

                void Click(float x, float y)
                { editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint(); }
                void Paint(string? state = null)
                {
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2); canvas.End();
                        if (!editor.ConversionRefreshing) break;
                        if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException("Legacy droplet mode validation timed out.");
                        Thread.Sleep(5);
                    } while (true);
                    if (state is null) return;
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: () =>
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
