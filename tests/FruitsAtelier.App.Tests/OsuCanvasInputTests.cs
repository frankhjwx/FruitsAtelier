using FruitsAtelier.Core;

internal static class OsuCanvasInputTests
{
    public static void Run()
    {
        OsuStableInputTests.Run();
        OsuSliderTimingTests.Run();
        SliderCanvasLockTests.Run();
        ToggleAndHeldKeys();
        NativeCircleAndLegacySlider();
        NativeSelectionAndCancellation();
        StackedMarqueeUsesVisibleCenters();
        NativePlacementPreservesGeometry();
        NativeRepeatSamples();
        SubMillisecondSliderLifetime();
        ReadOnlyBoundaries();
        DistanceSliderAndNavigation();
        OsuDistanceSpacingCacheTests.Run();
        ImportedCurveVisibilityTests.Run();
    }

    private static void ToggleAndHeldKeys()
    {
        var ui = NewUi(); var before = ui.View.Document.DeepClone();
        ui.Key(9); ui.Key(9);
        Check(ui.View.IsOsuCanvas, "Repeated Tab toggled the canvas twice.");
        Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Canvas switch changed content.");
        ui.View.KeyUp(9); ui.Key(20); ui.Key(20);
        Check(ui.View.IsOsuCanvas, "Caps Lock toggled the canvas.");
        ui.View.KeyUp(20);
        ui.Key(9); ui.Key(9);
        Check(!ui.View.IsOsuCanvas, "A released Tab did not toggle once on its next press.");
        ui.View.KeyUp(9);
        ui.Key(9, ctrl: true);
        Check(!ui.View.IsOsuCanvas, "Ctrl+Tab toggled the canvas.");
    }

    private static void SubMillisecondSliderLifetime()
    {
        foreach (var (start, beat, length) in new[] { (360405, "0.865377304725", "3174014.36923"), (377738, "0.280552367018", "9790400.37765") })
        {
            var map = OsuBeatmapReader.Read($"osu file format v14\n[General]\nMode:0\n[Difficulty]\nSliderMultiplier:1.6\n[TimingPoints]\n0,500,4,1,0,100,1,0\n{start},{beat},4,1,0,100,1,0\n{start},NaN,4,1,0,100,0,0\n{start + 1},500,4,1,0,100,1,0\n[HitObjects]\n256,192,{start},2,0,L|300:192,1,{length}\n");
            var ui = NewUi(); ui.LoadDocument(map); ui.View.ToggleOsuCanvas(); ui.Paint();
            double end = start + 17167;
            var head = ui.View.OsuCanvasScreen(256, 192);
            ui.View.UpdateTransport(end - 1, end + 1000, true, false, false, null, "fixture.wav"); ui.Paint();
            Check(ui.View.OsuCanvasHit(head.X, head.Y) == map.ImportedSliders.Single().Id,
                "A sub-millisecond slider disappeared before its stable end time.");
            ui.View.UpdateTransport(end + 701, end + 1000, true, false, false, null, "fixture.wav"); ui.Paint();
            Check(ui.View.OsuCanvasHit(head.X, head.Y) is null,
                "The osu canvas retained a slider after its stable end and fade period.");
            Check(!ui.View.IsDirty, "Evaluating slider lifetime changed authored timing.");
        }
    }

