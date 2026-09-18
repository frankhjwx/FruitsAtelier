using L = FruitsAtelier.Localization.Strings;

internal static class GridLevelMenuTests
{
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(false); ui.Resize(980, 620);
            var before = ui.View.Document.DeepClone();
            double time = ui.View.PlayheadMs;
            void Hover(string label)
            {
                var text = ui.Canvas.Texts.Single(t => t.Value == label);
                ui.View.PointerMove(text.X + 4, text.Y + 4, false, false); ui.Paint();
            }
            foreach (int size in new[] { 8, 16, 32, 4 })
            {
                ui.ClickText(L.Get("ui.view"));
                var parent = ui.Canvas.Texts.Single(t => t.Value.StartsWith(L.Get("ui.gridLevel", "")));
                Hover(parent.Value);
                var child = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.grid" + size));
                if (child.X < parent.X + 270 || !ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.follow")))
                    throw new Exception("Grid submenu must open beside the visible View menu.");
                // Cross the parent padding before entering the secondary popup.
                ui.View.PointerMove(495, parent.Y + 4, false, false); ui.Paint();
                Hover(L.Get("ui.grid" + size));
                ui.ClickText(L.Get("ui.grid" + size));
                if (ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.follow"))) throw new Exception("Choosing a level must dismiss both menus.");
                ui.ClickText(L.Get("ui.view"));
                Hover(L.Get("ui.gridLevel", L.Get("ui.grid" + size)));
                var selected = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.grid" + size));
                if (!ui.Canvas.Fills.Any(f => f.Color == 0x31494B && f.Bounds.Contains(selected.X, selected.Y)))
                    throw new Exception("Current grid level must be marked.");
                Hover(L.Get("ui.follow"));
                if (ui.Canvas.Texts.Any(t => t.Value == L.Get("ui.grid" + size))) throw new Exception("Hovering another parent row must close the submenu.");
                ui.Key(27);
            }
            ui.ClickText(L.Get("ui.view")); Hover(L.Get("ui.gridLevel", L.Get("ui.grid4")));
            ui.Click(ui.Plot.X + 100, ui.Plot.Bottom - 50);
            if (ui.View.PlayheadMs != time || !before.ContentEquals(ui.View.Document) || ui.View.IsDirty)
                throw new Exception("Grid settings or dismissing the menu changed the map or playhead.");
        }
    }
}
