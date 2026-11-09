using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class FruitStackSettingsTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath("artifacts/tests/selection-gap");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        Check(LibrarySettings.Load(path).ShowTestplayFruitStack, "Old settings must show caught fruit stacks.");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 10000 }); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, locale);
                var baseline = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                Appearance();
                var stack = ui.View.TestplayFruitStackBounds;
                var lighting = ui.View.HitLightingSettingsBounds;
                Check(stack.X > lighting.Right && stack.Y == lighting.Y && stack.Bottom < ui.View.SettingsBounds.Bottom - 60,
                    "Fruit stack control must fit beside lighting.");
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.showFruitStack"));
                Check(((FruitsAtelier.App.Rendering.ICanvas)ui.Canvas).MeasureText(label.Value, label.Size) <= label.MaxWidth, "Fruit stack label is clipped.");
                ui.Click(stack.X + 10, stack.Y + 10);
                Check(ui.View.LibrarySettings.ShowTestplayFruitStack, "Draft changed the active setting.");
                ui.Key(27); Appearance();
                ui.View.ApplySettings(path, force: true);
                Check(LibrarySettings.Load(path).ShowTestplayFruitStack, "Cancel retained the draft.");
                Appearance(); ui.Click(stack.X + 10, stack.Y + 10); ui.View.ApplySettings(path);
                var saved = LibrarySettings.Load(path);
                Check(!saved.ShowTestplayFruitStack && saved.CatchHitLighting == CatchHitLightingMode.FollowMode,
                    "Apply failed to persist the independent stack preference.");
                var reopened = new Ui(false); reopened.View.InitializeLibrary(false, saved);
                Check(!reopened.View.LibrarySettings.ShowTestplayFruitStack && baseline.ContentEquals(ui.View.Document) && ui.View.IsDirty == dirty,
                    $"Preference restart: stack={reopened.View.LibrarySettings.ShowTestplayFruitStack}, contentEqual={baseline.ContentEquals(ui.View.Document)}, dirty={ui.View.IsDirty}/{dirty}.");
                void Appearance()
                {
                    ui.View.OpenSettings(); ui.Paint();
                    var bounds = ui.View.SettingsBounds;
                    ui.Click(bounds.X + 60, bounds.Y + 78 + 2 * Math.Min(48, (bounds.Height - 108) / 10) + 12);
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