    private static void NativeCircleAndLegacySlider()
    {
        var ui = NewUi(); ui.View.ToggleOsuCanvas(); ui.Paint();
        ui.Key('2'); Click(ui, 128, 256);
        var fruit = ui.View.Document.Fruits.Single();
        Check(fruit.X == 128 && fruit.Y == 256, "Native circle lost its XY coordinates.");
        var exported = OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack.Fruits.Single();
        Check(exported.Y == 256, "Native circle geometric Y did not export.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits.Count == 0, "Native circle undo failed.");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits.Single().Y == 256, "Native circle redo lost Y.");
        ui.Key('3'); Click(ui, 64, 64); Click(ui, 192, 64); Click(ui, 256, 192);
        Check(ui.View.Document.ImportedSliders.Count == 0, "An incomplete native slider entered the document.");
        ui.Key(13); ui.Paint();
        var slider = ui.View.Document.ImportedSliders.Single();
        Check(ui.View.Document.Tracks.Count == 0 && slider.ControlPoints.Count == 3, "Native slider created an FSlider or lost controls.");
        _ = OsuBeatmapWriter.Serialize(ui.View.Document);
        ChangeRepeat(ui, true);
        Check(ui.View.Document.ImportedSliders.Single().SpanCount == 2, "Native repeat edit failed.");
        _ = OsuBeatmapWriter.Serialize(ui.View.Document);
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ImportedSliders.Single().SpanCount == 1, "Repeat undo failed.");
        ui.Key('1');
        var head = ui.View.OsuCanvasScreen(64, 64); var end = ui.View.OsuCanvasScreen(96, 96);
        ui.View.PointerDown(head.X, head.Y, 0, false, false);
        ui.View.PointerMove(end.X, end.Y, false, false);
        ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
        slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.X == 96 && slider.Y == 96 && slider.ControlPoints[0] == new SliderPathPoint(96, 96), "Native head drag did not update raw controls.");
        _ = OsuBeatmapWriter.Serialize(ui.View.Document);
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Single().X == 64, "Native drag did not form one undo step.");
    }

    private static void ReadOnlyBoundaries()
    {
        var ui = NewUi();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,64,1000,2,0,L|192:64,1,128\n");
        ui.LoadDocument(map); ui.Key('1'); ui.ClickMap(1000, 64); ui.Paint();
        var before = ui.View.Document.DeepClone();
        ui.Key(46); ui.Key('H', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "Catch canvas mutated a read-only Legacy Slider.");
        Check(ui.View.CopySelection(), "Read-only Legacy Slider could not be copied.");
        Check(!ui.View.CutSelection() && ui.View.Document.ContentEquals(before), "Cut removed a read-only Legacy Slider.");
        var fslider = new CurveTrack(); fslider.Nodes.Add(new() { TimeMs = 1000, X = 100 }); fslider.Nodes.Add(new() { TimeMs = 1500, X = 200 });
        map = new MapDocument(); map.Tracks.Add(fslider); ui.LoadDocument(map); ui.View.ToggleOsuCanvas(); ui.Paint();
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, "fixture.wav"); ui.Paint();
        ui.Key('1'); Click(ui, 100, 192); before = ui.View.Document.DeepClone();
        Check(ui.View.CopySelection(), "Read-only FSlider was not selected on the native canvas.");
        ui.Key(46); ui.Key('H', ctrl: true);
        Check(ui.View.Document.ContentEquals(before) && !ui.View.CutSelection(), "osu canvas mutated a read-only FSlider.");
    }

    private static void NativeSelectionAndCancellation()
    {
        var ui = NewUi();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,64,1000,2,0,L|256:64,1,128\n");
        ui.LoadDocument(map); ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, "fixture.wav"); ui.Paint();
        var before = ui.View.Document.DeepClone();
        Click(ui, 64, 64); Click(ui, 64, 64);
        Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Selecting a native control changed the slider's declared length or original text.");
        var p = ui.View.OsuCanvasScreen(64, 64); var q = ui.View.OsuCanvasScreen(96, 96);
        ui.View.PointerDown(p.X, p.Y, 0, false, false); ui.View.PointerMove(q.X, q.Y, false, false);
        ui.Key(27); ui.View.PointerUp(q.X, q.Y, 0); ui.Paint();
        Check(ui.View.Document.ContentEquals(before), "Cancelling native control drag did not restore original geometry.");
        ui.View.PointerDown(p.X, p.Y, 0, false, false); ui.View.PointerMove(q.X, q.Y, false, false);
        ui.View.PointerMove(p.X, p.Y, false, false); ui.View.PointerUp(p.X, p.Y, 0); ui.Paint();
        Check(ui.View.Document.ContentEquals(before), "Returning native drag to its origin changed declared length.");
    }

    private static void DistanceSliderAndNavigation()
    {
        var ui = NewUi(); ui.View.ToggleOsuCanvas(); ui.Paint();
        var track = ui.View.OsuDistanceSpacingTrackBounds;
        float y = track.Y + 4, left = track.X, right = track.Right;
        ui.View.PointerDown(left, y, 0, false, false);
        ui.View.PointerMove(right, y, false, false); ui.View.PointerUp(right, y, 0); ui.Paint();
        Check(Math.Abs(ui.View.Document.OsuDistanceSpacing - 3.2) < .0001, "Native DS slider did not reach 3.2.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.OsuDistanceSpacing == 1, "Native DS gesture did not undo in one step.");
        float fineX = track.X + (float)((1.23 - .1) / 3.1) * track.Width;
        ui.View.PointerDown(fineX, y, 0, true, false);
        ui.View.PointerMove(fineX, y, true, false); ui.View.PointerUp(fineX, y, 0, true); ui.Paint();
        Check(Math.Abs(ui.View.Document.OsuDistanceSpacing - 1.23) < .0001, "Shift did not retain the native DS fine step on release.");
        ui.Key('Z', ctrl: true);
        var before = ui.View.Document.DeepClone(); var p = ui.View.OsuCanvasScreen(256, 192);
        ui.View.Wheel(p.X, p.Y, 120, false, false, true);
        ui.View.PointerDown(p.X, p.Y, 1, false, false);
        ui.View.PointerMove(p.X + 40, p.Y + 20, false, false); ui.View.PointerUp(p.X + 40, p.Y + 20, 1);
        Check(ui.View.Document.ContentEquals(before), "Native zoom or pan edited map content.");
    }

    private static void StackedMarqueeUsesVisibleCenters()
    {
        var ui = NewUi();
        var map = new MapDocument { DurationMs = 10000, ApproachRate = 5, IsDemo = false };
        for (int i = 0; i < 12; i++) map.Fruits.Add(new() { TimeMs = 1000 + i * 20, X = 80, Y = 80 });
        ui.LoadDocument(map); ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(900, 10000, true, false, false, null, "fixture.wav"); ui.Paint(); ui.Key('1');
        var before = ui.View.Document.DeepClone();
        double shift = -11 * (54.4 - 4.48 * map.CircleSize) / 10;
        var start = ui.View.OsuCanvasScreen(0, 0); var end = ui.View.OsuCanvasScreen(80 + shift + .5, 80 + shift + .5);
        ui.View.PointerDown(start.X, start.Y, 0, false, false);
        ui.View.PointerMove(end.X, end.Y, false, false);
        ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
        var field = typeof(FruitsAtelier.App.Editor.EditorView).GetField("objectSelection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var selected = (HashSet<Guid>)field.GetValue(ui.View)!;
        Check(selected.SetEquals([map.Fruits[0].Id]), "Marquee did not select the visibly shifted circle center independently of unstacked centers.");
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Stacked marquee selection edited beatmap content.");
    }

    private static void NativePlacementPreservesGeometry()
    {
        var ui = NewUi();
        ui.LoadDocument(new MapDocument { DurationMs = 5000, DerandomizeFSliderDroplets = true });
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, "fixture.wav");
        ui.View.ToggleOsuCanvas(); ui.Paint();
        var before = ui.View.Document.DeepClone();
        ui.Key('3'); Click(ui, 64, 64); Click(ui, 192, 64); ui.Key(13);
        WaitLegacyRefresh(ui);
        Check(ui.View.Document.ImportedSliders.Count == 1, "Native slider placement under global settings failed: " + ui.View.StatusMessage);
        var slider = ui.View.Document.ImportedSliders.Single();
        Check(slider.PreserveNativeGeometry && slider.RandomizationTrack is null && slider.RandomizationSource is null
            && slider.PathType == 'B' && slider.ControlPoints.SequenceEqual(new SliderPathPoint[] { new(64, 64), new(192, 64) }),
            "Native Legacy Slider must retain its authored controls under global droplet settings.");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
        Check(restored.ContentEquals(ui.View.Document) && restored.ImportedSliders.Single().PreserveNativeGeometry,
            "Native Legacy geometry preference was not saved.");
        restored.RandomizeDropletStrength = 8;
        var refresh = SliderRepresentationEditing.RefreshLegacyDroplets(restored);
        Check(refresh.Success && restored.ImportedSliders.Single().ControlPoints.SequenceEqual(slider.ControlPoints)
            && restored.ImportedSliders.Single().RandomizationTrack is null,
            "Later global droplet edits reshaped native Legacy geometry.");
        var exported = OsuBeatmapWriter.Serialize(ui.View.Document);
        Check(exported.ObjectSequenceMatches, "Native global-droplet placement did not validate its exported sequence.");
        var after = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Native Legacy creation was not one undo step.");
        ui.Key('Y', ctrl: true); Check(after.ContentEquals(ui.View.Document), "Native global-droplet placement changed on redo.");
    }

    private static Ui NewUi()
    {
        var ui = new Ui(false); ui.View.LibrarySettings.LazerEditing = false;
        ui.LoadDocument(new MapDocument { DurationMs = 5000 });
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, "fixture.wav"); ui.Paint();
        if (ui.View.DistanceSnapEnabled) ui.Key('Y');
        return ui;
    }

    private static void ChangeRepeat(Ui ui, bool add)
    {
        ui.Key('1');
        var slider = ui.View.Document.ImportedSliders.Single();
        var point = ui.View.OsuCanvasScreen(slider.X, slider.Y);
        ui.Click(point.X, point.Y); ui.Key(93);
        ui.ClickText(FruitsAtelier.Localization.Strings.Get(add ? "editor.command.addReverse" : "editor.command.removeReverse"));
    }

    private static void NativeRepeatSamples()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,64,1000,2,0,L|192:64,2,128,2|4|8,1:2|2:3|3:1,1:2:3:70:slide.wav\n");
        var source = map.ImportedSliders.Single();
        var tail = CatchStreamConverter.Convert(map).Objects.Last(o => o.Kind == CatchObjectKind.Fruit);
        map.HitsoundOverrides.Add(new(source.Id, tail.EventIndex, new(Additions: 8), EdgeIndex: 2));
        var ui = NewUi(); ui.LoadDocument(map); ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, "fixture.wav"); ui.Paint();
        Click(ui, 64, 64); ChangeRepeat(ui, true);
        var current = ui.View.Document.ImportedSliders.Single();
        var fields = current.OriginalLine!.Split(',');
        Check(fields[8] == "2|4|0|8" && fields[9] == "1:2|2:3|0:0|3:1", "Adding a native repeat did not preserve head, surviving reverse and final-tail samples.");
        var changedTail = CatchStreamConverter.Convert(ui.View.Document).Objects.Last(o => o.Kind == CatchObjectKind.Fruit);
        var sample = ui.View.Document.HitsoundOverrides.Single();
        Check(sample.EdgeIndex == 3 && sample.EventIndex == changedTail.EventIndex, "Native repeat did not remap the final-tail override to its new event.");
        Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Native repeat samples did not survive export validation.");
        ChangeRepeat(ui, false); ChangeRepeat(ui, false);
        fields = ui.View.Document.ImportedSliders.Single().OriginalLine!.Split(',');
        Check(fields[8] == "2|8" && fields[9] == "1:2|3:1", "Removing native repeats lost the head or final-tail samples.");
        sample = ui.View.Document.HitsoundOverrides.Single();
        Check(sample.EdgeIndex == 1 && sample.EventIndex == CatchStreamConverter.Convert(ui.View.Document).Objects.Last(o => o.Kind == CatchObjectKind.Fruit).EventIndex,
            "Removing native repeats left the tail override on a deleted edge.");
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(map), "Native repeat sample edits were not reversible.");
    }

    private static void Click(Ui ui, double x, double y)
    { var p = ui.View.OsuCanvasScreen(x, y); ui.Click(p.X, p.Y); }
    private static void WaitLegacyRefresh(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ui.View.LegacyDropletRefreshBusy && DateTime.UtcNow < deadline) { Thread.Sleep(1); ui.Paint(); }
        Check(!ui.View.LegacyDropletRefreshBusy, "Native Legacy droplet validation did not complete.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
