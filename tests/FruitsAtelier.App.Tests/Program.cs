using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

var tests = new (string Name, Action Run)[]
{
    ("Canvas seeks follow the current beat grid and timing changes", CanvasSeekSnapTests.BeatGrid),
    ("Free canvas seeking retains continuous time", CanvasSeekSnapTests.FreeMode),
    ("Timeline grabs retain the exact original time after round trips", TimelineDragTests.ReturnToStart),
    ("Timeline background clicks seek and drag limits preserve the origin", TimelineDragTests.ClickAndLimits),
    ("Fruit tools place quarters and sixths without moving existing objects", PlaceOnBothGrids),
    ("Beat snap slider exposes every requested divisor through one drag control", RequestedInteractionTests.SnapDivisors),
    ("Double-click enters one Slider and other clicks leave its edit mode", RequestedInteractionTests.DoubleClickEditing),
    ("A Legacy Slider context action converts it to a strictly aligned FSlider", SliderInteractionTests.LegacyContextConversion),
    ("Selected parents snap from the earliest start and keep one time and X offset", RequestedInteractionTests.MultiObjectDrag),
    ("A single Slider uses its start as the snap reference while moving", RequestedInteractionTests.SingleSliderSnap),
    ("The main canvas reserves CS0 padding while timing grid lines stay inside X=0..512", RequestedInteractionTests.PlayfieldPadding),
    ("Banana placement uses left-start right-end and exposes precise times", RequestedInteractionTests.BananaPlacement),
    ("Banana showers expose an X=0..512 range with draggable body and time handles", RequestedInteractionTests.BananaRectangleEditing),
    ("A long audio track extends imported-map Fruit and Banana editing past the last source object", RequestedInteractionTests.AudioExtendsEditableDuration),
    ("Selecting an exact numeric time does not resnap or create undo", SelectOffGrid),
    ("A multi-update fruit drag is one undo and redo transaction", DragUndoRedo),
    ("Escape cancels a drag and a later mouse-up cannot commit it", EscapeDrag),
    ("Capture cancellation restores the document and releases capture intent", CaptureCancellation),
    ("Undo cancels a curve draft without undoing the preceding fruit", DraftUndo),
    ("A zero-length handle does not prevent dragging its anchor", ZeroHandleAnchor),
    ("Rejected numeric edits can be corrected before another paint", NumericRejectAndRetry),
    ("Non-finite numeric input and field shortcuts cannot mutate objects", NumericIsolation),
    ("Rejected anchor time preserves curve controls and accepts a corrected time", AnchorNumericRetry),
    ("Click-created slider keeps corner points and commits one transaction", DraftCompletion),
    ("Painted fruit, time ruler and playhead share an upward time axis", UpwardPainting),
    ("Clicking higher on the canvas selects a later time", UpwardClickTime),
    ("Wheel up reveals later time and middle drag keeps content under the pointer", WheelAndPan),
    ("Overview wheel seeks continuously, follows the clock and preserves playback and content", OverviewWheel),
    ("Default and reset view follow AR while preserving imported difficulty", TimeZoomTests.DefaultsAndReset),
    ("Zoom slider and wheel share scale, bounds and content isolation", TimeZoomTests.SliderAndWheel),
    ("Zoom slider fits both languages and preserves playback following", TimeZoomTests.PlaybackAndLanguages),
    ("Control-wheel keeps the painted time anchor fixed away from limits", ZoomPaintedAnchor),
    ("Restore AR scale positions the playhead without editing map or history", RestoreArViewport),
    ("AR controls preview fall distance, visibility and reversible numeric edits", ArPreviewAndInput),
    ("AR scale works with a hidden preview and follows canvas width changes", ArScaleResize),
    ("Hiding main curves preserves every converted fruit, droplet and tiny droplet", MainCurveVisibility),
    ("Preview debug curves are independent and remain behind converted objects", PreviewCurveLayers),
    ("CS numeric edits scale main and preview objects together and undo restores them", CircleSizeAcrossViews),
    ("Main curves overlay objects and selection changes only their opacity", MainCurveSelectionOpacity),
    ("Imported timing boundaries drive both quarter and sixth editing", MultiTimingEditing),
    ("Playback commands and device clock stay separate from content history", TransportIntegration),
    ("Saving records a baseline without destroying undo or redo", SavedBaseline),
    ("File keyboard commands invoke real host callbacks", FileCommands),
    ("OSZ import keeps map/audio layout and rejects unsafe archives without video or storyboard", ArchiveTests.Run),
    ("Playback and backward seeks keep a continuous viewport with a lower-quarter playhead", ContinuousFollow),
    ("Playback line stays fixed across endpoints, zoom, resize and seek", ViewportFeedbackTests.Run),
    ("Selecting generated fruit, tick or tiny droplet selects the entire owning slider", SliderSelectionTests.Run),
    ("One authoring gesture mixes straight and Bezier segments with reversible edits", MixedSliderTests.Author),
    ("Legacy Sliders convert to FSliders and preserve repeats through project and osu export", MixedSliderTests.Import),
    ("Beatmap resource export excludes output subtrees and preserves selected audio", ResourceTests.Run),
    ("Fruit clipboard snapshots preserve metadata and undo identities", ClipboardTests.FruitSnapshotAndHistory),
    ("Anchor clipboard actions copy and cut the entire repeated slider", ClipboardTests.AnchorCopiesAndCutsParent),
    ("Legacy Slider and banana clipboard preserve geometry and export samples", ClipboardTests.ImportedAndBananaMetadata),
    ("Clipboard rejects drafts and overflowing pastes without data loss", ClipboardTests.ClipboardBoundaries),
    ("Slider pen gestures combine corner points and curve handles", SliderInteractionTests.DrawGestures),
    ("Selected control points and handles highlight and drag", SliderInteractionTests.ControlSelectionAndDrag),
    ("A selected FSlider enters anchor editing on one point click and scopes its context menu", SliderInteractionTests.SelectedAnchorEntryAndContext),
    ("Point context menu inserts converts and deletes with undo", SliderInteractionTests.PointContextMenu),
    ("Fruit context menu copies cuts pastes and deletes", SliderInteractionTests.FruitClipboardAndDelete),
    ("Slider context menu operates on the entire parent", SliderInteractionTests.SliderClipboardAndDelete),
    ("Hierarchy selection completes or cancels drafts and selects immediately", SliderInteractionTests.HierarchyCompletesDraft),
    ("Closing a context menu does not pass clicks to the canvas", SliderInteractionTests.ContextOutsideClick),
    ("Deleting a point never revives dormant neighbour handles", SliderInteractionTests.DeleteDoesNotActivateDormantHandles),
    ("An incompatible Legacy repeat remains unchanged during point insertion", SliderInteractionTests.RepeatInsertion),
    ("Moving a draft tail keeps its visible future handle in bounds", SliderInteractionTests.DraftTailHandleBounds),
    ("Mixed clipboard batches preserve relative time and independent source order", ClipboardMultiTests.MixedBatchPreservesSnapshotAndOrder),
    ("Cutting a mixed batch is one reversible transaction", ClipboardMultiTests.MixedCutIsOneTransaction),
    ("A later overflowing pasted object rolls back the entire batch", ClipboardMultiTests.OverflowPasteRollsBackBatch),
    ("Invalid batch members cannot partially copy or cut", ClipboardMultiTests.InvalidMemberRejectsWholeCopyAndCut),
    ("Object and anchor Ctrl selections respect the active mode", MultiSelectionTests.ModesAndCtrlSelection),
    ("Object boxes deduplicate generated slider children in Select and Fruit modes", MultiSelectionTests.ObjectBoxAndParentDedup),
    ("Multi-selection clipboard menus and deletion preserve batch transactions", MultiSelectionTests.BatchClipboardDelete),
    ("Anchor boxes delete endpoints and remove insufficient tracks atomically", MultiSelectionTests.AnchorBoxAndEndpointDelete),
    ("Canceling object and anchor boxes restores selection without history", MultiSelectionTests.SelectionCancellation),
    ("Playback does not change the transform during a selection box", MultiSelectionTests.PlaybackBoxTransform),
    ("Language switching refreshes chrome without editing the map", LanguageTests.SwitchWithoutEditing),
    ("English batch menus and Core diagnostics use the same catalog", LanguageTests.EnglishMultiMenusAndDiagnostics)
};

