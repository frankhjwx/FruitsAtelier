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
                MetadataAcrossCanvases(); NativeRotation(); RotationPreview(); RotationPreferences(); NativeStream(); MovementGhost(); MovementBeatRange(); TimingNavigation(); CatchRotation(); CatchArc(); FixedIntervals();
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
        Near(slider.X, 100); Near(slider.Y, 100);
        Near(slider.ControlPoints[1].X, 0); Near(slider.ControlPoints[1].GeometryY, 200);
        Check(slider.TimeMs == 1000 && slider.PixelLength == map.ImportedSliders[0].PixelLength, "Rotation changed slider timing or length.");
        Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Rotated native slider could not be exported.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Native rotation did not undo.");
        Select(ui, id); ui.Key('R', ctrl: true, shift: true); Check(ui.View.RotationVisible, "Rotation dialog shortcut did not open.");
        ui.ClickText(L.Get("rotation.selection")); ui.Type("90"); ui.Key(13);
        Check(!ui.View.RotationVisible, "Valid rotation dialog did not apply.");
        Near(ui.View.Document.ImportedSliders[0].X, 100);
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Dialog rotation did not undo in one step.");
        Select(ui, id); ui.Key('R', ctrl: true, shift: true); ui.Type("45"); ui.Key(27);
        Check(ui.View.Document.ContentEquals(map), "Cancelling rotation changed content.");
        ui.Key(188, ctrl: true); Near(ui.View.Document.ImportedSliders[0].X, 100); Near(ui.View.Document.ImportedSliders[0].Y, 100);
        Near(ui.View.Document.ImportedSliders[0].ControlPoints[1].X, 200); Near(ui.View.Document.ImportedSliders[0].ControlPoints[1].GeometryY, 0);
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
        Check(ui.View.Document.Tracks[0].Nodes[0].TimeMs == 1000 && ui.View.Document.Tracks[0].Nodes[^1].TimeMs == 1500,
            "Catch rotation changed the authored time interval.");
        Near(ui.View.Document.Tracks[0].Nodes[0].X, 180);
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Catch rotation did not undo.");
        Select(ui, track.Id); ui.Key('R', ctrl: true, shift: true);
        ui.ClickText(L.Get("rotation.selection"));
        foreach (string angleText in new[] { "5", "-5", "8", "5", "0" })
        {
            ui.Key('A', ctrl: true); ui.Type(angleText); ui.Paint();
            var preview = (CatchConversionResult)typeof(EditorView).GetField("conversion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
            var uncached = CatchStreamConverter.Convert(ui.View.Document);
            Check(preview.Objects.SequenceEqual(uncached.Objects), "Cached Catch rotation preview differs from fresh conversion.");
            Near(ui.View.Document.Tracks[0].Nodes[0].X, 180);
        }
        Check(ui.View.Document.ContentEquals(map), "Returning to zero did not restore the rotation baseline.");
        ui.Key(27);
        var straight = new MapDocument { DurationMs = 10000 };
        var vertical = new CurveTrack { Kind = CurveKind.Linear };
        vertical.Nodes.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 1500, X = 256 }]); straight.Tracks.Add(vertical);
        ui = NewUi(straight); Select(ui, vertical.Id);
        ui.Key('R', ctrl: true, shift: true); ui.ClickText(L.Get("rotation.selection")); ui.Type("90");
        var validPreview = ui.View.Document.DeepClone();
        double limited = double.Parse((string)typeof(EditorView).GetField("rotationAngle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!, System.Globalization.CultureInfo.InvariantCulture);
        Check(limited > 0 && limited < 90, "Catch rotation control did not stop at a valid boundary.");
        Check(CurveMath.Validate(ui.View.Document).Count == 0 && ui.View.Document.Tracks[0].Nodes[0].TimeMs == 1000
            && ui.View.Document.Tracks[0].Nodes[^1].TimeMs == 1500, "Limited rotation changed time bounds or produced invalid geometry.");
        ui.Key(27);
        Check(ui.View.Document.ContentEquals(straight), "Cancelling an invalid catch angle did not restore the original curve.");
        Select(ui, vertical.Id); ui.Key('R', ctrl: true, shift: true); ui.Type("180"); ui.Key(13);
        Check(!ui.View.RotationVisible && !ui.View.Document.ContentEquals(straight), "A boundary-clamped rotation could not be confirmed.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(straight), "Boundary-clamped rotation did not undo exactly.");
        var collision = new MapDocument { DurationMs = 10000 };
        collision.Fruits.AddRange([new() { TimeMs = 1000.4, X = 256 }, new() { TimeMs = 1000.9, X = 256 }]);
        var simultaneous = NewUi(collision); Select(simultaneous, collision.Fruits.Select(f => f.Id).ToArray()); simultaneous.Key(190, ctrl: true);
        Check(simultaneous.View.Document.ContentEquals(collision), "Catch rotation accepted notes that export to the same millisecond.");
        var before = map.DeepClone(); var p = CurveMath.Evaluate(before.Tracks[0], 0, .37);
        SelectionRotation.Apply(map, new HashSet<Guid> { track.Id }, 5, false, true);
        var q = CurveMath.Evaluate(map.Tracks[0], 0, .37);
        double angle = 5 * Math.PI / 180;
        double factor = 500 / (500 * Math.Cos(angle) - 80 * Math.Sin(angle));
        Near(q.X, 220 + ((p.X - 220) * Math.Cos(angle) + (p.TimeMs - 1250) * Math.Sin(angle)) * factor);
        double rotatedStart = 1250 - 250 * Math.Cos(angle) + 40 * Math.Sin(angle);
        Near(q.TimeMs, 1000 + (1250 + (p.TimeMs - 1250) * Math.Cos(angle) - (p.X - 220) * Math.Sin(angle) - rotatedStart) * factor);
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
            double time = 1250 + (p.TimeMs - 1250) - (p.X - 180) * Math.Tan(angle) / .3;
            double x = 180 + (p.X - 180) + (p.TimeMs - 1250) * .3 * Math.Tan(angle);
            Check(Math.Abs(CurveMath.PositionAtTime(track, time) - x) < .005, "Catch arc fitting did not preserve the rotated trajectory.");
        }
    }
    private static void RotationPreview()
    {
        var map = SliderMap(); var ui = NewUi(map, true); Guid id = map.ImportedSliders[0].Id;
        Select(ui, id); ui.Key('R', ctrl: true, shift: true); ui.ClickText(L.Get("rotation.selection"));
        ui.Type("30"); ui.Paint();
        Check(!ui.View.Document.ContentEquals(map), "Rotation was not visible before confirmation.");
        ui.Key('A', ctrl: true); ui.Type("60"); ui.Paint();
        var expected = map.DeepClone(); SelectionRotation.Apply(expected, new HashSet<Guid> { id }, 60, true, true, selectionOrigin: (100, 100));
        Near(ui.View.Document.ImportedSliders[0].X, expected.ImportedSliders[0].X);
        Near(ui.View.Document.ImportedSliders[0].Y, expected.ImportedSliders[0].Y);
        var beforeMove = ui.View.Document.DeepClone(); var bounds = ui.View.RotationBounds;
        ui.View.PointerDown(bounds.X + 20, bounds.Y + 20, 0, false, false);
        ui.View.PointerMove(bounds.X + 60, bounds.Y + 50, false, false);
        ui.View.PointerUp(bounds.X + 60, bounds.Y + 50, 0); ui.Paint();
        Near(ui.View.RotationBounds.X, bounds.X + 40); Near(ui.View.RotationBounds.Y, bounds.Y + 30);
        Check(ui.View.Document.ContentEquals(beforeMove), "Moving the rotation window changed the preview.");
        Check(!ui.View.PrepareFileOperation(), "A provisional rotation escaped through saving.");
        ui.ClickText(L.Get("mac.cancel"));
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Cancel did not restore the original rotation state.");
        ui.Key('R', ctrl: true, shift: true); ui.Type("-90"); ui.Paint();
        expected = map.DeepClone(); SelectionRotation.Apply(expected, new HashSet<Guid> { id }, -90, true, true, selectionOrigin: (100, 100));
        Near(ui.View.Document.ImportedSliders[0].X, expected.ImportedSliders[0].X);
        Near(ui.View.Document.ImportedSliders[0].Y, expected.ImportedSliders[0].Y);
        ui.ClickText(L.Get("rotation.anticlockwise"));
        expected = map.DeepClone(); SelectionRotation.Apply(expected, new HashSet<Guid> { id }, 90, true, true, selectionOrigin: (100, 100));
        Near(ui.View.Document.ImportedSliders[0].ControlPoints[1].X, expected.ImportedSliders[0].ControlPoints[1].X);
        Near(ui.View.Document.ImportedSliders[0].ControlPoints[1].GeometryY, expected.ImportedSliders[0].ControlPoints[1].GeometryY);
        ui.ClickText(L.Get("rotation.clockwise"));
        ui.Key(13); ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(map), "Negative rotation did not commit and undo exactly.");
        Select(ui, id);
        ui.Key('R', ctrl: true, shift: true); ui.Type("30"); ui.Paint(); ui.Key(13);
        var committed = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Preview confirmation was not one undo step.");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(committed), "Confirmed preview did not redo exactly.");
        Select(ui, id); ui.Key('R', ctrl: true, shift: true); ui.Type("45"); ui.Paint(); ui.View.CancelInteraction(); ui.Paint();
        Check(!ui.View.RotationVisible && ui.View.Document.ContentEquals(committed), "Interrupted preview was not cancelled.");
        Select(ui, id); ui.Key('R', ctrl: true, shift: true);
        foreach (string angleText in new[] { "-180", "180" })
        {
            ui.Key('A', ctrl: true); ui.Type(angleText); ui.Paint();
            Near(ui.View.Document.ImportedSliders[0].X, committed.ImportedSliders[0].X);
            Near(ui.View.Document.ImportedSliders[0].Y, committed.ImportedSliders[0].Y);
        }
        ui.Key('A', ctrl: true); ui.Type("181");
        Check((string)typeof(EditorView).GetField("rotationAngle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)! == "180",
            "Rotation input did not clamp at 180 degrees.");
        ui.Key(27);
        Select(ui, id); ui.Key('R', ctrl: true, shift: true);
        bounds = ui.View.RotationBounds;
        float left = bounds.X + 24, right = bounds.X + bounds.Width - 138, y = bounds.Y + 97;
        foreach (int angle in new[] { 45, 90, 135, 180, 180 })
        {
            ui.Click(right - 1, y); CheckAngle(angle);
        }
        foreach (int angle in new[] { 135, 90, 45, 0, -45, -90, -135, -180, -180 })
        {
            ui.Click(left + 1, y); CheckAngle(angle);
        }
        ui.View.PointerDown(left, y, 0, false, false);
        ui.View.PointerMove(left + (right - left) * 210 / 360, y, false, false);
        ui.View.PointerUp(left + (right - left) * 210 / 360, y, 0); ui.Paint(); CheckAngle(30);
        ui.Key(27);
        Check(ui.View.Document.ContentEquals(committed), "Slider clicks did not cancel exactly.");

        void CheckAngle(int angle)
        {
            Check((string)typeof(EditorView).GetField("rotationAngle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)! == angle.ToString(),
                "Rotation slider click did not step or clamp to the expected angle.");
            var rotated = committed.DeepClone();
            SelectionRotation.Apply(rotated, new HashSet<Guid> { id }, angle, true, true, selectionOrigin: (committed.ImportedSliders[0].X, committed.ImportedSliders[0].Y));
            Check(ui.View.Document.ImportedSliders[0].ControlPoints.SequenceEqual(rotated.ImportedSliders[0].ControlPoints), "Slider angle did not update its preview.");
        }
    }
    private static void RotationPreferences()
    {
        var defaults = System.Text.Json.JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Check(defaults.RotationSelectionCentre && defaults.RotationClockwise, "Existing settings did not default to selection-centred clockwise rotation.");
        var map = SliderMap(); var ui = NewUi(map, true); Guid id = map.ImportedSliders[0].Id;
        string path = Path.GetFullPath(Path.Combine("artifacts", "tests", "rotation-preferences.json"));
        int saves = 0;
        ui.View.RequestViewPreference = () => { ui.View.LibrarySettings.Save(path); saves++; };
        Select(ui, id); ui.Key('R', ctrl: true, shift: true);
        ui.ClickText(L.Get("rotation.playfield")); ui.ClickText(L.Get("rotation.anticlockwise")); ui.Key(27);
        var loaded = LibrarySettings.Load(path);
        Check(saves == 2 && !loaded.RotationSelectionCentre && !loaded.RotationClockwise,
            "Rotation origin and direction were not saved globally on choice.");
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Saving rotation preferences changed beatmap content.");
        var restored = NewUi(map, true);
        typeof(EditorView).GetProperty("LibrarySettings")!.SetValue(restored.View, loaded);
        Select(restored, id); restored.Key('R', ctrl: true, shift: true); restored.Type("90");
        Near(restored.View.Document.ImportedSliders[0].X, 164); Near(restored.View.Document.ImportedSliders[0].Y, 348);
        restored.Key(27); restored.View.ToggleOsuCanvas(); restored.View.LoadDocument(new MapDocument());
        Check(!restored.View.LibrarySettings.RotationSelectionCentre && !restored.View.LibrarySettings.RotationClockwise,
            "Switching maps or canvases reset global rotation preferences.");
    }

    private static void FixedIntervals()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.AddRange([new() { TimeMs = 1000.25, X = 100 }, new() { TimeMs = 1200.5, X = 180 }, new() { TimeMs = 1500.75, X = 260 }]);
        foreach (var (start, end, x) in new[] { (3000.125, 3500.875, 180d), (5000.25, 5600.125, 200d) })
        {
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new() { TimeMs = start, X = x }, new() { TimeMs = end, X = x + 40 }]); map.Tracks.Add(track);
        }
        var original = map.DeepClone();
        SelectionRotation.Apply(map, map.Fruits.Select(f => f.Id).Concat(map.Tracks.Select(t => t.Id)).ToHashSet(), 5, false, true, .3);
        Check(map.Fruits[0].TimeMs == original.Fruits[0].TimeMs && map.Fruits[^1].TimeMs == original.Fruits[^1].TimeMs,
            "Catch note rotation changed its first or last timestamp.");
        for (int i = 0; i < map.Tracks.Count; i++)
            Check(map.Tracks[i].Nodes[0].TimeMs == original.Tracks[i].Nodes[0].TimeMs && map.Tracks[i].Nodes[^1].TimeMs == original.Tracks[i].Nodes[^1].TimeMs,
                "Multi-curve rotation changed an individual curve's time interval.");
        Check(map.DurationMs == original.DurationMs && OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches,
            "Fixed-time rotation changed map duration or invalidated export.");
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
    private static void MovementBeatRange()
    {
        foreach (var (previous, next, changing, expected) in new[] { (1000d, 3000d, false, 2), (999d, 3001d, false, 0),
            (999d, 3000d, false, 1), (1000d, 3001d, false, 1), (1000d, 2250d, true, 2), (999d, 2251d, true, 0) })
        {
            double time = changing ? 1750 : 2000;
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, ApproachRate = 0 };
            if (changing) map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 1500, BeatLengthMs = 250 }]);
            map.Fruits.AddRange([new() { TimeMs = previous, X = 100, Y = 100 }, new() { TimeMs = next, X = 300, Y = 200 }]);
            var ui = NewUi(map, true);
            var button = ui.View.OsuMovementAnalysisBounds; ui.Click(button.X + 5, button.Y + 5);
            ui.View.UpdateTransport(time, 10000, true, false, false, null, null); ui.Key('2');
            var pointer = ui.View.OsuCanvasScreen(200, 150); ui.View.PointerMove(pointer.X, pointer.Y, false, false); ui.Paint();
            Check(ui.Canvas.Lines.Count(l => l.Width == 3 && Math.Abs(l.Opacity - .65f) < .001) == expected,
                "Placement movement connections did not obey the two-beat window.");
            Check((ui.View.MovementReadout.Previous.HasValue ? 1 : 0) + (ui.View.MovementReadout.Next.HasValue ? 1 : 0) == expected,
                "Movement panel retained distant neighbours.");
            Check((ui.View.DistanceReadout.Previous.HasValue ? 1 : 0) + (ui.View.DistanceReadout.Next.HasValue ? 1 : 0) == expected,
                "Movement panel retained distant ratios.");
            ui.Click(pointer.X, pointer.Y); ui.Key('1'); Select(ui, ui.View.Document.Fruits.Single(f => f.TimeMs == time).Id);
            Check(ui.Canvas.Lines.Count(l => l.Width == 3 && Math.Abs(l.Opacity - .65f) < .001) == expected,
                $"Committed movement connections did not obey the two-beat window: previous={previous}, next={next}, changing={changing}, expected={expected}, actual={ui.Canvas.Lines.Count(l => l.Width == 3 && Math.Abs(l.Opacity - .65f) < .001)}.");
        }
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
