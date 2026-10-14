using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SongDockTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                var map = new MapDocument { Name = "Song", IsDemo = false };
                SongSetup.Set(map, "Metadata", "TitleUnicode", "Song");
                SongSetup.Set(map, "Metadata", "Title", "Song");
                SongSetup.Set(map, "Metadata", "Version", "Normal");
                ui.LoadDocument(map); ui.Key(115); ui.Paint();
                if (size.Item1 == 980)
                {
                    var resize = ui.View.DockResizeBounds(false);
                    ui.View.PointerDown(resize.X + 4, resize.Y + 60, 0, false, false);
                    ui.View.PointerMove(ui.Width - 180, resize.Y + 60, false, false);
                    ui.View.PointerUp(ui.Width - 180, resize.Y + 60, 0); ui.Paint();
                }
                Check(!ui.View.SongSetupVisible && ui.View.DockTabBounds(EditorView.DockPanel.SongSetup).Width > 0,
                    "F4 selects docked Song Setup without a modal");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("song.title") && t.Y < 40), "Header Song Setup entry is removed");
                var conversion = ui.View.Conversion;
                Set("TitleUnicode", "Draft title");
                Check(ui.View.Document.ContentEquals(map) && ReferenceEquals(ui.View.Conversion, conversion), "Typing is an isolated draft");
                Bottom(); ui.ClickText(L.Get("mac.cancel"));
                Check(ui.View.Document.ContentEquals(map), "Cancel preserves content");
                Set("TitleUnicode", "Updated title");
                Bottom(); ui.ClickText(L.Get("library.apply"));
                Check(ui.View.Document.Name == "Updated title" && !ui.View.SongSetupVisible, "Apply commits without opening a modal");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Dock Apply undoes in one step");
                ui.View.ShowSongDock(); ui.Paint(); ui.ClickText(L.Get("song.general") + " ▾"); ui.ClickText(L.Get("song.difficulty"));
                Set("CircleSize", "4.5"); Bottom(); ui.ClickText(L.Get("library.apply"));
                Check(ui.View.Document.CircleSize == 4.5, "Dock difficulty fields apply");
                ui.View.ShowSongDock(); ui.Paint(); ui.ClickText(L.Get("song.general") + " ▾"); ui.ClickText(L.Get("song.colours"));
                ui.ClickText(L.Get("song.customColours")); Set("Hex", "#123ABC");
                Bottom(); ui.ClickText(L.Get("library.apply"));
                Check(SongSetup.Colours(ui.View.Document)[0] == 0x123ABC, "Dock palette HEX applies");
                ui.View.ShowSongDock(); ui.Paint();
                var tab = ui.View.DockTabBounds(EditorView.DockPanel.SongSetup);
                ui.View.PointerDown(tab.X + 10, tab.Y + 10, 0, false, false);
                ui.View.PointerMove(8, 250, false, false); ui.View.PointerUp(8, 250, 0); ui.Paint();
                Check(ui.View.DockTabBounds(EditorView.DockPanel.SongSetup).X < ui.Width / 2, "Song Setup docks on the left");
                var before = ui.View.Document.DeepClone();
                ui.Key(114); ui.Key(27); ui.Paint();
                Check(ui.View.Document.ContentEquals(before) && ui.View.DockTabBounds(EditorView.DockPanel.SongSetup).X < ui.Width / 2,
                    "Timing switch retains layout and content");

                void Bottom() { ui.View.Wheel(ui.Width - 60, 350, -12000, false, false); ui.Paint(); }
                void Set(string key, string value)
                {
                    ui.View.Wheel(ui.Width - 60, 350, 12000, false, false); ui.Paint();
                    for (int i = 0; i < 25; i++)
                    {
                        var field = ui.View.SongSetupFieldBounds[key];
                        if (field.Y >= 168 && field.Bottom < ui.Height - 124)
                        {
                            ui.Click(field.X + 8, field.Y + 12); ui.Key('A', ctrl: true);
                            ui.View.PasteSongSetupText(value, ui.View.SongSetupInputSession); ui.Paint(); return;
                        }
                        ui.View.Wheel(ui.Width - 60, 350, -120, false, false); ui.Paint();
                    }
                    throw new InvalidOperationException("Could not reach Song Setup field: " + key);
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