int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} editor integration tests passed.");
return failures == 0 ? 0 : 1;

static void ContinuousFollow()
{
    var ui = new Ui();
    ui.View.UpdateTransport(0, 60000, true, false, false, null, "song.mp3");
    ui.View.UpdateTransport(10000, 60000, true, true, false, null, "song.mp3"); ui.Paint();
    double start = ui.View.ViewStartMs;
    double offset = ui.View.PlayheadMs - start;
    float overviewX = ui.Canvas.Outlines.Single(s => s.Color == 0x71849A).Bounds.X;
    ui.View.UpdateTransport(10016, 60000, true, true, false, null, "song.mp3"); ui.Paint();
    Near(start + 16, ui.View.ViewStartMs);
    Near(offset, ui.View.PlayheadMs - ui.View.ViewStartMs);
    float nextOverviewX = ui.Canvas.Outlines.Single(s => s.Color == 0x71849A).Bounds.X;
    True(nextOverviewX > overviewX && nextOverviewX - overviewX < 1, "Overview viewport snapped instead of moving continuously.");
    ui.View.UpdateTransport(8000, 60000, true, true, false, null, "song.mp3"); ui.Paint();
    Near(8000 - offset, ui.View.ViewStartMs);
    ui.View.UpdateTransport(8000, 60000, true, false, false, null, "song.mp3"); ui.Paint();
    float navY = ui.Canvas.Texts.Single(t => t.Value == "时间导航").Y + 50;
    ui.Click(900, navY);
    double later = ui.View.ViewStartMs;
    ui.View.PointerDown(900, navY, 0, false, false);
    ui.View.PointerMove(899, navY, false, false);
    ui.View.PointerUp(899, navY, 0); ui.Paint();
    True(ui.View.ViewStartMs < later && later - ui.View.ViewStartMs < 100, "Paused backward seek did not track continuously.");
    Near(offset, ui.View.PlayheadMs - ui.View.ViewStartMs);
    ui.View.Wheel(600, 300, 120, false);
    double resumeTime = ui.View.PlayheadMs;
    ui.View.UpdateTransport(resumeTime, 60000, true, true, false, null, "song.mp3");
    Near(resumeTime - offset, ui.View.ViewStartMs);
    ui.View.UpdateTransport(resumeTime + 17, 60000, true, true, false, null, "song.mp3");
    Near(resumeTime + 17 - offset, ui.View.ViewStartMs);
    True(!ui.View.IsDirty, "Following audio changed map content.");
}

static void MultiTimingEditing()
{
    var ui = new Ui();
    var doc = new MapDocument { DurationMs = 10000, IsDemo = false };
    doc.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
    doc.TimingPoints.Add(new() { TimeMs = 1100, BeatLengthMs = 400, Uninherited = true });
    doc.TimingPoints.Add(new() { TimeMs = 1180, BeatLengthMs = -50, Uninherited = false });
    ui.LoadDocument(doc); ui.Paint();
    ui.SetSnapDivisor(6); ui.Key('F'); ui.ClickMap(1210, 100);
    Near(1233.333333333333, ui.View.Document.Fruits.Single().TimeMs);
    ui.SetSnapDivisor(4); ui.ClickMap(1490, 400);
    Near(1500, ui.View.Document.Fruits.Last().TimeMs);
    True(ui.Canvas.Lines.Any(l => l.Color == 0x845460), "The red timing boundary is absent from the painted grid.");
    ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
    True(!ui.View.IsDirty && ui.View.Document.TimingPoints.Count == 3, "Timing was changed by fruit editing.");
}

static void TransportIntegration()
{
    var ui = new Ui();
    int toggles = 0;
    var seeks = new List<double>();
    ui.View.RequestTogglePlayback = () => toggles++;
    ui.View.RequestSeek = seeks.Add;
    ui.View.UpdateTransport(0, 45000, true, false, false, null, "music.mp3");
    ui.Key(32); True(toggles == 1, "Space did not reach audio transport.");
    ui.View.UpdateTransport(12500, 45000, true, true, false, null, "music.mp3"); ui.Paint();
    Near(12500, ui.View.PlayheadMs); Near(45000, ui.View.TimelineDurationMs);
    ui.Key(36); Near(0, seeks.Single());
    ui.View.UpdateTransport(2000, 45000, true, false, false, null, "music.mp3");
    Near(2000, ui.View.PlayheadMs);
    True(!ui.View.IsDirty, "Playback or seek dirtied the document.");
    ui.View.UpdateTransport(0, 0, false, false, false, "decoder error", "bad.mp3");
    ui.Key(32); True(toggles == 1 && ui.View.AudioNotice == "decoder error", "Failed audio still issued Play.");
}

static void SavedBaseline()
{
    var history = new EditorHistory(new MapDocument());
    history.Begin("fruit"); history.Document.Fruits.Add(new() { TimeMs = 500, X = 256 }); history.Commit();
    history.MarkSaved(); True(!history.IsDirty && history.CanUndo, "Saving destroyed history or left dirty state.");
    history.Undo(); True(history.IsDirty && history.Document.Fruits.Count == 0, "Undo past save did not dirty the map.");
    history.Redo(); True(!history.IsDirty && history.Document.Fruits.Count == 1, "Redo to saved state did not clear dirty state.");
}

