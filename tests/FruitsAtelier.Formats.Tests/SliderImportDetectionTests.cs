using FruitsAtelier.Core;

internal static class SliderImportDetectionTests
{
    public static void Run()
    {
        foreach (char type in new[] { 'L', 'B' })
        {
            var shortSlider = Slider(type, [0, 16, 40], 40);
            Check(Detect(shortSlider), "A short exported slider with one rounded droplet anchor was excluded.");
            var translated = Slider(type, [0, 16, 40], 40);
            translated.Y += 100;
            for (int i = 0; i < translated.ControlPoints.Count; i++)
            {
                var point = translated.ControlPoints[i];
                translated.ControlPoints[i] = new(point.X, point.GeometryY + 100);
            }
            Check(Detect(translated), "Y translation changed detection.");
            Check(!Detect(Slider(type, [0, 40], 40)), "A slider without its droplet anchor qualified.");
            Check(!Detect(Slider(type, [0, 12, 40], 40)), "A misplaced anchor qualified merely by count.");
            Check(!Detect(Slider(type, Enumerable.Range(0, 21).Select(i => i * 10).ToArray(), 200)),
                "Dense anchors unrelated to droplet timing qualified.");
            Check(!Detect(Slider(type, [0, 10], 10)), "A slider without droplets counted as evidence.");
            var repeated = Slider(type, [0, 16, 40], 40); repeated.SpanCount = 2;
            Check(!Detect(repeated), "Only the final traversal's droplet anchors were checked.");
            repeated = Slider(type, [0, 16, 20, 24, 40], 40); repeated.SpanCount = 2;
            Check(Detect(repeated), "Folded return-traversal anchors were not recognized.");

            var map = Map();
            for (int i = 0; i < 4; i++) AddMatch();
            map.ImportedSliders.Add(Slider(type, [0, 40], 40));
            Check(!SliderImportEditing.LooksCompensated(map), "Exactly 80% must not qualify.");
            AddMatch();
            Check(SliderImportEditing.LooksCompensated(map), "A matching majority above 80% was excluded.");
            for (int i = 0; i < 100; i++) map.Fruits.Add(new() { TimeMs = 5000 + i * 100, X = 30, Y = 20 + i });
            Check(SliderImportEditing.LooksCompensated(map), "Standalone fruit layout changed the slider percentage.");
            map.ImportedSliders.Add(Slider(type, [0, 10], 10));
            Check(!SliderImportEditing.LooksCompensated(map), "The denominator omitted sliders without droplets.");

            void AddMatch()
            {
                var copy = Slider(type, [0, 16, 40], 40); copy.Id = Guid.NewGuid();
                map.ImportedSliders.Add(copy);
            }
        }
        Check(!SliderImportEditing.LooksCompensated(Map()), "An empty map qualified.");
        var curve = Slider('L', [0, 16, 40], 40);
        curve.PathType = 'B';
        Check(!Detect(curve), "Bezier control points were mistaken for straight-segment anchors.");

        // A 1000ms slider has its regular droplet at distance 100; all tiny anchors
        // remain when removing that one point, so large droplets must also be checked.
        var ticks = Slider('L', [0, 13, 25, 38, 50, 63, 75, 88, 100, 112, 123, 135, 146, 158, 170, 181, 200], 200);
        Check(Detect(ticks), "Regular and tiny droplet anchors were not recognized.");
        var timingMap = Map(); timingMap.ImportedSliders.Add(ticks);
        timingMap.SliderTickRate = 2;
        Check(!SliderImportEditing.LooksCompensated(timingMap), "Detection ignored the active tick rate.");
        timingMap.SliderTickRate = 1;
        timingMap.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        timingMap.TimingPoints.Add(new() { TimeMs = 999, BeatLengthMs = -50, Uninherited = false });
        Check(!SliderImportEditing.LooksCompensated(timingMap), "Detection ignored inherited SV at the slider head.");
        ticks.ControlPoints.RemoveAt(8);
        Check(!Detect(ticks), "Missing regular-droplet anchor was accepted.");
    }

    private static MapDocument Map() => new() { BeatLengthMs = 500, SliderMultiplier = 1, SliderTickRate = 1 };

    private static ImportedSlider Slider(char type, int[] offsets, double length)
    {
        var slider = new ImportedSlider { X = 100, Y = 64, TimeMs = 1000, PixelLength = length, PathType = type };
        for (int i = 0; i < offsets.Length; i++)
        {
            var point = new SliderPathPoint(100, 64 + offsets[i]);
            slider.ControlPoints.Add(point);
            if (type == 'B' && i > 0 && i < offsets.Length - 1) slider.ControlPoints.Add(point);
        }
        return slider;
    }

    private static bool Detect(ImportedSlider slider)
    {
        var map = Map(); map.ImportedSliders.Add(slider);
        var before = map.DeepClone();
        bool result = SliderImportEditing.LooksCompensated(map);
        Check(map.ContentEquals(before), "Detection changed source content.");
        return result;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
