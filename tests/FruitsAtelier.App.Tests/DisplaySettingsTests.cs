using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DisplaySettingsTests
{
    public static void Run()
    {
        string originalLanguage = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "display-settings-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        Check(!LibrarySettings.Load(path).LowLatencyDisplay, "Older preferences must default to VSync.");
        try
        {
            foreach (string language in L.AvailableLanguages)
            {
                L.SetLanguage(language);
                var ui = new Ui(false);
                ui.View.LibrarySettings.Workspace = root;
                var original = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                ui.View.OpenSettings(); ui.Paint();
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayMode")), "Unsupported host exposed display setting.");
                ui.Key(27);
                ui.View.SupportsDisplayMode = true;
                ui.View.OpenSettings(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayDelayHint") && t.Color == 0xF2C66D), "Audio-delay hint is not highlighted.");
                ui.ClickText(L.Get("settings.displayVsync"));
                Check(!ui.View.LibrarySettings.LowLatencyDisplay, "Draft changed active presentation before Apply.");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayVsync")), "Closing retained an unapplied draft.");
                ui.ClickText(L.Get("settings.displayVsync"));
                ui.ClickText(L.Get("settings.appearance"));
                ui.ClickText(L.Get("settings.general"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayImmediate")), "Category switch lost the draft.");
                ui.View.ApplySettings(path); ui.Paint();
                Check(ui.View.LibrarySettings.LowLatencyDisplay && LibrarySettings.Load(path).LowLatencyDisplay, "Apply failed to activate and persist low latency.");
                ui.ClickText(L.Get("settings.displayImmediate"));
                ui.View.ApplySettings(path);
                Check(!ui.View.LibrarySettings.LowLatencyDisplay && !LibrarySettings.Load(path).LowLatencyDisplay, "VSync could not be restored.");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Display preference edited the beatmap.");
            }
        }
        finally { L.SetLanguage(originalLanguage); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
