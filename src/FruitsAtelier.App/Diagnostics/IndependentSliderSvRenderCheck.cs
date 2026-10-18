using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class IndependentSliderSvRenderCheck
{
    internal static void Run(D2DCanvas canvas, string folder)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n0,-50,4,2,1,70,0,1\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
                var editor = new EditorView(false); editor.LoadDocument(map); editor.ToggleOsuCanvas();
                editor.UpdateTransport(900, 5000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi); Paint();
                var head = editor.OsuCanvasScreen(100, 192);
                editor.PointerDown(head.X, head.Y, 0, false, false); editor.PointerUp(head.X, head.Y, 0);
                editor.ShowSliderInspector(); Paint();
                for (int i = 0; i < 20 && editor.LegacySliderSvBounds.Width == 0; i++)
                {
                    var panel = editor.SliderInspectorViewport;
                    editor.Wheel(panel.X + 10, panel.Y + 10, -120, false, false); Paint();
                }
                var input = editor.LegacySliderSvBounds;
                if (input.Width == 0) throw new InvalidOperationException("Native Legacy SV input is unreachable.");
                editor.PointerDown(input.X + 10, input.Y + 10, 0, false, false); editor.PointerUp(input.X + 10, input.Y + 10, 0);
                editor.KeyDown('A', true, false); editor.KeyUp('A'); editor.TextInput('3');
                editor.KeyDown(13, false, false); editor.KeyUp(13); Wait();
                if (editor.Document.ImportedSliders.Single().SliderVelocityMultiplier != 3)
                    throw new InvalidOperationException("Native Inspector SV input did not commit.");
                Paint("value");
                var before = editor.Document.DeepClone();
                var from = editor.OsuCanvasScreen(300, 192); var to = editor.OsuCanvasScreen(400, 192);
                editor.PointerDown(from.X, from.Y, 0, false, false); editor.PointerMove(to.X, to.Y, true, false);
                editor.PointerUp(to.X, to.Y, 0, true); Wait();
                double duration = ImportedSliderConverter.DurationMs(editor.Document, editor.Document.ImportedSliders.Single());
                double original = ImportedSliderConverter.DurationMs(before, before.ImportedSliders.Single());
                if (Math.Abs(duration - original) > .01 || editor.Document.TimingPoints.Count != map.TimingPoints.Count)
                    throw new InvalidOperationException("Native Shift gesture changed duration or added timing.");
                Paint("shift");
                void Wait()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    do { Paint(); Thread.Sleep(2); } while (editor.LegacyDropletRefreshBusy && DateTime.UtcNow < deadline);
                    if (editor.LegacyDropletRefreshBusy) throw new InvalidOperationException("Native SV validation timed out.");
                }
                void Paint(string? state = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: state is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"independent-sv-{state}-{locale}-{size.Item1}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Native independent Legacy SV input and Shift duration checks passed.");
    }
}
