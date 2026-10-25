using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class BananaEditTests
{
    internal static void Run()
    {
        CanvasBoundaries();
        TimelineBoundaries();
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 10000 };
        map.BananaShowers.Add(new() { TimeMs = 100, EndTimeMs = 600 });
        var shower = new BananaShower { TimeMs = 1000, EndTimeMs = 3000 };
        map.BananaShowers.Add(shower);
        ui.LoadDocument(map); ui.Key('1'); ui.ClickMap(1500, 256); ui.Key('F', ctrl: true);
        Check(ui.View.BananaEditVisible && !ui.View.IsDirty, "Ctrl+F opens a content-free draft");
        Wait();
        var candidate = map.DeepClone(); candidate.BananaShowers[1].Pattern = new();
        var expected = OsuBeatmapWriter.Serialize(candidate).PlayableObjects.Where(o => o.SourceId == shower.Id).ToArray();
        Check(ui.View.BananaEditPreview.SequenceEqual(expected), "Preview includes preceding banana RNG");
        var preview = ui.View.BananaEditPreview;
        var bounds = ui.View.BananaPreviewBounds;
        double scale = ui.View.BananaPreviewScale, visible = ui.View.BananaPreviewVisibleStart;
        float pointerY = bounds.Y + bounds.Height / 2;
        double time = visible + (bounds.Bottom - 16 - pointerY) / scale;
        ui.View.Wheel(bounds.X + 20, pointerY, 120, false, false, true); ui.Paint();
        Check(ui.View.BananaPreviewScale > scale && Math.Abs(time - ui.View.BananaPreviewVisibleStart
            - (bounds.Bottom - 16 - pointerY) / ui.View.BananaPreviewScale) < .001, "Banana zoom preserves pointer time");
        ui.View.Wheel(bounds.X + 20, pointerY, -120, false, false, true); ui.Paint();
        Check(Math.Abs(ui.View.BananaPreviewScale - scale) < .0001 && Math.Abs(ui.View.BananaPreviewVisibleStart - visible) < .001, "Banana zoom round trip");
        Check(ReferenceEquals(preview, ui.View.BananaEditPreview) && !ui.View.IsDirty, "Zoom reuses preview without content edits");
        for (int i = 0; i < 20; i++) ui.Paint();
        Check(ReferenceEquals(preview, ui.View.BananaEditPreview), "Paint reuses the completed preview");
        ui.Click(20, 20); Check(ui.View.BananaEditVisible && ui.View.Document.ContentEquals(map), "Modal isolates canvas input");
        SetNumber(0, "0"); ui.Key(13);
        Check(ui.View.BananaEditVisible && ui.View.Document.ContentEquals(map), "Invalid duration cannot apply");
        SetNumber(0, "2"); ui.Key(13); Wait();
        ui.Key(27); Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Cancel discards preview");
        ui.Key('F', ctrl: true); Wait(); ui.ClickText(L.Get("stream.confirm"));
        Check(!ui.View.BananaEditVisible && ui.View.Document.BananaShowers[1].Pattern == new BananaShowerPattern(), "Apply group");
        Check(ui.View.SelectedObjectIds.SequenceEqual(new[] { shower.Id }), "One selected parent");
        var grouped = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Undo group");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(grouped), "Redo group");
        ui.ClickMap(1600, 256);
        ui.DownMap(1000, 256); ui.MoveMap(1250, 256); ui.UpMap(1250, 256);
        Check(Math.Abs(ui.View.Document.BananaShowers[1].TimeMs - 1250) < 1 && ui.View.Document.BananaShowers[1].Pattern == new BananaShowerPattern(), "Resize group head");
        ui.DownMap(3000, 256); ui.MoveMap(3250, 256); ui.UpMap(3250, 256);
        Check(Math.Abs(ui.View.Document.BananaShowers[1].EndTimeMs - 3250) < 1, "Resize group tail");
        ui.Key('F', ctrl: true); Wait();
        expected = OsuBeatmapWriter.Serialize(ui.View.Document).PlayableObjects.Where(o => o.SourceId == shower.Id).ToArray();
        Check(ui.View.BananaEditPreview.SequenceEqual(expected), "Reopened preview uses resized group");
        ui.Key(27); ui.Key('C', ctrl: true); ui.Key('V', ctrl: true);
        Check(ui.View.Document.BananaShowers.Count == 3 && ui.View.Document.BananaShowers[^1].Pattern == new BananaShowerPattern(), "Clipboard preserves grouped pattern");
        ui.Key('Z', ctrl: true); ui.ClickMap(1500, 256); ui.Key(46);
        Check(ui.View.Document.BananaShowers.Count == 1, "Delete whole group");
        ui.Key('Z', ctrl: true); ui.ClickMap(1500, 256); ui.Key('F', ctrl: true); Wait();
        var beforeSplit = ui.View.Document.DeepClone();
        int count = BananaShowerPattern.Segments(beforeSplit.BananaShowers[1], new(beforeSplit)).Count;
        ui.ClickText(L.Get("bananaEdit.split")); ui.ClickText(L.Get("stream.confirm"));
        Check(ui.View.SelectedObjectIds.Count == count && ui.View.Document.BananaShowers.Count == count + 1
            && ui.View.Document.BananaShowers.All(s => s.Pattern is null), "Split checkbox creates selected independent showers");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(beforeSplit), "Split undoes in one step");
        StreamZoom();
        var shortMap = new MapDocument(); shortMap.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 1001.99 });
        ui.LoadDocument(shortMap); ui.Key('A', ctrl: true); ui.Key('F', ctrl: true);
        Check(ui.View.BananaPreviewReady && ui.View.BananaEditPreview.Count == 0
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("bananaEdit.tooShort")), "Sub-2ms-only range has no generated preview");
        ui.Key(13); Check(ui.View.BananaEditVisible && ui.View.Document.ContentEquals(shortMap), "Empty generation cannot apply");
        ui.Key(27);

        void Wait()
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!ui.View.BananaPreviewReady && DateTime.UtcNow < deadline) { Thread.Sleep(5); ui.Paint(); }
            Check(ui.View.BananaPreviewReady && ui.View.BananaEditPreview.Count > 0, "Preview completes");
        }
        void SetNumber(int index, string value)
        {
            var bounds = ui.View.BananaNumeratorBounds[index]; ui.Click(bounds.X + 10, bounds.Y + 10);
            ui.Key('A', ctrl: true); ui.Type(value);
        }
    }
    private static void CanvasBoundaries()
    {
        foreach (bool grouped in new[] { false, true })
        {
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
            map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 3000,
                Pattern = grouped ? new BananaShowerPattern() : null });
            ui.LoadDocument(map); ui.Key('1');
            var baseline = ui.View.Document.DeepClone();
            Drag(1000, 1250, 100);
            Check(Current().TimeMs == 1250 && Current().EndTimeMs == 3000, "Canvas begin boundary selects and resizes");
            var resized = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(baseline), "Canvas begin undo");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(resized), "Canvas begin redo");
            Drag(3000, 3250, 400);
            Check(Current().TimeMs == 1250 && Current().EndTimeMs == 3250, "Canvas end boundary resizes independently");
            var before = ui.View.Document.DeepClone();
            Drag(1250, 1500, 256, cancel: true);
            Check(ui.View.Document.ContentEquals(before), "Canvas boundary Escape restores interval");
            Drag(1250, 1407, 200, shift: true);
            Check(Math.Abs(Current().TimeMs - 1407) < .01 && Current().EndTimeMs == 3250, "Canvas Shift bypasses snapping");
            Drag(1407, 4000, 200);
            Check(Current().TimeMs < 3250 && Current().TimeMs > 3249.99 && Current().EndTimeMs == 3250, "Canvas begin cannot pass end");
            Check(Current().Pattern == baseline.BananaShowers[0].Pattern, "Canvas retains grouped pattern");

            BananaShower Current() => ui.View.Document.BananaShowers.Single();
            void Drag(double from, double to, double x, bool cancel = false, bool shift = false)
            {
                var a = ui.ScreenAt(from, x); var b = ui.ScreenAt(to, x);
                ui.View.PointerDown(a.X, a.Y, 0, shift, false);
                ui.View.PointerMove(b.X, b.Y, shift, false); ui.Paint();
                if (cancel) ui.Key(27);
                ui.View.PointerUp(b.X, b.Y, 0, shift); ui.Paint();
            }
        }
    }

    private static void TimelineBoundaries()
    {
        foreach (bool osu in new[] { false, true })
        foreach (bool grouped in new[] { false, true })
        {
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
            map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 3000,
                Pattern = grouped ? new BananaShowerPattern() : null });
            ui.LoadDocument(map);
            if (osu) { ui.View.ToggleOsuCanvas(); ui.Paint(); }
            ui.View.UpdateTransport(2000, 10000, true, false, false, null, null); ui.Paint();
            var baseline = ui.View.Document.DeepClone();
            Drag(1000, 1260);
            Check(Math.Abs(Current().TimeMs - 1250) < .01 && Current().EndTimeMs == 3000, "Timeline begin snaps independently");
            var resized = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(baseline), "Timeline begin undo");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(resized), "Timeline begin redo");
            Drag(1250, 1407, shift: true);
            Check(Math.Abs(Current().TimeMs - 1407) < .01 && Current().EndTimeMs == 3000, "Shift bypasses begin snapping");
            var beforeCancel = ui.View.Document.DeepClone();
            Drag(1407, 1750, cancel: true);
            Check(ui.View.Document.ContentEquals(beforeCancel), "Escape cancels begin resize");
            Drag(1407, 3500);
            Check(Current().TimeMs < 3000 && Current().TimeMs >= 2999.99 && Current().EndTimeMs == 3000, "Begin stays before fixed end");
            ui.Key('Z', ctrl: true);
            Drag(1407, -250);
            Check(Current().TimeMs == 0 && Current().EndTimeMs == 3000, "Begin stays nonnegative");
            ui.Key('Z', ctrl: true);
            Drag(3000, 3250);
            Check(Math.Abs(Current().TimeMs - 1407) < .01 && Current().EndTimeMs == 3250, "Tail still resizes independently");
            Drag(2000, 2250, cursor: false);
            Check(Current().TimeMs > 1407 && Math.Abs(Current().EndTimeMs - Current().TimeMs - (3250 - 1407)) < .01, "Body still moves complete interval");
            Check(Current().Pattern == baseline.BananaShowers[0].Pattern, "Timeline gestures retain pattern");

            BananaShower Current() => ui.View.Document.BananaShowers.Single();
            void Drag(double from, double to, bool shift = false, bool cancel = false, bool cursor = true)
            {
                var bounds = ui.View.ObjectTimelineBounds;
                float x = bounds.X + (float)((from - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
                float y = bounds.Y + bounds.Height / 2;
                float target = x + (float)((to - from) * ui.View.ObjectTimelinePixelsPerMs);
                ui.View.PointerMove(x, y, shift, false); ui.Paint();
                Check(ui.View.TimelineResizeCursor == cursor, "Timeline boundary resize cursor");
                ui.View.PointerDown(x, y, 0, shift, false);
                ui.View.PointerMove(target, y, shift, false); ui.Paint();
                if (cancel) ui.Key(27);
                ui.View.PointerUp(target, y, 0, shift); ui.Paint();
            }
        }
    }

    private static void StreamZoom()
    {
        foreach (bool stack in new[] { false, true })
        {
            var ui = new Ui(false); var map = new MapDocument { DurationMs = 30000 };
            var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 21000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id); ui.Key('F', ctrl: true, shift: true);
            if (stack) ui.ConversionMode(true);
            var accepted = ui.View.Document.DeepClone();
            var point = ui.ScreenAt(1500, 256);
            double scale = ui.View.CanvasTimeZoom;
            ui.View.Wheel(point.X, point.Y, 120, false, false, true); ui.Paint();
            Check(ui.View.CanvasTimeZoom > scale && ui.View.Document.ContentEquals(accepted), "Canvas zoom preserves live pattern content");
            ui.View.Wheel(point.X, point.Y, -120, false, false, true); ui.Paint();
            Check(Math.Abs(ui.View.CanvasTimeZoom - scale) < .001 && ui.View.Document.ContentEquals(accepted), "Pattern canvas zoom round trip");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
