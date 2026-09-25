using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class SliderHeadReplacementTests
{
    public static void OnCompletion()
    {
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
            Check(finished.Fruits.Select(f => f.Id).Order().SequenceEqual(
                new[] { simultaneous.Id, nearbyTime.Id, later.Id }.Order()),
                $"{mode}: completion did not remove only exact head overlaps.");

            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), $"{mode}: one undo did not restore the original circles.");
            ui.Key('Y', ctrl: true);
            Check(finished.ContentEquals(ui.View.Document), $"{mode}: redo did not restore the replacement.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
