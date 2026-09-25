using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class NoteSnapTests
{
    private sealed class ManualTime : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int ms) => ticks += ms;
    }
    private static readonly ManualTime clock = new();
    public static void HoldTimingAndDragging()
    {
        foreach (string cancellation in new[] { "release", "move", "focus", "none" })
        {
            var map = new MapDocument { DurationMs = 4000, BeatLengthMs = 1000, IsDemo = false };
            var fruit = new Fruit { TimeMs = 1167, X = 150 }; map.Fruits.Add(fruit);
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
            ui.DownMap(1167, 150); clock.Advance(299); ui.Paint();
            Check(ui.View.SnapDivisor == 4, "Note hold activated before 300 ms.");
            if (cancellation == "release") ui.UpMap(1167, 150);
            if (cancellation == "move") ui.MoveMap(1167, 180);
            if (cancellation == "focus") ui.View.CancelInteraction();
            clock.Advance(1); ui.Paint();
            Check(ui.View.SnapDivisor == (cancellation == "none" ? 6 : 4), "Note hold timing or cancellation failed.");
            if (cancellation == "none")
            {
                Check(map.ContentEquals(ui.View.Document), "Holding changed note content.");
                ui.MoveMap(1340, 170); ui.UpMap(1340, 170);
                Check(Math.Abs(ui.Fruit(fruit.Id).TimeMs - 4000d / 3) < .001,
                    "Dragging without releasing the long press did not use detected snap.");
                ui.Key('Z', ctrl: true);
                Check(map.ContentEquals(ui.View.Document), "Long-press drag did not undo in one step.");
            }
            else ui.View.CancelInteraction();
        }
    }

    public static void SliderObjectDragging()
    {
        foreach (bool imported in new[] { false, true })
        foreach (bool tail in new[] { false, true })
        foreach (bool hold in new[] { false, true })
        foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>())
        {
            var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 1000, SliderMultiplier = 1, IsDemo = false };
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 100 };
                slider.ControlPoints.AddRange([new(120, 192), new(220, 192)]);
                map.ImportedSliders.Add(slider);
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 120 }, new Anchor { TimeMs = 2000, X = 220 }]);
                map.Tracks.Add(track);
            }
            var edges = CatchStreamConverter.Convert(map).Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            var target = tail ? edges[^1] : edges[0];
            var other = tail ? edges[0] : edges[^1];
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Key('T');
            if (!hold) ui.ClickMap(target.TimeMs, target.X);
            ui.DownMap(target.TimeMs, target.X);
            if (hold) { clock.Advance(300); ui.Paint(); }
            ui.MoveMap(target.TimeMs + 80, target.X + 23); ui.UpMap(target.TimeMs + 80, target.X + 23);
            var after = CatchStreamConverter.Convert(ui.View.Document).Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            var moved = tail ? after[^1] : after[0]; var fixedEdge = tail ? after[0] : after[^1];
            Check(Math.Abs(moved.X - target.X - 24) < .001 && moved.TimeMs == target.TimeMs,
                $"Selected slider edge did not move only its snapped X: imported={imported}, tail={tail}, mode={mode}.");
            Check(fixedEdge.X == other.X && fixedEdge.TimeMs == other.TimeMs, "Dragging an edge translated the whole slider.");
            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), "Edge drag did not undo including any Legacy conversion.");
        }

        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 5000, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 120 }, new Anchor { TimeMs = 3000, X = 320 }]);
            map.Tracks.Add(track);
            var target = OsuBeatmapWriter.Serialize(map).PlayableObjects.First(o => o.Kind == kind);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('T');
            ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
            Check(map.ContentEquals(ui.View.Document), "Selecting an off-grid droplet snapped its X without dragging.");
            ui.DownMap(target.TimeMs, target.X);
            ui.MoveMap(target.TimeMs, target.X + 5); ui.UpMap(target.TimeMs, target.X + 5);
            var moved = CatchStreamConverter.Convert(ui.View.Document).Objects.Single(o => o.EventIndex == target.EventIndex);
            double expected = Math.Round((target.X + 5) / 4, MidpointRounding.AwayFromZero) * 4;
            Check(Math.Abs(moved.X - expected) < .001 && moved.TimeMs == target.TimeMs, "Droplet drag ignored Grid Snap.");
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Grid-snapped droplet drag did not undo.");
            ui.ClickMap(target.TimeMs, target.X); ui.DownMap(target.TimeMs, target.X);
            ui.MoveMap(target.TimeMs, 512); ui.UpMap(target.TimeMs, 512);
            var clamped = CatchStreamConverter.Convert(ui.View.Document).Objects.Single(o => o.EventIndex == target.EventIndex);
            Check(Math.Abs(clamped.X - target.X) < .001 || Math.Abs(clamped.X / 4 - Math.Round(clamped.X / 4)) < .001,
                "Clamping a grid-snapped droplet left it between grid lines.");
        }
    }

    public static void SliderDragBaseline()
    {
        foreach (bool imported in new[] { false, true })
        foreach (bool tail in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 1000, SliderMultiplier = 1, IsDemo = false };
            var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 100 };
            slider.ControlPoints.AddRange([new(120, 192), new(120, 292)]);
            map.ImportedSliders.Add(slider);
            if (!imported) ImportedSliderEditing.ConvertToTrack(map, slider.Id);
            map.Fruits.Add(new Fruit { TimeMs = 7000, X = 100 });
            var other = new CurveTrack { Kind = CurveKind.Linear };
            other.Nodes.AddRange([new Anchor { TimeMs = 8000, X = 100 }, new Anchor { TimeMs = 9000, X = 150 }]);
            map.Tracks.Add(other);
            var target = OsuBeatmapWriter.Serialize(map).PlayableObjects
                .Where(o => o.SourceId == slider.Id && o.Kind == CatchObjectKind.Fruit).ElementAt(tail ? 1 : 0);
            double originalX = CatchStreamConverter.Convert(map).Objects.Single(o => o.SourceId == target.SourceId && o.EventIndex == target.EventIndex).X;
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
            ui.ClickMap(target.TimeMs, target.X); ui.DownMap(target.TimeMs, target.X);
            var document = ui.View.Document;
            var untouched = document.Tracks.Single(t => t.Id == other.Id);
            foreach (double offset in new[] { 20d, -20, 392, 30, 0 })
            {
                ui.MoveMap(target.TimeMs, target.X + offset);
                Check(ReferenceEquals(document, ui.View.Document) && ReferenceEquals(untouched, ui.View.Document.Tracks.Single(t => t.Id == other.Id)),
                    "A slider candidate copied or replaced unrelated map content.");
                var actual = CatchStreamConverter.Convert(ui.View.Document);
                Check(actual.Success && actual.Objects.SequenceEqual(ui.View.Conversion.Objects),
                    "Cached slider preview differs from a fresh full-map conversion.");
                if (Math.Abs(offset) <= 30)
                {
                    var moved = actual.Objects.Single(o => o.SourceId == target.SourceId && o.EventIndex == target.EventIndex);
                    double expected = offset == 0 ? originalX : target.X + offset;
                    Check(Math.Abs(moved.X - expected) < .001, $"Slider candidate: imported={imported}, tail={tail}, offset={offset}, actual={moved.X}, expected={expected}; {ui.View.StatusMessage}");
                }
            }
            ui.UpMap(target.TimeMs, target.X);
            Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Returning to the drag origin retained a fitted replacement or changed source order.");
            ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, target.X + 20);
            ui.View.CancelInteraction(); ui.Paint();
            Check(map.ContentEquals(ui.View.Document), "Cancelling a cached drag did not restore the original source.");
            ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, target.X + 20); ui.UpMap(target.TimeMs, target.X + 20);
            var exported = (OsuWriteResult?)typeof(FruitsAtelier.App.Editor.EditorView)
                .GetField("playableExport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View);
            Check(exported is not null && exported.PlayableObjects.SequenceEqual(OsuBeatmapWriter.Serialize(ui.View.Document).PlayableObjects),
                "Releasing a slider drag did not restore export-quantized playback.");
            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), "Cached drag did not undo in one step.");
        }
    }

    private static void Inspect(Ui ui, float x, float y)
    {
        ui.View.PointerDown(x, y, 0, false, false);
        clock.Advance(300); ui.Paint();
        ui.View.PointerUp(x, y, 0);
        ui.Paint();
    }
    public static void SliderEdgesAndCurrentSnap()
    {
        foreach (bool imported in new[] { false, true })
        foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>())
        {
            var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 1000, SliderMultiplier = 1, IsDemo = false };
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1167, X = 120, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 2 };
                slider.ControlPoints.AddRange([new(120, 192), new(220, 192)]);
                map.ImportedSliders.Add(slider);
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2 };
                track.Nodes.AddRange([new Anchor { TimeMs = 1167, X = 120 }, new Anchor { TimeMs = 2167, X = 220 }]);
                map.Tracks.Add(track);
            }
            var edges = OsuBeatmapWriter.Serialize(map).PlayableObjects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            Check(edges.Length == 3, "Slider edge fixture must contain a head, repeat and tail.");
            foreach (var edge in edges)
            {
                var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
                ui.View.SetSliderEditingMode(mode); ui.SetSnapDivisor(4);
                ui.View.UpdateTransport(edge.TimeMs, 6000, true, false, false, null, null); ui.Paint();
                var original = ui.View.Document.DeepClone();
                var point = ui.ScreenAt(edge.TimeMs, edge.X);
                for (int click = 0; click < 2; click++)
                {
                    ui.ClickMap(edge.TimeMs, edge.X);
                    Check(ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0xE7EBF2
                        && Math.Abs(c.X - point.X) < 1 && Math.Abs(c.Y - point.Y) < 1) == (click == 1),
                        $"Slider edge has no individual highlight: imported={imported}, mode={mode}, event={edge.EventIndex}, click={click}.");
                }
                Inspect(ui, point.X, point.Y);
                Check(ui.View.SnapDivisor == 6, $"Slider edge long press did not detect sixth snap: imported={imported}, mode={mode}, event={edge.EventIndex}.");
                Check(ui.View.SelectedObjectIds.Contains(edge.SourceId), "Long press lost the owning slider selection.");
                Check(original.ContentEquals(ui.View.Document), "Slider edge inspection changed or converted the slider.");
                ui.ClickMap(edge.TimeMs + 500, 450);
                Check(ui.View.SelectedObjectIds.Contains(edge.SourceId), "Outside click did not return to whole-slider selection.");
                ui.ClickMap(edge.TimeMs + 500, 450);
                Check(ui.View.SnapDivisor == 4, "Leaving a slider edge did not restore the original snap.");
            }
        }

        foreach (var (time, current) in new[] { (1500d, 4), (1000d, 8), (1667d, 6) })
        {
            var map = new MapDocument { DurationMs = 4000, BeatLengthMs = 1000, IsDemo = false };
            map.Fruits.Add(new Fruit { TimeMs = time, X = 250 });
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map); ui.SetSnapDivisor(current);
            ui.ClickMap(time, 250);
            string status = ui.View.StatusMessage;
            var point = ui.ScreenAt(time, 250);
            Inspect(ui, point.X, point.Y); ui.Paint();
            Check(ui.View.SnapDivisor == current && ui.View.StatusMessage == status,
                "An already matching current snap or its display changed on long press.");
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == current, "An already matching note created an unwanted temporary snap.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void InspectBeatPosition()
    {
        foreach (string language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language);
            var map = new MapDocument { DurationMs = 4000, IsDemo = false };
            map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 1000, Uninherited = true });
            var odd = new Fruit { TimeMs = 1167, X = 150 };
            var offGrid = new Fruit { TimeMs = 2173, X = 350 };
            map.Fruits.AddRange([odd, offGrid]);
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
            var original = ui.View.Document.DeepClone();
            var point = ui.ScreenAt(odd.TimeMs, odd.X);
            Inspect(ui, point.X, point.Y); ui.Paint();
            Check(ui.View.SnapDivisor == 6 && ui.View.StatusMessage == Strings.Get("editor.status.noteBeatPosition", 1, 6, "00:01:167", 6),
                "Long press did not identify the sixth-beat note and activate its snap.");
            ui.DownMap(odd.TimeMs, odd.X); ui.MoveMap(odd.TimeMs, odd.X + 25); ui.UpMap(odd.TimeMs, odd.X + 25);
            Check(ui.Fruit(odd.Id).TimeMs == odd.TimeMs && ui.Fruit(odd.Id).X != odd.X,
                "Horizontal dragging moved an off-quarter note in time.");
            ui.Key('Z', ctrl: true);
            Check(original.ContentEquals(ui.View.Document), "Undo did not restore the odd-snap note.");
            ui.ClickMap(offGrid.TimeMs, offGrid.X);
            point = ui.ScreenAt(offGrid.TimeMs, offGrid.X);
            Inspect(ui, point.X, point.Y); ui.Paint();
            Check(ui.View.SnapDivisor == 4 && ui.View.StatusMessage == Strings.Get("editor.status.noteOffGrid", "00:02:173"),
                "A note beyond the 2 ms limit matched or retained temporary snap.");
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == 4, "Leaving selection changed the original snap.");
            point = ui.ScreenAt(odd.TimeMs, odd.X);
            Inspect(ui, point.X, point.Y); ui.Paint();
            ui.SetSnapDivisor(8);
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == 8, "Leaving selection reverted a manually chosen snap.");
            Check(original.ContentEquals(ui.View.Document), "Beat-position inspection edited map content.");

            foreach (var (time, current, expected, numerator, denominator) in new[]
                     { (1500d, 3, 4, 1, 2), (1333d, 4, 6, 1, 3), (1125d, 4, 8, 1, 8) })
            {
                var minimum = new MapDocument { DurationMs = 4000, BeatLengthMs = 1000, IsDemo = false };
                minimum.Fruits.Add(new Fruit { TimeMs = time, X = 250 });
                ui.LoadDocument(minimum); ui.SetSnapDivisor(current);
                point = ui.ScreenAt(time, 250); Inspect(ui, point.X, point.Y);
                Check(ui.View.SnapDivisor == expected && ui.View.StatusMessage == Strings.Get(
                    "editor.status.noteBeatPosition", numerator, denominator, TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff"), expected),
                    "Automatic snap did not respect the quarter/sixth minimum while preserving the beat fraction.");
                ui.ClickMap(3000, 450);
                Check(ui.View.SnapDivisor == current, "Leaving the note did not restore the manually chosen snap.");
            }

            foreach (var (time, x, expectedDivisor, numerator, denominator) in new[]
                     { (3670d, 208d, 6, 2, 3), (3726d, 176d, 6, 5, 6),
                       (47669d, 287d, 6, 2, 3), (47725d, 262d, 6, 5, 6) })
            {
                var ra = new MapDocument { DurationMs = 50000, IsDemo = false };
                ra.TimingPoints.Add(new TimingPoint { TimeMs = 115, BeatLengthMs = 333.333333333333, Uninherited = true });
                ra.TimingPoints.Add(new TimingPoint { TimeMs = 45448, BeatLengthMs = -100, Uninherited = false });
                ra.Fruits.AddRange([new Fruit { TimeMs = 3670, X = 208 }, new Fruit { TimeMs = 3726, X = 176 },
                    new Fruit { TimeMs = 47669, X = 287 }, new Fruit { TimeMs = 47725, X = 262 }]);
                ui.LoadDocument(ra);
                ui.SetSnapDivisor(4);
                ui.View.UpdateTransport(time, 50000, true, false, false, null, null); ui.Paint();
                var before = ui.View.Document.DeepClone();
                point = ui.ScreenAt(time, x);
                Inspect(ui, point.X, point.Y); ui.Paint();
                Check(ui.View.SnapDivisor == expectedDivisor && ui.View.StatusMessage == Strings.Get(
                    "editor.status.noteBeatPosition", numerator, denominator, TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff"), expectedDivisor),
                    $"Force of Ra note at {time} ms did not identify its nearest beat position independently of neighbours.");
                Check(before.ContentEquals(ui.View.Document), "Nearest-beat inspection changed original timestamps.");
                var fruit = ui.View.Document.Fruits.Single(f => f.TimeMs == time);
                double targetTime = time + 333.333333333333 / expectedDivisor;
                ui.DownMap(time, x); ui.MoveMap(targetTime, x); ui.UpMap(targetTime, x);
                Check(Math.Abs(ui.Fruit(fruit.Id).TimeMs - TimingMap.Snap(ra, targetTime, expectedDivisor)) < 1e-6,
                    "Vertical dragging did not use the detected subdivision.");
                ui.Key('Z', ctrl: true);
                Check(before.ContentEquals(ui.View.Document), "Undo did not restore the original offset timestamp.");
            }

            var boundary = new MapDocument { DurationMs = 4000, IsDemo = false };
            boundary.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 1000, Uninherited = true });
            boundary.TimingPoints.Add(new TimingPoint { TimeMs = 2169, BeatLengthMs = 500, Uninherited = true });
            boundary.Fruits.Add(new Fruit { TimeMs = 2168, X = 250 });
            ui.LoadDocument(boundary);
            point = ui.ScreenAt(2168, 250);
            Inspect(ui, point.X, point.Y); ui.Paint();
            Check(ui.View.SnapDivisor == 6, "An upcoming timing boundary replaced the active red point during beat identification.");

            var fast = new MapDocument { DurationMs = 4000, IsDemo = false };
            fast.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 50, Uninherited = true });
            fast.Fruits.Add(new Fruit { TimeMs = 1015, X = 250 });
            ui.LoadDocument(fast);
            ui.SetSnapDivisor(4);
            point = ui.ScreenAt(1015, 250);
            Inspect(ui, point.X, point.Y); ui.Paint();
            Check(ui.View.SnapDivisor == 16,
                "Recognition chose the first grid within 2 ms instead of the closest grid.");

            foreach (double time in new[] { 998d, 1002d, 997d, 1003d })
            {
                var threshold = new MapDocument { DurationMs = 4000, IsDemo = false };
                threshold.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 1000, Uninherited = true });
                threshold.Fruits.Add(new Fruit { TimeMs = time, X = 250 });
                ui.LoadDocument(threshold); ui.SetSnapDivisor(4);
                ui.ClickMap(time, 250);
                string previousStatus = ui.View.StatusMessage;
                point = ui.ScreenAt(time, 250);
                Inspect(ui, point.X, point.Y); ui.Paint();
                bool matches = time is 998 or 1002;
                string timestamp = TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff");
                Check(ui.View.SnapDivisor == 4 && ui.View.StatusMessage == (matches
                    ? previousStatus
                    : Strings.Get("editor.status.noteOffGrid", timestamp)),
                    $"The inclusive 2 ms match limit was not respected at {time} ms.");
            }
        }
        Strings.SetLanguage("en");
    }
}
