using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingNavigationTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 20000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2 });
        map.TimingPoints.Add(new() { TimeMs = 3000, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, Volume = 65 });
        map.TimingPoints.Add(new() { TimeMs = 5000, BeatLengthMs = 400, SampleSet = 2 });
        foreach (int time in Enumerable.Range(0, 41).Select(i => 1000 + i * 125))
            map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,{(time is 1000 or 1500 ? 2 : 0)},0:0:0:0:" });
        OsuTimeline.AddBookmark(map, 2000); OsuTimeline.AddBookmark(map, 8000);
        return map;
    }
    private static Ui Create(bool lazer = false, Clock? clock = null)
    {
        var ui = new Ui(false, clock); ui.LoadDocument(Map()); ui.Resize(1440, 900);
        ui.View.LibrarySettings.LazerEditing = lazer; ui.View.ShowHitsoundingPage();
        ui.View.UpdateTransport(2000, 20000, true, false, false, null, null); ui.Paint();
        return ui;
    }
    private static void Press(Ui ui, int key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        ui.View.SetModifiers(alt, shift); ui.Key(key, ctrl, shift); ui.View.KeyUp(key); ui.View.SetModifiers(false, false);
    }
    private static float X(Ui ui, double time) => ui.View.HitsoundingNotesLaneBounds.X
        + (float)((time - (ui.View.PlayheadMs - 1200)) / 4000 * ui.View.HitsoundingNotesLaneBounds.Width);
    private static void Unlock(Ui ui)
    { var box = ui.View.HitsoundingLockBounds(0); ui.Click(box.X + box.Width / 2, box.Y + 12); }
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            Navigation(); PanAndZoom(); NotesDrag(); SoundDragAndNudge(); EdgeScroll(); TimingDrag();
        }
        L.SetLanguage("en");
        Console.WriteLine("PASS Hitsounding pan, bounded zoom, navigation parity, held edits, edge speed and group nudges");
    }
    private static void Navigation()
    {
        foreach (bool lazer in new[] { false, true })
        foreach (bool playing in new[] { false, true })
        {
            var hs = Create(lazer); var mapping = new Ui(false); mapping.LoadDocument(Map()); mapping.View.LibrarySettings.LazerEditing = lazer;
            var baseline = hs.View.Document.DeepClone(); var conversion = hs.View.Conversion;
            foreach (int key in new[] { 37, 39, 38, 40, 36, 35, 90, 86, 88 })
            {
                hs.View.UpdateTransport(4000, 20000, true, playing, false, null, null);
                mapping.View.UpdateTransport(4000, 20000, true, playing, false, null, null);
                Press(hs, key); Press(mapping, key);
                Check(hs.View.PlayheadMs == mapping.View.PlayheadMs, $"Navigation key {key} matches Mapping, lazer={lazer}, playing={playing}");
            }
            foreach (bool shift in new[] { false, true })
            {
                hs.View.UpdateTransport(4000, 20000, true, playing, false, null, null); hs.Paint();
                mapping.View.UpdateTransport(4000, 20000, true, playing, false, null, null); mapping.Paint();
                var lane = hs.View.HitsoundingNotesLaneBounds;
                hs.View.Wheel(lane.X + 50, lane.Y + 10, -120, false, shift, false);
                mapping.View.Wheel(mapping.View.ObjectTimelineBounds.X + 30, mapping.View.ObjectTimelineBounds.Y + 10, -120, false, shift, false);
                Check(hs.View.PlayheadMs == mapping.View.PlayheadMs, "Wheel navigation matches Mapping timeline");
            }
            hs.View.UpdateTransport(4000, 20000, true, false, false, null, null); hs.Paint();
            Press(hs, 37, ctrl: !lazer, alt: lazer); Check(hs.View.PlayheadMs == 2000, "Previous bookmark shortcut");
            Press(hs, 39, ctrl: !lazer, alt: lazer); Check(hs.View.PlayheadMs == 8000, "Next bookmark shortcut");
            var r = hs.View.HitsoundingNotesLaneBounds;
            hs.View.Wheel(r.X + 50, r.Y + 10, 120, true, false, false); hs.Paint();
            double previous = hs.View.PlayheadMs;
            Press(hs, 39); Check(Math.Abs(hs.View.PlayheadMs - previous - 50) < .001, "Ctrl-wheel changes divisor without seeking");
            var command = EditorView.ShortcutCommands.Single(c => c.Scope == EditorView.ShortcutScope.Hitsounding && c.Id == "start" && c.Lazer == lazer);
            hs.View.LibrarySettings.ShortcutBindings[command.StorageKey] = [ShortcutBinding.From(85)];
            Press(hs, 85); Check(hs.View.PlayheadMs == 0, "Navigation command can be rebound");
            Check(baseline.ContentEquals(hs.View.Document) && ReferenceEquals(conversion, hs.View.Conversion), "Navigation preserves content and conversion");
        }
    }
    private static void PanAndZoom()
    {
        var ui = Create(); var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        var lane = ui.View.HitsoundingNotesLaneBounds; float x = lane.X + lane.Width / 2, y = lane.Y + 10;
        ui.View.PointerDown(x, y, 1, false, false);
        Check(ui.View.WantsCapture, "Middle drag captures even over locked Notes");
        ui.View.PointerMove(x - lane.Width / 4, y, false, false); ui.Paint();
        Check(Math.Abs(ui.View.PlayheadMs - 3000) < .01, "Middle drag pans one second without editing");
        ui.View.PointerUp(x - lane.Width / 4, y, 1); Check(!ui.View.WantsCapture, "Middle release clears capture");
        for (int i = 0; i < 50; i++) ui.View.Wheel(x, y, -120, false, false, true);
        ui.Paint();
        double before = ui.View.PlayheadMs;
        ui.View.PointerDown(x, y, 1, false, false); ui.View.PointerMove(x - lane.Width / 10, y, false, false);
        Check(Math.Abs(ui.View.PlayheadMs - before - 1000) < .01, "Zoom-out is bounded at a ten-second span");
        Press(ui, 27); Check(!ui.View.WantsCapture, "Escape ends panning");
        Check(baseline.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Pan and zoom reuse committed content");
    }
    private static void NotesDrag()
    {
        var ui = Create(); Unlock(ui); var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        float y = ui.View.HitsoundingNotesLaneBounds.Y + 12;
        ui.View.PointerDown(X(ui, 1000), y, 0, false, false);
        ui.View.PointerMove(X(ui, 1125), y, false, false); ui.Paint();
        Press(ui, 39);
        Check(baseline.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Held keyboard navigation only updates ghost");
        ui.View.PointerUp(X(ui, 1250), y, 0); ui.Paint();
        Check(ui.View.Document.Fruits[0].TimeMs == 1250, "Stationary-pointer seek advances the held note");
        Press(ui, 90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Seek and drag commit is one undo");
        ui.View.UpdateTransport(2000, 20000, true, false, false, null, null); ui.Paint();
        foreach (double time in new[] { 1000d, 1500d })
        { ui.View.PointerDown(X(ui, time), y, 0, false, true); ui.View.PointerUp(X(ui, time), y, 0); ui.Paint(); }
        var selected = ui.View.SelectedObjectIds.ToDictionary(id => id, id => ui.View.Document.Fruits.Single(f => f.Id == id).TimeMs);
        Press(ui, 75);
        Check(selected.Count == 2 && selected.All(p => ui.View.Document.Fruits.Single(f => f.Id == p.Key).TimeMs == p.Value + 125), "K nudges multiple Notes together");
        Press(ui, 74); Check(baseline.ContentEquals(ui.View.Document), "J moves selected Notes back");
    }
    private static void SoundDragAndNudge()
    {
        var ui = Create(); var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        var blocks = HitsoundingPanelTests.LaneBlocks(ui, 2);
        foreach (var r in blocks)
        { ui.View.PointerDown(r.X + r.Width / 2, r.Y + 10, 0, false, true); ui.View.PointerUp(r.X + r.Width / 2, r.Y + 10, 0); ui.Paint(); }
        Press(ui, 75);
        Check(AdditionTimes().SequenceEqual(new[] { 1125d, 1625d }), "K transfers selected additions together");
        Press(ui, 74); Check(AdditionTimes().SequenceEqual(new[] { 1000d, 1500d }), "J transfers selected additions back");
        Press(ui, 90, ctrl: true); Press(ui, 90, ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Addition nudges support undo");
        blocks = HitsoundingPanelTests.LaneBlocks(ui, 2); var first = blocks[0]; float x = first.X + first.Width / 2, y = first.Y + 10;
        ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(x + 20, y, false, false); ui.Paint();
        double before = ui.View.PlayheadMs; Press(ui, 39, shift: true);
        Check(ui.View.PlayheadMs == before + 500 && baseline.ContentEquals(ui.View.Document), "Coarse seek works during sound drag");
        Press(ui, 27); Check(baseline.ContentEquals(ui.View.Document), "Cancel after navigation preserves source sounds");
        double[] AdditionTimes() => ui.View.Conversion.Objects.Where(n => new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false).Describe(n).Additions == 2).Select(n => n.TimeMs).Order().ToArray();
    }
    private static void EdgeScroll()
    {
        var clock = new Clock(); var ui = Create(clock: clock); Unlock(ui);
        var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        var lane = ui.View.HitsoundingNotesLaneBounds; float y = lane.Y + 12;
        ui.View.PointerDown(X(ui, 1000), y, 0, false, false);
        ui.View.PointerMove(lane.Right, y, false, false); ui.Paint();
        Check(ui.View.TimelineDragScrollNeedsRedraw, "Held Notes request edge-scroll frames");
        double before = ui.View.PlayheadMs;
        clock.Now += 50; ui.Paint();
        Check(Math.Abs(ui.View.PlayheadMs - before - 600 * .05 * 4000 / lane.Width) < .01, "Edge speed matches Mapping's 600 pixels per second");
        Check(baseline.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Edge scrolling only changes ghost and view");
        Press(ui, 27); Check(!ui.View.TimelineDragScrollNeedsRedraw, "Cancel stops automatic scrolling");
        ui.View.UpdateTransport(2000, 20000, true, false, false, null, null); ui.Paint();
        ui.View.PointerDown(X(ui, 850), y, 0, false, false); ui.View.PointerMove(lane.Right, y, false, false); ui.Paint();
        for (int i = 0; i < 20; i++) { clock.Now += 50; ui.Paint(); }
        ui.View.PointerUp(lane.Right, y, 0); ui.Paint();
        Check(ui.View.SelectedObjectIds.Contains(ui.View.Document.Fruits[0].Id), "Notes marquee retains objects scrolled offscreen");
        Check(!ui.View.TimelineDragScrollNeedsRedraw && baseline.ContentEquals(ui.View.Document), "Marquee release stops scrolling without edits");
    }
    private static void TimingDrag()
    {
        var clock = new Clock(); var ui = Create(clock: clock);
        var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        var point = ui.View.Document.TimingPoints.Single(p => !p.Uninherited);
        var box = ui.View.HitsoundingTimingHandleBounds(point);
        float x = box.X + 4, y = box.Y + 10;
        ui.View.PointerDown(x, y, 0, false, false);
        Press(ui, 39);
        Check(baseline.ContentEquals(ui.View.Document), "Timing navigation updates a preview only");
        ui.View.PointerUp(x, y, 0); ui.Paint();
        Check(ui.View.Document.TimingPoints.Single(p => !p.Uninherited).TimeMs == 3125, "Stationary timing drag follows navigation");
        Press(ui, 90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Timing navigation drag is one undo");
        point = ui.View.Document.TimingPoints.Single(p => !p.Uninherited);
        box = ui.View.HitsoundingTimingHandleBounds(point);
        ui.View.PointerDown(box.X + 4, box.Y + 10, 0, false, false);
        ui.View.PointerMove(ui.View.HitsoundingNotesLaneBounds.Right, box.Y + 10, false, false); ui.Paint();
        Check(ui.View.TimelineDragScrollNeedsRedraw, "Timing strip supports edge scrolling");
        clock.Now += 50; ui.Paint(); Press(ui, 27);
        Check(baseline.ContentEquals(ui.View.Document), "Timing edge scroll cancels without content edits");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
