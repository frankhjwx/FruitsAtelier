using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class CrossCanvasEditingTests
{
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        foreach (bool osu in new[] { false, true })
        {
            string previous = L.Language;
            try
            {
                L.SetLanguage(language);
                var ui = Load(osu);
                var before = ui.View.Document.DeepClone();
                Check(!ui.View.SliderCanvasChoiceVisible && ui.View.OsuLinkNoticeOpacity == 0 && !ui.View.IsDirty, "Selection must be silent and leave content unchanged.");
                ui.Key(39, ctrl: true); Check(!before.ContentEquals(ui.View.Document) && !ui.View.SliderCanvasChoiceVisible, "Horizontal nudge must work across canvases.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Nudge must undo exactly."); Select(ui);
                ui.Key('H', ctrl: true); Check(!before.ContentEquals(ui.View.Document) && !ui.View.SliderCanvasChoiceVisible, "Mirror must work across canvases.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Mirror must undo exactly."); Select(ui);
                ui.Key(187, ctrl: true); ui.View.KeyUp(187);
                Check((osu ? ui.View.Document.Tracks.Single().SpanCount : ui.View.Document.ImportedSliders.Single().SpanCount) == 2, "Repeat shortcut must work across canvases.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Repeat must undo exactly."); Select(ui);
                ui.Key('Q');
                Check(!before.ContentEquals(ui.View.Document) && !ui.View.SliderCanvasChoiceVisible, "NC must remain editable across canvases.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "NC undo must restore content."); Select(ui);
                var start = osu ? ui.View.OsuCanvasScreen(100, 192) : ui.ScreenAt(1000, 100);
                var end = osu ? ui.View.OsuCanvasScreen(132, 192) : ui.ScreenAt(1000, 132);
                ui.Click(start.X, start.Y);
                Check(before.ContentEquals(ui.View.Document), "Click without movement must preserve raw source data.");
                ui.View.PointerDown(start.X, start.Y, 0, false, false);
                ui.View.PointerMove(end.X, end.Y, false, false);
                ui.View.PointerUp(end.X, end.Y, 0); ui.Paint();
                Check(!before.ContentEquals(ui.View.Document) && !ui.View.SliderCanvasChoiceVisible
                    && (osu ? ui.View.Document.Tracks.Count == 1 : ui.View.Document.ImportedSliders.Count == 1), "Horizontal drag must preserve representation.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Horizontal drag must undo exactly."); Select(ui);
                ui.Key('R', ctrl: true, shift: true);
                Check(ui.View.SliderCanvasChoiceVisible && before.ContentEquals(ui.View.Document), "Rotation must ask before modifying content.");
                ui.Key(46); ui.Key('H', ctrl: true); ui.Key(9);
                Check(before.ContentEquals(ui.View.Document) && ui.View.IsOsuCanvas == osu, "Choice window must block background shortcuts.");
                ui.Key(27); Check(!ui.View.SliderCanvasChoiceVisible && !ui.View.IsDirty, "Cancel must leave no content history.");
                var row = ui.View.ObjectTimelineBounds;
                float TimeX(double time) => row.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
                float rowY = row.Y + 27;
                ui.View.PointerDown(TimeX(1000), rowY, 0, false, false);
                ui.View.PointerMove(TimeX(1125), rowY, false, false);
                ui.View.PointerUp(TimeX(1125), rowY, 0); ui.Paint();
                Check((osu ? ui.View.Document.Tracks.Single().Nodes[0].TimeMs : ui.View.Document.ImportedSliders.Single().TimeMs) == 1125,
                    "Timeline movement must work across canvases.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Timeline movement must undo exactly."); Select(ui);
                ui.View.PointerDown(TimeX(2000), rowY, 0, false, false);
                ui.View.PointerMove(TimeX(3000), rowY, false, false);
                ui.View.PointerUp(TimeX(3000), rowY, 0); ui.Paint();
                Check((osu ? ui.View.Document.Tracks.Single().SpanCount : ui.View.Document.ImportedSliders.Single().SpanCount) == 2,
                    "Timeline tail must change repeats across canvases.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Timeline repeats must undo exactly."); Select(ui);
                ui.Key('B');
                Check(ui.View.SliderCanvasChoiceVisible, "Control editing tool must ask about mismatched sliders.");
                ui.ClickText(L.Get(osu ? "osu.switchCatch" : "osu.switchOsu"));
                Check(ui.View.IsOsuCanvas != osu && ui.View.SelectedObjectIds.Count == 1 && before.ContentEquals(ui.View.Document), "Switch must preserve selection, type and content.");
                ui.View.ToggleOsuCanvas(); Select(ui);
                ui.Key('R', ctrl: true, shift: true);
                ui.ClickText(L.Get(osu ? "osu.convertLegacy" : "osu.convertFSlider"));
                Check(!ui.View.SliderCanvasChoiceVisible && (osu ? ui.View.Document.ImportedSliders.Count == 1 : ui.View.Document.Tracks.Count == 1), "Conversion must produce the current canvas representation.");
                Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Converted output must validate.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Conversion must undo in one step.");
            }
            finally { L.SetLanguage(previous); }
        }
        var mixed = Load(false);
        var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 3500, X = 200 }]);
        mixed.View.Document.Tracks.Add(track); mixed.Paint(); Select(mixed);
        var snapshot = mixed.View.Document.DeepClone();
        mixed.Key(187, ctrl: true); mixed.View.KeyUp(187);
        Check(mixed.View.Document.ImportedSliders.Single().SpanCount == 2 && mixed.View.Document.Tracks.Single().SpanCount == 2,
            "Repeat edits must apply to both types in a mixed selection.");
        mixed.Key('Z', ctrl: true); Check(snapshot.ContentEquals(mixed.View.Document), "Mixed repeats must undo together."); Select(mixed);
        mixed.Key('R', ctrl: true, shift: true);
        mixed.ClickText(L.Get("osu.convertFSlider"));
        Check(mixed.View.Document.Tracks.Count == 2 && mixed.View.Document.ImportedSliders.Count == 0, "Mixed conversion must convert only mismatched sliders.");
        mixed.Key('Z', ctrl: true); Check(snapshot.ContentEquals(mixed.View.Document), "Mixed conversion must undo atomically.");
        var ctrl = Load(false); snapshot = ctrl.View.Document.DeepClone();
        ctrl.ClickMap(1500, 400, ctrl: true);
        Check(ctrl.View.SliderCanvasChoiceVisible && snapshot.ContentEquals(ctrl.View.Document), "Ctrl insertion must prompt without changing shape.");
        ctrl.Key(27);
        var timeline = ctrl.View.ObjectTimelineBounds;
        float headX = timeline.X + (float)((1000 - ctrl.View.ObjectTimelineStartMs) * ctrl.View.ObjectTimelinePixelsPerMs);
        ctrl.View.PointerDown(headX, timeline.Y + 27, 0, false, true); ctrl.View.PointerUp(headX, timeline.Y + 27, 0); ctrl.Paint();
        Check(!ctrl.View.SliderCanvasChoiceVisible, "Timeline Ctrl selection must not ask to edit shape.");
        var failed = Load(true);
        var stream = new CurveTrack { StreamSnapDivisor = 4 };
        stream.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 3500, X = 200 }]);
        failed.View.Document.Tracks.Add(stream); failed.Paint(); Select(failed);
        snapshot = failed.View.Document.DeepClone();
        failed.Key('R', ctrl: true, shift: true); failed.ClickText(L.Get("osu.convertLegacy"));
        Check(snapshot.ContentEquals(failed.View.Document) && failed.View.SelectedObjectIds.Count == 2 && failed.View.StatusMessage.Length > 0,
            "Failed batch conversion must restore every original and preserve the selection with a reason.");
        failed.Key('Z', ctrl: true); Check(snapshot.ContentEquals(failed.View.Document), "Failed conversion must not add an undo record.");
        failed.Key('R', ctrl: true, shift: true); failed.LoadDocument(new MapDocument());
        Check(!failed.View.SliderCanvasChoiceVisible && !failed.View.DiscardConfirmationVisible, "Document replacement must retire the canvas choice.");
    }

    private static void Select(Ui ui) { ui.Key('1'); ui.Key('A', ctrl: true); }
    private static Ui Load(bool osu)
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        map.DurationMs = 12000;
        if (osu)
        {
            var result = SliderRepresentationEditing.ConvertToTrack(map, map.ImportedSliders.Single().Id);
            Check(result.Success, "FSlider fixture conversion failed.");
        }
        var ui = new Ui(); ui.LoadDocument(map);
        if (osu) ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(1000, 12000, true, false, false, null, null); ui.Paint(); Select(ui);
        return ui;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
