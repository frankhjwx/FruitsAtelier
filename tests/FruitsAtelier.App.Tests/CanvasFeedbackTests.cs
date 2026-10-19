using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class CanvasFeedbackTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                MetadataAcrossCanvases(); NativeRotation(); NativeStream(); MovementGhost(); TimingNavigation(); CatchRotation(); CatchArc();
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static Ui NewUi(MapDocument map, bool osu = false)
    {
        var ui = new Ui(false); ui.LoadDocument(map); ui.Key('1');
        if (osu) ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        return ui;
    }
    private static void Select(Ui ui, params Guid[] ids)
    {
        typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ui.View, [ids, ids[0]]);
        ui.Paint();
    }
    private static MapDocument SliderMap()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:0\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,100,1000,2,0,L|200:200,2,141.421356237\n");
        map.DurationMs = 10000; return map;
    }
    private static void MetadataAcrossCanvases()
    {
        foreach (bool osu in new[] { false, true })
        {
            var map = SliderMap(); Guid id = map.ImportedSliders[0].Id;
            if (osu)
            {
                map.ImportedSliders.Clear();
                var track = new CurveTrack { Id = id, Kind = CurveKind.Linear };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
                map.Tracks.Add(track);
            }
            var ui = NewUi(map, osu); Select(ui, id);
            var combo = ui.View.AssistButtonBounds[0]; ui.Click(combo.X + 8, combo.Y + 8);
            Check(ObjectFlags.NewCombo(ui.View.Document, id), "Cross-canvas NC edit was rejected.");
            var whistle = ui.View.AssistButtonBounds[1]; ui.Click(whistle.X + 8, whistle.Y + 8);
            Check(ObjectFlags.Sounds(ui.View.Document, id).All(s => (s & 2) != 0), "Cross-canvas hitsound edit was rejected.");
            ui.Key('Q', ctrl: true); ui.Key('W', ctrl: true);
            Check(ObjectFlags.SampleBanks(ui.View.Document, id, true).All(b => b == 1), "Cross-canvas sample-bank edit was rejected.");
            ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(map), "Cross-canvas metadata edits did not undo independently.");
        }
    }
    private static void NativeRotation()
    {
        var map = SliderMap(); map.ImportedSliders[0].SpanCount = 1;
        var ui = NewUi(map, true); Guid id = map.ImportedSliders[0].Id; Select(ui, id);
        ui.Key('R', ctrl: true, shift: true); ui.Key(13);
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Zero-degree rotation modified native geometry or source text.");
        ui.Key(190, ctrl: true);
        var slider = ui.View.Document.ImportedSliders[0];
        Near(slider.X, 200); Near(slider.Y, 100);
        Near(slider.ControlPoints[1].X, 100); Near(slider.ControlPoints[1].GeometryY, 200);
        Check(slider.TimeMs == 1000 && slider.PixelLength == map.ImportedSliders[0].PixelLength, "Rotation changed slider timing or length.");
        Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Rotated native slider could not be exported.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Native rotation did not undo.");
        Select(ui, id); ui.Key('R', ctrl: true, shift: true); Check(ui.View.RotationVisible, "Rotation dialog shortcut did not open.");
        ui.ClickText(L.Get("rotation.selection")); ui.Type("90"); ui.Key(13);
        Check(!ui.View.RotationVisible, "Valid rotation dialog did not apply.");
        Near(ui.View.Document.ImportedSliders[0].X, 200);
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Dialog rotation did not undo in one step.");
        Select(ui, id); ui.Key('R', ctrl: true, shift: true); ui.Type("45"); ui.Key(27);
        Check(ui.View.Document.ContentEquals(map), "Cancelling rotation changed content.");
        ui.Key(188, ctrl: true); Near(ui.View.Document.ImportedSliders[0].X, 100); Near(ui.View.Document.ImportedSliders[0].Y, 200);
        ui.Key('Z', ctrl: true); Select(ui, id);
        ui.Key('R', ctrl: true, shift: true); ui.Type("45"); ui.Key(13);
        Check(!ui.View.RotationVisible && OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches,
            "Arbitrary native rotation did not retain serializable integer geometry.");
    }
    private static void CatchRotation()
    {
        var map = new MapDocument { DurationMs = 10000 };
        var track = new CurveTrack { Kind = CurveKind.Bezier };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 180, HandleOut = new(150, 30) },
            new() { TimeMs = 1500, X = 260, HandleIn = new(-150, -20) }]); map.Tracks.Add(track);
        var ui = NewUi(map); Select(ui, track.Id); ui.Key(190, ctrl: true);
        Check(!ui.View.Document.ContentEquals(map), "Catch 5-degree rotation did not edit the curve.");
        Check(CurveMath.Validate(ui.View.Document).Count == 0, "Catch rotation produced invalid time geometry.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Catch rotation did not undo.");
        var straight = new MapDocument { DurationMs = 10000 };
        var vertical = new CurveTrack { Kind = CurveKind.Linear };
        vertical.Nodes.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 1500, X = 256 }]); straight.Tracks.Add(vertical);
        ui = NewUi(straight); Select(ui, vertical.Id);
        ui.Key('R', ctrl: true, shift: true); ui.ClickText(L.Get("rotation.selection")); ui.Type("90"); ui.Key(13);
        Check(ui.View.RotationVisible && ui.View.Document.ContentEquals(straight), "Catch accepted simultaneous endpoints or partially applied a failed rotation.");
        ui.Key(27);
        var collision = new MapDocument { DurationMs = 10000 };
        collision.Fruits.AddRange([new() { TimeMs = 1000.4, X = 256 }, new() { TimeMs = 1000.9, X = 256 }]);
        var simultaneous = NewUi(collision); Select(simultaneous, collision.Fruits.Select(f => f.Id).ToArray()); simultaneous.Key(190, ctrl: true);
        Check(simultaneous.View.Document.ContentEquals(collision), "Catch rotation accepted notes that export to the same millisecond.");
        var before = map.DeepClone(); var p = CurveMath.Evaluate(before.Tracks[0], 0, .37);
        SelectionRotation.Apply(map, new HashSet<Guid> { track.Id }, 5, false, true);
        var q = CurveMath.Evaluate(map.Tracks[0], 0, .37);
        double angle = 5 * Math.PI / 180;
        Near(q.X, 220 + (p.X - 220) * Math.Cos(angle) + (p.TimeMs - 1250) * Math.Sin(angle));
        Near(q.TimeMs, 1250 + (p.TimeMs - 1250) * Math.Cos(angle) - (p.X - 220) * Math.Sin(angle));
    }
    private static void NativeStream()
    {
        var map = SliderMap();
        map.HitsoundOverrides.Add(new(map.ImportedSliders[0].Id, 0, new(Volume: 35, Additions: 2), InheritedFields: InheritedSampleFields.None));
        var ui = NewUi(map, true); Select(ui, map.ImportedSliders[0].Id);
        ui.Key('F', ctrl: true, shift: true); Check(ui.View.StreamDialogVisible, "Native Stream shortcut did not open.");
        ui.Key(27); Check(ui.View.Document.ContentEquals(map), "Cancelled native Stream changed content.");
        ui.Key('F', ctrl: true, shift: true); ui.Key(13);
        var fruits = ui.View.Document.Fruits;
        Check(fruits.Count > 3 && ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Count == 0, "Native Stream did not create notes.");
        Near(fruits[0].Y, 100); Check(fruits.Max(f => f.Y) > 180 && fruits[^1].Y < 130, "Native Stream lost geometric Y or reverse-span traversal.");
        var output = OsuBeatmapWriter.Serialize(ui.View.Document);
        Check(output.ReadBack.Fruits.Select(f => f.Y).Distinct().Count() > 3, "Stream export discarded geometric Y.");
        Check(ui.View.Document.HitsoundOverrides.Single().SourceId == fruits[0].Id
            && ui.View.Document.HitsoundOverrides.Single().Sample.Volume == 35, "Native Stream discarded a matching event sample override.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Native Stream did not undo atomically.");
    }
    private static void CatchArc()
    {
        var map = new MapDocument { DurationMs = 10000 };
        var track = new CurveTrack();
        var curve = new ControlCurve { Kind = ControlCurveKind.CircularArc, ReferenceScale = .4 };
        curve.Controls.Add(new() { Offset = new(250, 40) });
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 180, OutgoingCurve = curve }, new() { TimeMs = 1500, X = 180 }]);
        map.Tracks.Add(track);
        var original = track.DeepClone();
        SelectionRotation.Apply(map, new HashSet<Guid> { track.Id }, 5, false, true, .3);
        Check(track.Nodes.All(n => n.OutgoingCurve is null), "A nonuniform rotation retained circular geometry for an ellipse.");
        double angle = 5 * Math.PI / 180;
        for (int i = 1; i < 100; i++)
        {
            var p = CurveMath.Evaluate(original, 0, i / 100d);
            double time = 1250 + (p.TimeMs - 1250) * Math.Cos(angle) - (p.X - 180) * Math.Sin(angle) / .3;
            double x = 180 + (p.X - 180) * Math.Cos(angle) + (p.TimeMs - 1250) * .3 * Math.Sin(angle);
            Check(Math.Abs(CurveMath.PositionAtTime(track, time) - x) < .005, "Catch arc fitting did not preserve the rotated trajectory.");
        }
    }
    private static void MovementGhost()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100, Y = 100 }, new() { TimeMs = 1500, X = 300, Y = 200 }]);
        var ui = NewUi(map, true);
        var button = ui.View.OsuMovementAnalysisBounds; ui.Click(button.X + 5, button.Y + 5);
        ui.View.UpdateTransport(1250, 10000, true, false, false, null, null); ui.Key('2');
        var pointer = ui.View.OsuCanvasScreen(200, 150); ui.View.PointerMove(pointer.X, pointer.Y, false, false); ui.Paint();
        Check(ui.Canvas.Lines.Count(l => l.Width == 3 && Math.Abs(l.Opacity - .65f) < .001) == 2,
            "Native placement did not connect the ghost to both neighbours.");
        ui.View.PointerMove(0, 0, false, false); ui.Paint();
        Check(ui.Canvas.Lines.Count(l => l.Width == 3 && Math.Abs(l.Opacity - .65f) < .001) == 1,
            "Leaving native placement did not restore the committed connection.");
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Placement connections changed content.");
        ui.Click(pointer.X, pointer.Y);
        Check(ui.View.Document.Fruits.Count == 3
            && ui.Canvas.Lines.Count(l => l.Width == 3 && Math.Abs(l.Opacity - .65f) < .001) == 2,
            "Committed placement retained a duplicate ghost or lost its movement connections.");
    }
    private static void TimingNavigation()
    {
        var map = new MapDocument { DurationMs = 10000 };
        for (int i = 0; i < 8; i++) map.TimingPoints.Add(new() { TimeMs = i * 500, BeatLengthMs = i == 0 ? 500 : -100, Uninherited = i == 0 });
        var ui = NewUi(map); ui.Key(117); ui.Key(36); ui.Key(40, shift: true); ui.Key(40, shift: true); ui.Key(40, shift: true);
        string copied = ""; ui.View.RequestCopyText = value => copied = value; ui.Key('C', ctrl: true);
        Check(TimingEditing.Parse(copied).Length == 4, "Shift+Down did not extend timing selection beyond two rows.");
        ui.Key(38, shift: true); ui.Key('C', ctrl: true);
        Check(TimingEditing.Parse(copied).Length == 3, "Shift+Up did not shrink the range from its moving end.");
        ui.Key(27); ui.Key('P', ctrl: true, shift: true);
        Check(ui.View.TimingFields.Any(f => f.Key == "volume"), "Ctrl+Shift+P did not open Audio.");
        ui.Key(27); Check(ui.View.Document.ContentEquals(map), "Cancelled timing draft changed content.");
    }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .001, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
