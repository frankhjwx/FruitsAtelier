using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class UiScaleTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/ui-scale", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        Check(LibrarySettings.Load(path).UiScalePercent == 150, "Older settings must retain the current size.");
        foreach (int invalid in new[] { int.MinValue, 0, 64, 149, 188, int.MaxValue })
        {
            var settings = new LibrarySettings { UiScalePercent = invalid };
            Check(settings.UiScalePercent is >= 50 and <= 200 && settings.UiScalePercent % 25 == 0,
                "Invalid scales must normalize to a supported step.");
        }
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int percent in Enumerable.Range(2, 7).Select(step => step * 25))
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                ui.View.LibrarySettings.Workspace = root;
                var document = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
                SelectScale();
                Check(ui.View.UiScaleFactor == 1, "Draft scale applied before Apply.");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
                Check(ui.Canvas.Texts.Any(t => t.Value == "150% ▾"), "Cancelled scale draft survived reopening.");
                SelectScale();
                ui.View.ApplySettings(path, force: true); ui.Paint();
                Check(LibrarySettings.Load(path).UiScalePercent == percent, "Scale did not survive settings persistence.");
                Check(Math.Abs(ui.View.UiScaleFactor - percent / 150f) < .0001, "Scale reference is not 150%.");
                ui.Key(27);
                ui.Resize(1440 * 150f / percent, 900 * 150f / percent);
                var bounds = ui.View.CanvasScaleLockBounds;
                bool uniform = ui.View.UniformCanvasScale;
                ui.Click(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                Check(ui.View.UniformCanvasScale != uniform, "Resized layout button missed input.");
                Check(document.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "UI scaling changed beatmap content.");
                void SelectScale()
                {
                    ui.ClickText("150% ▾");
                    var label = ui.Canvas.Texts.Last(t => t.Value == (percent == 150 ? "✓ " : "") + $"{percent}%");
                    ui.Click(label.X + 4, label.Y + 5);
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
