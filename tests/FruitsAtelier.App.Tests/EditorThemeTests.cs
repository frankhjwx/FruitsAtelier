using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class EditorThemeTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/editor-themes", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        Check(LibrarySettings.Load(path).EditorTheme == EditorThemeId.Graphite, "Older settings retain Graphite.");
        File.WriteAllText(path, "{\"EditorTheme\":999}");
        Check(LibrarySettings.Load(path).EditorTheme == EditorThemeId.Graphite, "Unknown theme falls back safely.");
        Check(Enum.GetValues<EditorThemeId>().Length == 7, "Seven editor themes are available.");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var theme in Enum.GetValues<EditorThemeId>())
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(1440, 900);
                ui.View.LibrarySettings.Workspace = root;
                ui.View.LibrarySettings.OsuRoot = "";
                ui.View.LibrarySettings.EditorTheme = EditorThemeId.Graphite;
                var document = ui.View.Document;
                bool dirty = ui.View.IsDirty;
                string? skin = ui.View.LibrarySettings.SelectedSkin;
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
                Select();
                Check(ui.Canvas.Fills.Any(f => f.Color == EditorThemePalette.For(theme).Panel), "Preview updates panel colors.");
                Check(ui.View.LibrarySettings.EditorTheme == EditorThemeId.Graphite, "Preview does not persist before Apply.");
                ui.Key(27);
                Check(ui.Canvas.Fills.Any(f => f.Color == EditorThemePalette.For(EditorThemeId.Graphite).Background), "Cancel restores original colors.");
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance")); Select();
                ui.View.ApplySettings(path, force: true); ui.Paint(); ui.Key(27);
                Check(LibrarySettings.Load(path).EditorTheme == theme, "Applied theme survives persistence.");
                Check(ReferenceEquals(ui.View.Document, document) && ui.View.IsDirty == dirty, "Theme changes preserve map content and dirty state.");
                Check(ui.View.LibrarySettings.SelectedSkin == skin, "Editor themes preserve osu skin selection.");
                var other = new Ui(false);
                other.View.LibrarySettings.EditorTheme = EditorThemeId.Graphite; other.Paint();
                Check(other.Canvas.Fills.Any(f => f.Color == EditorThemePalette.For(EditorThemeId.Graphite).Background), "Theme is per editor instance.");
                var palette = EditorThemePalette.For(theme);
                Check(!ui.View.LightEditorTheme, "All seven editor themes use dark surfaces.");
                Check(Contrast(palette.Foreground, palette.Panel) >= 4.5 && Contrast(palette.Muted, palette.Surface) >= 3,
                    "Themes retain readable body and secondary text.");

                void Select()
                {
                    var bounds = ui.View.EditorThemeSelectorBounds;
                    ui.Click(bounds.X + 12, bounds.Y + 12);
                    string name = L.Get("settings.theme." + theme.ToString().ToLowerInvariant());
                    var item = ui.Canvas.Texts.Last(t => t.Value == name || t.Value == "✓ " + name);
                    ui.Click(item.X + 3, item.Y + 3);
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static double Contrast(uint a, uint b)
    {
        static double Channel(uint value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(uint c) => .2126 * Channel((c >> 16) & 255) + .7152 * Channel((c >> 8) & 255) + .0722 * Channel(c & 255);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
