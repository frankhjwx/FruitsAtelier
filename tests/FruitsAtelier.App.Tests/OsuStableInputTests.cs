using FruitsAtelier.Core;

internal static class OsuStableInputTests
{
    public static void Run()
    {
        NativeGridLevelsAndShift();
        CircularPlacementAndControlEdits();
        BezierPointRemovalRetainsType();
        DraftToolSwitchCompletesValidGeometry();
        ExplicitCurveTypesAndLength();
        ImportedEditsPreserveGeometry();
        DraftCornersAndEditableAnchors();
        ContentShortcutsDuringControlDrag();
    }

    private static void CircularPlacementAndControlEdits()
    {
        var ui = CreateUi(); ui.Key('3');
        Click(ui, 64, 192); Click(ui, 192, 64);
        var tail = ui.View.OsuCanvasScreen(320, 192);
        ui.View.PointerMove(tail.X, tail.Y, false, false); ui.Paint();
        var preview = (IReadOnlyList<SliderPathPoint>)typeof(FruitsAtelier.App.Editor.EditorView)
            .GetField("osuDraftPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
        CheckArc(preview);
        ui.View.PointerDown(tail.X, tail.Y, 2, false, false); ui.View.PointerUp(tail.X, tail.Y, 2); ui.Paint();
        var slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.PathType == 'P' && slider.ControlPoints.Count == 3, "One interior anchor must create a perfect-circle slider.");
        Check(preview.SequenceEqual(ImportedSliderConverter.Path(slider)), "Committing the circular draft changed its preview geometry.");
        CheckArc(ImportedSliderConverter.Path(OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack.ImportedSliders.Single()));
        var baseline = ui.View.Document.DeepClone();
        var middle = ui.View.OsuCanvasScreen(192, 64);
        var moved = ui.View.OsuCanvasScreen(192, 96);
        ui.View.PointerDown(middle.X, middle.Y, 0, false, false);
        ui.View.PointerMove(moved.X, moved.Y, false, false); ui.View.PointerUp(moved.X, moved.Y, 0); ui.Paint();
        Check(ui.View.Document.ImportedSliders.Single().PathType == 'P', "Moving a circular anchor changed its curve type.");
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Circular anchor dragging did not undo.");
        Click(ui, 64, 192);
        ui.View.PointerDown(middle.X, middle.Y, 0, false, true); ui.View.PointerUp(middle.X, middle.Y, 0); ui.Paint();
        slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.PathType == 'B' && slider.ControlPoints[1] == slider.ControlPoints[2], "A red anchor must convert the circle to segmented Bezier geometry.");
        _ = OsuBeatmapWriter.Serialize(ui.View.Document);
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Red circular anchor did not undo.");
        Click(ui, 64, 192);
        var inserted = ui.View.OsuCanvasScreen(272, 112);
        ui.View.PointerDown(inserted.X, inserted.Y, 0, false, true); ui.View.PointerUp(inserted.X, inserted.Y, 0); ui.Paint();
        Check(ui.View.Document.ImportedSliders.Single() is { PathType: 'B', ControlPoints.Count: 4 }, "Adding a fourth point must leave perfect-circle mode.");
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Circular point insertion did not undo.");
        Click(ui, 64, 192);
        ui.View.PointerDown(middle.X, middle.Y, 2, false, false); ui.View.PointerUp(middle.X, middle.Y, 2); ui.Paint();
        Check(ui.View.Document.ImportedSliders.Single() is { PathType: 'B', ControlPoints.Count: 2 }, "Removing the circular midpoint must produce a straight path.");
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Circular point removal did not undo.");
        Click(ui, 64, 192);
        ui.Key('G', ctrl: true); ui.Key('H', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Single().PathType == 'P', "Reversing and mirroring a circle changed its curve type.");
        _ = OsuBeatmapWriter.Serialize(ui.View.Document);
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Circular mirror and reverse did not undo.");

