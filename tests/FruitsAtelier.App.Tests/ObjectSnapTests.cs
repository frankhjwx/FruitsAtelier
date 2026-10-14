using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class ObjectSnapTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}.");

    public static void Run()
    {
        BeatRanges(); Placement(); Configuration(); Dragging(); OrderedDragging();
    }

    private static void BeatRanges()
    {
        var map = new MapDocument { BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Meter = 4, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 2000, BeatLengthMs = 250, Meter = 3, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 2400, BeatLengthMs = -50, Uninherited = false });
        var timing = new TimingMap.Lookup(map);
        Near(1000, timing.OffsetBeats(0, 2));
        Near(1500, timing.OffsetBeats(2250, -2));
        Near(1500, timing.OffsetBeats(2000, -1));
        Near(2250, timing.OffsetBeats(2000, 1));
        Near(3500, timing.OffsetBeats(3000, 2));
        Near(3000, timing.OffsetBeats(3000, 0));
    }

    private static void Placement()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100.25 }, new() { TimeMs = 875, X = 200 },
            new() { TimeMs = 2500, X = 300 }, new() { TimeMs = 2625, X = 400 }]);
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('F');
        Check(ui.View.ObjectSnapSettings == (true, 2, 1), "Object snap defaults are incorrect.");
        ui.MoveMap(2000, 103);
        Near(100.25, ui.View.ObjectSnapTargetPoint!.Value.X);
        ui.ClickMap(2000, 103);
        var fruit = ui.View.Document.Fruits.Single(f => Math.Abs(f.TimeMs - 2000) < .01);
        Near(100.25, fruit.X); Near(2000, fruit.TimeMs);
        ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Object-snapped placement did not undo.");
        ui.MoveMap(2000, 302); Near(300, ui.View.ObjectSnapTargetPoint!.Value.X);
        ui.MoveMap(2000, 202); Check(ui.View.ObjectSnapTargetPoint is null, "Earlier search exceeded two beats.");
        ui.MoveMap(2000, 402); Check(ui.View.ObjectSnapTargetPoint is null, "Later search exceeded one beat.");
        ui.MoveMap(2000, 330); Check(ui.View.ObjectSnapTargetPoint is null, "Distant X triggered object snap.");
        ui.Key('T'); ui.Key('Y'); ui.MoveMap(2000, 103);
        Near(100.25, ui.View.ObjectSnapTargetPoint!.Value.X);
        ui.ClickMap(2000, 103); Near(100.25, ui.View.Document.Fruits.Single(f => f.TimeMs == 2000).X);
        ui.Key('Z', ctrl: true);
        foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
        {
            ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Key('B');
            ui.ClickMap(2000, 302);
            var head = ui.View.Document.Tracks.Single().Nodes[0];
            Near(300, head.X); Near(2000, head.TimeMs);
            ui.View.CancelInteraction(); ui.Paint();
            Check(map.ContentEquals(ui.View.Document), "Snapped slider draft did not cancel.");
        }
        var sliderMap = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
        sliderMap.Tracks.Add(new CurveTrack { Kind = CurveKind.Linear,
            Nodes = { new() { TimeMs = 1000, X = 90 }, new() { TimeMs = 1500, X = 190 } } });
        ui.LoadDocument(sliderMap); ui.Key('F'); ui.MoveMap(2000, 192);
        Near(190, ui.View.ObjectSnapTargetPoint!.Value.X);
        Check(sliderMap.ContentEquals(ui.View.Document), "Object snap hover edited map content.");
    }

    private static void Configuration()
    {
        var legacy = JsonSerializer.Deserialize<MapEditingPreferences>("{\"DistanceSnap\":true}")!;
        Check(legacy.ObjectSnap && legacy.ObjectSnapEarlierBeats == 2 && legacy.ObjectSnapLaterBeats == 1,
            "Older settings lost default object snap.");
        var limits = JsonSerializer.Deserialize<MapEditingPreferences>("{\"ObjectSnapEarlierBeats\":16,\"ObjectSnapLaterBeats\":-1}")!;
        Check(limits.ObjectSnapEarlierBeats == 4 && limits.ObjectSnapLaterBeats == 0,
            "Stored object snap ranges exceeded zero to four beats.");
        string language = Strings.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            {
                Strings.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000 };
                var ui = new Ui(); ui.LoadDocument(map); ui.Resize(size.Item1, size.Item2);
                int saved = 0; ui.View.RequestViewPreference = () => saved++;
                var before = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                Open(); Click(ui.View.ObjectSnapEarlierPlusBounds); Click(ui.View.ObjectSnapLaterPlusBounds);
                Check(ui.View.ObjectSnapSettings == (true, 2, 1), "Search draft changed active preferences.");
                ui.ClickText(Strings.Get("mac.cancel"));
                Check(ui.View.ObjectSnapSettings == (true, 2, 1) && saved == 0, "Cancel saved object snap settings.");
                Open(); Click(ui.View.ObjectSnapEarlierPlusBounds); Click(ui.View.ObjectSnapLaterPlusBounds);
                ui.ClickText(Strings.Get("library.apply"));
                Check(ui.View.ObjectSnapSettings == (true, 3, 2) && saved == 1, "Apply did not save both search ranges.");
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty,
                    "Object snap preferences changed beatmap content or history.");
                var settings = JsonSerializer.Deserialize<LibrarySettings>(JsonSerializer.Serialize(ui.View.LibrarySettings))!;
                var restored = settings.MapEditingPreferences.Values.Single();
                Check(restored.ObjectSnap && restored.ObjectSnapEarlierBeats == 3 && restored.ObjectSnapLaterBeats == 2,
                    "Settings round trip lost search ranges.");
                Open();
                for (int i = 0; i < 5; i++) { Click(ui.View.ObjectSnapEarlierPlusBounds); Click(ui.View.ObjectSnapLaterPlusBounds); }
                ui.ClickText(Strings.Get("library.apply"));
                Check(ui.View.ObjectSnapSettings == (true, 4, 4), "Search controls exceeded four beats.");
                Open(); Click(ui.View.ObjectSnapToggleBounds); ui.ClickText(Strings.Get("library.apply"));
                Check(!ui.View.ObjectSnapSettings.Enabled, "Object snap toggle did not apply.");

                void Open()
                {
                    ui.View.OpenDistanceSnapDialog(); ui.Paint();
                    Check(ui.View.ObjectSnapLaterPlusBounds.Bottom < size.Item2 && ui.View.DistanceSnapPreviewBounds.Height > 0,
                        "Object snap controls do not fit the dialog.");
                }
                void Click(FruitsAtelier.App.Rendering.Rect r) => ui.Click(r.X + r.Width / 2, r.Y + r.Height / 2);
            }
        }
        finally { Strings.SetLanguage(language); }
    }

    private static void OrderedDragging()
    {
        foreach (bool collinear in new[] { false, true })
        foreach (bool grid in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500,
                SliderMultiplier = 2, DistanceSnapCollinear = collinear };
            var moving = new Fruit { TimeMs = 1250, X = 450 };
            map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, moving,
                new() { TimeMs = 1500, X = 400 }, new() { TimeMs = 900, X = 280 },
                new() { TimeMs = 850, X = 251.1 }]);
            map.DistanceSnapRatios.AddRange([.5, 1, 1.8]);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('1'); ui.Key('Y');
            if (grid) ui.Key('T');
            ui.DownMap(moving.TimeMs, moving.X);
            ui.MoveMap(moving.TimeMs, 480);
            double previous = ui.Fruit(moving.Id).X;
            var visited = new HashSet<double>();
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int x = 478; x >= 20; x -= 2)
            {
                ui.MoveMap(moving.TimeMs, x);
                double actual = ui.Fruit(moving.Id).X;
                Check(actual <= previous + .001, $"Right-to-left drag reversed at X={x}: {previous} -> {actual}, grid={grid}, collinear={collinear}.");
                Near(moving.TimeMs, ui.Fruit(moving.Id).TimeMs);
                visited.Add(Math.Round(actual, 3)); previous = actual;
            }
            Check(visited.Contains(280) && visited.Contains(400) && visited.Contains(200) && visited.Contains(251.1),
                "Unified drag skipped an object alignment or DS preset.");
            if (collinear) Check(visited.Contains(grid ? 252 : 250), "Unified drag skipped the collinear candidate.");
            for (int x = 22; x <= 480; x += 2)
            {
                ui.MoveMap(moving.TimeMs, x);
                double actual = ui.Fruit(moving.Id).X;
                Check(actual >= previous - .001, $"Left-to-right drag reversed at X={x}: {previous} -> {actual}.");
                previous = actual;
            }
            Console.WriteLine($"Ordered snap drag grid={grid}, collinear={collinear}: {timer.Elapsed.TotalMilliseconds / 460:F3} ms/update, {(GC.GetAllocatedBytesForCurrentThread() - allocation) / 460} B/update (pointer + recording render)");
            ui.UpMap(moving.TimeMs, 480);
            var moved = ui.View.Document.DeepClone();
            Check(OsuBeatmapWriter.Serialize(moved).ObjectSequenceMatches, "Ordered drag failed export validation.");
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Ordered sweep did not undo as one gesture.");
            ui.Key('Y', ctrl: true); Check(moved.ContentEquals(ui.View.Document), "Ordered sweep did not redo.");
            ui.DownMap(moving.TimeMs, ui.Fruit(moving.Id).X); ui.MoveMap(moving.TimeMs, 280);
            ui.View.CancelInteraction(); ui.Paint();
            Check(moved.ContentEquals(ui.View.Document), "Ordered drag cancellation retained content changes.");
        }
    }

    private static void Dragging()
    {
        foreach (bool headWins in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
            var first = new Fruit { TimeMs = 1000, X = 100 };
            var middle = new Fruit { TimeMs = 1500, X = 200 };
            var last = new Fruit { TimeMs = 2000, X = 300 };
            var headTarget = new Fruit { TimeMs = 750, X = headWins ? 184.25 : 450 };
            var tailTarget = new Fruit { TimeMs = 2250, X = 385.4 };
            map.Fruits.AddRange([first, middle, last, headTarget, tailTarget,
                new() { TimeMs = 1500, X = 284 }]);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('1');
            ui.ClickFruit(first.Id); ui.ClickMap(middle.TimeMs, middle.X, ctrl: true); ui.ClickMap(last.TimeMs, last.X, ctrl: true);
            ui.Key('T');
            ui.DownMap(1000, 100); ui.MoveMap(1125, 182);
            double shift = headWins ? 84.25 : 85.4;
            Near(headWins ? headTarget.X : tailTarget.X, ui.View.ObjectSnapTargetPoint!.Value.X);
            Near(100 + shift, ui.Fruit(first.Id).X); Near(200 + shift, ui.Fruit(middle.Id).X); Near(300 + shift, ui.Fruit(last.Id).X);
            Near(1125, ui.Fruit(first.Id).TimeMs); Near(2125, ui.Fruit(last.Id).TimeMs);
            ui.UpMap(1125, 182);
            var moved = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Group object snap did not undo atomically.");
            ui.Key('Y', ctrl: true); Check(moved.ContentEquals(ui.View.Document), "Group object snap did not redo.");
        }

        var single = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
        single.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        var one = new Ui(); one.LoadDocument(single); one.Key('1');
        one.DownMap(1000, 100); one.MoveMap(1125, 109); one.UpMap(1125, 109);
        Near(109, one.View.Document.Fruits.Single().X);
        one.Key('Z', ctrl: true); Check(single.ContentEquals(one.View.Document), "Single-fruit movement did not undo.");
        one.View.Document.Fruits.Add(new() { TimeMs = 750, X = 180.25 }); one.Paint();
        one.DownMap(1000, 100); one.MoveMap(1125, 178);
        Near(180.25, one.View.Document.Fruits.Single(f => f.Id == single.Fruits[0].Id).X);
        one.View.CancelInteraction(); one.Paint();
        Near(100, one.View.Document.Fruits.Single(f => f.Id == single.Fruits[0].Id).X);
        Check(one.View.ObjectSnapTargetPoint is null, "Cancellation retained the drag snap target.");

        var bounded = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
        var left = new Fruit { TimeMs = 1000, X = 5 };
        var right = new Fruit { TimeMs = 2000, X = 300 };
        bounded.Fruits.AddRange([left, right, new() { TimeMs = 2250, X = 294 }]);
        var boundsUi = new Ui(); boundsUi.LoadDocument(bounded); boundsUi.Key('1');
        boundsUi.ClickFruit(left.Id); boundsUi.ClickMap(right.TimeMs, right.X, ctrl: true);
        boundsUi.DownMap(2000, 300); boundsUi.MoveMap(2000, 292);
        Near(0, boundsUi.Fruit(left.Id).X); Near(295, boundsUi.Fruit(right.Id).X);
        Check(boundsUi.View.ObjectSnapTargetPoint is null, "Snap moved the other end beyond the playfield.");
        boundsUi.View.CancelInteraction(); boundsUi.Paint();
        Check(bounded.ContentEquals(boundsUi.View.Document), "Bounded group drag did not cancel.");

        foreach (int repeats in new[] { 1, 2, 3 })
        foreach (bool imported in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500, SliderMultiplier = 2 };
            Guid sourceId;
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 200, SpanCount = repeats };
                slider.ControlPoints.AddRange([new(100, 192), new(300, 192)]); map.ImportedSliders.Add(slider); sourceId = slider.Id;
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = repeats, CompensateTinyDroplets = false,
                    Nodes = { new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 300 } } };
                map.Tracks.Add(track); sourceId = track.Id;
            }
            double finalX = repeats % 2 == 0 ? 100 : 300;
            var reference = new Fruit { TimeMs = 1000 + repeats * 500 + 250, X = finalX + 85.4 };
            map.Fruits.Add(reference);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('1'); ui.ClickMap(1000, 100);
            var box = ui.View.SelectionTransformBounds;
            float x = box.X + 20, y = box.Y + box.Height / 2;
            float dx = 82 * ui.View.PlayfieldBounds.Width / 512;
            ui.View.PointerDown(x, y, 0, false, false); ui.Paint();
            ui.View.PointerMove(x + dx, y, false, false); ui.Paint();
            Check(ui.View.ObjectSnapTargetPoint is not null, $"Slider drag did not snap: repeats={repeats}, imported={imported}.");
            Near(reference.X, ui.View.ObjectSnapTargetPoint!.Value.X);
            var fruits = ui.View.Conversion.Objects.Where(o => o.SourceId == sourceId && o.Kind == CatchObjectKind.Fruit).ToArray();
            Check(ui.View.Conversion.Objects.SequenceEqual(CatchStreamConverter.Convert(ui.View.Document).Objects),
                "Dragged slider cache differs from independent conversion.");
            Near(185.4, fruits[0].X); Near(reference.X, fruits[^1].X);
            Near(1000, fruits[0].TimeMs); Near(1000 + repeats * 500, fruits[^1].TimeMs);
            ui.View.PointerUp(x + dx, y, 0); ui.Paint();
            Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Snapped slider failed export validation.");
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Slider snap failed to restore its source on undo.");
        }
    }
}
