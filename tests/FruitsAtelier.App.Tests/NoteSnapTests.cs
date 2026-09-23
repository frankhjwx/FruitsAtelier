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
                "Off-grid note was assigned a false subdivision or retained the temporary snap.");
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == 4, "Leaving selection changed the original snap.");
            point = ui.ScreenAt(odd.TimeMs, odd.X);
            ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
            ui.SetSnapDivisor(8);
            ui.ClickMap(3000, 450);
            Check(ui.View.SnapDivisor == 8, "Leaving selection reverted a manually chosen snap.");
            Check(original.ContentEquals(ui.View.Document), "Beat-position inspection edited map content.");

            var ra = new MapDocument { DurationMs = 50000, IsDemo = false };
            ra.TimingPoints.Add(new TimingPoint { TimeMs = 115, BeatLengthMs = 333.333333333333, Uninherited = true });
            ra.Fruits.AddRange([new Fruit { TimeMs = 3670, X = 208 }, new Fruit { TimeMs = 3726, X = 176 },
                new Fruit { TimeMs = 47669, X = 208 }, new Fruit { TimeMs = 47725, X = 176 }]);
            ui.LoadDocument(ra);
            foreach (var (time, x, numerator, denominator) in new[] { (3670d, 208d, 2, 3), (3726d, 176d, 5, 6) })
            {
                point = ui.ScreenAt(time, x);
                ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
                Check(ui.View.SnapDivisor == 6 && ui.View.StatusMessage == Strings.Get("editor.status.noteBeatPosition", numerator, denominator, TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff"), 6),
                    $"Force of Ra note at {time} ms did not identify the adjacent 1/6 pattern.");
            }
            ui.View.UpdateTransport(47669, 50000, true, false, false, null, null); ui.Paint();
            foreach (var (time, x, numerator, denominator) in new[] { (47669d, 208d, 2, 3), (47725d, 176d, 5, 6) })
            {
                point = ui.ScreenAt(time, x);
                ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
                Check(ui.View.SnapDivisor == 6 && ui.View.StatusMessage == Strings.Get("editor.status.noteBeatPosition", numerator, denominator, TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff"), 6),
                    $"Force of Ra note at {time} ms did not identify the adjacent 1/6 pattern.");
            }
        }
        Strings.SetLanguage("en");
    }
}
