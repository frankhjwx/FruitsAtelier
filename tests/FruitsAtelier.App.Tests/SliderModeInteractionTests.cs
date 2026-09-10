using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;

internal static class SliderModeInteractionTests
{
    public static void LegacyDraftAndMixedModes()
    {
        var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 10000 });
        ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy); ui.Key('B');
        ui.ClickMap(1000, 200); ui.ClickMap(1250, 250); ui.MoveMap(1500, 200);
        var arc = ui.View.Document.Tracks.Single();
        Check(arc.Nodes[0].OutgoingCurve?.Kind == ControlCurveKind.CircularArc, "Three-point preview is not an arc.");
        Near(ControlCurveMath.ReferenceScale(8), arc.Nodes[0].OutgoingCurve!.ReferenceScale);
        Right(ui, 1500, 200);
        Check(ui.View.ActiveTool == "Select", "Right-click did not finish legacy drawing.");
        var saved = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Tracks.Count == 0, "Draft was not one undo step.");
        ui.Key('Y', ctrl: true); Check(saved.ContentEquals(ui.View.Document), "Redo changed authored controls.");
        ui.Key('B'); ui.View.SetSliderEditingMode(SliderEditingMode.PenTool); ui.Paint();
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.newSlider"));
        ui.ClickMap(2000, 300); ui.DownMap(2500, 350); ui.MoveMap(2625, 380); ui.UpMap(2625, 380); ui.ClickMap(3000, 300); ui.Key(13);
        Check(ui.View.Document.Tracks.Count == 2 && ui.View.Document.Tracks[0].Nodes[0].OutgoingCurve is not null
            && ui.View.Document.Tracks[1].Nodes.All(n => n.OutgoingCurve is null), "Both editing styles did not coexist.");
        var penId = ui.View.Document.Tracks[1].Id;
        ui.Key('B'); ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy); ui.Paint();
        ui.DownMap(2625, 380); ui.MoveMap(2750, 390); ui.UpMap(2750, 390);
        var pen = ui.View.Document.Tracks.Single(t => t.Id == penId);
        Near(250, pen.Nodes[1].HandleOut.TimeMs); Near(40, pen.Nodes[1].HandleOut.X);
        Check(pen.Nodes.All(n => n.OutgoingCurve is null), "Legacy edit of cubic handles unnecessarily changed representation.");
        Valid(ui);
    }

    public static void SwitchArAndLocalPenEdit()
    {
        var map = Arc(); var ui = new Ui(false); ui.LoadDocument(map);
        var track = ui.View.Document.Tracks[0];
        ui.ClickText(track.Name); ui.Key('B');
        var baseline = ui.View.Document.DeepClone();
        for (int i = 0; i < 3; i++)
        {
            ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy); ui.Paint();
            ui.View.SetSliderEditingMode(SliderEditingMode.PenTool); ui.Paint();
        }
        Check(baseline.ContentEquals(ui.View.Document), "Mode switching modified slider content.");
        ui.SetAr("6");
        Check(track.Nodes[0].OutgoingCurve!.ReferenceScale == ControlCurveMath.ReferenceScale(8), "AR edit redefined existing arc.");
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.restoreAr"));
        ui.Key(36);
        double centerStart = 1000 - ui.Plot.Height / 2 / ui.View.PixelsPerMs;
        ui.View.Wheel(ui.Plot.X, ui.Plot.Bottom, (float)((centerStart - ui.View.ViewStartMs) * ui.View.PixelsPerMs / 78 * 120), false); ui.Paint();
        // Zoom around the segment so its small, exact-approximation endpoint handles can be selected.
        var p = Screen(ui, 1000, 200);
        ui.View.Wheel(p.X, p.Y, 120 * 18, true); ui.Paint();
        var handle = ControlCurveEditing.PenSegments(track, 0)[0].Out;
        p = Screen(ui, handle.TimeMs, handle.X);
        var beforeDrag = ui.View.Document.DeepClone();
        ui.View.PointerDown(p.X, p.Y, 0, false, false);
        ui.View.PointerUp(p.X, p.Y, 0);
        Check(beforeDrag.ContentEquals(ui.View.Document), "Merely clicking a proxy handle converted its segment.");
        ui.View.PointerDown(p.X, p.Y, 0, false, false);
        ui.View.PointerMove(p.X + 10, p.Y, false, false);
        ui.View.PointerUp(p.X + 10, p.Y, 0); ui.Paint();
        Check(ui.View.Document.Tracks[0].Nodes[0].OutgoingCurve is null, $"Pen handle movement did not convert the arc: handle={handle}, screen={p}, plot={ui.View.CanvasPlotBounds}, scale={ui.View.PixelsPerMs}, selected={string.Join(",", ui.View.SelectedAnchorIds)}, status={ui.View.StatusMessage}.");
        ui.Key('Z', ctrl: true); Check(beforeDrag.ContentEquals(ui.View.Document), "Undo did not restore the exact pre-pen arc.");
        Valid(ui);
    }

    public static void InsertDeleteAndBoundary()
    {
        var ui = new Ui(); ui.LoadDocument(Arc()); ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy);
        ui.ClickText(ui.View.Document.Tracks[0].Name); ui.Key('B');
        var saved = ui.View.Document.DeepClone();
        var p = Screen(ui, 1375, 240);
        ui.View.PointerDown(p.X, p.Y, 0, false, true); ui.View.PointerUp(p.X, p.Y, 0); ui.Paint();
        Check(SliderControlEditing.Vertices(ui.View.Document.Tracks[0]).Count == 4, "Ctrl+click did not insert a control.");
        ui.Key('Z', ctrl: true); Check(saved.ContentEquals(ui.View.Document), "Insert undo changed original shape.");
        ui.ClickText(ui.View.Document.Tracks[0].Name); ui.Key('B');
        ui.ClickMap(1250, 250); p = Screen(ui, 1250, 250);
        ui.View.PointerDoubleClick(p.X, p.Y, false, false); ui.Paint();
        Check(ui.View.Document.Tracks[0].Nodes.Count == 3, "Double-click did not create a segment boundary.");
        ui.View.PointerDoubleClick(p.X, p.Y, false, false); ui.Paint();
        Check(ui.View.Document.Tracks[0].Nodes.Count == 2, "Double-click did not remove a segment boundary.");
        Right(ui, 1250, 250);
        Check(SliderControlEditing.Vertices(ui.View.Document.Tracks[0]).Count == 2, "Right-click did not delete the control.");
        ui.Key('Z', ctrl: true); Check(SliderControlEditing.Vertices(ui.View.Document.Tracks[0]).Count == 3, "Delete undo lost a control.");
        Valid(ui);
    }

    public static void GlobalModeMenu()
    {
        var ui = new Ui(); ui.LoadDocument(Arc());
        var before = ui.View.Document.DeepClone();
        foreach (int key in new[] { 'V', 'F', 'B', 'N' })
        {
            ui.Key(key);
            foreach (var mode in Enum.GetValues<SliderEditingMode>())
            {
                var current = FruitsAtelier.Localization.Strings.Get(ui.View.SliderMode == SliderEditingMode.OsuLegacy ? "ui.osuLegacyMode" : "ui.penToolMode") + " ▾";
                var label = ui.Canvas.Texts.Single(t => t.Value == current);
                Check(label.Y >= 47 && label.Y <= 77, "Mode selector is not on the Snap toolbar row.");
                ui.ClickText(current);
                ui.ClickText(FruitsAtelier.Localization.Strings.Get(mode == SliderEditingMode.OsuLegacy ? "ui.osuLegacyMode" : "ui.penToolMode"));
                Check(ui.View.SliderMode == mode, "Global mode menu was unavailable for the current tool.");
                Check(before.ContentEquals(ui.View.Document), "Global mode switching changed content.");
            }
        }
        ui.ClickText(ui.View.Document.Tracks[0].Name); ui.Key('B');
        var end = Screen(ui, 1500, 200);
        ui.View.PointerDown(end.X + 28, end.Y, 0, false, false);
        ui.View.PointerMove(end.X + 28, end.Y - 45, false, false);
        ui.View.PointerUp(end.X + 28, end.Y - 45, 0); ui.Paint();
        Check(ui.View.Document.Tracks[0].SpanCount == 1, "Removed repeat marker still changed repeats.");
    }

    public static void LazerPlacementAndSelection()
    {
        var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 10000 });
        ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy); ui.Key('B');
        ui.ClickMap(1000, 200); ui.ClickMap(1250, 240);
        // A second ordinary click, even outside the OS double-click interval, starts a segment.
        ui.ClickMap(1250, 240); ui.MoveMap(1500, 200);
        var p = Screen(ui, 1500, 200);
        ui.View.PointerDown(p.X, p.Y, 2, false, false);
        Check(ui.View.ActiveTool == "Slider", "Right mouse-down completed the draft before release.");
        ui.View.PointerUp(p.X, p.Y, 2); ui.Paint();
        Check(ui.View.Document.Tracks[0].Nodes.Count == 3, "Repeated endpoint click did not create two segments.");
        Check(ui.View.ActiveTool == "Select", "Completed slider was not selected.");
        var before = ui.View.Document.DeepClone();
        // Selection alone enables control editing; entering B first is unnecessary.
        p = Screen(ui, 1125, 225);
        ui.View.PointerDown(p.X, p.Y, 0, false, true); ui.View.PointerUp(p.X, p.Y, 0); ui.Paint();
        Check(SliderControlEditing.Vertices(ui.View.Document.Tracks[0]).Count == 4, "Selected slider rejected Ctrl insertion outside B mode.");
        ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Direct control edit did not undo in one step.");
        ui.ClickText(ui.View.Document.Tracks[0].Name); ui.Key('V'); Right(ui, 1250, 240);
        Check(ui.View.Document.Tracks[0].Nodes.Count == 2, "Selected slider rejected right-click deletion outside B mode.");
        Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("Ctrl+click") || t.Value.Contains("Double-click") || t.Value.Contains("Undo restores")),
            "Instructional prose remained in the editor.");
        Valid(ui);
        ui.ClickMap(1000, 200);
        p = Screen(ui, 1500, 200);
        ui.View.PointerDown(p.X, p.Y, 0, false, true); ui.View.PointerUp(p.X, p.Y, 0);
        Check(ui.View.SelectedAnchorIds.Count == 2, "Ctrl selection did not retain the first control.");
        var target = Screen(ui, 1500, 210);
        ui.View.PointerDown(p.X, p.Y, 0, false, true);
        ui.View.PointerMove(target.X, target.Y, false, true); ui.View.PointerUp(target.X, target.Y, 0); ui.Paint();
        Near(210, ui.View.Document.Tracks[0].Nodes[0].X);
        Near(210, ui.View.Document.Tracks[0].Nodes[^1].X);
        Check(ui.View.SelectedAnchorIds.Count == 2, "Ctrl-drag deselected the dragged control.");

        ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 10000 });
        ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy); ui.Key('B');
        ui.ClickMap(1000, 200); ui.ClickMap(1125, 450); ui.MoveMap(1500, 200);
        Check(ui.View.Document.Tracks[0].Nodes[0].OutgoingCurve?.Kind == ControlCurveKind.Bezier,
            "Unrepresentable circular preview froze instead of falling back to Bezier.");
        Right(ui, 1500, 200); Valid(ui);
    }

    private static MapDocument Arc()
    {
        var map = new MapDocument { DurationMs = 10000, ApproachRate = 8 };
        var track = new CurveTrack { Name = "Shared slider", CompensateTinyDroplets = false };
        var node = new Anchor { TimeMs = 1000, X = 200, OutgoingCurve = new() { Kind = ControlCurveKind.CircularArc, ReferenceScale = ControlCurveMath.ReferenceScale(8) } };
        node.OutgoingCurve.Controls.Add(new() { Offset = new(250, 50) });
        track.Nodes.Add(node); track.Nodes.Add(new() { TimeMs = 1500, X = 200 });
        map.Tracks.Add(track); return map;
    }
    private static (float X, float Y) Screen(Ui ui, double time, double x) => (ui.Plot.X + (float)x / 512 * ui.Plot.Width,
        ui.Plot.Bottom - (float)((time - ui.View.ViewStartMs) * ui.View.PixelsPerMs));
    private static void Right(Ui ui, double time, double x)
    { var p = Screen(ui, time, x); ui.View.PointerDown(p.X, p.Y, 2, false, false); ui.View.PointerUp(p.X, p.Y, 2); ui.Paint(); }
    private static void Valid(Ui ui) { Check(CurveMath.Validate(ui.View.Document).Count == 0, "Invalid edited document."); }
    private static void Near(double a, double b) { Check(Math.Abs(a - b) < 0.001, $"Expected {a}, got {b}."); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
