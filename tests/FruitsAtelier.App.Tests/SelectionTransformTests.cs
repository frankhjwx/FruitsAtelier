using FruitsAtelier.Core;

internal static class SelectionTransformTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 12000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        map.Fruits.Add(new() { TimeMs = 2000, X = 300 });
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('1');
        ui.ClickMap(1000, 100);
        Check(ui.View.SelectionTransformBounds.Width == 0, "Single fruit has a box.");
        ui.ClickMap(2000, 300, ctrl: true);
        Check(ui.View.SelectionTransformBounds.Width > 0, "Consecutive fruits have no box.");
        float originalWidth = ui.View.SelectionTransformBounds.Width;
        ui.SetCs("7");
        Check(ui.View.SelectionTransformBounds.Width < originalWidth, "Circle size did not refresh visual bounds.");
        ui.SetCs("5");
        var before = ui.View.Document.DeepClone();
        Scale(ui, 1, 100);
        Near(100, ui.View.Document.Fruits[0].X); Near(400, ui.View.Document.Fruits[1].X);
        Near(1000, ui.View.Document.Fruits[0].TimeMs); Near(2000, ui.View.Document.Fruits[1].TimeMs);
        var scaled = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Scale undo differs.");
        ui.Key('Y', ctrl: true); Check(scaled.ContentEquals(ui.View.Document), "Scale redo differs.");
        ui.ClickMap(1000, 100); ui.ClickMap(2000, 400, ctrl: true);
        var box = ui.View.SelectionTransformBounds;
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
        float endX = x + ui.Plot.Width * 20 / 512, endY = y - (float)(ui.View.PixelsPerMs * 125);
        ui.View.PointerDown(x, y, 0, false, false);
        ui.View.PointerMove(endX, endY, false, false); ui.View.PointerUp(endX, endY, 0); ui.Paint();
        Near(120, ui.View.Document.Fruits[0].X); Near(420, ui.View.Document.Fruits[1].X);
        Near(1125, ui.View.Document.Fruits[0].TimeMs); Near(2125, ui.View.Document.Fruits[1].TimeMs);
        var cancelBefore = ui.View.Document.DeepClone();
        Scale(ui, -1, -30, cancel: true);
        Check(cancelBefore.ContentEquals(ui.View.Document), "Cancelled scale changed objects.");
        foreach (bool banana in new[] { false, true })
        {
            var blocked = map.DeepClone();
            if (banana) blocked.BananaShowers.Add(new() { TimeMs = 1500, EndTimeMs = 1700 });
            else blocked.Fruits.Add(new() { TimeMs = 1500, X = 450 });
            ui.LoadDocument(blocked); ui.Key('1'); ui.ClickMap(1000, 100); ui.ClickMap(2000, 300, ctrl: true);
            Check(ui.View.SelectionTransformBounds.Width == 0, "Selection crosses an unselected parent.");
        }
        var trackMap = new MapDocument { DurationMs = 12000 };
        var track = new CurveTrack { Kind = CurveKind.Bezier, SpanCount = 2, CompensateTinyDroplets = true };
        track.Nodes.Add(new() { TimeMs = 1000, X = 100, HandleOut = new(300, 400) });
        track.Nodes.Add(new() { TimeMs = 2000, X = 300, HandleIn = new(-300, -50) });
        trackMap.Tracks.Add(track);
        ui.LoadDocument(trackMap); ui.SelectTrack(track.Id);
        var events = OsuBeatmapWriter.Serialize(trackMap).PlayableObjects.ToArray();
        var bounds = ui.View.SelectionTransformBounds;
        double Radius(ConvertedCatchObject item) => item.Kind switch
        {
            CatchObjectKind.Fruit => CatchSize.FruitRadius(trackMap.CircleSize),
            CatchObjectKind.Droplet => CatchSize.DefaultDropletRadius(trackMap.CircleSize),
            _ => CatchSize.DefaultTinyDropletRadius(trackMap.CircleSize)
        };
        Near(ui.ScreenAt(1000, events.Min(o => o.X - Radius(o))).X, bounds.X);
        Near(ui.ScreenAt(1000, events.Max(o => o.X + Radius(o))).X, bounds.Right);
        Near(events.Min(o => ui.ScreenAt(o.TimeMs, o.X).Y - Radius(o) * ui.Plot.Width / 512), bounds.Y);
        Near(events.Max(o => ui.ScreenAt(o.TimeMs, o.X).Y + Radius(o) * ui.Plot.Width / 512), bounds.Bottom);
        Check(bounds.Right < ui.ScreenAt(1000, 500).X, "Control handle enlarged the box.");
        var trackBefore = ui.View.Document.DeepClone();
        double left = events.Min(o => o.X), right = events.Max(o => o.X);
        Scale(ui, 1, (right - left) / 2);
        var changed = ui.View.Document.Tracks.Single();
        Near(1000, changed.Nodes[0].TimeMs); Near(2000, changed.Nodes[^1].TimeMs);
        Near(600, changed.Nodes[0].HandleOut.X); Check(changed.SpanCount == 2, "Scaling changed repeats.");
        ui.Key('Z', ctrl: true); Check(trackBefore.ContentEquals(ui.View.Document), "Slider scale undo differs.");
        ui.SelectTrack(track.Id); ui.DownMap(1000, 100); ui.MoveMap(1000, 110); ui.UpMap(1000, 110);
        Check(ui.View.SelectedAnchorIds.Count == 1, "Box stole anchor editing.");
        Near(300, ui.View.Document.Tracks.Single().Nodes[^1].X);
        SpecialSliders();
        TinyDropletBounds();
        DropletPriority();
        SelectedEndpointPriority();
        SelectionRectToggle();
    }

    private static void SelectionRectToggle()
    {
        string language = FruitsAtelier.Localization.Strings.Language;
        try
        {
            foreach (string locale in FruitsAtelier.Localization.Strings.AvailableLanguages)
            foreach (int width in new[] { 1100, 1440 })
            {
                FruitsAtelier.Localization.Strings.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 12000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
                var ui = new Ui(); ui.LoadDocument(map); ui.Resize(width, ui.Height); ui.Key('1'); ui.Key('A', ctrl: true);
                var bounds = ui.View.SelectionTransformBounds;
                Check(bounds.Width > 0, "Selection rectangle must default to enabled.");
                var original = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                var selected = ui.View.SelectedObjectIds.ToArray();
                var path = FirstLine("ui.sliderPathCurves");
                var toggle = FirstLine("ui.selectionRect");
                var movement = FirstLine("movement.analysis");
                Check(toggle.X > path.X && toggle.X < movement.X, "Selection rectangle toolbar order differs.");
                Check(path.MaxWidth == toggle.MaxWidth && toggle.MaxWidth == movement.MaxWidth, "Canvas toggles have unequal widths.");
                foreach (var (key, first) in new[] { ("ui.sliderPathCurves", path), ("ui.selectionRect", toggle), ("movement.analysis", movement) })
                {
                    var lines = ui.Canvas.Texts.Where(t => t.X == first.X && t.Y >= 88 && t.Y < 117 && t.Value.Length > 0).ToArray();
                    Check(lines.All(t => ((FruitsAtelier.App.Rendering.ICanvas)ui.Canvas).MeasureText(t.Value, t.Size, true) <= t.MaxWidth),
                        $"{locale} toolbar label is clipped at {width} px.");
                    Check(lines.All(t => t.Y + t.Size * 1.35f <= 117), "Two-line label exceeds its button height.");
                    Check(lines.All(t => t.Size >= 10), "Toolbar label became too small to read.");
                    string displayed = string.Concat(lines.Select(t => t.Value));
                    Check(displayed.Replace(" ", "").Replace("\u200B", "") == FruitsAtelier.Localization.Strings.Get(key).Replace(" ", "").Replace("\u200B", ""),
                        $"{locale} toolbar label lost text at {width} px.");
                }
                RecordingCanvas.Label FirstLine(string key)
                {
                    string value = FruitsAtelier.Localization.Strings.Get(key);
                    return ui.Canvas.Texts.First(t => t.Value.Length > 0 && value.StartsWith(t.Value, StringComparison.Ordinal) && t.Y >= 88 && t.Y < 117);
                }
                ui.Click(toggle.X + 1, toggle.Y + 2);
                Check(ui.View.SelectionTransformBounds.Width == 0 && !ui.Canvas.Outlines.Any(o => o.Color == 0x35ABC9),
                    "Disabled selection rectangle is still drawn.");
                ui.View.PointerMove(bounds.Right, bounds.Y + bounds.Height / 2, false, false);
                Check(!ui.View.SelectionScaleCursor, "Hidden rectangle still exposes a scale cursor.");
                ui.Click(toggle.X + 1, toggle.Y + 2);
                Check(ui.View.SelectionTransformBounds == bounds, "Reenabled rectangle changed its bounds.");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty
                    && selected.ToHashSet().SetEquals(ui.View.SelectedObjectIds), "Display toggle changed document or selection.");
                ui.Key('Z', ctrl: true);
                Check(original.ContentEquals(ui.View.Document), "Display toggle entered content history.");
            }
        }
        finally { FruitsAtelier.Localization.Strings.SetLanguage(language); }
    }

    private static void SelectedEndpointPriority()
    {
        foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>())
        foreach (bool imported in new[] { false, true })
        foreach (bool hidden in new[] { false, true })
        foreach (bool tail in new[] { false, true })
        foreach (bool vertical in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 12000 };
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 240 };
                slider.ControlPoints.AddRange([new(120, 192), new(360, 192)]);
                map.ImportedSliders.Add(slider);
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 120 }, new() { TimeMs = 3000, X = 360 }]);
                map.Tracks.Add(track);
            }
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Key('1');
            if (hidden) ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.sliderPathCurves"));
            var fruits = OsuBeatmapWriter.Serialize(map).PlayableObjects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            var target = tail ? fruits[^1] : fruits[0];
            var authoredFruits = CatchStreamConverter.Convert(map).Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            var authoredTarget = tail ? authoredFruits[^1] : authoredFruits[0];
            var opposite = tail ? authoredFruits[0] : authoredFruits[^1];
            double pointerX = target.X + (tail ? -1 : 1) * CatchSize.FruitRadius(map.CircleSize) * .7;
            ui.ClickMap(target.TimeMs, pointerX); ui.ClickMap(target.TimeMs, pointerX);
            var p = ui.ScreenAt(target.TimeMs, target.X);
            Check(ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0xE7EBF2
                && Math.Abs(c.X - p.X) < 1 && Math.Abs(c.Y - p.Y) < 1), "Endpoint child was not selected inside its box.");
            var before = ui.View.Document.DeepClone();
            double wantedTime = target.TimeMs + (vertical ? 125 : 0);
            ui.DownMap(target.TimeMs, pointerX); ui.MoveMap(wantedTime, pointerX + 10); ui.UpMap(wantedTime, pointerX + 10);
            var moved = ui.View.Document.DeepClone();
            var output = CatchStreamConverter.Convert(moved);
            Check(output.Success, "Selected endpoint drag produced invalid geometry.");
            var movedFruits = output.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            var current = tail ? movedFruits[^1] : movedFruits[0];
            var fixedEnd = tail ? movedFruits[0] : movedFruits[^1];
            Near(opposite.TimeMs, fixedEnd.TimeMs); Near(opposite.X, fixedEnd.X);
            Near(wantedTime, current.TimeMs);
            // Horizontal child editing follows the displayed X; endpoint time editing starts from the authored anchor.
            Near((vertical ? authoredTarget.X : target.X) + 10, current.X);
            ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Selected endpoint drag undo differs.");
            ui.Key('Y', ctrl: true); Check(moved.ContentEquals(ui.View.Document), "Selected endpoint drag redo differs.");
        }
    }

    private static void DropletPriority()
    {
        foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>())
        foreach (bool imported in new[] { false, true })
        foreach (bool hidden in new[] { false, true })
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 12000 };
            Guid source;
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 240 };
                slider.ControlPoints.AddRange([new(120, 192), new(360, 192)]);
                map.ImportedSliders.Add(slider);
                source = slider.Id;
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 120 }, new() { TimeMs = 3000, X = 360 }]);
                map.Tracks.Add(track);
                source = track.Id;
            }
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Key('1');
            if (hidden) ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.sliderPathCurves"));
            var original = ui.View.Document.DeepClone();
            var baseline = ui.View.Conversion.Objects.Where(o => o.SourceId == source).ToArray();
            var displayed = OsuBeatmapWriter.Serialize(map).PlayableObjects;
            var target = displayed.Where(o => o.Kind == kind).OrderBy(o => Math.Abs(o.TimeMs - 1500)).First();
            ui.ClickMap(target.TimeMs, target.X);
            var point = ui.ScreenAt(target.TimeMs, target.X);
            Check(ui.View.SelectionTransformBounds.Contains(point.X, point.Y), "Droplet is outside the selection box.");
            ui.ClickMap(target.TimeMs, target.X);
            Check(ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0xE7EBF2
                && Math.Abs(c.X - point.X) < 1 && Math.Abs(c.Y - point.Y) < 1), "Box blocked droplet child selection.");
            ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, target.X + 10); ui.UpMap(target.TimeMs, target.X + 10);
            var after = CatchStreamConverter.Convert(ui.View.Document);
            Check(after.Success, "Droplet drag produced invalid geometry.");
            foreach (var old in baseline)
            {
                var current = after.Objects.Single(o => o.EventIndex == old.EventIndex);
                Near(old.TimeMs, current.TimeMs);
                if (imported && old.Kind == CatchObjectKind.TinyDroplet && old.EventIndex != target.EventIndex) continue;
                double expected = old.EventIndex == target.EventIndex ? target.X + 10 : old.X;
                Check(Math.Abs(expected - current.X) < .02,
                    $"{mode}, imported={imported}, hidden={hidden}, {kind} #{target.EventIndex}: event #{old.EventIndex} expected X {expected}, got {current.X}.");
            }
            var moved = ui.View.Document.DeepClone();
            Check(!original.ContentEquals(moved), "Droplet drag did not edit the slider.");
            var movedTarget = OsuBeatmapWriter.Serialize(moved).PlayableObjects.Single(o => o.EventIndex == target.EventIndex);
            ui.DownMap(movedTarget.TimeMs, movedTarget.X); ui.MoveMap(movedTarget.TimeMs, movedTarget.X + 5);
            Check(ui.View.SelectedAnchorIds.Count == 0, "A new curve anchor stole the selected droplet's next drag.");
            ui.Key(27); ui.UpMap(movedTarget.TimeMs, movedTarget.X + 5);
            Check(moved.ContentEquals(ui.View.Document), "Cancelling a droplet drag changed the accepted geometry.");
            ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "Droplet drag undo differs.");
            ui.Key('Y', ctrl: true); Check(moved.ContentEquals(ui.View.Document), "Droplet drag redo differs.");
        }
    }

    private static void TinyDropletBounds()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nCircleSize:6\nSliderMultiplier:3.2\nSliderTickRate:1\n[TimingPoints]\n0,1000,4,1,0,100,1,0\n[HitObjects]\n448,88,1000,2,0,P|504:144|416:200,1,192\n");
        map.DurationMs = 12000;
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('1'); ui.Key('A', ctrl: true);
        var events = OsuBeatmapWriter.Serialize(map).PlayableObjects;
        Check(events.Any(o => o.Kind == CatchObjectKind.TinyDroplet) && !events.Any(o => o.Kind == CatchObjectKind.Droplet),
            "Fixture must contain small droplets without regular ticks.");
        var box = ui.View.SelectionTransformBounds;
        double Radius(ConvertedCatchObject item) => item.Kind == CatchObjectKind.Fruit
            ? CatchSize.FruitRadius(map.CircleSize) : CatchSize.DefaultTinyDropletRadius(map.CircleSize);
        double left = events.Min(o => o.X - Radius(o)), right = events.Max(o => o.X + Radius(o));
        Check(right > events.Where(o => o.Kind == CatchObjectKind.Fruit).Max(o => o.X + Radius(o)),
            "Small droplets must extend beyond the endpoint circles.");
        Near(ui.ScreenAt(1000, left).X, box.X); Near(ui.ScreenAt(1000, right).X, box.Right);
        Near(events.Min(o => ui.ScreenAt(o.TimeMs, o.X).Y - Radius(o) * ui.Plot.Width / 512), box.Y);
        Near(events.Max(o => ui.ScreenAt(o.TimeMs, o.X).Y + Radius(o) * ui.Plot.Width / 512), box.Bottom);
        Scale(ui, 1, -10);
        ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Small-droplet box scale undo differs.");
    }

    private static void SpecialSliders()
    {
        var arcMap = new MapDocument { DurationMs = 12000 };
        var arc = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
        arc.Nodes.Add(new() { TimeMs = 1000, X = 200, OutgoingCurve = new()
            { Kind = ControlCurveKind.CircularArc, ReferenceScale = ControlCurveMath.ReferenceScale(8) } });
        arc.Nodes.Add(new() { TimeMs = 1500, X = 200 });
        arc.Nodes[0].OutgoingCurve!.Controls.Add(new() { Offset = new(250, 50) });
        arcMap.Tracks.Add(arc);
        var ui = new Ui(); ui.LoadDocument(arcMap); ui.SelectTrack(arc.Id);
        Scale(ui, 1, 20);
        Check(ui.View.Document.Tracks.Single().Nodes.All(n => n.OutgoingCurve?.Kind != ControlCurveKind.CircularArc), "Resized arc stayed circular.");
        Check(CatchStreamConverter.Convert(ui.View.Document).Objects.SequenceEqual(ui.View.Conversion.Objects), "Scaled preview differs from uncached output.");
        ui.Key('Z', ctrl: true); Check(arcMap.ContentEquals(ui.View.Document), "Arc undo lost exact geometry.");

        var legacyMap = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        legacyMap.DurationMs = 12000;
        ui.LoadDocument(legacyMap); ui.Key('1'); ui.Key('A', ctrl: true);
        Scale(ui, -1, -20, cancel: true);
        Check(legacyMap.ContentEquals(ui.View.Document), "Cancelled Legacy scale retained conversion.");
        Scale(ui, -1, -20);
        Check(ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Count == 1, "Legacy scale did not convert.");
        Near(1000, ui.View.Document.Tracks.Single().Nodes[0].TimeMs);
        Near(2000, ui.View.Document.Tracks.Single().Nodes[^1].TimeMs);
        ui.Key('Z', ctrl: true); Check(legacyMap.ContentEquals(ui.View.Document), "Legacy scale undo is not atomic.");

        var narrow = new MapDocument { DurationMs = 12000 };
        narrow.Fruits.AddRange([new() { TimeMs = 1000, X = 200 }, new() { TimeMs = 2000, X = 200 }]);
        ui.LoadDocument(narrow); ui.Key('1'); ui.Key('A', ctrl: true);
        Scale(ui, 1, 50);
        Check(narrow.ContentEquals(ui.View.Document), "Zero-width resize changed content.");
    }

    private static void Scale(Ui ui, int side, double deltaX, bool cancel = false)
    {
        var bounds = ui.View.SelectionTransformBounds;
        Check(bounds.Width > 0, "No box to scale.");
        float x = side < 0 ? bounds.X : bounds.Right, y = bounds.Y + bounds.Height / 2;
        float target = x + (float)(deltaX / 512 * ui.Plot.Width);
        ui.View.PointerDown(x, y, 0, false, false); ui.Paint();
        ui.View.PointerMove(target, y, false, false); ui.Paint();
        if (cancel) ui.Key(27);
        ui.View.PointerUp(target, y, 0); ui.Paint();
    }
    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > .02) throw new Exception($"Expected {expected}, got {actual}.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
