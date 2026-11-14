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
        foreach (float y in new[] { emptyY })
        {
            ui.View.UpdateTransport(1000, 10000, true, true, false, null, null); ui.Paint();
            ui.Click(emptyX, y);
            Check(ui.View.PlayheadMs == 1000 && ui.View.AudioPlaying, "Playing blank grid clicks preserve playback position and state");
            ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
            ui.Click(emptyX, y);
            Check(ui.View.PlayheadMs == 1000, "Paused blank grid clicks preserve the playhead");
        }
        Check(baseline.ContentEquals(ui.View.Document), "Blank clicks do not edit beatmap content");
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        var block = NormalBlocks()[0];
        ui.Click(block.X + block.Width / 2, block.Y + block.Height / 2);
        Check(ui.View.PlayheadMs == 1000, "Selecting a sound preserves transport position");
        var laneLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane1") && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146);
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

        ui.ClickText(L.Get("hitsounding.applyTiming"));
        Check(ui.View.Document.TimingPoints.Last() is { Uninherited: false, TimeMs: 1000 }, "An unchanged timing draft can insert a new inherited point");
        ui.Key(90, ctrl: true);
        var volumeLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.volume") && t.X > 1100);
        ui.Click(volumeLabel.X + 95, volumeLabel.Y + 5); ui.Key(65, ctrl: true); ui.Type("40"); ui.Key(13);
        Check(baseline.ContentEquals(ui.View.Document), "Timing draft is not automatically applied");
        ui.ClickText(L.Get("hitsounding.applyTiming"));
        Check(ui.View.Document.TimingPoints.Last() is { Uninherited: false, TimeMs: 1000, Volume: 40 }, "Apply inserts an inherited timing point at the note timestamp");
        ui.Key(90, ctrl: true);
        var normal = NormalBlocks();
        float whistleY = LaneY(2), startX = normal[1].X + normal[1].Width / 2;
        ui.ClickText(L.Get("hitsounding.draw"));
        ui.Click(startX, whistleY);
        Check(ui.View.Document.HitsoundOverrides.Count == 1 && ui.View.Document.HitsoundOverrides[0].Sample.Additions == 2, "Draw adds a whistle with one click");
        ui.Key(90, ctrl: true);
        ui.ClickText(L.Get("hitsounding.select"));
        normal = NormalBlocks(); startX = normal[1].X + normal[1].Width / 2;
        float finishX = normal[2].X + normal[2].Width / 2;
        float selectionY = normal[1].Y - 4;
        ui.View.PointerDown(startX, whistleY, 0, false, false); ui.Paint();
        Check(!ui.Canvas.PaintCalls.Any(p => p.Color == 0x65BF8A && p.FillBounds is not null && p.Opacity == .5f), "Pending click placement does not draw a ghost");
        ui.View.PointerMove(finishX, selectionY, false, false); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Select dragging from an empty slot does not change content");
        Check(!ui.Canvas.PaintCalls.Any(p => p.Color == 0x65BF8A && p.FillBounds is not null && p.Opacity == .5f), "Marquee disables placement previews");
        ui.View.PointerUp(finishX, selectionY, 0); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Releasing a placement-origin marquee adds no sounds");
        ui.Key(81);
        Check(!baseline.ContentEquals(ui.View.Document) && ui.View.HitsoundingBrush == 1, "Q changes selected sounds after marquee selection");
        ui.Key(90, ctrl: true);
        ui.ClickText(L.Get("hitsounding.draw"));
        ui.Click(startX, whistleY); ui.Paint();
        var whistle = LaneBlocks(ui, 2).First();
        ui.View.PointerDown(whistle.X + whistle.Width / 2, whistle.Y + 10, 2, false, false);
        ui.View.PointerUp(whistle.X + whistle.Width / 2, whistle.Y + 10, 2); ui.Paint();
        Check(ui.View.Document.HitsoundOverrides.All(s => (s.Sample.Additions & 2) == 0), "Right click deletes only the clicked addition");
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
        Console.WriteLine("PASS Timing Helper drafts, event editing, Select marquee selection, exact selection, playback feedback and play-line clamps");

        FruitsAtelier.App.Rendering.Rect[] NormalBlocks() => LaneBlocks(ui, 1);
        float LaneY(int lane) { var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146); return label.Y + 8; }

    }
    public static void DrawModes()
    {
        var map = new MapDocument { DurationMs = 6000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2 });
        foreach (int time in new[] { 1000, 1500, 2000 }) map.Fruits.Add(new() { TimeMs = time, X = 256 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        var blocks = LaneBlocks(ui, 1);
        float x = blocks[0].X + blocks[0].Width / 2;
        float end = blocks[^1].X + blocks[^1].Width / 2;
        float y = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane2") && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146).Y + 8;
        ui.View.PointerMove(x, y, false, false); ui.Paint();
        Check(!Ghost(), "Select is the safe default with no placement ghost");
        ui.Click(x, y); Check(baseline.ContentEquals(ui.View.Document), "Select single click never places a sound");
        ui.ClickText(L.Get("hitsounding.draw"));
        ui.View.PointerMove(x, y, false, false); ui.Paint(); Check(Ghost(), "Draw previews an eligible addition");
        ui.View.PointerDown(x - 30, y, 0, false, false);
        ui.View.PointerMove(end, y, false, false); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Draw stroke commits at release");
        ui.View.PointerUp(end, y, 0); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "Draw dragging creates a marquee without adding sounds");
        ui.Click(x, y);
        Check(ui.View.Document.HitsoundOverrides.Count == 1, "Draw click places one sound");
        ui.Key('Z', ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Single placement is one undo step");
        blocks = LaneBlocks(ui, 1);
        float normalY = blocks[0].Y + 10;
        ui.View.PointerDown(x, normalY, 0, false, false);
        ui.View.PointerMove(end, normalY, false, false); ui.View.PointerUp(end, normalY, 0); ui.Paint();
        Check(ui.View.SelectedObjectIds.Count == 3 && baseline.ContentEquals(ui.View.Document), "Draw drag from an existing sound selects all crossed tags");
        ui.Key(68);
        Check(ui.View.Document.HitsoundOverrides.Count == 3 && ui.View.Document.HitsoundOverrides.All(s => s.Sample.NormalSet == 3), "Draw marquee selection receives a batch bank change");
        ui.Key(90, ctrl: true);
        ui.ClickText(L.Get("hitsounding.select"));
        Check(baseline.ContentEquals(ui.View.Document), "Mode changes do not create beatmap edits");
        Console.WriteLine("PASS Select safety and Draw hover, marquee, single placement and undo");
        bool Ghost() => ui.Canvas.PaintCalls.Any(p => p.Color == 0x65BF8A && p.FillBounds is not null && p.Opacity == .5f);
    }
    public static void SampleSetCycling()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 1 });
        foreach (int time in new[] { 1000, 1500 }) map.Fruits.Add(new() { TimeMs = time, X = 256,
            OriginalLine = $"256,192,{time},1,14,{(time == 1000 ? 1 : 3)}:2:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.select")) && ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.draw"))
            && ui.View.SamplesetBounds.Width > 0 && ui.View.AdditionsBounds.Width > 0, "Toolbar retains placement banks and Select/Draw modes");
        var baseline = ui.View.Document.DeepClone();
        Click(1, 1); Click(1, 0, true);
        Check(ui.View.HitsoundingBrushEnabled, "Selected sounds enable brushes in Select mode");
        foreach (int expected in new[] { 3, 2, 1 })
        {
            ui.Key(81);
            var resolver = new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
            Check(ui.View.HitsoundingBrush == expected && ui.View.Conversion.Objects.All(n => resolver.Describe(n).NormalSet == expected
                && resolver.Describe(n).AdditionSet == 2), "Q applies the bank to both selected Normal sounds");
        }
        foreach (var (key, bank) in new[] { (65, 2), (83, 1), (68, 3) })
        { ui.Key(key); Check(ui.View.HitsoundingBrush == bank && ui.View.Document.HitsoundOverrides.All(s => s.Sample.NormalSet == bank), "ASD changes selected sound banks"); }
        ui.Key(50); Check(ui.View.HitsoundingDraw, "2 selects Draw");
        ui.Key(65); Check(ui.View.Document.HitsoundOverrides.All(s => s.Sample.NormalSet == 2), "Draw also changes selected sound banks");
        ui.Key(49); Check(!ui.View.HitsoundingDraw, "1 selects Select");
        Click(2, 0); ui.Key(81);
        Check(ui.View.Document.HitsoundOverrides.Single(s => s.SourceId == map.Fruits[0].Id) is { Sample.NormalSet: 2, Sample.AdditionSet: 1 }, "Q changes the selected addition bank while preserving Normal");
        Console.WriteLine("PASS independent Hitsounding brushes and keyboard modes");
        void Click(int lane, int index, bool ctrl = false)
        {
            var box = LaneBlocks(ui, lane)[index];
            ui.View.PointerDown(box.X + box.Width / 2, box.Y + 10, 0, false, ctrl);
            ui.View.PointerUp(box.X + box.Width / 2, box.Y + 10, 0); ui.Paint();
        }
    }
    public static void PlacementBanksAndHeadPriority()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 1 });
        map.Fruits.Add(new() { TimeMs = 1000, X = 256 });
        map.Fruits.Add(new() { TimeMs = 1500, X = 256 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
        ui.View.UpdateTransport(1000, 6000, true, false, false, null, null); ui.Paint();
        ui.ClickText(L.Get("hitsounding.draw"));
        var normal = LaneBlocks(ui, 1)[0];
        var baseline = ui.View.Document.DeepClone();
        var bank = ui.View.SamplesetBounds; ui.Click(bank.X + 10, bank.Y + 10); ChooseBank("timing.soft");
        bank = ui.View.AdditionsBounds; ui.Click(bank.X + 10, bank.Y + 10); ChooseBank("timing.drum");
        Check(baseline.ContentEquals(ui.View.Document), "Placement bank selectors without selection do not edit existing notes");
        ui.Key(68);
        normal = LaneBlocks(ui, 1)[0];
        var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane2") && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146);
        float x = normal.X + normal.Width / 2, y = label.Y + 8;
        var head = ui.Canvas.Lines.Single(l => l.Color is 0xE9CA64 or 0xFFF0A6 && l.Y1 == 130);
        Check(Math.Abs(x - head.X1) < 1, "Placement fixture overlaps the play line");
        ui.Click(x, y);
        Check(ui.View.Document.HitsoundOverrides.Single() is { Sample.NormalSet: 1, Sample.AdditionSet: 3, Sample.Additions: 2 },
            "Click at the play line uses the addition bank without changing Normal");
        ui.Key(49);
        var whistle = LaneBlocks(ui, 2)[0]; ui.Click(whistle.X + whistle.Width / 2, whistle.Y + 10); ui.Key(81);
        Check(ui.View.Document.HitsoundOverrides.Single().Sample.AdditionSet == 2 && ui.View.HitsoundingBrush == 2, "Q updates an existing sound at the play line");
        ui.View.PointerDown(x, y, 2, false, false); ui.View.PointerUp(x, y, 2); ui.Paint();
        Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions == 0, "Right-click deletes selected sound immediately");
        ui.Key(90, ctrl: true); Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions == 2, "Direct deletion is undoable");
        ui.Key(90, ctrl: true); Check(ui.View.Document.HitsoundOverrides.Single().Sample.AdditionSet == 3, "Bank change is undoable");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Placement is one undo step with no selector history");
        ui.Key(50); ui.Key(83); ui.Click(x, y);
        Check(ui.View.Document.HitsoundOverrides.Single() is { Sample.NormalSet: 1, Sample.AdditionSet: 1 }, "Drawing uses the independent Normal brush");
        Console.WriteLine("PASS placement banks, direct deletion and play-line input priority");
        void ChooseBank(string key)
        {
            var text = ui.Canvas.Texts.Single(t => t.Value == L.Get(key) && t.X >= bank.X && t.X < bank.Right);
            ui.Click(text.X + 5, text.Y + 5);
        }
    }
    public static void PlacementBankIsolation()
    {
        foreach (int lane in new[] { 2, 3, 4 })
        foreach (bool inheritedNormal in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2 });
            map.Fruits.Add(new() { TimeMs = 1000, X = 256,
                OriginalLine = $"256,192,1000,1,0,{(inheritedNormal ? 0 : 1)}:0:0:0:" });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint(); ui.ClickText(L.Get("hitsounding.draw"));
            var baseline = ui.View.Document.DeepClone();
            Choose(ui.View.SamplesetBounds, "timing.drum");
            Choose(ui.View.AdditionsBounds, "timing.soft");
            var normal = LaneBlocks(ui, 1).Single();
            var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146);
            ui.Click(normal.X + normal.Width / 2, label.Y + 8);
            var setting = ui.View.Document.HitsoundOverrides.Single();
            Check(setting.Sample.NormalSet == (inheritedNormal ? 2 : 1)
                && setting.InheritedFields.HasFlag(InheritedSampleFields.NormalSet) == inheritedNormal,
                "Addition placement preserves both the existing Normal bank and its inheritance");
            Check(setting.Sample.AdditionSet == 2 && setting.Sample.Additions == 1 << (lane - 1),
                "Whistle, Finish and Clap placement uses the Soft brush");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Isolated placement is one undo step");
            ui.Key(89, ctrl: true); Check(ui.View.Document.HitsoundOverrides.Single() == setting, "Redo retains bank isolation");
            void Choose(FruitsAtelier.App.Rendering.Rect bounds, string key)
            {
                ui.Click(bounds.X + 10, bounds.Y + 10);
                var text = ui.Canvas.Texts.Single(t => t.Value == L.Get(key) && t.X >= bounds.X && t.X < bounds.Right);
                ui.Click(text.X + 5, text.Y + 5);
            }
        }
        Console.WriteLine("PASS addition placement preserves Normal banks and inheritance");
    }
    public static void SelectedBankSelectors()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2 });
        map.Fruits.Add(new() { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1,14,0:0:0:0:" });
        map.Fruits.Add(new() { TimeMs = 1500, X = 256, OriginalLine = "256,192,1500,1,14,3:1:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        Choose(false, "timing.normal"); Choose(true, "timing.soft");
        var baseline = ui.View.Document.DeepClone();
        Select(1, 1); Display(false, "timing.drum"); Display(true, "timing.normal");
        Choose(true, "timing.drum");
        Check(ui.View.Document.HitsoundOverrides.Single() is { Sample.NormalSet: 3, Sample.AdditionSet: 3 }, "Single selected event uses the selector without changing the other bank");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Single selector edit is undoable");
        Select(1, 0, true); Display(false, "samples.auto"); Display(true, "samples.auto");
        Choose(false, "timing.normal"); Display(false, "timing.normal");
        var settings = ui.View.Document.HitsoundOverrides;
        Check(settings.Count == 2 && settings.All(h => h.Sample.NormalSet == 1), "Sampleset applies to all selected events");
        Check(settings.Single(h => h.SourceId == map.Fruits[0].Id).Sample.AdditionSet == 2
            && settings.Single(h => h.SourceId == map.Fruits[1].Id).Sample.AdditionSet == 1, "Normal changes preserve both inherited and explicit addition banks");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Batch Sampleset change is one undo step");
        Display(false, "samples.auto");
        Choose(true, "timing.drum"); Display(true, "timing.drum");
        settings = ui.View.Document.HitsoundOverrides;
        Check(settings.Count == 2 && settings.All(h => h.Sample.AdditionSet == 3)
            && settings.Single(h => h.SourceId == map.Fruits[0].Id).InheritedFields.HasFlag(InheritedSampleFields.NormalSet)
            && settings.Single(h => h.SourceId == map.Fruits[1].Id).Sample.NormalSet == 3, "Batch Additions preserves each Normal bank and inheritance");
        Choose(true, "samples.auto"); Display(true, "samples.auto");
        Check(ui.View.Document.HitsoundOverrides.All(h => h.InheritedFields.HasFlag(InheritedSampleFields.AdditionSet)), "Auto restores inheritance on all selected events");
        ui.Key(90, ctrl: true); Display(true, "timing.drum");
        ui.Key(89, ctrl: true); Display(true, "samples.auto");
        var normal = LaneBlocks(ui, 1)[0]; ui.Click(normal.X + normal.Width / 2, normal.Y - 10);
        Display(false, "timing.normal"); Display(true, "timing.soft");
        Select(2, 0); Select(3, 1, true); Choose(true, "timing.normal");
        Check(ui.View.Document.HitsoundOverrides.All(h => h.Sample.AdditionSet == 1), "Mixed addition-lane selections update every selected event");
        Console.WriteLine("PASS selected bank display, batch editing, Auto, isolation and undo");
        void Select(int lane, int index, bool ctrl = false)
        {
            var box = LaneBlocks(ui, lane)[index];
            ui.View.PointerDown(box.X + box.Width / 2, box.Y + 10, 0, false, ctrl);
            ui.View.PointerUp(box.X + box.Width / 2, box.Y + 10, 0); ui.Paint();
        }
        void Display(bool additions, string key)
        {
            var bounds = additions ? ui.View.AdditionsBounds : ui.View.SamplesetBounds;
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get(key) + " ▾" && t.X >= bounds.X && t.X < bounds.Right), "Selector reflects authored selection or placement defaults");
        }
        void Choose(bool additions, string key)
        {
            var bounds = additions ? ui.View.AdditionsBounds : ui.View.SamplesetBounds;
            ui.Click(bounds.X + 10, bounds.Y + 10);
            var text = ui.Canvas.Texts.Where(t => t.Value.TrimStart('✓', ' ') == L.Get(key) && t.X >= bounds.X && t.X < bounds.Right && t.Y >= bounds.Bottom).OrderBy(t => t.Y).First();
            ui.Click(text.X + 5, text.Y + 5);
        }
    }
    public static void KiaiStrip()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        map.TimingPoints.Add(new() { TimeMs = 500, BeatLengthMs = -100, Uninherited = false, Effects = 1 });
        map.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, Effects = 1, SampleSet = 3 });
        map.TimingPoints.Add(new() { TimeMs = 1500, BeatLengthMs = -100, Uninherited = false });
        map.TimingPoints.Add(new() { TimeMs = 2000, BeatLengthMs = 500, Effects = 1 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        float initialX = ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints[0]).X + 4;
        float pixelsPerMs = (ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints[2]).X + 4 - initialX) / 1000;
        Check(Covered(750) && Covered(1250) && !Covered(250) && !Covered(1750) && Covered(2250),
            "Orange On intervals cover Kiai across bank changes and red/green transitions only");
        var baseline = ui.View.Document.DeepClone();
        var handle = ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints[1]);
        float delta = X(1000) - X(500);
        ui.View.PointerDown(handle.X + 4, handle.Y + handle.Height / 2, 0, false, false);
        ui.View.PointerMove(handle.X + 4 + delta / 2, handle.Y + handle.Height / 2, false, false); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document) && !Covered(600) && Covered(800), "Kiai strip follows timing drag preview without editing content");
        ui.Key(27); Check(Covered(600), "Cancel restores the Kiai strip");
        ui.View.UpdateTransport(4000, 6000, true, false, false, null, null); ui.Paint();
        Check(Covered(3500) && baseline.ContentEquals(ui.View.Document), "Open-ended Kiai remains visible when its start scrolls offscreen");
        Console.WriteLine("PASS Hitsounding Kiai strip boundaries, clipping and drag preview");
        float X(double time) => initialX + (float)(time - ui.View.PlayheadMs) * pixelsPerMs;
        bool Covered(double time) => ui.Canvas.Fills.Any(f => f.Color == 0xD89532 && f.Bounds.Height == 22
            && f.Bounds.X <= X(time) && f.Bounds.Right >= X(time));
    }
    public static void SelectionAndTiming()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1, Volume = 80 });
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = 1, Volume = 60, Effects = 8 });
        map.TimingPoints.Add(new() { TimeMs = 1750, BeatLengthMs = -100, Uninherited = false, SampleSet = 3, SampleIndex = 2, Volume = 70 });
        foreach (int time in new[] { 1000, 1250, 2000, 2250 }) map.Fruits.Add(new() { TimeMs = time, X = 256,
            OriginalLine = $"256,192,{time},1,{(time < 1750 ? 6 : 0)},0:0:0:0:" });
        var ui = new Ui(overview: false); ui.LoadDocument(map); ui.Resize(1440, 1600); ui.View.ShowHitsoundingPage(); ui.Paint();
        Check(ui.Canvas.Texts.Count(t => t.Value == L.Get("timing.offset")) == 2, "Exact red and green points get separate editable sections");
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyExisting")), "Existing timing controls do not need an Apply button");
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyTiming")), "Existing lines never offer inserting a duplicate");
        var volumes = ui.Canvas.Texts.Where(t => t.Value == L.Get("volume.title") && t.X > 1100).OrderBy(t => t.Y).ToArray();
        var percent = ui.Canvas.Texts.Single(t => t.X > 1100 && t.Value == "%" && t.Y == volumes[1].Y);
        ui.Click(percent.X - 30, percent.Y + 5); ui.Key(65, ctrl: true); ui.Type("35"); ui.Key(13);
        Check(ui.View.Document.TimingPoints.Count == 3 && ui.View.Document.TimingPoints[0].Volume == 80 && ui.View.Document.TimingPoints[1].Volume == 35,
            "Editing the second section immediately modifies only the existing green line");
        Check(ui.View.IsDirty, "Live timing edits remain unsaved until the user saves");
        ui.Key(90, ctrl: true);
        ToggleGreenKiai();
        Check(ui.View.Document.TimingPoints[1].Effects == 9, "Enabling Kiai preserves other effect flags");
        ToggleGreenKiai();
        Check(ui.View.Document.TimingPoints[1].Effects == 8, "Disabling Kiai preserves other effect flags");
        ui.Key(90, ctrl: true);
        Check(ui.View.Document.TimingPoints[1].Effects == 9, "Kiai changes support undo");
        ui.Key(90, ctrl: true);
        void ToggleGreenKiai()
        {
            var label = ui.Canvas.Texts.Where(t => t.Value == L.Get("timing.kiai")).OrderBy(t => t.Y).Last();
            ui.Click(label.X + 5, label.Y + 5);
        }
        var interval = ui.Canvas.Fills.First(f => f.Color == 0x72C686 && f.Bounds.Y < 220).Bounds;
        ui.Click(interval.X + 15, interval.Y + 10);
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.selectedTiming")) && ui.Canvas.Texts.Count(t => t.Value == L.Get("timing.offset")) == 1,
            "Clicking a timing interval selects its own point");
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.sv")), "Timing Helper does not expose source SV as effective SV");
        var volumeTrack = ui.Canvas.Lines.Single(l => l.X1 > 1100 && l.Width == 4 && l.Y1 == l.Y2);
        double originalBeatLength = ui.View.Document.TimingPoints[1].BeatLengthMs;
        var conversionBeforeVolume = ui.View.Conversion;
        ui.View.PointerDown(volumeTrack.X1, volumeTrack.Y1, 0, false, false);
        Check(ui.View.WantsCapture, "Timing Helper volume slider captures dragging");
        ui.View.PointerMove(volumeTrack.X2, volumeTrack.Y1, false, false);
        ui.Paint();
        Check(ui.View.Document.TimingPoints[1].Volume == 100 && ReferenceEquals(conversionBeforeVolume, ui.View.Conversion), "Volume updates live without rebuilding map conversion during dragging");
        var heard = new List<Hitsound>(); ui.View.RequestHitsound = heard.Add;
        ui.View.UpdateTransport(1000, 6000, true, true, false, null, null);
        Check(heard.Count > 0 && heard.All(sound => sound.Volume == 1), "Playback uses the live timing volume before the drag ends");
        ui.View.UpdateTransport(0, 6000, true, false, false, null, null);
        ui.View.PointerUp(volumeTrack.X2, volumeTrack.Y1, 0); ui.Paint();
        Check(!ui.View.WantsCapture, "Volume drag releases capture");
        Check(ui.View.Document.TimingPoints[1].Volume == 100 && ui.View.Document.TimingPoints[1].BeatLengthMs == originalBeatLength, "Applying volume preserves inherited beat length");
        ui.Key(90, ctrl: true);
        Check(ui.View.Document.TimingPoints[1].Volume == 60, "Volume slider application is one undo step");
        interval = ui.Canvas.Fills.First(f => f.Color == 0x72C686 && f.Bounds.Y < 220).Bounds;
        ui.Click(interval.X + 15, interval.Y + 10);
        volumeTrack = ui.Canvas.Lines.Single(l => l.X1 > 1100 && l.Width == 4 && l.Y1 == l.Y2);
        ui.View.PointerDown(volumeTrack.X1, volumeTrack.Y1, 0, false, false);
        ui.View.PointerMove(volumeTrack.X2, volumeTrack.Y1, false, false); ui.Key(27); ui.Paint();
        Check(ui.View.Document.TimingPoints[1].Volume == 60 && !ui.View.LibraryVisible && !ui.View.WantsCapture, "Escape cancels the timing volume gesture without leaving the editor");
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
            Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("editor.command.delete")), "Sound right-click does not open a menu");
            if (blank)
            {
                ui.Key(46);
                Check(baseline.ContentEquals(ui.View.Document), "Right-click blank space clears the selection without deleting sounds or creating history");
                continue;
            }
            Check(ui.View.Document.HitsoundOverrides.Count == 2 && ui.View.Document.HitsoundOverrides.All(h => h.Sample.Additions == 0), "Right-click removes every selected addition");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Right-click group deletion is one undo step");
            ui.Key(89, ctrl: true); Check(ui.View.Document.HitsoundOverrides.Count == 2, "Right-click group deletion is one redo step");
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
    public static void NearbyTiming()
    {
        var map = new MapDocument { DurationMs = 20000, BeatLengthMs = 500 };
        for (int i = 0; i < 11; i++) map.TimingPoints.Add(new() { TimeMs = i * 1000, BeatLengthMs = i == 0 ? 500 : -100, Uninherited = i == 0, SampleSet = 2, SampleIndex = i, Volume = 50 + i });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        CheckRows(500, 0);
        CheckRows(5500, 2);
        CheckRows(10500, 4);
        foreach (double time in new[] { 6000.01, 6000.999 })
        {
            ui.View.UpdateTransport(time, 20000, true, false, false, null, null); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.offset"))
                && !ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyTiming")), "Fractional playhead positions at the displayed timing millisecond expose the existing point editor");
        }
        ui.View.UpdateTransport(6001, 20000, true, false, false, null, null); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyTiming")), "The next displayed millisecond retains the new timing point form");
        ui.View.UpdateTransport(6000, 20000, true, false, false, null, null); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.currentTime"))
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.offset"))
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("volume.title"))
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyTiming")), "Current timestamp at an existing point exposes its full editor without selecting it");
        ui.View.UpdateTransport(6000, 20000, true, true, false, null, null); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.applyTiming"))
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.offset")), "Playing through a timing point keeps the current timestamp form stable");
        ui.View.UpdateTransport(6000, 20000, true, false, false, null, null); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.offset")), "Pausing at the same timestamp restores timing controls");
        var disabledJump = ui.Canvas.Texts.Single(t => t.Value == L.Get("timeJump.go"));
        ui.View.UpdateTransport(5500, 20000, true, false, false, null, null); ui.Paint();
        var list = ui.View.TimingHelperNearbyBounds;
        float headerTop = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.currentTime")).Y;
        float parameterTop = ui.Canvas.Texts.Single(t => t.Value == L.Get("samples.sampleset") && t.X > 1100).Y;
        var navigationBaseline = ui.View.Document.DeepClone();
        ui.View.Wheel(list.X + 8, list.Y + 20, -120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 5500 && Rows().First() == "00:03:000", "Nearby timing wheel scrolls rows without seeking");
        for (int i = 0; i < 7; i++) Wheel(-120);
        Check(ui.View.PlayheadMs == 5500 && Rows().First() == "00:04:000", "Wheel scrolls to the final seven rows without seeking");
        Wheel(2400); Wheel(120);
        Check(ui.View.PlayheadMs == 5500 && Rows().First() == "00:00:000", "Reverse wheel stops at the first row without seeking");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.currentTime")) && navigationBaseline.ContentEquals(ui.View.Document), "List scrolling preserves selection and map content");
        Wheel(-720);
        Jump("00:06:000");
        Check(ui.View.PlayheadMs == 5500 && Rows().SequenceEqual(Enumerable.Range(3, 7).Select(i => $"00:{i:00}:000")), "Clicking a nearby row selects and centers the list without moving the canvas");
        Check(ui.View.TimingHelperNearbyBounds.Y == list.Y && ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.selectedTiming")).Y == headerTop
            && ui.Canvas.Texts.Single(t => t.Value == L.Get("timing.offset")).Y - 8 == parameterTop, "Timestamp header height and nearby list position remain stable after selecting a green point");
        Check(!ui.Canvas.Texts.Any(t => t.X > 1100 && (t.Value == L.Get("timing.meter") || t.Value == L.Get("timing.bpm"))), "Green timing points omit BPM and Meter controls");
        var custom = ui.Canvas.Texts.Single(t => t.X > 1100 && t.Value == L.Get("timing.custom"));
        var customIndex = ui.Canvas.Texts.Single(t => t.X > 1100 && t.Value == "6");
        Check(Math.Abs(custom.Y - customIndex.Y) < 2 && customIndex.X > custom.X, "Custom index input shares the Custom radio row");
        var affectingTitle = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.affectingTiming"));
        var affectingGreen = ui.Canvas.Texts.Single(t => t.Value == "00:06:000" && t.Y > affectingTitle.Y && t.Y < affectingTitle.Y + 60);
        ui.Click(affectingGreen.X + 3, affectingGreen.Y + 4);
        Check(ui.View.PlayheadMs == 5500, "Affecting green row preserves canvas time");
        var affectingRed = ui.Canvas.Texts.Single(t => t.Value == "00:00:000" && t.Y > affectingTitle.Y && t.Y < affectingTitle.Y + 60);
        ui.Click(affectingRed.X + 3, affectingRed.Y + 4);
        Check(ui.View.PlayheadMs == 5500 && ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.meter")), "Affecting red row selects its timing point without moving canvas time");
        ui.ClickText(L.Get("timeJump.go"));
        Check(ui.View.PlayheadMs == 0, "Jump explicitly seeks to the selected timing point");
        Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("timeJump.go")).Color == disabledJump.Color,
            "Jump is disabled when the playhead reaches the displayed timestamp");
        Check(navigationBaseline.ContentEquals(ui.View.Document), "Affecting row navigation preserves map content");
        map.TimingPoints.RemoveRange(3, 8); ui.LoadDocument(map); ui.View.ShowHitsoundingPage(); ui.Paint();
        Check(Rows().Length == 3, "Nearby timing lists all available points when fewer than seven exist");
        var original = ui.View.Document.DeepClone(); Jump("00:00:000");
        var red = ui.Canvas.Texts.Single(t => ui.View.TimingHelperNearbyBounds.Contains(t.X, t.Y) && t.Value == "00:00:000");
        Check(red.Color == 0xE77785 && ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.meter")), "Selected red points retain red highlighting and Meter controls");
        Check(original.ContentEquals(ui.View.Document), "Nearby point navigation does not edit the map");
        ui.View.UpdateTransport(1500, 20000, true, true, false, null, null); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.currentTime"))
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.selectedTiming")), "Starting playback clears the timing selection and resumes following current time");
        ui.View.UpdateTransport(1500, 20000, true, false, false, null, null); ui.Paint();
        Jump("00:00:000");
        var transportHead = ui.Canvas.Lines.Single(l => l.Color == 0xFFFFFF && l.X1 == l.X2 && l.Y1 > 800);
        ui.View.PointerDown(transportHead.X1, transportHead.Y1 + 12, 0, false, false);
        ui.View.PointerMove(transportHead.X1 + 20, transportHead.Y1 + 12, false, false);
        ui.View.PointerUp(transportHead.X1 + 20, transportHead.Y1 + 12, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.currentTime"))
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.selectedTiming")), "Dragging the transport timeline clears timing selection and resumes following");
        Check(original.ContentEquals(ui.View.Document), "Resuming Timing Helper follow does not edit the map");
        void Jump(string timestamp)
        {
            var text = ui.Canvas.Texts.Single(t => ui.View.TimingHelperNearbyBounds.Contains(t.X, t.Y) && t.Value == timestamp);
            ui.Click(text.X + 3, text.Y + 4);
        }
        void Wheel(float delta)
        {
            var bounds = ui.View.TimingHelperNearbyBounds;
            ui.View.Wheel(bounds.X + 8, bounds.Y + 20, delta, false, false, false); ui.Paint();
        }
        void CheckRows(double time, int first)
        {
            ui.View.UpdateTransport(time, 20000, true, false, false, null, null); ui.Paint();
            Check(Rows().SequenceEqual(Enumerable.Range(first, 7).Select(i => $"00:{i:00}:000")), "Nearby timing balances seven chronological rows at the middle and both boundaries");
            Check(ui.View.TimingHelperNearbyBounds.Bottom <= 900, "All nearby rows remain within the panel");
        }
        string[] Rows()
        {
            var bounds = ui.View.TimingHelperNearbyBounds;
            return ui.Canvas.Texts.Where(t => bounds.Contains(t.X, t.Y) && t.Value.StartsWith("00:") && t.Value.Length == 9).OrderBy(t => t.Y).Select(t => t.Value).ToArray();
        }
    }
    public static void CtrlSelection()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        foreach (int time in new[] { 1000, 1500 }) map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,6,0:0:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        var original = ui.View.Document.DeepClone();
        foreach (bool removeFirst in new[] { false, true })
        {
            var blocks = LaneBlocks(ui, 2);
            Click(blocks[0], false); Click(blocks[1], true);
            Check(ui.View.SelectedObjectIds.Count == 2, "Ctrl-click adds a sound without replacing the previous selection");
            if (removeFirst) Click(blocks[0], true);
            Check(original.ContentEquals(ui.View.Document), "Ctrl selection changes neither map content nor time");
            ui.Key(46);
            Check(ui.View.Document.HitsoundOverrides.Count == (removeFirst ? 1 : 2) && ui.View.Document.HitsoundOverrides.All(h => h.Sample.Additions == 4), "Delete uses the Ctrl-selected sounds and excludes toggled-off sounds");
            ui.Key(90, ctrl: true);
            Check(original.ContentEquals(ui.View.Document), "Ctrl-selected deletion is one undo step");
        }
        var notesLock = ui.View.HitsoundingLockBounds(0); ui.Click(notesLock.X + 12, notesLock.Y + 12);
        var normal = LaneBlocks(ui, 1);
        var notes = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane0") && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146);
        Click(new(normal[0].X, notes.Y, normal[0].Width, 12), false);
        Click(new(normal[1].X, notes.Y, normal[1].Width, 12), true);
        Check(ui.View.SelectedObjectIds.Count == 2, "Ctrl-click also accumulates Notes-row selection");
        Click(new(normal[0].X, notes.Y, normal[0].Width, 12), true);
        Check(ui.View.SelectedObjectIds.Count == 1 && ui.View.SelectedObjectIds.Contains(map.Fruits[1].Id), "Ctrl-click toggles a Notes-row object off");
        void Click(FruitsAtelier.App.Rendering.Rect r, bool ctrl)
        {
            float x = r.X + r.Width / 2, y = r.Y + 5;
            ui.View.PointerDown(x, y, 0, false, ctrl); ui.View.PointerUp(x, y, 0); ui.Paint();
        }
    }
    public static void TransferTimingIndex()
    {
        foreach (bool move in new[] { false, true })
        foreach (bool red in new[] { false, true })
        foreach (bool differentIndex in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1 });
            map.TimingPoints.Add(new() { TimeMs = 1750, BeatLengthMs = red ? 500 : -100, Uninherited = red,
                SampleSet = 2, SampleIndex = differentIndex ? 2 : 1 });
            map.Fruits.Add(new() { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1,2,2:3:7:0:" });
            map.Fruits.Add(new() { TimeMs = 2000, X = 256, OriginalLine = $"256,192,2000,1,0,2:{(differentIndex ? 3 : 1)}:{(differentIndex ? 7 : 8)}:0:" });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
            var baseline = ui.View.Document.DeepClone();
            var source = LaneBlocks(ui, 2).Single(); var target = LaneBlocks(ui, 1).Last();
            float x = source.X + source.Width / 2, y = source.Y + 10;
            if (move)
            {
                float destination = target.X + target.Width / 2;
                ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(destination, y, false, false);
                ui.View.PointerUp(destination, y, 0); ui.Paint();
            }
            else
            {
                ui.Click(x, y); ui.Key(67, ctrl: true);
                ui.View.UpdateTransport(2000, 6000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true);
            }
            Check(ui.View.DiscardConfirmationVisible == differentIndex,
                "Only timing-point sample index differences trigger transfer confirmation, regardless of event banks and overrides");
            if (differentIndex)
            {
                Check(baseline.ContentEquals(ui.View.Document), "Index mismatch waits without editing content");
                ui.ClickText(L.Get("hitsounding.pasteAdjust"));
            }
            var placed = ui.View.Document.HitsoundOverrides.Single(h => h.SourceId == map.Fruits[1].Id);
            Check(placed.Sample is { AdditionSet: 3, Additions: 2, Index: 7 }, "Transfer preserves the source sound bank and explicit index");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Transfer is one undo step");
        }
        Console.WriteLine("PASS transfer confirmation uses red/green timing sample indices");
    }
    public static void TransferIndexInheritance()
    {
        foreach (int initialIndex in new[] { 0, 1 })
        foreach (bool move in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = initialIndex });
            map.Fruits.Add(new() { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1,2,3:3:0:0:" });
            map.Fruits.Add(new() { TimeMs = 2000, X = 256 });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
            var source = LaneBlocks(ui, 2).Single(); var target = LaneBlocks(ui, 1).Last();
            float x = source.X + source.Width / 2, y = source.Y + 10;
            if (move)
            {
                float destination = target.X + target.Width / 2;
                ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(destination, y, false, false);
                ui.View.PointerUp(destination, y, 0); ui.Paint();
            }
            else
            {
                ui.Click(x, y); ui.Key(67, ctrl: true);
                ui.View.UpdateTransport(2000, 6000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true);
            }
            Check(!ui.View.DiscardConfirmationVisible && ui.View.Document.HitsoundOverrides.Single(h => h.SourceId == map.Fruits[1].Id)
                .InheritedFields.HasFlag(InheritedSampleFields.Index), "Same-index transfer retains timing index inheritance");
            target = LaneBlocks(ui, 2).Last(); ui.Click(target.X + target.Width / 2, target.Y + 10);
            var index = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.index") && t.X > 1100);
            ui.Click(index.X + 95, index.Y + 5); ui.Key(65, ctrl: true); ui.Type("2"); ui.Key(13);
            ui.ClickText(L.Get("hitsounding.applyTiming"));
            Check(ui.View.Document.TimingPoints.Any(p => p.TimeMs == 2000 && p.SampleIndex == 2), "Timing Helper inserts the S:2 line");
            CheckIndex(ui.View.Document, 2);
            var tag = LaneBlocks(ui, 2).Last();
            Check(ui.Canvas.Texts.Any(t => t.Value == "2" && t.X >= tag.X && t.X < tag.Right && t.Y >= tag.Y && t.Y < tag.Bottom), "Drum tag displays index 2 after timing insertion");
            ui.Key(90, ctrl: true); CheckIndex(ui.View.Document, initialIndex);
            ui.Key(89, ctrl: true); CheckIndex(ui.View.Document, 2);
            var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)); CheckIndex(saved, 2);
            if (initialIndex == 0)
            {
                int position = saved.HitsoundOverrides.FindIndex(h => h.SourceId == map.Fruits[1].Id);
                saved.HitsoundOverrides[position] = saved.HitsoundOverrides[position] with { Sample = saved.HitsoundOverrides[position].Sample with { Index = 0 },
                    InheritedFields = saved.HitsoundOverrides[position].InheritedFields & ~InheritedSampleFields.Index };
                CheckIndex(ProjectSerializer.Read(ProjectSerializer.Serialize(saved)), 2);
            }
        }
        Console.WriteLine("PASS transferred and saved zero-index sounds follow timing in UI, playback and export");
        static void CheckIndex(MapDocument document, int expected)
        {
            var exported = OsuBeatmapWriter.Serialize(document);
            var resolver = new HitsoundResolver(document, exported.PlayableObjects);
            var note = exported.PlayableObjects.Single(n => n.Kind == CatchObjectKind.Fruit && n.TimeMs == 2000);
            Check(resolver.Describe(note) is { AdditionSet: 3 } sample && sample.Index == expected, "Authoring audio uses the timing index and retains Drum");
            var readback = OsuBeatmapReader.Read(exported.Text);
            var objects = OsuBeatmapWriter.Serialize(readback).PlayableObjects;
            var exportedSample = new HitsoundResolver(readback, objects).Describe(objects.Single(n => n.Kind == CatchObjectKind.Fruit && n.TimeMs == 2000));
            Check(exportedSample.AdditionSet == 3 && exportedSample.Index == expected, "Export read-back matches the inherited timing sample");
        }
    }
    public static void LengthAndMove()
    {
        GroupMoveAndNavigation();
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
    private static void GroupMoveAndNavigation()
    {
        foreach (bool precise in new[] { false, true })
        foreach (bool draw in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 12000, BeatLengthMs = 500 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1 });
            foreach (int time in new[] { 1000, 1500, 2000, 2500 })
                map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,{(time <= 1500 ? 2 : 0)},0:0:0:0:" });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
            ui.View.UpdateTransport(2000, 12000, true, false, false, null, null); ui.Paint();
            if (precise) { ui.ClickText(L.Get("hitsounding.precise")); ui.Paint(); }
            var baseline = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
            var sounds = LaneBlocks(ui, 2);
            void Select()
            {
                foreach (var sound in sounds)
                {
                    ui.View.PointerDown(sound.X + 2, sound.Y + 10, 0, false, true);
                    ui.View.PointerUp(sound.X + 2, sound.Y + 10, 0);
                }
                ui.Paint();
            }
            Select();
            if (draw) ui.Key(50);
            var grid = ui.View.HitsoundingNotesLaneBounds;
            float X(double time) => grid.X + (float)((time - 800) / 4000 * grid.Width);
            float y = sounds[0].Y + 10;
            void Begin(double destination)
            {
                ui.View.PointerDown(sounds[0].X + 2, y, 0, false, false);
                ui.View.PointerMove(X(destination), y, false, false); ui.Paint();
                Check(baseline.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Group ghost retains committed content and conversion");
                Check(ui.Canvas.PaintCalls.Count(p => p.FillBounds is { Width: 18, Height: 32 } r && r.Y <= y && r.Bottom > y && p.Opacity == .5f) == 2,
                    "Both selected sounds have translucent destination ghosts");
            }
            Begin(2000); ui.Key(27); ui.View.PointerUp(X(2000), y, 0); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Escape cancels the complete sound move");
            Begin(2500); ui.View.PointerUp(X(2500), y, 0); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "A missing group destination rejects the entire move");
            Begin(2000); ui.View.PointerUp(X(2000), y, 0); ui.Paint();
            var resolver = new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
            Check(ui.View.Conversion.Objects.All(n => resolver.Describe(n).Additions == (n.TimeMs >= 2000 ? 2 : 0)), "Release moves both additions by the same offset without placing extra sounds in either tool");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Group move is one undo step");
            ui.View.UpdateTransport(6000, 12000, true, true, false, null, null);
            ui.Key(37, shift: true); Check(ui.View.PlayheadMs == 4000, "Shift-left seeks four beats during playback");
            ui.Key(39, shift: true); Check(ui.View.PlayheadMs == 6000, "Shift-right seeks four beats during playback");
            ui.View.UpdateTransport(6000, 12000, true, false, false, null, null);
            ui.Key(37, shift: true); Check(ui.View.PlayheadMs == 5500, "Paused Shift-left seeks four snap divisions");
        }
        foreach (bool lazer in new[] { false, true })
        foreach (bool playing in new[] { false, true })
        foreach (bool shift in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 12000, BeatLengthMs = 400 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 400 });
            var mapping = new Ui(false); mapping.LoadDocument(map); mapping.View.LibrarySettings.LazerEditing = lazer;
            var sounds = new Ui(false); sounds.LoadDocument(map); sounds.View.LibrarySettings.LazerEditing = lazer;
            sounds.View.ShowHitsoundingPage();
            foreach (int key in new[] { 37, 39 })
            {
                mapping.View.UpdateTransport(6000, 12000, true, playing, false, null, null);
                sounds.View.UpdateTransport(6000, 12000, true, playing, false, null, null);
                mapping.Key(key, shift: shift); sounds.Key(key, shift: shift);
                Check(mapping.View.PlayheadMs == sounds.View.PlayheadMs, "Hitsounding arrow navigation matches Mapping in each editing and playback mode");
            }
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
        var timingLabels = ui.Canvas.Fills.Where(f => f.Color is 0x72C686 or 0xE77785 or 0xD89532 or 0x54728C).Select(f => f.Bounds).ToArray();
        Check(timingLabels.Select(r => r.Y).Distinct().Count() == 4, "Timing values occupy four separate tracks");
        Check(timingLabels.GroupBy(r => r.Y).All(row => row.Zip(row.Skip(1)).All(pair => pair.First.X <= pair.Second.X)),
            "Timing intervals retain timestamp order in every track");
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
        var defaultChoice = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.default") && t.X < 400);
        ui.Click(defaultChoice.X + 4, defaultChoice.Y + 5);
        Check(baseline.ContentEquals(ui.View.Document), "SampleSet selection preserves map content");
        for (int index = 2; index <= 12; index++) map.TimingPoints.Add(new() { TimeMs = index * 100, SampleIndex = index, Uninherited = false, BeatLengthMs = -100 });
        ui.LoadDocument(map); ui.View.ShowHitsoundingPage(); ui.Paint();
        ui.ClickText(L.Get("hitsounding.default") + " ▾");
        Check(ui.Canvas.Texts.Count(t => t.X < 400 && t.Value.StartsWith("Custom ", StringComparison.Ordinal) && !t.Value.EndsWith("▾")) == 7, "Dropdown displays eight rows including Default");
        var choice = ui.Canvas.Texts.First(t => t.X < 400 && t.Value == L.Get("hitsounding.customSet", 1));
        ui.View.Wheel(choice.X + 5, choice.Y + 5, -1200, false, false, false); ui.Paint();
        ui.ClickText(L.Get("hitsounding.customSet", 12));
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.customSet", 12) + " ▾"), "Scrollable dropdown selects later timing indices");
        ui.ClickText(L.Get("hitsounding.customSet", 12) + " ▾");
        var thumb = ui.Canvas.Fills.Last(f => f.Bounds.Width == 4 && f.Bounds.Height >= 16).Bounds;
        var lastChoice = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.customSet", 12));
        float trackTop = lastChoice.Y - 7 - 7 * 32;
        ui.View.PointerDown(thumb.X + 2, thumb.Y + 2, 0, false, false);
        Check(ui.View.WantsCapture, "SampleSet scrollbar captures pointer while dragging");
        ui.View.PointerMove(thumb.X + 2, trackTop + 2, false, false);
        ui.View.PointerUp(thumb.X + 2, trackTop + 2, 0); ui.Paint();
        Check(!ui.View.WantsCapture, "SampleSet scrollbar releases pointer after dragging");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.default")), "Dragging SampleSet thumb reaches the first row without closing the menu");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.customSet", 12) + " ▾"), "Scrollbar drag does not select a SampleSet");
        ui.Click(thumb.X + 2, trackTop + 254);
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.customSet", 12)), "Clicking the SampleSet scrollbar track reaches the last row");
        ui.ClickText(L.Get("hitsounding.customSet", 12));
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
        var nudgeTarget = Blocks()[1];
        ui.View.PointerDown(nudgeTarget.X + 5, nudgeTarget.Y + 10, 0, false, true);
        ui.View.PointerUp(nudgeTarget.X + 5, nudgeTarget.Y + 10, 0); ui.Paint();
        ui.Key(75); Wait(2);
        Check(Samples().Select(s => s.TimeMs).Order().SequenceEqual(new[] { 625d, 1125d }), "K nudges the selected SB group");
        ui.Key(74); Wait(2);
        Check(Samples().Select(s => s.TimeMs).Order().SequenceEqual(new[] { 500d, 1000d }), "J retains SB selection and restores its timestamps");
        ui.Key(90, ctrl: true); ui.Key(90, ctrl: true); Wait(2);
        Check(baseline.ContentEquals(ui.View.Document), "SB nudges restore source sections through undo");
        ui.LoadDocument(map); ui.View.ShowHitsoundingPage(); Wait(2);
        first = Blocks()[0]; ui.Click(first.X + 5, first.Y + 10);
        var ctrlTarget = Blocks()[1];
        ui.View.PointerDown(ctrlTarget.X + 5, ctrlTarget.Y + 10, 0, false, true);
        ui.View.PointerUp(ctrlTarget.X + 5, ctrlTarget.Y + 10, 0); ui.Paint(); ui.Key(46); Wait(0);
        Check(Samples().Count == 0, "Ctrl-click adds storyboard sounds to a batch deletion");
        ui.Key(90, ctrl: true); Wait(2);
        first = Blocks()[0]; ui.Click(first.X + 5, first.Y + 10);
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
        foreach (var block in new[] { first, second })
        {
            ui.View.PointerDown(block.X + 5, block.Y + 10, 0, false, true);
            ui.View.PointerUp(block.X + 5, block.Y + 10, 0); ui.Paint();
        }
        ui.View.PointerDown(first.X + 5, first.Y + 10, 0, false, false);
        ui.View.PointerMove(first.X + 5 - dx / 2, first.Y + 10, false, false); ui.Paint();
        Check(baseline.ContentEquals(ui.View.Document), "SB group previews without rewriting its events");
        ui.View.PointerUp(first.X + 5 - dx / 2, first.Y + 10, 0); Wait(2);
        Check(Samples().Select(s => s.TimeMs).Order().SequenceEqual(new[] { 250d, 750d }), "SB group moves backward with one shared offset");
        ui.Key(90, ctrl: true); Wait(2); Check(baseline.ContentEquals(ui.View.Document), "SB group movement is one undo step");
        first = Blocks()[0]; second = Blocks()[1];
        ui.View.PointerDown(first.X - 5, first.Y - 5, 0, false, false);
        ui.View.PointerMove(second.Right + 5, second.Bottom + 5, false, false);
        ui.View.PointerUp(second.Right + 5, second.Bottom + 5, 0); ui.Paint();
        ui.View.PointerDown(first.X + 5, first.Y + 10, 2, false, false);
        ui.View.PointerUp(first.X + 5, first.Y + 10, 2); ui.Paint();
        Wait(0);
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
            throw new InvalidOperationException($"Expected {count} storyboard blocks, saw {Blocks().Length}; {ui.View.StatusMessage}; {string.Join(';', Samples().Select(s => s.TimeMs))}");
        }
    }
    public static void CrossDifficultyClipboard()
    {
        var source = new MapDocument { Name = "Source", DurationMs = 6000 };
        source.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, Volume = 80 });
        source.Fruits.Add(new() { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1,2,2:3:0:0:" });
        var target = source.DeepClone(); target.Name = "Target";
        target.Fruits[0].OriginalLine = "256,192,1000,1,0,2:0:0:0:";
        var ui = new Ui(false); ui.View.LoadProject(BeatmapProject.FromDocuments([source, target])); ui.Resize(1440, 900);
        ui.View.ShowHitsoundingPage(); ui.View.UpdateTransport(1000, 6000, true, false, false, null, null); ui.Paint();
        var block = LaneBlocks(ui, 2).Single(); ui.Click(block.X + block.Width / 2, block.Y + 10); ui.Key('C', ctrl: true);
        Check(ui.View.SwitchDifficulty(1), "Switch to paste target"); ui.Paint();
        Check(ui.View.HitsoundingPageVisible, "Mapset retains Hitsounding panel across difficulties");
        ui.Key('V', ctrl: true);
        var resolver = new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
        Check(resolver.Describe(ui.View.Conversion.Objects.Single()) is { NormalSet: 2, AdditionSet: 3, Additions: 2 },
            "Copied addition pastes across difficulties without changing target Normal");
        ui.Key('Z', ctrl: true); Check(target.ContentEquals(ui.View.Document), "Cross-difficulty paste has target-local undo");
        ui.View.SwitchDifficulty(0); ui.Paint();
        Check(source.ContentEquals(ui.View.Document), "Cross-difficulty copy preserves source content");
        Console.WriteLine("PASS shared mapset panel and cross-difficulty sound clipboard");
    }
    public static void SpinnerEndSamples()
    {
        var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, Volume = 80 });
        map.TimingPoints.Add(new() { TimeMs = 1500, BeatLengthMs = -100, Uninherited = false, SampleSet = 3, SampleIndex = 2, Volume = 60 });
        map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 2000,
            OriginalLine = "256,192,1000,8,0,2000,0:0:0:0:" });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
        ui.View.UpdateTransport(2000, 6000, true, false, false, null, null); ui.Paint();
        ui.View.ShowHitsoundingPage(); ui.Paint(); ui.ClickText(L.Get("hitsounding.draw"));
        var note = SpinnerHitsounds.Events(ui.View.Document).Single();
        var resolver = new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
        Check(resolver.Describe(note) is { NormalSet: 3, Index: 2, Volume: 60 }, "Spinner end inherits timing at its end");
        var normal = LaneBlocks(ui, 1).Single();
        ui.Click(normal.X + normal.Width / 2, normal.Y + 10); ui.View.SetModifiers(true, false); ui.Key(87); ui.View.SetModifiers(false, false);
        resolver = new(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
        Check(resolver.Describe(note).NormalSet == 1, "Spinner Normal tag is selectable and editable");
        ui.Key(90, ctrl: true);
        normal = LaneBlocks(ui, 1).Single();
        float x = normal.X + normal.Width / 2;
        var whistle = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane2") && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146);
        ui.Click(x, whistle.Y + 8);
        resolver = new(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
        Check(resolver.Describe(note).Additions == 2 && ui.View.Document.HitsoundOverrides.Count == 0,
            "Placing at a banana shower end edits the spinner sample columns");
        ui.Key(49); var block = LaneBlocks(ui, 2).Single(); ui.Click(block.X + block.Width / 2, block.Y + 10); ui.View.SetModifiers(true, false); ui.Key(87, ctrl: true); ui.View.SetModifiers(false, false);
        resolver = new(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
        Check(resolver.Describe(note) is { NormalSet: 3, AdditionSet: 1, Index: 2, Volume: 60 }, "Spinner addition selector preserves base bank and timing inheritance");
        var exported = OsuBeatmapWriter.Serialize(ui.View.Document).Text;
        Check(exported.Contains("256,192,1000,8,2,2000,0:1:0:0:"), "Spinner export preserves end time and authored additions");
        var sounds = new List<(Hitsound Sound, double Time)>();
        ui.View.RequestScheduleHitsound = (sound, time) => sounds.Add((sound, time));
        ui.View.PrimeScheduledHitsounds(1999);
        Check(sounds.Count(s => s.Time == 2000 && s.Sound.Name == "hitwhistle") == 1,
            "Spinner end schedules its addition exactly once");
        Check(ui.View.Conversion.Objects.Where(o => o.Kind == CatchObjectKind.Banana)
            .All(o => resolver.Resolve(o).Single().Name == "catch-banana"), "Individual bananas retain their fixed sample");
        ui.Key(90, ctrl: true); ui.Key(90, ctrl: true);
        Check(map.ContentEquals(ui.View.Document), "Spinner sound edits undo without changing geometry");
        var mapping = new Ui(); mapping.LoadDocument(map); mapping.Resize(1440, 900);
        mapping.Key('1'); mapping.Key('A', ctrl: true);
        var bounds = mapping.View.SamplesetBounds;
        mapping.Click(bounds.X + 10, bounds.Y + 10); mapping.ClickText(L.Get("timing.normal"));
        bounds = mapping.View.AdditionsBounds;
        mapping.Click(bounds.X + 10, bounds.Y + 10); mapping.ClickText(L.Get("timing.soft"));
        mapping.ClickText(L.Get("assist.finish"));
        resolver = new(mapping.View.Document, mapping.View.Conversion.Objects, inspectResources: false);
        Check(resolver.Describe(note) is { NormalSet: 1, AdditionSet: 2, Additions: 4, Index: 2, Volume: 60 },
            "Mapping panel enables spinner banks and additions without changing index or volume");
        mapping.Key('Z', ctrl: true); mapping.Key('Z', ctrl: true); mapping.Key('Z', ctrl: true);
        Check(map.ContentEquals(mapping.View.Document), "Mapping spinner edits undo together with preserved fields");
        Console.WriteLine("PASS spinner end placement, Normal selection, mapping controls, timing, playback, export and undo");
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
        var noteLock = ui.View.HitsoundingLockBounds(0);
        ui.Click(noteLock.X + noteLock.Width / 2, noteLock.Y + 12);
        Check(baseline.ContentEquals(ui.View.Document), "Unlocking Notes preserves benchmark content");
        ui.View.UpdateTransport(70500, 90000, true, false, false, null, null); ui.Paint();
        var noteLane = ui.View.HitsoundingNotesLaneBounds;
        float noteX = noteLane.X + noteLane.Width * .3f, noteY = noteLane.Y + noteLane.Height / 2;
        ui.Click(noteX, noteY); ui.Key(65, ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Selecting Notes preserves benchmark content");
        ui.View.PointerDown(noteX, noteY, 0, false, false);
        Check(ui.View.WantsCapture, "Group benchmark starts an editable Notes gesture");
        Measure("Notes group ghost");
        Check(baseline.ContentEquals(ui.View.Document), "Held Notes ghosts preserve benchmark content");
        ui.Key(27);
        Check(baseline.ContentEquals(ui.View.Document), "Group ghosts retain committed content");
        Check(ReferenceEquals(conversion, ui.View.Conversion), "Group ghosts reuse committed conversion");
        ui.Click(noteLock.X + noteLock.Width / 2, noteLock.Y + 12);
        ui.View.UpdateTransport(30500, 90000, true, false, false, null, null); ui.Paint();
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
    internal static FruitsAtelier.App.Rendering.Rect[] LaneBlocks(Ui ui, int lane)
    {
        var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100 && t.Y >= ui.View.HitsoundingNotesLaneBounds.Y && t.X >= ui.View.HitsoundingNotesLaneBounds.X - 146);
        return ui.Canvas.Outlines.Where(o => o.Color is 0x65BF8A or 0xE0B55F or 0xDC747B
            && o.Bounds.Right >= ui.View.HitsoundingNotesLaneBounds.X && o.Bounds.Y <= label.Y + 8 && o.Bounds.Bottom > label.Y + 8)
            .Select(o => o.Bounds).OrderBy(b => b.X).ToArray();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
