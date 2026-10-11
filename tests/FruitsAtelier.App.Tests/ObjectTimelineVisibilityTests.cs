using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ObjectTimelineVisibilityTests
{
    public static void Preferences()
    {
        string language = L.Language;
        string root = Path.GetFullPath("artifacts/tests/timeline-visibility");
        Directory.CreateDirectory(root);
        try
        {
            string legacy = Path.Combine(root, "legacy.json");
            File.WriteAllText(legacy, "{}");
            var defaults = LibrarySettings.Load(legacy);
            Check(!defaults.HideObjectTimeline && !defaults.RevealObjectTimelineOnHover,
                "Older settings must keep the timeline visible.");
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                ui.Resize(size.Item1, size.Item2);
                ui.View.SupportsDisplayMode = ui.View.SupportsFullscreen = true;
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
                var original = ui.View.Document.DeepClone();
                var plot = ui.Plot;
                Open();
                ui.ClickText(L.Get("settings.upperTimelineHoverOff"));
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.upperTimelineHoverOn")),
                    "Hover reveal must be disabled while the timeline is visible.");
                ui.ClickText(L.Get("settings.upperTimelineVisible"));
                ui.ClickText(L.Get("settings.upperTimelineHoverOff"));
                ui.Key(27);
                Check(!ui.View.LibrarySettings.HideObjectTimeline && ui.View.ObjectTimelineBounds.Height == 64,
                    "Closing without Apply must discard the visibility draft.");
                Open();
                ui.ClickText(L.Get("settings.upperTimelineVisible"));
                ui.ClickText(L.Get("settings.upperTimelineHoverOff"));
                string config = Path.Combine(root, $"{locale}-{size.Item1}.json");
                ui.View.ApplySettings(config); ui.Key(27);
                var loaded = LibrarySettings.Load(config);
                Check(loaded.HideObjectTimeline && loaded.RevealObjectTimelineOnHover,
                    "Apply must persist both timeline preferences.");
                Check(ui.View.ObjectTimelineBounds.Height == 0 && ui.Plot.Y == plot.Y - 72
                    && ui.Plot.Height == plot.Height + 72 && ui.Plot.Bottom == plot.Bottom,
                    "Hidden timeline must return its space to the canvas.");
                Check(original.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                    "Timeline preferences must preserve beatmap content and history.");
                Open(); ui.ClickText(L.Get("settings.upperTimelineHidden"));
                ui.View.ApplySettings(config); ui.Key(27);
                Check(ui.View.ObjectTimelineBounds.Height == 64 && ui.Plot == plot,
                    "Showing the timeline must restore the original layout.");

                void Open()
                {
                    ui.View.OpenSettings(); ui.Paint();
                    ui.View.Wheel(ui.View.SettingsBounds.Right - 80, ui.View.SettingsBounds.Y + 200, -1200, false);
                    ui.Paint();
                    var label = ui.Canvas.Texts.Single(t => t.Value == L.Get(ui.View.LibrarySettings.HideObjectTimeline
                        ? "settings.upperTimelineHidden" : "settings.upperTimelineVisible"));
                    Check(label.Y >= ui.View.SettingsBounds.Y + 116 && label.Y < ui.View.SettingsBounds.Bottom - 86,
                        "Timeline preference must be reachable in General settings.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    public static void HoverAndDrag()
    {
        var ui = new Ui(false);
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 128 });
        ui.LoadDocument(map);
        ui.View.UpdateTransport(1000, 10000, false, false, false, null, null); ui.Paint();
        var timeline = ui.View.ObjectTimelineBounds;
        var original = ui.View.Document.DeepClone();
        ui.View.LibrarySettings.HideObjectTimeline = true;
        ui.Paint();
        var hiddenPlot = ui.Plot;
        Move(timeline.X + 100, timeline.Y);
        Check(ui.View.ObjectTimelineBounds.Height == 0, "Hidden timeline must stay hidden without hover reveal.");
        ui.View.LibrarySettings.RevealObjectTimelineOnHover = true;
        var conversionField = typeof(FruitsAtelier.App.Editor.EditorView).GetField("conversion",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object? conversion = conversionField.GetValue(ui.View);
        for (int i = 0; i < 10; i++)
        {
            Move(timeline.X + 100, timeline.Y);
            Check(ui.View.ObjectTimelineBounds == timeline && ui.Plot.Y == hiddenPlot.Y + 72,
                "Approaching the toolbar must expand the timeline.");
            Move(timeline.X + 100, timeline.Bottom - 1);
            Check(ui.View.ObjectTimelineBounds == timeline, "Hovering in the timeline must keep it expanded.");
            Move(hiddenPlot.X + 100, hiddenPlot.Bottom - 20);
            Check(ui.View.ObjectTimelineBounds.Height == 0 && ui.Plot == hiddenPlot,
                "Leaving the timeline must collapse it.");
        }
        Check(ReferenceEquals(conversion, conversionField.GetValue(ui.View)) && !ui.View.IsDirty,
            "Hover reveal must reuse conversion and preserve history.");

        Move(timeline.X + timeline.Width / 2, timeline.Y);
        float x = timeline.X + (float)((1000 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        float y = timeline.Y + 27;
        ui.View.PointerDown(x, y, 0, false, false);
        Move(x + 18, hiddenPlot.Bottom - 20);
        Check(ui.View.ObjectTimelineBounds.Height == 64 && ui.View.SelectedObjectIds.Contains(map.Fruits[0].Id),
            "Timeline dragging must retain the expanded layout outside its strip.");
        ui.Key(27);
        Check(ui.View.ObjectTimelineBounds.Height == 0 && original.ContentEquals(ui.View.Document),
            "Cancelling a timeline drag must restore content and collapse outside the strip.");
        ui.View.KeyUp(27);
        ui.View.PointerDown(ui.Plot.X + ui.Plot.Width / 4, ui.Plot.Bottom - (float)(1000 * ui.View.PixelsPerMs), 0, false, false);
        Move(timeline.X + 100, timeline.Y);
        Check(ui.View.ObjectTimelineBounds.Height == 0 && ui.Plot == hiddenPlot,
            "Canvas dragging must not expand the timeline or shift its interaction coordinates.");
        ui.Key(27); ui.View.KeyUp(27);
        Move(hiddenPlot.X, hiddenPlot.Bottom - 20);
        ui.Key(114);
        Check(ui.View.TimingPageVisible && ui.View.ObjectTimelineBounds.Height == 0,
            "Timing waveform must remain independent of the upper timeline preference.");

        void Move(float mx, float my) { ui.View.PointerMove(mx, my, false, false); ui.Paint(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
