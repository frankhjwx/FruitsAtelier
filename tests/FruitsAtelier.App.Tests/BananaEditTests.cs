using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class BananaEditTests
{
    internal static void Run()
    {
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
        ui.Key('F', ctrl: true, shift: true); Wait(); ui.ClickText(L.Get("stream.confirm"));
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
    private static void StreamZoom()
    {
        foreach (bool stack in new[] { false, true })
        {
            var ui = new Ui(); var map = new MapDocument { DurationMs = 30000 };
            var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 21000, X = 256 }]);
            map.Tracks.Add(track); ui.LoadDocument(map); ui.SelectTrack(track.Id); ui.Key('F', ctrl: true, shift: true);
            if (stack) ui.ClickText(L.Get("conversion.stackTab"));
            var bounds = ui.View.StackPreviewBounds; double scale = ui.View.StackPreviewScale;
            ui.View.Wheel(bounds.X + 20, bounds.Y + 80, 120, false); ui.Paint();
            double start = ui.View.StackPreviewVisibleStart;
            ui.View.Wheel(bounds.X + 20, bounds.Y + 80, 120, false, false, true); ui.Paint();
            Check(ui.View.StackPreviewScale > scale && ui.View.StackPreviewVisibleStart > start, "Stream/Stack zoom anchors the pointer");
            ui.View.Wheel(bounds.X + 20, bounds.Y + 80, -120, false, false, true); ui.Paint();
            Check(Math.Abs(ui.View.StackPreviewScale - scale) < .0001 && Math.Abs(ui.View.StackPreviewVisibleStart - start) < .001
                && ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Stream/Stack zoom round trip preserves content");
            ui.Key(27);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
