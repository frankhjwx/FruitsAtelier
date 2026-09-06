using FruitsAtelier.Core;

internal static class MultiSelectionTests
{
    public static void ModesAndCtrlSelection()
    {
        var map = AnchorMap();
        var fruit = new Fruit { TimeMs = 1500, X = 70 }; map.Fruits.Add(fruit);
        var ui = Load(map);
        var track = ui.View.Document.Tracks.Single();
        var baseline = ui.View.Document.DeepClone();
        ui.ClickMap(1000, 180);
        Objects(ui, track.Id); Anchors(ui);
        Click(ui, 1500, 70, ctrl: true);
        Objects(ui, track.Id, fruit.Id);
        Click(ui, 1000, 180, ctrl: true);
        Objects(ui, fruit.Id); Anchors(ui);
        Check(baseline.ContentEquals(ui.View.Document), "Ctrl selection moved an object or modified the map.");

        ui.ClickText(track.Name); ui.Key('B');
        ui.ClickMap(2000, 220); Anchors(ui, track.Nodes[1].Id);
        Click(ui, 5000, 340, ctrl: true); Anchors(ui, track.Nodes[1].Id, track.Nodes[4].Id);
        Click(ui, 2000, 220, ctrl: true); Anchors(ui, track.Nodes[4].Id);
        ui.Key('V'); Anchors(ui);
        ui.DownMap(2500, 240); ui.MoveMap(2625, 260); ui.UpMap(2625, 260);
        Objects(ui, track.Id); Anchors(ui);
        Near(1125, ui.View.Document.Tracks.Single().Nodes[0].TimeMs);
        Near(200, ui.View.Document.Tracks.Single().Nodes[0].X);
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Whole-slider drag did not undo atomically.");

        ui.ClickText(track.Name); ui.Key('B'); ui.ClickMap(6000, 450);
        Check(ui.View.ActiveTool == "Select" && ui.View.Document.Tracks.Count == 1,
            "Empty click did not leave existing-slider edit mode.");
        ui.ClickText(track.Name); ui.Key('B');
        ui.ClickText("新 Slider");
        ui.ClickMap(5500, 80); ui.ClickMap(6500, 160); ui.Key(13);
        Check(ui.View.Document.Tracks.Count == 2 && ui.View.Document.Tracks.Single(t => t.Id == track.Id).Nodes.Count == 5,
            "Explicit New Slider did not create a separate curve.");
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "New Slider creation altered the previously selected curve.");
    }

    public static void ObjectBoxAndParentDedup()
    {
        foreach (bool imported in new[] { false, true })
        foreach (char tool in new[] { 'V', 'F' })
        {
            var map = ObjectMap();
            Guid sourceId = map.Tracks.Single().Id;
            if (imported)
            {
                map.Tracks.Clear();
                var slider = new ImportedSlider { TimeMs = 1000, X = 260, Y = 192, PathType = 'L', PixelLength = 600 };
                slider.ControlPoints.Add(new(260, 192)); slider.ControlPoints.Add(new(260, 792));
                map.ImportedSliders.Add(slider); sourceId = slider.Id;
            }
            var ui = Load(map);
            var baseline = ui.View.Document.DeepClone();
            Guid firstFruit = map.Fruits[0].Id;
            var covered = ui.View.Conversion.Objects.Where(o => o.SourceId == sourceId && o.TimeMs >= 1400 && o.TimeMs <= 2600).ToArray();
            Check(covered.Length > 1 && covered.Any(o => o.Kind == CatchObjectKind.Droplet)
                && covered.Any(o => o.Kind == CatchObjectKind.TinyDroplet), "Fixture lacks multiple nested objects inside the box.");
            ui.ClickText("隐藏曲线");
            ui.Key(tool); ui.ClickMap(1000, 80);
            Box(ui, 1400, 200, 2600, 320, ctrl: true);
            Objects(ui, firstFruit, sourceId); Anchors(ui);
            Box(ui, 1400, 200, 2600, 320);
            Objects(ui, sourceId);
            var nested = covered.First(o => o.Kind == CatchObjectKind.Droplet);
            Click(ui, nested.TimeMs, nested.X, ctrl: true);
            Objects(ui);
            var start = Screen(ui, 1400, 200); var end = Screen(ui, 2600, 320);
            ui.View.PointerDown(start.X, start.Y, 0, false, false); ui.Paint();
            ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
            Objects(ui, sourceId);
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                $"Box selection changed content or placed fruit (tool={tool}, imported={imported}).");
        }
    }

    public static void BatchClipboardDelete()
    {
        var map = ObjectMap();
        var ui = Load(map);
        var sourceTrack = map.Tracks.Single();
        Guid[] sourceIds = [map.Fruits[0].Id, map.Fruits[1].Id, sourceTrack.Id];
        var baseline = ui.View.Document.DeepClone();
        SelectFirstBatch(ui);
        Objects(ui, sourceIds);
        ui.Key('C', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Copying a batch changed the map or save baseline.");
        ui.View.UpdateTransport(4000, 20000, true, false, false, null, "fixture.wav"); ui.Paint();
        ui.Key('V', ctrl: true);
        var pastedIds = ui.View.SelectedObjectIds.ToArray();
        Check(pastedIds.Length == 3 && !pastedIds.Intersect(sourceIds).Any(), "Paste did not select three new parent identities.");
        var newFruits = ui.View.Document.Fruits.Where(f => pastedIds.Contains(f.Id)).OrderBy(f => f.TimeMs).ToArray();
        Check(newFruits.Length == 2, "Batch paste lost a fruit or duplicated a nested child.");
        Near(4000, newFruits[0].TimeMs); Near(5000, newFruits[1].TimeMs);
        var pastedTrack = ui.View.Document.Tracks.Single(t => pastedIds.Contains(t.Id));
        Near(4000, pastedTrack.Nodes[0].TimeMs); Near(7000, pastedTrack.Nodes[^1].TimeMs);
        Check(!pastedTrack.Nodes.Select(n => n.Id).Intersect(sourceTrack.Nodes.Select(n => n.Id)).Any(),
            "Batch paste reused original anchor IDs.");
        var withPaste = ui.View.Document.DeepClone();
        RightMap(ui, 4000, 80); ui.ClickText("剪切");
        Check(baseline.ContentEquals(ui.View.Document), "Right-clicking a selected member cut only one object or the wrong batch.");
        ui.Key('Z', ctrl: true);
        Check(withPaste.ContentEquals(ui.View.Document), "Undo did not restore the entire cut batch and its identities.");

        ui.Key(36); SelectFirstBatch(ui); ui.Key(46);
        Check(!ParentIds(ui.View.Document).Intersect(sourceIds).Any() && pastedIds.All(ParentIds(ui.View.Document).Contains),
            "Batch Delete removed pasted objects or retained part of the selected source batch.");
        ui.Key('Z', ctrl: true);
        Check(withPaste.ContentEquals(ui.View.Document), "Batch Delete did not undo as one edit.");
        Valid(ui);
    }

    public static void AnchorBoxAndEndpointDelete()
    {
        var map = AnchorMap();
        var ui = Load(map);
        var original = ui.View.Document.DeepClone();
        var track = map.Tracks.Single();
        ui.ClickText(track.Name); ui.Key('B');
        Box(ui, 800, 130, 3200, 280);
        Anchors(ui, track.Nodes[0].Id, track.Nodes[1].Id, track.Nodes[2].Id);
        ui.Key(46);
        var retained = ui.View.Document.Tracks.Single();
        Check(retained.Id == track.Id && retained.SpanCount == 2
            && retained.Nodes.Select(n => n.Id).SequenceEqual(track.Nodes.Skip(3).Select(n => n.Id)),
            "Endpoint batch deletion removed or renumbered surviving points.");
        Near(4000, retained.Nodes[0].TimeMs); Near(5000, retained.Nodes[1].TimeMs);
        Valid(ui);
        ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Undo did not recover all deleted endpoints and intermediate points.");
        ui.Key('Y', ctrl: true);
        var twoNodes = ui.View.Document.DeepClone();
        ui.ClickText(track.Name); ui.Key('B');
        ui.ClickMap(4000, 300); Anchors(ui, track.Nodes[3].Id);
        ui.Key(46);
        Check(ui.View.Document.Tracks.Count == 0, "Deleting one of two remaining anchors left an invalid one-point slider.");
        ui.Key('Z', ctrl: true);
        Check(twoNodes.ContentEquals(ui.View.Document), "Undo did not restore a slider removed by its final anchor deletion.");
    }

    public static void SelectionCancellation()
    {
        var map = ObjectMap();
        var ui = Load(map);
        var original = ui.View.Document.DeepClone();
        ui.Key('F'); ui.ClickMap(5500, 100); ui.Key('V');
        Check(ui.View.Document.Fruits.Count == map.Fruits.Count + 1, "Fixture edit was not created.");
        var withEdit = ui.View.Document.DeepClone();
        ui.ClickMap(1000, 80); Guid first = map.Fruits[0].Id;
        Objects(ui, first);
        var start = Screen(ui, 1400, 200); var end = Screen(ui, 2600, 320);
        ui.View.PointerDown(start.X, start.Y, 0, false, false);
        ui.View.PointerMove(end.X, end.Y, false, false); ui.Paint();
        Check(ui.View.WantsCapture, "Marquee did not capture the pointer.");
        ui.Key(27); ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
        Objects(ui, first);
        Check(!ui.View.WantsCapture && withEdit.ContentEquals(ui.View.Document), "Escape retained a partial marquee or edited content.");
        ui.View.PointerDown(start.X, start.Y, 0, false, false);
        ui.View.PointerMove(end.X, end.Y, false, false); ui.Paint();
        ui.View.CancelInteraction(); ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
        Objects(ui, first);
        Check(!ui.View.WantsCapture && withEdit.ContentEquals(ui.View.Document), "Capture cancellation failed to recover the prior selection.");
        ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Selection cancellation added history or undid an unrelated edit.");

        var anchors = AnchorMap();
        ui = Load(anchors);
        var track = anchors.Tracks.Single();
        ui.ClickText(track.Name); ui.Key('B'); ui.ClickMap(5000, 340);
        start = Screen(ui, 800, 130); end = Screen(ui, 3200, 280);
        ui.View.PointerDown(start.X, start.Y, 0, false, false);
        ui.View.PointerMove(end.X, end.Y, false, false); ui.Paint();
        ui.Key(27); ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
        Anchors(ui, track.Nodes[4].Id);
        Check(anchors.ContentEquals(ui.View.Document), "Canceling anchor marquee changed its track.");
    }

    public static void PlaybackBoxTransform()
    {
        var map = ObjectMap();
        var ui = Load(map);
        var baseline = ui.View.Document.DeepClone();
        ui.View.UpdateTransport(2000, 20000, true, true, false, null, "fixture.wav"); ui.Paint();
        var start = Screen(ui, 1800, 30); var end = Screen(ui, 2300, 125);
        ui.View.PointerDown(start.X, start.Y, 0, false, false); ui.Paint();
        ui.View.UpdateTransport(3500, 20000, true, true, false, null, "fixture.wav"); ui.Paint();
        ui.View.PointerMove(end.X, end.Y, false, false); ui.Paint();
        ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
        Objects(ui, map.Fruits[1].Id);
        Near(3500, ui.View.PlayheadMs);
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.WantsCapture,
            "Playback marquee edited objects or retained capture after release.");
    }

    private static MapDocument ObjectMap()
    {
        var map = new MapDocument { DurationMs = 20000, CircleSize = 10, SliderMultiplier = 1, SliderTickRate = 1 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 80 });
        map.Fruits.Add(new Fruit { TimeMs = 2000, X = 80 });
        map.Fruits.Add(new Fruit { TimeMs = 4500, X = 440 });
        var track = new CurveTrack { Name = "Batch slider", Kind = CurveKind.Linear, CompensateTinyDroplets = false };
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 260 }); track.Nodes.Add(new Anchor { TimeMs = 4000, X = 260 });
        map.Tracks.Add(track);
        return map;
    }

    private static MapDocument AnchorMap()
    {
        var map = new MapDocument { DurationMs = 20000, CircleSize = 10 };
        var track = new CurveTrack { Name = "Anchor batch slider", Kind = CurveKind.Linear, SpanCount = 2, CompensateTinyDroplets = false };
        for (int i = 0; i < 5; i++) track.Nodes.Add(new Anchor { TimeMs = 1000 + i * 1000, X = 180 + i * 40 });
        map.Tracks.Add(track);
        return map;
    }

    private static Ui Load(MapDocument map)
    {
        var ui = new Ui(); ui.LoadDocument(map); ui.Paint(); return ui;
    }

    private static (float X, float Y) Screen(Ui ui, double time, double x)
        => (ui.Plot.X + (float)(x / 512) * ui.Plot.Width,
            ui.Plot.Bottom - (float)((time - ui.View.ViewStartMs) * ui.View.PixelsPerMs));

    private static void Click(Ui ui, double time, double x, bool ctrl)
    {
        var p = Screen(ui, time, x);
        ui.View.PointerDown(p.X, p.Y, 0, false, ctrl); ui.Paint();
        ui.View.PointerUp(p.X, p.Y, 0); ui.Paint();
    }

    private static void Box(Ui ui, double time1, double x1, double time2, double x2, bool ctrl = false)
    {
        var a = Screen(ui, time1, x1); var b = Screen(ui, time2, x2);
        ui.View.PointerDown(a.X, a.Y, 0, false, ctrl); ui.Paint();
        ui.View.PointerMove(b.X, b.Y, false, ctrl); ui.Paint();
        ui.View.PointerUp(b.X, b.Y, 0); ui.Paint();
        Check(!ui.View.WantsCapture, "Finishing the marquee retained pointer capture.");
    }

    private static void RightMap(Ui ui, double time, double x)
    {
        var p = Screen(ui, time, x);
        ui.View.PointerDown(p.X, p.Y, 2, false, false); ui.Paint();
        ui.View.PointerUp(p.X, p.Y, 2); ui.Paint();
    }

    private static void SelectFirstBatch(Ui ui)
    {
        ui.ClickMap(1000, 80); Click(ui, 2000, 80, ctrl: true);
        Click(ui, 2000, 260, ctrl: true);
    }

    private static IEnumerable<Guid> ParentIds(MapDocument map) => map.Fruits.Select(f => f.Id)
        .Concat(map.Tracks.Select(t => t.Id)).Concat(map.ImportedSliders.Select(s => s.Id)).Concat(map.BananaShowers.Select(s => s.Id));

    private static void Objects(Ui ui, params Guid[] ids)
        => Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals(ids),
            $"Expected {ids.Length} object selections, got {ui.View.SelectedObjectIds.Count} with different identities.");

    private static void Anchors(Ui ui, params Guid[] ids)
        => Check(ui.View.SelectedAnchorIds.ToHashSet().SetEquals(ids),
            $"Expected {ids.Length} anchor selections, got {ui.View.SelectedAnchorIds.Count} with different identities.");

    private static void Valid(Ui ui) => Check(CurveMath.Validate(ui.View.Document).Count == 0,
        string.Join("; ", CurveMath.Validate(ui.View.Document)));

    private static void Near(double expected, double actual)
        => Check(double.IsFinite(actual) && Math.Abs(expected - actual) < 0.001, $"Expected {expected:R}, got {actual:R}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
