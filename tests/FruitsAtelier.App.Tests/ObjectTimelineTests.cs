using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ObjectTimelineTests
{
    public static void NavigationAndSelection()
    {
        var ui = new Ui(false);
        var map = new MapDocument { DurationMs = 10000 };
        var fruit = new Fruit { TimeMs = 1000, X = 256 };
        var shower = new BananaShower { TimeMs = 2000, EndTimeMs = 3000 };
        map.Fruits.Add(fruit); map.BananaShowers.Add(shower);
        ui.View.LoadDocument(map); ui.View.UpdateTransport(1500, 10000, true, false, false, null, null); ui.Paint();
        var rect = ui.View.ObjectTimelineBounds;
        if (rect.Y <= ui.View.ZoomSliderBounds.Bottom || rect.Bottom >= ui.View.CanvasPlotBounds.Y)
            throw new Exception("Object timeline must sit between Zoom and the main plot");
        float X(double time) => rect.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        double requested = -1;
        ui.View.RequestSeek = t => requested = t;
        ui.Click(X(1000), rect.Y + 27);
        if (!ui.View.SelectedObjectIds.SequenceEqual(new[] { fruit.Id }) || requested != 1000)
            throw new Exception("Timeline fruit did not select and seek its source");
        ui.Click(X(2500), rect.Y + 27);
        if (!ui.View.SelectedObjectIds.SequenceEqual(new[] { shower.Id }) || requested != 2000)
            throw new Exception("Duration body did not select its parent at its start");
        float origin = X(2300), y = rect.Bottom - 3;
        ui.View.PointerDown(origin, y, 0, false, false);
        ui.View.PointerMove(origin + 90, y, false, false); ui.Paint();
        ui.View.PointerMove(origin, y, false, false);
        ui.View.PointerUp(origin, y, 0);
        if (Math.Abs(ui.View.PlayheadMs - 2300) > .01 || ui.View.WantsCapture) throw new Exception("Timeline drag changed its origin after repaint");
        double zoom = ui.View.CanvasZoom, scale = ui.View.ObjectTimelinePixelsPerMs;
        ui.View.Wheel(rect.X + 100, rect.Y + 20, 120, true); ui.Paint();
        if (ui.View.CanvasZoom != zoom || ui.View.ObjectTimelinePixelsPerMs <= scale || ui.View.IsDirty)
            throw new Exception("Timeline zoom changed canvas scale or document content");
    }

    public static void BoxAndDelete()
    {
        var ui = new Ui(false);
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        map.Fruits.Add(new() { TimeMs = 1500, X = 200 });
        map.Fruits.Add(new() { TimeMs = 2200, X = 300 });
        ui.LoadDocument(map); ui.View.UpdateTransport(1500, 10000, true, false, false, null, null); ui.Paint();
        var r = ui.View.ObjectTimelineBounds;
        if (r.X >= ui.View.ToolButtonBounds[0].Right || r.Right < ui.View.CanvasPlotBounds.Right)
            throw new Exception("Timeline does not span the toolbar and canvas columns.");
        float X(double time) => r.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        var number = ui.Canvas.Texts.Single(t => t.Value == "1" && t.Y > r.Y && t.Y < r.Bottom);
        if (Math.Abs(number.X + ((FruitsAtelier.App.Rendering.ICanvas)ui.Canvas).MeasureText("1", 13) / 2 - X(1000)) > .01)
            throw new Exception("Timeline number is not centered.");
        var baseline = ui.View.Document.DeepClone();
        float left = X(800), right = X(1700);
        ui.View.PointerDown(left, r.Y + 2, 0, false, false);
        ui.View.PointerMove(right, r.Y + 49, false, false); ui.Paint();
        ui.View.PointerUp(right, r.Y + 49, 0); ui.Paint();
        if (!ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Take(2).Select(f => f.Id)) || ui.View.PlayheadMs != 1500)
            throw new Exception("Timeline box missed notes or changed the playhead.");
        ui.View.PointerDown(X(1000), r.Y + 27, 2, false, false); ui.Paint();
        if (ui.View.Document.Fruits.Count != 1 || ui.View.Document.Fruits[0].Id != map.Fruits[2].Id)
            throw new Exception("Timeline right-click did not delete the selected notes.");
        ui.Key('Z', ctrl: true);
        if (!baseline.ContentEquals(ui.View.Document)) throw new Exception("Timeline delete was not one undo transaction.");
        ui.View.PointerDown(left, r.Y + 2, 0, false, false);
        ui.View.PointerMove(right, r.Y + 49, false, false); ui.Paint();
        ui.Key(27);
        if (ui.View.SelectedObjectIds.Count != 0 || ui.View.WantsCapture) throw new Exception("Escape did not cancel timeline box selection.");
        ui.View.PointerDown(X(2200), r.Y + 27, 2, false, false); ui.Paint();
        if (ui.View.Document.Fruits.Count != 2) throw new Exception("Timeline right-click did not delete an unselected note.");
    }

    public static void SpeedControls()
    {
        var ui = new Ui(false);
        double requested = -1;
        ui.View.RequestPlaybackSpeed = speed => requested = speed;
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language); ui.Resize(980, 620);
            foreach (double speed in new[] { .25, .5, .75, 1 })
            {
                var text = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.zoomPercent", speed * 100) && t.Y > ui.Height - 120);
                ui.Click(text.X + 2, text.Y + 2);
                if (requested != speed || ui.View.PlaybackSpeed != speed || ui.View.IsDirty)
                    throw new Exception("Speed button did not update transport alone");
            }
        }
        ui.View.SetPlaybackSpeed(double.NaN);
        if (ui.View.PlaybackSpeed != 1) throw new Exception("Invalid speed was accepted");
    }
}
