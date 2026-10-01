using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderMultiplierTests
{
    public static int Performance()
    {
        var samples = new List<object>();
        foreach (int count in new[] { 100, 1000 })
        {
            var map = new MapDocument { IsDemo = false, DurationMs = count * 2000 + 5000 };
            for (int i = 0; i < count; i++)
            {
                var slider = new ImportedSlider { TimeMs = i * 2000 + 500, X = 100, Y = 192,
                    PathType = 'B', PixelLength = 200, SpanCount = 2, SourceOrder = i };
                slider.ControlPoints.AddRange([new(100, 192), new(200, 100), new(300, 192)]);
                map.ImportedSliders.Add(slider);
                if (i % 25 == 0) map.TimingPoints.Add(new() { TimeMs = i * 2000, BeatLengthMs = i % 50 == 0 ? 500 : 400 });
            }
            var ui = new Ui(false); ui.LoadDocument(map);
            ui.Key(114); ui.ClickText(L.Get("timing.overrideSv"));
            for (int step = 0; step < 4; step++)
            {
                var field = ui.View.TimingFields.Single(f => f.Key == "page.sliderMultiplier");
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ui.View.PointerDown(field.Bounds.Right + 14, field.Bounds.Y + 8, 0, false, true);
                double dispatchMs = watch.Elapsed.TotalMilliseconds;
                ui.View.PointerUp(field.Bounds.Right + 14, field.Bounds.Y + 8, 0); ui.Paint();
                Wait(ui);
                samples.Add(new { sliders = count, step, dispatchMs, totalMs = watch.Elapsed.TotalMilliseconds,
                    allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes, sv = ui.View.Document.EffectiveSliderMultiplier });
                Check(ui.View.Document.EffectiveSliderMultiplier == Math.Round(1.92 + .01 * (step + 1), 2), "Performance fixture SV edit applied");
            }
        }
        System.IO.Directory.CreateDirectory("artifacts/sv");
        string report = System.Text.Json.JsonSerializer.Serialize(samples, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText("artifacts/sv/performance.json", report); Console.WriteLine(report);
        return 0;
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = Fixture();
                var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(980, 620);
                ui.ClickText(L.Get("timing.detailsPanel") + " ▾"); ui.ClickText(L.Get("timing.panel"));
                Check(!ui.View.Document.OverrideSliderMultiplier && ui.View.TimingFields.All(f => f.Key != "page.sliderMultiplier"), "SV starts locked");
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("timing.overrideSv"));
                ui.Click(label.X + 4, label.Y + 5);
                var field = ui.View.TimingFields.Single(f => f.Key == "page.sliderMultiplier");
                Check(field.Value == "2.00", "SV displays two decimals in its own row");
                ClickArrow(ui, field.Bounds.Right + 14, field.Bounds.Y + 8, false);
                Check(ui.View.Document.EffectiveSliderMultiplier == 2.1, "SV arrow steps by 0.1");
                ClickArrow(ui, field.Bounds.X - 14, field.Bounds.Y + 8, true);
                Check(ui.View.Document.EffectiveSliderMultiplier == 2.09, "Ctrl SV arrow steps by 0.01");
                var changed = ui.View.Document.DeepClone();
                Check(changed.DistancePerBeat == map.DistancePerBeat && ProjectSerializer.Read(ProjectSerializer.Serialize(changed)).ContentEquals(changed), "SV preserves DPB and persists");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.EffectiveSliderMultiplier == 2.1, "SV undo");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(changed), "SV redo");
                Set(ui, "3.61"); ui.Key(13);
                Check(ui.View.Document.ContentEquals(changed), "Invalid SV is atomic");
                ui.Key(27);
                ui.ClickText(L.Get("timing.overrideSv"));
                Check(!ui.View.Document.OverrideSliderMultiplier && ui.View.Document.EffectiveSliderMultiplier == 2.09, "Locking SV retains its value");
                Check(ui.View.TimingFields.All(f => f.Key != "page.sliderMultiplier"), "Locked SV cannot receive text input");
                ui.ClickText(L.Get("timing.overrideSv"));
                var pendingField = ui.View.TimingFields.Single(f => f.Key == "page.sliderMultiplier");
                ui.View.PointerDown(pendingField.Bounds.Right + 14, pendingField.Bounds.Y + 8, 0, false, true);
                Check(ui.View.SliderMultiplierValidationBusy && !ui.View.PrepareFileOperation(), "Pending SV validation keeps file operations on the confirmed value");
                ui.View.Document.Fruits.Add(new() { TimeMs = 6000, X = 200 });
                ui.View.PointerUp(pendingField.Bounds.Right + 14, pendingField.Bounds.Y + 8, 0); Wait(ui);
                Check(ui.View.Document.EffectiveSliderMultiplier == 2.09 && ui.View.Document.Fruits.Count == 1,
                    "Stale SV validation never overwrites subsequent content edits");
                Set(ui, "1.3"); ui.Key(13); Wait(ui);
                Check(ui.View.Document.EffectiveSliderMultiplier == 1.3, "Typed SV applies after background validation");
                var outputCache = new OsuWriteCache();
                var output = OsuBeatmapWriter.Serialize(ui.View.Document, cache: outputCache);
                Check(output.Text == OsuBeatmapWriter.Serialize(ui.View.Document).Text, "Cached SV output matches fresh serialization");
                ui.View.Document.Fruits[0].X = 210;
                Check(OsuBeatmapWriter.Serialize(ui.View.Document, cache: outputCache).Text
                    == OsuBeatmapWriter.Serialize(ui.View.Document).Text, "Content edits invalidate override output");
                pendingField = ui.View.TimingFields.Single(f => f.Key == "page.sliderMultiplier");
                ui.View.PointerDown(pendingField.Bounds.Right + 14, pendingField.Bounds.Y + 8, 0, false, true);
                var owner = (EditorHistory)typeof(FruitsAtelier.App.Editor.EditorView).GetProperty("history",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ui.View)!;
                owner.Begin("Concurrent edit");
                var validation = (System.Threading.Tasks.Task)typeof(FruitsAtelier.App.Editor.EditorView).GetField("sliderMultiplierValidation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ui.View)!;
                Check(validation.Wait(10000), "SV worker finishes during an active transaction");
                ui.Paint(); Check(ui.View.SliderMultiplierValidationBusy, "SV completion waits for active history transactions");
                owner.Cancel(); ui.View.PointerUp(pendingField.Bounds.Right + 14, pendingField.Bounds.Y + 8, 0); Wait(ui);
                Check(ui.View.Document.EffectiveSliderMultiplier == 1.31, "SV commits after the active transaction ends");
                foreach (double value in new[] { .4, 3.6, 1.3 })
                {
                    var test = Fixture(); var before = OsuBeatmapWriter.Serialize(test);
                    SliderMultiplierEditing.Apply(test, value);
                    var after = OsuBeatmapWriter.Serialize(test);
                    Check(after.ReadBack.SliderMultiplier == value && test.DistancePerBeat == 200, "Requested SV exports with stable DPB");
                    Equivalent(before, after);
                }
                var limited = Fixture(); limited.TimingPoints.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -10 });
                var original = limited.DeepClone();
                try { SliderMultiplierEditing.Apply(limited, .4); throw new Exception("SV limit accepted"); }
                catch (System.IO.InvalidDataException) { Check(limited.ContentEquals(original), "Unrepresentable SV leaves map intact"); }
                var rejected = new Ui(false); rejected.LoadDocument(limited); rejected.Key(114);
                rejected.ClickText(L.Get("timing.overrideSv")); Set(rejected, ".4"); rejected.Key(13); Wait(rejected);
                Check(rejected.View.Document.EffectiveSliderMultiplier == 2 && rejected.View.IsEditingText,
                    "Rejected typed SV retains the last valid value and restores the draft field");
                var authored = new MapDocument { IsDemo = false };
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.Add(new() { TimeMs = 1000, X = 100 }); track.Nodes.Add(new() { TimeMs = 2000, X = 200 });
                authored.Tracks.Add(track);
                var authoredBefore = OsuBeatmapWriter.Serialize(authored);
                SliderMultiplierEditing.Apply(authored, 1.3);
                Equivalent(authoredBefore, OsuBeatmapWriter.Serialize(authored));
                var old = ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{\"SliderMultiplier\":1.4}}");
                Check(!old.OverrideSliderMultiplier && old.SliderMultiplier == 1.4, "Older projects retain SV with editing locked");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static MapDocument Fixture() => OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:2\nSliderTickRate:2\n[TimingPoints]\n0,500,4,1,0,80,1,1\n2000,400,3,2,1,60,1,0\n[HitObjects]\n100,192,500,2,0,L|300:192,2,200\n200,192,2500,2,0,L|400:192,1,200\n256,192,4000,8,0,5000\n");

    private static void Set(Ui ui, string value)
    {
        var field = ui.View.TimingFields.Single(f => f.Key == "page.sliderMultiplier");
        ui.Click(field.Bounds.X + 5, field.Bounds.Y + 5); ui.Key('A', ctrl: true);
        ui.View.PasteTimingText(value, ui.View.TimingInputSession); ui.Paint();
    }

    private static void ClickArrow(Ui ui, float x, float y, bool ctrl)
    {
        ui.View.PointerDown(x, y, 0, false, ctrl); ui.View.PointerUp(x, y, 0); ui.Paint();
        Wait(ui);
    }

    private static void Wait(Ui ui)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (ui.View.SliderMultiplierValidationBusy && timeout.ElapsedMilliseconds < 10000)
        { System.Threading.Thread.Sleep(1); ui.Paint(); }
        Check(!ui.View.SliderMultiplierValidationBusy, "SV validation completes");
    }

    private static void Equivalent(OsuWriteResult before, OsuWriteResult after)
    {
        foreach (var pair in new[] { (before.PlayableObjects, after.PlayableObjects), (before.PlayableHardRockObjects, after.PlayableHardRockObjects) })
            Check(pair.Item1.Count == pair.Item2.Count && pair.Item1.Zip(pair.Item2).All(p => p.First.Kind == p.Second.Kind
                && Math.Abs(p.First.TimeMs - p.Second.TimeMs) < .001 && Math.Abs(p.First.X - p.Second.X) < .001), "SV preserves NM/HR events including droplets and tiny RNG");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
