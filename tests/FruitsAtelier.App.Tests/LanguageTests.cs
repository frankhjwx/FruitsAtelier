using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class LanguageTests
{
    public static void SwitchWithoutEditing()
    {
        Strings.SetLanguage("zh-CN");
        try
        {
            var ui = new Ui();
            var baseline = ui.View.Document.DeepClone();
            ui.ClickText("中文 / EN");
            Check(Strings.Language == "en", "Language button did not select English.");
            Check(ui.Canvas.Texts.Any(t => t.Value == "File") && ui.Canvas.Texts.Any(t => t.Value == "Objects"), "English chrome is missing.");
            Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("尚未") || t.Value == "对象" || t.Value == "未加载音频"), "Chinese chrome or audio placeholder remained.");
            ui.Key('F'); ui.ClickMap(1250, 480);
            Check(ui.View.StatusMessage.All(c => c < 0x4e00 || c > 0x9fff), "Fruit status was not English.");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty, "Language changes entered content history.");
            ui.ClickText("中文 / EN");
            Check(Strings.Language == "zh-CN" && ui.Canvas.Texts.Any(t => t.Value == "文件"), "Chinese did not restore.");
        }
        finally { Strings.SetLanguage("zh-CN"); }
    }

    public static void EnglishMultiMenusAndDiagnostics()
    {
        Strings.SetLanguage("en");
        try
        {
            var ui = new Ui();
            var map = new MapDocument { Name = "Language fixture", DurationMs = 10000 };
            map.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
            map.Fruits.Add(new Fruit { TimeMs = 1500, X = 350 });
            ui.LoadDocument(map); ui.Paint();
            ui.ClickMap(1000, 100);
            var p = Screen(ui, 1500, 350);
            ui.View.PointerDown(p.X, p.Y, 0, false, true); ui.View.PointerUp(p.X, p.Y, 0); ui.Paint();
            ui.View.PointerDown(p.X, p.Y, 2, false, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == "Copy") && ui.View.SelectedObjectIds.Count == 2, "English menu lost its selection or translation.");
            ui.ClickText("Copy"); ui.Key('V', ctrl: true);
            Check(ui.View.Document.Fruits.Count == 4 && ui.View.SelectedObjectIds.Count == 2, "English batch copy/paste did not work.");
            var bad = new CurveTrack(); bad.Nodes.Add(new Anchor { TimeMs = 500, X = 100 });
            ui.View.Document.Tracks.Add(bad); ui.Paint();
            var english = CurveMath.Validate(ui.View.Document);
            Check(english.Count > 0 && english.All(m => !m.Any(c => c >= 0x4e00 && c <= 0x9fff)), "Core diagnostics retained Chinese.");
            Strings.SetLanguage("zh-CN"); ui.Paint();
            Check(CurveMath.Validate(ui.View.Document).Any(m => m.Any(c => c >= 0x4e00 && c <= 0x9fff)), "Core diagnostics did not switch back.");
        }
        finally { Strings.SetLanguage("zh-CN"); }
    }

    private static (float X, float Y) Screen(Ui ui, double time, double x) =>
        (ui.Plot.X + (float)(x / 512) * ui.Plot.Width, ui.Plot.Bottom - (float)((time - ui.View.ViewStartMs) * ui.View.PixelsPerMs));
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
}
