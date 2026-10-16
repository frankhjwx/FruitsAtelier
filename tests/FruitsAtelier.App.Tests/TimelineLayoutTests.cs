using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimelineLayoutTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/timeline-layout", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        var defaults = LibrarySettings.Load(path);
        Check(defaults.ObjectTimelineHeight == 64 && defaults.OverviewTimelineHeight == 40, "Existing preferences lose default timeline heights.");
        try
        {
            foreach (string locale in L.AvailableLanguages)
            foreach (var size in new[] { (760, 580), (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = root;
                var before = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                int saves = 0;
                ui.View.RequestViewPreference = () => { ui.View.LibrarySettings.Save(path); saves++; };
                float playhead = (float)ui.View.PlayheadMs;
                CheckLayout();
                var oldPlot = ui.Plot;
                Resize(true, -24, cancel: false);
                Check(ui.View.ObjectTimelineBounds.Height == 40 && ui.Plot.Y == oldPlot.Y - 24, "Upper timeline does not move the canvas ruler and plot.");
                Resize(false, 20, cancel: false);
                Check(Overview().Height == 20 && saves == 2, "Overview height was not saved on release.");
                var restored = LibrarySettings.Load(path);
                Check(restored.ObjectTimelineHeight == 40 && restored.OverviewTimelineHeight == 20, "Timeline heights did not survive persistence.");
                foreach (bool upper in new[] { true, false })
                {
                    float original = upper ? ui.View.LibrarySettings.ObjectTimelineHeight : ui.View.LibrarySettings.OverviewTimelineHeight;
                    Resize(upper, upper ? 20 : -20, cancel: true);
                    Check((upper ? ui.View.LibrarySettings.ObjectTimelineHeight : ui.View.LibrarySettings.OverviewTimelineHeight) == original && saves == 2,
                        "Escape failed to restore the height or saved a cancelled drag.");
                    var handle = upper ? ui.View.ObjectTimelineResizeBounds : ui.View.OverviewTimelineResizeBounds;
                    ui.View.PointerDown(handle.X + 10, handle.Y + 4, 0, false, false);
                    ui.View.PointerMove(handle.X + 10, handle.Y + 24, false, false); ui.Paint();
                    ui.View.CancelInteraction(); ui.Paint();
                    Check(!ui.View.WantsCapture && saves == 2, "Capture loss retained a resize gesture or saved a draft.");
                }
                Resize(true, 10000, cancel: false); Resize(false, -10000, cancel: false); CheckLayout();
                Check(ui.View.ObjectTimelineBounds.Height == 64 && ui.Plot.Height >= 120 && Overview().Height == 40, "Timeline growth consumed the editing area.");
                Resize(true, -10000, cancel: false); Resize(false, 10000, cancel: false);
                Check(ui.View.ObjectTimelineBounds.Height == 32 && Overview().Height == 20, "Timeline shrink limits failed.");
                Check(ui.View.TransportButtonBounds(0).Height == 32 && ui.View.TimeDisplayBounds.Height == 27,
                    "Timeline shrinking changed the time or playback control size.");
                Check(ui.View.TransportButtonBounds(0).Y >= ui.View.OverviewTimelineResizeBounds.Y + 4,
                    "Minimum transport row does not reserve the playback controls' height.");
                ui.View.OpenSettings(); ui.View.ApplySettings(path, force: true); ui.Key(27);
                Check(LibrarySettings.Load(path).ObjectTimelineHeight == 32 && LibrarySettings.Load(path).OverviewTimelineHeight == 20,
                    "Applying other settings reset timeline heights.");
                var selector = ui.View.PlaybackSpeedSelectorBounds;
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.playbackSpeed"));
                Check(label.X >= 0 && label.X + label.MaxWidth < selector.X, "Playback speed label overlaps the speed buttons or escapes the window.");
                ui.Click(label.X + 4, label.Y + 4);
                Check(!ui.View.LibraryVisible && ui.View.PlaybackSpeed == 1, "Static playback speed label changed speed or screen.");
                foreach (double rate in new[] { .1, .25, .5, .75, 1, 1.5 })
                {
                    var text = ui.Canvas.Texts.Single(t => t.Value == L.Get("ui.zoomPercent", rate * 100)
                        && t.Y >= selector.Y && t.X >= selector.X && t.X < selector.Right);
                    ui.Click(text.X + 2, text.Y + 2);
                    Check(ui.View.PlaybackSpeed == rate, "Expanded playback speed button did not select its rate.");
                }
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty && ui.View.PlayheadMs == playhead,
                    "Layout changes modified beatmap content or playhead.");

                Rect Overview() => ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
                void CheckLayout()
                {
                    var time = ui.View.TimeDisplayBounds; var play = ui.View.TransportButtonBounds(0); var test = ui.View.TransportButtonBounds(3);
                    var overview = Overview(); var speed = ui.View.PlaybackSpeedSelectorBounds; var volume = ui.View.VolumeButtonBounds;
                    Check(time.Right < play.X && test.Right < overview.X && overview.Right <= ui.Width, "Transport controls overlap or escape the window.");
                    Check(speed.Y == volume.Y && speed.Right < volume.X, "Speed selector is not immediately left of Volume.");
                    Check(ui.Plot.Bottom <= ui.View.OverviewTimelineResizeBounds.Y, "Plot overlaps the overview resize boundary.");
                }
                void Resize(bool upper, float delta, bool cancel)
                {
                    var handle = upper ? ui.View.ObjectTimelineResizeBounds : ui.View.OverviewTimelineResizeBounds;
                    float x = handle.X + 10, y = handle.Y + handle.Height / 2;
                    ui.View.PointerMove(x, y, false, false); ui.Paint();
                    Check(ui.View.TimelinePanelResizeCursor, "Resize handle has no vertical cursor.");
                    ui.View.PointerDown(x, y, 0, false, false);
                    Check(ui.View.WantsCapture, "Resize did not capture the pointer.");
                    ui.View.PointerMove(x, y + delta, false, false); ui.Paint();
                    if (cancel) ui.Key(27); else { ui.View.PointerUp(x, y + delta, 0); ui.Paint(); }
                    Check(!ui.View.WantsCapture, "Resize release retained capture.");
                }
            }
            var minimumUi = new Ui(false);
            var minimumMap = new MapDocument { DurationMs = 10000, IsDemo = false };
            var fruit = new Fruit { TimeMs = 0, X = 256 };
            minimumMap.Fruits.Add(fruit); minimumUi.LoadDocument(minimumMap);
            minimumUi.View.LibrarySettings.ObjectTimelineHeight = 32;
            minimumUi.View.LibrarySettings.OverviewTimelineHeight = 20;
            minimumUi.Paint();
            var timeline = minimumUi.View.ObjectTimelineBounds;
            minimumUi.Click(timeline.X + timeline.Width / 2, timeline.Y + 27 * .5f);
            Check(minimumUi.View.SelectedObjectIds.Contains(fruit.Id), "Minimum-height upper timeline cannot select a visible fruit.");
            var overviewBounds = minimumUi.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
            minimumUi.Click(overviewBounds.X + overviewBounds.Width / 4, overviewBounds.Y + overviewBounds.Height / 2);
            Check(minimumUi.View.PlayheadMs == 2500 && minimumMap.ContentEquals(minimumUi.View.Document),
                "Minimum-height overview cannot seek without editing content.");
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
