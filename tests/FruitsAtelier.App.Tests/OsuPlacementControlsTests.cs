using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class OsuPlacementControlsTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 })
            foreach (bool previousSlider in new[] { false, true })
            foreach (int tool in new[] { (int)'2', (int)'3' })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(width, 900);
                var map = new MapDocument { BeatLengthMs = 500, SliderMultiplier = 1.4, DurationMs = 5000, OsuDistanceSpacing = .6 };
                if (previousSlider)
                {
                    var source = new ImportedSlider { TimeMs = 750, X = 32, Y = 192, PathType = 'L', PixelLength = 140, SliderVelocityMultiplier = 2 };
                    source.ControlPoints.AddRange([new(32, 192), new(172, 192)]); map.ImportedSliders.Add(source);
                }
                else map.Fruits.Add(new() { TimeMs = 1000, X = 128, Y = 192 });
                double expectedX = previousSlider ? 340 : 212;
                ui.LoadDocument(map); ui.View.ToggleOsuCanvas();
                ui.View.UpdateTransport(1500, 5000, true, false, false, null, null); ui.Paint();
                if (!ui.View.DistanceSnapEnabled) ui.Key('Y');
                ui.Key(tool);
                var pointer = ui.View.OsuCanvasScreen(400, 192);
                var expected = ui.View.OsuCanvasScreen(expectedX, 192);
                ui.View.PointerMove(pointer.X, pointer.Y, false, false); ui.Paint();
                Check(ui.Canvas.Circles.Any(c => c.Filled && Math.Abs(c.X - expected.X) < .05 && Math.Abs(c.Y - expected.Y) < .05),
                    "Distance Snap did not affect the circle/slider placement preview.");
                ui.Click(pointer.X, pointer.Y);
                if (tool == '3')
                {
                    var end = ui.View.OsuCanvasScreen(300, 256); ui.Click(end.X, end.Y); ui.Key(13);
                    Check(ui.View.Document.ImportedSliders.Last().ControlPoints[0] == new SliderPathPoint(expectedX, 192),
                        "Distance Snap did not affect the Legacy slider head.");
                }
                else Check(ui.View.Document.Fruits.Last().X == expectedX && ui.View.Document.Fruits.Last().Y == 192,
                    "Distance Snap did not affect circle placement.");
                ui.Key('1'); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.canvasZoom"))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.snap"))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("timeline.insertBreak")), "osu toolbar controls are missing.");
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                var zoom = (Rect)typeof(EditorView).GetField("zoomSlider", hidden)!.GetValue(ui.View)!;
                double oldWidth = ui.View.OsuCanvasBounds.Width;
                var baseline = ui.View.Document.DeepClone();
                ui.Click(zoom.X + zoom.Width * .8f, zoom.Y + 15);
                Check(Math.Abs(ui.View.OsuCanvasBounds.Width - oldWidth) > 1 && baseline.ContentEquals(ui.View.Document),
                    "osu zoom slider did not change only geometric zoom.");
                var texts = ((string First, string? Second, float Size)[])typeof(EditorView).GetField("canvasToggleText", hidden)!.GetValue(ui.View)!;
                bool analysis = ui.View.MovementAnalysisEnabled;
                ui.ClickText(texts[2].First);
                Check(ui.View.MovementAnalysisEnabled != analysis && baseline.ContentEquals(ui.View.Document),
                    "osu Movement Analysis toolbar toggle did not work independently of content.");
                if (!ui.View.MovementAnalysisEnabled) ui.ClickText(texts[2].First);
                Check(ui.Canvas.Lines.Any(l => l.Width == 3 && l.Opacity == .65f),
                    "osu Movement Analysis did not draw the connection between objects.");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("assist.ratio", .6)),
                    "osu Movement Analysis DS does not match geometric placement spacing and the preceding slider's SV.");
                ui.ClickText(texts[2].First);
                Check(!ui.Canvas.Lines.Any(l => l.Width == 3 && l.Opacity == .65f),
                    "Disabling osu Movement Analysis retained connection lines.");
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
