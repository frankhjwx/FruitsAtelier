using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingFeedbackTests
{
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
            map.TimingPoints.AddRange([
                new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, Volume = 80 },
                new() { TimeMs = 500, BeatLengthMs = -50, Uninherited = false, Volume = 20, SampleSet = 2, SourceOrder = 1 },
                new() { TimeMs = 2500, BeatLengthMs = -100, Uninherited = false, Volume = 80, SampleSet = 3, SourceOrder = 2 }]);
            foreach (int time in new[] { 1000, 1250, 2000, 2250 })
                map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,{(time < 1500 ? 2 : 0)},{(time < 1500 ? 2 : 3)}:2:0:0:" });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 1600); ui.View.ShowHitsoundingPage(); ui.Paint();
            var baseline = ui.View.Document.DeepClone();
            foreach (double time in new[] { 500d, 2500d })
            {
                var bounds = ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints.Single(p => p.TimeMs == time));
                ui.View.PointerDown(bounds.X + 4, bounds.Y + 10, 0, false, true);
                ui.View.PointerUp(bounds.X + 4, bounds.Y + 10, 0); ui.Paint();
            }
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.generateRamp")), "Two green points reveal the volume ramp controls");
            ui.ClickText("1/2");
            Check(baseline.ContentEquals(ui.View.Document), "Choosing an interval does not edit content");
            ui.ClickText(L.Get("hitsounding.generateRamp"));
            Check(ui.View.Document.TimingPoints.Count == 10 && ui.View.Document.TimingPoints.Single(p => p.TimeMs == 1500).Volume == 50,
                "Half-beat ramp generates interior lines with linear volume");
            Check(ui.View.Document.TimingPoints.Single(p => p.TimeMs == 1500).BeatLengthMs == baseline.TimingPoints[1].BeatLengthMs, "Ramp preserves active SV");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Ramp is one undo step");
            ui.Key(89, ctrl: true); Check(ui.View.Document.TimingPoints.Count == 10, "Ramp supports redo");
            ui.Key(90, ctrl: true);

            ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 1600); ui.View.ShowHitsoundingPage();
            ui.View.UpdateTransport(1000, 8000, true, false, false, null, null); ui.Paint();
            baseline = ui.View.Document.DeepClone();
            var volume = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.volume"));
            ui.Click(volume.X + 110, volume.Y + 5); ui.Key(65, ctrl: true); ui.Type("37"); ui.Key(13);
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Affecting-line values remain drafts before applying");
            ui.ClickText(L.Get("hitsounding.applyAffecting"));
            Check(ui.View.Document.TimingPoints.Count == 3 && ui.View.Document.TimingPoints[1].Volume == 37
                && ui.View.Document.TimingPoints[1].TimeMs == 500 && ui.View.Document.TimingPoints[1].BeatLengthMs == baseline.TimingPoints[1].BeatLengthMs,
                "Apply changes the affecting line without moving it or adding a point");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Applying a draft is one undo step");
            ui.Key(53); Check(ui.View.Document.TimingPoints.Any(p => !p.Uninherited && p.TimeMs == 1000), "5 adds an inherited point at the playhead");
            var track = ui.Canvas.Lines.Single(l => l.X1 > 1100 && l.Width == 4 && l.Y1 == l.Y2);
            int original = ui.View.Document.TimingPoints.Single(p => p.TimeMs == 1000).Volume;
            ui.Click(track.X2 - 1, track.Y1);
            Check(ui.View.Document.TimingPoints.Single(p => p.TimeMs == 1000).Volume == original + 5, "One slider click adds five percentage points");
            ui.View.PointerDown(track.X1, track.Y1, 0, false, false);
            float exact = track.X1 + (track.X2 - track.X1) * .37f;
            ui.View.PointerMove(exact, track.Y1, false, false); ui.View.PointerUp(exact, track.Y1, 0); ui.Paint();
            Check(ui.View.Document.TimingPoints.Single(p => p.TimeMs == 1000).Volume == 37, "Dragging retains one-percent precision");

            ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
            ui.Key(50); ui.Key(68); ui.Key(49);
            var source = HitsoundingPanelTests.LaneBlocks(ui, 2)[0];
            ui.Click(source.X + source.Width / 2, source.Y + source.Height / 2);
            Check(ui.View.HitsoundingDisplayedBank == 2 && ui.View.HitsoundingBrush == 3, "Selection highlights its actual bank while retaining the drawing brush");
            ui.Key(67, ctrl: true);
            var target = HitsoundingPanelTests.LaneBlocks(ui, 1)[2];
            ui.Click(target.X + target.Width / 2, target.Y + target.Height / 2);
            double playhead = ui.View.PlayheadMs;
            ui.Key(86, ctrl: true);
            var fruit = ui.View.Document.Fruits.Single(f => f.TimeMs == 2000);
            Check(ui.View.Document.HitsoundOverrides.Any(h => h.SourceId == fruit.Id && (h.Sample.Additions & 2) != 0)
                && ui.View.PlayheadMs == playhead, "Pasting onto a selected target uses its time without seeking");
            ui.View.PointerDown(target.X + target.Width / 2, target.Y + target.Height / 2, 0, false, true);
            ui.View.PointerUp(target.X + target.Width / 2, target.Y + target.Height / 2, 0); ui.Paint();
            Check(ui.View.HitsoundingDisplayedBank == 0, "Mixed Normal and addition banks have no highlighted bank");
            ui.SetSnapDivisor(16);
            ui.View.UpdateTransport(1000, 8000, true, true, false, null, null); ui.Paint();
            ui.View.Wheel(source.X + 3, source.Y + 3, -120, false, false, false); ui.Paint();
            Check(ui.View.PlayheadMs == 1500, "Playing wheel uses a full beat even at 1/16 Snap");
        }
        RampTempoBoundary();
        RoundedNotes();
        Console.WriteLine("PASS hitsounding feedback shortcuts, draft apply, volume precision, ramp and selected-target clipboard");
    }
    private static void RoundedNotes()
    {
        var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        foreach (int time in new[] { 1000, 1166, 2000, 2167 })
            map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,{(time < 1500 ? 2 : 0)},2:2:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        var toggle = ui.View.HitsoundingLockBounds(0); ui.Click(toggle.X + 12, toggle.Y + 12);
        var lane = ui.View.HitsoundingNotesLaneBounds;
        float X(double time) => lane.X + (float)((time + 1200) / 4000 * lane.Width);
        float y = lane.Y + lane.Height / 2;
        ui.Click(X(1000), y);
        ui.View.PointerDown(X(1166), y, 0, false, true); ui.View.PointerUp(X(1166), y, 0); ui.Paint();
        ui.Key(67, ctrl: true);
        ui.Click(X(2000), y);
        var baseline = ui.View.Document.DeepClone();
        ui.Key(86, ctrl: true);
        var target = ui.View.Document.Fruits.Single(f => f.TimeMs == 2167);
        Check(ui.View.Document.HitsoundOverrides.Any(h => h.SourceId == target.Id && (h.Sample.Additions & 2) != 0),
            "Selected-target Notes paste tolerates one-millisecond rhythm quantization");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Matching Notes paste is one undo step");
    }
    private static void RampTempoBoundary()
    {
        var map = new MapDocument();
        map.TimingPoints.AddRange([
            new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 0, Uninherited = false, BeatLengthMs = -50, Volume = 80 },
            new() { TimeMs = 375, BeatLengthMs = 250, SampleSet = 3, SampleIndex = 7, Effects = 9 },
            new() { TimeMs = 1000, Uninherited = false, BeatLengthMs = -100, Volume = 20 }]);
        TimingEditing.GenerateVolumeRamp(map, map.TimingPoints[1], map.TimingPoints[3], 1);
        Check(map.TimingPoints.Any(p => p.TimeMs == 437 && !p.Uninherited && p.SampleIndex == 7 && p.Effects == 9),
            "Ramp preserves beat spacing across a mid-beat tempo change and inherits non-volume fields");
        Check(map.TimingPoints[1].Volume == 80 && map.TimingPoints[3].Volume == 20, "Decreasing ramps preserve endpoint volumes");
        int count = map.TimingPoints.Count;
        TimingEditing.GenerateVolumeRamp(map, map.TimingPoints[1], map.TimingPoints[3], 1);
        Check(map.TimingPoints.Count == count, "Regenerating a ramp does not duplicate green lines");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).ContentEquals(map), "Ramp survives project persistence");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
