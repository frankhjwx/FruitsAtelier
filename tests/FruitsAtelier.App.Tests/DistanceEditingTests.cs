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
        Input(ui, true, ".5"); Near(310, ui.View.Document.Fruits[1].X);
        Input(ui, false, "4"); Near(310, ui.View.Document.Fruits[1].X);
        Check(ui.View.IsEditingText, "Out-of-bounds DS silently committed"); ui.Key(27);
        Input(ui, false, "-1"); Near(310, ui.View.Document.Fruits[1].X); ui.Key(27);
        var field = ui.View.PreviousDistanceFieldBounds!.Value;
        ui.Click(field.X + 8, field.Y + 8); ui.Type(".8"); ui.Key(27); Near(310, ui.View.Document.Fruits[1].X);
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
            Check(ui.View.PreviousDistanceFieldBounds is not null, $"{point} was not individually selected");
            Input(ui, false, ratio.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Check(!ui.View.IsEditingText, $"{imported}/{point} DS was rejected");
            var result = CatchStreamConverter.Convert(ui.View.Document);
            var moved = result.Objects.Single(o => o.SourceId == source && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
            Near(reference.X + (target.X - reference.X) * .9, moved.X);
            Near(ratio, ui.View.DistanceReadout.Previous!.Value);
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
        var dense = Fruits(); dense.Fruits.Clear();
        for (int i = 0; i < 100; i++) dense.Fruits.Add(new Fruit { TimeMs = 1000 + i * 3, X = 256 });
        ui.LoadDocument(dense); ui.Paint();
        if (!ui.View.MovementAnalysisEnabled) ui.ClickText(Strings.Get("movement.analysis"));
        Check(ui.View.DistanceLabelBounds.Count < 99, "Dense connections did not suppress colliding labels");
    }

    private static void Input(Ui ui, bool next, string value)
    {
        var field = (next ? ui.View.NextDistanceFieldBounds : ui.View.PreviousDistanceFieldBounds)!.Value;
        ui.Click(field.X + 8, field.Y + 8); ui.Type(value); ui.Key(13);
    }
}