static void FileCommands()
{
    var ui = new Ui();
    var calls = new List<string>();
    ui.View.RequestOpen = () => calls.Add("open");
    ui.View.RequestSave = () => calls.Add("save");
    ui.View.RequestSaveAs = () => calls.Add("saveAs");
    ui.View.RequestExport = () => calls.Add("export");
    ui.Key('O', ctrl: true); ui.Key('S', ctrl: true); ui.Key('S', ctrl: true, shift: true); ui.Key('E', ctrl: true);
    True(calls.SequenceEqual(new[] { "open", "save", "saveAs", "export" }), "A file shortcut did not invoke its host callback.");
    True(!ui.View.IsDirty, "File commands changed content without a host action.");
}

static void PlaceOnBothGrids()
{
    var ui = new Ui();
    var original = ui.View.Document.Fruits.ToDictionary(f => f.Id, f => new MapPoint(f.TimeMs, f.X));
    ui.Key('F');
    ui.ClickMap(1130, 480);
    var quarter = ui.View.Document.Fruits.Single(f => !original.ContainsKey(f.Id));
    Near(1125, quarter.TimeMs);
    Near(480, quarter.X);
    ui.SetSnapDivisor(6);
    True(ui.View.SnapDivisor == 6, "Sixth grid was not selected.");
    foreach (var fruit in ui.View.Document.Fruits.Where(f => original.ContainsKey(f.Id)))
        PointNear(original[fruit.Id], new(fruit.TimeMs, fruit.X));
    ui.ClickMap(1190, 32);
    var sixth = ui.View.Document.Fruits.Single(f => !original.ContainsKey(f.Id) && f.Id != quarter.Id);
    Near(3500.0 / 3, sixth.TimeMs);
    Near(32, sixth.X);
    Valid(ui);
}

static void SelectOffGrid()
{
    var ui = new Ui();
    var id = ui.View.Document.Fruits[0].Id;
    ui.ClickFruit(id);
    ui.SetField("时间  ms", "1001");
    Near(1001, ui.Fruit(id).TimeMs);
    ui.ClickFruit(id);
    Near(1001, ui.Fruit(id).TimeMs);
    ui.Key('Z', ctrl: true);
    Near(250, ui.Fruit(id).TimeMs);
    True(!ui.View.IsDirty, "A selection click created an extra undo transaction.");
}

static void DragUndoRedo()
{
    var ui = new Ui();
    var id = ui.View.Document.Fruits[0].Id;
    ui.DownMap(250, 96);
    True(ui.View.WantsCapture, "Fruit drag did not request capture.");
    ui.MoveMap(300, 110);
    ui.MoveMap(345, 135);
    ui.MoveMap(377, 172);
    ui.UpMap(377, 172);
    PointNear(new(375, 172), new(ui.Fruit(id).TimeMs, ui.Fruit(id).X));
    True(!ui.View.WantsCapture && ui.View.IsDirty, "Completed drag did not release capture or become dirty.");
    ui.Key('Z', ctrl: true);
    PointNear(new(250, 96), new(ui.Fruit(id).TimeMs, ui.Fruit(id).X));
    True(!ui.View.IsDirty, "One undo did not revert the entire drag.");
    ui.Key('Y', ctrl: true);
    PointNear(new(375, 172), new(ui.Fruit(id).TimeMs, ui.Fruit(id).X));
    Valid(ui);
}

static void EscapeDrag()
{
    var ui = new Ui();
    var id = ui.View.Document.Fruits[0].Id;
    ui.DownMap(250, 96);
    ui.MoveMap(600, 240);
    ui.Key(27);
    ui.UpMap(600, 240);
    PointNear(new(250, 96), new(ui.Fruit(id).TimeMs, ui.Fruit(id).X));
    True(!ui.View.WantsCapture && !ui.View.IsDirty, "Escape left a drag active or committed.");
    ui.Key('Z', ctrl: true);
    True(!ui.View.IsDirty, "Cancelled drag left an undo entry.");
}

static void CaptureCancellation()
{
    var ui = new Ui();
    var id = ui.View.Document.Fruits[0].Id;
    ui.DownMap(250, 96);
    ui.MoveMap(550, 240);
    ui.View.CancelInteraction();
    ui.Paint();
    ui.UpMap(550, 240);
    PointNear(new(250, 96), new(ui.Fruit(id).TimeMs, ui.Fruit(id).X));
    True(!ui.View.WantsCapture && !ui.View.IsDirty, "Capture cancellation retained a partial edit.");
    ui.DownMap(250, 96);
    ui.MoveMap(500, 160);
    ui.UpMap(500, 160);
    Near(500, ui.Fruit(id).TimeMs);
    ui.Key('Z', ctrl: true);
    True(!ui.View.IsDirty, "Capture cancellation corrupted the next transaction.");
}

static void DraftUndo()
{
    var ui = new Ui();
    int fruits = ui.View.Document.Fruits.Count, tracks = ui.View.Document.Tracks.Count;
    ui.Key('F');
    ui.ClickMap(3500, 500);
    ui.Key('B');
    ui.ClickMap(2000, 24);
    ui.ClickMap(3000, 40);
    True(ui.View.Document.Tracks.Count == tracks + 1, "Curve draft was not created.");
    ui.Key('Z', ctrl: true);
    True(ui.View.Document.Tracks.Count == tracks, "Undo did not cancel the draft.");
    True(ui.View.Document.Fruits.Count == fruits + 1, "Cancelling a draft also undid the preceding fruit.");
    True(!ui.View.WantsCapture, "Cancelled draft retained capture.");
    ui.Key('Z', ctrl: true);
    True(ui.View.Document.Fruits.Count == fruits && !ui.View.IsDirty, "A second undo did not revert the preceding fruit.");
}

static void ZeroHandleAnchor()
{
    var ui = new Ui();
    var id = ui.View.Document.Tracks[0].Nodes[1].Id;
    ui.ClickMap(2500, 392);
    ui.Key('B');
    ui.ClickMap(2500, 392);
    ui.SetField("入柄 Δms", "0");
    ui.SetField("入柄 ΔX", "0");
    True(ui.Anchor(id).HandleIn == new MapPoint(0, 0), "Zero-length handle setup failed.");
    ui.DownMap(2500, 392);
    ui.MoveMap(2625, 360);
    ui.UpMap(2625, 360);
    var anchor = ui.Anchor(id);
    PointNear(new(2625, 360), new(anchor.TimeMs, anchor.X));
    True(anchor.HandleIn == new MapPoint(0, 0), "Dragging an anchor changed its overlapping handle.");
    Valid(ui);
}

