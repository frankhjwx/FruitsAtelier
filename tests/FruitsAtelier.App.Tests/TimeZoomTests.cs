using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimeZoomTests
{
    public static void DefaultsAndReset()
    {
        var ui = new Ui(overview: false);
        Near(8, ui.View.Document.ApproachRate);
        AssertAr(ui);
        Near(ui.View.PlayheadMs - ui.Plot.Height * 0.25 / ui.View.PixelsPerMs, ui.View.ViewStartMs);
        ui.Resize(980, 620);
        AssertAr(ui);
        ClickZoom(ui, 0.8f);
        ui.ClickText(L.Get("ui.resetView"));
        AssertAr(ui);
        var document = ui.View.Document.DeepClone();
        document.ApproachRate = 6.5;
        ui.View.LoadDocument(document); ui.Paint();
        AssertAr(ui);
        Near(6.5, ui.View.Document.ApproachRate);
        if (ui.View.IsDirty) throw new Exception("View defaults changed document content.");
    }

    public static void SliderAndWheel()
    {
        var ui = new Ui(overview: false);
        var original = ui.View.Document.DeepClone();
        var plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X + 80, plot.Y + 80, 480, false); ui.Paint();
        double centre = ui.View.ViewStartMs + plot.Height / 2 / ui.View.PixelsPerMs;
        ClickZoom(ui, 0.6f);
        Near(centre, ui.View.ViewStartMs + plot.Height / 2 / ui.View.PixelsPerMs);
        double scale = ui.View.PixelsPerMs;
        ui.View.Wheel(plot.X + 80, plot.Y + 80, 120, true); ui.Paint();
        Near(scale * 1.16, ui.View.PixelsPerMs);
        var slider = ui.View.ZoomSliderBounds;
        double preempt = 440 / ui.View.PixelsPerMs * ui.Plot.Width / 512;
        float fraction = (float)((preempt >= 1200 ? 5 - (preempt - 1200) / 120 : 5 + (1200 - preempt) / 150) / 10);
        if (!ui.Canvas.Circles.Any(c => c.Color == 0x59D3C3 && Math.Abs(c.X - (slider.X + fraction * slider.Width)) < 0.01 && c.Y == 103))
            throw new Exception("Slider thumb did not follow wheel zoom.");
        ui.View.PointerDown(slider.X, slider.Y + 15, 0, false, false);
        if (!ui.View.WantsCapture) throw new Exception("Slider did not capture dragging.");
        Near(CatchScrollTiming.PixelsPerMs(0, ui.Plot.Width), ui.View.PixelsPerMs);
        ui.View.PointerMove(slider.Right + 100, slider.Y + 15, false, false);
        Near(CatchScrollTiming.PixelsPerMs(10, ui.Plot.Width), ui.View.PixelsPerMs);
        ui.View.PointerUp(slider.Right + 100, slider.Y + 15, 0); ui.Paint();
        if (ui.View.WantsCapture) throw new Exception("Slider retained capture after release.");
        ui.ClickText(L.Get("ui.restoreAr"));
        AssertAr(ui);
        ui.Key('Z', ctrl: true);
        if (ui.View.IsDirty || !original.ContentEquals(ui.View.Document)) throw new Exception("Zoom entered document history.");
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
                if (!ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.timeZoom"))) throw new Exception("Missing zoom label.");
                var slider = ui.View.ZoomSliderBounds;
                if (slider.Width < 80 || slider.X < ui.Plot.X || slider.Right + 56 > ui.View.CanvasPlotBounds.Right - 164)
                    throw new Exception("Zoom slider does not fit the minimum-width header.");
                ui.View.UpdateTransport(12000, 60000, true, true, false, null, "song.mp3"); ui.Paint();
                ClickZoom(ui, 0.5f);
                Near(12000, ui.View.PlayheadMs);
                Near(12000 - ui.Plot.Height * 0.25 / ui.View.PixelsPerMs, ui.View.ViewStartMs);
                ui.View.PointerDown(slider.X + 10, slider.Y + 15, 0, false, false);
                ui.Key(27);
                double scale = ui.View.PixelsPerMs;
                ui.View.PointerMove(slider.Right, slider.Y + 15, false, false);
                Near(scale, ui.View.PixelsPerMs);
                if (ui.View.WantsCapture || ui.View.IsDirty) throw new Exception("Cancelled slider drag affected editing state.");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void ClickZoom(Ui ui, float fraction)
    {
        var slider = ui.View.ZoomSliderBounds;
        ui.Click(slider.X + fraction * slider.Width, slider.Y + slider.Height / 2);
    }
    private static void AssertAr(Ui ui) => Near(CatchScrollTiming.PixelsPerMs(ui.View.Document.ApproachRate, ui.Plot.Width), ui.View.PixelsPerMs);
    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.001) throw new Exception($"Expected {expected}, got {actual}.");
    }
}
