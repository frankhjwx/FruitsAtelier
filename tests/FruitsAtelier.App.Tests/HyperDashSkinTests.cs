using FruitsAtelier.Core;

internal static class HyperDashSkinTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void ThreeColours()
    {
        const uint fruitColour = 0x4E5A7B, hyperColour = 0x0C2238, afterImageColour = 0x869CB2;
        string folder = Path.GetFullPath("artifacts/tests/hdash-colours");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "skin.ini"),
            "[Colours]\nHyperDash: 12,34,56\nHyperDashFruit: 78,90,123\nHyperDashAfterImage: 134,156,178\n");
        var map = new MapDocument { DurationMs = 3000, CircleSize = 5, IsDemo = false };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 40 }, new Fruit { TimeMs = 1100, X = 470 }]);
        map.DistanceSnapRatios.Add(8);
        var ui = new Ui(); ui.LoadDocument(map); ui.View.LoadSkin(folder); ui.Paint();
        Check(ui.Canvas.Circles.Any(c => c.Color == fruitColour), "HDash fruit ignored HyperDashFruit.");
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("movement.analysis"));
        Check(ui.Canvas.Lines.Any(l => l.Color == hyperColour && l.Width == 4),
            "Movement Analysis ignored HyperDash.");
        ui.OpenPreview();
        ui.View.UpdateTransport(1050, 3000, true, false, false, null, null); ui.Paint();
        Check(ui.Canvas.Circles.Any(c => c.Color == hyperColour), "HDash catcher ignored HyperDash.");
        Check(ui.Canvas.Circles.Any(c => c.Color == afterImageColour),
            "HDash afterimage ignored HyperDashAfterImage.");
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        var preview = ui.View.DistanceSnapPreviewBounds;
        float X(double x) => preview.X + 10 + (float)(x / 512) * (preview.Width - 20);
        float Y(double beat) => preview.Bottom - 12 - (float)(beat / 4) * (preview.Height - 24);
        ui.Click(X(0), Y(0)); ui.Click(X(512), Y(.25));
        Check(ui.View.DistanceSnapPreviewFruits.Count == 2
            && ui.Canvas.Circles.Any(c => c.Color == fruitColour && preview.Contains(c.X, c.Y)),
            "Distance Snap preview retained a fixed red HDash fruit.");
    }
}
