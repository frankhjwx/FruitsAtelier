using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class MovementLabelSettingsTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/movement-labels", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        var settings = LibrarySettings.Load(path);
        Check(settings.MovementLabelTimeThresholdMs == 37.5, "Older settings lost the default cutoff.");
        settings.MovementLabelTimeThresholdMs = -10;
        Check(settings.MovementLabelTimeThresholdMs == 0, "Negative cutoff accepted.");
        settings.MovementLabelTimeThresholdMs = 501;
        Check(settings.MovementLabelTimeThresholdMs == 500, "Cutoff exceeded its limit.");
        settings.MovementLabelTimeThresholdMs = double.NaN;
        Check(settings.MovementLabelTimeThresholdMs == 37.5, "Invalid cutoff did not fall back.");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (bool speed in new[] { false, true })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                ui.View.LibrarySettings.Workspace = root;
                ui.View.LibrarySettings.LazerEditing = speed;
                var map = new MapDocument { DurationMs = 4000, ApproachRate = 5 };
                map.Fruits.AddRange([new Fruit { TimeMs = 100, X = 100 }, new Fruit { TimeMs = 137, X = 300 }]);
                ui.LoadDocument(map);
                ui.ClickText(L.Get("movement.analysis"));
                var plot = ui.View.CanvasPlotBounds;
                ui.View.Wheel(plot.X, plot.Bottom, 2400, false, false, true); ui.Paint();
                var original = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                Check(ui.View.DistanceLabelBounds.Count == 0, "Default cutoff showed a dense connection.");
                Open(); Choose(37.5, 0);
                Check(ui.View.LibrarySettings.MovementLabelTimeThresholdMs == 37.5, "Draft applied early.");
                ui.Key(27); Open(); Choose(37.5, 0);
                ui.View.ApplySettings(path); ui.Key(27);
                Check(LibrarySettings.Load(path).MovementLabelTimeThresholdMs == 0, "Cutoff did not persist.");
                Check(ui.View.DistanceLabelBounds.Count == 1, "Zero cutoff did not refresh dense labels.");
                ui.View.LibrarySettings.MovementLabelTimeThresholdMs = 37;
                ui.Paint();
                Check(ui.View.DistanceLabelBounds.Count == 0, "Exact cutoff boundary was labelled or cache was stale.");
                ui.View.LibrarySettings.MovementLabelTimeThresholdMs = 36.5;
                ui.Paint();
                Check(ui.View.DistanceLabelBounds.Count == 1, "Interval above cutoff was hidden.");
                Open();
                ui.ClickText(L.Get("settings.movementLabelReset"));
                Check(ui.View.LibrarySettings.MovementLabelTimeThresholdMs == 36.5, "Reset applied before Apply.");
                ui.View.ApplySettings(path); ui.Key(27);
                Check(LibrarySettings.Load(path).MovementLabelTimeThresholdMs == 37.5
                    && ui.View.DistanceLabelBounds.Count == 0, "Reset did not restore and persist the default cutoff.");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Display setting edited map content.");

                void Open()
                {
                    ui.Resize(760, 580);
                    ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.mappingCanvas"));
                    ui.View.Wheel(ui.View.SettingsBounds.X + 300, ui.View.SettingsBounds.Y + 200, -1200, false); ui.Paint();
                }
                void Choose(double from, double to)
                {
                    string label = L.Get("settings.movementLabelTimeThreshold", from) + " ▾";
                    var text = ui.Canvas.Texts.Single(t => t.Value == label);
                    var opacity = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.canvasGridOpacity", 40) + " ▾");
                    Check(text.X == opacity.X && text.MaxWidth == opacity.MaxWidth, "Cutoff and opacity rows differ in width.");
                    Check(text.Y >= ui.View.SettingsBounds.Y + 116 && text.Y + 20 < ui.View.SettingsBounds.Bottom - 86,
                        "Narrow settings cannot reach the cutoff.");
                    ui.ClickText(label);
                    ui.ClickText(L.Get("settings.movementLabelTimeThreshold", to));
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
