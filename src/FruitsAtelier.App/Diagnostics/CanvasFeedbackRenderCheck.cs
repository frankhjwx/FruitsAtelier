using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class CanvasFeedbackRenderCheck
{
    internal static void Run(D2DCanvas canvas, string parent)
    {
        string folder = Path.Combine(parent, "feedback"); Directory.CreateDirectory(folder);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:0\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,100,1000,2,0,L|200:200,1,141.421356237\n");
                map.DurationMs = 10000;
                var editor = new EditorView(false); editor.LoadDocument(map); editor.ToggleOsuCanvas();
                editor.UpdateTransport(1000, 10000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi); Paint();
                editor.KeyDown(65, true, false); editor.KeyDown('R', true, true); Paint("rotation");
                if (!editor.RotationVisible) throw new InvalidOperationException("Native rotation dialog did not open.");
                editor.TextInput('9'); editor.TextInput('0');
                editor.KeyDown(13, false, false); Paint("rotated");
                if (editor.RotationVisible || editor.Document.ImportedSliders[0].TimeMs != 1000)
                    throw new InvalidOperationException("Native rotation dialog did not apply.");
                if (editor.Document.ImportedSliders[0].X != 348 || !OsuBeatmapWriter.Serialize(editor.Document).ObjectSequenceMatches)
                    throw new InvalidOperationException("Native rotation failed geometry publication or export.");
                editor.KeyDown('Z', true, false); editor.KeyDown(65, true, false);
                editor.KeyDown('F', true, true); Paint("stream");
                if (!editor.StreamDialogVisible) throw new InvalidOperationException("Native Stream dialog did not open.");
                editor.KeyDown(13, false, false); Paint("stream-result");
                if (editor.Document.Fruits.Select(f => f.Y).Distinct().Count() < 2)
                    throw new InvalidOperationException("Native Stream discarded geometric Y.");
                editor.KeyDown('Z', true, false); editor.KeyDown('P', true, true); Paint("timing-audio");
                if (!editor.TimingFields.Any(f => f.Key == "volume"))
                    throw new InvalidOperationException("Native inherited timing shortcut did not open Audio.");
                editor.KeyDown(27, false, false);
                if (!editor.Document.ContentEquals(map)) throw new InvalidOperationException("Native feedback checks did not restore content.");

                void Paint(string? name = null)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2); canvas.End();
                        if (watch.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Native feedback validation did not settle.");
                    } while (editor.LegacyDropletRefreshBusy);
                    if (name is null) return;
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
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
