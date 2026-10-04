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
        ShiftD();
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
        ui.Key(27); ui.Key('1');
        ui.DownMap(1500, 200); ui.MoveMap(1625, 210); clock.Advance(1000); ui.Paint();
        Check(!ui.View.SliderHoldNeedsRedraw && ui.View.StreamConversionBounds.Width == 0, "drag cancels the hold");
        ui.UpMap(1625, 210); ui.Key('Z', ctrl: true);
        ui.DownMap(1000, 100); ui.View.CancelInteraction(); clock.Advance(1000); ui.Paint();
        Check(!ui.View.SliderHoldNeedsRedraw && ui.View.StreamConversionBounds.Width == 0, "focus cancellation removes hold");
        ui.SelectTrack(track.Id); ui.DownMap(1000, 100);
        ui.Key('F', ctrl: true, shift: true);
        Check(ui.View.StreamDialogVisible, "shortcut works during stationary control-point selection");
        ui.View.PointerUp(0, 0, 0); ui.Key(27);
        ui.Key('F', ctrl: true, shift: true);
        Check(ui.View.StreamDialogVisible, "shortcut works for selected controls after release");
        ui.Key(27);
    }

    private static void ShiftD()
    {
        foreach (bool imported in new[] { false, true })
        {
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
            Guid id = map.ImportedSliders.Single().Id;
            if (!imported) ImportedSliderEditing.ConvertToTrack(map, id);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('1'); ui.MoveMap(1000, 100);
            ui.Key('D'); Check(ui.View.StreamConversionBounds.Width == 0, "plain D does not open slider actions");
            ui.Key('D', shift: true);
            Check(ui.View.StreamConversionBounds.Width > 0 && (ui.View.LegacyConversionBounds.Width > 0) == imported,
                "Shift+D immediately opens the matching slider actions");
            Check(ui.View.SelectedObjectIds.Contains(id) && !ui.View.WantsCapture && !ui.View.SliderHoldNeedsRedraw
                && !ui.View.IsDirty && ui.View.Document.ContentEquals(map), "shortcut selects without editing or capturing");
            ui.Key(27); Check(ui.View.StreamConversionBounds.Width == 0, "Escape dismisses shortcut actions");
            ui.Key('A', ctrl: true);
            var box = ui.View.SelectionTransformBounds;
            ui.View.PointerMove(box.X + box.Width * .05f, box.Y + box.Height * .95f, false, false);
            ui.Key('D', shift: true); Check(ui.View.StreamConversionBounds.Width > 0, "shortcut works throughout the selection box");
            ui.ClickText(L.Get("conversion.title"));
            Check(ui.View.StreamDialogVisible, "shortcut actions open the same conversion dialog");
            ui.Key('D', shift: true); Check(ui.View.StreamDialogVisible && ui.View.Document.ContentEquals(map), "dialog isolates Shift+D");
            ui.Key(27); ui.MoveMap(4000, 450); ui.Key('D', shift: true);
            Check(ui.View.StreamConversionBounds.Width == 0, "empty canvas does not open actions");
            ui.DownMap(1000, 100); ui.MoveMap(1125, 120); ui.Key('D', shift: true);
            Check(ui.View.StreamConversionBounds.Width == 0, "dragging does not open shortcut actions");
            ui.View.CancelInteraction(); ui.Paint();
            ui.MoveMap(1000, 100); ui.Key('L'); ui.Key('D', shift: true);
            Check(ui.View.StreamConversionBounds.Width == 0, "Lock Notes follows the long-press restriction");
        }
        var batch = new MapDocument();
        foreach (int start in new[] { 1000, 2500 })
        {
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = start, X = 100 }, new Anchor { TimeMs = start + 500, X = 300 }]);
            batch.Tracks.Add(track);
        }
        var multi = new Ui(); multi.LoadDocument(batch); multi.Key('1'); multi.Key('A', ctrl: true);
        multi.MoveMap(1000, 100); multi.Key('D', shift: true);
        Check(multi.View.StreamConversionBounds.Width > 0 && multi.View.SelectedObjectIds.Count == 2
            && multi.View.Document.ContentEquals(batch), "shortcut retains a batch selection without editing");
    }

    public static void Run()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        var ui = new Ui(); ui.LoadDocument(map);
        ui.HoldMap(1000, 100);
        Check(ui.View.LegacyConversionBounds.Width > 0 && ui.View.StreamConversionBounds.Width > 0, "legacy long press offers both conversions");
        Check(ui.View.StreamConversionBounds.Y > ui.View.LegacyConversionBounds.Bottom
            && ui.View.StreamConversionBounds.X == ui.View.LegacyConversionBounds.X, "conversion actions are stacked vertically");
        var streamButton = ui.View.StreamConversionBounds;
        ui.Click(streamButton.X + 20, streamButton.Y + 15);
        Check(ui.View.StreamDialogVisible && ui.View.Document.ContentEquals(map), "long-press conversion waits for confirmation");
        ui.Key(46); ui.Key('Z', ctrl: true); ui.Key(116);
        Check(ui.View.StreamDialogVisible && !ui.View.IsTestplaying && ui.View.Document.ContentEquals(map), "dialog isolates editing and testplay");
        ui.Key(27); Check(!ui.View.StreamDialogVisible && !ui.View.IsDirty, "cancel is a no-op");
        ui.Key('F', ctrl: true, shift: true);
        var snap = ui.View.StreamSnapBounds;
        ui.View.PointerDown(snap.X + 7, snap.Y + 15, 0, false, false);
        Check(ui.View.WantsCapture, "snap slider captures pointer");
        ui.View.PointerMove(snap.Right - 31, snap.Y + 15, false, false);
        ui.View.PointerUp(snap.Right - 31, snap.Y + 15, 0); ui.Paint();
        Check(!ui.View.WantsCapture && ui.View.StreamSnapDivisor == 16, "snap slider reaches last supported division");
        ui.Key(37); Check(ui.View.StreamSnapDivisor == 12, "keyboard skips unsupported subdivisions");
        ui.Key(37); Check(ui.View.StreamSnapDivisor == 9, "keyboard skips 1/11 and 1/10");
        ui.Key(39); ui.Key(39); ui.Key(13);
        Check(ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Single().StreamSnapDivisor == 16, "confirmation converts imported slider in one edit");
        Guid id = ui.View.Document.Tracks.Single().Id;
        Check(ui.View.Conversion.Objects.All(o => o.Kind == CatchObjectKind.Fruit), "preview contains only fruit");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "undo restores imported source");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.Tracks.Single().StreamSnapDivisor == 16, "redo restores stream");
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
        ui.ClickText(L.Get("conversion.editTitle")); ui.Key(37); ui.Key(13);
        Check(ui.View.Document.Tracks.Single().StreamSnapDivisor == 12, "change snapping updates existing stream");
        ui.HoldMap(1000, 100); ui.ClickText(L.Get("stream.convertBack"));
        Check(ui.View.Document.Tracks.Single().StreamSnapDivisor is null && ui.View.Conversion.Sliders.Count == 1, "restore keeps slider geometry and preview");
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        ui.SelectTrack(id);
        var before = ui.View.Document.DeepClone();
        ui.DownMap(1375, 175); ui.MoveMap(1500, 200); ui.UpMap(1500, 200);
        Check(!ui.View.Document.ContentEquals(before) && ui.View.Document.Tracks.Single().StreamSnapDivisor == 16, "stream can still be dragged as a slider");
    }
    public static void HoldInSelectionBox()
    {
        foreach (bool imported in new[] { false, true })
        {
            var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
            Guid id = map.ImportedSliders.Single().Id;
            if (!imported) ImportedSliderEditing.ConvertToTrack(map, id);
            ui.LoadDocument(map); ui.Key('1'); ui.Key('A', ctrl: true);
            var before = ui.View.Document.DeepClone();
            foreach (var (u, v) in new[] { (.05f, .05f), (.95f, .05f), (.05f, .95f), (.95f, .95f), (0f, .5f), (1f, .5f) })
            {
                var box = ui.View.SelectionTransformBounds;
                float x = box.X + box.Width * u, y = box.Y + box.Height * v;
                ui.View.PointerDown(x, y, 0, false, false); clock.Advance(1000); ui.Paint();
                ui.View.PointerUp(x, y, 0); ui.Paint();
                Check(ui.View.StreamConversionBounds.Width > 0 && !ui.View.WantsCapture,
                    $"box hold opens slider actions: imported={imported}, u={u}, v={v}, bounds={box}, actions={ui.View.StreamConversionBounds}, capture={ui.View.WantsCapture}, status={ui.View.StatusMessage}");
                Check((ui.View.LegacyConversionBounds.Width > 0) == imported, "box hold offers the selected slider's actions");
                Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "box hold does not edit content");
                ui.Key(27);
            }
            var bounds = ui.View.SelectionTransformBounds;
            float dragX = bounds.X + bounds.Width * .15f, dragY = bounds.Y + bounds.Height * .5f;
            ui.View.PointerDown(dragX, dragY, 0, false, false);
            ui.View.PointerMove(dragX + 20, dragY, false, false); clock.Advance(1000); ui.Paint();
            Check(!ui.View.SliderHoldNeedsRedraw && ui.View.StreamConversionBounds.Width == 0, "box drag cancels long press");
            ui.View.CancelInteraction(); ui.Paint();
            Check(before.ContentEquals(ui.View.Document), "cancelled box drag restores content");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
