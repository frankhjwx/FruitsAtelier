using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingPanelTests
{
    private static void StableLabels()
    {
        var map = new MapDocument { DurationMs = 20000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 79 });
        foreach (int time in Enumerable.Range(0, 64).Select(i => i * 250).Concat(new[] { 5999, 10001 }))
            map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,0,2:0:{(time % 750 == 0 ? 11 : time % 750 == 250 ? 88 : 79)}:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
        bool sawVisible = false, sawHidden = false;
        foreach (int time in new[] { 3000, 5000, 6000, 7000, 9500, 10000, 11000 })
        {
            ui.View.UpdateTransport(time, 20000, true, false, false, null, null); ui.Paint();
            var blocks = LaneBlocks(ui, 1);
            Check(blocks.Length > 0, "Dense label fixture has visible sound blocks");
            foreach (var group in blocks.Where(b => b.Width > 0).GroupBy(b => Math.Round(b.Width, 2)))
            {
                var states = group.Select(box => ui.Canvas.Texts.Any(t => t.Value is "79" or "11" or "88" && t.Y >= box.Y && t.Y < box.Bottom
                    && Math.Abs(t.X + ((FruitsAtelier.App.Rendering.ICanvas)ui.Canvas).MeasureText(t.Value, t.Size, true) / 2 - box.X - box.Width / 2) < .1f)).Distinct().ToArray();
                Check(states.Length == 1, "Equal-width indexed blocks have consistent label visibility");
                sawVisible |= states[0]; sawHidden |= !states[0];
                if (group.Key < 12) Check(!states[0], "Crowded blocks hide their labels");
            }
            Check(ui.Canvas.Texts.Where(t => t.Value is "79" or "11" or "88" && t.Y >= blocks[0].Y && t.Y < blocks[0].Bottom && t.X < 1100).All(t => t.Size == 11), "Sound labels retain their readable fixed font size");
        }
        Check(sawVisible && sawHidden, "Fixture exercises both readable and crowded labels");
    }

    public static void Editing()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1, Volume = 80 });
        foreach (int time in new[] { 1000, 1250, 1500 }) map.Fruits.Add(new() { TimeMs = time, X = 256 });
        var ui = new Ui(overview: false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        foreach (bool left in new[] { false, true })
        {
            double beforeToggle = ui.View.PlayheadMs;
            var toggle = ui.View.DockToggleBounds(left); ui.Click(toggle.X + 10, toggle.Y + 10);
            var workspace = ui.View.CaptureDockLayouts().Hitsounding!;
            Check((left ? workspace.Left : workspace.Right)!.Collapsed, "Dock collapse has priority over the Hitsounding canvas");
            toggle = ui.View.DockToggleBounds(left); ui.Click(toggle.X + 10, toggle.Y + 10);
            workspace = ui.View.CaptureDockLayouts().Hitsounding!;
            Check(!(left ? workspace.Left : workspace.Right)!.Collapsed && ui.View.PlayheadMs == beforeToggle && baseline.ContentEquals(ui.View.Document), "Dock expand does not seek, select or edit underlying content");
        }
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        float emptyX = NormalBlocks().Last().X + 40, emptyY = LaneY(6);
        foreach (float y in new[] { emptyY, 145f })
        {
            ui.View.UpdateTransport(1000, 10000, true, true, false, null, null); ui.Paint();
            ui.Click(emptyX, y);
            Check(ui.View.PlayheadMs == 1000 && ui.View.AudioPlaying, "Playing blank grid and ruler clicks preserve playback position and state");
            ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
            ui.Click(emptyX, y);
            Check(ui.View.PlayheadMs == 1000, "Paused blank grid and ruler clicks preserve the playhead");
        }
        Check(baseline.ContentEquals(ui.View.Document), "Blank clicks do not edit beatmap content");
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        var block = NormalBlocks()[0];
        ui.Click(block.X + block.Width / 2, block.Y + block.Height / 2);
        Check(ui.View.PlayheadMs == 1000, "Selecting a sound preserves transport position");
        var laneLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane1") && t.X > 340 && t.X < 1100);
        ui.Click(laneLabel.X + 4, laneLabel.Y + 4);
        Check(ui.View.PlayheadMs == 1000, "Track label clicks never seek");
        ui.View.LibrarySettings.SongVolume = ui.View.LibrarySettings.HitsoundVolume = 100;
        ui.View.OpenVolumePopover(); ui.Paint();
        var masterBar = ui.View.VolumeBarBounds(0);
        ui.View.PointerDown(masterBar.X + masterBar.Width / 2, masterBar.Y + masterBar.Height / 2, 0, false, false);
        ui.View.PointerMove(masterBar.X + masterBar.Width / 2, masterBar.Y + masterBar.Height / 2, false, false);
        ui.View.PointerUp(masterBar.X + masterBar.Width / 2, masterBar.Y + masterBar.Height / 2, 0); ui.Paint();
        Check(ui.View.LibrarySettings.MasterVolume == 50, "Hitsounding volume popover receives mouse dragging");
        ui.View.SetModifiers(true, false); ui.Key(39); ui.View.KeyUp(39); ui.Key(40); ui.View.KeyUp(40);
        Check(ui.View.LibrarySettings.SongVolume == 95, "Hitsounding volume popover receives keyboard channel and gain shortcuts");
        ui.Key(39); ui.View.KeyUp(39); ui.Key(40); ui.View.KeyUp(40); ui.View.SetModifiers(false, false);
        Check(ui.View.LibrarySettings.HitsoundVolume == 95, "Keyboard controls reach Effect in Hitsounding");
        var effectBar = ui.View.VolumeBarBounds(2);
        ui.View.Wheel(effectBar.X + 10, effectBar.Y + 10, 120, false, false, false); ui.Paint();
        Check(ui.View.LibrarySettings.HitsoundVolume == 100 && ui.View.PlayheadMs == 1000, "Volume wheel has priority over timeline seeking");
        ui.Key(27); Check(!ui.View.VolumePopoverVisible && !ui.View.LibraryVisible, "Escape closes the popover before leaving the editor");

        Check(ui.Canvas.Texts.Count(t => t.Value == L.Get("hitsounding.index") && t.X > 1100) == 1, "Timing Helper exposes only the timing sample index");
        ui.View.Wheel(block.X, block.Y, -120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1125, "One wheel notch advances one quarter-beat snap");
        ui.View.Wheel(block.X, block.Y, 120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1000 && baseline.ContentEquals(ui.View.Document), "Reverse wheel restores time without editing content");
        ui.View.UpdateTransport(1030, 10000, true, false, false, null, null); ui.Paint();
        ui.View.Wheel(block.X, block.Y, -120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1125, "Forward wheel first aligns an off-grid timestamp to the next snap");
        ui.View.Wheel(block.X, block.Y, -120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1250, "Subsequent forward wheel follows the snap grid");
        ui.View.UpdateTransport(1030, 10000, true, false, false, null, null); ui.Paint();
        ui.View.Wheel(block.X, block.Y, 120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1000, "Backward wheel first aligns to the preceding snap");

        var volumeLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.volume") && t.X > 1100);
        ui.Click(volumeLabel.X + 95, volumeLabel.Y + 5); ui.Key(65, ctrl: true); ui.Type("40"); ui.Key(13);
        Check(baseline.ContentEquals(ui.View.Document), "Timing draft is not automatically applied");
        ui.ClickText(L.Get("hitsounding.applyTiming"));
        Check(ui.View.Document.TimingPoints.Last() is { Uninherited: false, TimeMs: 1000, Volume: 40 }, "Apply inserts an inherited timing point at the note timestamp");
        ui.Key(90, ctrl: true);
        ui.ClickText(L.Get("hitsounding.draw"));
        var normal = NormalBlocks();
        float whistleY = LaneY(2), startX = normal[1].X + normal[1].Width / 2;
        ui.Click(startX, whistleY);
        Check(ui.View.Document.HitsoundOverrides.Count == 1 && ui.View.Document.HitsoundOverrides[0].Sample.Additions == 2, "Draw adds a whistle with one click");
        ui.Key(90, ctrl: true);
        normal = NormalBlocks(); startX = normal[1].X + normal[1].Width / 2;
        float finishX = normal[2].X + normal[2].Width / 2;
        ui.View.PointerDown(startX, whistleY, 0, false, false);
        ui.View.PointerMove(finishX, whistleY, false, false); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "A paint stroke previews without rebuilding committed content");
        Check(ui.Canvas.PaintCalls.Any(p => p.Color == 0x65BF8A && p.FillBounds is not null && p.Opacity == .5f), "Draw previews use half opacity");
        ui.View.PointerUp(finishX, whistleY, 0); ui.Paint();
        Check(ui.View.Document.HitsoundOverrides.Count == 2, "A paint stroke covers crossed notes");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "One undo reverses the complete paint stroke");
        ui.Click(startX, whistleY); ui.Paint();
        var whistle = LaneBlocks(ui, 2).First();
        ui.View.PointerDown(whistle.X + whistle.Width / 2, whistle.Y + 10, 2, false, false);
        ui.View.PointerUp(whistle.X + whistle.Width / 2, whistle.Y + 10, 2); ui.Paint();
        Check(ui.View.Document.HitsoundOverrides.All(s => (s.Sample.Additions & 2) == 0), "Right click deletes only the clicked addition");
        ui.ClickText(L.Get("hitsounding.select"));
        normal = NormalBlocks();
        ui.Click(normal[1].X + normal[1].Width / 2, normal[1].Y - 10);
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.currentTime")), "Clicking empty lane space does not select a nearby note");
        ui.View.UpdateTransport(1000, 10000, true, true, false, null, null); ui.Paint();
        Check(ui.Canvas.PaintCalls.Any(p => p.Color == 0xFFFFFF && p.FillBounds is not null && p.Opacity > 0 && p.Opacity < 1), "Audible events flash during playback");
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        Check(!ui.Canvas.PaintCalls.Any(p => p.Color == 0xFFFFFF && p.FillBounds is not null && p.Opacity > 0 && p.Opacity < 1), "Paused notes do not flash");
        double timeBefore = ui.View.PlayheadMs;
        var head = ui.Canvas.Lines.Single(l => l.Color == 0xE9CA64 && l.Y1 == 130);
        ui.View.PointerDown(head.X1, 137, 0, false, false);
        ui.View.PointerMove(-500, 137, false, false); ui.Paint();
        var leftHead = ui.Canvas.Lines.Single(l => l.Color == 0xFFF0A6 && l.Y1 == 130);
        ui.View.PointerMove(2500, 137, false, false); ui.Paint();
        var rightHead = ui.Canvas.Lines.Single(l => l.Color == 0xFFF0A6 && l.Y1 == 130);
        ui.View.PointerUp(2500, 137, 0); ui.Paint();
        Check(timeBefore == ui.View.PlayheadMs && leftHead.X1 < rightHead.X1, "Dragging the play line changes its viewport position without seeking");
        ui.View.Wheel(rightHead.X1, 260, 12000, false, false, true); ui.Paint();
        var clampedHead = ui.Canvas.Lines.Single(l => l.Color == 0xE9CA64 && l.Y1 == 130);
        Check(clampedHead.X1 >= leftHead.X1 - .1 && clampedHead.X1 <= rightHead.X1 + .1, "Zoom keeps the play line inside its 5–95 percent boundaries");
        var interval = ui.Canvas.Fills.First(f => f.Color == 0xE77785).Bounds;
        Check(ui.Canvas.Texts.Any(t => t.Value == "S:1" && Math.Abs(t.X - interval.X - 6) < .1), "Timing text remains at the visible interval edge after its start leaves the canvas");
        PlaybackLineTests.Run();
        Console.WriteLine("PASS Timing Helper drafts, event editing, paint undo, exact selection, playback feedback and play-line clamps");

        FruitsAtelier.App.Rendering.Rect[] NormalBlocks() => LaneBlocks(ui, 1);
        float LaneY(int lane) { var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100); return label.Y + 8; }

    }
    public static void SelectionAndTiming()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1, Volume = 80 });
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = 1, Volume = 60 });
        map.TimingPoints.Add(new() { TimeMs = 1750, BeatLengthMs = -100, Uninherited = false, SampleSet = 3, SampleIndex = 2, Volume = 70 });
        foreach (int time in new[] { 1000, 1250, 2000, 2250 }) map.Fruits.Add(new() { TimeMs = time, X = 256,
            OriginalLine = $"256,192,{time},1,{(time < 1750 ? 6 : 0)},0:0:0:0:" });
        var ui = new Ui(overview: false); ui.LoadDocument(map); ui.Resize(1440, 1300); ui.View.ShowHitsoundingPage(); ui.Paint();
        Check(ui.Canvas.Texts.Count(t => t.Value == L.Get("hitsounding.applyExisting")) == 2, "Exact red and green points get separate editable sections");
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyTiming")), "Existing lines never offer inserting a duplicate");
        var volumes = ui.Canvas.Texts.Where(t => t.Value == L.Get("hitsounding.volume") && t.X > 1100).OrderBy(t => t.Y).ToArray();
        ui.Click(volumes[1].X + 115, volumes[1].Y + 5); ui.Key(65, ctrl: true); ui.Type("35"); ui.Key(13);
        var apply = ui.Canvas.Texts.Where(t => t.Value == L.Get("hitsounding.applyExisting")).OrderBy(t => t.Y).Last();
        ui.Click(apply.X + 8, apply.Y + 8);
        Check(ui.View.Document.TimingPoints.Count == 3 && ui.View.Document.TimingPoints[0].Volume == 80 && ui.View.Document.TimingPoints[1].Volume == 35,
            "Applying the second section modifies only the existing green line");
        ui.Key(90, ctrl: true);
        var interval = ui.Canvas.Fills.First(f => f.Color == 0x72C686 && f.Bounds.Y < 220).Bounds;
        ui.Click(interval.X + 15, interval.Y + 10);
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.selectedTiming")) && ui.Canvas.Texts.Count(t => t.Value == L.Get("hitsounding.applyExisting")) == 1,
            "Clicking a timing interval selects its own point");
        var whistles = LaneBlocks(ui, 2);
        var finishes = LaneBlocks(ui, 3);
        ui.View.PointerDown(whistles[0].X - 4, whistles[0].Y - 4, 0, false, false);
        ui.View.PointerMove(whistles[1].Right + 4, finishes[1].Bottom + 4, false, false);
        ui.View.PointerUp(whistles[1].Right + 4, finishes[1].Bottom + 4, 0); ui.Paint();
        ui.Key(67, ctrl: true);
        var baseline = ui.View.Document.DeepClone();
        ui.Key(46);
        Check(ui.View.Document.HitsoundOverrides.Count == 2 && ui.View.Document.HitsoundOverrides.All(h => h.Sample.Additions == 0), "Marquee delete removes all selected additions");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Batch delete is one undo step");
        foreach (bool blank in new[] { false, true })
        {
            whistles = LaneBlocks(ui, 2); finishes = LaneBlocks(ui, 3);
            ui.View.PointerDown(whistles[0].X - 4, whistles[0].Y - 4, 0, false, false);
            ui.View.PointerMove(whistles[1].Right + 4, finishes[1].Bottom + 4, false, false);
            ui.View.PointerUp(whistles[1].Right + 4, finishes[1].Bottom + 4, 0); ui.Paint();
            float contextX = whistles[0].X + 5, contextY = blank ? finishes[1].Bottom + 10 : whistles[0].Y + 10;
            ui.View.PointerDown(contextX, contextY, 2, false, false); ui.View.PointerUp(contextX, contextY, 2); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Select right-click preserves the group and waits for its menu action");
            ui.ClickText(L.Get("editor.command.delete"));
            Check(ui.View.Document.HitsoundOverrides.Count == 2 && ui.View.Document.HitsoundOverrides.All(h => h.Sample.Additions == 0), "Context Delete removes every selected addition");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Context group deletion is one undo step");
            ui.Key(89, ctrl: true); Check(ui.View.Document.HitsoundOverrides.Count == 2, "Context group deletion is one redo step");
            ui.Key(90, ctrl: true);
        }
        ui.View.UpdateTransport(2000, 6000, true, false, false, null, null); ui.Paint();
        ui.Key(86, ctrl: true);
        Check(ui.View.DiscardConfirmationVisible && baseline.ContentEquals(ui.View.Document), "Mismatched paste waits for a decision");
        ui.ClickText(L.Get("mac.cancel")); Check(baseline.ContentEquals(ui.View.Document), "Cancel paste leaves the map unchanged");
        ui.Key(86, ctrl: true); ui.ClickText(L.Get("hitsounding.pasteKeep"));
        Check(ui.View.Document.HitsoundOverrides.Count == 2 && ui.View.Document.HitsoundOverrides.All(h => h.Sample.Additions == 6 && h.Sample.Index == 2 && h.Sample.AdditionSet == 3),
            "Paste preserves relative times and can retain target samplesets");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Paste is one undo step");
        ui.Key(86, ctrl: true); ui.ClickText(L.Get("hitsounding.pasteAdjust"));
        Check(ui.View.Document.HitsoundOverrides.All(h => h.Sample.Index == 1 && h.Sample.AdditionSet == 2), "Paste can adopt source bank and index");
        Check(ui.View.Document.TimingPoints.Count == 3, "Paste adjustments do not rewrite unrelated timing intervals");
        Console.WriteLine("PASS Hitsounding marquee clipboard, sampleset confirmation and existing timing-line editing");
    }
    public static void PlayingSelectionAndSettings()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1 });
        foreach (int time in new[] { 1000, 1250, 1500, 1750 })
            map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,2,0:0:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
        ui.View.UpdateTransport(1000, 6000, true, true, false, null, null); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        var first = LaneBlocks(ui, 2)[0];
        float x = first.Right + 4, y = first.Bottom + 4;
        ui.View.PointerDown(first.X - 4, first.Y - 4, 0, false, false);
        ui.View.PointerMove(x, y, false, false); ui.Paint();
        var before = ui.Canvas.PaintCalls.Single(p => p.FillBounds is not null && p.Opacity == .15f).FillBounds!.Value;
        ui.View.UpdateTransport(1500, 6000, true, true, false, null, null); ui.Paint();
        var after = ui.Canvas.PaintCalls.Single(p => p.FillBounds is not null && p.Opacity == .15f).FillBounds!.Value;
        Check(after.X < before.X && Math.Abs(after.Right - before.Right) < .01, "Playing marquee keeps its time anchor while its pointer edge stays fixed");
        ui.View.UpdateTransport(3500, 6000, true, true, false, null, null); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Playing marquee does not modify content");
        ui.View.PointerUp(x, y, 0); ui.Paint(); ui.Key(46);
        Check(ui.View.Document.HitsoundOverrides.Count == 4 && ui.View.Document.HitsoundOverrides.All(h => h.Sample.Additions == 0), "Stationary-pointer playback updates selection and retains sounds that scrolled offscreen");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Playing marquee delete is one undo step");
        ui.View.UpdateTransport(1000, 6000, true, false, false, null, null); ui.Paint();
        var blocks = LaneBlocks(ui, 1); var layout = System.Text.Json.JsonSerializer.Serialize(ui.View.CaptureDockLayouts());
        ui.View.OpenVolumePopover(); ui.Paint();
        int volume = ui.View.LibrarySettings.MasterVolume;
        ui.View.OpenSettings(); ui.Paint();
        Check(!ui.View.VolumePopoverVisible, "Settings takes ownership from the volume popover");
        foreach (float wheelX in new[] { blocks[0].X, 20f })
        {
            ui.View.Wheel(wheelX, first.Y, -120, false, false, false);
            ui.View.Wheel(wheelX, first.Y, 120, false, false, true);
        }
        ui.Key(37); ui.Key(46);
        ui.View.SetModifiers(true, false); ui.Key(38); ui.View.KeyUp(38); ui.View.SetModifiers(false, false);
        Check(ui.View.PlayheadMs == 1000 && ui.View.LibrarySettings.MasterVolume == volume && baseline.ContentEquals(ui.View.Document), "Settings blocks underlying seek, edit and volume shortcuts");
        ui.Key(27); ui.Paint();
        Check(LaneBlocks(ui, 1).SequenceEqual(blocks) && System.Text.Json.JsonSerializer.Serialize(ui.View.CaptureDockLayouts()) == layout,
            "Settings wheel input cannot zoom or scroll the underlying Hitsounding canvas or docks");
        Console.WriteLine("PASS Playback-anchored Hitsounding marquee and exclusive Settings input");
    }
    public static void LengthAndMove()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1 });
        foreach (int time in new[] { 0, 250, 1250, 1500 }) map.Fruits.Add(new() { TimeMs = time, X = 256,
            OriginalLine = $"256,192,{time},1,{(time == 250 ? 0 : 2)},0:0:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        ui.View.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 1000).ToArray(), 1));
        ui.ClickText(L.Get("hitsounding.precise")); ui.Paint();
        var whistles = LaneBlocks(ui, 2);
        Check(whistles.Length == 3 && Math.Abs(whistles[0].Width - whistles[1].Width * 4) < .1, "1000 ms whistle ignores intervening notes, while a whistle 250 ms later clamps its predecessor");
        Check(Math.Abs(whistles[1].Right - whistles[2].X) < .1, "Precise length clamps to the next occurrence on the same lane");
        ui.View.UpdateTransport(1500, 6000, true, false, false, null, null); ui.Paint();
        Check(LaneBlocks(ui, 2).Length == 3, "An offscreen-start whistle remains visible across intervening Normal events");

        var movingMap = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        movingMap.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1 });
        movingMap.TimingPoints.Add(new() { TimeMs = 1750, BeatLengthMs = -100, Uninherited = false, SampleSet = 3, SampleIndex = 2 });
        movingMap.Fruits.Add(new() { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1,6,0:0:0:0:" });
        movingMap.Fruits.Add(new() { TimeMs = 2000, X = 256 });
        ui = new Ui(false); ui.LoadDocument(movingMap); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        Drag(); Check(ui.View.DiscardConfirmationVisible && baseline.ContentEquals(ui.View.Document), "Moving a mismatched sound waits for confirmation");
        ui.ClickText(L.Get("mac.cancel")); Check(baseline.ContentEquals(ui.View.Document), "Cancelling a move preserves the source");
        Drag(); ui.ClickText(L.Get("hitsounding.pasteKeep"));
        var changes = ui.View.Document.HitsoundOverrides;
        Check(changes.Single(h => h.SourceId == movingMap.Fruits[0].Id).Sample.Additions == 4 && changes.Single(h => h.SourceId == movingMap.Fruits[1].Id).Sample is { Additions: 2, Index: 2, AdditionSet: 3 }, "Move transfers only the chosen addition and can keep target samplesets");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Move has one undo step");
        var normal = LaneBlocks(ui, 1);
        ui.View.PointerDown(normal[0].X + normal[0].Width / 2, normal[0].Y + 10, 0, false, false);
        ui.View.PointerMove(normal[1].X, normal[0].Y + 10, false, false); ui.View.PointerUp(normal[1].X, normal[0].Y + 10, 0); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Normal cannot be moved by dragging");
        Console.WriteLine("PASS Per-lane Precise HS length, offscreen tails and undoable sound dragging");
        void Drag()
        {
            var source = LaneBlocks(ui, 2).First();
            var destination = LaneBlocks(ui, 1).Last();
            float x = source.X + source.Width / 2, y = source.Y + source.Height / 2, targetX = destination.X + destination.Width / 2;
            ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(targetX, y, false, false); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Drag preview does not modify content");
            ui.View.PointerUp(targetX, y, 0); ui.Paint();
        }
    }
    public static void Run()
    {
        HitsoundingTimingDragTests.Run();
        HitsoundingPreciseAuditionTests.Run();
        StableLabels();
        string root = Path.GetFullPath("artifacts/tests/hitsounding"); Directory.CreateDirectory(root);
        string wave = Path.Combine(root, "soft-hitnormal.wav");
        File.WriteAllBytes(wave, HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, SourcePath = Path.Combine(root, "test.osu") };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true, SampleSet = 2, SampleIndex = 1, Volume = 80 });
        foreach (int time in new[] { 1400, 1450, 1500, 1550 })
            map.TimingPoints.Add(new() { TimeMs = time, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = 0, Volume = time % 100 == 0 ? 60 : 5 });
        map.Fruits.Add(new() { TimeMs = 500, X = 128 }); map.Fruits.Add(new() { TimeMs = 625, X = 256 });
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 128 }, new Anchor { TimeMs = 2500, X = 384 }]);
        map.Tracks.Add(track);
        var ui = new Ui(overview: false); ui.LoadDocument(map); ui.Resize(1440, 900);
        var baseline = ui.View.Document.DeepClone();
        ui.View.ShowHitsoundingPage(); ui.Paint(); ui.SetSnapDivisor(4);
        Check(ui.View.HitsoundingPageVisible, "Hitsounding mode opens");
        var divider = ui.Canvas.Fills.First(f => f.Bounds.Width == 32 && f.Bounds.Height == 3 && f.Bounds.X < 340 && f.Bounds.Y > 200).Bounds;
        ui.View.PointerMove(divider.X + 16, divider.Y + 1, false, false);
        Check(ui.View.HitsoundingSplitResizeCursor, "Config divider hover uses a vertical resize cursor");
        ui.View.PointerDown(divider.X + 16, divider.Y + 1, 0, false, false);
        ui.View.PointerMove(divider.X + 16, divider.Y + 10, false, false);
        Check(ui.View.HitsoundingSplitResizeCursor, "Divider drag retains its resize cursor");
        ui.View.PointerMove(divider.X + 16, divider.Y + 1, false, false);
        ui.View.PointerUp(divider.X + 16, divider.Y + 1, 0); ui.Paint();
        ui.View.PointerMove(500, 200, false, false);
        Check(!ui.View.HitsoundingSplitResizeCursor, "Leaving the divider restores the ordinary cursor");

        Check(!ui.View.IsDirty && baseline.ContentEquals(ui.View.Document), "Mode changes leave content intact");
        for (int i = 0; i < 7; i++) Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.lane" + i)), "Seven lane labels");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x66AAFF && Math.Abs(l.Y2 - l.Y1 - 5) < .01), "Quarter subdivisions use short blue ticks");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xFF6688 && Math.Abs(l.Y2 - l.Y1 - 9) < .01), "Half beats use medium red ticks");
        Check(LaneBlocks(ui, 5).Length > 0, "Actual slider ticks have audio blocks");
        var blocks = LaneBlocks(ui, 1);
        var tagTexts = ui.Canvas.Texts.Where(t => t.X >= blocks[0].X && t.X < blocks[0].Right && t.Y >= blocks[0].Y && t.Y < blocks[0].Bottom).ToArray();
        Check(tagTexts.Select(t => t.Value).SequenceEqual(new[] { "S", "1" }), "Note based displays the bank above its index");
        ui.ClickText(L.Get("hitsounding.showText"));
        Check(!ui.Canvas.Texts.Any(t => t.X >= blocks[0].X && t.X < blocks[0].Right && t.Y >= blocks[0].Y && t.Y < blocks[0].Bottom), "HS text toggle hides bank and index in Note based");
        Check(LaneBlocks(ui, 1).SequenceEqual(blocks) && baseline.ContentEquals(ui.View.Document), "Text visibility preserves tag geometry and map content");
        var savedTextLayout = ui.View.CaptureDockLayouts();
        Check(!savedTextLayout.Hitsounding!.ShowHitsoundText, "HS text visibility is captured in the workspace layout");
        ui.ClickText(L.Get("hitsounding.showText"));
        ui.View.LibrarySettings.DockLayouts = savedTextLayout; ui.View.RestoreDockLayouts(); ui.Paint();
        Check(!ui.View.CaptureDockLayouts().Hitsounding!.ShowHitsoundText, "HS text visibility restores from saved layout");
        ui.ClickText(L.Get("hitsounding.showText"));
        Check(blocks.Length >= 2 && blocks.All(b => b.Width <= EditorView.HitsoundingTagWidth), "Sound tags retain the preferred width or shrink to their neighbours");
        Check(ui.Canvas.Circles.Any(d => Math.Abs(blocks[0].X + blocks[0].Width / 2 - d.X) < .1), "Sound tag centers match their note timestamps");
        Check(ui.Canvas.Texts.Any(t => t.Value == "1" && t.Y > 218), "Notes reuse numbered mapping timeline markers");
        float anchorX = blocks[0].X + blocks[0].Width / 2, anchorY = blocks[0].Y + 5;
        ui.View.Wheel(anchorX, anchorY, 120, false, false, true); ui.Paint();
        var zoomed = LaneBlocks(ui, 1);
        Check(Math.Abs(zoomed[0].X + zoomed[0].Width / 2 - blocks[0].X - blocks[0].Width / 2) < .1 && zoomed.All(b => b.Width <= EditorView.HitsoundingTagWidth), "Alt-wheel keeps the hovered time and fixed tag width");
        Check(zoomed[1].X - zoomed[0].X > blocks[1].X - blocks[0].X, "Alt-wheel expands temporal spacing");
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Zoom leaves content and history intact");
        Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("S:0") || t.Value.Contains("N:0") || t.Value.Contains("D:0")), "Default banks omit index zero everywhere");
        var timingLabels = ui.Canvas.Fills.Where(f => f.Color is 0x72C686 or 0xE77785).Select(f => f.Bounds).ToArray();
        Check(timingLabels.Select(r => r.Y).Distinct().Count() == 1, "Timing labels share one row");
        Check(timingLabels.Zip(timingLabels.Skip(1)).All(pair => pair.First.X <= pair.Second.X), "Later timing labels paint on top of earlier labels");
        var conversion = ui.View.Conversion;
        ui.View.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.001f, 400).ToArray(), 1));
        ui.ClickText(L.Get("hitsounding.precise"));
        var precise = LaneBlocks(ui, 1);
        Check(Math.Abs(precise[0].Right - precise[1].X) < .1, "Precise HS uses decoded length and the next Normal boundary");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x65BF8A && l.X1 >= precise[0].X && l.X1 < precise[0].Right
            && l.Y1 > precise[0].Y && l.Y2 < precise[0].Bottom && l.Y2 - l.Y1 > 10), "Quiet waveforms normalize inside narrow precise tags");
        ui.ClickText(L.Get("hitsounding.showText"));
        Check(!ui.Canvas.Texts.Any(t => t.X >= precise[0].X && t.X < precise[0].Right && t.Y >= precise[0].Y && t.Y < precise[0].Bottom)
            && ui.Canvas.Lines.Any(l => l.Color == 0x65BF8A && l.X1 >= precise[0].X && l.X1 < precise[0].Right && l.Y1 > precise[0].Y && l.Y2 < precise[0].Bottom), "Precise HS hides text while retaining its waveform");
        ui.ClickText(L.Get("hitsounding.showText"));
        Check(ui.View.CaptureDockLayouts().Hitsounding!.PreciseHitsounds, "Display mode is captured in the Hitsounding layout");
        ui.ClickText(L.Get("hitsounding.noteBased"));
        Check(ReferenceEquals(conversion, ui.View.Conversion) && !ui.View.IsDirty, "Display modes reuse conversion without content history");
        ui.View.Wheel(anchorX, anchorY, -600, false, false, true); ui.Paint();
        var dense = LaneBlocks(ui, 1);
        Check(dense.Any(b => b.Width < EditorView.HitsoundingTagWidth), "Dense tags shrink");
        Check(dense.Zip(dense.Skip(1)).All(pair => pair.First.Right <= pair.Second.X + .01f), "Dense note-based tags never overlap");
        ui.View.Wheel(anchorX, anchorY, 600, false, false, true); ui.Paint();
        var layouts = ui.View.CaptureDockLayouts();
        Check(layouts.Hitsounding!.Left!.Panels!.SequenceEqual(["HitsoundingConfig"]), "Split layout persists independently");
        ui.View.LibrarySettings.DockLayouts = layouts; ui.View.RestoreDockLayouts(); ui.Paint();
        Check(ui.View.CaptureDockLayouts().Hitsounding!.Left!.Panels!.SequenceEqual(["HitsoundingConfig"]), "Split layout restores");
        layouts.Hitsounding.Left.Panels = ["HitsoundLibrary", "MapSamplesets", "MovementStrain", "PatternGallery"];
        layouts.Hitsounding.Left.Active = "HitsoundLibrary";
        ui.View.LibrarySettings.DockLayouts = layouts; ui.View.RestoreDockLayouts(); ui.Paint();
        Check(ui.View.CaptureDockLayouts().Hitsounding!.Left!.Panels!.SequenceEqual(["HitsoundingConfig"]), "Previous separate panels migrate into one config dock");
        ui.ClickText(L.Get("hitsounding.panel") + " ▾"); ui.ClickText(L.Get("timing.detailsPanel"));
        Check(!ui.View.HitsoundingPageVisible && !ui.View.TimingPageVisible, "Mapping mode returns");
        ui.View.ShowHitsoundingPage(); ui.Paint();
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
        ui.View.DropLibraryFiles([wave]); WaitImport();
        string saved = Path.Combine(root, "workspace", "HitsoundLibrary", Path.GetFileName(wave));
        Check(File.Exists(saved), "External audio is copied into workspace library");
        byte[] replacement = HitsoundSamples.CreateWave(new Hitsound(CatchObjectKind.Fruit, null, 1, "hitclap"));
        string source = Path.Combine(root, "replacement.wav"); File.WriteAllBytes(source, replacement);
        var plan = HitsoundResourcePlan.ReplaceSample(root, 2, 1, "hitnormal", source);
        var history = new EditorHistory(map);
        byte[] before = File.ReadAllBytes(wave);
        history.EditResources("sample", forward => { if (forward) plan.Apply(); else plan.Restore(); });
        Check(history.IsDirty && File.ReadAllBytes(wave).SequenceEqual(replacement), "Sample replacement records history");
        history.Undo(); Check(!history.IsDirty && File.ReadAllBytes(wave).SequenceEqual(before), "Undo restores bytes and clean state");
        history.Redo(); Check(history.IsDirty && File.ReadAllBytes(wave).SequenceEqual(replacement), "Redo restores replacement");
        history.Undo();
        ui.View.DropLibraryFiles([source, source]); WaitImport();
        Check(Directory.GetFiles(Path.GetDirectoryName(saved)!).Length == 2, "Library imports deduplicate paths");
        ui.ClickText(L.Get("hitsounding.search"));
        foreach (char ch in "replacement") ui.View.TextInput(ch);
        ui.Paint();
        Check(ui.Canvas.Texts.Count(t => t.Value == "soft-hitnormal.wav") == 1 && ui.Canvas.Texts.Any(t => t.Value == "replacement.wav"), "Library search filters files without hiding map slots");
        ui.Key(65, ctrl: true); ui.Key(8); ui.Key(27); ui.Paint();
        var sourceLabel = ui.Canvas.Texts.Last(t => t.Value == "replacement.wav");
        var slotLabel = ui.Canvas.Texts.First(t => t.Value == "Normal" && t.X < 300);
        ui.View.PointerDown(sourceLabel.X + 4, sourceLabel.Y + 4, 0, false, false);
        ui.View.PointerMove(slotLabel.X + 4, slotLabel.Y + 4, false, false);
        ui.View.PointerUp(slotLabel.X + 4, slotLabel.Y + 4, 0); ui.Paint();
        Check(!ui.View.ErrorVisible && File.ReadAllBytes(wave).SequenceEqual(replacement), "Dragging a library file assigns the sample slot");
        ui.Key(90, ctrl: true); Check(File.ReadAllBytes(wave).SequenceEqual(before), "UI undo restores assigned sample");
        ui.Key(89, ctrl: true); Check(File.ReadAllBytes(wave).SequenceEqual(replacement), "UI redo replaces assigned sample");
        ui.Key(90, ctrl: true);
        string libraryRoot = Path.GetDirectoryName(saved)!;
        string nested = Path.Combine(libraryRoot, "Drums", "Tight"); Directory.CreateDirectory(nested);
        ui.View.ShowHitsoundingPage(); ui.Paint();
        OpenFolder("Drums"); OpenFolder("Tight");
        ui.View.DropLibraryFiles([source]); WaitImport();
        Check(File.Exists(Path.Combine(nested, "replacement.wav")), "Imports target the browsed subfolder");
        ui.View.NavigateHitsoundingDirectory(Path.GetDirectoryName(libraryRoot)); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value.EndsWith(Path.Combine("Drums", "Tight"), StringComparison.Ordinal)), "Navigation cannot escape the audio library root");
        ui.ClickText("←"); Check(ui.Canvas.Texts.Any(t => t.Value == "Tight"), "Parent navigation shows child directories");
        ui.ClickText("⌂"); Check(ui.Canvas.Texts.Any(t => t.Value == "Drums"), "Home navigation returns to the library root");
        string importFolder = Path.Combine(root, "FolderDrop");
        Directory.CreateDirectory(Path.Combine(importFolder, "Nested"));
        File.WriteAllBytes(Path.Combine(importFolder, "Nested", "tap.wav"), replacement);
        File.WriteAllText(Path.Combine(importFolder, "ignore.txt"), "not audio");
        Check(ui.View.CanDropFile(importFolder), "Folder drops are accepted in Hitsounding");
        ui.View.DropLibraryFiles([importFolder]); WaitImport();
        Check(File.Exists(Path.Combine(libraryRoot, "FolderDrop", "Nested", "tap.wav"))
            && !File.Exists(Path.Combine(libraryRoot, "FolderDrop", "ignore.txt")), "Folder imports preserve nested audio only");
        string watchFolder = Path.Combine(libraryRoot, "Watched"); Directory.CreateDirectory(watchFolder);
        ui.View.NavigateHitsoundingDirectory(watchFolder); ui.Paint();
        int audioRefreshes = 0;
        ui.View.RequestPreloadHitsounds = _ => audioRefreshes++;
        ui.View.EnableFileMonitoring();
        try
        {
            string watched = Path.Combine(watchFolder, "watch-test.wav");
            File.WriteAllBytes(watched, replacement);
            WaitFor(() => ui.Canvas.Texts.Any(t => t.Value == "watch-test.wav"));
            int refreshCount = audioRefreshes;
            File.WriteAllBytes(watched, before);
            WaitFor(() => audioRefreshes > refreshCount);
            File.Move(watched, Path.Combine(watchFolder, "watch-renamed.wav"), true);
            WaitFor(() => ui.Canvas.Texts.Any(t => t.Value == "watch-renamed.wav") && !ui.Canvas.Texts.Any(t => t.Value == "watch-test.wav"));
            File.Delete(Path.Combine(watchFolder, "watch-renamed.wav"));
            WaitFor(() => !ui.Canvas.Texts.Any(t => t.Value == "watch-renamed.wav"));
        }
        finally { ui.View.StopFileMonitoring(); }
        ui.View.NavigateHitsoundingDirectory(libraryRoot); ui.Paint();
        string? opened = null;
        ui.View.RequestOpenExternalPath = path => opened = path;
        var search = ui.Canvas.Texts.First(t => t.Value == L.Get("hitsounding.search"));
        ui.View.PointerDown(search.X + 5, search.Y + 5, 2, false, false); ui.Paint();
        ui.ClickText(L.Get("hitsounding.openFolder"));
        Check(opened == libraryRoot, "Library context menu opens the current folder");
        ui.ClickText(L.Get("hitsounding.customSet", 1) + " ▾");
        ui.ClickText(L.Get("hitsounding.default"));
        Check(baseline.ContentEquals(ui.View.Document), "SampleSet selection preserves map content");
        for (int index = 2; index <= 12; index++) map.TimingPoints.Add(new() { TimeMs = index * 100, SampleIndex = index, Uninherited = false, BeatLengthMs = -100 });
        ui.LoadDocument(map); ui.View.ShowHitsoundingPage(); ui.Paint();
        ui.ClickText(L.Get("hitsounding.default") + " ▾");
        Check(ui.Canvas.Texts.Count(t => t.Value.StartsWith("Custom ", StringComparison.Ordinal) && !t.Value.EndsWith("▾")) == 7, "Dropdown displays eight rows including Default");
        var choice = ui.Canvas.Texts.First(t => t.Value == L.Get("hitsounding.customSet", 1));
        ui.View.Wheel(choice.X + 5, choice.Y + 5, -1200, false, false, false); ui.Paint();
        ui.ClickText(L.Get("hitsounding.customSet", 12));
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.customSet", 12) + " ▾"), "Scrollable dropdown selects later timing indices");
        void WaitFor(Func<bool> condition)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do { Thread.Sleep(25); ui.Paint(); if (condition()) return; } while (clock.Elapsed.TotalSeconds < 5);
            throw new TimeoutException("Library watcher did not refresh");
        }
        ui.Resize(980, 620); Check(ui.Canvas.Fills.All(f => float.IsFinite(f.Bounds.Width) && f.Bounds.Width >= 0), "Narrow layout stays valid");
        void OpenFolder(string name)
        {
            var label = ui.Canvas.Texts.Single(t => t.Value == name);
            ui.View.PointerDoubleClick(label.X + 3, label.Y + 3, false, false); ui.Paint();
        }
        void WaitImport()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do { ui.Paint(); if (!ui.View.WaveformNeedsRedraw) return; Thread.Sleep(5); } while (clock.Elapsed.TotalSeconds < 10);
            throw new TimeoutException("Library import did not finish");
        }
        Console.WriteLine("PASS Hitsounding snap ruler, tick audio, centered tags, Alt-wheel zoom, independent split docks, library and resource history");
    }
    public static void StoryboardEditing()
    {
        string root = Path.GetFullPath("artifacts/tests/hitsounding-storyboard"); Directory.CreateDirectory(root);
        string samplePath = Path.Combine(root, "sound,one.wav");
        File.WriteAllBytes(samplePath, HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        string shared = Path.Combine(root, "shared.osb");
        if (File.Exists(shared)) File.Delete(shared);
        var map = new MapDocument { SourcePath = Path.Combine(root, "map.osu"), DurationMs = 6000, BeatLengthMs = 500 };
        var events = new OsuSection { Name = "Events" };
        events.Lines.AddRange(["// Samples", "Sample,500,0,\"sound,one.wav\",65", "Sample,1000,3,\"sound,one.wav\",80"]);
        map.OriginalSections.Add(events);
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        ui.ClickText(L.Get("hitsounding.precise")); Wait(2);
        var baseline = ui.View.Document.DeepClone(); double time = ui.View.PlayheadMs;
        var first = Blocks()[0]; ui.Click(first.X + 5, first.Y + 10);
        Check(ui.View.PlayheadMs == time, "Storyboard selection does not seek");
        ui.Key(67, ctrl: true); ui.View.UpdateTransport(1500, 6000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true); Wait(3);
        Check(Samples().Any(s => s.TimeMs == 1500 && s.Volume == 65 && s.Path == samplePath), "Storyboard paste preserves filename, volume and targets the playhead without requiring a note");
        ui.Key(90, ctrl: true); Wait(2); Check(baseline.ContentEquals(ui.View.Document), "Storyboard paste undo restores source sections");
        first = Blocks()[0]; ui.Click(first.X + 5, first.Y + 10); ui.Key(88, ctrl: true); Wait(1);
        Check(Samples().Single().TimeMs == 1000, "Storyboard cut removes only its selected event");
        ui.Key(90, ctrl: true); Wait(2);
        first = Blocks()[0]; var second = Blocks()[1]; float dx = second.X - first.X;
        ui.View.PointerDown(first.X + 5, first.Y + 10, 0, false, false);
        ui.View.PointerMove(first.X + 5 + dx / 2, first.Y + 10, false, false); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Storyboard drag preview leaves content unchanged");
        ui.View.PointerUp(first.X + 5 + dx / 2, first.Y + 10, 0); Wait(2);
        Check(Samples().Any(s => s.TimeMs == 750 && s.Volume == 65), "Storyboard dragging snaps to an independent timestamp");
        ui.Key(90, ctrl: true); Wait(2); Check(baseline.ContentEquals(ui.View.Document), "Storyboard move is undoable");
        first = Blocks()[0]; second = Blocks()[1];
        ui.View.PointerDown(first.X - 5, first.Y - 5, 0, false, false);
        ui.View.PointerMove(second.Right + 5, second.Bottom + 5, false, false);
        ui.View.PointerUp(second.Right + 5, second.Bottom + 5, 0); ui.Paint();
        ui.View.PointerDown(first.X + 5, first.Y + 10, 2, false, false);
        ui.View.PointerUp(first.X + 5, first.Y + 10, 2); ui.Paint();
        ui.ClickText(L.Get("editor.command.delete")); Wait(0);
        Check(Samples().Count == 0, "Storyboard marquee selects multiple sounds for deletion");
        ui.Key(90, ctrl: true); Wait(2);
        File.WriteAllText(shared, "[Events]\nSample,1250,0,\"sound,one.wav\",40\n");
        ui.LoadDocument(map); ui.View.ShowHitsoundingPage(); Wait(3);
        var last = Blocks().Last(); ui.Click(last.X + 5, last.Y + 10); ui.Key(46); Wait(2);
        Check(!File.ReadAllText(shared).Contains("Sample,"), "Shared storyboard deletion edits the source file");
        ui.Key(90, ctrl: true); Wait(3); Check(File.ReadAllText(shared).Contains("Sample,1250"), "Shared storyboard deletion restores through undo");
        last = Blocks().Last(); first = Blocks()[0];
        float pixelsPer500 = Blocks()[1].X - first.X;
        ui.View.PointerDown(last.X + 5, last.Y + 10, 0, false, false);
        ui.View.PointerMove(last.X + 5 + pixelsPer500, last.Y + 10, false, false);
        ui.View.PointerUp(last.X + 5 + pixelsPer500, last.Y + 10, 0); ui.Paint();
        Check(File.ReadAllText(shared).Contains("Sample,1750") && Samples().Count == 2, "Moving a shared sample retains its shared-file ownership");
        ui.Key(90, ctrl: true); Wait(3);
        Check(File.ReadAllText(shared).Contains("Sample,1250"), "Shared sample movement supports undo");
        ui.Key(89, ctrl: true); ui.Paint(); Check(File.ReadAllText(shared).Contains("Sample,1750"), "Shared sample movement supports redo");
        Console.WriteLine("PASS Storyboard sound selection, clipboard, marquee deletion, snapping, source files and undo");
        FruitsAtelier.App.Rendering.Rect[] Blocks() => ui.Canvas.Outlines.Where(o => o.Color == 0xA57CC7).Select(o => o.Bounds).OrderBy(r => r.X).ToArray();
        IReadOnlyList<StoryboardSample> Samples() => StoryboardReader.Read(string.Join("\n", ui.View.Document.OriginalSections.SelectMany(s => new[] { "[" + s.Name + "]" }.Concat(s.Lines))), null, root).Samples;
        void Wait(int count)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            do { ui.Paint(); if (Blocks().Length == count && (count != 0 || Samples().Count == 0)) return; Thread.Sleep(5); } while (watch.Elapsed.TotalSeconds < 10);
            throw new InvalidOperationException($"Expected {count} storyboard blocks, saw {Blocks().Length}");
        }
    }
    public static void Performance()
    {
        var map = new MapDocument { DurationMs = 90000, BeatLengthMs = 500 };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 20000, BeatLengthMs = 400 },
            new() { TimeMs = 30000, BeatLengthMs = -100, Uninherited = false, SampleIndex = 11 },
            new() { TimeMs = 40000, BeatLengthMs = -50, Uninherited = false }]);
        for (int i = 0; i < 3000; i++) map.Fruits.Add(new() { TimeMs = 200 + i * 20, X = 40 + i * 73 % 430 });
        for (int i = 0; i < 100; i++)
        {
            var slider = new ImportedSlider { TimeMs = 205 + i * 600, X = 100, Y = 100, PixelLength = 100, SpanCount = 2 };
            slider.ControlPoints.AddRange([new(100, 100), new(140, 160), new(200, 100)]); map.ImportedSliders.Add(slider);
        }
        map.BananaShowers.Add(new() { TimeMs = 70000, EndTimeMs = 71000 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
        ui.View.UpdateTransport(30500, 90000, true, false, false, null, null); ui.Paint();
        var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        Measure("Mapping");
        long start = System.Diagnostics.Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        ui.View.ShowHitsoundingPage(); ui.Paint();
        Console.WriteLine($"Hitsounding cold setup={System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3}ms / {GC.GetAllocatedBytesForCurrentThread() - allocated}B");
        Measure("Note based");
        var timingHandle = ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints.Single(p => p.TimeMs == 30000));
        ui.View.PointerDown(timingHandle.X + 4, timingHandle.Y + 10, 0, false, false);
        Measure("Timing handle drag"); ui.Key(27);
        Check(baseline.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Timing drag retains committed map and conversion");
        ui.View.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 200).ToArray(), 1));
        ui.ClickText(L.Get("hitsounding.precise")); Measure("Precise HS");
        ui.View.PointerDown(700, 400, 0, false, false);
        ui.View.PointerMove(900, 600, false, false); ui.Paint();
        Measure("Playing marquee", playback: true);
        ui.View.PointerUp(900, 600, 0);
        Check(ReferenceEquals(conversion, ui.View.Conversion) && baseline.ContentEquals(ui.View.Document), "Views and pointer motion reuse committed conversion");
        void Measure(string mode, bool playback = false)
        {
            var input = new List<double>(); var render = new List<double>(); var memory = new List<long>();
            for (int i = 0; i < 120; i++)
            {
                if (playback) ui.View.UpdateTransport(30500 + i * 10, 90000, true, true, false, null, null);
                long bytes = GC.GetAllocatedBytesForCurrentThread(), stamp = System.Diagnostics.Stopwatch.GetTimestamp();
                ui.View.PointerMove(800 + i % 100, 400 + i % 80, false, false);
                double dispatch = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                stamp = System.Diagnostics.Stopwatch.GetTimestamp(); ui.Paint();
                double paint = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                if (i >= 20) { input.Add(dispatch); render.Add(paint); memory.Add(GC.GetAllocatedBytesForCurrentThread() - bytes); }
            }
            input.Sort(); render.Sort(); memory.Sort();
            Console.WriteLine($"{mode}: pointer p95={input[94]:F4}ms; render p50={render[49]:F3}ms p95={render[94]:F3}ms; allocation p50={memory[49]}B (recording canvas, native GPU excluded)");
        }
    }
    private static FruitsAtelier.App.Rendering.Rect[] LaneBlocks(Ui ui, int lane)
    {
        var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100);
        return ui.Canvas.Outlines.Where(o => o.Color is 0x65BF8A or 0xE0B55F or 0xDC747B
            && o.Bounds.Y <= label.Y + 8 && o.Bounds.Bottom > label.Y + 8)
            .Select(o => o.Bounds).OrderBy(b => b.X).ToArray();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
