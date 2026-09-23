using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class SettingsTests
{
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
            ui.ClickText(L.Get("library.editor"));
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
                uint ApplyColor() => ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply")).Color;
                uint disabled = ApplyColor();
                ui.ClickText(L.Get("library.apply"));
                Check(ApplyColor() == disabled, "Apply initially disabled");
                ui.ClickText(L.Get("settings.appearance"));
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
                ui.ClickText(L.Get("settings.testplay"));
                ui.ClickText("Shift"); ui.Key(65);
                Check(ApplyColor() != disabled, "Binding change enables Apply");
                ui.View.ApplySettings(path); ui.Paint();
                Check(ApplyColor() == disabled && LibrarySettings.Load(path).TestplayDashKey == 65,
                    "Bindings apply and reset dirty state");
                ui.ClickText(L.Get(fromLibrary ? "library.back" : "library.editor"));
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
                ui.ClickText(L.Get("settings.testplay"));
                ui.ClickText("Shift"); ui.Key(65);
                ui.ClickText(L.Get("settings.appearance"));
                ui.ClickText(L.Get("settings.testplay"));
                Check(ui.Canvas.Texts.Any(t => t.Value == "A"), "Category changes retain draft bindings");
                ui.Key(46); ui.Key(116);
                ui.ClickText(L.Get("library.editor"));
                Check(!ui.View.LibraryVisible && !ui.View.IsTestplaying, "Return button restores editor");
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty &&
                    playhead == ui.View.PlayheadMs && viewport == ui.View.ViewStartMs &&
                    selected.SequenceEqual(ui.View.SelectedObjectIds), "Settings preserve editor state");
                ui.ClickText(L.Get("library.settings")); ui.Key(27);
                Check(!ui.View.LibraryVisible, "Escape restores editor");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.Fruits.Count == before.Fruits.Count - 1, "Undo history survives settings");
                ui.View.MarkSaved(); ui.View.ShowLibrary(); ui.Paint();
                ui.ClickText(L.Get("library.settings")); ui.Key(27);
                Check(ui.View.LibraryVisible && !ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")),
                    "Escape restores library");
                ui.ClickText(L.Get("library.settings")); ui.ClickText(L.Get("library.back"));
                Check(ui.View.LibraryVisible && !ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")),
                    "Return button restores library");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
