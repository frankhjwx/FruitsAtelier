using System.Text.Json;
using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class DistanceSnapPresetTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Snapping()
    {
        var reference = new DistanceSnap.Reference(Guid.NewGuid(), new(1000, 256), new(1000, 256), .28, 0);
        DistanceSnap.Preset[] presets = [new("Walk", 1), new("Dash", 2), new("HDash", 4), new("Wide", 7.25)];
        foreach (var (mouse, expected) in new[] { (260d, 256d), (290d, 291d), (330d, 326d), (399d, 396d), (220d, 221d), (115d, 116d), (2d, 2.25d) })
        {
            var point = DistanceSnap.SnapMultiple(new(1125, mouse), reference, presets, 1, out bool outside);
            Check(Math.Abs(point.X - expected) < .001 && !outside, "Nearest DS or implicit zero was not selected.");
        }
        var edge = reference with { Start = new(1000, 500), End = new(1000, 500) };
        Check(DistanceSnap.SnapMultiple(new(1125, 480), edge, presets, 1, out _).X == 465, "Invalid right candidates hid a valid left candidate.");
        Check(DistanceSnap.SnapMultiple(new(5000, 200), reference, presets, 1, out bool invalid).X == 256 && !invalid,
            "Zero DS did not remain available when every nonzero distance exceeded the field.");
        Check(DistanceSnap.SnapMultiple(new(1125, 260), reference, [], 1, out _).X == 256, "Empty list omitted zero DS.");
        Check(DistanceSnap.SnapMultiple(new(1125, 290), reference, [], 1, out _).X == 291, "Empty list omitted legacy spacing.");
        Check(DistanceSnap.SnapMultiple(new(1000, 290), reference, presets, 1, out _).X == 290, "Simultaneous notes were snapped.");
        var settings = new LibrarySettings { DistanceSnapPresets = presets.ToList() };
        var restored = JsonSerializer.Deserialize<LibrarySettings>(JsonSerializer.Serialize(settings))!;
        Check(restored.DistanceSnapPresets.SequenceEqual(presets), "Preset names/ratios did not survive settings persistence.");
        Check(JsonSerializer.Deserialize<LibrarySettings>("{}")!.DistanceSnapPresets.Count == 0, "Old settings did not default to an empty list.");
        settings.DistanceSnapPresets = Enumerable.Range(0, 12).Select(i => new DistanceSnap.Preset("DS", 1)).ToList();
        Check(settings.DistanceSnapPresets.Count == 8, "Settings did not enforce eight slots.");
    }

    public static void Dialog()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language);
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 10000, IsDemo = false };
            map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
            ui.LoadDocument(map);
            var baseline = ui.View.Document.DeepClone();
            int saves = 0;
            ui.View.RequestDistanceSnapPreference = () => saves++;
            var button = ui.View.AssistButtonBounds[5];
            ui.View.PointerMove(button.X + 10, button.Y + 10, false, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.configure")), "Hover did not reveal DS configuration.");
            ui.ClickText(Strings.Get("ds.configure"));
            Check(ui.View.DistanceSnapDialogVisible && ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.empty")), "Dialog did not open empty.");
            double start = ui.View.PlayheadMs, duration = TimingMap.At(map, start).BeatLengthMs / ui.View.SnapDivisor;
            ConvertedCatchObject Note(double time) => new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, time, 256, 256, 256, 0);
            var range = HyperDashCalculator.Calculate([Note(start), Note(start + duration)], map.CircleSize)[0].Movement!.Value;
            double unit = duration * DistanceSnap.BaseVelocity(map, start);
            foreach (double boundary in new[] { range.StandLimit, range.WalkLimit, range.DashLimit })
                Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("assist.ratio", boundary / unit)), "Reference transition differs from a fresh catcher departure.");
            ui.Key('F'); ui.Key('Y'); ui.Key(116); ui.ClickMap(1250, 300);
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.DistanceSnapEnabled && !ui.View.IsTestplaying, "Modal input escaped to editor.");
            ui.ClickText(Strings.Get("ds.add"));
            ui.ClickText(Strings.Get("ds.defaultName", 1)); ui.Type("Walk"); ui.Key(13);
            float rowY = ui.Canvas.Texts.Last(t => t.Value == "Walk").Y;
            var value = ui.Canvas.Texts.Single(t => t.Value == Strings.Get("ds.ratio", 1) && t.Y == rowY);
            ui.Click(value.X + 3, value.Y + 3); ui.Type("0"); ui.Key(13);
            Check(ui.View.IsEditingText && ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.invalid")), "Invalid ratio was accepted.");
            ui.Key('A', ctrl: true); ui.Type("1.25"); ui.Key(13);
            for (int i = 1; i < 8; i++) ui.ClickText(Strings.Get("ds.add"));
            ui.ClickText(Strings.Get("ds.add"));
            ui.ClickText(Strings.Get("library.apply"));
            Check(!ui.View.DistanceSnapDialogVisible && saves == 1 && ui.View.LibrarySettings.DistanceSnapPresets.Count == 8, "Apply or preset cap failed.");
            Check(ui.View.LibrarySettings.DistanceSnapPresets[0] == new DistanceSnap.Preset("Walk", 1.25), "Field edits were lost.");
            Check(baseline.ContentEquals(ui.View.Document), "Configuration changed beatmap content.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            var remove = ui.Canvas.Texts.First(t => t.Value == Strings.Get("ds.remove")); ui.Click(remove.X + 2, remove.Y + 2);
            ui.ClickText(Strings.Get("mac.cancel"));
            Check(ui.View.LibrarySettings.DistanceSnapPresets.Count == 8 && saves == 1, "Cancel persisted a draft deletion.");
            ui.Resize(960, 640); ui.View.OpenDistanceSnapDialog(); ui.Paint();
            double time = ui.View.PlayheadMs;
            ui.View.Wheel(480, 400, -1200, false); ui.Paint();
            Check(ui.View.PlayheadMs == time, "Modal scrolling moved the playhead.");
            ui.Key(27); Check(!ui.View.DistanceSnapDialogVisible, "Esc did not close configuration.");
        }
        Strings.SetLanguage("en");
    }
}
