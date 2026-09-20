using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class SettingsTests
{
    public static void ApplyState()
    {
        string root = Path.Combine(Path.GetTempPath(), "FruitsAtelier-settings-" + Guid.NewGuid());
        try
        {
            foreach (bool fromLibrary in new[] { false, true })
            {
                var ui = new Ui(false);
                ui.View.LibrarySettings.Workspace = root;
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
