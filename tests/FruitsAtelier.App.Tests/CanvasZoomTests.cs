using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class CanvasZoomTests
{
    public static void DenseCoordinateProjection()
    {
        var ui = new Ui(false);
        var project = typeof(EditorView).GetMethod("Screen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Func<MapPoint, (float X, float Y)>>(ui.View);
        foreach (var size in new[] { (980, 620), (1440, 900) })
        {
            ui.Resize(size.Item1, size.Item2);
            ClickZoom(ui, .46f);
            ui.View.UpdateTransport(110000, 300000, true, true, false, null, "fixture.wav"); ui.Paint();
            var expected = ui.ScreenAt(110000, 128);
            var actual = project(new(110000, 128));
            Near(expected.X, actual.X); Near(expected.Y, actual.Y);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            double checksum = 0;
            for (int i = 0; i < 10000; i++)
            {
                var point = project(new(110000 + i % 9, 128 + i % 256));
                checksum += point.X + point.Y;
            }
            if (!double.IsFinite(checksum) || GC.GetAllocatedBytesForCurrentThread() - allocated > 4096)
                throw new Exception("Dense coordinate projection allocated per object.");
        }
    }

    public static void DefaultsAndReset()
    {
        var ui = new Ui(overview: false);
        Near(.6, ui.View.CanvasZoom);
        AssertScale(ui);
        if (ui.View.CanvasPlotBounds.X > ui.View.DifficultyCurvePanelBounds.Right + 180
            || ui.Canvas.Texts.Any(t => t.Value is "Objects" or "对象"))
            throw new Exception("Objects panel still occupies the canvas layout");
        ClickZoom(ui, 0);
        Near(EditorView.MinimumPlayfieldWidth, ui.Plot.Width);
        ui.Resize(980, 620);
        if (ui.Plot.Width < EditorView.MinimumPlayfieldWidth - .001) throw new Exception("Resize bypassed minimum width");
        double remembered = ui.View.LibrarySettings.CanvasZoom;
        ui.Resize(1440, 900);
        ui.ClickText(L.Get("ui.resetView"));
        Near(remembered, ui.View.CanvasZoom);
        AssertScale(ui);
        if (ui.View.IsDirty) throw new Exception("View defaults changed document content");
        ClickZoom(ui, .5f);
        var map = new MapDocument { DurationMs = 30000 };
        map.Fruits.Add(new Fruit { TimeMs = 100, X = 256 });
        ui.Resize(1440, 900); ui.View.LoadDocument(map); ui.Paint();
        ui.OpenPreview();
        float fullWidth = ui.Plot.Width;
        float mainRadius = FruitRadius(false), previewRadius = FruitRadius(true);
        double visibleDuration = ui.Plot.Height / ui.View.PixelsPerMs;
        ClickZoom(ui, 0);
        Near(mainRadius * ui.Plot.Width / fullWidth, FruitRadius(false));
        Near(previewRadius, FruitRadius(true));
        if (ui.Plot.Height / ui.View.PixelsPerMs <= visibleDuration) throw new Exception("Smaller objects did not expose more time");

        float FruitRadius(bool preview) => ui.Canvas.Circles.Single(c => c.Filled && c.Color == 0xFFFFFF
            && (preview ? c.X > ui.View.CanvasPlotBounds.Right : Math.Abs(c.X - (ui.Plot.X + ui.Plot.Width / 2)) < .01)).Radius;
    }

    public static void SliderAndWheel()
    {
        var ui = new Ui(overview: false);
        var original = ui.View.Document.DeepClone();
        var plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X + 80, plot.Y + 80, 480, false); ui.Paint();
        double centre = ui.View.ViewStartMs + plot.Height / 2 / ui.View.PixelsPerMs;
        double initialWidth = ui.Plot.Width, initialSpacing = ui.View.PixelsPerMs;
        ClickZoom(ui, .5f);
        Near(centre, ui.View.ViewStartMs + plot.Height / 2 / ui.View.PixelsPerMs);
        Near(ui.Plot.Width / initialWidth, ui.View.PixelsPerMs / initialSpacing);
        Near((plot.X + plot.Right) / 2, (ui.Plot.X + ui.Plot.Right) / 2);
        double scale = ui.View.PixelsPerMs;
        ui.View.Wheel(plot.X + 80, plot.Y + 80, 120, false, false, true); ui.Paint();
        Near(scale * 1.16, ui.View.PixelsPerMs);
        var slider = ui.View.ZoomSliderBounds;
        ui.View.PointerDown(slider.X, slider.Y + 15, 0, false, false);
        if (!ui.View.WantsCapture) throw new Exception("Slider did not capture dragging");
        Near(EditorView.MinimumPlayfieldWidth, ui.Plot.Width);
        ui.View.PointerMove(slider.Right + 100, slider.Y + 15, false, false);
        Near(1, ui.View.CanvasZoom);
        ui.View.PointerUp(slider.Right + 100, slider.Y + 15, 0); ui.Paint();
        if (ui.View.WantsCapture) throw new Exception("Slider retained capture after release");
        ui.View.Wheel(plot.X + 80, plot.Y + 80, -120000, false, false, true); ui.Paint();
        Near(EditorView.MinimumPlayfieldWidth, ui.Plot.Width);
        ui.View.Wheel(plot.X + 80, plot.Y + 80, 120000, false, false, true); ui.Paint();
        Near(1, ui.View.CanvasZoom);
        ui.Key('Z', ctrl: true);
        if (ui.View.IsDirty || !original.ContentEquals(ui.View.Document)) throw new Exception("Zoom entered document history");
    }

    public static void PlaybackAndLanguages()
    {
        string language = L.Language;
        try
        {
            foreach (string lang in new[] { "zh-CN", "en" })
            {
                L.SetLanguage(lang);
                var ui = new Ui(overview: false); ui.Resize(980, 620);
                if (!ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.canvasZoom"))
                    || ui.Canvas.Texts.Any(t => t.Value is "Match AR scale" or "还原 AR 比例")) throw new Exception("Canvas zoom labels are incorrect");
                var slider = ui.View.ZoomSliderBounds;
                if (slider.Width < 30 || slider.X < 0 || slider.Right + 56 > ui.View.CanvasPlotBounds.Right)
                    throw new Exception("Zoom slider does not fit the minimum-width header");
                ui.View.UpdateTransport(12000, 60000, true, true, false, null, "song.mp3"); ui.Paint();
                ClickZoom(ui, .5f);
                Near(12000, ui.View.PlayheadMs);
                Near(12000 - ui.Plot.Height * .25 / ui.View.PixelsPerMs, ui.View.ViewStartMs);
                ui.View.PointerDown(slider.X + 10, slider.Y + 15, 0, false, false);
                ui.Key(27);
                double scale = ui.View.PixelsPerMs;
                ui.View.PointerMove(slider.Right, slider.Y + 15, false, false);
                Near(scale, ui.View.PixelsPerMs);
                if (ui.View.WantsCapture || ui.View.IsDirty) throw new Exception("Cancelled zoom affected editing state");
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void ClickZoom(Ui ui, float fraction)
    {
        var slider = ui.View.ZoomSliderBounds;
        ui.Click(slider.X + fraction * slider.Width, slider.Y + slider.Height / 2);
    }
    private static void AssertScale(Ui ui) => Near(CatchScrollTiming.PixelsPerMs(ui.View.Document.ApproachRate, ui.Plot.Width), ui.View.PixelsPerMs);
    private static void Near(double expected, double actual)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}, got {actual}");
    }
}
