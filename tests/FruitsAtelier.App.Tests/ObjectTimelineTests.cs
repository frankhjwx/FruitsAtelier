using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ObjectTimelineTests
{
    public static void Stacking()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100, SourceOrder = 0 }, new() { TimeMs = 1010, X = 200, SourceOrder = 1 }]);
        var ui = new Ui(false); ui.LoadDocument(map);
        var r = ui.View.ObjectTimelineBounds;
        float X(double time) => r.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        void CheckOrder()
        {
            var labels = ui.Canvas.Texts.Where(t => t.Y == r.Y + 19 && t.Value is "1" or "2").ToArray();
            if (!labels.Select(t => t.Value).SequenceEqual(new[] { "2", "1" })) throw new Exception("Earlier timeline numbers do not cover later objects.");
            var bodies = ui.Canvas.Circles.Where(c => c.Y == r.Y + 27 && c.Radius == 19 && c.Filled).ToArray();
            if (bodies.Length != 2 || bodies[0].X <= bodies[1].X) throw new Exception("Earlier circle is not painted last.");
        }
        CheckOrder();
        ui.Click(X(1010) + 18.9f, r.Y + 27);
        if (!ui.View.SelectedObjectIds.Contains(map.Fruits[1].Id)) throw new Exception("Exposed later object cannot be selected.");
        CheckOrder();
        ui.Click(X(1000), r.Y + 27);
        if (!ui.View.SelectedObjectIds.Contains(map.Fruits[0].Id)) throw new Exception("Timeline hit order disagrees with visible stacking.");
        CheckOrder();
    }
    public static void GridColors()
    {
        var ui = new Ui(false);
        var map = new MapDocument { DurationMs = 20000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 1600, Meter = 3 });
        ui.LoadDocument(map);
        ui.View.UpdateTransport(4800, 20000, true, false, false, null, null); ui.Paint();
        foreach (var (divisor, color) in new[] { (1, 0xEEEEEEu), (2, 0xFF6688u), (3, 0xBB66EEu),
            (4, 0x66AAFFu), (5, 0xEEDD66u), (6, 0xBB66EEu), (7, 0xEEDD66u),
            (8, 0xEEDD66u), (9, 0xEEDD66u), (12, 0xAAAAAAu), (16, 0xAAAAAAu) })
        {
            ui.SetSnapDivisor(divisor); ui.Paint();
            var r = ui.View.ObjectTimelineBounds;
            var ticks = ui.Canvas.Lines.Where(l => l.X1 == l.X2 && l.Y2 == r.Bottom && l.Y1 < l.Y2).ToArray();
            var canvas = ui.Canvas.Lines.Where(l => l.Y1 == l.Y2 && l.X1 == ui.Plot.X && l.X2 == ui.Plot.Right).ToArray();
            if (!ticks.Any(l => l.Color == color) || !canvas.Any(l => l.Color == color))
                throw new Exception($"Missing 1/{divisor} color on canvas or timeline.");
            foreach (var lines in new[] { ticks, canvas })
            {
                if (!lines.Any(l => l.Color == 0xEEEEEE && l.Width == 2.5f)) throw new Exception("Missing measure emphasis.");
                if (divisor % 2 == 0 && !lines.Any(l => l.Color == 0xFF6688 && l.Width == 1.5f)) throw new Exception("Missing half-beat emphasis.");
                if (divisor % 3 == 0 && !lines.Any(l => l.Color == 0xBB66EE && l.Width == 1.5f)) throw new Exception("Missing third-beat emphasis.");
            }
        }
    }

    public static void PlaybackMarquee()
    {
        var map = new MapDocument { DurationMs = 20000 };
        foreach (double time in new[] { 2000d, 2500, 4000, 9000 }) map.Fruits.Add(new() { TimeMs = time, X = 256 });
        var ui = new Ui(false); ui.LoadDocument(map);
        ui.View.UpdateTransport(2500, 20000, true, true, false, null, null); ui.Paint();
        var r = ui.View.ObjectTimelineBounds;
        float X(double t) => r.X + (float)((t - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        float start = X(1700), end = X(2700);
        ui.View.PointerDown(start, r.Y + 2, 0, false, false);
        ui.View.PointerMove(end, r.Y + 45, false, false); ui.Paint();
        AssertSelected(2);
        double before = ui.View.ObjectTimelineStartMs;
        ui.View.UpdateTransport(7500, 20000, true, true, false, null, null); ui.Paint();
        if (Math.Abs(ui.View.ObjectTimelineStartMs - before - 5000) > .001) throw new Exception("Marquee froze the timeline.");
        AssertSelected(3);
        ui.View.PointerUp(end, r.Y + 45, 0); ui.Paint(); AssertSelected(3);
        if (!map.ContentEquals(ui.View.Document) || ui.View.IsDirty || ui.View.WantsCapture)
            throw new Exception("Marquee edited content or retained capture.");
        void AssertSelected(int count)
        {
            if (!ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Take(count).Select(f => f.Id)))
                throw new Exception("Marquee did not retain its original timestamp and offscreen objects.");
        }
    }

    public static void MoveAndNavigate()
    {
        var map = new MapDocument { BeatLengthMs = 500, DurationMs = 10000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 123 });
        var slider = new ImportedSlider { TimeMs = 2000, X = 200, Y = 192, PixelLength = 140, PathType = 'L', SpanCount = 1,
            OriginalLine = "200,192,2000,2,0,L|340:192,1,140" };
        slider.ControlPoints.AddRange([new(200, 192), new(340, 192)]);
        map.ImportedSliders.Add(slider);
        var ui = new Ui(false); ui.LoadDocument(map);
        ui.View.UpdateTransport(1500, 10000, true, false, false, null, null); ui.Paint();
        var r = ui.View.ObjectTimelineBounds;
        float X(double time) => r.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        float y = r.Y + 27;
        ui.View.Wheel(r.X + 100, y, 120, false);
        if (ui.View.PlayheadMs != 1375) throw new Exception("Wheel up must seek earlier");
        ui.View.Wheel(r.X + 100, y, -120, false);
        if (ui.View.PlayheadMs != 1500) throw new Exception("Wheel down must seek later");
        ui.Paint();
        foreach (float emptyY in new[] { r.Y + 3, r.Y + 27, r.Bottom - 3 })
        {
            ui.Click(X(1000), y);
            ui.Click(X(3000), emptyY);
            if (ui.View.PlayheadMs != 1500 || ui.View.SelectedObjectIds.Count != 0)
                throw new Exception("Empty timeline click must clear selection without seeking");
        }
        ui.View.UpdateTransport(1500, 10000, true, false, false, null, null); ui.Paint();
        ui.Click(X(1000), y);
        ui.View.PointerDown(X(2000), y, 0, false, true); ui.View.PointerUp(X(2000), y, 0); ui.Paint();
        var before = ui.View.Document.DeepClone();
        float start = X(1000), end = X(1250);
        ui.View.PointerDown(start, y, 0, false, false);
        ui.View.PointerMove(end, y + 10, false, false); ui.Paint();
        ui.View.PointerUp(end, y + 10, 0); ui.Paint();
        if (ui.View.Document.Fruits.Single().TimeMs != 1250 || ui.View.Document.Fruits.Single().X != 123
            || ui.View.Document.ImportedSliders.Single().TimeMs != 2250 || ui.View.PlayheadMs != 1500)
            throw new Exception("Timeline dragging must shift the group in time without changing X, slider type, or playhead");
        _ = OsuBeatmapWriter.Serialize(ui.View.Document);
        ui.Key('Z', ctrl: true);
        if (!before.ContentEquals(ui.View.Document)) throw new Exception("Timeline movement must be one undo step");
        ui.View.PointerDown(start, y, 0, false, false); ui.View.PointerMove(end, y, false, false);
        ui.Key(27);
        if (!before.ContentEquals(ui.View.Document)) throw new Exception("Escape must cancel timeline movement");
        ui.Key('L');
        ui.View.PointerDown(start, y, 0, false, false); ui.View.PointerUp(end, y, 0);
        if (!before.ContentEquals(ui.View.Document)) throw new Exception("Locked notes must not move on the timeline");
    }

    public static void TimingCacheInvalidation()
    {
        var ui = new Ui(false);
        var map = new MapDocument { BeatLengthMs = 500, DurationMs = 200000 };
        for (int i = 0; i < 2000; i++)
            map.TimingPoints.Add(new() { TimeMs = i * 100, BeatLengthMs = -100, Uninherited = false });
        ui.LoadDocument(map);
        ui.SetSnapDivisor(16); ui.Paint();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        ui.Paint();
        if (GC.GetAllocatedBytesForCurrentThread() - bytes > 4 * 1024 * 1024)
            throw new Exception("Dense timing data caused excessive per-frame allocation.");
        var field = typeof(FruitsAtelier.App.Editor.EditorView).GetField("renderedTiming", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        TimingMap.Lookup Lookup() => (TimingMap.Lookup)field.GetValue(ui.View)!;
        var initial = Lookup();
        ui.View.UpdateTransport(1000, 10000, true, true, false, null, null); ui.Paint();
        if (!ReferenceEquals(initial, Lookup())) throw new Exception("Playback rebuilt unchanged timing data.");
        ui.View.Document.BeatLengthMs = 400; ui.Paint();
        if (ReferenceEquals(initial, Lookup()) || Lookup().At(1000).BeatLengthMs != 400) throw new Exception("Timing edit retained a stale lookup.");
        ui.View.Document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = 300, Uninherited = true }); ui.Paint();
        if (Lookup().At(1000).BeatLengthMs != 300) throw new Exception("Inserted red point did not invalidate timing data.");
        ui.LoadDocument(map);
        if (Lookup().At(1000).BeatLengthMs != 500) throw new Exception("Document replacement reused another map's timing.");
    }

    public static void ReverseMarkers()
    {
        foreach (bool imported in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, SliderMultiplier = 1 };
            var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 3 };
            slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
            var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 3 };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
            if (imported) map.ImportedSliders.Add(slider); else map.Tracks.Add(track);
            var ui = new Ui(false); ui.LoadDocument(map);
            ui.View.UpdateTransport(1750, 10000, true, false, false, null, null); ui.Paint();
            var r = ui.View.ObjectTimelineBounds;
            float X(double t) => r.X + (float)((t - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
            float y = r.Y + 27;
            void CheckMarkers(int spans)
            {
                var circles = ui.Canvas.Circles.Where(c => c.Y == y && c.Radius == 19 && !c.Filled && c.Color == 0xFFFFFF).ToArray();
                if (circles.Length != spans + 1) throw new Exception("Timeline must show a head, each reverse boundary, and an empty tail.");
                for (int i = 0; i <= spans; i++)
                    if (!circles.Any(c => Math.Abs(c.X - X(1000 + i * 500)) < .01)) throw new Exception("Reverse circle is not at its span boundary.");
                for (int i = 1; i < spans; i++)
                    if (!ui.Canvas.Lines.Any(l => Math.Abs(l.X1 - (X(1000 + i * 500) - 8)) < .01 && l.Y1 == y && l.Y2 == y))
                        throw new Exception("Reverse boundary has no right-facing arrow.");
            }
            CheckMarkers(3);
            ui.View.PointerDown(X(2500), y, 0, false, false);
            ui.View.PointerUp(X(1500), y, 0); ui.Paint();
            CheckMarkers(1);
            ui.Key('Z', ctrl: true); CheckMarkers(3);
        }
    }

    public static void TailReverses()
    {
        if (new FruitsAtelier.App.Editor.EditorView().SliderMode != FruitsAtelier.App.Editor.SliderEditingMode.OsuLegacy)
            throw new Exception("New sessions must use legacy slider mode.");
        foreach (bool imported in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, SliderMultiplier = 1 };
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 100,
                    OriginalLine = "100,192,1000,2,0,L|200:192,1,100,2|4,1:0|2:0,0:0:0:0:" };
                slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
                map.ImportedSliders.Add(slider);
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.Add(new() { TimeMs = 1000, X = 100 });
                track.Nodes.Add(new() { TimeMs = 1500, X = 200 });
                map.Tracks.Add(track);
            }
            var ui = new Ui(false); ui.LoadDocument(map);
            ui.View.UpdateTransport(1500, 10000, true, false, false, null, null); ui.Paint();
            int Spans() => imported ? ui.View.Document.ImportedSliders.Single().SpanCount : ui.View.Document.Tracks.Single().SpanCount;
            var before = ui.View.Document.DeepClone();
            var r = ui.View.ObjectTimelineBounds;
            float x = r.X + (float)((1500 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs), y = r.Y + 27;
            float span = (float)(500 * ui.View.ObjectTimelinePixelsPerMs);
            ui.View.PointerMove(x, y, false, false);
            if (!ui.View.TimelineResizeCursor) throw new Exception("Slider tail has no resize cursor.");
            ui.View.PointerDown(x, y, 0, false, false);
            ui.View.PointerMove(x + 2 * span, y, false, false); ui.Paint();
            if (Spans() != 3 || ui.View.PlayheadMs != 1500) throw new Exception("Tail drag did not add two reverses without seeking.");
            ui.View.PointerMove(x - 2 * span, y, false, false); ui.Paint();
            if (Spans() != 1 || !before.ContentEquals(ui.View.Document)) throw new Exception("Dragging back changed the original path or samples.");
            ui.View.PointerUp(x + span, y, 0); ui.Paint();
            if (Spans() != 2) throw new Exception("Release did not commit reverse count.");
            if (imported && !ui.View.Document.ImportedSliders[0].OriginalLine!.Contains(",2,100,2|0|4,1:0|0:0|2:0,"))
                throw new Exception("Imported edge samples were not retained.");
            ui.Key('Z', ctrl: true);
            if (!before.ContentEquals(ui.View.Document)) throw new Exception("Undo did not restore the slider.");
            ui.View.PointerDown(x, y, 0, false, false);
            ui.View.PointerMove(x + span, y, false, false); ui.View.CancelInteraction(); ui.Paint();
            if (!before.ContentEquals(ui.View.Document)) throw new Exception("Cancellation did not restore the slider.");
        }
    }
    public static void CachedDurationsFollowEdits()
    {
        var ui = new Ui(false);
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 1 };
        slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
        map.ImportedSliders.Add(slider);
        ui.LoadDocument(map); ui.Paint();
        float Width()
        {
            var r = ui.View.ObjectTimelineBounds;
            if (ui.Canvas.Outlines.Any(o => o.Bounds.Y == r.Y + 8 && o.Bounds.Height == 38))
                throw new Exception("Slider timeline body must not have a separate perimeter");
            return ui.Canvas.Fills.Single(o => o.Bounds.Y == r.Y + 8 && o.Bounds.Height == 38).Bounds.Width;
        }
        float initial = Width();
        ui.Paint();
        if (Width() != initial) throw new Exception("An unchanged timeline moved");
        ui.View.Document.BeatLengthMs *= 2;
        ui.Paint();
        if (Math.Abs(Width() - (38 + (initial - 38) * 2)) > .01) throw new Exception("Timing edit left a stale slider duration");
        ui.View.Document.ImportedSliders[0].SpanCount = 2;
        ui.Paint();
        if (Math.Abs(Width() - (38 + (initial - 38) * 4)) > .01) throw new Exception("Repeat edit left a stale slider duration");
        ui.LoadDocument(map); ui.Paint();
        if (Math.Abs(Width() - initial) > .01) throw new Exception("Document replacement retained stale timeline data");
    }
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
        if (!ui.View.SelectedObjectIds.SequenceEqual(new[] { fruit.Id }) || requested != -1 || ui.View.PlayheadMs != 1500)
            throw new Exception("Timeline fruit selection moved the playhead");
        ui.Click(X(2500), rect.Y + 27);
        if (!ui.View.SelectedObjectIds.SequenceEqual(new[] { shower.Id }) || requested != -1 || ui.View.PlayheadMs != 1500)
            throw new Exception("Duration selection moved the playhead");
        float origin = X(2300), y = rect.Bottom - 3;
        ui.View.PointerDown(origin, y, 0, false, false);
        ui.View.PointerMove(origin + 90, y, false, false); ui.Paint();
        ui.View.PointerMove(origin, y, false, false);
        ui.View.PointerUp(origin, y, 0);
        if (ui.View.PlayheadMs != 1500 || requested != -1 || ui.View.WantsCapture) throw new Exception("Empty timeline drag must not seek");
        double zoom = ui.View.CanvasZoom, scale = ui.View.ObjectTimelinePixelsPerMs;
        ui.View.Wheel(rect.X + 100, rect.Y + 20, 120, false, false, true); ui.Paint();
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
            foreach (double speed in new[] { .1, .25, .5, .75, 1, 1.5 })
            {
                var text = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.zoomPercent", speed * 100) && t.Y > ui.Height - 120);
                ui.Click(text.X + 2, text.Y + 2);
                if (requested != speed || ui.View.PlaybackSpeed != speed || ui.View.IsDirty)
                    throw new Exception("Speed button did not update transport alone");
            }
        }
        ui.View.SetPlaybackSpeed(double.NaN);
        if (ui.View.PlaybackSpeed != 1.5) throw new Exception("Invalid speed was accepted");
        foreach (double expected in new[] { 1.25, 1d, .75, .5, .25, .1 })
        {
            ui.Key(40, ctrl: true);
            if (ui.View.PlaybackSpeed != expected) throw new Exception("Slower shortcut skipped a speed or the lower limit.");
        }
        foreach (double expected in new[] { .35, .6, .85, 1.1, 1.35, 1.5 })
        {
            ui.Key(38, ctrl: true);
            if (ui.View.PlaybackSpeed != expected) throw new Exception("Faster shortcut skipped a speed or the upper limit.");
        }
    }
}
