using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DisplaySettingsTests
{
    public static void Run()
    {
        string originalLanguage = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "display-settings-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        Check(!LibrarySettings.Load(path).LowLatencyDisplay, "Older preferences must default to VSync.");
        Check(!LibrarySettings.Load(path).ReverseCanvasScroll, "Older preferences must default to normal canvas scrolling.");
        try
        {
            foreach (string language in L.AvailableLanguages)
            {
                L.SetLanguage(language);
                var ui = new Ui(false);
                ui.View.LibrarySettings.Workspace = root;
                var original = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                ui.View.OpenSettings(); ui.Paint();
                ui.ClickText(L.Get("settings.reverseCanvasScrollOff"));
                Check(!ui.View.LibrarySettings.ReverseCanvasScroll, "Scroll draft applied before Apply.");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.reverseCanvasScrollOff")), "Closing retained an unapplied scroll draft.");
                ui.ClickText(L.Get("settings.reverseCanvasScrollOff"));
                ui.ClickText(L.Get("settings.appearance")); ui.ClickText(L.Get("settings.general"));
                ui.View.ApplySettings(path);
                Check(LibrarySettings.Load(path).ReverseCanvasScroll, "Reverse canvas scroll was not saved.");
                ui.Key(27);
                foreach (bool shift in new[] { false, true })
                {
                    ui.View.UpdateTransport(3000, 10000, true, false, false, null, null); ui.Paint();
                    ui.View.Wheel(ui.Plot.X + 10, ui.Plot.Y + 20, 120, false, shift); ui.Paint();
                    Check(ui.View.PlayheadMs > 3000, "Reverse canvas wheel did not move later.");
                    ui.View.Wheel(ui.Plot.X + 10, ui.Plot.Y + 20, -120, false, shift); ui.Paint();
                    Check(Math.Abs(ui.View.PlayheadMs - 3000) < .001, "Reverse wheel did not return to its starting time.");
                }
                ui.View.UpdateTransport(3000, 10000, true, false, false, null, null); ui.Paint();
                var timeline = ui.View.ObjectTimelineBounds;
                ui.View.Wheel(timeline.X + 10, timeline.Y + 10, 120, false); ui.Paint();
                Check(ui.View.PlayheadMs < 3000, "Canvas preference reversed the object timeline.");
                double zoom = ui.View.CanvasZoom;
                ui.View.Wheel(ui.Plot.X + 10, ui.Plot.Y + 20, 120, false, false, true); ui.Paint();
                Check(ui.View.CanvasZoom > zoom, "Reverse scrolling changed zoom direction.");
                ui.View.OpenSettings(); ui.Paint();
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayMode")), "Unsupported host exposed display setting.");
                ui.Key(27);
                ui.View.SupportsDisplayMode = true;
                ui.View.OpenSettings(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayDelayHint") && t.Color == 0xF2C66D), "Audio-delay hint is not highlighted.");
                ui.ClickText(L.Get("settings.displayVsync") + " ▾");
                Check(ui.Canvas.Texts.Any(t => t.Value == "✓ " + L.Get("settings.displayVsync")) &&
                    ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayImmediate")), "Dropdown omitted options or the current-choice check.");
                ui.Key(27);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayVsync") + " ▾"), "Escape closed settings instead of the dropdown.");
                ui.ClickText(L.Get("settings.displayVsync") + " ▾");
                ui.ClickText(L.Get("settings.displayImmediate"));
                Check(!ui.View.LibrarySettings.LowLatencyDisplay, "Draft changed active presentation before Apply.");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayVsync") + " ▾"), "Closing retained an unapplied draft.");
                ui.ClickText(L.Get("settings.displayVsync") + " ▾");
                ui.ClickText(L.Get("settings.displayImmediate"));
                ui.ClickText(L.Get("settings.appearance"));
                ui.ClickText(L.Get("settings.general"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.displayImmediate") + " ▾"), "Category switch lost the draft.");
                ui.View.ApplySettings(path); ui.Paint();
                Check(ui.View.LibrarySettings.LowLatencyDisplay && LibrarySettings.Load(path).LowLatencyDisplay, "Apply failed to activate and persist low latency.");
                ui.ClickText(L.Get("settings.displayImmediate") + " ▾");
                Check(ui.Canvas.Texts.Any(t => t.Value == "✓ " + L.Get("settings.displayImmediate")), "Dropdown check did not follow the chosen mode.");
                ui.ClickText(L.Get("settings.displayVsync"));
                ui.View.ApplySettings(path);
                Check(!ui.View.LibrarySettings.LowLatencyDisplay && !LibrarySettings.Load(path).LowLatencyDisplay, "VSync could not be restored.");
                Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Display preference edited the beatmap.");
            }
        }
        finally { L.SetLanguage(originalLanguage); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
