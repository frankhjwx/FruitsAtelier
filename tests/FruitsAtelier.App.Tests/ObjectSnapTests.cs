using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class ObjectSnapTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}.");

    public static void Run()
    {
        BeatRanges(); Placement(); Configuration();
    }

    private static void BeatRanges()
    {
        var map = new MapDocument { BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Meter = 4, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 2000, BeatLengthMs = 250, Meter = 3, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 2400, BeatLengthMs = -50, Uninherited = false });
        var timing = new TimingMap.Lookup(map);
        Near(1000, timing.OffsetBeats(0, 2));
        Near(1500, timing.OffsetBeats(2250, -2));
        Near(1500, timing.OffsetBeats(2000, -1));
        Near(2250, timing.OffsetBeats(2000, 1));
        Near(3500, timing.OffsetBeats(3000, 2));
        Near(3000, timing.OffsetBeats(3000, 0));
    }

    private static void Placement()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100.25 }, new() { TimeMs = 875, X = 200 },
            new() { TimeMs = 2500, X = 300 }, new() { TimeMs = 2625, X = 400 }]);
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('F');
        Check(ui.View.ObjectSnapSettings == (true, 2, 1), "Object snap defaults are incorrect.");
        ui.MoveMap(2000, 103);
        Near(100.25, ui.View.ObjectSnapTargetPoint!.Value.X);
        ui.ClickMap(2000, 103);
        var fruit = ui.View.Document.Fruits.Single(f => Math.Abs(f.TimeMs - 2000) < .01);
        Near(100.25, fruit.X); Near(2000, fruit.TimeMs);
        ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Object-snapped placement did not undo.");
        ui.MoveMap(2000, 302); Near(300, ui.View.ObjectSnapTargetPoint!.Value.X);
        ui.MoveMap(2000, 202); Check(ui.View.ObjectSnapTargetPoint is null, "Earlier search exceeded two beats.");
        ui.MoveMap(2000, 402); Check(ui.View.ObjectSnapTargetPoint is null, "Later search exceeded one beat.");
        ui.MoveMap(2000, 330); Check(ui.View.ObjectSnapTargetPoint is null, "Distant X triggered object snap.");
        ui.Key('T'); ui.Key('Y'); ui.MoveMap(2000, 103);
        Near(100.25, ui.View.ObjectSnapTargetPoint!.Value.X);
        ui.ClickMap(2000, 103); Near(100.25, ui.View.Document.Fruits.Single(f => f.TimeMs == 2000).X);
        ui.Key('Z', ctrl: true);
        foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
        {
            ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Key('B');
            ui.ClickMap(2000, 302);
            var head = ui.View.Document.Tracks.Single().Nodes[0];
            Near(300, head.X); Near(2000, head.TimeMs);
            ui.View.CancelInteraction(); ui.Paint();
            Check(map.ContentEquals(ui.View.Document), "Snapped slider draft did not cancel.");
        }
        var sliderMap = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
        sliderMap.Tracks.Add(new CurveTrack { Kind = CurveKind.Linear,
            Nodes = { new() { TimeMs = 1000, X = 90 }, new() { TimeMs = 1500, X = 190 } } });
        ui.LoadDocument(sliderMap); ui.Key('F'); ui.MoveMap(2000, 192);
        Near(190, ui.View.ObjectSnapTargetPoint!.Value.X);
        Check(sliderMap.ContentEquals(ui.View.Document), "Object snap hover edited map content.");
    }

    private static void Configuration()
    {
        var legacy = JsonSerializer.Deserialize<MapEditingPreferences>("{\"DistanceSnap\":true}")!;
        Check(legacy.ObjectSnap && legacy.ObjectSnapEarlierBeats == 2 && legacy.ObjectSnapLaterBeats == 1,
            "Older settings lost default object snap.");
        var limits = JsonSerializer.Deserialize<MapEditingPreferences>("{\"ObjectSnapEarlierBeats\":16,\"ObjectSnapLaterBeats\":-1}")!;
        Check(limits.ObjectSnapEarlierBeats == 4 && limits.ObjectSnapLaterBeats == 0,
            "Stored object snap ranges exceeded zero to four beats.");
        string language = Strings.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            {
                Strings.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000 };
                var ui = new Ui(); ui.LoadDocument(map); ui.Resize(size.Item1, size.Item2);
                int saved = 0; ui.View.RequestViewPreference = () => saved++;
                var before = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                Open(); Click(ui.View.ObjectSnapEarlierPlusBounds); Click(ui.View.ObjectSnapLaterPlusBounds);
                Check(ui.View.ObjectSnapSettings == (true, 2, 1), "Search draft changed active preferences.");
                ui.ClickText(Strings.Get("mac.cancel"));
                Check(ui.View.ObjectSnapSettings == (true, 2, 1) && saved == 0, "Cancel saved object snap settings.");
                Open(); Click(ui.View.ObjectSnapEarlierPlusBounds); Click(ui.View.ObjectSnapLaterPlusBounds);
                ui.ClickText(Strings.Get("library.apply"));
                Check(ui.View.ObjectSnapSettings == (true, 3, 2) && saved == 1, "Apply did not save both search ranges.");
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty,
                    "Object snap preferences changed beatmap content or history.");
                var settings = JsonSerializer.Deserialize<LibrarySettings>(JsonSerializer.Serialize(ui.View.LibrarySettings))!;
                var restored = settings.MapEditingPreferences.Values.Single();
                Check(restored.ObjectSnap && restored.ObjectSnapEarlierBeats == 3 && restored.ObjectSnapLaterBeats == 2,
                    "Settings round trip lost search ranges.");
                Open();
                for (int i = 0; i < 5; i++) { Click(ui.View.ObjectSnapEarlierPlusBounds); Click(ui.View.ObjectSnapLaterPlusBounds); }
                ui.ClickText(Strings.Get("library.apply"));
                Check(ui.View.ObjectSnapSettings == (true, 4, 4), "Search controls exceeded four beats.");
                Open(); Click(ui.View.ObjectSnapToggleBounds); ui.ClickText(Strings.Get("library.apply"));
                Check(!ui.View.ObjectSnapSettings.Enabled, "Object snap toggle did not apply.");

                void Open()
                {
                    ui.View.OpenDistanceSnapDialog(); ui.Paint();
                    Check(ui.View.ObjectSnapLaterPlusBounds.Bottom < size.Item2 && ui.View.DistanceSnapPreviewBounds.Height > 0,
                        "Object snap controls do not fit the dialog.");
                }
                void Click(FruitsAtelier.App.Rendering.Rect r) => ui.Click(r.X + r.Width / 2, r.Y + r.Height / 2);
            }
        }
        finally { Strings.SetLanguage(language); }
    }
}
