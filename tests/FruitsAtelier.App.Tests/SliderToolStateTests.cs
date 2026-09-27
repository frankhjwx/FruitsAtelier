using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderToolStateTests
{
    public static void EditingAndPlacement()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            L.SetLanguage(language);
            var ui = Load(mode);
            var track = ui.View.Document.Tracks.Single();
            var before = ui.View.Document.DeepClone();
            ui.EditTrack(track.Id); ui.ClickMap(1500, 250);
            Check(ui.View.ActiveTool == "Select", "Control selection switched to placement.");
            ui.DownMap(1500, 250); ui.MoveMap(1500, 270); ui.UpMap(1500, 270);
            Check(!before.ContentEquals(ui.View.Document), "Select did not edit the control.");
            ui.Key('Z', ctrl: true);
            Check(before.ContentEquals(ui.View.Document) && ui.View.ActiveTool == "Select"
                && ui.View.SelectedAnchorIds.Count == 0 && ui.View.SelectedObjectIds.Count == 0,
                "Undo did not restore content and clear the changed slider selection in Select.");
            ui.Key('Y', ctrl: true);
            Check(ui.View.ActiveTool == "Select" && ui.View.SelectedAnchorIds.Count == 0
                && ui.View.SelectedObjectIds.Count == 0, "Redo selected the restored slider or changed tools.");
            ui.Key('Z', ctrl: true); ui.ClickMap(2500, 450);
            Check(before.ContentEquals(ui.View.Document) && !ui.View.WantsCapture,
                "Clicking after undo began a new slider.");

            foreach (int key in new[] { (int)'B', (int)'3' })
            {
                ui.EditTrack(track.Id); ui.Key(key);
                Check(ui.View.ActiveTool == "Slider" && ui.View.SelectedObjectIds.Count == 0
                    && ui.View.SelectedAnchorIds.Count == 0 && before.ContentEquals(ui.View.Document),
                    "The placement shortcut retained existing controls or changed content.");
                ui.ClickMap(2250, 100); ui.ClickMap(2500, 140, ctrl: true); ui.Key(13);
                Check(ui.View.Document.Tracks.Count == 2 && ui.View.ActiveTool == "Slider"
                    && ui.View.SelectedAnchorIds.Count == 0, "Finishing placement did not prepare another slider.");
                ui.Key('Z', ctrl: true);
                Check(before.ContentEquals(ui.View.Document) && ui.View.ActiveTool == "Slider",
                    "Undoing placement changed the tool or the existing slider.");
            }
        }
    }

    public static void DeleteAndCut()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        foreach (bool cut in new[] { false, true })
        {
            var ui = Load(mode);
            var track = ui.View.Document.Tracks.Single();
            track.Nodes.RemoveAt(1); ui.Paint();
            var before = ui.View.Document.DeepClone();
            ui.EditTrack(track.Id); ui.ClickMap(1000, 200);
            if (cut) ui.Key('X', ctrl: true); else ui.Key(46);
            Check(ui.View.Document.Tracks.Count == 0 && ui.View.ActiveTool == "Select",
                "Removing the edited slider left a placement tool active.");
            ui.Key('Z', ctrl: true);
            Check(before.ContentEquals(ui.View.Document) && ui.View.SelectedAnchorIds.Count == 0
                && ui.View.SelectedObjectIds.Count == 0, "Undo removal automatically selected restored controls.");
            ui.ClickMap(2500, 450);
            Check(before.ContentEquals(ui.View.Document) && ui.View.ActiveTool == "Select",
                "An empty click after restoring the slider placed an object.");
        }
    }

    public static void HistorySelection()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var ui = Load(mode);
            var track = ui.View.Document.Tracks.Single();
            ui.EditTrack(track.Id); ui.ClickMap(1500, 250);
            var selected = ui.View.SelectedAnchorIds.ToArray();
            var before = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); ui.Key('Y', ctrl: true); ui.Key('Z', ctrl: true, shift: true);
            Check(selected.Length == 1 && ui.View.SelectedAnchorIds.SequenceEqual(selected)
                && before.ContentEquals(ui.View.Document) && ui.View.ActiveTool == "Select",
                "Unavailable history changed control selection or content.");

            var fruit = new Fruit { TimeMs = 2500, X = 400 };
            var otherFruit = new Fruit { TimeMs = 2750, X = 100 };
            ui.View.Document.Fruits.AddRange([fruit, otherFruit]); ui.Paint();
            ui.ClickMap(2500, 400); ui.Key(39, ctrl: true, shift: true);
            Check(ui.View.Document.Fruits.Single(f => f.Id == fruit.Id).X == 401, "Fixture did not move its fruit.");
            ui.EditTrack(track.Id); ui.ClickMap(1500, 250);
            ui.Key('Z', ctrl: true);
            Check(ui.View.SelectedAnchorIds.SequenceEqual(selected), "Undo of another object cleared unchanged controls.");
            ui.Key('Y', ctrl: true);
            ui.Key('A', ctrl: true);
            ui.Key('Z', ctrl: true);
            Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals([track.Id, otherFruit.Id]),
                "Undo did not retain precisely the unchanged selected parents.");
            ui.Key('Y', ctrl: true); ui.Key('Y', ctrl: true);
            Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals([track.Id, otherFruit.Id]),
                "Redo changed unrelated selection or selected a restored object.");
        }
    }

    private static Ui Load(SliderEditingMode mode)
    {
        var map = new MapDocument { DurationMs = 12000 };
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 200 }, new() { TimeMs = 1500, X = 250 }, new() { TimeMs = 2000, X = 300 }]);
        map.Tracks.Add(track);
        var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Paint();
        return ui;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
