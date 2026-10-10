using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;

internal static class PlaybackPlacementTests
{
    public static void DragFollowsPlayback()
    {
        foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
        foreach (bool tail in new[] { false, true })
        foreach (bool moved in new[] { false, true })
        foreach (bool cancel in new[] { false, true })
        foreach (bool initiallyPlaying in new[] { false, true })
        foreach (bool editControls in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 20000, IsDemo = false };
            var fruit = new Fruit { TimeMs = 1000, X = 100 };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
            if (tail) map.Tracks.Add(track); else map.Fruits.Add(fruit);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.SetSliderEditingMode(mode);
            ui.View.UpdateTransport(1500, 20000, true, initiallyPlaying, false, null, "fixture.wav"); ui.Paint();
            if (tail && editControls) ui.EditTrack(track.Id); else ui.Key('1');
            var original = ui.View.Document.DeepClone();
            var pointer = ui.ScreenAt(tail ? 2000 : 1000, tail ? 300 : 100);
            ui.View.PointerDown(pointer.X, pointer.Y, 0, false, false);
            float y = pointer.Y - (moved ? 22.5f : 0);
            if (moved) ui.View.PointerMove(pointer.X, y, false, false);
            ui.Paint();
            double Time() => tail ? ui.View.Document.Tracks.Single().Nodes[^1].TimeMs : ui.View.Document.Fruits.Single().TimeMs;
            double beforeTime = Time(), beforeView = ui.View.ViewStartMs;
            ui.View.UpdateTransport(1500, 20000, true, true, false, null, "fixture.wav");
            ui.View.UpdateTransport(2000, 20000, true, true, false, null, "fixture.wav"); ui.Paint();
            Near(beforeView + 500, ui.View.ViewStartMs);
            Near(beforeTime + 500, Time());
            Check(Math.Abs(y - ui.ScreenAt(Time(), tail ? 300 : 100).Y) <= ui.View.PixelsPerMs * 62.5 + .05,
                "Playback lost the snapped object's pointer position.");
            ui.View.UpdateTransport(2000, 20000, true, false, false, null, "fixture.wav");
            double paused = ui.View.ViewStartMs;
            ui.View.UpdateTransport(2500, 20000, true, false, false, null, "fixture.wav");
            Near(paused, ui.View.ViewStartMs);
            if (cancel) ui.View.CancelInteraction();
            else { ui.View.PointerUp(pointer.X, y, 0); ui.Key('Z', ctrl: true); }
            Check(original.ContentEquals(ui.View.Document), "Playback drag did not cancel or undo atomically.");
        }
    }

    public static void TimelinePlacement()
    {
        foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
        foreach (int key in new[] { 'F', 'B', 'N' })
        {
            var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 20000, IsDemo = false });
            ui.View.SetSliderEditingMode(mode); ui.Key(key); ui.MoveMap(1234, 230);
            var baseline = ui.View.Document.DeepClone();
            CheckGhost(ui, 1250);
            Check(!ui.View.IsDirty && baseline.ContentEquals(ui.View.Document), "Hover preview changed content.");
            ui.View.PointerMove(0, 0, false, false); ui.Paint();
            Check(!Ghosts(ui).Any(), "Timeline hover preview survived leaving the canvas.");
            if (key == 'F')
            {
                ui.ClickMap(1234, 230);
                ui.View.PointerMove(0, 0, false, false); ui.Paint();
                Check(!Ghosts(ui).Any() && ui.View.Document.Fruits.Count == 1, "Fruit preview did not commit cleanly.");
                ui.Key('Z', ctrl: true);
            }
            else
            {
                ui.ClickMap(1234, 230); ui.MoveMap(1987, 350);
                CheckGhost(ui, 1250); CheckGhost(ui, 2000);
                ui.Key(27);
            }
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Preview cancellation or undo changed content.");
        }
    }

    public static void GroupAndTimelinePlayback()
    {
        foreach (bool timeline in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 20000, IsDemo = false };
            map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 350 }]);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.UpdateTransport(1500, 20000, true, false, false, null, "fixture.wav"); ui.Paint();
            ui.ClickMap(1000, 100); ui.ClickMap(1500, 350, ctrl: true);
            var baseline = ui.View.Document.DeepClone();
            var p = ui.ScreenAt(1000, 100);
            if (timeline) p = (TimelineX(ui, 1000), ui.View.ObjectTimelineBounds.Y + 27);
            ui.View.PointerDown(p.X, p.Y, 0, false, false);
            float x = p.X + (timeline ? 45 : 0), y = p.Y - (timeline ? 0 : 22.5f);
            ui.View.PointerMove(x, y, false, false); ui.Paint();
            double first = ui.View.Document.Fruits[0].TimeMs;
            ui.View.UpdateTransport(1500, 20000, true, true, false, null, "fixture.wav");
            ui.View.UpdateTransport(2000, 20000, true, true, false, null, "fixture.wav"); ui.Paint();
            Near(first + 500, ui.View.Document.Fruits[0].TimeMs);
            Near(500, ui.View.Document.Fruits[1].TimeMs - ui.View.Document.Fruits[0].TimeMs);
            ui.View.PointerUp(x, y, 0); ui.Key('Z', ctrl: true);
            Check(baseline.ContentEquals(ui.View.Document), "Scrolling multi-selection did not undo atomically.");
        }
        foreach (bool timeline in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 20000, IsDemo = false };
            var banana = new BananaShower { TimeMs = 1000, EndTimeMs = 2000 }; map.BananaShowers.Add(banana);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.UpdateTransport(1500, 20000, true, false, false, null, "fixture.wav"); ui.Paint();
            ui.ClickMap(1500, 256);
            var baseline = ui.View.Document.DeepClone();
            var p = ui.ScreenAt(2000, 256);
            if (timeline) p = (TimelineX(ui, 2000), ui.View.ObjectTimelineBounds.Y + 27);
            ui.View.PointerDown(p.X, p.Y, 0, false, false);
            ui.View.PointerMove(p.X + (timeline ? 45 : 0), p.Y - (timeline ? 0 : 22.5f), false, false); ui.Paint();
            double before = ui.View.Document.BananaShowers.Single().EndTimeMs;
            ui.View.UpdateTransport(1500, 20000, true, true, false, null, "fixture.wav");
            ui.View.UpdateTransport(2000, 20000, true, true, false, null, "fixture.wav"); ui.Paint();
            Near(before + 500, ui.View.Document.BananaShowers.Single().EndTimeMs);
            ui.View.CancelInteraction();
            Check(baseline.ContentEquals(ui.View.Document), "Scrolling Banana endpoint did not cancel.");
        }
    }

    public static void WheelStartsHeldDrag()
    {
        foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
        foreach (string kind in new[] { "fruit", "slider", "controls", "import", "banana", "timelineFruit", "timelineBanana" })
        foreach (bool playing in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 20000, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
            bool fruit = kind is "fruit" or "timelineFruit", banana = kind is "banana" or "timelineBanana";
            if (fruit) map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
            else if (banana) map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 2000 });
            else if (kind == "import")
            {
                map.SliderMultiplier = 1.4;
                var imported = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 140 };
                imported.ControlPoints.AddRange([new(100, 192), new(240, 192)]);
                map.ImportedSliders.Add(imported);
            }
            else map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode);
            ui.View.UpdateTransport(1500, 20000, true, playing, false, null, "fixture.wav"); ui.Paint();
            if (kind == "controls") ui.EditTrack(track.Id);
            if (banana) ui.ClickMap(1500, 256);
            var baseline = ui.View.Document.DeepClone();
            double time = fruit ? 1000 : kind == "import" ? 1500 : 2000;
            var p = ui.ScreenAt(time, fruit ? 100 : banana ? 256 : kind == "import" ? 240 : 300);
            if (kind.StartsWith("timeline")) p = (TimelineX(ui, time), ui.View.ObjectTimelineBounds.Y + 27);
            ui.View.PointerDown(p.X, p.Y, 0, false, false);
            Check(ui.View.WantsCapture, $"{kind}: Hold did not capture.");
            ui.View.UpdateTransport(1500, 20000, true, playing, false, null, "fixture.wav");
            Check(baseline.ContentEquals(ui.View.Document), "An unchanged transport position edited the held object.");
            double Time() => fruit ? ui.View.Document.Fruits.Single().TimeMs
                : banana ? ui.View.Document.BananaShowers.Single().EndTimeMs : ui.View.Document.Tracks.Single().Nodes[^1].TimeMs;
            double step = playing ? 500 : 125;
            for (int i = 1; i <= 2; i++)
            {
                ui.View.Wheel(p.X, p.Y, -120, false); ui.Paint();
                Near(time + step * i, Time());
                Check(ui.View.WantsCapture, "Wheel navigation released the held drag.");
            }
            ui.View.PointerUp(p.X, p.Y, 0); ui.Key('Z', ctrl: true);
            Check(baseline.ContentEquals(ui.View.Document), $"{kind}: Wheel edits did not undo atomically.");
            ui.Key('Y', ctrl: true); Near(time + 2 * step, Time());
        }
        foreach (bool zoom in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 20000, IsDemo = false };
            map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
            var ui = new Ui(); ui.LoadDocument(map);
            var p = ui.ScreenAt(1000, 100); ui.View.PointerDown(p.X, p.Y, 0, false, false);
            ui.View.Wheel(p.X, p.Y, 120, ctrl: !zoom, alt: zoom); ui.Paint();
            Check(map.ContentEquals(ui.View.Document), "Snap or zoom wheel edited a held fruit.");
            ui.View.CancelInteraction();
        }
    }

    private static float TimelineX(Ui ui, double time) => ui.View.ObjectTimelineBounds.X
        + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);

    private static IEnumerable<RecordingCanvas.Dot> Ghosts(Ui ui) => ui.Canvas.Operations
        .Where(o => o.Clip == ui.View.ObjectTimelineBounds && o.Dot is { Filled: true, Opacity: .45f })
        .Select(o => o.Dot!.Value);
    private static void CheckGhost(Ui ui, double time)
    {
        float x = ui.View.ObjectTimelineBounds.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        Check(Ghosts(ui).Any(d => Math.Abs(d.X - x) < .1), $"Missing timeline preview at {time}, tool={ui.View.ActiveTool}.");
    }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .05, $"Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
