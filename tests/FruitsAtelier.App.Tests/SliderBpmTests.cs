using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderBpmTests
{
    public static void Performance()
    {
        foreach (int count in new[] { 1, 1000 })
        foreach (bool aspire in new[] { false, true })
        {
            var map = new MapDocument { EnableSliderBpmMultiplier = aspire, DurationMs = count * 1000 + 3000 };
            for (int i = 0; i < count; i++)
            {
                var track = new CurveTrack { BpmMultiplier = 4, CompensateTinyDroplets = false };
                track.Nodes.AddRange([new() { TimeMs = 1000 + i * 1000, X = 100 }, new() { TimeMs = 1500 + i * 1000, X = 200 }]);
                map.Tracks.Add(track);
            }
            var ui = new Ui(false);
            var timer = System.Diagnostics.Stopwatch.StartNew(); ui.LoadDocument(map);
            double cold = timer.Elapsed.TotalMilliseconds;
            for (int i = 0; i < 4; i++) ui.Paint();
            long bytes = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
            for (int i = 0; i < 64; i++) { ui.View.PointerMove(300 + i % 2, 400, false, false); ui.Paint(); }
            Console.WriteLine($"BPM aspire={aspire} sliders={count}: cold load={cold:F2}ms; warm pointer+recording paint={timer.Elapsed.TotalMilliseconds / 64:F3}ms/{(GC.GetAllocatedBytesForCurrentThread() - bytes) / 64}B per frame (native presentation excluded).");
        }
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { EnableSliderBpmMultiplier = true, DurationMs = 10000 };
                var track = new CurveTrack { CompensateTinyDroplets = false };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 200 }]);
                map.Tracks.Add(track);
                map.EnableSliderBpmMultiplier = false;
                var ui = new Ui(false); ui.LoadDocument(map);
                ui.View.ShowSongDock();
                typeof(FruitsAtelier.App.Editor.EditorView).GetField("songTab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(ui.View, 4);
                ui.Paint();
                var category = ui.Canvas.Texts.Single(t => t.Value == L.Get("aspire.category"));
                ui.Click(ui.View.SongDockViewport.Right - 20, category.Y + 47);
                Check(ui.View.Document.EnableSliderBpmMultiplier, "FA Settings did not enable Aspire BPM");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("aspire.category")), "Enabling Aspire left FA Settings");
                category = ui.Canvas.Texts.Single(t => t.Value == L.Get("aspire.category"));
                ui.Click(ui.View.SongDockViewport.Right - 20, category.Y + 47);
                Check(!ui.View.Document.EnableSliderBpmMultiplier
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("aspire.category")), "Disabling Aspire left FA Settings");
                category = ui.Canvas.Texts.Single(t => t.Value == L.Get("aspire.category"));
                ui.Click(ui.View.SongDockViewport.Right - 20, category.Y + 47);
                var box = ui.View.BpmMultiplierBounds;
                ui.Click(box.X + 8, box.Y + 8);
                foreach (double preset in SliderBpm.Presets)
                    Check(ui.Canvas.Texts.Any(t => t.Value == preset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "×"), "Missing preset " + preset + ": " + string.Join(" | ", ui.Canvas.Texts.Select(t => t.Value)));
                Check(!ui.Canvas.Texts.Any(t => t.Value == "16×" || t.Value == "32×"), "Extra preset");
                ui.ClickText("4×");
                Check(ui.View.Document.DefaultSliderBpmMultiplier == 4 && ui.View.Document.Tracks[0].BpmMultiplier == 1, "Default changed an existing slider");
                ui.Key('B'); ui.ClickMap(3000, 150); ui.ClickMap(4000, 250); ui.Key(13);
                Check(ui.View.Document.Tracks.Last().BpmMultiplier == 4, "New slider lost default BPM");
                ui.SelectTrack(track.Id); ui.View.ShowSliderInspector(); ui.Paint();
                var label = FindControl(); ui.Click(label.X + 4, label.Y + 5); ui.ClickText("2×");
                Check(ui.View.Document.Tracks.First(t => t.Id == track.Id).BpmMultiplier == 2, "Inspector preset did not apply");
                label = FindControl(); ui.Click(label.X + 4, label.Y + 5); ui.ClickText(L.Get("aspire.custom"));
                var viewport = ui.View.SliderInspectorViewport;
                ui.View.Wheel(viewport.X + 10, viewport.Y + 10, -120, false, false); ui.Paint();
                ui.FocusField(L.Get("aspire.custom")); ui.Key(65, ctrl: true); ui.Type("3.5"); ui.Key(13);
                Check(ui.View.Document.Tracks.First(t => t.Id == track.Id).BpmMultiplier == 3.5, "Custom input did not apply: " + ui.View.Document.Tracks.First(t => t.Id == track.Id).BpmMultiplier + " status " + ui.View.StatusMessage);
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.Tracks.First(t => t.Id == track.Id).BpmMultiplier == 2, "Custom undo failed");
                ui.Key('Y', ctrl: true);
                Check(ui.View.Document.Tracks.First(t => t.Id == track.Id).BpmMultiplier == 3.5, "Custom redo failed");
                ui.SelectTrack(track.Id); ui.View.ShowSliderInspector(); ui.Paint(); FindControl();
                ui.FocusField(L.Get("aspire.custom")); ui.Key(65, ctrl: true); ui.Type("0"); ui.Key(13);
                Check(ui.View.Document.Tracks.First(t => t.Id == track.Id).BpmMultiplier == 3.5 && !ui.View.ErrorVisible, "Invalid custom input changed content or crashed");
                ui.Key(27);
                Check(!ui.View.ShowTimingSv && ui.View.DisplayedTiming.Any(p => p.Uninherited && p.TimeMs == 1000), "Aspire red hidden by SV toggle");
                var before = ui.View.Document.DeepClone();
                ui.Resize(980, 620); ui.Paint(); ui.Paint();
                Check(ui.View.Document.ContentEquals(before), "Repaint modified content");

                RecordingCanvas.Label FindControl()
                {
                    for (int i = 0; i < 30; i++)
                    {
                        var found = ui.Canvas.Texts.Where(t => t.Value.StartsWith(L.Get("aspire.multiplier"))
                            && t.Y > 140 && t.Y < ui.View.SliderInspectorViewport.Bottom).ToArray();
                        if (found.Length > 0) return found.Last();
                        var panel = ui.View.SliderInspectorViewport;
                        ui.View.Wheel(panel.X + 10, panel.Y + 10, -120, false, false); ui.Paint();
                    }
                    throw new Exception("BPM inspector control not reachable");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
