using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LazerPresentationTests
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    public static void Run()
    {
        var clock = new Clock();
        var ui = new Ui(false, clock);
        ui.View.LoadDocument(new MapDocument { DurationMs = 20000, IsDemo = false });
        ui.View.LibrarySettings.LazerEditing = true;
        ui.Paint();
        int seeks = 0;
        ui.View.RequestSeek = _ => seeks++;
        var before = ui.View.Document.DeepClone();
        Seek(1000);
        Near(1000, ui.View.PlayheadMs);
        Near(0, ui.View.LazerPresentationTime);
        clock.Milliseconds = 150; ui.Paint();
        Near(968.75, ui.View.LazerPresentationTime);
        Check(ui.View.LazerPresentationNeedsRedraw, "The seek must request intermediate frames.");
        Check(seeks == 1, "Animation must not repeatedly seek the audio transport.");
        Seek(2000);
        Near(968.75, ui.View.LazerPresentationTime);
        clock.Milliseconds = 450; ui.Paint();
        Near(2000, ui.View.LazerPresentationTime);
        Check(!ui.View.LazerPresentationNeedsRedraw && seeks == 2, "The second seek must settle after 300 ms.");
        Seek(7001);
        Near(7001, ui.View.LazerPresentationTime);
        Check(!ui.View.LazerPresentationNeedsRedraw, "Seeks greater than five seconds must be immediate.");
        ui.View.UpdateTransport(7001, 20000, true, true, false, null, null);
        Seek(7100);
        Near(7100, ui.View.LazerPresentationTime);
        Check(!ui.View.LazerPresentationNeedsRedraw, "Playing seeks must be immediate.");
        ui.View.UpdateTransport(7100, 20000, true, false, false, null, null);
        ui.Paint();
        foreach (var size in new[] { (980f, 620f), (1440f, 900f) })
        {
            ui.Resize(size.Item1, size.Item2);
            Near(ui.View.LazerCatchLineY,
                ui.View.CanvasPlotBounds.Bottom - (ui.View.PlayheadMs - ui.View.ViewStartMs) * ui.View.PixelsPerMs, .001);
            Near(ui.View.CanvasPlotBounds.Y + ui.View.CanvasPlotBounds.Height * .9,
                ui.View.LazerCatchLineY, .001);
            var field = ui.View.PlayfieldBounds;
            Near(ui.View.LazerCanvasScale * 512, field.Width, .001);
        }
        double initialSpeed = ui.View.PixelsPerMs;
        ui.Key(114);
        clock.Milliseconds += 100; ui.Paint();
        Near(initialSpeed / 1.96875, ui.View.PixelsPerMs, .00001);
        clock.Milliseconds += 100; ui.Paint();
        Near(initialSpeed / 2, ui.View.PixelsPerMs, .00001);
        double initialTimeline = ui.View.ObjectTimelinePixelsPerMs;
        double savedTimeline = ui.View.LibrarySettings.ObjectTimelineScale;
        var timeline = ui.View.ObjectTimelineBounds;
        ui.View.Wheel(timeline.X + 10, timeline.Y + 10, 120, false, false, true);
        Near(initialTimeline, ui.View.ObjectTimelinePixelsPerMs);
        clock.Milliseconds += 100; ui.Paint();
        double zoomIncrease = timeline.Width * (1d / 500 - 1d / 10000) * .02;
        Near(initialTimeline + zoomIncrease * .96875, ui.View.ObjectTimelinePixelsPerMs);
        clock.Milliseconds += 100; ui.Paint();
        Near(initialTimeline + zoomIncrease, ui.View.ObjectTimelinePixelsPerMs);
        Near(savedTimeline, ui.View.LibrarySettings.ObjectTimelineScale);
        double initialWidth = ui.View.PlayfieldBounds.Width, speedBeforeZoom = ui.View.PixelsPerMs;
        double catchY = ui.View.LazerCatchLineY, timeBeforeZoom = ui.View.PlayheadMs;
        double mappingZoom = ui.View.LibrarySettings.CanvasZoom;
        var plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X + plot.Width / 2, plot.Y + 10, 120, false, false, true);
        clock.Milliseconds += 200; ui.Paint();
        Near(initialWidth * 1.16, ui.View.PlayfieldBounds.Width, .001);
        Near(speedBeforeZoom * 1.16, ui.View.PixelsPerMs, .00001);
        Near(catchY, ui.View.LazerCatchLineY);
        Near(timeBeforeZoom, ui.View.PlayheadMs);
        Near(mappingZoom, ui.View.LibrarySettings.CanvasZoom);
        var reference = ui.View.LazerGameScreenBounds;
        Near(4d / 3, reference.Width / reference.Height, .00001);
        Near(ui.View.PlayfieldBounds.Width, reference.Width * .8, .001);
        Near(catchY, reference.Y + reference.Height * .15 + 340 * ui.View.PlayfieldBounds.Width / 512, .001);
        ui.View.Wheel(plot.X + plot.Width / 2, plot.Y + 10, 1200, false, false, true);
        clock.Milliseconds += 200; ui.Paint();
        double beforePan = ui.View.PlayfieldBounds.X;
        ui.View.PointerDown(plot.X + plot.Width / 2, plot.Y + 10, 1, false, false);
        ui.View.PointerMove(plot.X + plot.Width / 2 + 30, plot.Y + 50, false, false);
        ui.View.PointerUp(plot.X + plot.Width / 2 + 30, plot.Y + 50, 1);
        ui.Paint();
        Near(beforePan + 30, ui.View.PlayfieldBounds.X, .001);
        Near(timeBeforeZoom, ui.View.PlayheadMs);
        Near(catchY, ui.View.LazerCatchLineY);
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Presentation must not edit beatmap content.");
        ui.View.LibrarySettings.LazerEditing = false;
        Seek(7200);
        Near(7200, ui.View.LazerPresentationTime);
        ui.View.LibrarySettings.LazerEditing = true;
        Seek(8200);
        clock.Milliseconds += 50;
        double displayed = ui.View.LazerPresentationTime;
        ui.View.RequestTogglePlayback = () => ui.View.UpdateTransport(ui.View.PlayheadMs, 20000, true, true, false, null, null);
        ui.Key(32);
        Near(displayed, ui.View.PlayheadMs);
        ui.View.UpdateTransport(displayed, 20000, true, false, false, null, null);
        Near(displayed, ui.View.LazerPresentationTime);
        Check(!ui.View.LazerPresentationNeedsRedraw, "Starting then pausing must retire the old seek transform.");
        WheelSeekingKeepsDraggedObjectUnderPointer();

        void Seek(double target) => typeof(EditorView).GetMethod("SeekLazerSmoothlyTo", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ui.View, [target]);
    }

    private static void WheelSeekingKeepsDraggedObjectUnderPointer()
    {
        var clock = new Clock();
        var ui = new Ui(false, clock);
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, ApproachRate = 5, IsDemo = false };
        map.Fruits.Add(new Fruit { TimeMs = 2000, X = 256 });
        ui.View.LoadDocument(map);
        ui.View.LibrarySettings.LazerEditing = true;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(EditorView).GetField("snap", flags)!.SetValue(ui.View, false);
        typeof(EditorView).GetProperty("distanceSnap", flags)!.SetValue(ui.View, false);
        typeof(EditorView).GetProperty("gridSnap", flags)!.SetValue(ui.View, false);
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null);
        ui.Paint();
        var before = ui.View.Document.DeepClone();
        var pointer = Position();
        ui.View.PointerDown(pointer.X, pointer.Y, 0, false, false);
        Check(ui.View.WantsCapture, "The visible fruit must begin a real object drag.");
        Check(typeof(EditorView).GetField("drag", flags)!.GetValue(ui.View)!.ToString() == "Objects",
            $"Expected an object drag at {pointer}, got {typeof(EditorView).GetField("drag", flags)!.GetValue(ui.View)}; plot={ui.View.CanvasPlotBounds}.");
        int seeks = 0;
        ui.View.RequestSeek = _ => seeks++;
        ui.View.Wheel(pointer.X, pointer.Y, -120, false);
        Near(1125, ui.View.PlayheadMs);
        Check(seeks == 1, $"The wheel must issue one seek during the object drag; got {seeks}.");
        Near(2000, ui.View.Document.Fruits[0].TimeMs);
        Near(pointer.Y, Position().Y, .002);
        foreach (long elapsed in new long[] { 100, 200, 300 })
        {
            clock.Milliseconds = elapsed;
            ui.Paint();
            Near(1000 + 125 * (1 - Math.Pow(1 - elapsed / 300d, 5)), ui.View.LazerPresentationTime, .001);
            var position = Position();
            Near(pointer.X, position.X, .002);
            Near(pointer.Y, position.Y, .002);
            Check(ui.View.WantsCapture, "Animated wheel seeking must retain the object drag.");
        }
        Near(2125, ui.View.Document.Fruits[0].TimeMs, .002);
        Check(seeks == 1, "Drag presentation frames must not issue repeated transport seeks.");
        ui.View.PointerUp(pointer.X, pointer.Y, 0);
        ui.Key('Z', ctrl: true);
        Check(before.ContentEquals(ui.View.Document), "An animated wheel drag must undo as one edit.");
        ui.Key('Y', ctrl: true);
        Near(2125, ui.View.Document.Fruits[0].TimeMs, .002);

        (float X, float Y) Position()
        {
            var fruit = ui.View.Document.Fruits[0];
            return ((float, float))typeof(EditorView).GetMethod("Screen", flags)!
                .Invoke(ui.View, [new MapPoint(fruit.TimeMs, fruit.X)])!;
        }
    }

    private static void Near(double expected, double actual, double tolerance = .000001)
    { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
