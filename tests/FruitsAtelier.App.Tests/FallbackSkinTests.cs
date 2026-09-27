using FruitsAtelier.Core;

internal static class FallbackSkinTests
{
    public static void BrightComboColour()
    {
        var map = new MapDocument { DurationMs = 3000, IsDemo = false };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
        var colours = new OsuSection { Name = "Colours" };
        colours.Lines.Add("Combo1: 0,0,128");
        map.OriginalSections.Add(colours);

        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        var point = ui.ScreenAt(1000, 256);
        if (!ui.Canvas.Circles.Any(circle => circle.Filled && circle.Color == 0x9999CC
            && Math.Abs(circle.X - point.X) < .01f && Math.Abs(circle.Y - point.Y) < .01f))
            throw new Exception("Unskinned fruit did not brighten the beatmap combo colour.");
    }
}
