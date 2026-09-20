using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class DistanceEditingTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .002, $"Expected {expected}, got {actual}");
    private static MapDocument Fruits()
    {
        var map = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, IsDemo = false };
        foreach (var (time, x) in new[] { (1000, 100), (1500, 240), (2000, 380) }) map.Fruits.Add(new Fruit { TimeMs = time, X = x });
        map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = -25, Uninherited = false });
        return map;
    }

    public static void NumericFields()
    {
        var ui = new Ui(); ui.LoadDocument(Fruits()); ui.ClickMap(1500, 240);
        var original = ui.View.Document.DeepClone();
        Near(1, ui.View.DistanceReadout.Previous!.Value);
        Input(ui, false, ".5");
        Near(170, ui.View.Document.Fruits[1].X);
        Near(.5, ui.View.DistanceReadout.Previous!.Value);
        Near(1500, ui.View.Document.Fruits[1].TimeMs);
        ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "DS undo changed other content");
        ui.Key('Y', ctrl: true); Near(170, ui.View.Document.Fruits[1].X); ui.ClickMap(1500, 170);
        Check(ui.View.NextDistanceFieldBounds is null, "Next DS remains editable");
        var panel = ui.View.MovementOverlayBounds!.Value;
        ui.Click(panel.Right - 8, panel.Y + 8);
        Check(ui.View.IsEditingText, "Whole panel did not start editing");
        ui.View.PointerDoubleClick(panel.Right - 8, panel.Y + 8, false, false); ui.Paint();
        ui.Type(".75"); ui.Paint(); Near(205, ui.View.Document.Fruits[1].X);
        Near(.75, ui.View.DistanceReadout.Previous!.Value);
        ui.Key(27); Near(170, ui.View.Document.Fruits[1].X);
        Input(ui, false, "4"); Near(170, ui.View.Document.Fruits[1].X);
        Check(ui.View.IsEditingText, "Out-of-bounds DS silently committed"); ui.Key(27);
        Input(ui, false, "-1"); Near(170, ui.View.Document.Fruits[1].X); ui.Key(27);
        ui.Click(panel.X + 8, panel.Y + 8);
        var slider = ui.View.DistanceSliderBounds!.Value;
        ui.View.PointerDown(slider.X, slider.Y + 10, 0, false, false); ui.Paint();
        Near(100, ui.View.Document.Fruits[1].X);
        ui.View.PointerMove(slider.X + slider.Width * .46f, slider.Y + 10, false, false); ui.Paint();
        var ratio = ui.View.DistanceReadout.Previous!.Value;
        Near(Math.Round(ratio, 1), ratio);
        Check(ui.View.Document.Fruits[1].X > 100, "Slider lost original direction after zero");
        ui.View.SetModifiers(false, true);
        ui.View.PointerMove(slider.X + slider.Width * .46f, slider.Y + 10, true, false); ui.Paint();
        ratio = ui.View.DistanceReadout.Previous!.Value;
        Near(1.35, ratio);
        ui.View.PointerUp(slider.X + slider.Width * .46f, slider.Y + 10, 0); ui.Paint();
        Near(1.35, ui.View.DistanceReadout.Previous!.Value);
        ui.View.SetModifiers(false, false);
        Check(ui.Canvas.Texts.Any(t => t.Value == ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)), "DS input is not formatted to two decimals");
        ui.Key('Z', ctrl: true); Near(170, ui.View.Document.Fruits[1].X);
        Check(!ui.View.IsEditingText, "Immediate DS undo left the input active");
        ui.Key('Y', ctrl: true); Near(289, ui.View.Document.Fruits[1].X);
        ui.Key('Z', ctrl: true); Near(170, ui.View.Document.Fruits[1].X);
        ui.ClickMap(1500, 170); ui.Click(panel.X + 8, panel.Y + 8);
        ui.Key('Z', ctrl: true); Near(170, ui.View.Document.Fruits[1].X);
        ui.ClickMap(1500, 170); ui.Click(panel.X + 8, panel.Y + 8); ui.Type(".8"); ui.Paint();
        Near(212, ui.View.Document.Fruits[1].X);
        ui.View.CancelInteraction(); ui.Paint(); Near(170, ui.View.Document.Fruits[1].X);
        ui.Click(panel.X + 8, panel.Y + 8); ui.Type(".9"); ui.Paint();
        ui.ClickMap(2200, 100); Near(226, ui.View.Document.Fruits[1].X);
        ui.Key('Z', ctrl: true); Near(170, ui.View.Document.Fruits[1].X);
        ui.ClickMap(1500, 170);
        ui.Key('L'); Check(ui.View.PreviousDistanceFieldBounds is null, "Locked note still exposes DS editing");
    }

    public static void SliderPoints()
    {
        foreach (bool imported in new[] { false, true })
        foreach (string point in new[] { "head", "droplet", "tail" })
        {
            var map = Fruits(); map.Fruits.RemoveRange(1, 2); map.Fruits[0].TimeMs = 500; map.Fruits[0].X = 40;
            map.TimingPoints.Clear(); map.SliderTickRate = 4;
            map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = -50, Uninherited = false });
            Guid source;
            if (imported)
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 280, SpanCount = 1 };
                slider.ControlPoints.AddRange([new(100, 192), new(380, 192)]); map.ImportedSliders.Add(slider); source = slider.Id;
            }
            else
            {
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 2000, X = 300 }]);
                map.Tracks.Add(track); source = track.Id;
            }
            var converted = CatchStreamConverter.Convert(map);
            Check(converted.Success, "Slider fixture did not convert");
            var objects = converted.Objects.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet).OrderBy(o => o.TimeMs).ToArray();
            var target = point switch
            {
                "head" => objects.First(o => o.SourceId == source),
                "tail" => objects.Last(o => o.SourceId == source),
                _ => objects.First(o => o.SourceId == source && o.Kind == CatchObjectKind.Droplet)
            };
            var reference = objects[Array.IndexOf(objects, target) - 1];
            double ratio = DistanceSnap.Ratio(new(reference.TimeMs, reference.X), new(target.TimeMs, target.X), DistanceSnap.BaseVelocity(map, reference.TimeMs))!.Value * .9;
            var ui = new Ui(); ui.LoadDocument(map); ui.ClickMap(target.TimeMs, target.X);
            var original = ui.View.Document.DeepClone();
            if (point == "droplet") CheckTickHighlight(ui, target);
            Check(ui.View.PreviousDistanceFieldBounds is not null, $"{point} was not individually selected");
            ratio = Math.Round(ratio, 2);
            Input(ui, false, ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            Check(!ui.View.IsEditingText, $"{imported}/{point} DS was rejected");
            var result = CatchStreamConverter.Convert(ui.View.Document);
            var moved = result.Objects.Single(o => o.SourceId == source && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
            Near(reference.X + Math.Sign(target.X - reference.X) * (target.TimeMs - reference.TimeMs) * DistanceSnap.BaseVelocity(map, reference.TimeMs) * ratio, moved.X);
            Near(ratio, ui.View.DistanceReadout.Previous!.Value);
            if (point == "droplet")
            {
                CheckTickHighlight(ui, moved);
                ui.ClickText(Strings.Get("movement.analysis"));
                CheckTickHighlight(ui, moved);
            }
            ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "Slider DS undo lost source geometry");
        }
    }

    public static void Labels()
    {
        var ui = new Ui(); ui.LoadDocument(Fruits()); ui.ClickText(Strings.Get("movement.analysis"));
        Check(ui.View.DistanceLabelBounds.Count > 0, "No DS labels on sparse connections");
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("assist.ratio", 1)), "Green SV changed the base DS label");
        var bounds = ui.View.DistanceLabelBounds.ToArray();
        for (int i = 0; i < bounds.Length; i++)
        for (int j = i + 1; j < bounds.Length; j++)
            Check(bounds[i].Right < bounds[j].X || bounds[j].Right < bounds[i].X || bounds[i].Bottom < bounds[j].Y || bounds[j].Bottom < bounds[i].Y, "DS labels overlap");
        var longGap = Fruits(); longGap.Fruits.Clear();
        longGap.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 5000, X = 400 }]);
        ui.LoadDocument(longGap); ui.Paint();
        var labelBefore = ui.View.DistanceLabelBounds.Single();
        var plot = ui.View.CanvasPlotBounds;
        float panX = plot.X + 4, panY = plot.Y + 10;
        ui.View.PointerDown(panX, panY, 1, false, false);
        ui.View.PointerMove(panX, panY + (float)(2000 * ui.View.PixelsPerMs), false, false);
        ui.View.PointerUp(panX, panY + (float)(2000 * ui.View.PixelsPerMs), 1); ui.Paint();
        var labelAfter = ui.View.DistanceLabelBounds.Single();
        Near(labelBefore.X, labelAfter.X);
        Near(labelBefore.Y + 2000 * ui.View.PixelsPerMs, labelAfter.Y);
        var shortGap = Fruits(); shortGap.Fruits.Clear();
        shortGap.Fruits.AddRange([new Fruit { TimeMs = 100, X = 256 }, new Fruit { TimeMs = 175, X = 256 }]);
        ui.LoadDocument(shortGap); ui.Paint();
        plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X, plot.Bottom, 2400, true); ui.Paint();
        Check(ui.View.DistanceLabelBounds.Count == 1, "Short isolated connection was hidden at high zoom");
        shortGap.Fruits[1].TimeMs = 137.5;
        ui.LoadDocument(shortGap); ui.Paint();
        Check(ui.View.DistanceLabelBounds.Count == 0, "200 BPM eighth-beat interval was labelled");
        shortGap.Fruits[1].TimeMs = 138;
        ui.LoadDocument(shortGap); ui.Paint();
        Check(ui.View.DistanceLabelBounds.Count == 1, "Interval above the dense timing cutoff was hidden");
        var dense = Fruits(); dense.Fruits.Clear();
        for (int i = 0; i < 100; i++) dense.Fruits.Add(new Fruit { TimeMs = 1000 + i * 3, X = 256 });
        ui.LoadDocument(dense); ui.Paint();
        if (!ui.View.MovementAnalysisEnabled) ui.ClickText(Strings.Get("movement.analysis"));
        Check(ui.View.DistanceLabelBounds.Count < 99, "Dense connections did not suppress colliding labels");
        PlaybackLabelStability();
    }

    private static void PlaybackLabelStability()
    {
        var map = Fruits(); map.Fruits.Clear();
        for (int i = 0; i < 50; i++) map.Fruits.Add(new Fruit { TimeMs = 1000 + i * 40, X = 256 });
        var ui = new Ui(); ui.LoadDocument(map); ui.ClickText(Strings.Get("movement.analysis"));
        var visibility = new Dictionary<int, bool>();
        for (int frame = 0; frame < 100; frame++)
        {
            double start = 800 + frame * 13;
            double playhead = start + ui.View.CanvasPlotBounds.Height * .25 / ui.View.PixelsPerMs;
            ui.View.UpdateTransport(playhead, 10000, true, true, false, null, "fixture.wav"); ui.Paint();
            var plot = ui.View.CanvasPlotBounds;
            var visible = ui.View.DistanceLabelBounds.Select(r => (int)Math.Round(ui.View.ViewStartMs + (plot.Bottom - r.Y - 9) / ui.View.PixelsPerMs)).ToHashSet();
            foreach (int time in Enumerable.Range(0, 49).Select(i => 1020 + i * 40))
            {
                double y = plot.Bottom - (time - ui.View.ViewStartMs) * ui.View.PixelsPerMs;
                if (y < plot.Y + 30 || y > plot.Bottom - 30) continue;
                bool shown = visible.Contains(time);
                if (visibility.TryGetValue(time, out bool before)) Check(before == shown, $"DS label at {time} flickered during playback");
                visibility[time] = shown;
            }
        }
        Check(visibility.Values.Any(v => v) && visibility.Values.Any(v => !v), "Playback fixture did not exercise collision suppression");
    }

    private static void CheckTickHighlight(Ui ui, ConvertedCatchObject tick)
    {
        var rings = ui.Canvas.Circles.Where(c => !c.Filled && c.Color == 0xE7EBF2).ToArray();
        Check(rings.Length == 1, "Selected tick should have one distinct outer ring");
        Near(ui.View.PlayfieldBounds.X + tick.X / 512 * ui.View.PlayfieldBounds.Width, rings[0].X);
        Near(ui.View.CanvasPlotBounds.Bottom - (tick.TimeMs - ui.View.ViewStartMs) * ui.View.PixelsPerMs, rings[0].Y);
    }

    private static void Input(Ui ui, bool next, string value)
    {
        var field = (next ? ui.View.NextDistanceFieldBounds : ui.View.PreviousDistanceFieldBounds)!.Value;
        ui.Click(field.X + 8, field.Y + 8); ui.Type(value); ui.Key(13);
    }
}
