using System.Collections;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class ImportedCurveVisibilityTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 40000, BeatLengthMs = 100, SliderMultiplier = 10, IsDemo = false };
        var slider = new ImportedSlider { X = 100, Y = 192, TimeMs = 1000, PathType = 'L', SpanCount = 3, PixelLength = 100000 };
        slider.ControlPoints.Capacity = 100001;
        for (int i = 0; i <= 100000; i++) slider.ControlPoints.Add(new(100 + i % 2, 192));
        map.ImportedSliders.Add(slider);
        var ui = new Ui(false); ui.LoadDocument(map);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var cacheField = typeof(EditorView).GetField("importedCurveScreens", flags)!;
        var distanceField = typeof(EditorView).GetField("importedCurveDistances", flags)!;
        var draw = typeof(EditorView).GetMethod("DrawImportedCurves", flags)!;
        foreach (double time in new[] { 5000d, 15000, 25000 })
        {
            Draw();
            var screens = (IDictionary)cacheField.GetValue(ui.View)!;
            Check(screens.Count > 0, "Visible imported span did not produce a path.");
            var paths = screens.Values.Cast<System.Numerics.Vector2[]>().ToArray();
            Check(paths.All(p => p.Length > 1 && p.Length < 10000), "Imported drawing visited the complete offscreen path.");
            foreach (var path in paths)
                Check(path.Zip(path.Skip(1)).All(pair => pair.First.Y >= pair.Second.Y), "Repeated path did not retain chronological screen order.");
            var distance = ((IDictionary)distanceField.GetValue(ui.View)!).Values.Cast<double[]>().Single();
            Draw();
            Check(paths.SequenceEqual(((IDictionary)cacheField.GetValue(ui.View)!).Values.Cast<System.Numerics.Vector2[]>()), "Unchanged view rebuilt screen geometry.");
            Check(ReferenceEquals(distance, ((IDictionary)distanceField.GetValue(ui.View)!).Values.Cast<double[]>().Single()), "Unchanged view rebuilt distance data.");
            void Draw() => draw.Invoke(ui.View, [ui.Canvas, 0f, 512f, 900f, time, 1d, time, time + 100]);
        }
        Check(!ui.View.IsDirty, "Viewport rendering changed imported content.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
