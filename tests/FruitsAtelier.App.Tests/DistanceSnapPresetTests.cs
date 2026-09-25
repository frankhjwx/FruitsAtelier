using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class DistanceSnapPresetTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void BaseDistance()
    {
        Check(new MapDocument().SliderMultiplier == 1.92 && new MapDocument().DistancePerBeat == 192,
            "New maps should start at 192 px per beat with a 1.92 slider multiplier.");
        var imported = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n");
        var ui = new Ui(false);
        ui.LoadDocument(imported);
        Check(ui.Canvas.Texts.Any(t => t.Value.Contains("DPB 140px")), "Imported base distance should reflect its stored multiplier.");
        ui.Key('G'); ui.Key('G'); ui.Key('T');
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        var track = ui.View.DistanceSnapBaseTrackBounds;
        double standBoundary = CatchSize.CatchWidth(imported.CircleSize) / 2 * ui.View.SnapDivisor;
        float X(double px) => track.X + (float)(px / standBoundary) * track.Width / 4;
        float x = X(77);
        ui.View.PointerDown(x, track.Y + 12, 0, false, false);
        ui.View.PointerMove(x, track.Y + 12, false, false);
        ui.View.PointerUp(x, track.Y + 12, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "80") && imported.SliderMultiplier == 1.4,
            "Grid Snap drag should use the selected 16 px step without editing the map before Apply.");
        float expectedBoundaryX = track.X + track.Width / 4 - 2;
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xC0C0C0 && f.Bounds.Y == track.Y + 3
            && Math.Abs(f.Bounds.Right - expectedBoundaryX) < 1),
            "The base DPB bar should use the same four equal movement regions as the preset bar.");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xCE7683 && f.Bounds.Y == track.Y + 3
            && Math.Abs(f.Bounds.X - (track.X + track.Width * .75f)) < 1)
            && ui.Canvas.Lines.Any(l => Math.Abs(l.X1 - X(80)) < 1 && l.Y1 == track.Y - 4),
            "The HDash region and 1.0x marker should use the preset bar's distance scale.");
        var level = ui.Canvas.Texts.First(t => t.Value == "32 px" && t.Y > track.Y + 110 && t.Y < track.Y + 145);
        ui.Click(level.X + 4, level.Y + 5); ui.Paint();
        uint enabledLevelColor = ui.Canvas.Texts.First(t => t.Value == Strings.Get("ui.gridLevel", Strings.Get("ui.grid32"))).Color;
        ui.View.PointerDown(x, track.Y + 12, 0, false, false);
        ui.View.PointerUp(x, track.Y + 12, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "64"), "Changing Grid Level in the dialog did not update DPB dragging.");
        var toggle = ui.Canvas.Texts.First(t => t.Value == Strings.Get("ui.gridSnap") && t.Y > track.Y + 70 && t.Y < track.Y + 110);
        ui.Click(toggle.X + 4, toggle.Y + 5); ui.Paint();
        Check(ui.View.EditorGridSettings == (true, 16), "Dialog Grid Snap settings changed the editor grid.");
        var disabledLevel = ui.Canvas.Texts.First(t => t.Value == Strings.Get("ui.gridLevel", Strings.Get("ui.grid32")));
        Check(disabledLevel.Color != enabledLevelColor, "Grid Level label should dim when dialog Grid Snap is off.");
        var disabledChoice = ui.Canvas.Texts.First(t => t.Value == "4 px" && t.Y > track.Y + 110 && t.Y < track.Y + 145);
        Check(disabledChoice.Color == 0x5B6777, "Grid Level choices should dim when dialog Grid Snap is off.");
        ui.Click(disabledChoice.X + 4, disabledChoice.Y + 5); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ui.gridLevel", Strings.Get("ui.grid32"))),
            "A disabled Grid Level choice changed the dialog setting.");
        ui.View.PointerDown(x, track.Y + 12, 0, false, false);
        ui.View.PointerUp(x, track.Y + 12, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "77"), "Disabling Grid Snap in the dialog did not update DPB dragging.");
        var field = ui.View.DistanceSnapBaseFieldBounds;
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText("0"); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(ui.View.DistanceSnapDialogVisible && ui.View.Document.SliderMultiplier == 1.4,
            "Out-of-range manual DPB must not apply.");
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText("73.25"); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(!ui.View.DistanceSnapDialogVisible && Math.Abs(ui.View.Document.DistancePerBeat - 73.25) < .000001
            && ui.View.Document.SliderMultiplier == 1.4,
            "Manual DPB should change the editor base distance without changing the imported slider multiplier.");
        Check(Math.Abs(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)).DistancePerBeat - 73.25) < .000001,
            "Project save lost DPB.");
        Check(Math.Abs(OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack.SliderMultiplier - 1.4) < .000001,
            "osu export changed the imported slider multiplier.");
        ui.Key('Z', ctrl: true);
        Check(Math.Abs(ui.View.Document.DistancePerBeat - 140) < .000001, "DPB Apply did not undo.");
        ui.Key('Y', ctrl: true);
        Check(Math.Abs(ui.View.Document.DistancePerBeat - 73.25) < .000001, "DPB Apply did not redo.");
    }

    public static void ExistingSliderPreservation()
    {
        const string source = "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n0,-11.1111111111111,4,1,0,100,0,0\n[HitObjects]\n100,192,1000,2,0,L|240:192,1,140\n";
        var imported = OsuBeatmapReader.Read(source);
        var before = CatchStreamConverter.Convert(imported);
        Check(before.Success && imported.DistancePerBeat == 140, "Imported slider or its original DPB did not load.");
        var ui = new Ui(false);
        ui.LoadDocument(imported);
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        var field = ui.View.DistanceSnapBaseFieldBounds;
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText("32"); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(ui.View.Document.DistancePerBeat == 32 && ui.View.Document.SliderMultiplier == 1.4,
            "Editing DPB changed the imported slider multiplier.");
        var after = CatchStreamConverter.Convert(ui.View.Document);
        Check(after.Success && before.Sliders.Single().DurationMs == after.Sliders.Single().DurationMs
            && before.Objects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(after.Objects.Select(o => (o.Kind, o.TimeMs, o.X))),
            "Editing DPB changed the imported slider's playable objects.");
        Check(Math.Abs(DistanceSnap.BaseVelocity(ui.View.Document, 1000) - .064) < 1e-9,
            "Distance Snap did not use the independent DPB.");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
        Check(restored.DistancePerBeat == 32 && restored.SliderMultiplier == 1.4,
            "Project persistence mixed editor DPB with the slider multiplier.");
        var exported = OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack;
        Check(exported.SliderMultiplier == 1.4
            && CatchStreamConverter.Convert(exported).Objects.Select(o => (o.Kind, o.TimeMs, o.X))
                .SequenceEqual(before.Objects.Select(o => (o.Kind, o.TimeMs, o.X))),
            "osu export changed an existing slider after editing DPB.");
        Check(ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{\"SliderMultiplier\":1.4}}")
            .DistancePerBeat == 140, "Older projects should derive DPB from their stored slider multiplier.");
        var authored = new MapDocument { IsDemo = false };
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 100 });
        track.Nodes.Add(new Anchor { TimeMs = 2000, X = 300 });
        authored.Tracks.Add(track);
        var authoredBefore = CatchStreamConverter.Convert(authored);
        authored.DistancePerBeatOverride = 32;
        var authoredAfter = CatchStreamConverter.Convert(authored);
        Check(authoredBefore.Success && authoredAfter.Success
            && authoredBefore.Sliders.Single().Velocity == authoredAfter.Sliders.Single().Velocity
            && authoredBefore.Objects.Select(o => (o.Kind, o.TimeMs, o.X))
                .SequenceEqual(authoredAfter.Objects.Select(o => (o.Kind, o.TimeMs, o.X))),
            "Editing DPB changed an authored FSlider.");
        var precise = new Ui(false);
        precise.LoadDocument(OsuBeatmapReader.Read(source.Replace("SliderMultiplier:1.4", "SliderMultiplier:1.23456789")));
        precise.View.OpenDistanceSnapDialog(); precise.Paint();
        precise.ClickText(Strings.Get("ds.add"));
        precise.ClickText(Strings.Get("library.apply"));
        Check(precise.View.Document.DistancePerBeatOverride is null
            && Math.Abs(precise.View.Document.DistancePerBeat - 123.456789) < 1e-9,
            "Editing only DS presets rounded or overwrote the imported DPB baseline.");
    }

    public static void DynamicBaseRange()
    {
        var ui = new Ui(false);
        ui.LoadDocument(new MapDocument { IsDemo = false });
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        Check(ui.View.SnapDivisor == 4, "Unexpected default reference Snap.");
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.baseRange", "2048")),
            "The current DPB range should be visible beside the input.");
        var field = ui.View.DistanceSnapBaseFieldBounds;
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText("2048"); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(!ui.View.DistanceSnapDialogVisible && ui.View.Document.DistancePerBeat == 2048,
            "The DPB field should accept the reference bar's upper limit beyond 512 px.");
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText("2049"); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(ui.View.DistanceSnapDialogVisible && ui.View.Document.DistancePerBeat == 2048,
            "The DPB field accepted a value beyond the reference bar.");
        var track = ui.View.DistanceSnapBaseTrackBounds;
        ui.View.PointerDown(track.X, track.Y + 12, 0, false, false);
        ui.View.PointerUp(track.X, track.Y + 12, 0); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(ui.View.Document.DistancePerBeat == 32, "The DPB bar should stop at its 32 px minimum.");
        var slow = new Ui(false);
        slow.LoadDocument(new MapDocument { IsDemo = false, BeatLengthMs = 4000 });
        slow.View.OpenDistanceSnapDialog(); slow.Paint();
        var slowTrack = slow.View.DistanceSnapBaseTrackBounds;
        slow.View.PointerDown(slowTrack.Right - 1, slowTrack.Y + 12, 0, false, false);
        slow.View.PointerUp(slowTrack.Right - 1, slowTrack.Y + 12, 0); slow.Paint();
        Check(slow.Canvas.Lines.Any(l => Math.Abs(l.X1 - slowTrack.Right) < 2 && l.Y1 == slowTrack.Y - 4),
            "The upper bar should retain a usable HDash region when Dash exceeds 512 px.");
        slow.ClickText(Strings.Get("library.apply"));
        Check(slow.View.Document.DistancePerBeat > 512,
            "The upper bar should accept DPB values beyond a playfield width on slow maps.");
    }

    public static void PresetDistancePreservation()
    {
        var map = new MapDocument { IsDemo = false };
        map.DistanceSnapRatios.AddRange([.75, 1.5]);
        var ui = new Ui(false);
        ui.LoadDocument(map);
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        var track = ui.View.DistanceSnapBaseTrackBounds;
        ui.View.PointerDown(track.Right - 5, track.Y + 12, 0, false, false);
        ui.View.PointerMove(track.Right - 2, track.Y + 12, false, false);
        ui.Key(27); ui.View.PointerUp(track.Right - 2, track.Y + 12, 0); ui.Paint();
        var field = ui.View.DistanceSnapBaseFieldBounds;
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText("96"); ui.Paint();
        ui.ClickText(Strings.Get("library.apply"));
        Check(ui.View.Document.DistancePerBeat == 96
            && ui.View.Document.DistanceSnapRatios.SequenceEqual([1.5, 3.0])
            && ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)).DistanceSnapRatios.SequenceEqual([1.5, 3.0]),
            "Changing DPB should store rescaled preset multipliers and preserve their pixel distances.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.DistancePerBeat == 192
            && ui.View.Document.DistanceSnapRatios.SequenceEqual([.75, 1.5]),
            "Undo should restore DPB and its preset multipliers together.");
    }

    public static void Snapping()
    {
        var reference = new DistanceSnap.Reference(Guid.NewGuid(), new(1000, 256), new(1000, 256), .28, 0);
        double[] presets = [0, 1, 2, 4, 7.25];
        foreach (var (mouse, expected) in new[] { (260d, 256d), (290d, 291d), (330d, 326d), (399d, 396d), (220d, 221d), (115d, 116d), (2d, 2.25d) })
        {
            var point = DistanceSnap.SnapMultiple(new(1125, mouse), reference, presets, out bool outside);
            Check(Math.Abs(point.X - expected) < .001 && !outside, "Nearest DS or implicit zero was not selected.");
        }
        var edge = reference with { Start = new(1000, 500), End = new(1000, 500) };
        Check(DistanceSnap.SnapMultiple(new(1125, 480), edge, presets, out _).X == 465, "Invalid right candidates hid a valid left candidate.");
        Check(DistanceSnap.SnapMultiple(new(5000, 200), reference, presets, out bool invalid).X == 256 && !invalid,
            "Zero DS did not remain available when every nonzero distance exceeded the field.");
        Check(DistanceSnap.SnapMultiple(new(1125, 260), reference, [], out _).X == 256, "Empty list omitted zero DS.");
        Check(DistanceSnap.SnapMultiple(new(1125, 290), reference, [], out _).X == 256, "Empty list included a hidden nonzero spacing.");
        Check(DistanceSnap.SnapMultiple(new(1000, 290), reference, presets, out _).X == 290, "Simultaneous notes were snapped.");
        var map = new MapDocument(); map.DistanceSnapRatios.AddRange(presets);
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(map));
        Check(restored.DistanceSnapRatios.SequenceEqual(presets), "Map DS values did not survive project persistence.");
        var clone = map.DeepClone(); clone.DistanceSnapRatios[0] = .25;
        Check(map.DistanceSnapRatios[0] == 0 && !map.ContentEquals(clone), "Map snapshots share DS values or ignore changes.");
        clone.DistanceSnapRatios[0] = -.1;
        try { ProjectSerializer.Serialize(clone); throw new Exception("Negative DS was accepted."); }
        catch (InvalidDataException) { }
        Check(ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{}}").DistanceSnapRatios.Count == 0, "Old maps did not default to an empty list.");
        map.DistanceSnapRatios.AddRange([1, 1, 1, 1, 1]);
        try { ProjectSerializer.Serialize(map); throw new Exception("More than eight DS values were saved."); }
        catch (InvalidDataException) { }

    }

    public static void SliderEvents()
    {
        var map = new MapDocument { DurationMs = 5000, SliderTickRate = 4 };
        map.DistanceSnapRatios.Add(1);
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 200 });
        track.Nodes.Add(new Anchor { TimeMs = 1500, X = 392 });
        map.Tracks.Add(track);
        var objects = new[]
        {
            new ConvertedCatchObject(track.Id, 0, CatchObjectKind.Fruit, 1000, 200, 200, 200, 0),
            new ConvertedCatchObject(track.Id, 1, CatchObjectKind.Droplet, 1250, 296, 296, 296, 0),
            new ConvertedCatchObject(track.Id, 2, CatchObjectKind.Fruit, 1500, 392, 392, 392, 0)
        };
        Check(SliderDistanceSnap.Excesses(map, objects, track.Id).Count == 0
            && SliderDistanceSnap.StrictErrors(map, track, objects).Count == 0,
            "Straight slider events at 1 DS were rejected.");
        var bulged = objects.ToArray();
        bulged[1] = bulged[1] with { X = 500 };
        var excess = SliderDistanceSnap.Excesses(map, bulged, track.Id);
        Check(excess.Count == 2 && !SliderDistanceSnap.Allows(new Dictionary<(int, int), double>(), excess),
            "An internal droplet exceeding DS was not detected.");
        Check(SliderDistanceSnap.Allows(excess, excess), "An existing violation blocked unchanged geometry.");
        var ui = new Ui(); ui.LoadDocument(WithTrackRemoved());
        ui.View.SetSliderEditingMode(FruitsAtelier.App.Editor.SliderEditingMode.PenTool);
        ui.Key('B'); ui.Key('Y');
        ui.ClickMap(1000, 200);
        ui.ClickMap(1500, 450, ctrl: true);
        var drawn = ui.View.Document.Tracks.Single();
        Check(Math.Abs(drawn.Nodes[^1].X - 392) < 1e-5, "Straight slider endpoint did not snap to 1 DS.");
        var generated = CatchStreamConverter.Convert(ui.View.Document);
        Check(generated.Success && generated.Objects.Count(item => item.SourceId == drawn.Id
            && item.Kind == CatchObjectKind.Droplet) > 0
            && SliderDistanceSnap.Excesses(ui.View.Document, generated.Objects, drawn.Id).Count == 0
            && SliderDistanceSnap.StrictErrors(ui.View.Document, drawn, generated.Objects).Count == 0,
            "Generated straight-slider droplets did not follow the selected DS.");
        ui.Key(13);
        ui.DownMap(1500, 392); ui.MoveMap(1625, 450); ui.UpMap(1625, 450);
        Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 440) < 1e-5,
            "Dragging a completed pen slider tail did not snap to DS.");

        var legacy = new Ui(); legacy.LoadDocument(ui.View.Document.DeepClone());
        legacy.View.SetSliderEditingMode(FruitsAtelier.App.Editor.SliderEditingMode.OsuLegacy);
        legacy.Key('Y'); legacy.SelectTrack(drawn.Id); legacy.Key('B');
        legacy.DownMap(1625, 440); legacy.MoveMap(1750, 480); legacy.UpMap(1750, 480);
        Check(Math.Abs(legacy.View.Document.Tracks.Single().Nodes[^1].X - 488) < 1e-5,
            $"Dragging a completed legacy slider tail did not snap to DS: {legacy.View.Document.Tracks.Single().Nodes[^1].TimeMs}, {legacy.View.Document.Tracks.Single().Nodes[^1].X}, {legacy.View.StatusMessage}.");

        MapDocument WithTrackRemoved()
        {
            var copy = map.DeepClone(); copy.Tracks.Clear(); return copy;
        }
    }

    public static void Dialog()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language);
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 10000, IsDemo = false, DistanceSpacing = .4 };
            map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
            ui.LoadDocument(map); ui.SetSnapDivisor(4);
            var baseline = ui.View.Document.DeepClone();
            var button = ui.View.AssistButtonBounds[5];
            ui.View.PointerMove(button.X + 10, button.Y + 10, false, false); ui.Paint();
            ui.ClickText(Strings.Get("ds.configure"));
            Check(ui.View.DistanceSnapDialogVisible && ui.View.DistanceSnapPointerBounds.Count == 0, "Dialog did not open empty.");
            double start = ui.View.PlayheadMs, beatLength = TimingMap.At(map, start).BeatLengthMs;
            double duration = beatLength / 4;
            ConvertedCatchObject Note(double time) => new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, time, 256, 256, 256, 0);
            var range = HyperDashCalculator.Calculate([Note(start), Note(start + duration)], map.CircleSize)[0].Movement!.Value;
            double unit = duration * DistanceSnap.BaseVelocity(map, start);
            foreach (double boundary in new[] { range.StandLimit, range.WalkLimit, range.DashLimit })
                Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("assist.ratio", boundary / unit)), "Reference transition differs from a fresh departure.");
            Check(!ui.View.IsEditingText, "Preview settings entered input mode without a click.");
            var preview = ui.View.DistanceSnapPreviewBounds;
            Check(ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 17, "Quarter snap did not show sixteen intervals and the closing line.");
            ui.Click(preview.X + 50, preview.Bottom - 12);
            ui.Click(preview.X + 140, preview.Bottom - 12 - (preview.Height - 24) / 16);
            Check(ui.View.DistanceSnapPreviewFruits.Count == 2
                && ui.View.DistanceSnapPreviewFruits[0].X == ui.View.DistanceSnapPreviewFruits[1].X,
                "Empty configuration used the hidden legacy DistanceSpacing.");
            ui.ClickText(Strings.Get("ds.reset"));
            ui.Key('F'); ui.Key('Y'); ui.Key(116);
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsTestplaying, "Modal input escaped to editor.");
            var emptyTrack = ui.View.DistanceSnapTrackBounds;
            ui.Click(emptyTrack.X + emptyTrack.Width * .8f, emptyTrack.Y + 8);
            var addedPointer = ui.View.DistanceSnapPointerBounds.Single();
            Check(Math.Abs(addedPointer.X + 8 - (emptyTrack.X + emptyTrack.Width * .8f)) < 3,
                "Clicking the empty pointer row did not add at the clicked position.");
            ui.View.PointerDown(addedPointer.X + 8, addedPointer.Y + 8, 2, false, false); ui.Paint();
            ui.ClickText(Strings.Get("ds.add"));
            var track = ui.View.DistanceSnapTrackBounds;
            var pointer = ui.View.DistanceSnapPointerBounds.Single();
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", 0)), "Add did not create zero DS.");
            Check(Math.Abs(pointer.X + 8 - track.X) < .01, "New pointer was not at zero.");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", 0) && t.Y < pointer.Y), "Arrow value is missing above the pointer.");
            Check(ui.Canvas.Outlines.Any(o => o.Color == 0xC0C0C0 && o.Bounds.Y > track.Y + 100), "Zero value does not have the Stand border.");
            Drag(.625f, false);
            double expected = Math.Round((range.WalkLimit + range.DashLimit) / 2 / unit, 1, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Pointer drag did not snap to tenths.");
            Drag(.625f, true);
            expected = Math.Round((range.WalkLimit + range.DashLimit) / 2 / unit, 2, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Shift pointer drag did not snap to hundredths.");
            pointer = ui.View.DistanceSnapPointerBounds.Single();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
            ui.View.PointerMove(track.X + track.Width * .2f, track.Y + 8, false, false);
            ui.Key(27); ui.View.PointerUp(track.X, track.Y + 8, 0); ui.Paint();
            Check(ui.View.DistanceSnapDialogVisible && !ui.View.WantsCapture && ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Escape did not restore the dragged value.");
            pointer = ui.View.DistanceSnapPointerBounds.Single();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
            ui.View.PointerMove(track.X, track.Y + 8, false, false); ui.View.CancelInteraction(); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Capture cancellation lost the original ratio.");

            float FruitX(double x) => preview.X + 10 + (float)(x / 512) * (preview.Width - 20);
            float FruitY(double beat) => preview.Bottom - 12 - (float)(beat / 4) * (preview.Height - 24);
            ui.Click(FruitX(100), FruitY(0));
            ui.Click(FruitX(100 + expected * unit), FruitY(.25));
            var fruits = ui.View.DistanceSnapPreviewFruits;
            Check(fruits.Count == 2 && Math.Abs(fruits[1].X - fruits[0].X - expected * unit) < .001 && fruits[1].TimeMs == .25, "Preview fruit placement did not snap to the custom DS and beat.");
            Check(!ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", expected)), "Show DS should default to off.");
            ui.ClickText(Strings.Get("ds.showValues"));
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", expected)), "Adjacent preview fruits do not show actual DS.");
            ui.Click(FruitX(fruits[1].X + 1), FruitY(.5));
            Check(Math.Abs(fruits[2].X - fruits[1].X) < .001, "Preview omitted implicit zero DS.");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 0)), "Zero DS is missing from the preview readout.");
            ui.View.PointerDown(FruitX(fruits[2].X), FruitY(.5), 2, false, false); ui.Paint();
            Check(fruits.Count == 2, "Right click did not remove a preview fruit.");
            ui.View.PointerDown(preview.X + 40, preview.Y + 40, 1, false, false);
            ui.View.PointerMove(preview.X + 80, preview.Y + 80, false, false);
            ui.View.PointerUp(preview.X + 80, preview.Y + 80, 1);
            ui.View.Wheel(preview.X + 50, preview.Y + 50, -1200, false); ui.Paint();
            Check(ui.View.PlayheadMs == start && preview == ui.View.DistanceSnapPreviewBounds && baseline.ContentEquals(ui.View.Document), "Preview navigation or placement modified the map.");

            var subdivision = ui.View.DistanceSnapSubdivisionBounds;
            int[] divisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16];
            for (int i = 0; i < divisors.Length; i++)
            {
                float x = subdivision.X + 7 + (subdivision.Width - 38) * i / (divisors.Length - 1);
                ui.View.PointerDown(x, subdivision.Y + 15, 0, false, false);
                Check(ui.View.WantsCapture, "Snap slider did not capture input.");
                ui.View.PointerMove(x, subdivision.Y + 15, false, false);
                ui.View.PointerUp(x, subdivision.Y + 15, 0); ui.Paint();
                Check(!ui.View.WantsCapture && ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 4 * divisors[i] + 1,
                    "Snap slider did not select a supported divisor or draw the final line.");
            }
            ui.View.PointerDown(subdivision.X + 7, subdivision.Y + 15, 0, false, false);
            ui.Key(27); ui.View.PointerUp(subdivision.X + 7, subdivision.Y + 15, 0); ui.Paint();
            Check(ui.View.DistanceSnapDialogVisible && ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 65, "Escape did not restore the preview Snap.");
            ui.View.PointerDown(subdivision.X + 7, subdivision.Y + 15, 0, false, false);
            ui.View.CancelInteraction(); ui.Paint();
            Check(!ui.View.WantsCapture && ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 65, "Lost capture did not restore Snap.");
            Check(ui.View.SnapDivisor == 4 && baseline.ContentEquals(ui.View.Document), "Preview Snap changed editor settings or content.");

            for (int i = 1; i < 9; i++) ui.ClickText(Strings.Get("ds.add"));
            Check(ui.View.DistanceSnapPointerBounds.Count == 8, "Preset cap failed.");
            ui.Click(track.X + track.Width * .95f, track.Y + 8);
            Check(ui.View.DistanceSnapPointerBounds.Count == 8, "Empty-row click exceeded the preset cap.");
            var arrowLabels = ui.Canvas.Texts.Where(t => t.Y < track.Y && t.Y > track.Y - 70
                && t.Value.Length > 1 && char.IsAsciiDigit(t.Value[0]) && t.Value.EndsWith("x", StringComparison.Ordinal)).ToArray();
            Check(arrowLabels.Length == 8, "Not all eight arrows have value labels.");
            pointer = ui.View.DistanceSnapPointerBounds.Last();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 2, false, false); ui.Paint();
            Check(ui.View.DistanceSnapPointerBounds.Count == 7, "Right click did not delete an arrow.");
            ui.ClickText(Strings.Get("library.apply"));
            Check(!ui.View.DistanceSnapDialogVisible && ui.View.Document.DistanceSnapRatios.Count == 7, "Apply failed.");
            Check(ui.View.Document.DistanceSnapRatios.SequenceEqual(ui.View.Document.DistanceSnapRatios.Order()), "Applied values are not sorted.");
            var configured = ui.View.Document.DeepClone(); configured.DistanceSnapRatios.Clear();
            Check(baseline.ContentEquals(configured), "Configuration moved beatmap objects.");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.DistanceSnapRatios.Count == 0, "Apply did not undo as one step.");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.DistanceSnapRatios.Count == 7, "Apply did not redo.");
            ui.Resize(960, 640); ui.View.OpenDistanceSnapDialog(); ui.Paint();
            Check(ui.View.DistanceSnapPreviewFruits.Count == 0 && ui.Canvas.Lines.Count(l => l.X1 == ui.View.DistanceSnapPreviewBounds.X && l.X2 == ui.View.DistanceSnapPreviewBounds.Right && l.Y1 == l.Y2) == 17, "Reopening did not reset the preview to editor settings.");
            pointer = ui.View.DistanceSnapPointerBounds.Last();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 2, false, false); ui.Paint();
            ui.ClickText(Strings.Get("mac.cancel"));
            Check(ui.View.Document.DistanceSnapRatios.Count == 7, "Cancel persisted a draft deletion.");
            var saved = ui.View.Document.DeepClone();
            ui.View.LoadProject(BeatmapProject.FromDocuments([saved, new MapDocument()])); ui.Paint();
            Check(ui.View.SwitchDifficulty(1), "Could not switch difficulty.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            Check(ui.View.DistanceSnapPointerBounds.Count == 0, "Unconfigured map inherited another map's DS list.");
            ui.Key(27); Check(ui.View.SwitchDifficulty(0), "Could not return to configured difficulty.");
            Check(ui.View.Document.DistanceSnapRatios.SequenceEqual(saved.DistanceSnapRatios), "Map switch lost DS configuration.");
            PreviewReadouts();

            void Drag(float fraction, bool shift)
            {
                var arrow = ui.View.DistanceSnapPointerBounds.Single();
                ui.View.PointerDown(arrow.X + 8, arrow.Y + 8, 0, shift, false);
                Check(ui.View.WantsCapture, "Arrow did not capture input.");
                ui.View.PointerMove(track.X + track.Width * fraction, track.Y + 8, shift, false);
                ui.View.PointerUp(track.X + track.Width * fraction, track.Y + 8, 0); ui.Paint();
                Check(!ui.View.WantsCapture, "Arrow did not release capture.");
            }
        }
        Strings.SetLanguage("en");
    }

    private static void PreviewReadouts()
    {
        var ui = new Ui();
        var map = new MapDocument { SliderMultiplier = 1, IsDemo = false };
        map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        map.DistanceSnapRatios.Add(12);
        ui.LoadDocument(map); ui.SetSnapDivisor(4); ui.View.OpenDistanceSnapDialog(); ui.Paint();
        ui.ClickText(Strings.Get("ds.showValues"));
        var r = ui.View.DistanceSnapPreviewBounds;
        float X(double x) => r.X + 10 + (float)(x / 512) * (r.Width - 20);
        float Y(double beat) => r.Bottom - 12 - (float)(beat / 4) * (r.Height - 24);
        ui.Click(X(80), Y(0));
        ui.View.PointerMove(X(380), Y(.25), false, false); ui.Paint();
        Check(ui.Canvas.Circles.Any(c => c.Filled && c.Color == 0xFF0000 && Math.Abs(c.X - X(80)) < .01), "Prospective HDash did not use its departure fruit colour.");
        ui.Click(X(380), Y(.25));
        ui.View.PointerMove(r.X - 20, r.Y, false, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12)), "HDash connection omitted its actual DS.");
        Check(ui.Canvas.Circles.Count(c => c.Filled && c.Color == 0xFF0000 && r.Contains(c.X, c.Y)) == 1, "HDash did not mark only the departure fruit.");
        ui.ClickText(Strings.Get("ds.showValues"));
        Check(!ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12))
            && ui.Canvas.Circles.Any(c => c.Filled && c.Color == 0xFF0000 && r.Contains(c.X, c.Y))
            && ui.View.DistanceSnapPreviewFruits.Count == 2, "Hiding DS labels changed preview fruits or HDash markings.");
        ui.ClickText(Strings.Get("ds.showValues"));
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12)), "Show DS did not restore the labels.");
        var pointer = ui.View.DistanceSnapPointerBounds.Single();
        var track = ui.View.DistanceSnapTrackBounds;
        ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
        ui.View.PointerMove(track.X + track.Width * .375f, track.Y + 8, false, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12) && t.Color == 0xFF5555), "Old preview DS was not red after its preset changed.");
        ui.Key(27); ui.View.PointerUp(track.X, track.Y + 8, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12) && t.Color != 0xFF5555), "Restored DS remained red.");
        ui.Click(X(80), Y(.25));
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewUndefined")), "Simultaneous fruits displayed a finite DS.");
        ui.View.PointerDown(X(80), Y(.25), 2, false, false);
        ui.View.PointerMove(r.X - 20, r.Y, false, false); ui.Paint();
        ui.View.PointerDown(X(380), Y(.25), 2, false, false);
        ui.View.PointerMove(r.X - 20, r.Y, false, false); ui.Paint();
        Check(!ui.Canvas.Circles.Any(c => c.Filled && c.Color == 0xFF0000 && r.Contains(c.X, c.Y)), "Deleted target left a stale HDash colour.");
        ui.ClickText(Strings.Get("ds.reset"));
        Check(ui.View.DistanceSnapPreviewFruits.Count == 0 && ui.View.DistanceSnapPointerBounds.Count == 1,
            "Reset did not clear only the preview fruits.");
        ui.Click(X(256), Y(4));
        Check(ui.View.DistanceSnapPreviewFruits.Single().TimeMs == 4, "Closing beat line did not accept a fruit at beat four.");
        Check(ui.View.Document.Fruits.Count == 0, "Preview readouts modified the map.");
    }
}
