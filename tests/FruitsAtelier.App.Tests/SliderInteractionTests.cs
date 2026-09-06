using FruitsAtelier.Core;

internal static class SliderInteractionTests
{
    public static void DrawGestures()
    {
        var ui = Load(new MapDocument { DurationMs = 12000 });
        ui.Key('B');
        ui.ClickMap(1000, 100); ui.ClickMap(2000, 200); ui.ClickMap(3000, 260);
        ui.Key(13);
        var straight = ui.View.Document.Tracks.Single();
        Check(straight.Nodes.Count == 3 && straight.Nodes.All(n => n.HandleIn == default && n.HandleOut == default),
            "Click-only Slider authoring created handles or lost points.");
        Check(Enumerable.Range(0, 2).All(i => CurveMath.SegmentKind(straight, i) == CurveKind.Linear),
            "Click-only Slider segments are not straight.");
        var saved = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Tracks.Count == 0, "One undo did not remove the complete draft.");
        ui.Key('Y', ctrl: true);
        Check(saved.ContentEquals(ui.View.Document), "Redo changed click-only authoring.");

        ui.Key('B'); ui.ClickMap(4000, 100);
        ui.DownMap(5000, 200); ui.MoveMap(5250, 270); ui.UpMap(5250, 270);
        ui.ClickMap(6000, 300);
        var curved = ui.View.Document.Tracks.Single(t => t.Id != straight.Id);
        Guid middle = curved.Nodes[1].Id;
        Check(curved.Nodes[1].HandleIn != default && curved.Nodes[1].HandleOut != default,
            "Press-drag did not create both active direction handles.");
        Check(curved.Nodes[0].HandleIn == default && curved.Nodes[0].HandleOut == default
            && curved.Nodes[2].HandleIn == default && curved.Nodes[2].HandleOut == default,
            "A simple click acquired an implicit direction handle.");
        ui.DownMap(5000, 200); ui.MoveMap(5125, 210); ui.UpMap(5125, 210);
        Near(5125, ui.Anchor(middle).TimeMs);
        ui.Key(13);
        Check(ui.View.ActiveTool == "Select" && !ui.View.WantsCapture, "Completing Slider retained drawing capture/tool.");
        Check(Enumerable.Range(0, 2).All(i => CurveMath.SegmentKind(curved, i) == CurveKind.Bezier),
            "The dragged point did not curve its adjacent segments.");
        Valid(ui);
        ui.Key('Z', ctrl: true);
        Check(saved.ContentEquals(ui.View.Document), "Dragging a point inside the draft split its undo transaction.");
    }

    public static void ControlSelectionAndDrag()
    {
        var ui = Load(CurveMap());
        var track = ui.View.Document.Tracks.Single();
        Guid nodeId = track.Nodes[1].Id;
        ui.ClickText(track.Name);
        var saved = ui.View.Document.DeepClone();
        Check(!DotAt(ui, 3000, 350, 3, 0xE7EBF2), "Whole-object selection unexpectedly highlighted a point.");
        ui.Key('B');
        ui.ClickMap(3000, 350);
        Check(DotAt(ui, 3000, 350, 3, 0xE7EBF2), "Selected control point has no visible center highlight.");
        Check(saved.ContentEquals(ui.View.Document), "Point selection changed curve content.");
        ui.DownMap(3000, 350); ui.MoveMap(3125, 330); ui.UpMap(3125, 330);
        Near(3125, ui.Anchor(nodeId).TimeMs); Near(330, ui.Anchor(nodeId).X);
        ui.Key('Z', ctrl: true);
        Check(saved.ContentEquals(ui.View.Document), "Point drag did not undo completely.");
        ui.Key('Y', ctrl: true); Near(3125, ui.Anchor(nodeId).TimeMs);
        ui.Key('Z', ctrl: true);

        ui.ClickText(track.Name); ui.Key('B');
        ui.ClickMap(3000, 350);
        Check(ui.Canvas.Circles.Any(c => At(ui, c, 3250, 320) && Math.Abs(c.Radius - 4.5) < 0.001),
            "The unselected outgoing handle was not drawn.");
        ui.DownMap(3250, 320);
        Check(DotAt(ui, 3250, 320, 6, 0xE7EBF2), "The pressed handle is not visibly selected.");
        ui.MoveMap(3375, 340); ui.UpMap(3375, 340);
        Near(375, ui.Anchor(nodeId).HandleOut.TimeMs); Near(-10, ui.Anchor(nodeId).HandleOut.X);
        ui.Key('Z', ctrl: true);
        Check(saved.ContentEquals(ui.View.Document), "Handle drag did not undo completely.");
        ui.ClickMap(3000, 350);
        ui.DownMap(3250, 320); ui.MoveMap(3375, 340); ui.Key(27); ui.UpMap(3375, 340);
        Check(saved.ContentEquals(ui.View.Document) && !ui.View.WantsCapture, "Escape retained a partial handle edit.");
        Valid(ui);
    }

    public static void SelectedAnchorEntryAndContext()
    {
        var ui = Load(CurveMap());
        var track = ui.View.Document.Tracks.Single();
        var node = track.Nodes[1];
        ui.ClickText(track.Name);
        Check(ui.View.ActiveTool == "Select" && ui.View.SelectedAnchorIds.Count == 0,
            "Selecting the FSlider unexpectedly entered point editing.");

        ui.ClickMap(node.TimeMs, node.X);
        Check(ui.View.ActiveTool == "Slider" && ui.View.SelectedAnchorIds.SequenceEqual([node.Id]),
            "A single click on a selected FSlider anchor did not enter anchor editing.");
        var point = Screen(ui, node.TimeMs, node.X);
        Check(ui.Canvas.Lines.Any(line => line.Color == 0xFF7F8D
            && (Math.Abs(line.X1 - point.X) < 0.001 || Math.Abs(line.X2 - point.X) < 0.001)
            && (Math.Abs(line.Y1 - point.Y) < 8.1 || Math.Abs(line.Y2 - point.Y) < 8.1)),
            "The selected anchor does not use the distinct high-contrast colour.");

        double farTime = 2000;
        RightMap(ui, farTime, CurveMath.PositionAtTime(track, farTime));
        Check(ui.Canvas.Texts.Any(text => text.Value == "插入控制点"),
            "Right-clicking the selected curve away from its anchor lost the insertion action.");
        Check(!ui.Canvas.Texts.Any(text => text.Value is "转换为曲线控制点" or "转换为直线控制点"),
            "A distant curve click offered a point-type action for the previously selected anchor.");
    }

    public static void PointContextMenu()
    {
        var ui = Load(CurveMap());
        var original = ui.View.Document.DeepClone();
        var track = ui.View.Document.Tracks.Single();
        Guid trackId = track.Id;
        var oldIds = track.Nodes.Select(n => n.Id).ToHashSet();
        RightMap(ui, 1777, CurveMath.PositionAtTime(track, 1777));
        ui.ClickText("插入控制点");
        track = ui.View.Document.Tracks.Single();
        var inserted = track.Nodes.Single(n => !oldIds.Contains(n.Id));
        Guid nodeId = inserted.Id;
        Check(inserted.HandleIn == default && inserted.HandleOut == default, "Inserted point is not a handle-free corner.");
        Check(track.Nodes.Count == 4 && inserted.TimeMs > 1000 && inserted.TimeMs < 3000,
            "Context insertion did not affect the hit segment.");
        Valid(ui);
        var withCorner = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Undo insertion did not restore neighboring handles.");
        ui.Key('Y', ctrl: true);
        Check(withCorner.ContentEquals(ui.View.Document), "Redo insertion changed its identity or handles.");

        RightNode(ui, nodeId); ui.ClickText("转换为曲线控制点");
        Check(ui.Anchor(nodeId).HandleIn != default || ui.Anchor(nodeId).HandleOut != default,
            "Converting a corner did not expose an editable handle.");
        Valid(ui);
        ui.Key('Z', ctrl: true);
        Check(withCorner.ContentEquals(ui.View.Document), "Undo point conversion changed another control point.");
        ui.Key('Y', ctrl: true);
        RightNode(ui, nodeId); ui.ClickText("转换为直线控制点");
        Check(ui.Anchor(nodeId).HandleIn == default && ui.Anchor(nodeId).HandleOut == default,
            "Converting to a corner retained a handle.");
        var beforeDelete = ui.View.Document.DeepClone();
        RightNode(ui, nodeId); ui.ClickText("删除控制点");
        Check(ui.View.Document.Tracks.Single().Id == trackId && ui.View.Document.Tracks.Single().Nodes.Count == 3,
            "Delete control point removed the whole slider or retained the point.");
        ui.Key('Z', ctrl: true);
        Check(beforeDelete.ContentEquals(ui.View.Document), "Undo point deletion did not restore the exact curve.");
        Valid(ui);
    }

    public static void FruitClipboardAndDelete()
    {
        var map = new MapDocument { DurationMs = 12000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 80 });
        var ui = Load(map);
        Guid originalId = ui.View.Document.Fruits.Single().Id;
        var baseline = ui.View.Document.DeepClone();
        RightMap(ui, 1000, 80); ui.ClickText("复制");
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Copy mutated the map or history baseline.");
        Navigate(ui, 4000); double pasteTime = ui.View.PlayheadMs;
        ui.Key('V', ctrl: true);
        var pasted = ui.View.Document.Fruits.Single(f => f.Id != originalId);
        Guid pastedId = pasted.Id;
        Near(pasteTime, pasted.TimeMs); Near(80, pasted.X);
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Pasting fruit did not undo in one step.");
        ui.Key('Y', ctrl: true);
        RightMap(ui, pasteTime, 80); ui.ClickText("剪切");
        Check(ui.View.Document.Fruits.Single().Id == originalId, "Cut removed the wrong fruit.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Any(f => f.Id == pastedId), "Undo cut did not restore the original pasted identity.");
        Navigate(ui, 5500); pasteTime = ui.View.PlayheadMs;
        ui.Key('V', ctrl: true);
        var third = ui.View.Document.Fruits.Single(f => f.Id != originalId && f.Id != pastedId);
        Near(pasteTime, third.TimeMs); Near(80, third.X);
        RightMap(ui, pasteTime, 80); ui.ClickText("删除");
        Check(ui.View.Document.Fruits.Count == 2 && ui.View.Document.Fruits.All(f => f.Id != third.Id),
            "Context delete removed more than the selected fruit.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 3, "Context deletion is not undoable.");
    }

    public static void SliderClipboardAndDelete()
    {
        var map = CurveMap();
        var source = map.Tracks.Single();
        source.SpanCount = 2;
        foreach (var node in source.Nodes)
        {
            node.TimeMs = 1000 + (node.TimeMs - 1000) / 2;
            node.HandleIn = new(node.HandleIn.TimeMs / 2, node.HandleIn.X);
            node.HandleOut = new(node.HandleOut.TimeMs / 2, node.HandleOut.X);
        }
        source.Nodes[1].OutgoingKind = CurveKind.Linear;
        var ui = Load(map);
        var baseline = ui.View.Document.DeepClone();
        RightMap(ui, 1400, CurveMath.PositionAtTime(source, 1400)); ui.ClickText("复制");
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Slider copy dirtied the model.");
        Navigate(ui, 4000); double time = ui.View.PlayheadMs;
        ui.Key('V', ctrl: true);
        var pasted = ui.View.Document.Tracks.Single(t => t.Id != source.Id);
        Guid pastedId = pasted.Id;
        Check(pasted.SpanCount == 2 && pasted.CompensateTinyDroplets == source.CompensateTinyDroplets,
            "Pasting a slider lost repeat or tiny-droplet policy.");
        for (int i = 0; i < source.Nodes.Count; i++)
        {
            var a = source.Nodes[i]; var b = pasted.Nodes[i];
            Check(a.Id != b.Id && a.HandleIn == b.HandleIn && a.HandleOut == b.HandleOut && a.OutgoingKind == b.OutgoingKind,
                "Pasted control point aliases its source or loses editing intent.");
            Near(time + a.TimeMs - source.Nodes[0].TimeMs, b.TimeMs); Near(a.X, b.X);
        }
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Slider paste did not undo atomically.");
        ui.Key('Y', ctrl: true);
        pasted = ui.View.Document.Tracks.Single(t => t.Id == pastedId);
        RightMap(ui, time + 400, CurveMath.PositionAtTime(pasted, time + 400)); ui.ClickText("剪切");
        Check(ui.View.Document.Tracks.Single().Id == source.Id, "Cut removed only a slider point or the wrong parent.");
        ui.Key('Z', ctrl: true);
        pasted = ui.View.Document.Tracks.Single(t => t.Id == pastedId);
        RightMap(ui, time + 400, CurveMath.PositionAtTime(pasted, time + 400)); ui.ClickText("删除");
        Check(ui.View.Document.Tracks.Single().Id == source.Id, "Delete did not remove the entire selected slider.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Tracks.Single(t => t.Id == pastedId).Nodes.Count == source.Nodes.Count,
            "Undo did not restore the complete deleted slider.");
        Valid(ui);
    }

    public static void HierarchyCompletesDraft()
    {
        var map = new MapDocument { DurationMs = 12000 };
        map.Fruits.Add(new Fruit { TimeMs = 900, X = 480 });
        var ui = Load(map);
        var baseline = ui.View.Document.DeepClone();
        ui.Key('B'); ui.ClickMap(1000, 100); ui.ClickMap(2000, 250);
        ui.ClickText("Fruit 01   900");
        Check(ui.View.ActiveTool == "Select" && !ui.View.WantsCapture && ui.View.Document.Tracks.Single().Nodes.Count == 2,
            "Hierarchy selection did not finish the two-point draft and switch tool.");
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Hierarchy-completed draft did not undo as one edit.");
        ui.Key('Y', ctrl: true);
        var complete = ui.View.Document.DeepClone();
        ui.Key('B'); ui.ClickMap(4000, 200);
        ui.ClickText("Fruit 01   900");
        Check(complete.ContentEquals(ui.View.Document) && ui.View.ActiveTool == "Select" && !ui.View.WantsCapture,
            "Hierarchy selection retained a one-point draft or canceled earlier work.");
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Canceled one-point draft consumed an undo entry.");
    }

    public static void ContextOutsideClick()
    {
        var map = new MapDocument { DurationMs = 12000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 80 });
        var ui = Load(map);
        var baseline = ui.View.Document.DeepClone();
        RightMap(ui, 1000, 80);
        Check(ui.Canvas.Texts.Any(t => t.Value == "复制"), "Context menu did not open.");
        double oldPlayhead = ui.View.PlayheadMs;
        ui.ClickMap(3000, 450);
        Check(!ui.Canvas.Texts.Any(t => t.Value == "复制"), "Outside click left context menu open.");
        Near(oldPlayhead, ui.View.PlayheadMs);
        Check(baseline.ContentEquals(ui.View.Document), "Outside click edited the underlying canvas.");
        ui.ClickMap(3000, 450);
        Near(3000, ui.View.PlayheadMs);
        ui.ClickText("Fruit 01   1000");
        RightMap(ui, 1000, 80);
        Check(ui.Canvas.Texts.Any(t => t.Value == "复制"), "Context menu did not reopen after recentering the fruit.");
        ui.Key(27);
        Check(!ui.Canvas.Texts.Any(t => t.Value == "复制"), "Escape did not close context menu.");
        ui.Key('C', ctrl: true);
        Check(ui.View.CanPasteSelection && baseline.ContentEquals(ui.View.Document),
            "Escape dismissed the object selection as well as its context menu.");
    }

    public static void DeleteDoesNotActivateDormantHandles()
    {
        var map = CurveMap();
        var track = map.Tracks.Single();
        track.Nodes[1].OutgoingKind = CurveKind.Linear;
        track.Nodes[2].HandleIn = new(-900, -180);
        var ui = Load(map);
        var original = ui.View.Document.DeepClone();
        RightNode(ui, track.Nodes[1].Id); ui.ClickText("删除控制点");
        track = ui.View.Document.Tracks.Single();
        Check(track.Nodes.Count == 2, "Deleting an internal point did not merge its adjacent segments.");
        Check(CurveMath.SegmentKind(track, 0) != CurveKind.Bezier || track.Nodes[1].HandleIn == default,
            "Deleting a point activated a previously hidden incoming handle from a Linear segment.");
        Check(track.Nodes[0].HandleOut == original.Tracks.Single().Nodes[0].HandleOut,
            "Deleting a point discarded the surviving visible outgoing handle.");
        Valid(ui);
        ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Undo deletion failed to recover dormant handle data.");
    }

    public static void RepeatInsertion()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode: 2\n[Difficulty]\nSliderMultiplier: 1\nSliderTickRate: 1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,2,200\n");
        map.DurationMs = 12000;
        var ui = Load(map);
        var original = ui.View.Document.DeepClone();
        Guid sourceId = map.ImportedSliders.Single().Id;
        // The return span goes X=300 to X=100 between 2000 and 3000 ms.
        RightMap(ui, 2500, 200); ui.ClickText("插入控制点");
        Check(original.ContentEquals(ui.View.Document) && ui.View.Document.ImportedSliders.Single().Id == sourceId,
            "A Legacy repeat that cannot guarantee TinyDroplet alignment was partially converted.");
        Check(ui.View.StatusMessage.Contains("TinyDroplet"), "The rejected Legacy repeat conversion did not explain its FSlider alignment constraint.");
    }

    public static void LegacyContextConversion()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode: 2\n[Difficulty]\nSliderMultiplier: 1\nSliderTickRate: 1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        map.DurationMs = 12000;
        var ui = Load(map);
        Guid sourceId = map.ImportedSliders.Single().Id;
        RightMap(ui, 1500, 200);
        Check(ui.Canvas.Texts.Any(text => text.Value == "转换为 FSlider"), "Legacy Slider context menu has no explicit FSlider conversion action.");
        var action = ui.Canvas.Texts.Last(text => text.Value == "转换为 FSlider");
        ui.Click(action.X + 4, action.Y + 5);
        var track = ui.View.Document.Tracks.Single();
        Check(track.Id == sourceId && track.CompensateTinyDroplets == true && ui.View.Document.ImportedSliders.Count == 0,
            "Context conversion did not replace the Legacy Slider with one FSlider.");
        var objects = ui.View.Conversion.Objects.Where(item => item.SourceId == sourceId).ToArray();
        Check(objects.Length > 0 && objects.All(item => Math.Abs(item.X - CurveMath.PositionAtTime(track, item.TimeMs))
            <= CatchStreamConverter.AlignmentTolerance), "Converted FSlider objects are not aligned to its target path.");
        Check(ui.Canvas.Texts.Any(text => text.Value == "FSlider"), "The converted object is not labelled as a FSlider.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Single().Id == sourceId && ui.View.Document.Tracks.Count == 0,
            "Undo did not restore the Legacy Slider representation.");
    }

    public static void DraftTailHandleBounds()
    {
        var ui = Load(new MapDocument { DurationMs = 12000 });
        ui.Key('B'); ui.ClickMap(1000, 100);
        ui.DownMap(2000, 200); ui.MoveMap(2125, 300); ui.UpMap(2125, 300);
        Guid tailId = ui.View.Document.Tracks.Single().Nodes[^1].Id;
        var handle = ui.Anchor(tailId).HandleOut;
        Check(handle.X > 99, "The fixture did not create an outgoing draft handle.");
        ui.DownMap(2000, 200); ui.MoveMap(2000, 450); ui.UpMap(2000, 450);
        var tail = ui.Anchor(tailId);
        Check(tail.X > 200 && tail.X + tail.HandleOut.X <= 512,
            "Moving the draft tail allowed its visible outgoing handle past X=512.");
        Check(tail.HandleOut == handle, "Clamping the tail silently changed its direction handle.");
        ui.ClickMap(3000, 400); ui.Key(13);
        Check(ui.View.Document.Tracks.Single().Nodes.Count == 3, "A valid continuation could not finish after moving the tail.");
        Valid(ui);
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Tracks.Count == 0, "Draft-tail editing split the complete slider's undo transaction.");
    }

    private static MapDocument CurveMap()
    {
        var map = new MapDocument { DurationMs = 12000 };
        var track = new CurveTrack { Name = "Interaction curve", CompensateTinyDroplets = false };
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 120, HandleOut = new(250, 50) });
        track.Nodes.Add(new Anchor { TimeMs = 3000, X = 350, HandleIn = new(-300, -80), HandleOut = new(250, -30) });
        track.Nodes.Add(new Anchor { TimeMs = 5000, X = 240, HandleIn = new(-400, 60) });
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

    private static bool At(Ui ui, RecordingCanvas.Dot dot, double time, double x)
    {
        var p = Screen(ui, time, x);
        return Math.Abs(dot.X - p.X) < 0.001 && Math.Abs(dot.Y - p.Y) < 0.001;
    }

    private static bool DotAt(Ui ui, double time, double x, double radius, uint color)
        => ui.Canvas.Circles.Any(c => At(ui, c, time, x) && c.Filled && c.Color == color && Math.Abs(c.Radius - radius) < 0.001);

    private static void RightMap(Ui ui, double time, double x)
    {
        var p = Screen(ui, time, x);
        ui.View.PointerDown(p.X, p.Y, 2, false, false); ui.Paint();
        ui.View.PointerUp(p.X, p.Y, 2); ui.Paint();
    }

    private static void RightNode(Ui ui, Guid id)
    {
        var node = ui.Anchor(id);
        var track = ui.View.Document.Tracks.Single(t => t.Nodes.Any(n => n.Id == id));
        ui.ClickText(track.Name); ui.Key('B');
        RightMap(ui, node.TimeMs, node.X);
    }

    private static void Navigate(Ui ui, double time)
    {
        var markers = ui.Canvas.Lines.Where(l => l.Color == 0x2B3442 && l.X1 == l.X2).ToArray();
        float left = markers.Min(l => l.X1), right = markers.Max(l => l.X1);
        ui.Click(left + (float)(time / ui.View.TimelineDurationMs) * (right - left), (markers[0].Y1 + markers[0].Y2) / 2);
    }

    private static void Valid(Ui ui) => Check(CurveMath.Validate(ui.View.Document).Count == 0,
        string.Join("; ", CurveMath.Validate(ui.View.Document)));

    private static void Near(double expected, double actual)
        => Check(double.IsFinite(actual) && Math.Abs(expected - actual) < 0.001, $"Expected {expected:R}, got {actual:R}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
