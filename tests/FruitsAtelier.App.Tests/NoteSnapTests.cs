using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class NoteSnapTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void DoubleClickBeatPosition()
    {
        foreach (string language in Strings.AvailableLanguages)
        {
            Strings.SetLanguage(language);
            var map = new MapDocument { DurationMs = 4000, IsDemo = false };
            map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 1000, Uninherited = true });
            var odd = new Fruit { TimeMs = 1167, X = 150 };
            var offGrid = new Fruit { TimeMs = 2173, X = 350 };
            map.Fruits.AddRange([odd, offGrid]);
            var ui = new Ui(); ui.LoadDocument(map);
            var original = ui.View.Document.DeepClone();
            var point = ui.ScreenAt(odd.TimeMs, odd.X);
            ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
            Check(ui.View.SnapDivisor == 6 && ui.View.StatusMessage == Strings.Get("editor.status.noteBeatPosition", 1, 6, "00:01:167", 6),
                "Double-click did not identify the sixth-beat note and activate its snap.");
            ui.DownMap(odd.TimeMs, odd.X); ui.MoveMap(odd.TimeMs, odd.X + 25); ui.UpMap(odd.TimeMs, odd.X + 25);
            Check(ui.Fruit(odd.Id).TimeMs == odd.TimeMs && ui.Fruit(odd.Id).X != odd.X,
                "Horizontal dragging moved an off-quarter note in time.");
            ui.Key('Z', ctrl: true);
            Check(original.ContentEquals(ui.View.Document), "Undo did not restore the odd-snap note.");
            ui.ClickMap(offGrid.TimeMs, offGrid.X);
            point = ui.ScreenAt(offGrid.TimeMs, offGrid.X);
            ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
            Check(ui.View.SnapDivisor == 4 && ui.View.StatusMessage == Strings.Get("editor.status.noteOffGrid", "00:02:173"),
                "A note beyond the 2 ms limit matched or retained temporary snap.");
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == 4, "Leaving selection changed the original snap.");
            point = ui.ScreenAt(odd.TimeMs, odd.X);
            ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
            ui.SetSnapDivisor(8);
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == 8, "Leaving selection reverted a manually chosen snap.");
            Check(original.ContentEquals(ui.View.Document), "Beat-position inspection edited map content.");

            foreach (var (time, x, expectedDivisor, numerator, denominator) in new[]
                     { (3670d, 208d, 3, 2, 3), (3726d, 176d, 6, 5, 6),
                       (47669d, 287d, 3, 2, 3), (47725d, 262d, 6, 5, 6) })
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
                ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
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
            ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
            Check(ui.View.SnapDivisor == 6, "An upcoming timing boundary replaced the active red point during beat identification.");

            var fast = new MapDocument { DurationMs = 4000, IsDemo = false };
            fast.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 50, Uninherited = true });
            fast.Fruits.Add(new Fruit { TimeMs = 1015, X = 250 });
            ui.LoadDocument(fast);
            point = ui.ScreenAt(1015, 250);
            ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
            Check(ui.View.SnapDivisor == 16,
                "Recognition chose the first grid within 2 ms instead of the closest grid.");

            foreach (double time in new[] { 998d, 1002d, 997d, 1003d })
            {
                var threshold = new MapDocument { DurationMs = 4000, IsDemo = false };
                threshold.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 1000, Uninherited = true });
                threshold.Fruits.Add(new Fruit { TimeMs = time, X = 250 });
                ui.LoadDocument(threshold); ui.SetSnapDivisor(4);
                point = ui.ScreenAt(time, 250);
                ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
                bool matches = time is 998 or 1002;
                string timestamp = TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff");
                Check(ui.View.SnapDivisor == (matches ? 1 : 4) && ui.View.StatusMessage == (matches
                    ? Strings.Get("editor.status.noteBeatPosition", 0, 1, timestamp, 1)
                    : Strings.Get("editor.status.noteOffGrid", timestamp)),
                    $"The inclusive 2 ms match limit was not respected at {time} ms.");
            }
        }
        Strings.SetLanguage("en");
    }
}