static void NumericRejectAndRetry()
{
    var ui = new Ui();
    var id = ui.View.Document.Fruits[0].Id;
    ui.ClickFruit(id);
    ui.FocusField("时间  ms");
    ui.Type("-1");
    ui.View.KeyDown(13, false, false);
    Near(250, ui.Fruit(id).TimeMs);
    True(!ui.View.IsDirty, "Rejected time polluted the model.");
    // Queued input can arrive before WM_PAINT refreshes field callbacks.
    ui.View.KeyDown('A', true, false);
    ui.Type("1001");
    ui.View.KeyDown(13, false, false);
    ui.Paint();
    Near(1001, ui.Fruit(id).TimeMs);
    ui.Key('Z', ctrl: true);
    Near(250, ui.Fruit(id).TimeMs);
    True(!ui.View.IsDirty, "A rejected numeric edit added undo history.");
    ui.Key('Y', ctrl: true);
    Near(1001, ui.Fruit(id).TimeMs);
    Valid(ui);
}

static void NumericIsolation()
{
    var ui = new Ui();
    var id = ui.View.Document.Fruits[0].Id;
    int count = ui.View.Document.Fruits.Count;
    ui.ClickFruit(id);
    ui.FocusField("时间  ms");
    ui.Type("1e309");
    ui.Key(13);
    Near(250, ui.Fruit(id).TimeMs);
    ui.Key('Z', ctrl: true);
    ui.Key(46);
    ui.Key('B');
    True(ui.View.Document.Fruits.Count == count, "Delete in a numeric field deleted a fruit.");
    True(ui.View.ActiveTool == "Select", "A field keystroke switched editor tools.");
    ui.Key(27);
    True(!ui.View.IsDirty, "Cancelling invalid field input mutated the document.");
    Valid(ui);
}

static void AnchorNumericRetry()
{
    var ui = new Ui();
    var id = ui.View.Document.Tracks[0].Nodes[1].Id;
    var originalIn = ui.Anchor(id).HandleIn;
    var originalOut = ui.Anchor(id).HandleOut;
    ui.ClickMap(2500, 392);
    ui.Key('B');
    ui.ClickMap(2500, 392);
    ui.FocusField("时间  ms");
    ui.Type("1100");
    ui.View.KeyDown(13, false, false);
    Near(2500, ui.Anchor(id).TimeMs);
    ui.View.KeyDown('A', true, false);
    ui.Type("2600");
    ui.View.KeyDown(13, false, false);
    ui.Paint();
    Near(2600, ui.Anchor(id).TimeMs);
    True(ui.Anchor(id).HandleIn == originalIn && ui.Anchor(id).HandleOut == originalOut, "Numeric time edit changed relative controls.");
    ui.Key('Z', ctrl: true);
    Near(2500, ui.Anchor(id).TimeMs);
    True(!ui.View.IsDirty, "Rejected anchor edit created a transaction.");
    Valid(ui);
}

static void DraftCompletion()
{
    var ui = new Ui();
    int tracks = ui.View.Document.Tracks.Count;
    ui.Key('B');
    ui.ClickMap(2000, 24);
    ui.ClickMap(3000, 240);
    var track = ui.View.Document.Tracks[^1];
    True(track.Nodes[1].HandleIn == default && track.Nodes[0].HandleOut == default
        && CurveMath.SegmentKind(track, 0) == CurveKind.Linear, "Click-only drawing introduced curve handles.");
    ui.Key(13);
    True(ui.View.ActiveTool == "Select", "Completed curve did not return to selection.");
    Valid(ui);
    Guid id = track.Id, anchorId = track.Nodes[1].Id;
    ui.Key('Z', ctrl: true);
    True(ui.View.Document.Tracks.Count == tracks && !ui.View.IsDirty, "Curve completion was not one undo transaction.");
    ui.Key('Y', ctrl: true);
    var restored = ui.View.Document.Tracks.Single(t => t.Id == id);
    True(restored.Nodes[1].Id == anchorId, "Curve redo replaced anchor identity.");
    Valid(ui);
}

static void UpwardPainting()
{
    var ui = new Ui();
    var plot = ui.Plot;
    var fruits = ui.Canvas.Circles.Where(c => c.Filled && c.Color == 0xFFFFFF
        && Math.Abs(c.Radius - CatchSize.FruitRadius(ui.View.Document.CircleSize) * plot.Width / 512) < 0.001).ToArray();
    True(fruits.Length >= 2, "No main-canvas fruit were painted.");
    True(fruits[1].Y < fruits[0].Y, "The later fruit was not painted above the earlier fruit.");
    Near(plot.Bottom - 250 * ui.View.PixelsPerMs, fruits[0].Y);
    Near(plot.Bottom - 500 * ui.View.PixelsPerMs, fruits[1].Y);
    var zero = ui.Canvas.Texts.Single(t => t.Value == "0" && t.X < plot.X && t.Y > plot.Y);
    var beat = ui.Canvas.Texts.Single(t => t.Value == "500" && t.X < plot.X && t.Y > plot.Y);
    True(beat.Y < zero.Y, "The time ruler increases downward.");
    True(zero.Y >= plot.Bottom - 16 && zero.Y + 14 <= plot.Bottom,
        "The zero-time label must remain readable inside the bottom edge.");
    var canvasPlot = ui.View.CanvasPlotBounds;
    True(ui.Canvas.Lines.Any(l => l.X1 == plot.X && l.X2 == plot.Right
        && l.Y1 == plot.Bottom && l.Y2 == plot.Bottom), "The zero-time grid line is not at the bottom.");
    var head = ui.Canvas.Lines.Single(l => l.Color == 0xF2C66D && l.X1 == canvasPlot.X
        && l.X2 == canvasPlot.Right && l.Y1 == l.Y2);
    Near(plot.Bottom - ui.View.PlayheadMs * ui.View.PixelsPerMs, head.Y1);
}

static void UpwardClickTime()
{
    var ui = new Ui();
    ui.ClickText("自由");
    var plot = ui.Plot;
    ui.Click(plot.Right - 10, plot.Bottom - 100);
    double earlier = ui.View.PlayheadMs;
    Near(100 / ui.View.PixelsPerMs, earlier);
    double expectedLater = ui.View.ViewStartMs + 250 / ui.View.PixelsPerMs;
    ui.Click(plot.Right - 10, plot.Bottom - 250);
    True(ui.View.PlayheadMs > earlier, "A higher canvas click did not select a later time.");
    Near(expectedLater, ui.View.PlayheadMs);
    True(!ui.View.IsDirty, "Time navigation mutated the map.");
}

