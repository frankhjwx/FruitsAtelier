using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingEditorTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string lang in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(lang);
                var map = Map(); var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(980, 620);
                ui.View.UpdateTransport(0, 10000, true, false, false, null, null); ui.Paint();
                ui.Key(117); Check(ui.View.TimingSetupVisible, "F6 opens timing setup");
                Set(ui, "bpm", "150"); Check(ui.View.Document.ContentEquals(map), "Draft does not mutate live map");
                ui.Key(27); Check(!ui.View.TimingSetupVisible && ui.View.Document.ContentEquals(map), "Escape cancels whole draft");
                ui.Key(117); ui.Key('A', true); ui.Key(46); ui.Key(13);
                Check(ui.View.Document.TimingPoints.Count == 1 && !ui.View.IsDirty, "First red survives multi-delete; unchanged confirm is clean");
                ui.View.UpdateTransport(1000.9, 10000, true, false, false, null, null);
                ui.Key('P', ctrl: true, shift: true); Check(ui.View.TimingSetupVisible, "Green shortcut opens setup");
                Check(ui.View.TimingFields.All(f => f.Key == "offset"), "Green timing page exposes only offset");
                Check(ui.View.TimingFields.Single().Value == "1000", "Green creation truncates fractional milliseconds");
                ui.ClickText(L.Get("timing.audio")); Set(ui, "volume", "35");
                Check(ui.View.TimingFields.All(f => f.Key != "index"), "Default sample index is read-only");
                ui.ClickText(L.Get("timing.custom")); Set(ui, "index", "2");
                ui.ClickText(L.Get("timing.drum"));
                var samples = new List<Hitsound>(); ui.View.RequestAuditionHitsound = samples.Add;
                foreach (string name in new[] { "hitnormal", "hitfinish", "hitwhistle", "hitclap" }) ClickProperty(ui, L.Get($"timing.{name}"));
                Check(samples.Select(s => s.Name).SequenceEqual(new[] { "hitnormal", "hitfinish", "hitwhistle", "hitclap" }) && samples.All(s => s.SampleSet == 3 && Math.Abs(s.Volume - .35f) < .001), "Audition buttons resolve the selected bank and volume independently");
                ui.ClickText(L.Get("timing.custom1")); Check(ui.View.TimingFields.All(f => f.Key != "index"), "Custom 1 locks numeric sample index");
                ui.ClickText(L.Get("timing.custom"));

                Check(ui.View.TimingFields.All(f => f.Bounds.Bottom <= ui.View.TimingSetupBounds.Bottom - 188), "Audio inputs fit narrow window above apply options");
                ui.ClickText(L.Get("timing.style")); ClickProperty(ui, L.Get("timing.kiai"));
                string copied = ""; ui.View.RequestCopyText = text => copied = text;
                ui.Key('C', ctrl: true); Check(copied.Contains(",3,2,35,0,1"), "Clipboard includes edited samples, volume and Kiai");
                ui.Key(13);
                var green = ui.View.Document.TimingPoints.Single(p => !p.Uninherited);
                Check(green.TimeMs == 1000 && green.BeatLengthMs == -100 && green.Volume == 35 && green.Effects == 1, "All tabs commit together");
                var saved = ui.View.Document.DeepClone();
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "One undo restores before dialog");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(saved), "Redo restores entire edit");
                ui.Key('I', ctrl: true); Check(ui.View.Document.TimingPoints.Count == 1, "Ctrl+I deletes current timing section");
                ui.Key('Z', ctrl: true);
                ui.Key(117); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                int session = ui.View.TimingInputSession;
                ui.View.PasteTimingText("broken", session); Check(ui.View.TimingSetupVisible, "Invalid paste leaves dialog open");
                ui.Key(27); ui.View.PasteTimingText(copied, session); Check(ui.View.Document.ContentEquals(saved), "Delayed clipboard cannot change closed dialog");
                ui.ClickText(L.Get("timing.detailsPanel") + " ▾"); ui.ClickText(L.Get("timing.panel"));
                Check(ui.View.TimingPageVisible, "Panel dropdown enters timing mode");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.waveform")) && !ui.Canvas.Circles.Any(c => c.Color == 0xFFFFFF), "Timing panel replaces notes with audio view");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.panel") + " ▾"), "Timing panel retains its switcher");
                Set(ui, "page.bpm", "180"); Check(Math.Abs(TimingMap.At(ui.View.Document, 0).BeatLengthMs - 60000d / 180) < .001, "Timing page edits BPM");
                var bpmField = ui.View.TimingFields.Single(f => f.Key == "page.bpm").Bounds;
                ui.Click(bpmField.X + 8, bpmField.Y + 8); ui.Key('A', ctrl: true); ui.Type("160");
                ui.Click(bpmField.Right + 12, bpmField.Y + 8);
                Check(Math.Abs(60000 / TimingMap.At(ui.View.Document, 0).BeatLengthMs - 161) < .001, "Stepper uses newly committed input");
                ui.Key(112); Check(!ui.View.TimingPageVisible, "F1 returns to compose");
                var beforeReset = ui.View.Document.DeepClone();
                ui.ClickText(L.Get("timeline.timingMenu")); ui.ClickText(L.Get("timing.reset")); ui.Key(13);
                Check(ui.View.TimingPageVisible && ui.View.Document.TimingPoints.All(p => !p.Uninherited), "Reset clears current red and enters timing page");
                Set(ui, "page.bpm", "145");
                Check(ui.View.Document.TimingPoints.Count(p => p.Uninherited) == 1, "Entering BPM restores reset section");
                ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(beforeReset), "Reset and replacement undo independently");
            }
        }
        finally { L.SetLanguage(language); }
    }

    public static void Metronome()
    {
        var ui = new Ui(false); ui.LoadDocument(Map());
        var events = new List<(Hitsound Sound, double Time)>();
        ui.View.RequestScheduleHitsound = (s, t) => events.Add((s, t));
        ui.View.RequestStopHitsounds = () => { };
        ui.View.UpdateTransport(0, 10000, true, false, false, null, null);
        ui.Key(114);
        void At(double time, bool playing = true) => ui.View.UpdateTransport(time, 10000, true, playing, false, null, null);
        for (int time = 0; time <= 400; time += 50) At(time);
        Check(events.Select(e => e.Time).SequenceEqual(new double[] { 0, 500 }), "Normal metronome schedules whole beats once");
        Check(events[0].Sound.Name == "metronome-downbeat" && events[1].Sound.Name == "metronome-tick", "Measure starts use distinct accent sample");
        events.Clear(); ui.Key(17, ctrl: true);
        for (int time = 501; time <= 1001; time += 50) At(time);
        Check(events.Select(e => e.Time).SequenceEqual(new double[] { 750, 1000 }), "Ctrl with even snap schedules two ticks per beat");
        events.Clear(); ui.View.KeyUp(17);
        for (int time = 1100; time <= 1400; time += 50) At(time);
        Check(events.Select(e => e.Time).SequenceEqual(new double[] { 1500 }), "Releasing Ctrl restores whole beats");
        events.Clear(); At(1400, false); At(1400, false); Check(events.Count == 0, "Paused updates are silent");
        At(0); Check(events.Count == 1 && events[0].Time == 0, "Backward seek restarts schedule at new position");
        ui.Key(112); events.Clear(); At(10); Check(events.All(e => !e.Sound.Name.StartsWith("metronome")), "Compose has no timing metronome");
        foreach (int snap in new[] { 3, 6, 9, 12, 5, 7, 16 })
        {
            ui.Key(114); ui.SetSnapDivisor(snap); events.Clear(); ui.Key(17, ctrl: true);
            At(0, false);
            for (int time = 1; time <= 451; time += 50) At(time);
            int expected = snap % 3 == 0 ? 3 : snap % 2 == 0 ? 2 : 1;
            Check(events.Count == expected, "Ctrl respects triplet/even/odd Snap family: " + snap);
            ui.Key(112);
        }
    }

    public static void Waveform()
    {
        var ui = new Ui(false); var map = Map(); map.AudioPath = "synthetic.wav";
        int loads = 0;
        var pending = new TaskCompletionSource<AudioWaveform>();
        ui.View.RequestWaveform = (_, _) => { loads++; return pending.Task; };
        ui.LoadDocument(map); ui.Resize(980, 620);
        ui.View.UpdateTransport(0, 10000, true, false, false, null, map.AudioPath);
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, map.AudioPath); ui.Key(114);
        Check(!ui.View.WaveformNeedsRedraw, "Pending waveform does not busy-loop rendering");
        pending.SetResult(new AudioWaveform(Enumerable.Range(0, 10000).Select(i => .3f + .7f * (float)Math.Abs(Math.Sin(i * .017))).ToArray(), 1));
        Check(ui.View.WaveformNeedsRedraw, "Decoded waveform wakes a paused editor"); ui.Paint();
        Check(!ui.View.WaveformNeedsRedraw, "Completed waveform is consumed once");
        var before = ui.View.Document.DeepClone();
        var r = ui.View.WaveformBounds;
        RecordingCanvas.Outline[] Envelope() => ui.Canvas.Fills.Where(f => f.Color == 0x59D3C3 && f.Bounds.Width == 2 && f.Bounds.Y > r.Y).ToArray();
        var envelope = Envelope();
        Check(envelope.Length > 20 && envelope.All(f => f.Bounds.Height <= r.Height * .32f + .001), "Waveform uses a filled envelope at half the former height");
        Check(ui.Canvas.Texts.All(t => !t.Value.Contains("Alt + wheel")), "Waveform omits instructional caption");
        var quarterTicks = ui.Canvas.Lines.Where(l => l.Y2 == ui.View.WaveformRulerY && l.Y1 < l.Y2).ToArray();
        Check(quarterTicks.Any(l => l.Color == 0x66AAFF), "Waveform ruler includes quarter snap ticks");
        ui.View.UpdateTransport(1001, 10000, true, true, false, null, map.AudioPath); ui.Paint();
        var moved = Envelope();
        Check(envelope.Take(20).Select(f => f.Bounds.Height).SequenceEqual(moved.Take(20).Select(f => f.Bounds.Height))
            && moved[10].Bounds.X < envelope[10].Bounds.X, "Playback translates fixed envelope peaks without resampling flicker");
        ui.SetSnapDivisor(3);
        var tripletTicks = ui.Canvas.Lines.Where(l => l.Y2 == ui.View.WaveformRulerY && l.Y1 < l.Y2).ToArray();
        Check(tripletTicks.Length < quarterTicks.Length && tripletTicks.Any(l => l.Color == 0xBB66EE), "Changing Snap updates ruler subdivisions and colours");
        float RedX() => ui.Canvas.Texts.Single(t => t.Value == "120 BPM").X;
        float red = RedX();
        ui.View.Wheel(r.X + r.Width / 2, r.Y + 80, 120, false, false, true); ui.Paint();
        Check(RedX() < red && loads == 1, $"Zoom spreads red lines without decoding audio again: {red} -> {RedX()}, loads {loads}, playhead {ui.View.PlayheadMs}");
        ui.Click(RedX() - 5, r.Y + 80);
        Check(ui.View.TimingSetupVisible && ui.View.TimingFields.Any(f => f.Key == "bpm"), "Clicking a red line opens its BPM properties");
        ui.Key(27); ui.Key(112);
        Check(ui.View.Document.ContentEquals(before), "Waveform navigation and cancelled timing edit preserve content");
        var dense = Map();
        foreach (int time in new[] { 10, 20, 30, 1000, 2000 })
            dense.TimingPoints.Add(new TimingPoint { TimeMs = time, BeatLengthMs = 400, Uninherited = true });
        ui.LoadDocument(dense); ui.Key(114);
        r = ui.View.WaveformBounds;
        var labels = ui.Canvas.Texts.Where(t => t.Value.EndsWith(" BPM") && t.Y == r.Y + 8).OrderBy(t => t.X).ToArray();
        Check(labels.Length > 0 && labels.Length < dense.TimingPoints.Count, "Dense red points omit crowded labels");
        for (int i = 1; i < labels.Length; i++)
            Check(labels[i].X >= labels[i - 1].X + labels[i - 1].Value.Length * 12 * .6f + 8,
                "Visible BPM labels retain a gap");
        Check(ui.Canvas.Lines.Count(l => l.Y1 == r.Y + 30 && l.Y2 == ui.View.WaveformRulerY) == dense.TimingPoints.Count,
            "Crowded labels preserve every red timing line");
    }

    private static MapDocument Map() => OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n128,192,1200,1,0,0:0:0:0:\n");
    private static void ClickProperty(Ui ui, string text)
    {
        var bounds = ui.View.TimingSetupBounds;
        var label = ui.Canvas.Texts.Single(t => t.Value == text && t.X > bounds.X && t.X < bounds.X + 380 && t.Y > bounds.Y + 98);
        ui.Click(label.X + 2, label.Y + 2);
    }

    private static void Set(Ui ui, string key, string value)
    {
        var bounds = ui.View.TimingFields.Single(f => f.Key == key).Bounds;
        ui.Click(bounds.X + 8, bounds.Y + 8); ui.Key('A', ctrl: true); ui.Type(value); ui.Key(13);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
