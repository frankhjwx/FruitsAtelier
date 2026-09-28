using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class MappingSessionTests
{
    public static void RightClickSelection()
    {
        var map = Map();
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 },
            new() { TimeMs = 4000, X = 400 }]);
        map.Tracks.Add(new CurveTrack { Nodes = { new() { TimeMs = 2000, X = 220 }, new() { TimeMs = 2500, X = 300 } } });
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('1');
        var baseline = ui.View.Document.DeepClone();
        Box();
        Check(ui.View.SelectedObjectIds.Count == 3, "Marquee did not select the three parent objects.");
        RightClick(1000, 100);
        Check(ui.View.Document.Fruits.Count == 1 && ui.View.Document.Fruits[0].TimeMs == 4000
            && ui.View.Document.Tracks.Count == 0, "Right-click deleted only the hit object from the selected group.");
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Group deletion did not restore all objects in one undo.");
        ui.ClickMap(1000, 100); ui.ClickMap(1500, 200, ctrl: true); ui.ClickMap(2000, 220, ctrl: true);
        Check(ui.View.SelectedObjectIds.Count == 3, "Ctrl-click did not select fruit and FSlider parents.");
        RightClick(2000, 220);
        Check(ui.View.Document.Fruits.Count == 1 && ui.View.Document.Tracks.Count == 0,
            "Right-click on the selected FSlider did not delete the Ctrl-click selection.");
        ui.Key('Z', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Ctrl-click selection deletion did not undo together.");
        Box(); RightClick(4000, 400);
        Check(ui.View.Document.Fruits.Count == 2 && ui.View.Document.Tracks.Count == 1,
            "Right-click on an unselected object deleted the existing selection.");
        void Box() { ui.DownMap(750, 50); ui.MoveMap(2750, 350); ui.UpMap(2750, 350); }
        void RightClick(double time, double x)
        {
            var p = ui.ScreenAt(time, x);
            ui.View.PointerDown(p.X, p.Y, 2, false, false); ui.View.PointerUp(p.X, p.Y, 2); ui.Paint();
        }
    }

    public static void ReverseAndNudge()
    {
        var map = Map();
        map.Fruits.Add(new() { TimeMs = 1000, X = 50 });
        var track = new CurveTrack { Nodes = { new() { TimeMs = 1500, X = 100 }, new() { TimeMs = 2000, X = 300 } }, SpanCount = 2 };
        map.Tracks.Add(track);
        map.BananaShowers.Add(new() { TimeMs = 3000, EndTimeMs = 3500 });
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('A', ctrl: true);
        var baseline = ui.View.Document.DeepClone();
        ui.Key('G', ctrl: true);
        Near(3500, ui.View.Document.Fruits.Single().TimeMs);
        var reversed = ui.View.Document.Tracks.Single();
        Near(2000, reversed.Nodes[0].TimeMs); Near(300, reversed.Nodes[0].X);
        Near(2500, reversed.Nodes[^1].TimeMs); Near(100, reversed.Nodes[^1].X);
        Check(reversed.SpanCount == 2, "Reverse changed repeat count.");
        Near(1000, ui.View.Document.BananaShowers.Single().TimeMs);
        Near(1500, ui.View.Document.BananaShowers.Single().EndTimeMs);
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Group reverse was not one undo.");
        ui.SelectTrack(track.Id); ui.Key('G', ctrl: true);
        reversed = ui.View.Document.Tracks.Single();
        Near(1500, reversed.Nodes[0].TimeMs); Near(300, reversed.Nodes[0].X);
        Near(2500, CurveMath.EndTimeMs(reversed));
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Single reverse was not undoable.");
        ui.Key('A', ctrl: true); ui.Key('L'); ui.Key('G', ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Locked notes reversed."); ui.Key('L');
        ui.Key(39, ctrl: true); Near(51, ui.View.Document.Fruits.Single().X);
        ui.Key('Z', ctrl: true); ui.Key('A', ctrl: true);
        ui.Key('3', ctrl: true); ui.Key(37, ctrl: true, shift: true);
        Near(34, ui.View.Document.Fruits.Single().X);
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Nudge did not undo.");

        ui = new Ui(); ui.LoadDocument(OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n50,192,3000,1,0,0:0:0:0:"));
        baseline = ui.View.Document.DeepClone(); ui.Key('A', ctrl: true); ui.Key('G', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Count == 1, "Imported slider was not reversed as an editable path.");
        Near(1000, ui.View.Document.Fruits.Single().TimeMs);
        Check(ui.View.Document.Tracks.Single().Nodes[0].X > 250, "Imported slider body did not reverse.");
        OsuBeatmapWriter.Serialize(ui.View.Document);
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Imported reverse did not restore source context.");
    }

    public static void SnapAndVolume()
    {
        var ui = new Ui(); ui.LoadDocument(Map());
        var plot = ui.View.CanvasPlotBounds; var timeline = ui.View.ObjectTimelineBounds;
        foreach (var point in new[] { (plot.X + 50, plot.Y + 100), (timeline.X + 50, timeline.Y + 20), (320f, ui.Height - 57) })
        {
            ui.SetSnapDivisor(4);
            foreach (int expected in new[] { 8, 16, 16 }) { ui.View.Wheel(point.Item1, point.Item2, 120, true); Check(ui.View.SnapDivisor == expected, "Binary Snap progression failed."); }
            ui.SetSnapDivisor(3);
            foreach (int expected in new[] { 6, 12, 12 }) { ui.View.Wheel(point.Item1, point.Item2, 120, true); Check(ui.View.SnapDivisor == expected, "Triplet Snap progression failed."); }
            foreach (int expected in new[] { 6, 3, 3 }) { ui.View.Wheel(point.Item1, point.Item2, -120, true); Check(ui.View.SnapDivisor == expected, "Triplet Snap lower boundary failed."); }
            foreach (int odd in new[] { 5, 7, 9 }) { ui.SetSnapDivisor(odd); ui.View.Wheel(point.Item1, point.Item2, 120, true); Check(ui.View.SnapDivisor == odd, "Unsupported doubling changed Snap family."); }
            ui.SetSnapDivisor(4); ui.View.Wheel(point.Item1, point.Item2, 60, true); Check(ui.View.SnapDivisor == 4, "Partial wheel changed Snap.");
            ui.View.Wheel(point.Item1, point.Item2, 60, true); Check(ui.View.SnapDivisor == 8, "Partial wheel did not accumulate.");
        }
        var baseline = ui.View.Document.DeepClone();
        ui.View.LibrarySettings.MasterVolume = ui.View.LibrarySettings.SongVolume = ui.View.LibrarySettings.HitsoundVolume = 50;
        ui.View.AdjustVolumeShortcut(39, true); ui.View.ReleaseVolumeShortcut(39);
        ui.View.Wheel(320, ui.Height - 57, 120, false, alt: true);
        Check(ui.View.LibrarySettings.SongVolume == 55 && ui.View.LibrarySettings.MasterVolume == 50 && ui.View.LibrarySettings.HitsoundVolume == 50, "Overview wheel did not adjust the selected Music channel.");
        Check(ui.View.VolumePopoverVisible && baseline.ContentEquals(ui.View.Document), "Volume failed to show or edited content.");
    }

    public static void DistanceAndHover()
    {
        var map = Map();
        map.Fruits.AddRange([new() { TimeMs = 500, X = 100 }, new() { TimeMs = 1000, X = 150 }, new() { TimeMs = 1500, X = 350 }, new() { TimeMs = 2500, X = 450 }]);
        var ui = new Ui(); ui.LoadDocument(map);
        ui.ClickMap(1000, 150); ui.ClickMap(1500, 350, ctrl: true);
        Check(ui.View.MovementOverlayBounds is not null, "Multi-selection distance panel missing.");
        double expected = 50 / map.DistancePerBeat;
        Near(expected, ui.View.DistanceReadout.Previous!.Value); Near(expected, ui.View.DistanceReadout.Next!.Value);
        Check(ui.View.EqualDistanceHighlighted, "Equal DS across unequal time intervals did not highlight.");
        Check(ui.View.PreviousDistanceFieldBounds is null, "Multi-selection exposed single-object editing.");
        var baseline = ui.View.Document.DeepClone();
        ui.View.Document.Fruits[^1].X += map.DistancePerBeat * 2 * .019; ui.Paint();
        Check(ui.View.EqualDistanceHighlighted, "DS tolerance rejected .019.");
        ui.View.Document.Fruits[^1].X += map.DistancePerBeat * 2 * .002; ui.Paint();
        Check(!ui.View.EqualDistanceHighlighted, "DS tolerance accepted .021.");
        ui.LoadDocument(baseline); ui.Key('F'); ui.MoveMap(1250, 250);
        Check(ui.View.MovementOverlayBounds is not null, "Placement panel missing.");
        ui.View.PointerMove(0, 0, false, false); ui.Paint();
        Check(ui.View.MovementOverlayBounds is null, "Outside-canvas placement panel remains.");
        ui.MoveMap(1250, 250); ui.View.PointerLeave(); ui.Paint();
        Check(ui.View.MovementOverlayBounds is null && !ui.Canvas.Lines.Any(l => Math.Abs(l.X1 - ui.View.PlayfieldBounds.X) < .001 && Math.Abs(l.X2 - ui.View.PlayfieldBounds.Right) < .001 && Math.Abs(l.Opacity - .4f) < .001), "Window exit retained placement UI.");
        Check(baseline.ContentEquals(ui.View.Document), "Distance display edited the document.");
    }

    public static void MarqueeAndBanana()
    {
        var ui = new Ui(); ui.LoadDocument(Map());
        foreach (bool timeline in new[] { false, true })
        {
            ui.View.UpdateTransport(0, 10000, true, false, false, null, null);
            ui.View.RequestTogglePlayback = () => ui.View.UpdateTransport(ui.View.PlayheadMs, 10000, true, !ui.View.AudioPlaying, false, null, null);
            var r = timeline ? ui.View.ObjectTimelineBounds : ui.View.CanvasPlotBounds;
            float x = r.X + 60, y = r.Y + 25;
            ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(x + 70, y + 20, false, false);
            ui.Key(32); Check(ui.View.AudioPlaying && ui.View.WantsCapture, "Marquee blocked playback or lost capture.");
            ui.Key('C'); Check(!ui.View.AudioPlaying && ui.View.WantsCapture, "Marquee blocked pause.");
            ui.View.PointerUp(x + 70, y + 20, 0); ui.Paint();
        }
        var map = Map(); map.BananaShowers.Add(new() { TimeMs = 500, EndTimeMs = 1000 });
        ui.LoadDocument(map); ui.Key('N'); ui.MoveMap(1250, 256);
        float targetY = ui.ScreenAt(1250, 256).Y;
        Check(ui.Canvas.Lines.Any(l => Math.Abs(l.Y1 - targetY) < .1 && Math.Abs(l.Opacity - .7f) < .001), "Banana placement snap line missing.");
        ui.Key('1'); ui.View.UpdateTransport(750, 10000, false, false, false, null, null); ui.Paint();
        var bounds = ui.View.ObjectTimelineBounds;
        float endX = bounds.X + (float)((1000 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs), endY = bounds.Y + 27;
        var baseline = ui.View.Document.DeepClone();
        ui.View.PointerDown(endX, endY, 0, false, false);
        ui.View.PointerMove(endX + (float)(500 * ui.View.ObjectTimelinePixelsPerMs), endY, false, false);
        ui.View.PointerUp(endX + (float)(500 * ui.View.ObjectTimelinePixelsPerMs), endY, 0); ui.Paint();
        Near(1500, ui.View.Document.BananaShowers.Single().EndTimeMs);
        Near(500, ui.View.Document.BananaShowers.Single().TimeMs);
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Banana tail resize did not undo.");
    }

    public static void Preferences()
    {
        string folder = Path.GetFullPath("artifacts/tests/mapping-session");
        string path = Path.Combine(folder, "settings.json");
        var ui = new Ui(overview: false);
        ui.View.InitializeLibrary(false, new LibrarySettings { Workspace = folder }); ui.View.LoadDocument(Map()); ui.Paint();
        ui.View.RequestViewPreference = () => ui.View.LibrarySettings.Save(path);
        var baseline = ui.View.Document.DeepClone();
        var p = ui.View.CanvasPlotBounds; var t = ui.View.ObjectTimelineBounds;
        ui.View.Wheel(p.X + 80, p.Y + 80, 120, false, alt: true); ui.Paint();
        ui.View.Wheel(t.X + 80, t.Y + 20, 120, false, alt: true);
        ui.Key(114); var w = ui.View.WaveformBounds;
        ui.View.Wheel(w.X + 80, w.Y + 20, 120, false, alt: true);
        var saved = LibrarySettings.Load(path);
        Near(.696, saved.CanvasZoom); Near(.225, saved.ObjectTimelineScale); Near(8000, saved.WaveformSpanMs);
        ui.Key(112); ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
        ui.ClickText(L.Get("testplay.comboOn"));
        Check(ui.View.LibrarySettings.ShowTestplayCombo, "Combo draft applied before Apply.");
        ui.Key(27); ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("testplay.comboOn")), "Cancelled combo setting was retained.");
        ui.ClickText(L.Get("testplay.comboOn")); ui.View.ApplySettings(path);
        saved = LibrarySettings.Load(path);
        Check(!saved.ShowTestplayCombo, "Combo setting was not persisted.");
        Near(.696, saved.CanvasZoom); Near(.225, saved.ObjectTimelineScale); Near(8000, saved.WaveformSpanMs);
        var reopened = new Ui(overview: false); reopened.View.InitializeLibrary(false, saved); reopened.View.LoadDocument(Map()); reopened.Paint();
        Near(.696, reopened.View.CanvasZoom); Near(.225, reopened.View.ObjectTimelinePixelsPerMs);
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Preferences edited map content.");
        Check(new LibrarySettings().ShowTestplayCombo, "Old settings no longer default to visible Combo.");
    }

    public static void TestplayDisplay()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock); var map = Map();
        map.Fruits.AddRange([new() { TimeMs = 100, X = 256 }, new() { TimeMs = 5000, X = 256 }]);
        ui.LoadDocument(map); ui.Key(36); ui.View.LibrarySettings.TestplayDashKey = 'D';
        ui.View.StartTestplay(); clock.Advance(120); ui.Paint();
        Check(ui.View.TestplayCombo == 1, "Combo fixture did not catch.");
        clock.Advance(300); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "1"), "Visible Combo counter missing.");
        ui.View.LibrarySettings.ShowTestplayCombo = false; ui.Paint();
        Check(!ui.Canvas.Texts.Any(t => t.Value == "1") && ui.View.TestplayCombo == 1, "Hidden Combo affected gameplay or remained drawn.");
        ui.Key('D');
        Check(ui.Canvas.Circles.Any(c => c.Color == 0xFFFFFF && Math.Abs(c.Opacity - .65f) < .001), "Configured dash key did not brighten catcher.");
        ui.View.KeyUp('D'); ui.Paint();
        Check(!ui.Canvas.Circles.Any(c => c.Color == 0xFFFFFF && Math.Abs(c.Opacity - .65f) < .001), "Released dash retained white effect.");
        ui.View.StopTestplay();
        var objects = CatchStreamConverter.Convert(map).Objects;
        var session = new CatchTestplaySession(new CatchTestplay(objects, 5, 0), new(0, 1, clock.GetTimestamp(), false),
            0, false, false, 37, 39, 'D', clock, 5, []);
        session.SetKey('D', true); var frame = session.Capture();
        Check(frame.Dashing, "Gameplay snapshot omitted held dash.");
        session.ReleaseKeys(); Check(!session.Capture().Dashing && frame.Dashing, "Dash snapshot changed after capture or focus release failed.");
    }

    private sealed class ManualTime : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(long milliseconds) => ticks += milliseconds;
    }

    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        return map;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}, got {actual}."); }
}
