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
                Reach(() => editor.SliderInspectorSectionBounds(1));
                var section = editor.SliderInspectorSectionBounds(1); Click(section.X + 10, section.Y + 10);
                if (editor.SliderEndpointToggleBounds.Width != 0) throw new InvalidOperationException("Native Inspector section did not collapse.");
                Paint("collapsed");
                section = editor.SliderInspectorSectionBounds(1); Click(section.X + 10, section.Y + 10);
                var catchMap = new MapDocument { DurationMs = 10000, BeatLengthMs = 700, DerandomizeFSliderDroplets = true };
                var track = new CurveTrack { Kind = CurveKind.Linear, EndpointSnap = new() };
                track.Nodes.Add(new() { TimeMs = 875, X = 100 }); track.Nodes.Add(new() { TimeMs = 2240, X = 350 });
                catchMap.Tracks.Add(track); ObjectFlags.SetNewCombo(catchMap, track.Id, true);
                editor.ToggleOsuCanvas(); editor.LoadDocument(catchMap); Paint();
                editor.Wheel(editor.PlayfieldBounds.X, editor.PlayfieldBounds.Bottom,
                    (float)(120 * Math.Log(.1 / editor.PixelsPerMs) / Math.Log(1.16)), false, false, true);
                typeof(EditorView).GetMethod("SelectObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(editor, [new[] { track.Id }, track.Id]);
                editor.ShowSliderInspector(); Paint("catch-positions-nc");
                var limitMap = new MapDocument { DurationMs = 5000, BeatLengthMs = 60000d / 185,
                    SliderMultiplier = 1.2, SliderTickRate = 2 };
                double start = 1000 + limitMap.BeatLengthMs / 12;
                var limitTrack = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
                limitTrack.Nodes.AddRange([new() { TimeMs = start, X = 470 },
                    new() { TimeMs = start + limitMap.BeatLengthMs, X = 300 },
                    new() { TimeMs = start + limitMap.BeatLengthMs * 1.25, X = 27 }]);
                limitMap.Tracks.Add(limitTrack);
                editor.LoadDocument(limitMap); Paint();
                var target = editor.Conversion.Objects.Last(o => o.Kind == CatchObjectKind.Droplet);
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var point = ((float X, float Y))typeof(EditorView).GetMethod("Screen", hidden)!
                    .Invoke(editor, [new MapPoint(target.TimeMs, target.X)])!;
                typeof(EditorView).GetMethod("BeginSliderObjectDrag", hidden)!.Invoke(editor, [target, point.X, point.Y]);
                editor.PointerMove(point.X + (float)(450 - target.X) / 512 * editor.PlayfieldBounds.Width, point.Y, false, false);
                if (editor.StatusMessage != L.Get("coordinate.sliderVelocityLimit"))
                    throw new InvalidOperationException("Native drag did not report the 10x SV limit: " + editor.StatusMessage);
                Paint("sv-limit-warning");
                editor.CancelInteraction();
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
