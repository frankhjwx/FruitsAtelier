using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SettingsPreferencesTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "settings-preferences-" + Guid.NewGuid()));
        string language = L.Language;
        Directory.CreateDirectory(root);
        try
        {
            string skin = Path.Combine(root, "osu", "Skins", "Settings skin");
            Directory.CreateDirectory(skin);
            Directory.CreateDirectory(Path.Combine(root, "osu", "Songs"));
            File.WriteAllText(Path.Combine(skin, "skin.ini"), "[General]\nName: Settings skin");
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (bool library in new[] { false, true })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
                ui.View.LibrarySettings.OsuRoot = Path.Combine(root, "osu");
                ui.LoadDocument(new MapDocument());
                ui.Resize(size.Item1, size.Item2);
                if (library) ui.View.ShowLibrary();
                var before = ui.View.Document.DeepClone();
                string config = Path.Combine(root, "preferences.json");
                int saved = 0;
                float songGain = -1, hitGain = -1;
                ui.View.RequestAudioPreference = () => { saved++; ui.View.LibrarySettings.Save(config); };
                ui.View.RequestSkinPreference = () => ui.View.LibrarySettings.Save(config);
                ui.View.RequestAudioVolume = (song, hit) => { songGain = song; hitGain = hit; };
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.audio"));
                for (int channel = 0; channel < 3; channel++)
                {
                    var bar = ui.View.VolumeSliderBounds(channel);
                    Check(bar.X >= ui.View.SettingsBounds.X && bar.Right < ui.View.SettingsBounds.Right
                        && bar.Bottom < ui.View.SettingsBounds.Bottom - 116, "Settings slider fits content area");
                    float x = bar.X + bar.Width * (channel + 1) / 4;
                    ui.View.PointerDown(bar.X, bar.Y + 12, 0, false, false);
                    ui.View.PointerMove(x, bar.Y + 12, false, false);
                    Check(ui.View.WantsCapture, "Settings volume drag captures pointer");
                    ui.View.PointerUp(x, bar.Y + 12, 0); ui.Paint();
                }
                var loaded = LibrarySettings.Load(config);
                Check(saved == 3 && loaded.MasterVolume == 25 && loaded.SongVolume == 50 && loaded.HitsoundVolume == 75,
                    "Settings saves all three volume channels on release");
                Check(songGain == .125f && hitGain == .1875f && !ui.View.WantsCapture, "Settings applies shared audio gains");
                ui.ClickText(L.Get("settings.skins"));
                void Menu()
                {
                    var selector = ui.View.SettingsSkinSelectorBounds;
                    ui.Click(selector.X + 8, selector.Y + 8);
                }
                Menu(); ui.ClickText("Settings skin");
                Check(ui.View.SkinName == "Settings skin" && LibrarySettings.Load(config).SelectedSkin == skin,
                    "Settings skin selector loads and persists the shared selection");
                Menu(); ui.Key(27);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.immediatePreferences")), "Escape dismisses skin menu before Settings");
                bool importRequested = false;
                ui.View.RequestLoadSkin = () => importRequested = true;
                Menu(); ui.ClickText(L.Get("skin.import"));
                Check(importRequested, "Settings exposes the skin import action");
                Menu(); ui.ClickText(L.Get("skin.default"));
                Check(ui.View.LibrarySettings.SelectedSkin is null && LibrarySettings.Load(config).SelectedSkin is null,
                    "Settings default selection persists");
                Menu(); ui.ClickText("Settings skin");
                ui.Key(27);
                Check(ui.View.LibraryVisible == library && before.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                    "Settings preferences preserve content and return destination");
                if (library) { ui.LoadDocument(new MapDocument()); ui.View.CloseLibrary(); }
                ui.Paint();
                var header = ui.View.SkinSelectorBounds;
                ui.Click(header.X + 8, header.Y + 8);
                Check(ui.Canvas.Texts.Any(t => t.Value == "✓ Settings skin"), $"Header selector reflects Settings selection ({locale}, library={library}, size={size}, visible={ui.View.LibraryVisible}, skin={ui.View.SkinName})");
                ui.ClickText(L.Get("skin.default"));
                ui.View.OpenVolumeDialog(); ui.Paint();
                var originalBar = ui.View.VolumeSliderBounds(1);
                ui.Click(originalBar.Right - .1f, originalBar.Y + 12);
                ui.Key(27);
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.audio"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("volume.song") + "  " + L.Get("ui.zoomPercent", 100)),
                    "Settings reflects changes from original volume control");
                ui.ClickText(L.Get("settings.skins")); Menu();
                Check(ui.Canvas.Texts.Any(t => t.Value == "Settings skin") && !ui.Canvas.Texts.Any(t => t.Value == "✓ Settings skin"),
                    "Settings reflects changes from header skin selector");
                ui.Key(27); ui.Key(27);
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
