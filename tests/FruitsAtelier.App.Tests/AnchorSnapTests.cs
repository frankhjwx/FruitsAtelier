using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class AnchorSnapTests
{
    public static void Dragging()
    {
        var document = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        var track = new CurveTrack { CompensateTinyDroplets = false };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 200 }, new() { TimeMs = 3000, X = 300 }]);
        document.Tracks.Add(track);
        var ui = new Ui(); ui.LoadDocument(document);
        ui.ClickMap(2000, 200); ui.Key('B');
        if (ui.View.AnchorSnapEnabled) throw new Exception("Anchor snap must default off.");
        void Drag(double from, double to)
        {
            ui.Key('V'); ui.ClickMap(from, 200); ui.Key('B');
            ui.DownMap(from, 200); ui.MoveMap(to, 205); ui.UpMap(to, 205);
        }
        Drag(2000, 2183.25);
        Near(2183.25, ui.Anchor(track.Nodes[1].Id).TimeMs);
        ui.Key('Z', ctrl: true); Near(2000, ui.Anchor(track.Nodes[1].Id).TimeMs);
        ui.ClickText(L.Get("ui.anchorSnap"));
        if (!ui.View.AnchorSnapEnabled || ui.View.IsDirty) throw new Exception("Checkbox must enable snapping without editing the map.");
        ui.SetSnapDivisor(4); Drag(2000, 2183.25);
        Near(2125, ui.Anchor(track.Nodes[1].Id).TimeMs);
        ui.Key('Z', ctrl: true);
        ui.ClickText(L.Get("ui.free")); // Independent from fruit/whole-object snapping.
        Drag(2000, 2183.25); Near(2125, ui.Anchor(track.Nodes[1].Id).TimeMs);
        ui.Key('Z', ctrl: true); ui.ClickText(L.Get("ui.anchorSnap"));
        Drag(2000, 2183.25); Near(2183.25, ui.Anchor(track.Nodes[1].Id).TimeMs);
        ui.Key('Z', ctrl: true); ui.Key('Y', ctrl: true); Near(2183.25, ui.Anchor(track.Nodes[1].Id).TimeMs);
        ui.Key('Z', ctrl: true);
        void DragEndpoint(int index, double to, double expected)
        {
            var node = ui.Anchor(track.Nodes[index].Id);
            ui.Key('V'); ui.ClickMap(node.TimeMs, node.X); ui.Key('B');
            ui.DownMap(node.TimeMs, node.X); ui.MoveMap(to, node.X + 5); ui.UpMap(to, node.X + 5);
            Near(expected, ui.Anchor(node.Id).TimeMs);
            ui.Key('Z', ctrl: true);
        }
        // Global Free currently enabled: both endpoints remain free.
        DragEndpoint(0, 1183.25, 1183.25); DragEndpoint(2, 3183.25, 3183.25);
        ui.ClickText(L.Get("ui.free"));
        DragEndpoint(0, 1183.25, 1125); DragEndpoint(2, 3183.25, 3125);
        // A rejected snapped target cannot clamp the endpoint to an off-grid time.
        DragEndpoint(0, 2183.25, 1000);
        ui.ClickText(L.Get("ui.anchorSnap")); ui.ClickText(L.Get("ui.free"));
        DragEndpoint(0, 1183.25, 1183.25); DragEndpoint(2, 3183.25, 3183.25);
    }
    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > .002) throw new Exception($"Anchor time: expected {expected}, got {actual}");
    }
}
