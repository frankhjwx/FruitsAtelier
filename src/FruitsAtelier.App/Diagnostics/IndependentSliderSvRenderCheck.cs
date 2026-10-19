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
            foreach (int dpi in new[] { 96, 120, 144, 168, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n0,-50,4,2,1,70,0,1\n[HitObjects]\n100,192,1000,2,0,P|300:64|100:320,1,400\n");
                map.ImportedSliders.Single().PreserveNativeGeometry = true;
                map.DerandomizeFSliderDroplets = true;
                var processed = SliderRepresentationEditing.RefreshLegacyDroplets(map);
                if (!processed.Success) throw new InvalidOperationException(string.Join("; ", processed.Diagnostics));
                var editor = new EditorView(false); editor.LoadDocument(map); editor.ToggleOsuCanvas();
                editor.UpdateTransport(900, 5000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi); Paint();
                var zoomTrack = editor.OsuToolbarZoomBounds;
                double oldWidth = editor.OsuCanvasBounds.Width;
                editor.PointerDown(zoomTrack.X + zoomTrack.Width * .4f, zoomTrack.Y + 15, 0, false, false);
                editor.PointerUp(zoomTrack.X + zoomTrack.Width * .4f, zoomTrack.Y + 15, 0); Paint();
                if (editor.OsuCanvasBounds.Width <= oldWidth) throw new InvalidOperationException("Native osu toolbar zoom did not respond.");
                editor.PointerDown(zoomTrack.X + zoomTrack.Width * .2f, zoomTrack.Y + 15, 0, false, false);
                editor.PointerUp(zoomTrack.X + zoomTrack.Width * .2f, zoomTrack.Y + 15, 0); Paint();
                var analysisButton = editor.OsuMovementAnalysisBounds;
                bool analysis = editor.MovementAnalysisEnabled;
                ClickAnalysis();
                if (editor.MovementAnalysisEnabled == analysis) throw new InvalidOperationException("Native osu Movement Analysis did not respond.");
                ClickAnalysis();
                void ClickAnalysis()
                {
                    editor.PointerDown(analysisButton.X + 8, analysisButton.Y + 8, 0, false, false);
                    editor.PointerUp(analysisButton.X + 8, analysisButton.Y + 8, 0); Paint();
                }
                var defaultTrack = editor.OsuSliderDefaultSvTrackBounds;
                if (editor.OsuSliderDefaultSvValueBounds.X <= editor.CanvasPlotBounds.Right
                    || defaultTrack.Width > 120 || editor.OsuDistanceSpacingTrackBounds.Width > 120
                    || editor.AssistButtonBounds[0].Y <= editor.OsuSliderDefaultSvValueBounds.Bottom + 26
                    || editor.AssistButtonBounds[0].Width > 76)
                    throw new InvalidOperationException("Canvas corner controls overlap or exceed their compact bounds.");
                var spacingTrack = editor.OsuDistanceSpacingTrackBounds;
                editor.PointerDown(spacingTrack.Right, spacingTrack.Y + 4, 0, false, false);
                editor.PointerUp(spacingTrack.Right, spacingTrack.Y + 4, 0); Paint();
                if (editor.Document.OsuDistanceSpacing != 3.2)
                    throw new InvalidOperationException("Corner DS track endpoint did not receive input.");
                editor.KeyDown('Z', true, false); editor.KeyUp('Z'); Paint();
                float defaultX = defaultTrack.X + (float)((2.5 - .1) / 3.1) * defaultTrack.Width;
                var unchanged = editor.Document.DeepClone();
                editor.PointerDown(defaultX, defaultTrack.Y + 4, 0, false, false);
                editor.PointerUp(defaultX, defaultTrack.Y + 4, 0); Paint();
                if (editor.LibrarySettings.OsuSliderDefaultSv != 2.5 || !unchanged.ContentEquals(editor.Document) || editor.IsDirty)
                    throw new InvalidOperationException("Native default SV control changed content or failed to update.");
                var defaultInput = editor.OsuSliderDefaultSvValueBounds;
                editor.PointerDown(defaultInput.X + 8, defaultInput.Y + 8, 0, false, false);
                editor.PointerUp(defaultInput.X + 8, defaultInput.Y + 8, 0);
                editor.KeyDown('A', true, false); editor.KeyUp('A');
                foreach (char ch in "4.25") editor.TextInput(ch);
                editor.KeyDown(13, false, false); editor.KeyUp(13); Paint();
                if (editor.LibrarySettings.OsuSliderDefaultSv != 4.25 || !unchanged.ContentEquals(editor.Document) || editor.IsDirty)
                    throw new InvalidOperationException("Native default SV numeric input did not commit independently.");
                Paint("corner-osu");
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
                var from = editor.OsuCanvasScreen(300, 64); var to = editor.OsuCanvasScreen(400, 64);
                editor.PointerDown(from.X, from.Y, 0, false, false); editor.PointerMove(to.X, to.Y, true, false);
                editor.PointerUp(to.X, to.Y, 0, true); Wait();
                double duration = ImportedSliderConverter.DurationMs(editor.Document, editor.Document.ImportedSliders.Single());
                double original = ImportedSliderConverter.DurationMs(before, before.ImportedSliders.Single());
                if (Math.Abs(duration - original) > .01 || editor.Document.TimingPoints.Count != map.TimingPoints.Count)
                    throw new InvalidOperationException("Native Shift gesture changed duration or added timing.");
                if (editor.Document.ImportedSliders.Single().RandomizationSource is not { ControlPoints.Count: 3 } authored
                    || authored.ControlPoints[1].X <= 300)
                    throw new InvalidOperationException("Native drawn-path control editing did not retain the original arc controls.");
                Paint("shift");
                var distanceMap = new MapDocument { BeatLengthMs = 500, SliderMultiplier = 1.4, DurationMs = 5000 };
                distanceMap.Fruits.Add(new() { TimeMs = 1000, X = 128, Y = 192 });
                distanceMap.Fruits.Add(new() { TimeMs = 1500, X = 212, Y = 192 });
                editor.LoadDocument(distanceMap); editor.UpdateTransport(900, 5000, true, false, false, null, null); Paint();
                if (!editor.MovementAnalysisEnabled) ClickAnalysis();
                Paint("ds");
                editor.UpdateTransport(1500, 5000, true, false, false, null, null); Paint();
                if (!editor.DistanceSnapEnabled) { editor.KeyDown('Y', false, false); editor.KeyUp('Y'); Paint(); }
                var dragFrom = editor.OsuCanvasScreen(212, 192); var dragTo = editor.OsuCanvasScreen(400, 192);
                editor.PointerDown(dragFrom.X, dragFrom.Y, 0, false, false);
                editor.PointerMove(dragTo.X, dragTo.Y, false, false); editor.PointerUp(dragTo.X, dragTo.Y, 0); Wait();
                var snappedFruit = editor.Document.Fruits.Last();
                if (Math.Abs(snappedFruit.X - 268) > .001 || Math.Abs(snappedFruit.Y - 192) > .001)
                    throw new InvalidOperationException("Native osu DS dragging missed the preceding object.");
                Paint("ds-drag");
                editor.ToggleOsuCanvas(); Paint();
                editor.KeyDown('A', true, false); editor.KeyUp('A'); Paint("corner-catch");
                if (editor.AssistButtonBounds[0].Y < editor.CanvasPlotBounds.Y + 78)
                    throw new InvalidOperationException("Catch coordinate rows overlap the assist buttons.");
                var firstButton = editor.AssistButtonBounds[0];
                int flagsBefore = ObjectFlags.NewCombo(editor.Document, editor.Document.Fruits[0].Id) ? 1 : 0;
                editor.PointerDown(firstButton.X + firstButton.Width / 2, firstButton.Y + firstButton.Height / 2, 0, false, false);
                editor.PointerUp(firstButton.X + firstButton.Width / 2, firstButton.Y + firstButton.Height / 2, 0); Paint();
                int flagsAfter = ObjectFlags.NewCombo(editor.Document, editor.Document.Fruits[0].Id) ? 1 : 0;
                if (flagsBefore == flagsAfter) throw new InvalidOperationException("Compact assist button did not receive input.");
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
