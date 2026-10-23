using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class SliderHeadReplacementTests
{
    public static void OnCompletion()
    {
        ExistingAdjustments();
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var map = new MapDocument { DurationMs = 12000 };
            var replaced = new Fruit { TimeMs = 1000, X = 128 };
            var duplicate = new Fruit { TimeMs = 1000, X = 128 };
            var simultaneous = new Fruit { TimeMs = 1000, X = 256 };
            var nearbyTime = new Fruit { TimeMs = 1000.25, X = 128 };
            var later = new Fruit { TimeMs = 1250, X = 128 };
            map.Fruits.AddRange([replaced, duplicate, simultaneous, nearbyTime, later]);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.SetSliderEditingMode(mode); ui.Key('B');
            ui.ClickMap(1000, 128);
            Check(ui.View.Document.Fruits.Count == 5, $"{mode}: starting a draft removed fruit before completion.");
            ui.ClickMap(1500, 320, ctrl: true);
            ui.Key(13);

            var finished = ui.View.Document.DeepClone();
            Check(finished.Tracks.Count == 1 && finished.Tracks[0].Nodes[0].TimeMs == 1000
                && finished.Tracks[0].Nodes[0].X == 128, $"{mode}: slider head moved.");
            Check(finished.Fruits.Select(f => f.Id).SequenceEqual(new[] { later.Id }),
                $"{mode}: completion did not replace same-time fruits across coordinates.");

            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), $"{mode}: one undo did not restore the original circles.");
            ui.Key('Y', ctrl: true);
            Check(finished.ContentEquals(ui.View.Document), $"{mode}: redo did not restore the replacement.");
        }
    }

    private static void ExistingAdjustments()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        foreach (bool moveWhole in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 12000 };
            var fruit = new Fruit { TimeMs = 1500, X = 256 };
            map.Fruits.Add(fruit);
            var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 128 }, new() { TimeMs = 2000, X = 320 }]);
            map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.LibrarySettings.PreserveSameTimeObjects = false;
            ui.View.SetSliderEditingMode(mode);
            if (moveWhole) ui.Key('1');
            else { ui.EditTrack(track.Id); ui.ClickMap(1000, 128); }
            ui.DownMap(1000, 128); ui.MoveMap(1500, 128); ui.UpMap(1500, 128);
            var adjusted = ui.View.Document.DeepClone();
            Check(Math.Abs(adjusted.Tracks.Single().Nodes[0].TimeMs - fruit.TimeMs) < .01,
                $"{mode}: adjustment did not move the slider head to the circle time.");
            Check(adjusted.Fruits.Single().Id == fruit.Id && adjusted.Fruits.Single().TimeMs == fruit.TimeMs,
                $"{mode}: adjusting an existing slider replaced the circle (whole={moveWhole}).");
            Check(Math.Abs(adjusted.Tracks.Single().Nodes[^1].TimeMs - (moveWhole ? 2500 : 2000)) < .01,
                $"{mode}: adjustment did not exercise the expected drag (whole={moveWhole}, tail={adjusted.Tracks.Single().Nodes[^1].TimeMs}).");
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Slider adjustment undo changed the circle.");
            ui.Key('Y', ctrl: true); Check(adjusted.ContentEquals(ui.View.Document), "Slider adjustment redo changed the circle.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
