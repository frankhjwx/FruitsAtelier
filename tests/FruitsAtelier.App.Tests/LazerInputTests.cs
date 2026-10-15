using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using System.Reflection;

internal static class LazerInputTests
{
    public static void NavigationAndBindings()
    {
        var ui = Create();
        var before = ui.View.Document.DeepClone();
        ui.View.UpdateTransport(1600, 10000, true, false, false, null, null);
        ui.Key(38);
        Check(ui.View.PlayheadMs == 1000, "Lazer Up seeks the previous timing point.");
        ui.Key(40);
        Check(ui.View.PlayheadMs == 2000, "Lazer Down seeks the next timing point.");
        ui.View.UpdateTransport(400, 10000, true, false, false, null, null);
        ui.Key(39, ctrl: true);
        Check(ui.View.PlayheadMs == 1200, "Ctrl+Right without selection seeks the next object.");
        ui.Key('A', ctrl: true);
        ui.Key(39, ctrl: true);
        Check(ui.View.Document.Fruits[0].X == 129, "Ctrl+Right nudges the selection by one X unit.");
        var moved = ui.View.Document.DeepClone();
        ui.Key(39, ctrl: true, shift: true);
        Check(ui.View.Document.ContentEquals(moved), "Ctrl+Shift+Right sample navigation does not perform the default grid nudge.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "Lazer movement is undoable in one transaction.");
        bool distance = ui.View.DistanceSnapEnabled;
        ui.Key('T');
        Check(ui.View.DistanceSnapEnabled != distance, "Lazer T toggles distance snapping.");
        ui.Key('Y');
        Check(ui.View.DistanceSnapEnabled == distance, "The unique Y distance-snap alias remains available.");
        ui.View.SetModifiers(true, false);
        ui.Key(39);
        ui.View.SetModifiers(false, false);
        Check(ui.View.PlayheadMs == 2500, "Alt+Right seeks a bookmark instead of selecting a volume meter.");
        ui.Key(114);
        Check(ui.View.TimingPageVisible, "Lazer F3 switches to Timing.");
        ui.Key(115);
        ui.Paint();
        Check(!ui.View.TimingPageVisible && ui.View.SongDockViewport.Width > 0, "Lazer F4 switches from Timing to Song Setup.");
    }

    public static void SeekAndDivisorFamilies()
    {
        var ui = Create();
        ui.Key('3', shift: true);
        var canvas = ui.View.PlayfieldBounds;
        ui.View.Wheel(canvas.X + canvas.Width / 2, canvas.Y + 10, -120, true);
        Check(ui.View.SnapDivisor == 1, "Lazer triplet presets include the 3 to 1 transition.");
        ui.View.Wheel(canvas.X + canvas.Width / 2, canvas.Y + 10, 120, true);
        Check(ui.View.SnapDivisor == 3, "Lazer triplet presets advance from 1 to 3.");
        ui.Key('4', shift: true);
        ui.View.UpdateTransport(601, 10000, true, false, false, null, null);
        ui.Key(39, shift: true);
        Check(ui.View.PlayheadMs == 1000, "Shift seeking aligns to the coarser four-subdivision lattice.");
        ui.View.UpdateTransport(601, 10000, true, true, false, null, null);
        ui.Key(39);
        Check(ui.View.PlayheadMs == 1101, "Playing navigation uses lazer's fixed time step.");
        ui.View.UpdateTransport(100, 10000, true, false, false, null, null);
        ui.Key('Z');
        Check(ui.View.PlayheadMs == 1200, "Z seeks the first object from before its start.");
        ui.Key('Z');
        Check(ui.View.PlayheadMs == 0, "Z at the first object seeks song start.");
    }

    public static void MouseDeletionAndContext()
    {
        var ui = Create();
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null);
        ui.Paint();
        var fruit = ui.View.Document.Fruits[0];
        var (x, y) = ((float, float))typeof(EditorView).GetMethod("Screen", Private)!
            .Invoke(ui.View, [new MapPoint(fruit.TimeMs, fruit.X)])!;
        var before = ui.View.Document.DeepClone();
        ui.View.PointerDown(x, y, 2, false, false);
        ui.View.PointerUp(x, y, 2);
        Check(ui.View.Document.ContentEquals(before), "Lazer right-click opens actions without deleting an object.");
        ui.Key(27);
        ui.View.PointerDown(x, y, 1, false, false);
        ui.View.PointerUp(x, y, 1);
        Check(ui.View.Document.ContentEquals(before), "Lazer canvas middle-click reserves horizontal panning without deleting objects.");
        ui.View.PointerDown(x, y, 2, true, false);
        ui.View.PointerUp(x, y, 2);
        Check(ui.View.Document.Fruits.Count == before.Fruits.Count - 1, "Lazer Shift-right-click quick deletes the hovered object.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "Quick deletion is undoable.");
    }

    public static void DragSnappingAndHiddenHits()
    {
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 4000, BeatLengthMs = 500, ApproachRate = 5 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 1200, X = 220 }, new Fruit { TimeMs = 1400, X = 204 }]);
        ui.LoadDocument(map);
        ui.View.LibrarySettings.LazerEditing = true;
        ui.View.UpdateTransport(1000, 4000, true, false, false, null, null);
        ui.Paint();
        Property("gridSize", 32);
        Property("gridSnap", true);
        Property("distanceSnap", true);
        var moved = ui.View.Document.Fruits[1];
        var original = ui.View.Document.DeepClone();
        Drag(204);
        Check(Math.Abs(ui.Fruit(moved.Id).X - 200) < .001,
            "Lazer whole-object drag follows the exact walk guide instead of grid or object alignment.");
        ui.Key('Z', ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Lazer snapped dragging undoes as one edit.");
        Property("distanceSnap", false);
        Drag(207);
        Check(Math.Abs(ui.Fruit(moved.Id).X - 207) < .001,
            "Lazer dragging without DS preserves raw X despite enabled FA grid and object-snap preferences.");
        ui.Key('Z', ctrl: true);
        Property("distanceSnap", true);
        var pointer = Screen(1200, 204);
        var placed = (MapPoint)Call("PlacementPoint", pointer.X, pointer.Y)!;
        Check(Math.Abs(placed.TimeMs - 1250) < .001 && Math.Abs(placed.X - 195) < .001,
            $"Lazer placement must snap to 1250 ms and the preceding 1200 ms fruit's left walk guide at X=195; got {placed}.");
        var caught = Screen(1000, 100);
        Check(Call("HitCatchObject", caught.X, caught.Y, null, false) is null,
            "An invisible caught fruit cannot be selected at the catch line.");
        var future = Screen(1200, 220);
        Check(Call("HitCatchObject", future.X, future.Y, null, false) is ConvertedCatchObject { TimeMs: 1200 },
            "A future visible fruit remains selectable.");

        void Drag(double targetX)
        {
            Call("SelectObjects", new[] { moved.Id }, moved.Id);
            var start = Screen(1200, 220);
            Call("BeginObjectDrag", start.X, start.Y, false, false);
            var end = Screen(1200, targetX);
            ui.View.PointerMove(end.X, end.Y, false, false);
            ui.View.PointerUp(end.X, end.Y, 0);
            ui.Paint();
        }
        (float X, float Y) Screen(double time, double x) => ((float, float))Call("Screen", new MapPoint(time, x))!;
        object? Call(string name, params object?[] args) => typeof(EditorView).GetMethod(name, Private)!.Invoke(ui.View, args);
        void Property(string name, object value) => typeof(EditorView).GetProperty(name, Private)!.SetValue(ui.View, value);
    }

    private static Ui Create()
    {
        var ui = new Ui();
        ui.LoadDocument(OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Editor]\nBookmarks:2500\n[TimingPoints]\n0,500,4,1,0,100,1,0\n1000,-100,4,1,0,100,0,0\n2000,400,4,1,0,100,1,0\n[HitObjects]\n128,192,1200,1,0,0:0:0:0:\n256,192,1800,1,0,0:0:0:0:\n"));
        ui.View.LibrarySettings.LazerEditing = true;
        ui.Paint();
        return ui;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
}
