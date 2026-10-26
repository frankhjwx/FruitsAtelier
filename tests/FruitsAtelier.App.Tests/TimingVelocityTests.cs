using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingVelocityTests
{
    internal static void Benchmark()
    {
        var timing = Enumerable.Range(1, 200).Select(i => $"{i * 1000},{(i % 2 == 0 ? -50 : -25)},4,1,0,{(i % 5 == 0 ? 35 : 70)},0,0");
        var objects = Enumerable.Range(1, 200).Select(i => $"100,192,{i * 1000},2,0,L|240:192,1,140");
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n"
            + string.Join('\n', timing) + "\n[HitObjects]\n" + string.Join('\n', objects) + "\n256,192,202000,8,0,203000\n");
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 204000, X = 100 }, new() { TimeMs = 204500, X = 300 }]);
        map.Tracks.Add(track); WorkspaceTimingSynchronization.NormalizeDocument(map);
        var ui = new Ui(false); ui.LoadDocument(map); ui.Key(114);
        var before = ui.View.Document.DeepClone();
        var report = new List<string>();
        for (int mode = 0; mode < 2; mode++)
        {
            for (int i = 0; i < 10; i++) ui.Paint();
            var snapshot = ui.View.DisplayedTiming;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                ui.View.PointerMove(180 + i % 100, 240, false, false); ui.Paint();
            }
            timer.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(ReferenceEquals(snapshot, ui.View.DisplayedTiming) && ui.View.Document.ContentEquals(before), "Warm timing presentation reuses its snapshot without edits");
            report.Add($"{(mode == 0 ? "Hidden" : "Visible")} SV, {snapshot.Count} rows: pointer + RecordingCanvas frame {timer.Elapsed.TotalMilliseconds / 100:F3} ms, {allocated / 100} bytes/frame (GPU and display latency excluded).");
            if (mode == 0) ui.Click(ui.View.TimingSvDisplayBounds.X + 10, ui.View.TimingSvDisplayBounds.Y + 10);
        }
        Directory.CreateDirectory("artifacts/tests/timing-sv");
        File.WriteAllLines("artifacts/tests/timing-sv/performance.txt", report);
        foreach (string line in report) Console.WriteLine(line);
    }

    internal static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n1000,-50,4,2,2,35,0,1\n2000,-25,4,2,2,35,0,1\n[HitObjects]\n100,192,1000,2,0,L|240:192,1,140\n100,192,2000,2,0,L|240:192,1,140\n");
                var track = new CurveTrack { Kind = CurveKind.Linear, SliderVelocityMultiplier = 1.7 };
                track.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 3500, X = 200 }]);
                map.Tracks.Add(track);
                WorkspaceTimingSynchronization.NormalizeDocument(map);
                var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(980, 620); ui.Key(114);
                var before = ui.View.Document.DeepClone();
                Check(!ui.View.ShowTimingSv && ui.View.TimingSvLocked, "Initial switches hide SV and lock it independently");
                Check(Text(ui.View.DisplayedTiming) == Text(before.TimingPoints), "Hidden mode retains authoring timing");
                ui.ClickText(L.Get("timing.showSv"));
                Check(ui.View.ShowTimingSv && ui.View.TimingSvLocked, "Showing SV retains the lock");
                Check(Text(ui.View.DisplayedTiming) == Text(OsuBeatmapWriter.Serialize(before).ReadBack.TimingPoints), "Visible timing exactly matches exported timing");
                Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Display switch does not edit content");
                ui.Key(117); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                ui.Key(13);
                Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Accepting unchanged exported rows preserves authoring order and text");
                Select(ui, 1000);
                string copied = ""; ui.View.RequestCopyText = value => copied = value;
                ui.View.PasteTimingText("2200,-10,4,1,0,100,0,0", ui.View.TimingInputSession); ui.Paint();
                ui.Key('C', ctrl: true);
                Check(Math.Abs(TimingEditing.Parse(copied).Single().BeatLengthMs + 25) < 1e-9, "Clipboard cannot bypass the SV lock");
                ui.Key(27);
                Select(ui, 1000);
                Check(ui.View.TimingFields.All(f => f.Key != "sv") && ui.View.TimingFields.Any(f => f.Key == "offset"), "Compound row locks only SV");
                ui.ClickText(L.Get("timing.audio")); Set(ui, "volume", "70"); ui.Key(13);
                Check(ui.View.Document.TimingPoints.Single(p => !p.Uninherited).Volume == 70, "Compound volume remains editable");
                Check(ui.View.Document.ImportedSliders[0].SliderVelocityMultiplier == before.ImportedSliders[0].SliderVelocityMultiplier, "Sample edits preserve slider SV");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "Sample edit undoes in one step");
                Select(ui, 2000);
                Check(ui.View.TimingFields.Count == 0, "Pure SV row is read only while locked");
                ClickLock(ui);
                Check(ui.View.ShowTimingSv && !ui.View.TimingSvLocked, "Unlocking retains the display mode");
                Set(ui, "sv", "3"); ui.Key(13);
                Check(Math.Abs(ui.View.Document.ImportedSliders[1].SliderVelocityMultiplier!.Value - 3) < 1e-9, $"Unlocked Legacy SV edits the slider: {ui.View.Document.ImportedSliders[1].SliderVelocityMultiplier}; {ui.View.StatusMessage}; modal={ui.View.TimingSetupVisible}");
                Check(Math.Abs(new TimingMap.Lookup(OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack).At(2000).SliderVelocityMultiplier - 3) < 1e-6, "Legacy edit reaches exported SV");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "Legacy edit undoes with timing");
                Select(ui, 3000); Set(ui, "sv", "0.1"); ui.Key(13);
                Check(ui.View.TimingSetupVisible && ui.View.Document.ContentEquals(before), "An impossible fixed FSlider SV is rejected atomically");
                ui.Key(27);
                Select(ui, 3000); Set(ui, "sv", "2"); ui.Key(13);
                Check(ui.View.Document.Tracks.Single().SliderVelocityMultiplier == 2, "Unlocked FSlider SV is authored on the track");
                var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
                Check(saved.ContentEquals(ui.View.Document), "FSlider SV survives single-document persistence");
                var project = BeatmapProject.FromDocuments([saved]);
                Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Difficulties[0].Document.ContentEquals(saved), "FSlider SV survives project persistence");
                Check(Math.Abs(new TimingMap.Lookup(OsuBeatmapWriter.Serialize(saved).ReadBack).At(3000).SliderVelocityMultiplier - 2) < 1e-6, "FSlider SV survives export");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "FSlider edit undoes");
                ui.ClickText(L.Get("timing.showSv"));
                Check(!ui.View.ShowTimingSv && !ui.View.TimingSvLocked, "Hiding SV preserves unlocked state");
                Check(Text(ui.View.DisplayedTiming) == Text(before.TimingPoints), "Hidden timing is restored after edits and undo");
                ClickLock(ui);
                Check(!ui.View.ShowTimingSv && ui.View.TimingSvLocked && ui.View.Document.ContentEquals(before), "Lock can change while SV is hidden without content edits");
                var fractional = new MapDocument { IsDemo = false };
                fractional.TimingPoints.AddRange([new() { TimeMs = .25, BeatLengthMs = 500 },
                    new() { TimeMs = 1000.25, BeatLengthMs = -100, Uninherited = false, Volume = 35 }]);
                fractional.Fruits.Add(new() { TimeMs = 2000, X = 100 });
                ui.LoadDocument(fractional); ui.Key(114);
                ui.Click(ui.View.TimingSvDisplayBounds.X + 10, ui.View.TimingSvDisplayBounds.Y + 10);
                Select(ui, 1000.25); ui.ClickText(L.Get("timing.audio")); Set(ui, "volume", "40"); ui.Key(13);
                Check(ui.View.Document.TimingPoints.Count == 2 && ui.View.Document.TimingPoints.Single(p => !p.Uninherited).TimeMs == 1000.25
                    && ui.View.Document.TimingPoints.Single(p => !p.Uninherited).Volume == 40, "Fractional exported rows update their source without duplicate timing");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void ClickLock(Ui ui)
    {
        var bounds = ui.View.TimingSvLockBounds;
        ui.Click(bounds.X + 10, bounds.Y + 10);
    }

    private static void Select(Ui ui, double time)
    {
        ui.View.UpdateTransport(time, 10000, true, false, false, null, null); ui.Key(117);
        var bounds = ui.View.TimingSetupBounds;
        var label = ui.Canvas.Texts.Single(t => t.Value == TimeSpan.FromMilliseconds(time).ToString(@"mm\:ss\:fff") && t.X > bounds.X + bounds.Width * .4f && t.Y > bounds.Y + 120);
        ui.Click(label.X + 2, label.Y + 3);
    }

    private static string Text(IEnumerable<TimingPoint> points) => TimingEditing.Serialize(points.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder));
    private static void Set(Ui ui, string key, string value)
    {
        var field = ui.View.TimingFields.Single(f => f.Key == key).Bounds;
        ui.Click(field.X + 4, field.Y + 4); ui.Key('A', ctrl: true); ui.Type(value); ui.Key(13);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
