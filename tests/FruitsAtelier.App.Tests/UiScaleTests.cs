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
            ShortcutsAndHighlight(root, path);
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

    private static void ShortcutsAndHighlight(string root, string path)
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(locale);
            var ui = new Ui();
            ui.View.LibrarySettings.Workspace = root;
            int saves = 0;
            ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
            var before = ui.View.Document.DeepClone();
            bool dirty = ui.View.IsDirty;
            var track = ui.View.Document.Tracks.First();
            ui.SelectTrack(track.Id);
            int spans = track.SpanCount;
            ui.Key(187, ctrl: true);
            Check(ui.View.Document.Tracks.First(t => t.Id == track.Id).SpanCount == spans
                && ui.View.LibrarySettings.UiScalePercent == 175, "Main-row scale shortcut changed slider reverses.");
            ui.Key(189, ctrl: true);
            // Keypad bindings work even with an existing slider selection.
            ui.Key(107, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 175 && LibrarySettings.Load(path).UiScalePercent == 175,
                "Keypad zoom did not activate and persist.");
            ui.Key(109, ctrl: true);
            ui.Key(187, ctrl: true, shift: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 175, "Ctrl+plus did not resize with a slider selected.");
            ui.Key('2'); ui.Key('1');
            ui.Key(189, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 150, "Main-row minus did not resize without a slider.");
            ui.Key(187, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 175, "Main-row equals did not resize without a slider.");
            for (int i = 0; i < 10; i++) ui.Key(107, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 200, "Shortcut exceeded maximum scale.");
            int previousSaves = saves;
            ui.Key(107, ctrl: true);
            Check(saves == previousSaves, "Clamped shortcut saved unchanged preferences.");
            for (int i = 0; i < 10; i++) ui.Key(109, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 50, "Shortcut exceeded minimum scale.");
            Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Scale shortcut changed content.");
            ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
            ui.Key(107, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 50, "Shortcut changed scale inside Settings.");
            ui.ClickText(L.Get("settings.selectionHighlight"));
            var hex = ui.Canvas.Texts.Last(t => t.Value == "#59D3C3");
            ui.Click(hex.X + 4, hex.Y + 5); ui.Key(65, ctrl: true); ui.Type("#FF33AA");
            ui.ClickText(L.Get("settings.indicatorDone"));
            Check(ui.View.LibrarySettings.SelectionHighlightColour == LibrarySettings.DefaultSelectionHighlightColour,
                "Colour draft applied early.");
            ui.Key(27); ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.appearance"));
            Check(ui.Canvas.Texts.Any(t => t.Value == "#59D3C3"), "Cancelled colour draft survived.");
            ui.ClickText(L.Get("settings.selectionHighlight"));
            hex = ui.Canvas.Texts.Last(t => t.Value == "#59D3C3");
            ui.Click(hex.X + 4, hex.Y + 5); ui.Key(65, ctrl: true); ui.Type("#FF33AA");
            ui.ClickText(L.Get("settings.indicatorDone"));
            ui.View.ApplySettings(path); ui.Key(27);
            Check(LibrarySettings.Load(path).SelectionHighlightColour == 0xFF33AA, "Highlight did not persist.");
            ui.Key(65, ctrl: true);
            Check(ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0xFF33AA), "Catch selection ignored the preference.");
            ui.View.ToggleOsuCanvas(); ui.Key(65, ctrl: true);
            Check(ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0xFFA600), "osu selection lost its orange ring.");
            Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Highlight setting changed content.");
            ui.View.LibrarySettings.UiScalePercent = 150;
            ui.View.ShowLibrary(); ui.Paint();
            ui.Key(187, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 175, "Library did not accept the scale shortcut.");
            ui.Key('F', ctrl: true); ui.Key(189, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == 175, "Library search lost input isolation.");
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