static void WheelAndPan()
{
    var ui = new Ui();
    var plot = ui.Plot;
    float x = plot.X + plot.Width / 2, y = plot.Y + plot.Height / 2;
    ui.View.Wheel(x, y, 360, false);
    ui.Paint();
    True(ui.View.ViewStartMs > 0, "Wheel up from the start did not reveal later times.");
    var before = ui.PaintedFruitAtX(128);
    double start = ui.View.ViewStartMs;
    ui.View.PointerDown(x, y, 1, false, false);
    True(ui.View.WantsCapture, "Middle-button pan did not request capture.");
    ui.View.PointerMove(x, y + 45, false, false);
    ui.Paint();
    var moved = ui.PaintedFruitAtX(128);
    Near(before.Y + 45, moved.Y);
    True(ui.View.ViewStartMs > start, "Dragging the upward timeline down did not expose later times.");
    ui.View.PointerUp(x, y + 45, 1);
    ui.Paint();
    Near(moved.Y, ui.PaintedFruitAtX(128).Y);
    True(!ui.View.WantsCapture && !ui.View.IsDirty, "Panning retained capture or edited the map.");
}

static void OverviewWheel()
{
    foreach (var (ready, playing) in new[] { (false, false), (true, false), (true, true) })
    {
        var ui = new Ui();
        var before = ui.View.Document.DeepClone();
        var seeks = new List<double>();
        int toggles = 0;
        ui.View.RequestSeek = seeks.Add;
        ui.View.RequestTogglePlayback = () => toggles++;
        ui.View.UpdateTransport(5000, ready ? 60000 : 0, ready, playing, false, null, ready ? "song.mp3" : null);
        ui.Paint();
        float x = 700, y = ui.Canvas.Texts.Single(t => t.Value == "时间导航").Y + 50;
        double start = ui.View.PlayheadMs;
        double scale = ui.View.PixelsPerMs;
        ui.View.Wheel(x, y, 30, false); ui.Paint();
        Near(start + 0.25 * 78 / scale, ui.View.PlayheadMs);
        Near(ui.View.PlayheadMs, seeks.Single());
        Near(ui.View.PlayheadMs - ui.Plot.Height * 0.25 / scale, ui.View.ViewStartMs);
        ui.View.Wheel(x, y, -30, false); ui.Paint();
        Near(start, ui.View.PlayheadMs);
        Near(scale, ui.View.PixelsPerMs);
        ui.View.Wheel(x, y, -120000, false); ui.Paint(); Near(0, ui.View.PlayheadMs);
        ui.View.Wheel(x, y, 120000, false); ui.Paint(); Near(ui.View.TimelineDurationMs, ui.View.PlayheadMs);
        True(ui.View.AudioPlaying == playing && toggles == 0, "Overview wheel changed playback intent");
        True(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Overview wheel edited the map");
        ui.Key('Z', ctrl: true);
        True(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Navigation entered undo history");
    }
}

static void ZoomPaintedAnchor()
{
    var ui = new Ui();
    var plot = ui.Plot;
    ui.View.Wheel(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, 720, false);
    ui.Paint();
    True(ui.View.ViewStartMs > 1000 && ui.View.ViewStartMs < 20000, "Zoom setup reached a viewport clamp.");
    var anchor = ui.PaintedFruitAtX(160);
    double initialScale = ui.View.PixelsPerMs;
    foreach (float delta in new[] { 120f, 120f, -120f, -120f })
    {
        double previousScale = ui.View.PixelsPerMs;
        ui.View.Wheel(anchor.X, anchor.Y, delta, true);
        ui.Paint();
        True(delta > 0 ? ui.View.PixelsPerMs > previousScale : ui.View.PixelsPerMs < previousScale,
            "Control-wheel did not change zoom in the requested direction.");
        var after = ui.PaintedFruitAtX(160);
        Near(anchor.X, after.X);
        Near(anchor.Y, after.Y);
        True(ui.View.ViewStartMs > 1000 && ui.View.ViewStartMs < 20000, "Zoom hit a viewport clamp.");
    }
    Near(initialScale, ui.View.PixelsPerMs);
    True(!ui.View.IsDirty, "Viewport zoom edited the map.");
}

static void RestoreArViewport()
{
    var ui = new Ui();
    string original = Snapshot(ui);
    ui.ClickText("还原 AR 比例");
    Near(440.0 / 750 * (ui.Plot.Width / 512), ui.View.PixelsPerMs);
    Near(ui.View.PlayheadMs - ui.Plot.Height * 0.25 / ui.View.PixelsPerMs, ui.View.ViewStartMs);
    double restoredScale = ui.View.PixelsPerMs;
    var plot = ui.Plot;
    ui.View.Wheel(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, -120, true);
    ui.Paint();
    True(ui.View.PixelsPerMs < restoredScale, "Manual zoom could not leave AR scale.");
    ui.ClickText("还原 AR 比例");
    Near(restoredScale, ui.View.PixelsPerMs);
    Near(ui.View.PlayheadMs - ui.Plot.Height * 0.25 / ui.View.PixelsPerMs, ui.View.ViewStartMs);
    True(Snapshot(ui) == original && !ui.View.IsDirty, "AR viewport restoration edited the map.");
    ui.Key('Z', ctrl: true);
    True(Snapshot(ui) == original && !ui.View.IsDirty, "AR viewport restoration changed undo history.");
}

static void ArPreviewAndInput()
{
    var ui = new Ui();
    ui.View.Document.Tracks.Clear();
    var fixtures = new (double Remaining, double X)[]
    {
        (-1, 22), (225, 42), (451, 62), (450, 82), (0, 102),
        (900, 142), (1800, 182), (1801, 202)
    };
    foreach (var fixture in fixtures)
        ui.View.Document.Fruits.Add(new Fruit { TimeMs = ui.View.PlayheadMs + fixture.Remaining, X = fixture.X });
    ui.Paint();
    string original = Snapshot(ui);
    ui.SetAr("10");
    Near(10, ui.View.Document.ApproachRate);
    AssertArPreview(ui, 450, fixtures, 42, 82);
    ui.SetAr("0");
    Near(0, ui.View.Document.ApproachRate);
    foreach (float height in new[] { 900f, 800f, 1050f })
    {
        ui.Resize(1440, height);
        AssertArPreview(ui, 1800, fixtures, 142, 182);
    }
    string arZero = Snapshot(ui);
    foreach (string invalid in new[] { "-1", "11" })
    {
        ui.SetAr(invalid);
        Near(0, ui.View.Document.ApproachRate);
        True(Snapshot(ui) == arZero, "An out-of-range AR changed the document.");
        ui.Key(27);
    }
    ui.Key('Z', ctrl: true);
    Near(10, ui.View.Document.ApproachRate);
    ui.Key('Z', ctrl: true);
    True(Snapshot(ui) == original, "AR undo did not restore the original numeric value and fixture objects.");
    ui.Key('Y', ctrl: true);
    Near(10, ui.View.Document.ApproachRate);
    ui.Key('Y', ctrl: true);
    True(Snapshot(ui) == arZero, "AR redo did not restore the accepted document.");
}

static void AssertArPreview(Ui ui, double preempt, (double Remaining, double X)[] fixtures, double halfX, double topX)
{
    var catchLine = ui.Canvas.Lines.Single(l => l.Color == 0x677085);
    var field = ui.Canvas.Outlines.Single(s => s.Color == 0x2B3442 && s.Bounds.X > ui.Plot.Right).Bounds;
    Near(440.0 / 512, field.Height / field.Width);
    Near(field.Bottom, catchLine.Y1);
    var fruits = ui.Canvas.Circles.Where(c => c.Filled && c.Color == 0xFFFFFF && c.X > ui.Plot.Right
        && Math.Abs(c.Radius - CatchSize.FruitRadius(ui.View.Document.CircleSize) * field.Width / 512) < 0.001).ToArray();
    int expectedCount = ui.View.Document.Fruits.Count(f => f.TimeMs >= ui.View.PlayheadMs
        && f.TimeMs <= ui.View.PlayheadMs + preempt);
    True(fruits.Length == expectedCount, "Preview includes fruit outside the AR preempt window.");
    foreach (var fixture in fixtures)
    {
        float expectedX = catchLine.X1 + (float)(fixture.X / 512) * (catchLine.X2 - catchLine.X1);
        var matches = fruits.Where(f => Math.Abs(f.X - expectedX) < 0.001).ToArray();
        bool visible = fixture.Remaining >= 0 && fixture.Remaining <= preempt;
        True(matches.Length == (visible ? 1 : 0), $"Incorrect preview visibility for fruit at offset {fixture.Remaining}.");
    }
    RecordingCanvas.Dot AtX(double x) => fruits.Single(f => Math.Abs(f.X
        - (catchLine.X1 + x / 512 * (catchLine.X2 - catchLine.X1))) < 0.001);
    Near(field.Y + field.Height / 2, AtX(halfX).Y);
    Near(field.Y, AtX(topX).Y);
    Near(catchLine.Y1, AtX(102).Y);
}

static void ArScaleResize()
{
    var ui = new Ui();
    ui.ClickMap(2500, 392);
    ui.ClickMap(2500, 392);
    string original = Snapshot(ui);
    ui.Resize(980, 620);
    True(ui.Canvas.Texts.All(t => t.Value != "Catch 预览"), "Resize fixture did not hide the preview.");
    ui.ClickText("还原 AR 比例");
    Near(440.0 / 750 * (ui.Plot.Width / 512), ui.View.PixelsPerMs);
    Near(ui.View.PlayheadMs - ui.Plot.Height * 0.25 / ui.View.PixelsPerMs, ui.View.ViewStartMs);
    double narrowScale = ui.View.PixelsPerMs;
    ui.Resize(1440, 900);
    Near(440.0 / 750 * (ui.Plot.Width / 512), ui.View.PixelsPerMs);
    True(ui.View.PixelsPerMs > narrowScale, "AR scale did not follow a wider canvas.");
    ui.Resize(980, 620);
    Near(narrowScale, ui.View.PixelsPerMs);
    True(Snapshot(ui) == original && !ui.View.IsDirty, "AR scaling during resize edited the map.");
}

static void MainCurveVisibility()
{
    var ui = new Ui();
    string original = Snapshot(ui);
    var curves = CurveCommands(ui, preview: false).Select(c => c.Segment!.Value).ToArray();
    var objects = ObjectCircles(ui, preview: false);
    var previewObjects = ObjectCircles(ui, preview: true);
    True(curves.Length > 0, "Main target curves are not visible by default.");
    AssertObjectKinds(ui, objects, ui.Plot.Width);
    ui.ClickText("隐藏曲线");
    True(!CurveCommands(ui, preview: false).Any(), "Main target lines remained after hiding curves.");
    True(ObjectCircles(ui, preview: false).SequenceEqual(objects), "Hiding curves changed the main converted object sequence.");
    True(ObjectCircles(ui, preview: true).SequenceEqual(previewObjects), "The main curve toggle changed preview objects.");
    ui.ClickText("显示曲线");
    True(CurveCommands(ui, preview: false).Select(c => c.Segment!.Value).SequenceEqual(curves), "Showing curves did not restore the target geometry.");
    True(ObjectCircles(ui, preview: false).SequenceEqual(objects), "Restoring curves changed converted objects.");
    True(Snapshot(ui) == original && !ui.View.IsDirty, "Curve visibility mutated the document or history.");
}

static void PreviewCurveLayers()
{
    var ui = new Ui();
    var objects = ObjectCircles(ui, preview: true);
    True(objects.Length > 0, "Preview has no converted objects.");
    True(!CurveCommands(ui, preview: true).Any(), "Debug curves are visible in the preview by default.");
    ui.ClickText("调试曲线");
    var curves = CurveCommands(ui, preview: true).Select(c => c.Segment!.Value).ToArray();
    True(curves.Length > 0, "The preview debug toggle did not show target curves.");
    True(ObjectCircles(ui, preview: true).SequenceEqual(objects), "Enabling debug curves changed preview objects.");
    AssertPreviewDrawOrder(ui);
    ui.ClickText("隐藏曲线");
    True(!CurveCommands(ui, preview: false).Any(), "The main curve toggle did not hide the main layer.");
    True(CurveCommands(ui, preview: true).Select(c => c.Segment!.Value).SequenceEqual(curves), "Main visibility incorrectly changed the preview debug flag.");
    True(ObjectCircles(ui, preview: true).SequenceEqual(objects), "Main visibility changed the preview object sequence.");
    AssertPreviewDrawOrder(ui);
    ui.ClickText("显示曲线");
    ui.ClickText("调试曲线");
    True(!CurveCommands(ui, preview: true).Any(), "Preview debug curves did not hide again.");
    True(CurveCommands(ui, preview: false).Any(), "Disabling preview curves also hid the main target layer.");
    True(ObjectCircles(ui, preview: true).SequenceEqual(objects), "Disabling debug curves changed preview objects.");
    True(!ui.View.IsDirty, "Debug layer toggles created an edit transaction.");
}

static void CircleSizeAcrossViews()
{
    var ui = new Ui();
    string original = Snapshot(ui);
    double originalCs = ui.View.Document.CircleSize;
    var plot = ui.Plot;
    // Compare objects safely inside the viewport, so size-dependent edge culling cannot change the fixture set.
    float margin = CatchSize.FruitRadius(originalCs) * plot.Width / 512 * 1.5f;
    RecordingCanvas.Dot[] MainInterior() => ObjectCircles(ui, preview: false)
        .Where(c => c.Y > plot.Y + margin && c.Y < plot.Bottom - margin).ToArray();
    var main = MainInterior();
    var preview = ObjectCircles(ui, preview: true);
    var field = ui.Canvas.Outlines.Single(s => s.Color == 0x2B3442 && s.Bounds.X > plot.Right).Bounds;
    AssertObjectKinds(ui, main, plot.Width);
    AssertObjectKinds(ui, preview, field.Width);
    ui.SetCs("7");
    Near(7, ui.View.Document.CircleSize);
    double previewRatio = CatchSize.FruitRadius(7) / CatchSize.FruitRadius(originalCs);
    True(previewRatio < 1, "CS setup did not reduce object size.");
    AssertScaled(main, MainInterior(), previewRatio);
    AssertScaled(preview, ObjectCircles(ui, preview: true), previewRatio);
    True(ui.View.IsDirty, "An accepted CS edit did not dirty the map.");
    ui.Key('Z', ctrl: true);
    Near(originalCs, ui.View.Document.CircleSize);
    True(Snapshot(ui) == original && !ui.View.IsDirty, "One undo did not restore the original CS document.");
    True(MainInterior().SequenceEqual(main), "CS undo did not restore main object sizes.");
    True(ObjectCircles(ui, preview: true).SequenceEqual(preview), "CS undo did not restore preview sizes.");
    ui.Key('Y', ctrl: true);
    AssertScaled(main, MainInterior(), previewRatio);
    AssertScaled(preview, ObjectCircles(ui, preview: true), previewRatio);
}

static void MainCurveSelectionOpacity()
{
    var ui = new Ui();
    string original = Snapshot(ui);
    var objects = ObjectCircles(ui, preview: false);
    True(CurveCommands(ui, preview: false).Any(), "The main curve fixture is empty.");
    AssertMainDrawOrder();
    foreach (var command in CurveCommands(ui, preview: false)) Near(0.5, command.Segment!.Value.Opacity);
    ui.ClickText("调试曲线");
    AssertPreviewLayer();
    // The linear fixture has a distinct colour, so selected and unselected tracks can be distinguished without private state.
    ui.ClickText("02 · Linear zigzag");
    var selected = CurveCommands(ui, preview: false).Where(c => c.Segment!.Value.Color == 0x59D3C3).ToArray();
    var unselected = CurveCommands(ui, preview: false).Where(c => c.Segment!.Value.Color == 0xAB9DF2).ToArray();
    True(selected.Length > 0 && unselected.Length > 0, "The test must contain both selected and unselected curve layers.");
    foreach (var command in selected) Near(1, command.Segment!.Value.Opacity);
    foreach (var command in unselected) Near(0.5, command.Segment!.Value.Opacity);
    True(ObjectCircles(ui, preview: false).SequenceEqual(objects), "Curve selection changed the converted objects.");
    AssertMainDrawOrder();
    AssertPreviewLayer();
    ui.Key(27);
    foreach (var command in CurveCommands(ui, preview: false)) Near(0.5, command.Segment!.Value.Opacity);
    AssertMainDrawOrder();
    AssertPreviewLayer();
    True(Snapshot(ui) == original && !ui.View.IsDirty, "Selection or opacity changes mutated the document.");

    void AssertMainDrawOrder()
    {
        int lastObject = ViewCommands(ui, preview: false).Where(c => c.Dot is { Filled: true, Color: 0xFFFFFF }).Max(c => c.Order);
        int firstCurve = CurveCommands(ui, preview: false).Min(c => c.Order);
        True(lastObject < firstCurve, "A main slider curve was painted beneath converted objects.");
    }
    void AssertPreviewLayer()
    {
        var curves = CurveCommands(ui, preview: true).ToArray();
        True(curves.Length > 0, "The enabled preview debug layer disappeared during selection.");
        foreach (var command in curves) Near(1, command.Segment!.Value.Opacity);
        AssertPreviewDrawOrder(ui);
    }
}

static IEnumerable<RecordingCanvas.Operation> ViewCommands(Ui ui, bool preview)
{
    Rect plot = ui.Plot;
    Rect canvasPlot = ui.View.CanvasPlotBounds;
    return ui.Canvas.Operations.Where(c => c.Clip is { } clip && (preview ? clip.X > plot.Right : clip == canvasPlot));
}

static IEnumerable<RecordingCanvas.Operation> CurveCommands(Ui ui, bool preview)
    => ViewCommands(ui, preview).Where(c => c.Segment is { Color: 0xAB9DF2 or 0x59D3C3 });

static RecordingCanvas.Dot[] ObjectCircles(Ui ui, bool preview)
    => ViewCommands(ui, preview).Where(c => c.Dot is { Filled: true, Color: 0xFFFFFF })
        .Select(c => c.Dot!.Value).ToArray();

static void AssertObjectKinds(Ui ui, RecordingCanvas.Dot[] circles, float fieldWidth)
{
    double cs = ui.View.Document.CircleSize;
    var radii = new[] { CatchSize.FruitRadius(cs), CatchSize.DefaultDropletRadius(cs), CatchSize.DefaultTinyDropletRadius(cs) };
    foreach (float radius in radii)
        True(circles.Any(c => Math.Abs(c.Radius - radius * fieldWidth / 512) < 0.001),
            "The fixture is missing a painted Fruit, Droplet or TinyDroplet type.");
}

static void AssertPreviewDrawOrder(Ui ui)
{
    int lastCurve = CurveCommands(ui, preview: true).Max(c => c.Order);
    int firstObject = ViewCommands(ui, preview: true).Where(c => c.Dot is { Filled: true }).Min(c => c.Order);
    True(lastCurve < firstObject, "Preview target curves were painted over the converted objects.");
}

static void AssertScaled(RecordingCanvas.Dot[] before, RecordingCanvas.Dot[] after, double ratio, bool comparePosition = true)
{
    True(before.Length > 0 && before.Length == after.Length, "CS changed the compared object sequence.");
    for (int i = 0; i < before.Length; i++)
    {
        if (comparePosition)
        {
            Near(before[i].X, after[i].X);
            Near(before[i].Y, after[i].Y);
        }
        Near(before[i].Radius * ratio, after[i].Radius);
    }
}

static string Snapshot(Ui ui) => System.Text.Json.JsonSerializer.Serialize(ui.View.Document);

static void Valid(Ui ui)
{
    var errors = CurveMath.Validate(ui.View.Document);
    True(errors.Count == 0, string.Join("; ", errors));
}

static void PointNear(MapPoint expected, MapPoint actual)
{
    Near(expected.TimeMs, actual.TimeMs);
    Near(expected.X, actual.X);
}

static void Near(double expected, double actual)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.001)
        throw new Exception($"Expected {expected:R}, got {actual:R}.");
}

