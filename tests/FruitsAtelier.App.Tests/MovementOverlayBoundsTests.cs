using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class MovementOverlayBoundsTests
{
    public static void Run()
    {
        var ui = new Ui(false);
        var map = new MapDocument();
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        ui.LoadDocument(map); ui.ClickMap(1000, 100);
        var baseline = ui.View.Document.DeepClone();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var plotField = typeof(EditorView).GetField("plot", hidden)!;
        var draw = typeof(EditorView).GetMethod("DrawMovementOverlay", hidden)!;
        int reversed = 0;
        foreach (float x in new[] { 454.57144f, 212.8f, 100.1f })
        for (int step = 0; step < 850; step++)
        {
            var plot = new Rect(x, 120, 192 + step / 7f, 400);
            float width = Math.Min(300, plot.Width - 12);
            if (plot.X + 6 > plot.Right - width - 6) reversed++;
            plotField.SetValue(ui.View, plot);
            ui.Canvas.Clear(); draw.Invoke(ui.View, [ui.Canvas]);
            var panel = ui.View.MovementOverlayBounds ?? throw new Exception("Narrow movement panel missing.");
            if (Math.Abs(panel.Width - width) > .0001 || panel.X < plot.X || panel.Right > plot.Right + .0001)
                throw new Exception("Fractional-width movement panel escaped its canvas.");
        }
        if (reversed == 0) throw new Exception("Movement panel fixture did not exercise reversed float bounds.");
        if (!baseline.ContentEquals(ui.View.Document) || ui.View.IsDirty)
            throw new Exception("Movement panel layout changed content.");
    }
}
