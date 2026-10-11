using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class FullscreenSettingsTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "fullscreen-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        Check(!LibrarySettings.Load(path).Fullscreen, "Older preferences must start windowed.");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620), (760, 580) })
            foreach (bool displayMode in new[] { false, true })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = root;
                ui.View.SupportsDisplayMode = displayMode;
                ui.View.SupportsFullscreen = true;
                int changes = 0, saves = 0;
                ui.View.RequestFullscreen = _ => changes++;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                var original = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                ui.View.OpenSettings(); ui.Paint();
                ScrollToFullscreen(ui);
                ui.ClickText(L.Get("settings.fullscreenOff"));
                Check(changes == 0 && !ui.View.LibrarySettings.Fullscreen, "Fullscreen draft changed the active window.");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint(); ScrollToFullscreen(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.fullscreenOff")), "Closing settings retained the fullscreen draft.");
                ui.ClickText(L.Get("settings.fullscreenOff"));
                ui.ClickText(L.Get("settings.appearance")); ui.ClickText(L.Get("settings.general"));
                ui.View.ApplySettings(path); ui.Paint();
                Check(changes == 1 && LibrarySettings.Load(path).Fullscreen, "Apply did not save and activate fullscreen.");
                ui.View.SetModifiers(true, false);
                ui.View.KeyDown(13, false, false); ui.View.KeyDown(13, false, false);
                Check(changes == 2 && saves == 1 && !LibrarySettings.Load(path).Fullscreen, "Alt+Enter did not toggle exactly once and save immediately.");
                ui.View.KeyUp(13); ui.View.SetModifiers(false, false); ui.Paint(); ScrollToFullscreen(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.fullscreenOff")), "Shortcut left a stale fullscreen draft.");
                ui.View.Wheel(ui.View.SettingsBounds.X + 300, ui.View.SettingsBounds.Y + 200, 1200, false); ui.Paint();
                ui.ClickText(L.Get("settings.romanisedOn"));
                AltEnter(ui);
                Check(changes == 3 && ui.View.LibrarySettings.Fullscreen, "Releasing Enter did not allow another toggle.");
                ui.View.ApplySettings(path);
                Check(!LibrarySettings.Load(path).RomanisedMetadata && LibrarySettings.Load(path).Fullscreen,
                    "Shortcut lost another settings draft or Apply reverted fullscreen.");
                ui.Key(27);
                ui.View.SetModifiers(true, true); ui.Key(13, shift: true); ui.View.KeyUp(13);
                ui.View.SetModifiers(true, false); ui.Key(13, ctrl: true); ui.View.KeyUp(13);
                Check(changes == 3, "Modified Alt+Enter triggered fullscreen.");
                ui.View.SetModifiers(false, false);
                ui.Key(116);
                Check(ui.View.IsTestplaying, "Fullscreen testplay fixture did not start.");
                AltEnter(ui);
                Check(ui.View.IsTestplaying && !ui.View.LibrarySettings.Fullscreen && changes == 4,
                    "Alt+Enter interrupted testplay or failed to leave fullscreen.");
                ui.View.StopTestplay();
                ui.View.OpenSettings(); ui.View.UpdateFullscreenState(true); ui.Paint(); ScrollToFullscreen(ui);
                Check(LibrarySettings.Load(path).Fullscreen && changes == 4
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.fullscreenOn")),
                    "A native fullscreen change was not saved and reflected in the settings draft.");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Fullscreen changed beatmap content or history.");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void ScrollToFullscreen(Ui ui)
    {
        var bounds = ui.View.SettingsBounds;
        ui.View.Wheel(bounds.X + 300, bounds.Y + 200, -1200, false); ui.Paint();
        var control = ui.Canvas.Texts.Single(t => t.Value == L.Get(ui.View.LibrarySettings.Fullscreen ? "settings.fullscreenOn" : "settings.fullscreenOff"));
        Check(control.Y >= bounds.Y + 116 && control.Y < bounds.Bottom - 86, "Fullscreen is not reachable in General.");
    }

    private static void AltEnter(Ui ui)
    {
        ui.View.SetModifiers(true, false); ui.View.KeyDown(13, false, false); ui.View.KeyUp(13);
        ui.View.SetModifiers(false, false); ui.Paint();
    }

    private static void Check(bool valid, string message)
    { if (!valid) throw new InvalidOperationException(message); }
}
