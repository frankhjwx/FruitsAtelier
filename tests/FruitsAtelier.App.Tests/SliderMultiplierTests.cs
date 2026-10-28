using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderMultiplierTests
{
    public static int Performance()
    {
        var samples = new List<object>();
        var taskField = typeof(FruitsAtelier.App.Editor.EditorView).GetField("sliderMultiplierValidation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
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
            ui.View.OpenDistanceSnapDialog(); ui.Paint(); ui.ClickText(L.Get("ui.sv"));
            long now = 0; ui.View.SliderMultiplierClock = () => now;
            for (int step = 0; step < 8; step++)
            {
                now = step * 100;
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long processBytes = GC.GetTotalAllocatedBytes();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Set(ui, Math.Round(1.92 + .01 * (step + 1), 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
                double dispatchMs = watch.Elapsed.TotalMilliseconds;
                bool workerStarted = taskField.GetValue(ui.View) is not null;
                ui.Paint();
                samples.Add(new { sliders = count, step, dispatchMs, totalMs = watch.Elapsed.TotalMilliseconds,
                    allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes,
                    processAllocatedBytes = GC.GetTotalAllocatedBytes() - processBytes, workerStarted,
                    sv = ui.View.Document.EffectiveSliderMultiplier });
                Check(ui.View.Document.EffectiveSliderMultiplier == 1.92
                    && ui.Canvas.Texts.Any(t => t.Value == Math.Round(1.92 + .01 * (step + 1), 2).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    "Continuous SV benchmark keeps conversion on the confirmed value");
            }
            ui.ClickText(L.Get("library.apply")); var completion = System.Diagnostics.Stopwatch.StartNew(); Wait(ui);
            samples.Add(new { sliders = count, phase = "apply-validation", completionMs = completion.Elapsed.TotalMilliseconds });
            Check(ui.View.Document.EffectiveSliderMultiplier == 2, "SV benchmark applies the confirmed draft");
        }
        System.IO.Directory.CreateDirectory("artifacts/sv");
        string report = System.Text.Json.JsonSerializer.Serialize(samples, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText("artifacts/sv/performance.json", report); Console.WriteLine(report);
        return 0;
    }

    private static void UnusedTimingLimits()
    {
        foreach (double magnitude in new[] { 10d, 1000 })
        foreach (double value in new[] { .4, 3.6 })
        {
            var map = Fixture();
            // The first point is shadowed before any slider; the last point is after all slider heads.
            map.TimingPoints.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -magnitude, SampleSet = 3, Volume = 35 });
            map.TimingPoints.Add(new() { TimeMs = 100, Uninherited = false, BeatLengthMs = -100 });
            map.TimingPoints.Add(new() { TimeMs = 3000, Uninherited = false, BeatLengthMs = -magnitude, SampleSet = 2, Volume = 45 });
            var before = OsuBeatmapWriter.Serialize(map);
            var cache = new OsuWriteCache();
            var history = new EditorHistory(map);
            history.Begin("Change base SV");
            SliderMultiplierEditing.Apply(history.Document, value);
            history.Commit();
            Verify(); history.Undo(); Verify(); history.Redo(); Verify();
            Check(SliderMultiplierEditing.TryRebase(before, value, out var fast, double.PositiveInfinity),
                "Unused extreme timing does not block fast base SV validation");
            Equivalent(before, fast!);
            Check(fast!.Text == OsuBeatmapWriter.Serialize(history.Document).Text, "Fast and full unused timing compensation agree");

            void Verify()
            {
                var output = OsuBeatmapWriter.Serialize(history.Document, cache: cache);
                var full = OsuBeatmapWriter.Serialize(history.Document);
                var restored = OsuBeatmapWriter.Serialize(ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document)));
                Equivalent(before, output);
                Check(output.Text == full.Text && output.Text == restored.Text, "Unused timing survives cache, persistence and undo");
                Check(output.ReadBack.TimingPoints.Any(p => p.TimeMs == 3000 && p.SampleSet == 2 && p.Volume == 45),
                    "Unused SV compensation retains sample timing");
            }
        }

        var authored = new MapDocument { IsDemo = false, SliderMultiplier = 2 };
        authored.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        authored.TimingPoints.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -10 });
        authored.SliderMultiplierOverride = .4;
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 200 }]);
        authored.Tracks.Add(track);
        var result = OsuBeatmapWriter.Serialize(authored);
        Check(result.ObjectSequenceMatches && result.ReadBack.ImportedSliders.Count == 1,
            "FSlider solves a legal SV when its source timing compensation exceeds the range");
        var lookup = new TimingMap.Lookup(authored);
        var retained = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PixelLength = 200, PathType = 'L' };
        retained.ControlPoints.AddRange([new(100, 192), new(300, 192)]);
        authored.ImportedSliders.Add(retained);
        Check(!lookup.MatchesTiming(authored), "Adding a Legacy head invalidates compensation requirements");
        try { _ = OsuBeatmapWriter.Serialize(authored); throw new Exception("Required Legacy compensation accepted"); }
        catch (System.IO.InvalidDataException error)
        { Check(error.Message == L.Get("timing.sliderMultiplierSvLimit"), "Legacy head retains the exact compensation limit"); }
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                DialogChanges();
                UnusedTimingLimits();
                foreach (double value in new[] { .4, 3.6, 1.3 })
                {
                    var test = Fixture(); var before = OsuBeatmapWriter.Serialize(test);
                    SliderMultiplierEditing.Apply(test, value);
                    var after = OsuBeatmapWriter.Serialize(test);
                    Check(after.ReadBack.SliderMultiplier == value && test.DistancePerBeat == 200, "Requested SV exports with stable DPB");
                    Equivalent(before, after);
                    Check(SliderMultiplierEditing.TryRebase(before, value, out var fast, double.PositiveInfinity), "Local SV validation resolves ordinary adjustments");
                    Check(fast!.Text == after.Text, "Local SV timing matches full export");
                    Equivalent(after, fast);
                    Check(!SliderMultiplierEditing.TryRebase(before, value, out _, 0), "Exhausted fast-check budget falls back without applying");
                }
                var limited = Fixture(); limited.TimingPoints.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -10 });
                foreach (var slider in limited.ImportedSliders) slider.SliderVelocityMultiplier = 10;
                var original = limited.DeepClone();
                try { SliderMultiplierEditing.Apply(limited, .4); throw new Exception("SV limit accepted"); }
                catch (System.IO.InvalidDataException) { Check(limited.ContentEquals(original), "Unrepresentable SV leaves map intact"); }
                var timingWithOverride = Fixture();
                timingWithOverride.TimingPoints.Add(new() { TimeMs = 500, Uninherited = false, BeatLengthMs = -100 });
                SliderMultiplierEditing.Apply(timingWithOverride, 1.3);
                var timingUi = new Ui(false); timingUi.LoadDocument(timingWithOverride); timingUi.Resize(980, 620);
                timingUi.Key(117); timingUi.ClickText(L.Get("timing.green")); timingUi.Key('A', ctrl: true); timingUi.Key(46);
                timingUi.View.PasteTimingText("500,-10,4,1,0,100,0,0", timingUi.View.TimingInputSession);
                timingUi.Key(13);
                Check(!timingUi.View.TimingSetupVisible && timingUi.View.Document.ImportedSliders.All(s => s.SliderVelocityMultiplier == 1),
                    "A green edit overrode independent Legacy SV");
                _ = OsuBeatmapWriter.Serialize(timingUi.View.Document);
                timingUi.Key(27);
                var slowest = Fixture(); slowest.TimingPoints.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -1000 });
                foreach (var slider in slowest.ImportedSliders) slider.SliderVelocityMultiplier = .1;
                var slowBefore = OsuBeatmapWriter.Serialize(slowest);
                try { SliderMultiplierEditing.CheckLimits(slowBefore, 3.6); throw new Exception("Minimum green SV accepted"); }
                catch (System.IO.InvalidDataException) { }
                var authored = new MapDocument { IsDemo = false };
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.Add(new() { TimeMs = 1000, X = 100 }); track.Nodes.Add(new() { TimeMs = 2000, X = 200 });
                authored.Tracks.Add(track);
                var authoredBefore = OsuBeatmapWriter.Serialize(authored);
                SliderMultiplierEditing.Apply(authored, 1.3);
                Equivalent(authoredBefore, OsuBeatmapWriter.Serialize(authored));
                Check(SliderMultiplierEditing.TryRebase(authoredBefore, 1.3, out var authoredFast, double.PositiveInfinity), "Generated slider geometry supports local SV validation");
                Equivalent(OsuBeatmapWriter.Serialize(authored), authoredFast!);
                FastBoundaryCases();

                var rounding = new MapDocument { IsDemo = false, SliderMultiplier = 1.4 };
                var shortRepeat = new ImportedSlider { TimeMs = 500, X = 100, Y = 192, PixelLength = 28.2799999, SpanCount = 2, PathType = 'L' };
                shortRepeat.ControlPoints.AddRange([new(100, 192), new(200, 192)]); rounding.ImportedSliders.Add(shortRepeat);
                var roundingBefore = OsuBeatmapWriter.Serialize(rounding);
                Check(!SliderMultiplierEditing.TryRebase(roundingBefore, 1.3, out _, double.PositiveInfinity), "Tiny count boundary requires full validation");
                try { SliderMultiplierEditing.Rebase(roundingBefore, 1.3); throw new Exception("Changed tiny count accepted"); }
                catch (System.IO.InvalidDataException error)
                {
                    Check(error.Message.Contains("500") && error.Message != L.Get("timing.sliderMultiplierPreservation"),
                        "Rejected SV reports the affected slider time and object-count change");
                    var errorUi = new Ui(false); errorUi.LoadDocument(rounding); errorUi.Resize(980, 620);
                    var errorSnapshot = errorUi.View.Document.DeepClone();
                    errorUi.View.ShowError(error.Message); errorUi.Paint();
                    Check(errorUi.View.ErrorVisible && errorUi.Canvas.Texts.Any(t => t.Value.Contains("500")),
                        "SV failure keeps the affected slider time visible in the narrow error dialog");
                    errorUi.Key(13);
                    Check(!errorUi.View.ErrorVisible && errorUi.View.Document.ContentEquals(errorSnapshot),
                        "Dismissing an SV failure preserves the project");
                }
                var positionBefore = OsuBeatmapWriter.Serialize(Fixture());
                var displaced = positionBefore.PlayableObjects[0];
                var invalidPosition = new OsuWriteResult
                {
                    Text = positionBefore.Text, ReadBack = positionBefore.ReadBack,
                    ObjectSources = positionBefore.ObjectSources, Diagnostics = positionBefore.Diagnostics,
                    ObjectSequenceMatches = true, PlayableHardRockObjects = positionBefore.PlayableHardRockObjects,
                    PlayableObjects = positionBefore.PlayableObjects.Select((o, i) => i == 0 ? o with { X = o.X + 2 } : o).ToArray()
                };
                try { SliderMultiplierEditing.Rebase(invalidPosition, 1.3); throw new Exception("Changed position accepted"); }
                catch (System.IO.InvalidDataException error)
                {
                    Check(error.Message == L.Get("timing.sliderMultiplierPositionChange", displaced.TimeMs, "NM",
                        displaced.TimeMs, displaced.TimeMs, displaced.X + 2, displaced.X),
                        "Rejected SV explains mode, affected time and before/after position");
                }
                var old = ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{\"SliderMultiplier\":1.4}}");
                Check(!old.OverrideSliderMultiplier && old.SliderMultiplier == 1.4, "Older projects retain SV with editing locked");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static MapDocument Fixture() => OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:2\nSliderTickRate:2\n[TimingPoints]\n0,500,4,1,0,80,1,1\n2000,400,3,2,1,60,1,0\n[HitObjects]\n100,192,500,2,0,L|300:192,2,200\n200,192,2500,2,0,L|400:192,1,200\n256,192,4000,8,0,5000\n");

    private static void FastBoundaryCases()
    {
        foreach (double length in new[] { 31.99999, 32, 32.00001, 100, 200 })
        foreach (double value in new[] { 1.3, 1.99, 2.01, 3.6 })
        {
            var map = Fixture();
            map.ImportedSliders[0].OriginalLine = null;
            map.ImportedSliders[0].PixelLength = length;
            map.ImportedSliders[0].PathType = 'B';
            map.ImportedSliders[0].ControlPoints.Insert(1, new(200, 100));
            map.Fruits.Add(new() { TimeMs = 5800, X = 256 });
            var baseline = OsuBeatmapWriter.Serialize(map);
            if (!SliderMultiplierEditing.TryRebase(baseline, value, out var fast, double.PositiveInfinity)) continue;
            var full = SliderMultiplierEditing.Rebase(baseline, value);
            Check(fast!.Text == full.Text, "Local timing matches full validation near tiny thresholds");
            foreach (var pair in new[] { (fast.PlayableObjects, full.PlayableObjects), (fast.PlayableHardRockObjects, full.PlayableHardRockObjects) })
                Check(pair.Item1.SequenceEqual(pair.Item2), "Local validation reproduces full NM/HR events and downstream banana RNG");
        }
    }

    private static void Set(Ui ui, string value)
    {
        var field = ui.View.DistanceSnapBaseFieldBounds;
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true);
        ui.View.PasteFieldText(value); ui.Paint(); ui.Key(13);
    }

    private static void Wait(Ui ui)
    {
        AdvanceIdle(ui); ui.Paint();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (ui.View.SliderMultiplierValidationBusy && timeout.ElapsedMilliseconds < 10000)
        { System.Threading.Thread.Sleep(1); ui.Paint(); }
        Check(!ui.View.SliderMultiplierValidationBusy, "SV validation completes");
    }

    private static void AdvanceIdle(Ui ui)
    {
        long now = ui.View.SliderMultiplierClock();
        ui.View.SliderMultiplierClock = () => now + 1000;
    }

    private static void ApplyBeforePaint(Ui ui)
    {
        // Painting publishes completed validation; let pending-state checks run before that boundary.
        var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("library.apply"));
        ui.View.PointerDown(label.X + 4, label.Y + 5, 0, false, false);
        ui.View.PointerUp(label.X + 4, label.Y + 5, 0);
        Check(ui.View.SliderMultiplierValidationBusy, "Apply queues validation before painting");
    }

    private static void DialogChanges()
    {
        var ui = new Ui(false); ui.LoadDocument(Fixture()); ui.Resize(980, 620); ui.Key(114);
        Check(ui.View.TimingFields.All(f => f.Key != "page.sliderMultiplier"), "Timing contains only the SV navigation entry");
        var link = ui.View.TimingSliderVelocityLinkBounds;
        ui.Click(link.X + 8, link.Y + 8);
        Check(ui.View.DistanceSnapDialogVisible, "Timing entry opens DS configuration");
        var original = ui.View.Document.DeepClone();
        ui.ClickText(L.Get("ui.sv"));
        Check(ui.Canvas.Texts.Any(t => t.Value == "2"), "DPB 200 converts to SV 2");
        ui.ClickText(L.Get("ui.dpb"));
        Check(ui.Canvas.Texts.Any(t => t.Value == "200"), "SV converts back to DPB without content changes");
        ui.ClickText(L.Get("library.apply"));
        Check(ui.View.Document.ContentEquals(original), "Unit-only Apply does not add a content edit");
        ui.View.OpenDistanceSnapDialog(); ui.Paint(); ui.ClickText(L.Get("ui.sv"));
        Set(ui, "1.3"); ui.ClickText(L.Get("ui.dpb"));
        Check(ui.Canvas.Texts.Any(t => t.Value == "130"), "Edited SV converts to DPB in the same draft");
        ui.ClickText(L.Get("ui.sv")); ApplyBeforePaint(ui);
        Check(ui.View.SliderMultiplierValidationBusy && !ui.View.PrepareFileOperation(), "SV Apply waits for background validation");
        Wait(ui);
        Check(!ui.View.DistanceSnapDialogVisible && ui.View.Document.EffectiveSliderMultiplier == 1.3
            && ui.View.Document.DistancePerBeat == 130, "Apply commits SV and DPB together");
        Equivalent(OsuBeatmapWriter.Serialize(original), OsuBeatmapWriter.Serialize(ui.View.Document));
        var confirmed = ui.View.Document.DeepClone();
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(confirmed)).ContentEquals(confirmed), "Unified values persist");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(original), "SV and DPB undo together");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(confirmed), "SV and DPB redo together");
        ui.View.OpenDistanceSnapDialog(); ui.Paint(); Set(ui, "3.61"); ui.ClickText(L.Get("library.apply"));
        Check(!ui.View.SliderMultiplierValidationBusy && ui.View.Document.ContentEquals(confirmed), "Invalid SV remains a draft");
        Set(ui, "1.4"); ApplyBeforePaint(ui); ui.Key(27); Wait(ui);
        Check(ui.View.Document.ContentEquals(confirmed), "Cancel discards pending SV validation");
        ui.View.OpenDistanceSnapDialog(); ui.Paint(); Set(ui, "1.4"); ApplyBeforePaint(ui);
        ui.View.Document.Fruits.Add(new() { TimeMs = 6000, X = 200 }); Wait(ui);
        Check(ui.View.Document.EffectiveSliderMultiplier == 1.3 && ui.View.Document.Fruits.Count == 1, "Stale validation cannot overwrite later content");
        ui.Key(27);
        var limited = Fixture(); limited.TimingPoints.Add(new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -10 });
        foreach (var slider in limited.ImportedSliders) slider.SliderVelocityMultiplier = 10;
        var rejected = new Ui(false); rejected.LoadDocument(limited); rejected.View.OpenDistanceSnapDialog(); rejected.Paint();
        Set(rejected, "40"); rejected.ClickText(L.Get("library.apply")); Wait(rejected);
        Check(rejected.View.DistanceSnapDialogVisible && rejected.View.Document.ContentEquals(limited), "Unrepresentable compensation rejects the entire draft");
        var conflict = Fixture(); conflict.SliderMultiplierOverride = 1.3; conflict.DistancePerBeatOverride = 350;
        var loaded = ProjectSerializer.Read(ProjectSerializer.Serialize(conflict));
        Check(loaded.EffectiveSliderMultiplier == 1.3 && loaded.DistancePerBeat == 130, "Loading legacy conflicts gives SV precedence");
        loaded = ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{\"SliderMultiplier\":1.4,\"DistancePerBeatOverride\":350}}");
        Check(loaded.DistancePerBeat == 140, "Original SV also takes precedence over legacy DPB");
        loaded = ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{\"SliderMultiplier\":0.2,\"DistancePerBeatOverride\":350}}");
        Check(loaded.DistancePerBeat == 20 && loaded.DistancePerBeatOverride is null,
            "Conflict loading preserves imported SV outside the editing range");
    }

    private static void Equivalent(OsuWriteResult before, OsuWriteResult after)
    {
        foreach (var pair in new[] { (before.PlayableObjects, after.PlayableObjects), (before.PlayableHardRockObjects, after.PlayableHardRockObjects) })
            Check(pair.Item1.Count == pair.Item2.Count && pair.Item1.Zip(pair.Item2).All(p => p.First.Kind == p.Second.Kind
                && Math.Abs(p.First.TimeMs - p.Second.TimeMs) < .001 && Math.Abs(p.First.X - p.Second.X) < .001), "SV preserves NM/HR events including droplets and tiny RNG");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
