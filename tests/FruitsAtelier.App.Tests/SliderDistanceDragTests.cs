using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class SliderDistanceDragTests
{
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
            Check(result.Success && SliderDistanceSnap.Excesses(ui.View.Document, result.Objects, id).Count == 0,
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
            Check(Math.Abs(shape.Nodes[0].X - 392) < .001, "Shared repeat endpoint could not snap to zero DS.");
            var result = CatchStreamConverter.Convert(ui.View.Document);
            Check(result.Success && SliderDistanceSnap.StrictErrors(ui.View.Document, shape, result.Objects).Count == 0,
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
