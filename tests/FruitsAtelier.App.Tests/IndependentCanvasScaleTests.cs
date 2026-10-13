using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class IndependentCanvasScaleTests
{
    public static void Run()
    {
        CanvasZoomTests.DefaultsAndReset(); CanvasZoomTests.SliderAndWheel(); CanvasZoomTests.PlaybackAndLanguages();
        SelectionTransformTests.Run(); MappingSessionTests.Preferences();
        NoteSnapTests.SliderObjectDragging(); MixedSliderTests.Import();
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (var size in new[] { (980, 620), (1440, 900) })
        {
            L.SetLanguage(locale);
            var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
            var map = new MapDocument { DurationMs = 30000, BeatLengthMs = 480 };
            map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 }); ui.LoadDocument(map); ui.Resize(size.Item1, size.Item2);
            var before = ui.View.Document.DeepClone();
            Check(ui.View.UniformCanvasScale, "Existing preferences start locked");
            double startWidth = ui.Plot.Width, startTime = ui.View.PixelsPerMs;
            var lockBounds = ui.View.CanvasScaleLockBounds;
            ui.Click(lockBounds.X + 10, lockBounds.Y + 10);
            Check(!ui.View.UniformCanvasScale, "Lock icon enables independent scaling");
            void Axis(int axis, float fraction)
            {
                var r = ui.View.CanvasAxisZoomBounds(axis);
                ui.View.PointerDown(r.X + r.Width / 2, r.Y + 14, 0, false, false);
                Check(ui.View.WantsCapture, "Independent axis slider captures");
                ui.View.PointerMove(r.X + r.Width * fraction, r.Y + 14, false, false);
                ui.View.PointerUp(r.X + r.Width * fraction, r.Y + 14, 0); ui.Paint();
            }
            Axis(0, 1); Near(2, ui.View.CanvasZoom); Near(startTime, ui.View.PixelsPerMs);
            Check(ui.Plot.Width > startWidth, "X scaling expands map positions");
            double wide = ui.Plot.Width; Axis(1, 1);
            Near(wide, ui.Plot.Width); Near(2, ui.View.CanvasTimeZoom);
            Check(ui.View.PixelsPerMs > startTime, "Y scaling expands time independently");
            Axis(0, 0); Near(192, ui.Plot.Width); Near(2, ui.View.CanvasTimeZoom);
            Axis(0, 1); ui.Key(27);
            var plot = ui.View.CanvasPlotBounds;
            double originalX = ui.Plot.X, originalStart = ui.View.ViewStartMs;
            float x = plot.X + plot.Width / 2, y = plot.Y + plot.Height / 2;
            ui.View.PointerDown(x, y, 1, false, false);
            ui.View.PointerMove(x + 100, y, false, false); ui.View.PointerUp(x + 100, y, 1); ui.Paint();
            Near(originalX + 100, ui.Plot.X); Near(originalStart, ui.View.ViewStartMs);
            ui.View.PointerDown(x, y, 1, false, false); ui.View.PointerMove(x - 20000, y, false, false);
            ui.View.PointerUp(x - 20000, y, 1); ui.Paint();
            Check(ui.Plot.Right + 54.4 * ui.Plot.Width / 512 >= plot.Right - 1, "Horizontal pan remains within the padded playfield");
            double currentX = ui.View.CanvasZoom;
            ui.View.Wheel(x, y, -120, false, false, true); ui.Paint();
            Near(currentX, ui.View.CanvasZoom); Near(2 / 1.16, ui.View.CanvasTimeZoom);
            ui.View.PointerDown(x, y, 1, false, false); ui.View.PointerMove(x + 20000, y, false, false);
            ui.View.PointerUp(x + 20000, y, 1); ui.Paint();
            double panOffset = (plot.X + plot.Right - ui.Plot.X - ui.Plot.Right) / 2;
            ui.View.PointerDown(x, y, 1, false, false); ui.View.PointerMove(x + (float)panOffset, y, false, false);
            ui.View.PointerUp(x + (float)panOffset, y, 1); ui.Paint();
            ui.View.UpdateTransport(1000, 30000, true, true, false, null, "fixture.wav"); ui.Paint();
            ui.View.UpdateTransport(1000, 30000, true, false, false, null, "fixture.wav"); ui.Paint();
            ui.Key('1'); ui.DownMap(1000, 256); ui.MoveMap(1100, 300); ui.UpMap(1100, 300);
            Near(1080, ui.View.Document.Fruits.Single().TimeMs); Near(300, ui.View.Document.Fruits.Single().X);
            ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Independent transform drag undoes exactly");
            string root = Path.GetFullPath("artifacts/tests/independent-scale/" + locale + "-" + size.Item1);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace"); ui.View.LibrarySettings.OsuRoot = "";
            string config = Path.Combine(root, "settings.json"); ui.View.LibrarySettings.Save(config);
            var saved = LibrarySettings.Load(config);
            Check(!saved.UniformCanvasScale, "Unlocked state survives saving");
            Near(2, saved.CanvasZoom); Near(2 / 1.16, saved.CanvasTimeZoom);
            ui.View.OpenSettings(); ui.Paint(); ui.View.ApplySettings(config, force: true); ui.Key(27);
            Check(!LibrarySettings.Load(config).UniformCanvasScale, "Settings Apply retains independent scales");
            lockBounds = ui.View.CanvasScaleLockBounds; ui.Click(lockBounds.X + 10, lockBounds.Y + 10);
            Check(ui.View.UniformCanvasScale, "Lock restores uniform scaling");
            Near(1, ui.View.CanvasZoom);
            Near(CatchScrollTiming.PixelsPerMs(map.ApproachRate, ui.Plot.Width), ui.View.PixelsPerMs);
            Near((plot.X + plot.Right) / 2, (ui.Plot.X + ui.Plot.Right) / 2);
            var timeline = ui.View.ObjectTimelineBounds;
            ui.View.Wheel(timeline.X + 20, timeline.Y + 20, 12000, false, false, true); ui.Paint(); Near(1.5, ui.View.ObjectTimelinePixelsPerMs);
            ui.View.LibrarySettings.ExperimentalSnapDivisions = true;
            ui.View.Wheel(timeline.X + 20, timeline.Y + 20, 12000, false, false, true); ui.Paint(); Near(16, ui.View.ObjectTimelinePixelsPerMs);
            ui.View.LibrarySettings.ExperimentalSnapDivisions = false; ui.Paint(); Near(1.5, ui.View.ObjectTimelinePixelsPerMs);
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty && !ui.View.WantsCapture, "Viewport controls preserve content and release capture");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}, got {actual}"); }
}
