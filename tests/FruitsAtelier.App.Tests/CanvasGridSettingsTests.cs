using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class CanvasGridSettingsTests
{
    private sealed class Clock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/canvas-grid", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        var defaults = LibrarySettings.Load(path);
        Check(defaults.CanvasGridColour == 0x8F8F8F && defaults.CanvasCentreLineColour == 0xFFFFFF && defaults.CanvasGridOpacity == 40,
            "Older settings did not retain the default grid appearance.");
        defaults.CanvasGridOpacity = -1; Check(defaults.CanvasGridOpacity == 0, "Opacity accepts negative values.");
        defaults.CanvasGridOpacity = 101; Check(defaults.CanvasGridOpacity == 100, "Opacity exceeds 100%.");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false, new Clock()); ui.View.LibrarySettings.Workspace = root;
                var gridButton = ui.View.AssistButtonBounds[4];
                ui.Click(gridButton.X + 10, gridButton.Y + 10);
                var original = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                var horizontal = ui.Canvas.Lines.Where(l => l.Y1 == l.Y2).ToArray();
                var vertical = GridLines(ui);
                Check(vertical.Length == 5, "Fixture did not expose the five X-axis lines.");
                var subdivisions = SubdivisionLines(ui);
                Check(subdivisions.Length > 0, "Fixture did not expose Grid Snap subdivision lines.");
                Open(); Colour("settings.canvasGridLine", "123456"); Colour("settings.canvasCentreLine", "ABCDEF");
                Check(ui.View.LibrarySettings.CanvasGridColour == defaults.CanvasGridColour, "Colour draft applied early.");
                ui.Key(27); Open();
                Check(ui.Canvas.Texts.Any(t => t.Value == "#8F8F8F"), "Closing settings retained a colour draft.");
                Colour("settings.canvasGridLine", "123456"); Colour("settings.canvasCentreLine", "ABCDEF");
                ui.ClickText(L.Get("settings.canvasGridOpacity", 40) + " ▾");
                ui.ClickText(L.Get("settings.canvasGridOpacity", 60));
                ui.View.ApplySettings(path); ui.Key(27);
                var saved = LibrarySettings.Load(path);
                Check(saved.CanvasGridColour == 0x123456 && saved.CanvasCentreLineColour == 0xABCDEF && saved.CanvasGridOpacity == 60,
                    "Grid preferences did not persist.");
                var updated = GridLines(ui);
                Check(updated.Count(l => l.Color == 0x123456) == 4 && updated.Count(l => l.Color == 0xABCDEF) == 1
                    && updated.All(l => Math.Abs(l.Opacity - .6f) < .001), "Canvas did not apply grid colour and opacity.");
                var updatedSubdivisions = SubdivisionLines(ui);
                Check(updatedSubdivisions.Length == subdivisions.Length && updatedSubdivisions.All(l => l.Color == 0x123456
                    && Math.Abs(l.Opacity - .6f) < .001), "Grid Snap subdivisions did not apply colour and opacity.");
                Check(horizontal.SequenceEqual(ui.Canvas.Lines.Where(l => l.Y1 == l.Y2)), "Grid preferences changed horizontal lines.");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Grid preferences edited map content.");
                ui.Resize(760, 580); Open();
                ui.View.Wheel(ui.View.SettingsBounds.X + 300, ui.View.SettingsBounds.Y + 200, -1200, false); ui.Paint();
                var opacity = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.canvasGridOpacity", 60) + " ▾");
                Check(opacity.Y >= ui.View.SettingsBounds.Y + 116 && opacity.Y + 20 < ui.View.SettingsBounds.Bottom - 86,
                    "Narrow settings cannot reach grid opacity.");
                ui.ClickText(L.Get("settings.canvasGridReset")); ui.View.ApplySettings(path); ui.Key(27);
                saved = LibrarySettings.Load(path);
                Check(saved.CanvasGridColour == defaults.CanvasGridColour && saved.CanvasCentreLineColour == defaults.CanvasCentreLineColour
                    && saved.CanvasGridOpacity == 40, "Reset did not restore the default grid.");
                Check(saved.StandIndicatorColour == defaults.StandIndicatorColour, "Grid reset changed indicator colours.");

                void Open() { ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.mappingCanvas")); }
                void Colour(string key, string value)
                {
                    ui.ClickText(L.Get(key));
                    var hex = ui.Canvas.Texts.Last(t => t.Value == L.Get("settings.indicatorHex"));
                    ui.Click(hex.X + 30, hex.Y + 30); ui.Key('A', ctrl: true); ui.Type(value);
                    ui.ClickText(L.Get("settings.indicatorDone"));
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static RecordingCanvas.Segment[] GridLines(Ui ui)
    {
        var positions = ui.Canvas.Texts.Where(t => t.Value is "0" or "128" or "256" or "384" or "512"
            && t.Y == ui.Plot.Y - 20).Select(t => t.X + 9).ToArray();
        return ui.Canvas.Lines.Where(l => l.X1 == l.X2 && l.Y1 == ui.Plot.Y && l.Y2 == ui.Plot.Bottom
            && positions.Contains(l.X1)).ToArray();
    }

    private static RecordingCanvas.Segment[] SubdivisionLines(Ui ui)
    {
        var basePositions = GridLines(ui).Select(l => l.X1).ToArray();
        return ui.Canvas.Lines.Where(l => l.X1 == l.X2 && l.Y1 == ui.Plot.Y && l.Y2 == ui.Plot.Bottom
            && !basePositions.Contains(l.X1)).ToArray();
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
