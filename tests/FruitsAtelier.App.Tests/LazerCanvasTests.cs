using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LazerCanvasTests
{
    public static void Run()
    {
        MovementSpeedLabels();
        CustomGuidesAndMovementColours();
        var ui = new Ui(overview: false);
        ui.View.LibrarySettings.LazerEditing = true;
        var map = new MapDocument { DurationMs = 4000, IsDemo = false, ApproachRate = 5, CircleSize = 5 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 2000, X = 400 }]);
        ui.View.LoadDocument(map);
        ui.Paint();
        ui.View.UpdateTransport(1000, 4000, true, false, false, null, null);
        ui.Paint();
        ui.Canvas.Clear();
        Invoke("DrawCanvasCatchObjects", ui.Canvas);
        float radius = CatchSize.FruitRadius(5) * ui.View.PlayfieldBounds.Width / 512;
        Check(ui.Canvas.Circles.Count(circle => circle.Filled && Math.Abs(circle.Radius - radius) < .001) == 1,
            "Caught canvas fruit must vanish at its timestamp, leaving only future fruit.");
        ui.Canvas.Clear();
        Invoke("DrawLazerCanvasCatcher", ui.Canvas);
        Check(ui.Canvas.Images.Any(image => image.Path.EndsWith("scoreboard-explosion-2@2x.png")),
            "Canvas catch emits the classic tall flash.");
        Check(!ui.Canvas.Circles.Any(circle => circle.Filled && Math.Abs(circle.Radius - radius / 2) < .001),
            "Canvas catcher does not draw a plate stack.");
        ui.View.LibrarySettings.CatchHitLighting = CatchHitLightingMode.Off;
        ui.Canvas.Clear(); Invoke("DrawLazerCanvasCatcher", ui.Canvas);
        Check(!ui.Canvas.Images.Any(image => image.Path.Contains("scoreboard-explosion")), "Off suppresses hit lighting.");
        ui.View.LibrarySettings.CatchHitLighting = CatchHitLightingMode.On;
        ui.View.UpdateTransport(1700, 4000, true, false, false, null, null);
        ui.Paint(); ui.Canvas.Clear(); Invoke("DrawLazerCanvasCatcher", ui.Canvas);
        Check(!ui.Canvas.Images.Any(image => image.Path.Contains("scoreboard-explosion")), "Expired flashes do not reappear.");
        ui.View.UpdateTransport(1100, 4000, true, false, false, null, null);
        ui.Paint(); ui.Canvas.Clear(); Invoke("DrawLazerCanvasCatcher", ui.Canvas);
        Check(ui.Canvas.Images.Any(image => image.Path.Contains("scoreboard-explosion")), "Backward seek reconstructs catch lighting.");

        typeof(EditorView).GetProperty("distanceSnap", Private)!.SetValue(ui.View, true);
        var source = Invoke("LazerDistanceSnapSource", 1500d, null) as DistanceSnap.Reference;
        Check(source is { End.TimeMs: 1000, End.X: 256 }, "Distance guide uses the preceding completed fruit.");
        var snap = typeof(EditorView).GetMethod("TryLazerDistanceSnap", Private)!;
        object?[] arguments = [new MapPoint(1200, 360), default(MapPoint), null, 0d, 512d];
        Check((bool)snap.Invoke(ui.View, arguments)! && Math.Abs(((MapPoint)arguments[1]!).X - 356) < .001,
            "Walk guide snaps 200 ms forward by 100 playfield units.");
        arguments = [new MapPoint(1200, 460), default(MapPoint), null, 0d, 512d];
        Check((bool)snap.Invoke(ui.View, arguments)! && Math.Abs(((MapPoint)arguments[1]!).X - 456) < .001,
            "Dash guide snaps 200 ms forward by 200 playfield units.");
        Check(Invoke("LazerDistanceSnapSource", 1000d, null) is null,
            "A guide reference must finish strictly before the target.");

        var first = new ConvertedCatchObject(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 1000, 256, 256, 256, 0);
        var overlap = first with { SourceId = Guid.NewGuid(), TimeMs = 1500, X = 0 };
        var tail = first with { EventIndex = 1, TimeMs = 2000, X = 512 };
        ConvertedCatchObject[] objects = [first, overlap, tail];
        var autoplay = new CatchAutoPreview(objects, 5, [first, tail, overlap], lazerEditor: true);
        var lighting = (CatchLightingEvent[])typeof(EditorView).GetMethod("BuildLighting", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [objects, autoplay, 5d])!;
        Check(lighting.All(entry => entry.Object != overlap), "Missed overlapping objects must not emit a canvas flash.");
        var caught = (Dictionary<(Guid, int), bool>)typeof(EditorView).GetField("lazerCaught", Private)!.GetValue(ui.View)!;
        var current = ui.View.Document.Fruits[0];
        caught[(current.Id, 0)] = false;
        var converted = new ConvertedCatchObject(current.Id, 0, CatchObjectKind.Fruit, current.TimeMs, current.X, current.X, current.X, 0);
        float opacity = (float)Invoke("LazerCanvasObjectOpacity", converted)!;
        Check(Math.Abs(opacity - .6f) < .001f, "Missed canvas fruit fades over 250 ms below the catcher.");

        var patterned = new MapDocument { DurationMs = 4000, IsDemo = false, BeatLengthMs = 500 };
        patterned.BananaShowers.Add(new BananaShower
        {
            TimeMs = 1000.75, EndTimeMs = 2200.75,
            Pattern = new BananaShowerPattern(1, 2, 1, 2)
        });
        patterned.Fruits.Add(new Fruit { TimeMs = 1300, X = 256 });
        ui.View.LoadDocument(patterned); ui.Paint();
        var traversal = (ConvertedCatchObject[])Invoke("BuildLazerAutoplayTraversal")!;
        Check(traversal.Count(item => item.Kind == CatchObjectKind.Banana) > 3,
            "Pattern fixture contains multiple bananas within each segment.");
        Check(traversal.Select(item => item.TimeMs).SequenceEqual(traversal.Select(item => item.TimeMs).Order()),
            "Patterned shower traversal groups bananas by containing segment, retaining intervening parents.");

        object? Invoke(string name, params object?[] arguments)
            => typeof(EditorView).GetMethod(name, Private)!.Invoke(ui.View, arguments);
    }

    private static void MovementSpeedLabels()
    {
        var ui = new Ui(false);
        var map = new MapDocument { DurationMs = 4000, BeatLengthMs = 500, ApproachRate = 5 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 1500, X = 350 }]);
        ui.View.LibrarySettings.LazerEditing = true;
        ui.LoadDocument(map);
        ui.View.UpdateTransport(1000, 4000, true, false, false, null, null);
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("movement.analysis"));
        string speed = FruitsAtelier.Localization.Strings.Get("ds.lazerSpeed", .5);
        Check(ui.Canvas.Texts.Any(t => t.Value == speed), "Lazer movement labels must show logical pixels per millisecond.");
        ui.View.Document.DistancePerBeatOverride = 250;
        ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == speed), "Movement speed must be independent of base SV.");
        ui.View.LibrarySettings.LazerEditing = false;
        ui.Paint();
        Check(ui.Canvas.Texts.All(t => t.Value != speed)
            && ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("assist.ratio", 1)),
            "Switching back to Mapping must restore DS ratio labels.");
    }

    private static void CustomGuidesAndMovementColours()
    {
        var ui = new Ui(overview: false);
        var map = new MapDocument { DurationMs = 4000, ApproachRate = 5, CircleSize = 5 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        ui.View.LoadDocument(map);
        ui.View.LibrarySettings.LazerEditing = true;
        ui.View.LibrarySettings.LazerDistanceCustomSpeeds = new[] { 1.3, 3d };
        ui.View.UpdateTransport(1000, 4000, true, false, false, null, null);
        ui.Paint();
        typeof(EditorView).GetProperty("distanceSnap", Private)!.SetValue(ui.View, true);
        typeof(EditorView).GetMethod("EnsureLazerGuideMovement", Private)!.Invoke(ui.View, [null]);
        Check(ui.View.LazerGuideMovementAt(1020, 126) == CatchMovementMode.Stand, "A fast guide starts within standing reach.");
        Check(ui.View.LazerGuideMovementAt(1040, 152) == CatchMovementMode.Walk, "Guide colours use catch-edge walking reach.");
        Check(ui.View.LazerGuideMovementAt(1100, 230) == CatchMovementMode.Dash, "A speed above 1 can require only a regular dash.");
        Check(ui.View.LazerGuideMovementAt(1200, 360) == CatchMovementMode.HyperDash, "The same speed becomes hyperdash later along the guide.");
        object?[] snap = [new MapPoint(1200, 362), default(MapPoint), null, 0d, 512d];
        Check((bool)typeof(EditorView).GetMethod("TryLazerDistanceSnap", Private)!.Invoke(ui.View, snap)!
            && Math.Abs(((MapPoint)snap[1]!).X - 360) < .001, "Custom speed guides participate in snapping.");
        ui.View.LibrarySettings.StandIndicatorColour = 0x102030;
        ui.View.LibrarySettings.WalkIndicatorColour = 0x406050;
        ui.View.LibrarySettings.DashIndicatorColour = 0xA08010;
        ui.View.LibrarySettings.HyperDashIndicatorColour = 0xE02050;
        ui.Canvas.Clear();
        typeof(EditorView).GetMethod("DrawLazerColouredGuide", Private)!.Invoke(ui.View, [ui.Canvas, new MapPoint(1000, 100), 1.3d, 1300d]);
        var colours = ui.Canvas.Lines.Select(p => p.Color).ToHashSet();
        uint[] categories = [0x102030, 0x406050, 0xA08010, 0xE02050];
        Check(categories.All(colours.Contains), "Guide drawing follows all four configured movement colours.");
        Check(colours.Except(categories).Any(), "Movement category boundaries contain blended transition colours.");
        ui.View.Document.CircleSize = 10;
        ui.Paint();
        typeof(EditorView).GetMethod("EnsureLazerGuideMovement", Private)!.Invoke(ui.View, [null]);
        Check(ui.View.LazerGuideMovementAt(1100, 230) == CatchMovementMode.HyperDash, "Guide context refreshes when CS changes.");
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
