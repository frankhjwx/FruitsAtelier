using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class SliderDistanceDragTests
{
    public static void DropletOutgoingSpacing()
    {
        var map = new MapDocument { DurationMs = 5000, SliderTickRate = 8 };
        map.DistanceSnapRatios.AddRange([.2, .6, 1.4]);
        var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 200 },
            new Anchor { TimeMs = 1062.5, X = 210 }, new Anchor { TimeMs = 1125, X = 225 }]);
        map.Tracks.Add(track);
        var before = CatchStreamConverter.Convert(map);
        var target = before.Objects.Single(o => o.Kind == CatchObjectKind.Droplet);
        var ui = new Ui(); ui.LoadDocument(map); ui.SelectTrack(track.Id); ui.Key('Y');
        // Closely spaced events need a larger viewport scale for unambiguous hit testing.
        ui.Resize(1440, 2400);
        ui.View.Wheel(ui.Plot.X, ui.Plot.Bottom, 120 * 15, false, false, true); ui.Paint();
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.sliderPathCurves"));
        ui.ClickMap(target.TimeMs, target.X);
        ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, 215); ui.UpMap(target.TimeMs, 215);
        var after = CatchStreamConverter.Convert(ui.View.Document);
        var moved = after.Objects.Single(o => o.EventIndex == target.EventIndex);
        Check(after.Success && Math.Abs(moved.X - 214.4) < .001,
            $"Following straight segment prevented preceding-event snap: {moved.X}; {ui.View.StatusMessage}");
        ui.DownMap(moved.TimeMs, moved.X); ui.MoveMap(moved.TimeMs, 50); ui.UpMap(moved.TimeMs, 50);
        after = CatchStreamConverter.Convert(ui.View.Document);
        Check(Math.Abs(after.Objects.Single(o => o.EventIndex == target.EventIndex).X - 166.4) < .001,
            "Following maximum DS prevented a valid preceding-event snap.");
    }

    public static void SegmentedDroplet()
    {
        var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 458.015267175573,
            TimingOffsetMs = -185109, SliderMultiplier = 3.59999990463257, SliderTickRate = 2 };
        map.DistanceSnapRatios.AddRange([.2, .6, 1.4]);
        var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
        double[] times = [845.19847328262, 902.44847328262, 959.69847328262, 1016.94847328262,
            1074.20610687038, 1131.4561068704, 1188.7061068704, 1245.9561068704, 1303.2137404582, 1417.7175572521];
        double[] positions = [171.0263961846318, 234.02429603620365, 243.02399175377687, 252.02368657799562,
            183.59120467790223, 246.5891001133521, 245.43899131731197, 252.6513828929663, 301.0438374598856, 320.2513155679774];
        for (int i = 0; i < times.Length; i++) track.Nodes.Add(new() { TimeMs = times[i], X = positions[i] });
        track.Nodes[^2].OutgoingKind = CurveKind.Bezier;
        track.Nodes[^2].HandleOut = new(34.90816775755957, 6.1512222750441765);
        track.Nodes[^1].HandleIn = new(-41.514074097562116, -6.645059076611005);
        map.Tracks.Add(track);
        var before = CatchStreamConverter.Convert(map);
        var target = before.Objects.First(o => o.Kind == CatchObjectKind.Droplet);
        var displayed = OsuBeatmapWriter.Serialize(map).PlayableObjects.Single(o => o.EventIndex == target.EventIndex);
        var ui = new Ui(); ui.LoadDocument(map); ui.SelectTrack(track.Id); ui.Key('Y');
        ui.ClickMap(displayed.TimeMs, displayed.X);
        ui.DownMap(displayed.TimeMs, displayed.X); ui.MoveMap(displayed.TimeMs, 210); ui.UpMap(displayed.TimeMs, 210);
        var after = CatchStreamConverter.Convert(ui.View.Document);
        var moved = after.Objects.Single(o => o.EventIndex == target.EventIndex);
        Check(after.Success && Math.Abs(moved.X - target.X) > 10,
            $"Segmented droplet stayed stuck at {moved.X}: {ui.View.StatusMessage}");
        double ratio = Math.Abs(moved.X - before.Objects[0].X)
            / ((moved.TimeMs - before.Objects[0].TimeMs) * DistanceSnap.BaseVelocity(map, before.Objects[0].TimeMs));
        Check(SliderDistanceSnap.MatchesPreset(map, ratio), "Droplet did not snap to previous large event.");
        foreach (var old in before.Objects.Where(o => o.EventIndex != target.EventIndex))
            Check(Math.Abs(after.Objects.Single(o => o.EventIndex == old.EventIndex).X - old.X) < .001,
                "Droplet drag moved an unselected event.");
        ui.Key('Z', ctrl: true);
        Check(map.ContentEquals(ui.View.Document), "Segmented droplet undo lost source geometry.");
    }

    public static void ControlOverhangs()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        foreach (bool ds in new[] { false, true })
        foreach (bool incoming in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 5000 };
            map.DistanceSnapRatios.Add(2);
            var track = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
            track.Nodes.Add(new() { TimeMs = 1000, X = 200, HandleOut = new(200, 60) });
            track.Nodes.Add(new() { TimeMs = 2000, X = 240, HandleIn = new(-200, 60) });
            map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode);
            ui.SelectTrack(track.Id); ui.Key('B');
            if (ds) ui.Key('Y');
            double x = incoming ? 300 : 260, from = incoming ? 1800 : 1200, to = incoming ? 2200 : 800;
            ui.DownMap(from, x); ui.MoveMap(to, x); ui.UpMap(to, x);
            var points = ControlCurveMath.Points(ui.View.Document.Tracks.Single(), 0);
            Check(Math.Abs(points[incoming ? ^2 : 1].TimeMs - to) < 1,
                $"Control could not cross endpoint: {mode}, DS={ds}, incoming={incoming}. {ui.View.StatusMessage}");
            Check(CatchStreamConverter.Convert(ui.View.Document).Success, "Dragged overhang failed conversion.");
            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), "Overhang undo lost source geometry.");
        }
    }

    public static void FractionalFoldedTail()
    {
        foreach (bool ds in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 458.015267175573,
                TimingOffsetMs = -185109, SliderMultiplier = 3.59999990463257, SliderTickRate = 2 };
            map.DistanceSnapRatios.AddRange([.2, .6, 1.4]);
            var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
            track.Nodes.AddRange([
                new Anchor { TimeMs = 1188.7099236643, X = 216.40395027797723 },
                new Anchor { TimeMs = 1285.2099236643, X = 360.5540771484375 },
                new Anchor { TimeMs = 1417.7175572521, X = 147.5218814438465 }]);
            map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            if (ds) ui.Key('Y');
            var displayed = OsuBeatmapWriter.Serialize(map).PlayableObjects.Last(o => o.Kind == CatchObjectKind.Fruit);
            ui.DownMap(displayed.TimeMs, displayed.X);
            ui.MoveMap(displayed.TimeMs, 280); ui.UpMap(displayed.TimeMs, 280);
            var moved = ui.View.Document.Tracks.Single().Nodes[^1];
            Check(Math.Abs(moved.X - track.Nodes[^1].X) > 10,
                $"Fractional folded tail stayed stuck (DS={ds}): {moved.X}; {ui.View.StatusMessage}");
            Check(Math.Abs(moved.TimeMs - track.Nodes[^1].TimeMs) < 1e-6, "Displayed tail quantization changed authored time.");
            if (ds) CheckStrictTail(ui, CatchStreamConverter.Convert(ui.View.Document), track.Id);
            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), "Fractional tail undo did not restore the authored geometry.");
        }
    }
    public static void SelectedTail()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var (ui, id) = Create(mode, false);
            ui.DownMap(1500, 392);
            ui.MoveMap(1500, 210);
            Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 200) < .001,
                $"{mode}: selected tail could not reach zero DS: {ui.View.Document.Tracks.Single().Nodes[^1].X}");
            ui.MoveMap(1500, 380);
            Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 392) < .001,
                $"{mode}: selected tail could not return to 1 DS.");
            ui.MoveMap(1500, 210); ui.UpMap(1500, 210);
            ui.Key('Z', ctrl: true);
            Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 392) < .001,
                "Tail drag did not undo as one edit.");
        }
    }

    public static void CurvedTail()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        foreach (bool controls in new[] { false, true })
        {
            var (ui, id) = Create(mode, true);
            if (controls) ui.Key('B');
            ui.DownMap(1500, 280); ui.MoveMap(1500, 500);
            double moved = ui.View.Document.Tracks.Single().Nodes[^1].X;
            Check(controls ? moved > 281 && moved < 499 : Math.Abs(moved - 280) > 1,
                $"{mode}, controls={controls}: curve tail should clamp with useful movement, got {moved}. {ui.View.StatusMessage}");
            var result = CatchStreamConverter.Convert(ui.View.Document);
            Check(result.Success && (!controls || SliderDistanceSnap.Excesses(ui.View.Document, result.Objects, id).Count == 0),
                "Clamped tail exceeded maximum DS.");
            if (!controls) CheckStrictTail(ui, result, id);
            ui.Key(27);
            Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 280) < .001,
                "Cancelled curved tail drag changed geometry.");
        }
    }

    public static void TinyDroplet()
    {
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        foreach (bool compensate in new[] { true, false })
        {
            var map = new MapDocument { DurationMs = 5000, SliderTickRate = 1 };
            var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = compensate };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 200 }, new Anchor { TimeMs = 3000, X = 200 }]);
            map.Tracks.Add(track);
            var before = CatchStreamConverter.Convert(map);
            var target = before.Objects.First(item => item.Kind == kind);
            var previous = before.Objects.Last(item => item.TimeMs < target.TimeMs
                && (kind == CatchObjectKind.TinyDroplet || item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet));
            double wanted = previous.X + 9;
            map.DistanceSnapRatios.Add(9 / ((target.TimeMs - previous.TimeMs) * DistanceSnap.BaseVelocity(map, previous.TimeMs)));
            var ui = new Ui(); ui.LoadDocument(map);
            ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
            ui.Key('Y'); ui.Key('T');
            ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, wanted + .3); ui.UpMap(target.TimeMs, wanted + .3);
            var after = CatchStreamConverter.Convert(ui.View.Document);
            var moved = after.Objects.Single(item => item.EventIndex == target.EventIndex);
            Check(after.Success && Math.Abs(moved.X - wanted) < .001,
                $"{kind} did not snap using displayed X (compensate={compensate}): {moved.X}, expected {wanted}. {ui.View.StatusMessage}");
            foreach (var old in before.Objects.Where(item => item.EventIndex != target.EventIndex))
                Check(Math.Abs(after.Objects.Single(item => item.EventIndex == old.EventIndex).X - old.X) < .001,
                    "Tiny DS drag displaced another event.");
            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), "Tiny DS drag did not undo.");
        }
    }

    public static void ModifiersAndUnreachable()
    {
        var (ui, id) = Create(SliderEditingMode.PenTool, false);
        ui.DownMap(1500, 392);
        ui.View.SetModifiers(true, false);
        ui.MoveMap(1500, 310);
        Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 310) < .5,
            $"Alt did not disable endpoint DS: {ui.View.Document.Tracks.Single().Nodes[^1].X}, enabled={ui.View.DistanceSnapEnabled}, {ui.View.StatusMessage}");
        ui.View.SetModifiers(false, false);
        ui.MoveMap(1500, 210);
        Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 200) < .001, "Re-enabling DS did not restore strict endpoint snapping.");
        ui.View.CancelInteraction(); ui.Paint();
        Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 392) < .001, "Capture cancellation lost the endpoint baseline.");

        var map = ui.View.Document;
        map.Tracks.Single().SpanCount = 2;
        var before = CatchStreamConverter.Convert(map);
        var target = before.Objects.First(item => item.Kind == CatchObjectKind.Droplet);
        ui.Paint(); ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
        var baseline = ui.View.Document.DeepClone();
        ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, 500); ui.UpMap(target.TimeMs, 500);
        Check(baseline.ContentEquals(ui.View.Document), "An unreachable repeated droplet edit left partial geometry.");
    }

    public static void FirstDraftHandle()
    {
        var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 5000 });
        ui.Key('B'); ui.Key('Y');
        ui.DownMap(1000, 200); ui.MoveMap(1250, 250); ui.UpMap(1250, 250);
        var node = ui.View.Document.Tracks.Single().Nodes.Single();
        Check(node.HandleOut.TimeMs > 200 && node.HandleOut.X > 40,
            "DS blocked the first draft handle before any slider interval existed.");
        ui.Key(27);
        Check(ui.View.Document.Tracks.Count == 0, "Cancelling a one-anchor draft left content.");
    }

    public static void SegmentedTail()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        foreach (bool controls in new[] { false, true })
        {
            var (ui, id) = Create(mode, false);
            ui.View.Document.Tracks.Single().Nodes.Insert(1, new Anchor { TimeMs = 1250, X = 296 });
            ui.Paint();
            if (controls) ui.Key('B');
            ui.DownMap(1500, 392); ui.MoveMap(1500, 310); ui.UpMap(1500, 310);
            Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[^1].X - 296) < .001,
                $"{mode}, controls={controls}: segmented straight tail did not snap: {ui.View.Document.Tracks.Single().Nodes[^1].X}");
            CheckStrictTail(ui, CatchStreamConverter.Convert(ui.View.Document), id);
        }
    }

    public static void RepeatedTailAndHead()
    {
        foreach (bool head in new[] { false, true })
        {
            var (ui, id) = Create(SliderEditingMode.PenTool, false);
            ui.View.Document.Tracks.Single().SpanCount = 2;
            ui.Paint();
            double time = head ? 1000 : 2000;
            ui.DownMap(time, 200); ui.MoveMap(time, 380); ui.UpMap(time, 380);
            var shape = ui.View.Document.Tracks.Single();
            Check(Math.Abs(shape.Nodes[0].X - (head ? 380 : 392)) < .001,
                "Selected endpoint did not follow its preceding-reference rule.");
            var result = CatchStreamConverter.Convert(ui.View.Document);
            Check(result.Success && (head || SliderDistanceSnap.StrictErrors(ui.View.Document, shape, result.Objects).Count == 0),
                "Repeat endpoint lost strict DS.");
        }
    }

    private static void CheckStrictTail(Ui ui, CatchConversionResult result, Guid id)
    {
        var events = result.Objects.Where(item => item.SourceId == id && item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet)
            .OrderBy(item => item.TimeMs).ToArray();
        var from = events[^2]; var to = events[^1];
        double ratio = Math.Abs(to.X - from.X) / ((to.TimeMs - from.TimeMs) * DistanceSnap.BaseVelocity(ui.View.Document, from.TimeMs));
        Check(SliderDistanceSnap.MatchesPreset(ui.View.Document, ratio), $"Selected tail DS was {ratio}, not a preset.");
    }

    private static (Ui Ui, Guid Id) Create(SliderEditingMode mode, bool curved)
    {
        var map = new MapDocument { DurationMs = 5000, SliderTickRate = 4 };
        map.DistanceSnapRatios.Add(1);
        var track = new CurveTrack { Kind = curved ? CurveKind.Bezier : CurveKind.Linear, CompensateTinyDroplets = true };
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 200, HandleOut = curved ? new(150, 0) : default });
        track.Nodes.Add(new Anchor { TimeMs = 1500, X = curved ? 280 : 392, HandleIn = curved ? new(-150, 0) : default });
        map.Tracks.Add(track);
        var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode);
        ui.SelectTrack(track.Id); ui.Key('Y');
        return (ui, track.Id);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
