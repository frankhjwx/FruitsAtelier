using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class StackDialogTests
{
    public static void Run()
    {
        SharedBreakSwitch();
        CombinedPreview();
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui();
            var map = new MapDocument(); var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            void Open() { ui.Key('F', ctrl: true, shift: true); ui.ClickText(L.Get("conversion.stackTab")); }
            Open();
            Check(ui.View.StreamDialogVisible && ui.View.Document.ContentEquals(map), "draft keeps document unchanged");
            var graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.X + graph.Width * .25f, graph.Bottom - 24f / 32 * graph.Height, 0, false, false);
            ui.View.PointerMove(graph.X + graph.Width * .3f, graph.Bottom - 48f / 32 * graph.Height, false, false);
            ui.View.PointerUp(graph.X + graph.Width * .3f, graph.Bottom - 48f / 32 * graph.Height, 0); ui.Paint();
            Check(!ui.View.WantsCapture && ui.View.Document.ContentEquals(map), "draft drag stays local");
            ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points.Any(p => p.Distance == 32), "confirm stores envelope");
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
            ui.Key(13); Check(ui.View.Document.Tracks.Single().Stack!.Points.Count == accepted.Tracks[0].Stack!.Points.Count + 1, "add interior point");
            ui.Key('A', ctrl: true); Open(); graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.X + graph.Width * .5f, graph.Bottom - graph.Height * .5f, 2, false, false);
            ui.View.PointerUp(graph.X + graph.Width * .5f, graph.Bottom - graph.Height * .5f, 2); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points.Count == accepted.Tracks[0].Stack!.Points.Count, "remove interior point");
            ui.Key('A', ctrl: true); Open(); graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
            ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height * .25f, false, false);
            ui.View.CancelInteraction(); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points[^1].Distance == 0, "lost capture restores endpoint draft");
            ui.Key('A', ctrl: true); Open(); graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
            ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height * .25f, false, false);
            ui.View.PointerUp(graph.Right, graph.Bottom - graph.Height * .25f, 0); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks.Single().Stack!.Points[^1].Distance == 8, "endpoint accepts boundary hit");
            ui.Resize(800, 600); ui.Key('A', ctrl: true); Open(); ui.Key(27);
        }
        L.SetLanguage("en");
    }
    private static void CombinedPreview()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        foreach (double ar in new[] { 1d, 5d, 9d })
        {
            L.SetLanguage(language);
            var ui = new Ui(); var map = new MapDocument { ApproachRate = ar };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 21000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            ui.Key('F', ctrl: true, shift: true);
            Check(ui.View.StreamDialogVisible && ui.View.StackGraphBounds.Width == 0, "shared shortcut defaults to Stream");
            void CheckSpacing()
            {
                var bounds = ui.View.StackPreviewBounds;
                var dots = ui.Canvas.Operations.Where(o => o.Clip == bounds && o.Dot is { Filled: false })
                    .Select(o => o.Dot!.Value).ToArray();
                double expected = 500d / ui.View.StreamSnapDivisor * CatchScrollTiming.PixelsPerMs(ar, bounds.Width);
                Check(dots.Length >= 2 && Math.Abs(dots[0].Y - dots[1].Y - expected) < .001, "preview spacing follows map AR");
            }
            CheckSpacing();
            var streamPreview = ui.View.StackPreviewBounds;
            ui.ClickText(L.Get("conversion.stackTab")); CheckSpacing();
            var graph = ui.View.StackGraphBounds;
            Check(ui.View.StackPreviewBounds == streamPreview && graph.Width == streamPreview.Width
                && ui.View.StreamSnapBounds.Right < streamPreview.X
                && ui.View.StackDistanceFieldBounds.Right < streamPreview.X, "tabs share equal columns with controls on the left");
            ui.Click(graph.X + graph.Width * .5f, graph.Bottom - graph.Height * .5f);
            ui.ClickText(L.Get("conversion.streamTab")); CheckSpacing();
            Check(ui.View.StackGraphBounds.Width == 0 && ui.View.Document.ContentEquals(map), "Stream preview keeps curve draft local");
            ui.ClickText(L.Get("conversion.stackTab"));
            var preview = ui.View.StackPreviewBounds;
            float[] Positions() => ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot is { Filled: false })
                .Select(o => o.Dot!.Value.Y).ToArray();
            var before = Positions();
            foreach (bool reverse in new[] { false, true })
            {
                ui.View.LibrarySettings.ReverseCanvasScroll = reverse;
                ui.View.Wheel(preview.X + 20, preview.Y + 20, 120, false); ui.Paint();
                Check(!Positions().SequenceEqual(before) && ui.View.Document.ContentEquals(map), "wheel up views later times independently of canvas settings");
                ui.View.Wheel(preview.X + 20, preview.Y + 20, -120000, false); ui.Paint();
                Check(Positions().SequenceEqual(before), "wheel down clamps at the start");
            }
            ui.View.Wheel(preview.X + 20, preview.Y + 20, 120000, false); ui.Paint();
            var last = ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot is { Filled: false })
                .Select(o => o.Dot!.Value).Last();
            Check(Math.Abs(last.Y - last.Radius - preview.Y - 2) < .01, "scroll reaches the final fruit");
            ui.View.PointerDown(last.X, last.Y, 0, false, false);
            ui.View.PointerMove(last.X + 5, last.Y, false, false);
            ui.View.PointerUp(last.X + 5, last.Y, 0); ui.Paint(); ui.Key(13);
            Check(ui.View.Document.Tracks[0].Stack!.Points.Count == 5
                && ui.View.Document.Tracks[0].Stack!.FruitAdjustments.Count == 1, "tab switching keeps curve edits and scrolled fruit drag");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "combined confirmation is one undo step");
            ui.Key('Y', ctrl: true); ui.Key('A', ctrl: true); ui.Key('F', ctrl: true, shift: true); ui.Key(13);
            Check(ui.View.Document.Tracks[0].Stack is null, "default Stream confirmation converts an existing stack to Stream");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.Tracks[0].Stack is not null, "Stack to Stream undo retains envelope");
        }
    }
    private static void SharedBreakSwitch()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        foreach (bool stack in new[] { false, true })
        {
            L.SetLanguage(language); var ui = new Ui(); var map = new MapDocument();
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4, Stack = new() };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("conversion.editMenu"));
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("conversion.editTitle")), "existing streams and stacks use Edit Stream/Stack");
            ui.ClickText(L.Get("stream.breakFruits"));
            ui.ClickText(L.Get("conversion.stackTab"));
            Check(ui.View.StreamBreakIntoFruits, "Stack shares the enabled break switch");
            ui.ClickText(L.Get("stream.breakFruits"));
            ui.ClickText(L.Get("conversion.streamTab"));
            Check(!ui.View.StreamBreakIntoFruits, "Stream shares the disabled break switch");
            if (stack) ui.ClickText(L.Get("conversion.stackTab"));
            ui.ClickText(L.Get("stream.breakFruits"));
            var expectedTrack = track.DeepClone(); expectedTrack.Stack = stack ? track.Stack : null;
            var expected = SliderFruitStream.Convert(map, expectedTrack).Select(f => (f.TimeMs, f.X)).ToArray();
            ui.Key(13);
            Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.Fruits.Select(f => (f.TimeMs, f.X)).SequenceEqual(expected),
                "both modes break into fruits at their generated positions");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "breaking a stack or stream has one undo step");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.Tracks.Count == 0, "breaking supports redo");
        }
    }
    public static void ManualFruits()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(); var map = new MapDocument { ApproachRate = 5 };
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4, Stack = new() };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            void Open() { ui.Key('A', ctrl: true); ui.Key('F', ctrl: true, shift: true); ui.ClickText(L.Get("conversion.stackTab")); }
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
            float graphY = graph.Bottom - 8f / 32 * graph.Height;
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
            graphX = graph.X + graph.Width * .25f; graphY = graph.Bottom - 8f / 32 * graph.Height;
            ui.View.PointerDown(graphX, graphY, 0, false, false);
            ui.View.PointerMove(graphX + 12, graph.Bottom - 16f / 32 * graph.Height, false, false);
            ui.View.PointerUp(graphX + 12, graph.Bottom - 16f / 32 * graph.Height, 0); ui.Paint(); ui.Key(13);
            Check(Math.Abs(ui.View.Conversion.Objects[2].X - 272) < .001 && ui.View.Conversion.Objects[2].TimeMs == 1250,
                "left manual control changes the fruit distance at its fixed time");
        }
        L.SetLanguage("en");
    }
    public static void Numeric()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language); var ui = new Ui(); var map = new MapDocument();
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            void Open() { ui.Key('A', ctrl: true); ui.Key('F', ctrl: true, shift: true); ui.ClickText(L.Get("conversion.stackTab")); }
            void EnterValue(bool percent, string value)
            {
                var bounds = percent ? ui.View.StackPercentFieldBounds : ui.View.StackDistanceFieldBounds;
                ui.Click(bounds.X + 10, bounds.Y + 12); ui.Key('A', ctrl: true); ui.Type(value); ui.Key(13); ui.Paint();
            }
            Open(); Check(ui.View.StreamSnapDivisor == 16, "new stack defaults to sixteenths");
            var graph = ui.View.StackGraphBounds;
            ui.Click(graph.X + graph.Width * .02f, graph.Bottom - graph.Height * 24 / 32);
            EnterValue(true, "20.5"); EnterValue(false, "26.25");
            Check(ui.View.Document.ContentEquals(map), "numeric input stays local"); ui.Key(13);
            var saved = ui.View.Document.DeepClone();
            Check(saved.Tracks[0].Stack!.Points.Any(p => Math.Abs(p.Progress - .205) < 1e-8 && p.Distance == 26.25), "exact percent and pixel input");
            Check(saved.Tracks[0].StreamSnapDivisor == 16, "snap persists");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "numeric undo");
            ui.Key('Y', ctrl: true); Open(); Check(ui.View.StreamSnapDivisor == 16, "existing snap retained");
            graph = ui.View.StackGraphBounds; ui.Click(graph.X + graph.Width * .205f, graph.Bottom - graph.Height * 26.25f / 32);
            EnterValue(true, "100"); Check(ui.View.IsEditingText, "out-of-order time rejected"); ui.Key(27);
            EnterValue(false, "33"); Check(ui.View.IsEditingText, "out-of-range width rejected"); ui.Key(27);
            ui.Key(13);
            Check(ui.View.Document.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document))), "numeric persistence");
            Open(); graph = ui.View.StackGraphBounds;
            Check(ui.Canvas.Lines.Count(line => Math.Abs(line.X1 - graph.X) < .01 && Math.Abs(line.X2 - graph.Right) < .01
                && line.Y1 == line.Y2 && line.Y1 >= graph.Y && line.Y1 <= graph.Bottom) == 33, "32 grid divisions");
            ui.Click(graph.X + graph.Width * .6f, graph.Bottom - graph.Height * 8.4f / 32); ui.Key(13);
            Check(ui.View.Document.Tracks[0].Stack!.Points.Any(p => Math.Abs(p.Progress - .6) < .001 && p.Distance == 8), "whole-pixel mouse snap");
        }
        L.SetLanguage("en");
    }
    public static void DraftHistory()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language); var ui = new Ui(); var map = new MapDocument();
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4, Stack = new() };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id);
            ui.Key('F', ctrl: true, shift: true); ui.ClickText(L.Get("conversion.stackTab"));
            float FruitX() => ui.Canvas.Operations.Where(o => o.Clip == ui.View.StackPreviewBounds && o.Dot is { Filled: false })
                .Select(o => o.Dot!.Value).ElementAt(2).X;
            var dot = ui.Canvas.Operations.Where(o => o.Clip == ui.View.StackPreviewBounds && o.Dot is { Filled: false })
                .Select(o => o.Dot!.Value).ElementAt(2);
            var preview = ui.View.StackPreviewBounds;
            ui.View.PointerDown(dot.X, dot.Y, 0, false, false);
            ui.View.PointerMove(dot.X + preview.Width * 32 / 512, dot.Y, false, false);
            ui.View.PointerUp(dot.X + preview.Width * 32 / 512, dot.Y, 0); ui.Paint();
            float moved = FruitX(); Check(moved > dot.X + 1, "fruit draft moves");
            ui.Key('Z', ctrl: true); Check(Math.Abs(FruitX() - dot.X) < .01, "local fruit undo");
            ui.Key('Y', ctrl: true); Check(Math.Abs(FruitX() - moved) < .01, "local fruit redo");
            var graph = ui.View.StackGraphBounds; float gx = graph.X + graph.Width * .25f, gy = graph.Bottom - graph.Height * 8 / 32;
            ui.View.PointerDown(gx, gy, 2, false, false); ui.View.PointerUp(gx, gy, 2); ui.Paint();
            Check(Math.Abs(FruitX() - dot.X) < .01, "remove manual knot recomputes fruit from envelope");
            ui.Key('Z', ctrl: true); Check(Math.Abs(FruitX() - moved) < .01, "removal undo");
            ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); Check(Math.Abs(FruitX() - dot.X) < .01 && ui.View.Document.ContentEquals(map), "draft undo stays local at boundary");
            ui.Key('Z', ctrl: true, shift: true); Check(Math.Abs(FruitX() - moved) < .01, "shift-Z redo");
            ui.Key(13); Check(ui.View.Document.Tracks[0].Stack!.FruitAdjustments.Count == 1, "confirm current history state");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "confirmed edits form one document undo");
        }
        L.SetLanguage("en");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