        static void CheckArc(IReadOnlyList<SliderPathPoint> path)
        {
            Check(path.Count > 3 && path.Min(p => p.GeometryY) < 65, "The three-point path did not pass through its circular midpoint.");
            Check(path.All(p => Math.Abs(Math.Sqrt(Math.Pow(p.X - 192, 2) + Math.Pow(p.GeometryY - 192, 2)) - 128) < .11),
                "The three-point path was not a constant-radius circle.");
        }
    }

    private static void BezierPointRemovalRetainsType()
    {
        var ui = CreateUi(); ui.Key('3');
        Click(ui, 64, 192); Click(ui, 128, 64); Click(ui, 256, 64); Click(ui, 320, 192); ui.Key(13);
        Check(ui.View.Document.ImportedSliders.Single().PathType == 'B', "Four controls must create a Bezier slider.");
        var point = ui.View.OsuCanvasScreen(256, 64);
        ui.View.PointerDown(point.X, point.Y, 2, false, false); ui.View.PointerUp(point.X, point.Y, 2); ui.Paint();
        Check(ui.View.Document.ImportedSliders.Single() is { PathType: 'B', ControlPoints.Count: 3 },
            "Removing a Bezier control must retain Bezier rather than silently changing to a circle.");
    }

    private static void DraftToolSwitchCompletesValidGeometry()
    {
        foreach (int key in new[] { (int)'1', (int)'2', (int)'3', (int)'4' })
        {
            var ui = CreateUi(); ui.Key('3');
            Click(ui, 64, 192); Click(ui, 192, 64);
            var tail = ui.View.OsuCanvasScreen(320, 192);
            ui.View.PointerMove(tail.X, tail.Y, false, false); ui.Key(key);
            Check(ui.View.Document.ImportedSliders.Single().PathType == 'P', "Switching tools discarded a valid circular draft.");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ImportedSliders.Count == 0 && !ui.View.IsDirty, "Tool-switch completion did not create one undo step.");
        }
        var empty = CreateUi(); empty.Key('3'); Click(empty, 64, 64); empty.Key('1');
        Check(empty.View.Document.ImportedSliders.Count == 0 && !empty.View.IsDirty, "An insufficient draft was committed on tool switch.");
    }

    private static void ExplicitCurveTypesAndLength()
    {
        foreach (char type in new[] { 'L', 'B', 'P', 'C' })
        {
            var ui = CreateUi(); ui.Key('3');
            Click(ui, 64, 192); Click(ui, 192, 64); Click(ui, 320, 192); ui.Key(13);
            Click(ui, 64, 192); ui.Key(93);
            ui.ClickText(FruitsAtelier.Localization.Strings.Get("osu.path." + type));
            var slider = ui.View.Document.ImportedSliders.Single();
            Check(slider.PathType == type, "The curve-type menu did not apply " + type);
            var baseline = ui.View.Document.DeepClone();
            double length = slider.PixelLength;
            Click(ui, 64, 192); ui.Key(93); ui.ClickText(FruitsAtelier.Localization.Strings.Get("osu.lengthIncrease"));
            slider = ui.View.Document.ImportedSliders.Single();
            Check(slider.PixelLength > length && slider.PathType == type, "Length adjustment changed curve type or failed.");
            var persisted = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
            Check(persisted.ContentEquals(ui.View.Document), "Edited slider geometry did not persist.");
            Check(OsuBeatmapWriter.Serialize(persisted).ReadBack.ImportedSliders.Single().PathType == type, "Export changed the chosen curve type.");
            ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Length adjustment did not undo.");
            Click(ui, 64, 192);
            var from = ui.View.OsuCanvasScreen(192, 64); var to = ui.View.OsuCanvasScreen(208, 80);
            ui.View.PointerDown(from.X, from.Y, 0, false, false);
            ui.View.PointerMove(to.X, to.Y, false, false); ui.Key(27); ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Canceling an explicit-curve drag lost its original geometry.");
        }
    }

    private static void ImportedEditsPreserveGeometry()
    {
        var ui = CreateUi();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n64,192,1000,2,0,P|192:64|320:192,1,400\n");
        map.DerandomizeFSliderDroplets = true;
        ui.LoadDocument(map); ui.Key('1'); Click(ui, 64, 192);
        var baseline = ui.View.Document.DeepClone();
        var from = ui.View.OsuCanvasScreen(192, 64); var to = ui.View.OsuCanvasScreen(192, 96);
        ui.View.PointerDown(from.X, from.Y, 0, false, false);
        ui.View.PointerMove(to.X, to.Y, false, false); ui.View.PointerMove(from.X, from.Y, false, false);
        ui.View.PointerUp(from.X, from.Y, 0); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "A no-op imported drag changed native geometry ownership.");
        ui.View.PointerDown(from.X, from.Y, 0, false, false);
        ui.View.PointerMove(to.X, to.Y, false, false); ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.LegacyDropletRefreshBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(5); }
        Check(!ui.View.LegacyDropletRefreshBusy, "Legacy effects did not finish after control editing.");
        var edited = ui.View.Document.ImportedSliders.Single();
        Check(edited.PreserveNativeGeometry && edited.RandomizationTrack is not null && edited.RandomizationSource is { PathType: 'P' }
            && edited.RandomizationSource.ControlPoints.SequenceEqual(new SliderPathPoint[] { new(64, 192), new(192, 96), new(320, 192) }),
            "Editing an imported circle lost its authored controls while applying droplet effects.");
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Imported geometry ownership did not undo.");
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
