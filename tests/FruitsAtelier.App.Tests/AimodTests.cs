using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
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
                ui.ClickText(L.Get("aimod.overlap")); ui.Key(13);
                Check(!ui.View.AimodVisible && ui.View.PlayheadMs == 1000
                    && ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Select(f => f.Id)), "Error must locate and select both objects.");
                Check(map.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Navigation must not edit content.");
                ui.Key(46);
                ui.Key('A', ctrl: true, shift: true); ui.Paint();
                Check(ui.View.AimodVisible, "Ctrl+Shift+A opens AiMod");
                ui.ClickText(L.Get("aimod.tabCompose", 0));
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

    public static void TableAndDock()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(); ui.Resize(1440, 900);
                var map = new MapDocument { DurationMs = 40000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1005, X = 400 }, new() { TimeMs = 40000, X = 200 }]);
                ui.LoadDocument(map); ui.Key('A', ctrl: true, shift: true);
                string copied = ""; ui.View.RequestCopyText = text => copied = text;
                ui.ClickText(L.Get("aimod.overlap")); ui.Key('C', ctrl: true);
                Check(copied.Contains(L.Get("aimod.overlap")) && copied.Contains("00:01:000") && ui.View.AimodVisible, "Selected row copies a complete TSV report without navigating");
                var secondRow = ui.View.AimodRowBounds(1);
                ui.View.PointerDown(secondRow.X + 15, secondRow.Y + 10, 0, true, false);
                ui.View.PointerUp(secondRow.X + 15, secondRow.Y + 10, 0); ui.Key('C', ctrl: true);
                Check(copied.Split(Environment.NewLine).Length == 3, "Shift-click copies exactly the selected row range");
                var header = ui.View.AimodHeaderBounds(1);
                Drag(header.Right, header.Y + 12, header.Right + 60, header.Y + 12);
                Check(Math.Abs(ui.View.AimodHeaderBounds(1).Width - header.Width - 60) < 1, "Column boundary resizes independently");
                var resized = ui.View.AimodHeaderBounds(1);
                ui.View.PointerDown(resized.Right, resized.Y + 10, 0, false, false);
                ui.View.PointerMove(resized.Right + 30, resized.Y + 10, false, false); ui.Key(27);
                Check(!ui.View.WantsCapture && ui.View.AimodHeaderBounds(1).Width == resized.Width, "Escape cancels a column resize without closing the report");
                header = ui.View.AimodHeaderBounds(1);
                Drag(header.X + 30, header.Y + 12, ui.View.AimodHeaderBounds(0).X + 5, header.Y + 12);
                ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                Check(copied.StartsWith(L.Get("aimod.columnInformation") + "\t") && copied.Contains(L.Get("aimod.noBackground")), "Column reorder is reflected in copied selected rows");
                ui.ClickText(L.Get("aimod.attach"));
                Check(ui.View.AimodDocked && !ui.View.AimodVisible, "AiMod attaches as an independent dock tab");
                var layout = ui.View.CaptureDockLayouts();
                Check(layout.Mapping!.Right!.Panels!.Contains("Aimod"), "Dock layout persists AiMod identity");
                ui.Key(27); ui.ClickMap(15000, 250); ui.Key('A', ctrl: true, shift: true);
                Check(ui.View.AimodDocked && !ui.View.AimodVisible, "Shortcut focuses and refreshes an attached report");
                var tab = ui.View.DockTabBounds(EditorView.DockPanel.Aimod);
                Drag(tab.X + 15, tab.Y + 12, 8, 200);
                Check(ui.View.AimodDocked && ui.View.AimodPanelBounds.X < ui.Plot.X, "AiMod can attach on the left");
                ui.ClickText(L.Get("aimod.detach"));
                Check(ui.View.AimodVisible && !ui.View.AimodDocked, "Float returns the report to its own panel");
                ui.Key(27);
                Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty && !ui.View.WantsCapture, "Report selection, copying and docking preserve beatmap and history");
                ui.Key('A', ctrl: true, shift: true); ui.ClickText(L.Get("aimod.attach"));
                Check(ui.View.AddDifficulty(), "Add another difficulty while AiMod is docked"); ui.Paint();
                Check(ui.View.AimodIssues.Count == 0 && ui.View.AimodDocked, "Difficulty changes retain the panel and discard previous findings");
                ui.View.NewProject();

                void Drag(float x, float y, float endX, float endY)
                {
                    ui.View.PointerDown(x, y, 0, false, false);
                    ui.View.PointerMove(endX, endY, false, false); ui.Paint();
                    ui.View.PointerUp(endX, endY, 0); ui.Paint();
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
