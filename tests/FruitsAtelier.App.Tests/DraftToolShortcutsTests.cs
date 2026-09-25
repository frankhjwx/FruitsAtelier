using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

static class DraftToolShortcutsTests
{
    public static void Run()
    {
        string[] tools = ["Select", "Fruit", "Slider", "Banana"];
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            for (int index = 0; index < tools.Length; index++)
            {
                var ui = new Ui();
                ui.LoadDocument(new MapDocument { DurationMs = 5000 });
                ui.View.SetSliderEditingMode(mode);
                ui.Key('B');
                ui.ClickMap(1000, 100);
                ui.Key('7', shift: true);
                Check(ui.View.SnapDivisor == 7 && ui.View.Document.Tracks.Single().Nodes.Count == 1,
                    "Shift+number must set Snap without finishing the slider draft.");
                ui.ClickMap(1500, 200);
                ui.Key('1' + index);
                Check(ui.View.ActiveTool == tools[index] && !ui.View.WantsCapture,
                    "A tool key must finish a valid slider draft and select the tool.");
                Check(ui.View.Document.Tracks.Count == 1 && CurveMath.Validate(ui.View.Document).Count == 0,
                    "Tool switch lost or damaged the finished slider.");
                if (index == 2)
                {
                    ui.ClickMap(2000, 300);
                    Check(ui.View.Document.Tracks.Count == 2 && ui.View.Document.Tracks[1].Nodes.Count == 1,
                        "Pressing 3 must leave Slider ready to draw a new draft.");
                    ui.Key(27);
                }
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.Tracks.Count == 0, "Finishing from a tool key must be one undoable edit.");

                ui.Key('B');
                ui.ClickMap(1000, 100);
                ui.Key('1' + index);
                Check(ui.View.ActiveTool == tools[index] && !ui.View.WantsCapture && ui.View.Document.Tracks.Count == 0,
                    "A tool key must cancel an invalid slider draft and select the tool.");
            }
        }
        OtherShortcuts();
    }

    private static void OtherShortcuts()
    {
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        ui.LoadDocument(map);
        Check(ui.View.AddDifficulty(), "A second difficulty is required for the chooser check.");
        ui.Paint();
        string second = ui.View.CurrentDifficultyName;
        int opened = 0, exported = 0;
        ui.View.RequestOpen = () => opened++;
        ui.View.RequestExport = () => exported++;
        ui.Key('O', ctrl: true);
        Check(opened == 0 && ui.View.CurrentDifficultyName == second,
            "Ctrl+O must open difficulty selection without opening a project.");
        ui.Click(30, 108);
        Check(ui.View.CurrentDifficultyName != second, "The Ctrl+O chooser did not switch difficulty.");
        ui.Key('O', ctrl: true, shift: true);
        Check(opened == 1, "Ctrl+Shift+O must open a project.");
        ui.Key('E', ctrl: true);
        Check(exported == 0, "Ctrl+E must not use the relocated export command.");
        ui.View.SetModifiers(true, false);
        ui.Key('E', ctrl: true);
        ui.View.SetModifiers(false, false);
        Check(exported == 1, "Ctrl+Alt+E must export.");
        ui.Key(115);
        Check(ui.View.SongSetupVisible, "F4 must open Song Setup.");
        ui.Key(27);
        ui.Key(38, ctrl: true);
        Check(Math.Abs(ui.View.PlaybackSpeed - 1.25) < .001, "Ctrl+Up must add 25 percentage points.");
        ui.Key(40, ctrl: true, shift: true);
        Check(Math.Abs(ui.View.PlaybackSpeed - 1.20) < .001,
            "Ctrl+Shift+Down must subtract five percentage points.");
        ui.Key('Q', shift: true);
        Check(!ui.View.NextFruitNewCombo, "Shift+Q must not invoke unmodified New Combo.");

        ui.View.SwitchDifficulty(0);
        ui.Paint();
        var fruit = ui.View.Document.Fruits.Single();
        var timeline = ui.View.ObjectTimelineBounds;
        float x = timeline.X + (float)((fruit.TimeMs - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        float y = timeline.Y + 27;
        float dx = (float)(60 * ui.View.ObjectTimelinePixelsPerMs);
        ui.View.PointerDown(x, y, 0, false, false);
        ui.View.PointerMove(x + dx, y, false, false);
        ui.View.PointerUp(x + dx, y, 0);
        Check(Math.Abs(ui.View.Document.Fruits.Single().TimeMs - 1000) < .001,
            $"Plain timeline drag must follow beat Snap (got {ui.View.Document.Fruits.Single().TimeMs}).");
        ui.View.PointerDown(x, y, 0, true, false);
        Check(ui.View.WantsCapture && ui.View.SelectedObjectIds.Count == 1,
            "Shift timeline pointer did not begin a selected-object drag.");
        ui.View.PointerMove(x + dx, y, true, false);
        Check(Math.Abs(ui.View.Document.Fruits.Single().TimeMs - 1060) < .1,
            $"Shift timeline move did not bypass Snap (got {ui.View.Document.Fruits.Single().TimeMs}).");
        ui.View.PointerUp(x + dx, y, 0, true);
        Check(Math.Abs(ui.View.Document.Fruits.Single().TimeMs - 1060) < .1,
            $"Shift timeline drag must bypass beat Snap (got {ui.View.Document.Fruits.Single().TimeMs}).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
