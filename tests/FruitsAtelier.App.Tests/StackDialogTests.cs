using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class StackDialogTests
{
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui();
            var map = new MapDocument(); var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            void Open() { ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("stack.menu")); }
            Open();
            Check(ui.View.StreamDialogVisible && ui.View.Document.ContentEquals(map), "draft keeps document unchanged");
            var graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.X + graph.Width * .25f, graph.Bottom - 24f / 128 * graph.Height, 0, false, false);
            ui.View.PointerMove(graph.X + graph.Width * .3f, graph.Bottom - 48f / 128 * graph.Height, false, false);
            ui.View.PointerUp(graph.X + graph.Width * .3f, graph.Bottom - 48f / 128 * graph.Height, 0); ui.Paint();
            Check(!ui.View.WantsCapture && ui.View.Document.ContentEquals(map), "draft drag stays local");
            ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points.Any(p => p.Distance == 48), "confirm stores envelope");
            var accepted = ui.View.Document.DeepClone();
            var beforeMirror = ui.View.Conversion.Objects.Select(o => o.X).ToArray();
            ui.Key('H', ctrl: true);
            Check(ui.View.Conversion.Objects.Select(o => o.X).Zip(beforeMirror).All(p => Math.Abs(p.First - (512 - p.Second)) < .001), "mirror flips centre and starting side");
            ui.Key('Z', ctrl: true);
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "one undo restores slider");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(accepted), "redo restores stack");
            ui.Key('A', ctrl: true); Open(); ui.Key(27);
            Check(ui.View.Document.ContentEquals(accepted), "cancel keeps stack");
            ui.Key('A', ctrl: true); Open();
            graph = ui.View.StackGraphBounds;
            ui.Click(graph.X + graph.Width * .5f, graph.Bottom - graph.Height * .5f);
            ui.Key(13); Check(ui.View.Document.Tracks.Single().Stack!.Points.Count == 5, "add interior point");
            ui.Key('A', ctrl: true); Open(); graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.X + graph.Width * .5f, graph.Bottom - graph.Height * .5f, 2, false, false);
            ui.View.PointerUp(graph.X + graph.Width * .5f, graph.Bottom - graph.Height * .5f, 2); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points.Count == 4, "remove interior point");
            ui.Key('A', ctrl: true); Open(); graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
            ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height * .25f, false, false);
            ui.View.CancelInteraction(); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points[^1].Distance == 0, "lost capture restores endpoint draft");
            ui.Key('A', ctrl: true); Open(); graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
            ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height * .25f, false, false);
            ui.View.PointerUp(graph.Right, graph.Bottom - graph.Height * .25f, 0); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points[^1].Distance == 32, "endpoint accepts boundary hit");
            ui.Resize(800, 600); ui.Key('A', ctrl: true); Open(); ui.Key(27);
        }
        L.SetLanguage("en");
    }
    public static void ManualFruits()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(); var map = new MapDocument();
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4, Stack = new() };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            void Open() { ui.Key('A', ctrl: true); ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("stack.menu")); }
            Open();
            var bounds = ui.View.StackPreviewBounds;
            var dots = ui.Canvas.Operations.Where(o => o.Clip == bounds && o.Dot is { Filled: false }).Select(o => o.Dot!.Value).ToArray();
            Check(dots.Length == 9 && dots.All(d => d.Y - d.Radius >= bounds.Y + .75f && d.Y + d.Radius <= bounds.Bottom - .75f), "complete first and last fruit outlines");
            var selected = dots[2]; float movedX = selected.X + bounds.Width * 32 / 512;
            ui.View.PointerDown(selected.X, selected.Y, 0, false, false);
            Check(ui.View.WantsCapture, "fruit drag captures pointer");
            ui.View.PointerMove(movedX, selected.Y - 30, false, false);
            ui.View.PointerUp(movedX, selected.Y - 30, 0); ui.Paint();
            Check(ui.View.Document.ContentEquals(map), "fruit draft remains local");
            var graph = ui.View.StackGraphBounds;
            float graphX = graph.X + graph.Width * .25f;
            float graphY = graph.Bottom - 8f / 128 * graph.Height;
            Check(ui.Canvas.Circles.Any(d => d.Filled && Math.Abs(d.X - graphX) < .01 && Math.Abs(d.Y - graphY) < .01), "fruit movement updates the left distance control point");
            Check(ui.Canvas.Lines.Any(l => Math.Abs(l.X2 - graphX) < .01 && Math.Abs(l.Y2 - graphY) < .01), "left curve passes through the adjusted fruit distance");
            ui.Key(13);
            var adjusted = ui.View.Document.DeepClone();
            var before = SliderFruitStream.Convert(map, track); var after = ui.View.Conversion.Objects;
            Check(Math.Abs(after[2].X - before[2].X - 32) < .001 && after[2].TimeMs == before[2].TimeMs,
                "fruit moves horizontally while time stays fixed");
            Check(after.Where((f, i) => i != 2).Select(f => (f.TimeMs, f.X)).SequenceEqual(before.Where((f, i) => i != 2).Select(f => (f.TimeMs, f.X))), "neighbours stay fixed");
            Check(adjusted.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(adjusted))), "fruit adjustment persists");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "fruit undo");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(adjusted), "fruit redo");
            Open(); bounds = ui.View.StackPreviewBounds;
            selected = ui.Canvas.Operations.Where(o => o.Clip == bounds && o.Dot is { Filled: false }).Select(o => o.Dot!.Value).ElementAt(2);
            ui.Click(selected.X, selected.Y); ui.Key(13);
            Check(ui.View.Document.ContentEquals(adjusted), "click without moving is a no-op");
            Open(); bounds = ui.View.StackPreviewBounds;
            selected = ui.Canvas.Operations.Where(o => o.Clip == bounds && o.Dot is { Filled: false }).Select(o => o.Dot!.Value).ElementAt(2);
            ui.View.PointerDown(selected.X, selected.Y, 0, false, false);
            ui.View.PointerMove(selected.X + 15, selected.Y, false, false);
            ui.View.CancelInteraction(); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.ContentEquals(adjusted), "lost capture restores manual adjustment");
            Open(); graph = ui.View.StackGraphBounds;
            graphX = graph.X + graph.Width * .25f; graphY = graph.Bottom - 8f / 128 * graph.Height;
            ui.View.PointerDown(graphX, graphY, 0, false, false);
            ui.View.PointerMove(graphX + 12, graph.Bottom - 16f / 128 * graph.Height, false, false);
            ui.View.PointerUp(graphX + 12, graph.Bottom - 16f / 128 * graph.Height, 0); ui.Paint(); ui.Key(13);
            Check(Math.Abs(ui.View.Conversion.Objects[2].X - 272) < .001 && ui.View.Conversion.Objects[2].TimeMs == 1250,
                "left manual control changes the fruit distance at its fixed time");
        }
        L.SetLanguage("en");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
