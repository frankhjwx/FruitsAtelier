using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ExportStyleSettingsTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(760, 580);
                string root = Path.GetFullPath("artifacts/tests/export-style/" + locale);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
                ui.View.LibrarySettings.OsuRoot = "";
                string config = Path.Combine(root, "settings.json");
                var map = new MapDocument { ApproachRate = 8, DurationMs = 5000 };
                map.Fruits.AddRange([new() { X = 100, TimeMs = 1000 }, new() { X = 170, TimeMs = 1100 }, new() { X = 190, TimeMs = 1200 }]);
                ui.LoadDocument(map);
                var before = ui.View.Document.DeepClone();
                Check(!ui.View.LibrarySettings.ExperimentalExportStyle, "Style defaults off");
                void Settings()
                {
                    ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.experimental"));
                    var r = ui.View.SettingsBounds;
                    ui.View.Wheel(r.Right - 80, r.Y + 220, -1200, false); ui.Paint();
                }
                void Toggle()
                {
                    var r = ui.View.ExportStyleToggleBounds;
                    Check(r.Y >= ui.View.SettingsBounds.Y + 116 && r.Bottom < ui.View.SettingsBounds.Bottom - 80,
                        "Scrolled toggle must fit between heading and footer");
                    ui.Click(r.X + 10, r.Y + r.Height / 2);
                }
                Settings(); Toggle(); ui.Key(27);
                Check(!ui.View.LibrarySettings.ExperimentalExportStyle, "Cancel discards draft");
                Settings(); Toggle(); ui.View.ApplySettings(config); ui.Paint(); ui.Key(27);
                Check(LibrarySettings.Load(config).ExperimentalExportStyle, "Apply persists style");
                Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Style changes must not edit source");
                ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(1000, 5000, true, false, false, null, null); ui.Paint();
                var output = OsuBeatmapWriter.Serialize(ui.View.Document, experimentalExportStyle: true);
                var fruit = output.ReadBack.Fruits[0];
                var point = ui.View.OsuCanvasScreen(fruit.X, fruit.Y);
                Check(ui.View.OsuCanvasHit(point.X, point.Y) == ui.View.Document.Fruits[0].Id, "Canvas must show actual styled export");
                ui.View.PointerDown(point.X, point.Y, 0, false, false);
                ui.View.PointerMove(point.X + 40, point.Y + 40, false, false);
                ui.View.PointerUp(point.X + 40, point.Y + 40, 0); ui.Paint();
                Check(before.ContentEquals(ui.View.Document), "Preview drag changed authored geometry");
                var layoutField = typeof(FruitsAtelier.App.Editor.EditorView).GetField("styledPlayableExport",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var cached = layoutField.GetValue(ui.View);
                for (int i = 0; i < 20; i++)
                {
                    ui.View.ZoomOsuCanvasAt(point.X, point.Y, i % 2 == 0 ? 1.01 : 1 / 1.01);
                    ui.View.PointerMove(point.X + i, point.Y, false, false); ui.Paint();
                }
                Check(ReferenceEquals(cached, layoutField.GetValue(ui.View)), "Hover, zoom and paint must reuse the prepared layout");
                Settings(); Toggle(); ui.View.ApplySettings(config); ui.Paint(); ui.Key(27);
                Check(!LibrarySettings.Load(config).ExperimentalExportStyle, "Disable persists");
                point = ui.View.OsuCanvasScreen(100, 192);
                Check(ui.View.OsuCanvasHit(point.X, point.Y) == ui.View.Document.Fruits[0].Id, "Disable restores authored layout");
            }
        }
        finally { L.SetLanguage(language); }
        Console.WriteLine("PASS export style settings: default, cancel, persistence, canvas parity and source isolation");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
