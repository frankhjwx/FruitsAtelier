using FruitsAtelier.Core;

internal static class OsuStableInputTests
{
    public static void Run()
    {
        NativeGridLevelsAndShift();
        DraftCornersAndEditableAnchors();
        ContentShortcutsDuringControlDrag();
    }

    private static void NativeGridLevelsAndShift()
    {
        var ui = CreateUi(); ui.Key('2');
        foreach (int level in new[] { 1, 2, 3, 4 })
        {
            ui.Key('0' + level, ctrl: true);
            Click(ui, 110.25, 145.25);
            var fruit = ui.View.Document.Fruits[^1];
            Check(fruit.X == (level == 4 ? 96 : 112) && fruit.Y == (level == 4 ? 160 : 144),
                $"Grid level {level} did not snap both native axes.");
        }
        Click(ui, 110.25, 145.25, shift: true);
        Check(ui.View.Document.Fruits[^1] is { X: 110, Y: 145 }, "Shift did not temporarily release native grid snapping.");
        ui.Key('T');
        Click(ui, 110.25, 145.25);
        Check(ui.View.Document.Fruits[^1] is { X: 110, Y: 145 }, "Disabled native grid still snapped the circle.");
        Click(ui, 110.25, 145.25, shift: true);
        Check(ui.View.Document.Fruits[^1] is { X: 96, Y: 160 }, "Shift did not temporarily enable native grid snapping.");
    }

    private static void DraftCornersAndEditableAnchors()
    {
        var ui = CreateUi(); ui.Key('3');
        var before = ui.View.Document.DeepClone();
        Click(ui, 64, 64); Click(ui, 160, 64);
        var corner = ui.View.OsuCanvasScreen(160, 64);
        ui.View.PointerDoubleClick(corner.X, corner.Y, false, false); ui.Paint();
        Check(ui.View.Document.ImportedSliders.Count == 0, "Double-click committed the native slider instead of creating a red corner.");
        Click(ui, 197, 107, shift: true);
        var hover = ui.View.OsuCanvasScreen(256, 192);
        ui.View.PointerMove(hover.X, hover.Y, false, false); ui.Paint();
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Pending native hover preview changed document content.");
        var tail = ui.View.OsuCanvasScreen(320, 192);
        ui.View.PointerDown(tail.X, tail.Y, 2, false, false); ui.View.PointerUp(tail.X, tail.Y, 2); ui.Paint();
        var slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.ControlPoints.SequenceEqual(new SliderPathPoint[] { new(64, 64), new(160, 64), new(160, 64), new(197, 107), new(320, 192) }),
            "Draft corner, Shift grid override or hovered final endpoint did not retain stable control points.");
        Check(OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack.ImportedSliders.Single().ControlPoints.SequenceEqual(slider.ControlPoints),
            "Native segment separators changed during export.");
        var original = ui.View.Document.DeepClone();
        var moved = ui.View.OsuCanvasScreen(176, 96);
        ui.View.PointerDown(corner.X, corner.Y, 0, false, false);
        ui.View.PointerMove(moved.X, moved.Y, false, false); ui.View.PointerUp(moved.X, moved.Y, 0); ui.Paint();
        slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.ControlPoints[1] == new SliderPathPoint(176, 96) && slider.ControlPoints[2] == slider.ControlPoints[1],
            "Dragging a red corner separated its repeated control points.");
        ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "Red corner drag did not undo atomically.");
        Click(ui, 64, 64);
        var ordinary = ui.View.OsuCanvasScreen(197, 107);
        ui.View.PointerDown(ordinary.X, ordinary.Y, 0, false, true); ui.View.PointerUp(ordinary.X, ordinary.Y, 0); ui.Paint();
        slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.ControlPoints.Count == 6 && slider.ControlPoints[3] == slider.ControlPoints[4], "Ctrl-click did not make the selected control red.");
        ui.View.PointerDown(ordinary.X, ordinary.Y, 2, false, false); ui.View.PointerUp(ordinary.X, ordinary.Y, 2); ui.Paint();
        slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.ControlPoints.Count == 4 && !slider.ControlPoints.Contains(new(197, 107)), "Right-click did not remove the logical red anchor.");
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Red anchor insertion/removal undo lost original geometry.");
    }

    private static void ContentShortcutsDuringControlDrag()
    {
        var ui = CreateUi(); ui.Key('3');
        Click(ui, 64, 64); Click(ui, 160, 96); Click(ui, 256, 64); ui.Key(13);
        var baseline = ui.View.Document.DeepClone();
        var control = ui.View.OsuCanvasScreen(160, 96); var moved = ui.View.OsuCanvasScreen(176, 112);
        ui.View.PointerDown(control.X, control.Y, 0, false, false);
        ui.View.PointerMove(moved.X, moved.Y, false, false);
        var candidate = ui.View.Document.DeepClone();
        foreach (var key in new[] { (46, false), (72, true), (71, true), (37, true), (39, true), (187, true), (189, true), (93, false) })
        {
            ui.Key(key.Item1, ctrl: key.Item2);
            Check(candidate.ContentEquals(ui.View.Document) && ui.View.WantsCapture,
                $"Native shortcut {key.Item1} changed or ended the active control drag.");
        }
        ui.Key(27); ui.View.PointerUp(moved.X, moved.Y, 0); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Cancelling a native drag after content shortcuts lost its baseline.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Count == 0, "Blocked native shortcuts created extra undo steps.");
    }

    private static Ui CreateUi()
    {
        var ui = new Ui(false); ui.View.LibrarySettings.LazerEditing = false;
        ui.LoadDocument(new MapDocument { DurationMs = 5000 });
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, "fixture.wav");
        ui.View.ToggleOsuCanvas(); ui.Paint();
        if (ui.View.DistanceSnapEnabled) ui.Key('Y');
        if (!ui.View.EditorGridSettings.Enabled) ui.Key('T');
        ui.Key('3', ctrl: true);
        return ui;
    }

    private static void Click(Ui ui, double x, double y, bool shift = false)
    {
        var p = ui.View.OsuCanvasScreen(x, y);
        ui.View.PointerDown(p.X, p.Y, 0, shift, false); ui.View.PointerUp(p.X, p.Y, 0, shift); ui.Paint();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
