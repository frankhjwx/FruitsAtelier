using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class SettingsTests
{
    public static void Layout()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620), (760, 580) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.GetFullPath(Path.Combine("artifacts/tests/settings-layout", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(ui.View.LibrarySettings.Workspace);
                ui.View.LibrarySettings.OsuRoot = "";
                ui.View.SupportsDisplayMode = true;
                ui.View.RequestUpdateCheck = () => { };
                ui.View.UpdateStatus = new(FruitsAtelier.App.Editor.UpdatePhase.Ready, "0.9.6", 100);
                ui.View.OpenSettings(); ui.Paint();
                foreach (string category in new[] { "settings.general", "settings.workspace", "settings.appearance", "settings.audio", "settings.testplay", "update.title" })
                {
                    var navigation = ui.Canvas.Texts.Single(t => t.Value == L.Get(category) && t.X < ui.View.SettingsBounds.X + 230);
                    ui.Click(navigation.X + 2, navigation.Y + 2);
                    var page = ui.Canvas.Texts.Single(t => t.Value == L.Get(category) && t.X >= ui.View.SettingsBounds.X + 230);
                    Check(page.Size == 24 && page.Bold, "page headings share their typography");
                    var apply = ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply"));
                    Check(apply.Size == 13 && !apply.Bold, "actions keep a regular body weight");
                    if (category == "settings.general")
                    {
                        var heading = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.dropletDefaults"));
                        Check(heading.Size == 16 && heading.Bold, "section headings use the shared size");
                        foreach (string key in new[] { "settings.derandomizeOn", "settings.newProjectDerandomizeOn" })
                        {
                            var label = ui.Canvas.Texts.Single(t => t.Value == L.Get(key));
                            Check(label.Size == 13 && label.X == page.X + 12, "droplet controls share body type and padding");
                            Check(ui.Canvas.Fills.Any(f => f.Bounds.Contains(label.X, label.Y) && f.Bounds.Height == 32
                                && f.Bounds.Right == ui.View.SettingsBounds.Right - 32), "droplet controls align to the content column");
                        }
                    }
                    if (category == "settings.general" && size.Item1 == 760)
                    {
                        float applyY = apply.Y;
                        ui.View.Wheel(page.X + 100, ui.View.SettingsBounds.Y + 200, -1200, false); ui.Paint();
                        var reverse = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.reverseCanvasScrollOff"));
                        Check(reverse.Y >= ui.View.SettingsBounds.Y + 116 && reverse.Y < ui.View.SettingsBounds.Bottom - 86,
                            "General scrolling makes the bottom preference reachable");
                        Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply")).Y == applyY, "Apply stays fixed while General scrolls");
                        ui.Click(reverse.X + 2, reverse.Y + 2);
                        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.reverseCanvasScrollOn")), "scrolled preference retains its hit target");
                    }
                    if (category == "settings.testplay")
                    {
                        Check(ui.View.TestplayBindingBounds(2).Right == ui.View.TestplayStartupDelayBounds.Right,
                            "startup delay aligns with the Dash binding right edge");
                        var combo = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.comboOn"));
                        Check(combo.MaxWidth <= 280, "Combo count uses a compact button");
                        var dim = ui.Canvas.Texts.Single(t => t.Value.EndsWith(L.Get("settings.forceBackgroundDim")));
                        Check(dim.MaxWidth <= 280, "Dim Background uses a compact button");
                    }
                    if (category == "settings.workspace")
                    {
                        var lines = ui.Canvas.Texts.Where(t => t.Y >= ui.View.SettingsBounds.Y + 128 && t.Y < ui.View.SettingsBounds.Y + 180 && t.X == page.X).ToArray();
                        string compact(string text) => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));
                        Check(compact(string.Concat(lines.Select(t => t.Value))) == compact(L.Get("library.settingsDescription")),
                            "workspace explanation retains every word when wrapped");
                    }
                    if (category == "update.title")
                    {
                        var restart = ui.Canvas.Texts.Single(t => t.Value == L.Get("update.restart"));
                        Check(restart.X + restart.MaxWidth <= ui.View.SettingsBounds.Right - 32,
                            "update actions stay inside narrow content columns");
                    }
                }
                ui.Key(27);
            }
        }
        finally { L.SetLanguage(language); }
    }

    public static void IndicatorColours()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "indicator-settings-" + Guid.NewGuid()));
        try
        {
            var ui = new Ui(false);
            ui.View.LibrarySettings.Workspace = root;
            var map = new MapDocument { DurationMs = 10000, CircleSize = 5, IsDemo = false };
            foreach (var (time, x) in new[] { (1000, 100), (1125, 120), (1250, 220), (1375, 370), (1500, 70) })
                map.Fruits.Add(new Fruit { TimeMs = time, X = x });
            ui.LoadDocument(map);
            var original = ui.View.Document.DeepClone();
            ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.indicatorColours")), "Appearance omitted indicator colours.");
            Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.language") || t.Value == L.Get("settings.romanisedLabel")),
                "Language and metadata display belong to General.");
            string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
            string[] hexes = ["#112233", "#445566", "#778899", "#AABBCC"];
            string originalStand = $"#{ui.View.LibrarySettings.StandIndicatorColour:X6}";
            ui.ClickText(L.Get(names[0]));
            var cancelHex = ui.Canvas.Texts.Last(t => t.Value == L.Get("settings.indicatorHex"));
            Check(ui.Canvas.Fills.Any(f => f.Color == ui.View.LibrarySettings.StandIndicatorColour
                && Math.Abs(f.Bounds.X - (cancelHex.X - 48)) < .1f
                && Math.Abs(f.Bounds.Y - (cancelHex.Y + 20)) < .1f),
                "Selected-colour preview was not beside the HEX field.");
            ui.Click(cancelHex.X + 400, cancelHex.Y - 262);
            Check(ui.Canvas.Texts.Last(t => t.Value.StartsWith('#')).Value != originalStand,
                "Colour picker did not update its draft before Cancel.");
            ui.ClickText(L.Get("settings.indicatorCancel"));
            ui.ClickText(L.Get(names[0]));
            Check(ui.Canvas.Texts.Last(t => t.Value.StartsWith('#')).Value == originalStand,
                "Cancel did not restore the colour from before the picker opened.");
            ui.Key(27);
            for (int i = 0; i < names.Length; i++)
            {
                ui.ClickText(L.Get(names[i]));
                var hexLabel = ui.Canvas.Texts.Last(t => t.Value == L.Get("settings.indicatorHex"));
                if (i == 0)
                {
                    string before = ui.Canvas.Texts.Last(t => t.Value.StartsWith('#')).Value;
                    float paletteX = hexLabel.X + 400, paletteY = hexLabel.Y - 262;
                    ui.View.PointerDown(paletteX, paletteY, 0, false, false);
                    ui.View.PointerMove(paletteX - 100, paletteY + 100, false, false);
                    ui.View.PointerUp(paletteX - 100, paletteY + 100, 0); ui.Paint();
                    string afterPalette = ui.Canvas.Texts.Last(t => t.Value.StartsWith('#')).Value;
                    Check(afterPalette != before,
                        "Saturation/value drag did not update the indicator colour.");
                    float hueY = hexLabel.Y - 31;
                    ui.View.PointerDown(hexLabel.X + 10, hueY, 0, false, false);
                    ui.View.PointerMove(hexLabel.X + 350, hueY, false, false);
                    ui.View.PointerUp(hexLabel.X + 350, hueY, 0); ui.Paint();
                    Check(ui.Canvas.Texts.Last(t => t.Value.StartsWith('#')).Value != afterPalette,
                        "Hue drag did not update the indicator colour.");
                }
                ui.Click(hexLabel.X + 30, hexLabel.Y + 32);
                ui.Key('A', ctrl: true); ui.Type(hexes[i]); ui.Key(13);
            }
            string path = Path.Combine(root, "settings.json");
            ui.View.ApplySettings(path); ui.Paint();
            var saved = LibrarySettings.Load(path);
            Check(saved.StandIndicatorColour == 0x112233 && saved.WalkIndicatorColour == 0x445566
                && saved.DashIndicatorColour == 0x778899 && saved.HyperDashIndicatorColour == 0xAABBCC,
                "Appearance did not persist all four indicator colours.");
            ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
            ui.ClickText(L.Get("movement.analysis"));
            foreach (uint colour in new uint[] { 0x112233, 0x445566, 0x778899, 0xAABBCC })
                Check(ui.Canvas.Lines.Any(l => l.Color == colour && l.Width == 4 && Math.Abs(l.Opacity - .65f) < .001),
                    $"Movement Analysis did not use custom indicator colour {colour:X6}.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            var reference = ui.View.DistanceSnapBaseTrackBounds;
            foreach (uint colour in new uint[] { 0x112233, 0x445566, 0x778899, 0xAABBCC })
                Check(ui.Canvas.Fills.Any(f => f.Color == colour && f.Bounds.Y == reference.Y + 3),
                    $"Distance Snap reference did not use custom indicator colour {colour:X6}.");
            ui.Key(27);
            Check(original.ContentEquals(ui.View.Document), "Changing indicator colours edited the beatmap.");
            ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
            ui.ClickText(L.Get("settings.indicatorReset"));
            ui.View.ApplySettings(path);
            saved = LibrarySettings.Load(path);
            Check(saved.StandIndicatorColour == LibrarySettings.DefaultStandIndicatorColour
                && saved.WalkIndicatorColour == LibrarySettings.DefaultWalkIndicatorColour
                && saved.DashIndicatorColour == LibrarySettings.DefaultDashIndicatorColour
                && saved.HyperDashIndicatorColour == LibrarySettings.DefaultHyperDashIndicatorColour,
                "Reset did not restore the four original indicator colours.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void ApplyState()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "settings-" + Guid.NewGuid()));
        try
        {
            foreach (bool fromLibrary in new[] { false, true })
            {
                var ui = new Ui(false);
                ui.View.LibrarySettings.Workspace = root;
                ui.View.LibrarySettings.PlaybackLineFromBottom = .6;
                if (fromLibrary)
                {
                    ui.View.ShowLibrary();
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (ui.View.LibraryLoading && DateTime.UtcNow < deadline) Thread.Sleep(10);
                    Check(!ui.View.LibraryLoading, "Library finishes loading before settings interaction");
                }
                ui.View.OpenSettings(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.language"))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.romanisedLabel")),
                    "General exposes language and metadata display.");
                uint ApplyColor() => ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply")).Color;
                uint disabled = ApplyColor();
                ui.ClickText(L.Get("library.apply"));
                Check(ApplyColor() == disabled, "Apply initially disabled");
                ui.ClickText(L.Get("settings.romanisedOn"));
                Check(ApplyColor() != disabled, "Draft change enables Apply");
                ui.ClickText(L.Get("settings.romanisedOff"));
                Check(ApplyColor() == disabled, "Reverting a change disables Apply");
                ui.ClickText(L.Get("settings.romanisedOn"));
                string path = Path.Combine(root, "settings.json");
                ui.View.ApplySettings(path); ui.Paint();
                Check(!LibrarySettings.Load(path).RomanisedMetadata, "Apply persists preference");
                Check(LibrarySettings.Load(path).PlaybackLineFromBottom == .6, "Apply preserves playback line height");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.romanisedOff")) &&
                    ApplyColor() == disabled, "Apply stays in category and resets dirty state");
                ui.ClickText(L.Get("settings.derandomizeOn"));
                Check(ApplyColor() != disabled, "Droplet default change did not enable Apply");
                ui.View.ApplySettings(path); ui.Paint();
                Check(!LibrarySettings.Load(path).DerandomizeDroplets && ApplyColor() == disabled,
                    "General droplet default did not persist");
                ui.ClickText(L.Get("settings.testplay"));
                ui.ClickText("Shift"); ui.Key(65);
                Check(ApplyColor() != disabled, "Binding change enables Apply");
                ui.View.ApplySettings(path); ui.Paint();
                Check(ApplyColor() == disabled && LibrarySettings.Load(path).TestplayDashKey == 65,
                    "Bindings apply and reset dirty state");
                var searchField = typeof(FruitsAtelier.App.Editor.EditorView).GetField("searchTask",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var pendingSearch = new TaskCompletionSource<FruitsAtelier.App.Editor.LibraryBrowser>();
                searchField.SetValue(ui.View, pendingSearch.Task);
                try
                {
                    ui.ClickText(L.Get("settings.forceBackgroundDim"));
                    Check(ApplyColor() != disabled, "Background toggle enables Apply during library search");
                    ui.View.ApplySettings(path); ui.Paint();
                    Check(LibrarySettings.Load(path).ForceBackgroundDim && ApplyColor() == disabled,
                        "Background toggle persists during library search");
                    int originalDim = ui.View.LibrarySettings.BackgroundDim;
                    var dimMinus = ui.Canvas.Texts.Single(t => t.Value == "−"
                        && t.Y > ui.View.SettingsBounds.Y + 384 && t.Y < ui.View.SettingsBounds.Y + 422);
                    ui.Click(dimMinus.X + 4, dimMinus.Y + 5);
                    Check(ApplyColor() != disabled, "Background percentage enables Apply during library search");
                    ui.View.ApplySettings(path); ui.Paint();
                    Check(LibrarySettings.Load(path).BackgroundDim == Math.Max(0, originalDim - 5) && ApplyColor() == disabled,
                        "Background percentage persists during library search");
                }
                finally { searchField.SetValue(ui.View, null); }
                ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
                Check(ui.View.LibraryVisible == fromLibrary, "Apply preserves return destination");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void Navigation()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                ui.Resize(size.Item1, size.Item2);
                var map = new MapDocument();
                map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
                ui.View.LoadDocument(map); ui.Paint();
                ui.Key('A', ctrl: true); ui.Key('D', ctrl: true);
                var before = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                double playhead = ui.View.PlayheadMs, viewport = ui.View.ViewStartMs;
                var selected = ui.View.SelectedObjectIds.ToArray();
                ui.ClickText(L.Get("library.settings"));
                Check(!ui.View.LibraryVisible, "Settings keeps the editor visible underneath");
                var bounds = ui.View.SettingsBounds;
                Check(bounds.X > 0 && bounds.Y > 0 && bounds.Right < size.Item1 && bounds.Bottom < size.Item2,
                    "Settings is inset from all window edges");
                ui.Click(2, 2);
                ui.View.Wheel(400, 300, -120, false);
                ui.Key(32); ui.Key(117); ui.Key('Z', ctrl: true);
                Check(!ui.View.TimingSetupVisible && before.ContentEquals(ui.View.Document) &&
                    playhead == ui.View.PlayheadMs && viewport == ui.View.ViewStartMs,
                    "Settings blocks background pointer, wheel and keyboard input");
                ui.ClickText(L.Get("settings.testplay"));
                ui.ClickText("Shift"); ui.Key(65);
                ui.ClickText(L.Get("settings.appearance"));
                ui.ClickText(L.Get("settings.testplay"));
                Check(ui.Canvas.Texts.Any(t => t.Value == "A"), "Category changes retain draft bindings");
                ui.Key(46); ui.Key(116);
                ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
                Check(!ui.View.LibraryVisible && !ui.View.IsTestplaying, "Close button restores editor");
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty &&
                    playhead == ui.View.PlayheadMs && viewport == ui.View.ViewStartMs &&
                    selected.SequenceEqual(ui.View.SelectedObjectIds), "Settings preserve editor state");
                ui.ClickText(L.Get("library.settings")); ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
                Check(!ui.View.LibraryVisible, "Close button restores editor");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.Fruits.Count == before.Fruits.Count - 1, "Undo history survives settings");
                ui.View.MarkSaved(); ui.View.ShowLibrary(); ui.Paint();
                ui.ClickText(L.Get("library.settings")); ui.Key(27);
                Check(ui.View.LibraryVisible && !ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")),
                    "Escape restores library");
                ui.ClickText(L.Get("library.settings")); ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
                Check(ui.View.LibraryVisible && !ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")),
                    "Close button restores library");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