static void True(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class Ui
{
    private float width = 1440, height = 900;
    public EditorView View { get; } = new();
    public RecordingCanvas Canvas { get; } = new();
    public Ui(bool overview = true)
    {
        Paint();
        if (overview) ShowFixtureOverview();
    }
    public void LoadDocument(MapDocument document)
    {
        View.LoadDocument(document);
        Paint();
        ShowFixtureOverview();
    }
    private void ShowFixtureOverview()
    {
        // Multi-second editing fixtures need an overview independent of the application's default AR scale.
        View.Wheel(Plot.X, Plot.Bottom, (float)(120 * Math.Log(0.09 / View.PixelsPerMs) / Math.Log(1.16)), true);
        View.Wheel(Plot.X, Plot.Bottom, (float)(-View.ViewStartMs * View.PixelsPerMs / 78 * 120), false);
        Paint();
    }
    public Fruit Fruit(Guid id) => View.Document.Fruits.Single(f => f.Id == id);
    public Anchor Anchor(Guid id) => View.Document.Tracks.SelectMany(t => t.Nodes).Single(n => n.Id == id);
    public void Paint() { Canvas.Clear(); View.Render(Canvas, width, height); }
    public void Resize(float width, float height) { this.width = width; this.height = height; Paint(); }
    public void SetAr(string value)
    {
        var label = Canvas.Texts.Single(t => t.Value == "AR");
        Click(label.X + 40, label.Y + 5);
        Type(value);
        Key(13);
    }
    public void SetCs(string value)
    {
        var label = Canvas.Texts.Single(t => t.Value == "CS");
        Click(label.X + 40, label.Y + 5);
        Type(value);
        Key(13);
    }
    public void Key(int key, bool ctrl = false, bool shift = false) { View.KeyDown(key, ctrl, shift); Paint(); }
    public void SetSnapDivisor(int divisor)
    {
        int[] values = [4, 5, 6, 7, 8, 9, 12, 16];
        int index = Array.IndexOf(values, divisor);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        var slider = View.SnapSliderBounds;
        float left = slider.X + 7, right = slider.Right - 31;
        float x = left + index / (float)(values.Length - 1) * (right - left);
        View.PointerDown(x, slider.Y + slider.Height / 2, 0, false, false);
        View.PointerUp(x, slider.Y + slider.Height / 2, 0);
        Paint();
    }
    public void Type(string value) { foreach (char c in value) View.TextInput(c); }
    public void ClickFruit(Guid id) { var fruit = Fruit(id); ClickMap(fruit.TimeMs, fruit.X); }
    public void ClickText(string text)
    {
        var label = Canvas.Texts.Single(t => t.Value == text);
        Click(label.X + 4, label.Y + 5);
    }
    public void FocusField(string text)
    {
        var label = Canvas.Texts.Single(t => t.Value == text && t.X > 1000);
        Click(label.X + 100, label.Y + 5);
    }
    public void SetField(string label, string value) { FocusField(label); Type(value); Key(13); }
    public void ClickMap(double time, double x) { var p = Screen(time, x); Click(p.X, p.Y); }
    public void DownMap(double time, double x)
    {
        var p = Screen(time, x); View.PointerDown(p.X, p.Y, 0, false, false); Paint();
    }
    public void MoveMap(double time, double x)
    {
        var p = Screen(time, x); View.PointerMove(p.X, p.Y, false, false); Paint();
    }
    public void UpMap(double time, double x)
    {
        var p = Screen(time, x); View.PointerUp(p.X, p.Y, 0); Paint();
    }
    public void Click(float x, float y)
    {
        View.PointerDown(x, y, 0, false, false); Paint();
        View.PointerUp(x, y, 0); Paint();
    }
    public Rect Plot => View.PlayfieldBounds;
    public RecordingCanvas.Dot PaintedFruitAtX(double mapX)
    {
        float screenX = Plot.X + (float)(mapX / 512) * Plot.Width;
        return Canvas.Circles.Single(c => c.Filled && c.Color == 0xFFFFFF
            && Math.Abs(c.Radius - CatchSize.FruitRadius(View.Document.CircleSize) * Plot.Width / 512) < 0.001
            && Math.Abs(c.X - screenX) < 0.001f);
    }
    private (float X, float Y) Screen(double time, double x)
    {
        var plot = Plot;
        return (plot.X + (float)(x / 512) * plot.Width,
            plot.Bottom - (float)((time - View.ViewStartMs) * View.PixelsPerMs));
    }
}

sealed class RecordingCanvas : ICanvas
{
    public readonly record struct Label(string Value, float X, float Y);
    public readonly record struct Dot(float X, float Y, float Radius, bool Filled, uint Color);
    public readonly record struct Segment(float X1, float Y1, float X2, float Y2, uint Color, float Opacity);
    public readonly record struct Outline(Rect Bounds, uint Color);
    public readonly record struct Operation(int Order, Rect? Clip, Dot? Dot, Segment? Segment);
    private readonly Stack<Rect> clipStack = new();
    public List<Label> Texts { get; } = [];
    public List<Rect> Clips { get; } = [];
    public List<Dot> Circles { get; } = [];
    public List<Segment> Lines { get; } = [];
    public List<Outline> Outlines { get; } = [];
    public List<Operation> Operations { get; } = [];
    public void Clear() { Texts.Clear(); Clips.Clear(); Circles.Clear(); Lines.Clear(); Outlines.Clear(); Operations.Clear(); clipStack.Clear(); }
    public void Fill(Rect r, uint color, float radius = 0) { }
    public void Stroke(Rect r, uint color, float width = 1, float radius = 0) => Outlines.Add(new(r, color));
    public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1)
    {
        var line = new Segment(x1, y1, x2, y2, color, opacity);
        Lines.Add(line);
        Operations.Add(new(Operations.Count, clipStack.TryPeek(out var clip) ? clip : null, null, line));
    }
    public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1)
    {
        var dot = new Dot(x, y, radius, filled, color);
        Circles.Add(dot);
        Operations.Add(new(Operations.Count, clipStack.TryPeek(out var clip) ? clip : null, dot, null));
    }
    public bool Image(string filePath, Rect destination, uint tint = 0xFFFFFF, Rect? source = null) => false;
    public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false)
        => Texts.Add(new(text, x, y));
    public void Clip(Rect r) { Clips.Add(r); clipStack.Push(r); }
    public void Unclip() { if (clipStack.Count > 0) clipStack.Pop(); }
}
