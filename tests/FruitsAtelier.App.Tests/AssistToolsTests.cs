using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class AssistToolsTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}");
    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, IsDemo = false };
        map.DistanceSnapRatios.Add(1);
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        map.Fruits.Add(new Fruit { TimeMs = 2000, X = 310 });
        return map;
    }
    public static void MovementAnalysis()
    {
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 10000, CircleSize = 5, IsDemo = false };
        foreach (var (time, x) in new[] { (1000, 100), (1125, 120), (1250, 220), (1375, 370), (1500, 70) })
            map.Fruits.Add(new Fruit { TimeMs = time, X = x });
        ui.LoadDocument(map);
        var original = ui.View.Document.DeepClone();
        Check(!ui.View.MovementAnalysisEnabled, "Analysis must start disabled");
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language); ui.Paint();
            ui.ClickText(Strings.Get("movement.analysis"));
            Check(ui.View.MovementAnalysisEnabled, "Canvas toolbar did not enable analysis");
            foreach (uint color in new uint[] { 0xC0C0C0, 0x63B99D, 0xD6B365, 0xCE7683 })
                Check(ui.Canvas.Lines.Any(l => l.Color == color && l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001), "Missing movement connection colour");
            ui.ClickText(Strings.Get("movement.analysis"));
            Check(!ui.View.MovementAnalysisEnabled, "Canvas toolbar did not disable analysis");
            Check(!ui.Canvas.Lines.Any(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001), "Disabled analysis retained lines");
            Check(original.ContentEquals(ui.View.Document), "Display toggle edited content");
        }
        ui = new Ui(); ui.LoadDocument(map); ui.ClickText(Strings.Get("movement.analysis"));
        int ordinaryConnections = ui.Canvas.Lines.Count(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001);
        ui.View.Document.BananaShowers.Add(new BananaShower { TimeMs = 1260, EndTimeMs = 1360 }); ui.Paint();
        Check(ui.Canvas.Lines.Count(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001) == ordinaryConnections - 1,
            "Analysis connected fruits across a banana shower");
        ui.View.Document.BananaShowers.Clear(); ui.Paint();
        Check(ui.Canvas.Lines.Count(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001) == ordinaryConnections,
            "Removing a shower did not restore its connection");
        var breakMap = new MapDocument { DurationMs = 5000, IsDemo = false };
        breakMap.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 2000, X = 300 }]);
        OsuTimeline.AddBreak(breakMap, 1250, 1750);
        ui = new Ui(); ui.LoadDocument(breakMap); ui.ClickText(Strings.Get("movement.analysis"));
        Check(!ui.Canvas.Lines.Any(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001)
            && ui.View.DistanceLabelBounds.Count == 0,
            "Movement Analysis retained part of a connection spanning a break.");
        var kiaiMap = new MapDocument { DurationMs = 5000, IsDemo = false };
        kiaiMap.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 2000, X = 300 }]);
        kiaiMap.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        kiaiMap.TimingPoints.Add(new TimingPoint { TimeMs = 1250, BeatLengthMs = 500, Uninherited = true, Effects = 1 });
        kiaiMap.TimingPoints.Add(new TimingPoint { TimeMs = 1750, BeatLengthMs = 500, Uninherited = true });
        ui = new Ui(); ui.LoadDocument(kiaiMap); ui.ClickText(Strings.Get("movement.analysis"));
        Check(ui.Canvas.Lines.Any(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001)
            && ui.View.DistanceLabelBounds.Count == 1,
            "Kiai between two fruits hid their connection or DS label.");
        kiaiMap.TimingPoints.RemoveRange(1, 2);
        ui.LoadDocument(kiaiMap); ui.Paint();
        Check(ui.Canvas.Lines.Any(l => l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001)
            && ui.View.DistanceLabelBounds.Count == 1,
            "Movement connection or DS label disappeared outside kiai.");
        ui = new Ui(); ui.LoadDocument(DemoMap.Create());
        ui.ClickText(Strings.Get("movement.analysis"));
        var curves = ui.Canvas.Operations.Where(o => o.Clip == ui.View.CanvasPlotBounds && o.Segment is { Color: 0xAB9DF2 }).ToArray();
        var connections = ui.Canvas.Operations.Where(o => o.Clip == ui.View.CanvasPlotBounds && o.Segment is { Width: 4, Opacity: .65f }).ToArray();
        Check(curves.Length > 0 && connections.Length > 0 && curves.Max(o => o.Order) < connections.Min(o => o.Order),
            "Movement connections must draw above curves");
        Strings.SetLanguage("en");
    }

    public static void MovementOverlay()
    {
        var ui = new Ui(); var map = Map();
        ui.LoadDocument(map);
        var original = ui.View.Document.DeepClone();
        var plot = ui.Plot;
        ui.Key('F'); ui.MoveMap(1500, 240);
        Check(ui.View.MovementReadout.Previous?.Mode == CatchMovementMode.Walk, "Placement did not show incoming walk");
        Check(ui.View.MovementOverlayBounds is not null, "Placement panel missing");
        Check(ui.Plot == plot, "Overlay resized the playfield");
        Check(original.ContentEquals(ui.View.Document), "Hover changed content");
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language); ui.MoveMap(1500, 120);
            Check(ui.View.MovementReadout.Previous?.Mode == CatchMovementMode.Stand, "Placement did not show Stand");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("movement.previous", "Stand")), "Stand label missing");
            Check(ui.Canvas.Fills.Any(f => f.Color == 0xC0C0C0 && f.Bounds.Width > 0), "Stand segment missing");
            ui.MoveMap(1500, 300);
            Check(ui.View.MovementReadout.Next?.Mode == CatchMovementMode.Stand, "Outgoing Stand missing");
        }
        ui.Key('1'); ui.ClickMap(2000, 310);
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language); ui.Resize(980, 620);
            var panel = ui.View.MovementOverlayBounds!.Value;
            Check(panel.Bottom < ui.View.CanvasPlotBounds.Bottom && panel.X >= ui.View.CanvasPlotBounds.X, "Panel escaped canvas");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("movement.previous", "Walk")), "Localized movement label missing");
        }
        ui = new Ui(); ui.LoadDocument(Map()); original = ui.View.Document.DeepClone(); ui.Key('F'); ui.MoveMap(1500, 240);
        ui.View.PointerMove(10, 10, false, false); ui.Paint();
        Check(ui.View.MovementOverlayBounds is null, "Hidden ghost retained panel");
        ui.Key('1'); ui.ClickMap(2000, 310);
        Check(ui.View.MovementReadout.Previous is not null && ui.View.MovementReadout.Next is null, "Selected fruit neighbours are wrong");
        ui.DownMap(2000, 310); ui.MoveMap(1125, 480); ui.UpMap(1125, 480);
        Check(ui.View.MovementReadout.Previous?.Mode == CatchMovementMode.HyperDash, "Dragged fruit did not update movement");
        ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Drag undo did not restore document");
        Strings.SetLanguage("en");
    }

    public static void SpacingAndPlacement()
    {
        var ui = new Ui(); ui.LoadDocument(Map()); ui.Key('F');
        Check(ui.View.AssistButtonBounds.Count == 7, "Missing assist buttons");
        var icons = ui.Canvas.Images.Where(i => i.Path.Contains(Path.Combine("icons", "assist"))).ToArray();
        Check(icons.Length == 7 && icons.All(i => File.Exists(i.Path)), "Generated assist icons are missing from the application output");
        var p = ui.View.CanvasPlotBounds;
        ui.View.SetModifiers(true, false); ui.Paint();
        Check(ui.View.DistanceSnapEnabled, "Alt did not switch snapping");
        ui.MoveMap(1500, 240);
        Near(1, ui.View.DistanceReadout.Previous!.Value);
        Near(.5, ui.View.DistanceReadout.Next!.Value);
        ui.ClickMap(1500, 270);
        Near(240, ui.View.Document.Fruits.Single(f => f.TimeMs == 1500).X);
        ui.View.SetModifiers(false, false);
        ui.Key('1');
        ui.View.SetModifiers(true, false);
        Near(1, ui.View.DistanceReadout.Previous!.Value);
        Near(.5, ui.View.DistanceReadout.Next!.Value);
        ui.View.SetModifiers(false, false);
        ui.Key('Z', ctrl: true);
        ui.Key('1');
        Check(ui.View.DistanceReadout == (null, null), "Clearing selection retained distance readouts");
        ui.Key('B'); ui.View.SetModifiers(true, false); ui.MoveMap(1500, 240);
        Near(1, ui.View.DistanceReadout.Previous!.Value);
        Near(.5, ui.View.DistanceReadout.Next!.Value);
        ui.View.PointerMove(10, 10, false, false); ui.Paint();
        Check(ui.View.DistanceReadout == (null, null), "A hidden placement preview retained distance readouts");
        ui.View.SetModifiers(true, false);
        ui.View.Wheel(p.X + 10, p.Y + 10, 120, false); ui.Paint();
        Near(1, ui.View.Document.DistanceSpacing);
        ui.View.SetModifiers(true, true); ui.View.Wheel(p.X + 10, p.Y + 10, 120, false); Near(1, ui.View.Document.DistanceSpacing);
        ui.View.SetModifiers(false, false); ui.Key('Y');
        Check(ui.View.DistanceSnapEnabled, "Y did not enable distance snap");
        ui.View.SetModifiers(true, false); Check(!ui.View.DistanceSnapEnabled, "Alt must invert persistent snap");
        ui.View.CancelInteraction(); Check(ui.View.DistanceSnapEnabled, "Focus cancellation left Alt active");
        var original = ui.View.Document.DeepClone();
        ui.View.SetModifiers(true, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ui.snap")), "Alt hid the beat-snap control");
        var slider = ui.View.SnapSliderBounds;
        ui.View.PointerDown(slider.X + 20, slider.Y + 10, 0, false, false);
        ui.View.PointerMove(slider.X + 60, slider.Y + 10, false, false);
        ui.View.CancelInteraction(); Check(original.ContentEquals(ui.View.Document), "Cancelled beat-snap drag changed document");
        ui.View.SetModifiers(true, false); ui.Paint();
        ui.View.PointerDown(slider.X + 20, slider.Y + 10, 0, false, false);
        ui.View.PointerMove(slider.X + 60, slider.Y + 10, false, false);
        ui.View.PointerUp(slider.X + 60, slider.Y + 10, 0);
        Check(original.ContentEquals(ui.View.Document), "Alt beat-snap drag changed document");
        var project = BeatmapProject.FromDocuments([Map(), Map()]);
        project.Difficulties[0].Document.DistanceSpacing = 1.7;
        ui.View.LoadProject(project); ui.Paint(); Near(1.7, ui.View.Document.DistanceSpacing);
        ui.View.SwitchDifficulty(1); Near(1, ui.View.Document.DistanceSpacing);
        ui.View.SwitchDifficulty(0); Near(1.7, ui.View.Document.DistanceSpacing);
        string json = ProjectSerializer.Serialize(Map()).Replace("\"DistanceSpacing\": 1,", "");
        Near(1, ProjectSerializer.Read(json).DistanceSpacing);
    }

    public static void DistanceRules()
    {
        var map = Map();
        map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500 });
        map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = -50, Uninherited = false });
        var references = DistanceSnap.References(map, CatchStreamConverter.Convert(map));
        Near(.28, references[0].Velocity);
        Near(1.5, DistanceSnap.Ratio(new(1000, 100), new(1500, 310), .28)!.Value);
        Check(DistanceSnap.Ratio(new(1000, 100), new(1000, 310), .28) is null, "Simultaneous objects must have no ratio");
        var snapped = DistanceSnap.Snap(new(1500, 20), references[0], 1, out bool outside);
        Near(240, snapped.X); Check(!outside, "Valid opposite side rejected");
        DistanceSnap.Snap(new(5000, 20), references[0], 6, out outside); Check(outside, "Impossible spacing not reported");
        var slider = new ImportedSlider { TimeMs = 2500, X = 100, Y = 192, PathType = 'L', PixelLength = 140, SpanCount = 2 };
        slider.ControlPoints.AddRange([new(100, 192), new(240, 192)]); map.ImportedSliders.Add(slider);
        var last = DistanceSnap.References(map, CatchStreamConverter.Convert(map)).Last();
        Near(.28, last.Velocity); Near(3000, last.End.TimeMs); Near(100, last.End.X);
        Near(240, DistanceSnap.Snap(new(3500, 240), last, 1, out _).X);
        map.TimingPoints.Add(new TimingPoint { TimeMs = 2800, BeatLengthMs = 250 });
        last = DistanceSnap.References(map, CatchStreamConverter.Convert(map)).Last();
        Near(.56, last.Velocity); Near(3000, last.End.TimeMs);
        Near(DistanceSnap.BaseVelocity(map, last.End.TimeMs), last.Velocity);
        Near(380, DistanceSnap.Snap(new(3500, 380), last, 1, out _).X);

        var ui = new Ui(); ui.LoadDocument(map); ui.ClickMap(2000, 310);
        Near(.75, ui.View.DistanceReadout.Previous!.Value);
        double ratio = ui.View.DistanceReadout.Previous.Value;
        ui.View.Wheel(ui.Plot.X, ui.Plot.Y + 50, 120, true, true); ui.Paint(); Near(ratio, ui.View.DistanceReadout.Previous!.Value);
        Check(ui.Canvas.Texts.Any(t => t.Value.Contains("DPB 140px")), "Details must show base DPB");
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language); ui.Resize(980, 620);
            var bounds = ui.View.AssistButtonBounds;
            Check(bounds.All(b => b.X >= ui.View.CanvasPlotBounds.Right && b.Width == b.Height), "Assist buttons must be square and beside the plot");
            var first = bounds[0];
            ui.View.Wheel(first.X + 10, first.Y + 10, -1200, false); ui.Paint();
            Check(ui.View.AssistButtonBounds[^1].Bottom <= ui.View.CanvasPlotBounds.Bottom + .01, "Small-window scrolling does not reach Lock Notes");
        }
    }

    public static void SoundsAndLocks()
    {
        var ui = new Ui(); var map = Map(); ui.LoadDocument(map);
        Guid id = map.Fruits[0].Id;
        ui.ClickFruit(id); ui.Key('Q'); ui.Key('W'); ui.Key('E'); ui.Key('R');
        Check(ObjectFlags.NewCombo(ui.View.Document, id), "Combo toggle missing");
        Check(ObjectFlags.Sounds(ui.View.Document, id).Single() == 14, "Combined additions missing");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
        Check(ObjectFlags.Sounds(restored, id).Single() == 14, "Saved flags missing");
        var exported = OsuBeatmapWriter.Serialize(restored).ReadBack;
        Check(ObjectFlags.Sounds(exported, exported.Fruits[0].Id).Single() == 14, "Export lost additions");
        var converted = CatchStreamConverter.Convert(restored);
        Check(new HitsoundResolver(restored, converted.Objects).Resolve(converted.Objects.First(o => o.SourceId == id)).Count == 4, "Playback does not reflect additions");
        ui.Key('R'); Check(ObjectFlags.Sounds(ui.View.Document, id).Single() == 6, "Cannot remove clap");
        ui.Key('Z', ctrl: true); Check(ObjectFlags.Sounds(ui.View.Document, id).Single() == 14, "Sound undo failed");
        ui.ClickFruit(id); ui.Key('L'); var locked = ui.View.Document.DeepClone();
        ui.DownMap(1000, 100); ui.MoveMap(1250, 200); ui.UpMap(1250, 200);
        Check(locked.ContentEquals(ui.View.Document), "Locked drag moved note");
        ui.Key(46); Check(locked.ContentEquals(ui.View.Document), "Locked delete removed note");
        ui.Key('W'); Check(ObjectFlags.Sounds(ui.View.Document, id).Single() == 12, "Lock blocked sound edit");
        ui.Key('L'); ui.Key(46); Check(ui.View.Document.Fruits.Count == 1, "Unlock did not restore editing");
        ui.Key('F'); ui.Key('W'); ui.ClickMap(1250, 180);
        Check(ObjectFlags.Sounds(ui.View.Document, ui.View.Document.Fruits.Single(f => f.TimeMs == 1250).Id).Single() == 2, "Placement sound missing");
        ui.Key('L'); ui.Key('B'); ui.ClickMap(3000, 200); ui.ClickMap(3500, 240); ui.Key(13);
        Check(ui.View.Document.Tracks.Count == 1 && ui.View.Document.Tracks[0].Nodes.Count == 2, "Lock prevented completing a new slider");
    }

    public static void GroupDistanceDrag()
    {
        var map = Map(); map.Fruits.Add(new Fruit { TimeMs = 500, X = 80 });
        var ui = new Ui(); ui.LoadDocument(map);
        ui.ClickMap(1000, 100); ui.ClickMap(2000, 310, ctrl: true);
        ui.Key('Y');
        var before = ui.View.Document.DeepClone();
        ui.DownMap(1000, 100); ui.MoveMap(1250, 250); ui.UpMap(1250, 250);
        Near(290, ui.Fruit(map.Fruits[0].Id).X); Near(500, ui.Fruit(map.Fruits[1].Id).X);
        Near(1250, ui.Fruit(map.Fruits[0].Id).TimeMs); Near(2250, ui.Fruit(map.Fruits[1].Id).TimeMs);
        ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Distance group movement did not undo atomically");
    }

    public static void SliderSounds()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|240:192,2,140,2|4|8,1:2|2:3|3:1,2:3:7:60:\n");
        var ui = new Ui(); ui.LoadDocument(map); var id = map.ImportedSliders.Single().Id;
        ui.ClickMap(1000, 100); ui.ClickMap(1000, 100); ui.Key('R');
        Check(ObjectFlags.Sounds(ui.View.Document, id).SequenceEqual(new[] { 10, 4, 8 }), "Head click changed other edges");
        Check(ui.View.Document.ImportedSliders.Single().OriginalLine!.EndsWith("1:2|2:3|3:1,2:3:7:60:"), "Sample banks were overwritten");
        var output = OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack;
        Check(ObjectFlags.Sounds(output, output.ImportedSliders.Single().Id).SequenceEqual(new[] { 10, 4, 8 }), "Edge export failed");
        ui.Key('Z', ctrl: true);
        ui.View.SetModifiers(false, false);
        var timeline = ui.View.ObjectTimelineBounds;
        float x = timeline.X + (float)((1000 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        ui.Click(x, timeline.Y + 20); ui.Key('W');
        Check(ObjectFlags.Sounds(ui.View.Document, id).SequenceEqual(new[] { 2, 6, 10 }), "Whole-slider toggle failed");
        ui.Key('L'); var before = ui.View.Document.DeepClone(); ui.Key(187, ctrl: true);
        Check(before.ContentEquals(ui.View.Document), "Lock allowed reverse edit");
        ui.View.ConvertAllSliders(); Check(!ui.View.SliderConversionBusy, "Lock allowed batch geometry conversion");
        ui.Key('L'); ui.MoveMap(1000, 100); ui.HoldMap(1000, 100); ui.ClickText(FruitsAtelier.Localization.Strings.Get("preview.convertSlider"));
        Check(ui.View.SliderImportPromptVisible, "First single-slider conversion did not offer droplet options.");
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("sliderBatch.convert"));
        Check(ui.View.Document.Tracks.Count == 1, "Cannot convert sound-edited slider");
        var fs = OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack;
        Check(ObjectFlags.Sounds(fs, fs.ImportedSliders.Single().Id).SequenceEqual(new[] { 2, 6, 10 }), "FSlider export lost edge flags");
    }
}
