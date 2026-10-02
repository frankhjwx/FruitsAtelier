using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class AimodTests
{
    public static void Run()
    {
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                var map = new MapDocument { DurationMs = 10000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1005, X = 400 }]);
                var ui = new Ui(); ui.LoadDocument(map);
                bool dirty = ui.View.IsDirty;
                ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("aimod.title"));
                Check(ui.View.AimodVisible && ui.View.AimodErrors.Count == 1, "Menu must open an initial check.");
                ui.Key(46); ui.Key('Z', ctrl: true); ui.Key('2'); ui.Key(116); ui.ClickMap(4000, 200);
                ui.View.PointerDoubleClick(ui.Plot.X + 20, ui.Plot.Y + 20, false, false); ui.Type("123");
                Check(map.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty && !ui.View.IsTestplaying,
                    "AiMod must isolate editing and preserve content and dirty state.");
                ui.Resize(800, 600);
                ui.ClickText(L.Get("aimod.refresh"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("aimod.overlap")), "Localized error must remain visible in a narrow window.");
                ui.ClickText(L.Get("aimod.overlap"));
                Check(!ui.View.AimodVisible && ui.View.PlayheadMs == 1000
                    && ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Select(f => f.Id)), "Error must locate and select both objects.");
                Check(map.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Navigation must not edit content.");
                ui.Key(46);
                ui.View.ShowAimod(); ui.Paint();
                Check(ui.View.AimodErrors.Count == 0 && ui.Canvas.Texts.Any(t => t.Value == L.Get("aimod.empty")), "Reopening must inspect current content.");
                ui.Key(27); ui.Key('Z', ctrl: true);
                Check(map.ContentEquals(ui.View.Document), "AiMod must leave deletion undo intact.");
                ui.View.ShowAimod(); ui.Paint();
                ui.View.LoadDocument(new MapDocument());
                Check(!ui.View.AimodVisible, "Switching documents must discard old results.");
                ui.View.NewProject();
            }
        }
        finally { L.SetLanguage(previous); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
