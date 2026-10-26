using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class SelectedPenNodeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        var map = new MapDocument { DurationMs = 10000 };
        var track = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
        track.Nodes.AddRange([
            new() { TimeMs = 1000, X = 100, HandleOut = new(500, 156) },
            new() { TimeMs = 2000, X = 400, HandleIn = new(-500, -144), HandleOut = new(300, -50) },
            new() { TimeMs = 3000, X = 200, HandleIn = new(-300, 50) }
        ]);
        map.Tracks.Add(track);
        var ui = new Ui(); ui.LoadDocument(map); SelectControls(ui, track.Id);
        var actual = ui.View.Document.Tracks.Single();
        ui.Paint();
        CheckHandles(ui, actual);

        // Both handles coincide: the earlier node's outgoing handle owns the hit.
        var overlap = ui.ScreenAt(1500, 256);
        ui.View.PointerDown(overlap.X, overlap.Y, 0, false, false);
        CheckSelection(ui, actual.Nodes[0].Id, "HandleOut", "overlapping handles");
        ui.View.PointerUp(overlap.X, overlap.Y, 0);

        // Anchors are tested before every handle, even a handle from an earlier node.
        actual.Nodes[0].HandleOut = new(1000, 300);
        SelectControls(ui, track.Id);
        var anchor = ui.ScreenAt(2000, 400);
        ui.View.PointerDown(anchor.X, anchor.Y, 0, false, false);
        CheckSelection(ui, actual.Nodes[1].Id, "Anchor", "anchor priority");
        ui.View.PointerUp(anchor.X, anchor.Y, 0);

        actual.Nodes[0].HandleOut = new(1000, 200);
        SelectControls(ui, track.Id);
        typeof(EditorView).GetField("viewStart", Private)!.SetValue(ui.View, 1500d);
        ui.Paint();
        CheckHandles(ui, actual);
        var offscreen = ui.ScreenAt(1000, 100);
        if (offscreen.Y <= ui.View.CanvasPlotBounds.Bottom + 9)
            throw new Exception("Offscreen anchor fixture did not leave the viewport.");
        var visibleHandle = ui.ScreenAt(2000, 300);
        if (!ui.View.CanvasPlotBounds.Contains(visibleHandle.X, visibleHandle.Y))
            throw new Exception("Offscreen anchor fixture lost its visible handle.");
        ui.View.PointerDown(visibleHandle.X, visibleHandle.Y, 0, false, false);
        CheckSelection(ui, actual.Nodes[0].Id, "HandleOut", "offscreen anchor");
        ui.View.PointerUp(visibleHandle.X, visibleHandle.Y, 0);

        SelectControls(ui, track.Id);
        ui.Key('F');
        if (ui.Canvas.Lines.Any(line => line.Color == 0x625E7C && line.Opacity == 1))
            throw new Exception("Inactive controls painted Pen handles.");
        ui.Key('1');
        SelectControls(ui, track.Id);
        ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy);
        ui.Paint();
        if (ui.Canvas.Lines.Any(line => line.Color == 0x625E7C && line.Opacity == 1))
            throw new Exception("Legacy editing painted Pen handles.");
    }

    private static void CheckHandles(Ui ui, CurveTrack track)
    {
        var lines = ui.Canvas.Lines.Where(line => line.Color == 0x625E7C && line.Opacity == 1).ToArray();
        (int Index, bool Incoming)[] expected = [(0, false), (1, true), (1, false), (2, true)];
        if (lines.Length != expected.Length) throw new Exception($"Expected four Pen handle lines, got {lines.Length}.");
        for (int i = 0; i < expected.Length; i++)
        {
            var (index, incoming) = expected[i];
            var node = track.Nodes[index];
            var p = ui.ScreenAt(node.TimeMs, node.X);
            var h = ((float X, float Y))typeof(EditorView).GetMethod("PenHandleScreen", Private)!
                .Invoke(ui.View, [track, index, incoming])!;
            var line = lines[i];
            if (Math.Abs(line.X1 - p.X) > .001 || Math.Abs(line.Y1 - p.Y) > .001
                || Math.Abs(line.X2 - h.X) > .001 || Math.Abs(line.Y2 - h.Y) > .001)
                throw new Exception($"Pen handle {i} used another node's index or changed paint order.");
        }
    }

    private static void CheckSelection(Ui ui, Guid node, string part, string scenario = "last handle")
    {
        var selected = (Guid)typeof(EditorView).GetField("selection", Private)!.GetValue(ui.View)!;
        var selectedPart = typeof(EditorView).GetField("selectedPart", Private)!.GetValue(ui.View)!.ToString();
        if (selected != node || selectedPart != part)
            throw new Exception($"Pen {scenario} expected {node} / {part}, got {selected} / {selectedPart} (track {ui.View.Document.Tracks.Single().Id}).");
    }

    private static void Select(Ui ui, Guid track) => typeof(EditorView).GetMethod("SelectObjects", Private)!
        .Invoke(ui.View, [new[] { track }, track]);

    private static void SelectControls(Ui ui, Guid track) => typeof(EditorView).GetMethod("SelectAnchors", Private)!
        .Invoke(ui.View, [ui.View.Document.Tracks.Single(t => t.Id == track), Array.Empty<Guid>(), Guid.Empty]);

    internal static void RunPerformance()
    {
        foreach (int count in new[] { 512, 4096 })
        {
            var map = new MapDocument { DurationMs = 10000 };
            var track = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
            for (int i = 0; i < count; i++)
                track.Nodes.Add(new() { TimeMs = 1000 + i * 4000d / (count - 1), X = 256,
                    HandleIn = new(-.1, i == count - 1 ? 120 : -40), HandleOut = new(.1, -60) });
            map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map); Select(ui, track.Id);
            ui.Paint();
            var canvas = new CountCanvas();
            var hit = ui.ScreenAt(track.Nodes[^1].TimeMs - .1, 376);
            if (!ui.View.CanvasPlotBounds.Contains(hit.X, hit.Y)) throw new Exception("Pen benchmark hit is outside the viewport.");
            var paint = new List<double>(); var input = new List<double>();
            var paintBytes = new List<long>(); var inputBytes = new List<long>();
            for (int sample = 0; sample < 56; sample++)
            {
                Select(ui, track.Id);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                ui.View.PointerDown(hit.X, hit.Y, 0, false, false);
                double inputMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long inputAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
                CheckSelection(ui, ui.View.Document.Tracks.Single().Nodes[^1].Id, "HandleIn");
                ui.View.PointerUp(hit.X, hit.Y, 0);
                Select(ui, track.Id);
                allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                ui.View.Render(canvas, ui.Width, ui.Height);
                double paintMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long paintAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (sample < 8) continue;
                paint.Add(paintMs); input.Add(inputMs); paintBytes.Add(paintAllocation); inputBytes.Add(inputAllocation);
            }
            paint.Sort(); input.Sort(); paintBytes.Sort(); inputBytes.Sort();
            Console.WriteLine($"Selected Pen nodes={count}: render CPU p50={paint[24]:F4}ms p95={paint[45]:F4}ms allocation p50={paintBytes[24]}B; "
                + $"last-handle pointer-down CPU p50={input[24]:F4}ms p95={input[45]:F4}ms allocation p50={inputBytes[24]}B (counting canvas; native GPU excluded).");
            if (!ui.View.Document.ContentEquals(map)) throw new Exception("Pen benchmark changed its fixture.");
            ui.View.NewProject();
        }
    }

    private sealed class CountCanvas : ICanvas
    {
        public void Fill(Rect r, uint c, float radius = 0, float opacity = 1) { }
        public void Stroke(Rect r, uint c, float width = 1, float radius = 0) { }
        public void Line(float x, float y, float x2, float y2, uint c, float width = 1, float opacity = 1) { }
        public void Circle(float x, float y, float radius, uint c, bool filled = true, float width = 1, float opacity = 1) { }
        public void Text(string text, float x, float y, float size, uint c, float maxWidth = 10000, bool bold = false) { }
        public bool Image(string path, Rect r, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1) => false;
        public void Clip(Rect r) { }
        public void Unclip() { }
    }
}
