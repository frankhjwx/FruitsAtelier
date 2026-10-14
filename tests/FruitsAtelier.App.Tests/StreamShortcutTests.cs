using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class StreamShortcutTests
{
    private sealed class ManualTime : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int ms) => ticks += ms;
    }

    public static void HoldAndShortcut()
    {
        ShiftF();
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        var map = new MapDocument(); var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 2000, X = 300 }]);
        map.Tracks.Add(track); ui.LoadDocument(map);
        ui.MoveMap(1500, 200); Check(ui.View.StreamConversionBounds.Width == 0, "hover never opens actions");
        ui.DownMap(1500, 200); clock.Advance(299); ui.Paint();
        Check(!ui.Canvas.Circles.Any(c => !c.Filled && c.Radius == 9 && c.Color == 0x282F3A), "first 300 ms have no progress circle");
        clock.Advance(351); ui.Paint();
        Check(ui.View.SliderHoldNeedsRedraw && ui.View.StreamConversionBounds.Width == 0
            && ui.Canvas.Circles.Any(c => !c.Filled && c.Radius == 9 && c.Color == 0x282F3A), "hold paints a small progress ring after the delay");
        ui.UpMap(1500, 200); clock.Advance(1000); ui.Paint();
        Check(!ui.View.SliderHoldNeedsRedraw && ui.View.StreamConversionBounds.Width == 0, "early release cancels hold");
        ui.HoldMap(1500, 200, () => clock.Advance(1000));
        Check(ui.View.StreamConversionBounds.Width > 0 && !ui.View.WantsCapture && !ui.View.IsDirty, "completed hold opens actions without editing");
        ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
        ui.SelectTrack(track.Id); ui.DownMap(1000, 100);
        ui.Key('F', ctrl: true, shift: true);
        Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0, "shortcut works during stationary control-point selection");
        ui.View.PointerUp(0, 0, 0); ui.Key(27);
        ui.Key('F', ctrl: true, shift: true);
        Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0, "shortcut works for selected controls after release");
        ui.Key(27);
    }

    private static void ShiftF()
    {
        foreach (bool imported in new[] { false, true })
        {
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
            Guid id = map.ImportedSliders.Single().Id;
            if (!imported) ImportedSliderEditing.ConvertToTrack(map, id);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('1'); ui.MoveMap(1000, 100);
            ui.Key('F', shift: true); ui.Paint();
            Check(ui.View.StreamConversionBounds.Width > 0
                && ui.View.SelectedObjectIds.Contains(id) && !ui.View.WantsCapture && ui.View.Document.ContentEquals(map),
                "Shift+F selects the slider and opens the action list without editing");
            ui.ClickText(L.Get("conversion.title"));
            Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0 && ui.View.Document.Tracks.Single().StreamSnapDivisor is not null,
                "Inspector conversion opens the live Stream/Stack dock");
        }
    }

    public static void Run()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        var ui = new Ui(); ui.LoadDocument(map);
        ui.HoldMap(1000, 100);
        Check(ui.View.LegacyConversionBounds.Width > 0 && ui.View.StreamConversionBounds.Width > 0, "legacy long press offers both conversions");
        Check(ui.View.StreamConversionBounds.Y >= ui.View.LegacyConversionBounds.Y, "Conversion actions use the compact grid");
        var streamButton = ui.View.StreamConversionBounds;
        ui.Click(streamButton.X + 20, streamButton.Y + 15);
        Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0 && ui.View.Document.Tracks.Single().StreamSnapDivisor is not null, "Inspector conversion opens the draft panel");
        ui.Key('Z', ctrl: true);
        Check(!ui.View.IsDirty, "Immediate conversion undoes");
        ui.Key('A', ctrl: true);
        ui.Key('F', ctrl: true, shift: true);
        var snap = ui.View.StreamSnapBounds;
        ui.View.PointerDown(snap.X + 7, snap.Y + 15, 0, false, false);
        Check(ui.View.WantsCapture, "snap slider captures pointer");
        ui.View.PointerMove(snap.Right - 31, snap.Y + 15, false, false);
        ui.View.PointerUp(snap.Right - 31, snap.Y + 15, 0); ui.Paint();
        Check(!ui.View.WantsCapture && ui.View.StreamSnapDivisor == 16, "snap slider reaches last supported division");
        ui.StreamSnap(12); Check(ui.View.StreamSnapDivisor == 12, "dock selects supported subdivisions");
        ui.StreamSnap(9); Check(ui.View.StreamSnapDivisor == 9, "dock selects 1/9");
        ui.StreamSnap(16);
        Check(ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Single().StreamSnapDivisor == 16, "confirmation converts imported slider in one edit");
        Guid id = ui.View.Document.Tracks.Single().Id;
        Check(ui.View.Conversion.Objects.All(o => o.Kind == CatchObjectKind.Fruit), "preview contains only fruit");
        for (int i = 0; i < 5; i++) ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Undo restores the imported source after each live snap change");
        for (int i = 0; i < 5; i++) ui.Key('Y', ctrl: true); Check(ui.View.Document.Tracks.Single().StreamSnapDivisor == 16, "redo restores stream");
        ui.Key('A', ctrl: true); ui.Key('K');
        Check(ui.View.Document.Tracks.Single().Nodes[0].TimeMs == 1125, "stream nudge moves its slider parent");
        ui.Key('H', ctrl: true); Check(ui.View.Document.Tracks.Single().Nodes[0].X == 412, "stream horizontal flip preserves geometry owner");
        ui.Key('D', ctrl: true); Check(ui.View.Document.Tracks.Count == 2 && ui.View.Document.Tracks.All(t => t.StreamSnapDivisor == 16), "clone retains stream settings");
        ui.Key('9', shift: true); Check(ui.View.SnapDivisor == 9, "legacy snap shortcut");
        foreach (int expected in new[] { 3, 4, 6, 8, 3 })
        {
            ui.Key('M', ctrl: true);
            Check(ui.View.SnapDivisor == expected, "Ctrl+M must cycle only the four quick Snap divisors");
        }
        ui.Key('A', ctrl: true); Check(ui.View.SelectedObjectIds.Count == 2, "select all includes stream parents");
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        ui.SelectTrack(id); ui.HoldMap(1000, 100);
        Check(ui.View.StreamConversionBounds.Width > 0 && ui.View.LegacyConversionBounds.Width == 0, "stream long press offers its actions");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("conversion.editTitle"))
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("stream.convertBack")), "stream has conversion and restore actions");
        ui.Key('F', ctrl: true, shift: true); ui.StreamSnap(12);
        Check(ui.View.Document.Tracks.Single().StreamSnapDivisor == 12, "change snapping updates existing stream");
        ui.HoldMap(1000, 100); ui.ClickObjectAction(L.Get("stream.convertBack"));
        Check(ui.View.Document.Tracks.Single().StreamSnapDivisor is null && ui.View.Conversion.Sliders.Count == 1,
            $"restore keeps slider geometry and preview: stream={ui.View.Document.Tracks.Single().StreamSnapDivisor}, sliders={ui.View.Conversion.Sliders.Count}, status={ui.View.StatusMessage}");
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        ui.SelectTrack(id);
        var before = ui.View.Document.DeepClone();
        ui.DownMap(1375, 175); ui.MoveMap(1500, 200); ui.UpMap(1500, 200);
        Check(!ui.View.Document.ContentEquals(before) && ui.View.Document.Tracks.Single().StreamSnapDivisor == 16, "stream can still be dragged as a slider");
    }
    public static void HoldInSelectionBox()
    {
        foreach (bool imported in new[] { false, true })
        foreach (var (u, v) in new[] { (.05f, .05f), (.95f, .05f), (.05f, .95f), (.95f, .95f), (0f, .5f), (1f, .5f) })
        {
            var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
            Guid id = map.ImportedSliders.Single().Id;
            if (!imported) ImportedSliderEditing.ConvertToTrack(map, id);
            ui.LoadDocument(map); ui.Key('1'); ui.Key('A', ctrl: true);
            var box = ui.View.SelectionTransformBounds;
            float x = box.X + box.Width * u, y = box.Y + box.Height * v;
            ui.View.PointerDown(x, y, 0, false, false); clock.Advance(1000); ui.Paint();
            ui.View.PointerUp(x, y, 0); ui.Paint();
            Check(ui.View.StreamConversionBounds.Width > 0
                && !ui.View.WantsCapture && ui.View.Document.ContentEquals(map), "Selection-box hold opens the action list without edits");
            Check((ui.View.LegacyConversionBounds.Width > 0) == imported, "Action list exposes the matching conversion");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
