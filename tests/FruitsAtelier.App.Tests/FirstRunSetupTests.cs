using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class FirstRunSetupTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/first-run", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            foreach (uint state in new[] { 0u, 0x10000000u, 0x18000000u })
            {
                uint originalStyle = Native.WindowStyle | state;
                uint setupStyle = EditorWindow.FirstRunWindowStyle(originalStyle, true);
                Check((setupStyle & 0x18000000u) == state && (setupStyle & Native.WindowStyle) == 0,
                    "changing setup decorations preserves window visibility and disabled state");
                Check(EditorWindow.FirstRunWindowStyle(setupStyle, false) == originalStyle,
                    "Start restores editor decorations without hiding the window");
            }
            string missing = Path.Combine(root, "missing.json");
            var fresh = LibrarySettings.Load(missing);
            Check(!fresh.FirstRunSetupCompleted && fresh.MasterVolume == 50 && fresh.SongVolume == 50 && fresh.HitsoundVolume == 50,
                "fresh installations start with setup and three half-volume preferences");
            string legacy = Path.Combine(root, "legacy.json");
            File.WriteAllText(legacy, "{\"MasterVolume\":72,\"SongVolume\":34,\"HitsoundVolume\":91}");
            var upgraded = LibrarySettings.Load(legacy);
            Check(!upgraded.FirstRunSetupCompleted && upgraded.MasterVolume == 72 && upgraded.SongVolume == 34 && upgraded.HitsoundVolume == 91,
                "existing installations require this setup version while retaining their stored preferences");
            string partial = Path.Combine(root, "partial.json");
            File.WriteAllText(partial, "{\"MasterVolume\":0,\"SongVolume\":83}");
            var partialSettings = LibrarySettings.Load(partial);
            Check(partialSettings.MasterVolume == 0 && partialSettings.SongVolume == 83 && partialSettings.HitsoundVolume == 50,
                "saved volumes including mute load unchanged and only missing channels default to 50 percent");
            foreach (double scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
            {
                var size = EditorView.FirstRunWindowSize(1920, 1040, scale);
                Check(size.Width * scale <= 1920 * .85 && size.Height * scale <= 1040 * .85,
                    "the compact window fits 1080p work areas at common DPI scales");
            }

            foreach (string locale in new[] { "en", "zh-CN", "zh-TW", "ja" })
            foreach (var size in new[] { (880, 620), (980, 620) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                string workspace = Path.Combine(root, locale + size.Item1);
                string settings = Path.Combine(workspace, "settings.json");
                ui.View.LibrarySettings.Workspace = workspace;
                ui.View.LibrarySettings.FirstRunSetupCompleted = false;
                ui.View.LibrarySettings.MasterVolume = 50;
                ui.View.LibrarySettings.SongVolume = 50;
                ui.View.LibrarySettings.HitsoundVolume = 50;
                var original = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                var audio = new List<SetupAudioCommand>();
                var samples = new List<Hitsound>();
                var links = new List<string>();
                ui.View.RequestSetupAudio = audio.Add;
                ui.View.RequestAuditionHitsound = samples.Add;
                ui.View.RequestSetupLink = links.Add;
                ui.View.BeginFirstRunSetup(settings); ui.Paint();
                Check(L.Language == "en", "unfinished setup starts in English regardless of the previous language");
                L.SetLanguage(locale); ui.Paint();
                Check(audio.Count == 0, "entering setup never autoplays");
                Check(ui.Canvas.Texts.Count(t => t.Value.StartsWith("1. ", StringComparison.Ordinal) || t.Value.StartsWith("2. ", StringComparison.Ordinal) ||
                    t.Value.StartsWith("3. ", StringComparison.Ordinal) || t.Value.StartsWith("4. ", StringComparison.Ordinal) || t.Value.StartsWith("5. ", StringComparison.Ordinal)) == 5,
                    "the guide has five progress tabs");
                float tabWidth = size.Item1 / 5f;
                uint ColourAt(float x, float y) => ui.Canvas.Fills.Last(fill => fill.Bounds.Contains(x, y)).Color;
                uint firstInactive = ColourAt(tabWidth * 1.5f, 23), secondInactive = ColourAt(tabWidth * 2.5f, 23);
                Check(firstInactive != secondInactive && ColourAt(tabWidth * 3.5f, 23) == firstInactive &&
                    ColourAt(tabWidth * 4.5f, 23) == secondInactive, "inactive progress tabs alternate two colours");
                for (float x = .5f; x < size.Item1; x += 4)
                foreach (float y in new[] { .5f, 23, 45.5f })
                    Check(ColourAt(x, y) != 0x171A20, "the progress strip exposes no window background between tabs");
                var languageLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.language"));
                var metadataLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.romanisedLabel"));
                Check(metadataLabel.Y > languageLabel.Y, "Artist/Title configuration appears below Language on the first step");
                ui.ClickText(L.Get("settings.romanisedOn"));
                var languageButton = ui.Canvas.Texts.Single(t => t.Value.EndsWith(" ▾", StringComparison.Ordinal));
                ui.Click(languageButton.X + 4, languageButton.Y + 5);
                Check(!ui.View.FirstRunHeaderDraggable, "language menus can receive input where they overlap the draggable header");
                ui.Key(27);
                Check(ui.View.FirstRunSetupVisible && ui.View.FirstRunHeaderDraggable, "Escape dismisses the language menu before exiting setup");
                ui.View.SetLibraryFolder(true, "\0");
                ui.ClickText(L.Get("setup.next"));
                Check(ui.View.FirstRunStep == 0 && !File.Exists(settings), "invalid roots prevent advancing");
                ui.View.SetLibraryFolder(true, workspace);
                ui.ClickText(L.Get("setup.next"));
                Check(ui.View.FirstRunStep == 1 && !File.Exists(settings), "step navigation does not save settings");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.skinSoundsHint")), "setup omits the skin sound explanatory paragraph");
                ui.ClickText(L.Get("setup.downloadSkin"));
                Check(links.Single() == "https://osu.ppy.sh/community/forums/topics/1411279?n=1", "default skin link matches the download page");
                ui.ClickText(L.Get("setup.next"));
                Check(ui.View.FirstRunStep == 2 && audio.All(c => c == SetupAudioCommand.Stop), "audio step stays stopped");
                var hitsoundTitle = ui.Canvas.Texts.Single(t => t.Value == "Hitsound Test");
                Check(new[] { "Hit", "Whistle", "Finish", "Clap" }.All(name => ui.Canvas.Texts.Any(t => t.Value == name && t.Y > hitsoundTitle.Y)),
                    "hitsound names retain English below the Hitsound Test heading");
                ClickAudio(0);
                Check(audio[^1] == SetupAudioCommand.Play, "Play requests music playback");
                ui.View.UpdateSetupAudio(1200, 3000, true, false, null); ui.Paint();
                ClickAudio(1);
                Check(audio[^1] == SetupAudioCommand.Pause, "Pause uses the separate audition transport");
                ClickAudio(2);
                Check(audio[^1] == SetupAudioCommand.Stop, "Stop ends the music test");
                foreach (string sound in new[] { "hitnormal", "hitwhistle", "hitfinish", "hitclap" })
                {
                    var label = ui.Canvas.Texts.Last(t => t.Value == L.Get("timing." + sound));
                    ui.Click(label.X + 4, label.Y + 5);
                }
                Check(samples.Select(s => s.Name).SequenceEqual(new[] { "hitnormal", "hitwhistle", "hitfinish", "hitclap" }), "sample buttons resolve all four hitsounds");
                var volume = ui.View.VolumeSliderBounds(1);
                ui.Click(volume.X + volume.Width * .7f, volume.Y + 12);
                Check(ui.View.LibrarySettings.SongVolume == 70, "setup volume sliders accept input");
                ui.ClickText(L.Get("setup.back"));
                Check(ui.View.FirstRunStep == 1 && audio[^1] == SetupAudioCommand.Stop, "leaving the audio step stops playback");
                ui.ClickText(L.Get("setup.next"));
                ui.ClickText(L.Get("setup.next"));
                ui.ClickText(L.Get("settings.newProjectDerandomizeOn"));
                ui.ClickText(L.Get("setup.next"));
                Check(ui.View.FirstRunStep == 4, "Slider Droplets advances directly to Testplay");
                ui.ClickText("←"); ui.Key(65);
                Check(ui.View.TestplayBindingBounds(2).Right == ui.View.TestplayStartupDelayBounds.Right,
                    "the setup lead-in ends at the Dash button edge");
                Check(!ui.View.CapturingTestplayKey, "testplay bindings capture keys inside setup");
                ui.ClickText(L.Get("setup.finish"));
                Check(ui.View.FirstRunStep == 5 && !ui.Canvas.Texts.Any(t => t.Value == L.Get("setup.step", 1, L.Get("setup.paths"))), "completion removes the progress bar");
                Check(!ui.Canvas.Images.Any(i => Path.GetFileName(i.Path) == "discord.png"), "completion omits the Discord logo");
                var welcome = ui.Canvas.Texts.Single(t => t.Value == L.Get("setup.discordHint"));
                var join = ui.Canvas.Texts.Single(t => t.Value == L.Get("setup.discord"));
                var start = ui.Canvas.Texts.Single(t => t.Value == L.Get("setup.start"));
                var exit = ui.Canvas.Texts.Single(t => t.Value == L.Get("setup.exit"));
                Check(Math.Abs(welcome.Y - join.Y) < 4 && start.Y > join.Y + 80 && start.Y == exit.Y,
                    "welcome and Discord join share a row with padded Start and Exit buttons below");
                var saved = LibrarySettings.Load(settings);
                Check(saved.FirstRunSetupCompleted && !saved.RomanisedMetadata && !saved.DerandomizeNewProjects && saved.TestplayLeftKey == 65 && saved.SongVolume == 70,
                    "completion persists each chosen preference");
                ui.ClickText(L.Get("setup.discord"));
                Check(links[^1] == "https://discord.gg/ur9QKs4EG2", "completion opens the requested Discord invite");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "setup preserves beatmap content and dirty state");
                ui.ClickText(L.Get("setup.start"));
                Check(!ui.View.FirstRunSetupVisible, "start leaves the completion screen");
                ui.View.BeginFirstRunSetup(settings); ui.Paint();
                Check(ui.View.FirstRunStep == 0 && ui.View.LibrarySettings.SongVolume == 70, "debug replay starts again and retains preferences");
                ui.View.StopFileMonitoring();
                ui.View.CancelFirstRunSetup();
                void ClickAudio(int index)
                {
                    var bounds = ui.View.SetupAudioButtonBounds(index);
                    ui.Click(bounds.X + 10, bounds.Y + 10);
                }
            }
            var cancel = new Ui(false); cancel.Resize(640, 440);
            cancel.View.LibrarySettings.Workspace = Path.Combine(root, "cancel");
            cancel.View.LibrarySettings.MasterVolume = 72;
            cancel.View.LibrarySettings.SongVolume = 34;
            cancel.View.LibrarySettings.HitsoundVolume = 91;
            var beforeSettings = cancel.View.LibrarySettings;
            string beforeLanguage = L.Language;
            string canceledPath = Path.Combine(root, "cancel.json");
            int preferenceWrites = 0;
            cancel.View.RequestAudioPreference = () => preferenceWrites++;
            cancel.View.RequestSkinPreference = () => preferenceWrites++;
            cancel.View.RequestLanguagePreference = _ => preferenceWrites++;
            cancel.View.BeginFirstRunSetup(canceledPath); cancel.Paint();
            Check(cancel.View.LibrarySettings.MasterVolume == 72 && cancel.View.LibrarySettings.SongVolume == 34 &&
                cancel.View.LibrarySettings.HitsoundVolume == 91 && !ReferenceEquals(beforeSettings, cancel.View.LibrarySettings),
                "unfinished setup preserves saved volumes in an isolated preference draft");
            cancel.ClickText(L.Get("setup.next")); cancel.ClickText(L.Get("setup.next"));
            cancel.View.Wheel(100, 200, -1200, false); cancel.Paint();
            var play = cancel.View.SetupAudioButtonBounds(0);
            Check(play.Y >= cancel.View.SettingsBounds.Y + 116 && play.Bottom < cancel.View.SettingsBounds.Bottom - 86,
                "short windows can scroll to audio controls without moving navigation");
            var slider = cancel.View.VolumeSliderBounds(0);
            cancel.View.Wheel(100, 200, 1200, false); cancel.Paint();
            slider = cancel.View.VolumeSliderBounds(0);
            cancel.Click(slider.X + slider.Width * .2f, slider.Y + 12);
            cancel.ClickText(L.Get("setup.exit"));
            Check(ReferenceEquals(beforeSettings, cancel.View.LibrarySettings) && beforeSettings.MasterVolume == 72 &&
                beforeSettings.SongVolume == 34 && beforeSettings.HitsoundVolume == 91 && L.Language == beforeLanguage && preferenceWrites == 0 && !File.Exists(canceledPath),
                "Exit discards every preference and leaves setup incomplete without saving");
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
