using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderSpeedReadoutTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 8000, SliderMultiplier = 1, BeatLengthMs = 500 };
                var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false, SpanCount = 2 };
                track.Nodes.Add(new() { TimeMs = 1000, X = 100 });
                track.Nodes.Add(new() { TimeMs = 1500, X = 300 });
                map.Tracks.Add(track);
                map.Fruits.Add(new() { TimeMs = 1250, X = 500 });
                var ui = new Ui(); ui.LoadDocument(map); ui.SelectTrack(track.Id);
                var before = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                Check(ui.View.SliderSpeedReadout is not null, "Selected slider has no speed readout.");
                Near(2, ui.View.SliderSpeedReadout!.Value.MaxDs);
                Near(CatchStreamConverter.Convert(ui.View.Document).Sliders[0].SliderVelocityMultiplier, ui.View.SliderSpeedReadout.Value.Sv);
                for (int frame = 0; frame < 5; frame++) ui.Paint();
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Speed display edited the document.");
                ui.View.LibrarySettings.CanvasZoom = .4; ui.Paint();
                Near(2, ui.View.SliderSpeedReadout!.Value.MaxDs);
                ui.EditTrack(track.Id); ui.DownMap(1500, 300); ui.MoveMap(1500, 450); ui.UpMap(1500, 450);
                Near(450, ui.View.Document.Tracks[0].Nodes[^1].X);
                Check(ui.View.SliderSpeedReadout!.Value.MaxDs > 3, "Speed did not refresh during unrestricted editing.");
                ui.Key('Z', ctrl: true); ui.SelectTrack(track.Id);
                Near(2, ui.View.SliderSpeedReadout!.Value.MaxDs);
                ui.Key('Y', ctrl: true); ui.SelectTrack(track.Id);
                Check(ui.View.SliderSpeedReadout!.Value.MaxDs > 3, "Redo left a stale speed.");
                ui.View.Document.Tracks[0].CompensateTinyDroplets = true; ui.Paint();
                ui.ClickText(L.Get("ui.view")); ui.ClickText(L.Get("movement.includeTiny"));
                var converted = CatchStreamConverter.Convert(ui.View.Document);
                var events = converted.Objects.Where(o => o.SourceId == track.Id).ToArray();
                double maximum = events.Zip(events.Skip(1)).Where(p => p.Second.TimeMs > p.First.TimeMs)
                    .Max(p => Math.Abs(p.Second.X - p.First.X) / ((p.Second.TimeMs - p.First.TimeMs) * .2));
                Near(maximum, ui.View.SliderSpeedReadout!.Value.MaxDs);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sliderSpeed.maxDsTiny", L.Get("assist.ratio", maximum))), "Tiny-inclusive readout has no visible label.");
                ui.LoadDocument(new MapDocument()); Check(ui.View.SliderSpeedReadout is null, "Replacing the document retained a speed badge.");
                ui.LoadDocument(map); ui.ClickFruit(ui.View.Document.Fruits[0].Id);
                Check(ui.View.SliderSpeedReadout is null, "Standalone fruit has a slider speed badge.");
                ui.View.Document.Tracks[0].StreamSnapDivisor = 2; ui.SelectTrack(track.Id);
                Check(ui.View.SliderSpeedReadout is null, "Stream is presented as an exported slider.");

                var imported = OsuBeatmapWriter.Serialize(map).ReadBack;
                ui.LoadDocument(imported); ui.ClickMap(1000, 100);
                Check(ui.View.SliderSpeedReadout is { Sv: > 1, MaxDs: > 1 }, "Legacy slider has no speed readout.");
                var importedBefore = ui.View.Document.DeepClone();
                ui.Paint(); Check(importedBefore.ContentEquals(ui.View.Document), "Inspecting speed converted a Legacy slider.");

                foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
                {
                    ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 8000, SliderMultiplier = 1 });
                    ui.View.SetSliderEditingMode(mode); ui.Key('B'); ui.ClickMap(1000, 100);
                    Check(ui.View.SliderSpeedReadout is { Sv: null, MaxDs: null }, "Incomplete draft shows a stale speed.");
                    ui.ClickMap(1500, 300, ctrl: true);
                    Check(ui.View.SliderSpeedReadout is { Sv: > 1, MaxDs: > 1 }, "Draft speed is missing.");
                    ui.Key(27); Check(ui.View.SliderSpeedReadout is null, "Cancelled draft retained its badge.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Near(double expected, double? actual)
        => Check(actual is { } value && Math.Abs(expected - value) < .002, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
