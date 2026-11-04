using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingPanelTests
{
    public static void Editing()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1, Volume = 80 });
        foreach (int time in new[] { 1000, 1250, 1500 }) map.Fruits.Add(new() { TimeMs = time, X = 256 });
        var ui = new Ui(overview: false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage(); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        var block = NormalBlocks()[0];
        ui.Click(block.X + block.Width / 2, block.Y + block.Height / 2);
        Check(ui.View.PlayheadMs == 1000, "Selecting a sound locates its exact note");
        Check(ui.Canvas.Texts.Count(t => t.Value == L.Get("hitsounding.index") && t.X > 1100) == 1, "Timing Helper exposes only the timing sample index");
        ui.View.Wheel(block.X, block.Y, -120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1125, "One wheel notch advances one quarter-beat snap");
        ui.View.Wheel(block.X, block.Y, 120, false, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1000 && baseline.ContentEquals(ui.View.Document), "Reverse wheel restores time without editing content");
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
        Check(ui.Canvas.PaintCalls.Any(p => p.Color == 0x51BDD2 && p.FillBounds is not null && p.Opacity == .5f), "Draw previews use half opacity");
        ui.View.PointerUp(finishX, whistleY, 0); ui.Paint();
        Check(ui.View.Document.HitsoundOverrides.Count == 2, "A paint stroke covers crossed notes");
        ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "One undo reverses the complete paint stroke");
        ui.Click(startX, whistleY); ui.Paint();
        var whistle = ui.Canvas.Outlines.First(o => o.Color == 0x51BDD2).Bounds;
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

        FruitsAtelier.App.Rendering.Rect[] NormalBlocks() => ui.Canvas.Outlines.Where(o => o.Color == 0x56B9AA).Select(o => o.Bounds).OrderBy(b => b.X).ToArray();
        float LaneY(int lane) { var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100); return label.Y + 8; }

    }
    public static void Run()
    {
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
        Check(!ui.View.IsDirty && baseline.ContentEquals(ui.View.Document), "Mode changes leave content intact");
        for (int i = 0; i < 7; i++) Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.lane" + i)), "Seven lane labels");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x66AAFF && Math.Abs(l.Y2 - l.Y1 - 5) < .01), "Quarter subdivisions use short blue ticks");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xFF6688 && Math.Abs(l.Y2 - l.Y1 - 9) < .01), "Half beats use medium red ticks");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0x829DC7), "Actual slider ticks have audio blocks");
        var blocks = ui.Canvas.Outlines.Where(f => f.Color == 0x56B9AA).Select(f => f.Bounds).OrderBy(r => r.X).ToArray();
        Check(ui.Canvas.Texts.Any(t => t.Value == "S" && t.Y >= blocks[0].Y + 3 && t.Y < blocks[0].Y + blocks[0].Height / 2)
            && ui.Canvas.Texts.Any(t => t.Value == "1" && t.Y >= blocks[0].Y + blocks[0].Height / 2 && t.Y < blocks[0].Bottom - 3), "Bank and index occupy separate padded lines");
        Check(blocks.Length >= 2 && blocks.All(b => b.Width <= EditorView.HitsoundingTagWidth), "Sound tags retain the preferred width or shrink to their neighbours");
        Check(ui.Canvas.Circles.Any(d => Math.Abs(blocks[0].X + blocks[0].Width / 2 - d.X) < .1), "Sound tag centers match their note timestamps");
        Check(ui.Canvas.Texts.Any(t => t.Value == "1" && t.Y > 218), "Notes reuse numbered mapping timeline markers");
        float anchorX = blocks[0].X + blocks[0].Width / 2, anchorY = blocks[0].Y + 5;
        ui.View.Wheel(anchorX, anchorY, 120, false, false, true); ui.Paint();
        var zoomed = ui.Canvas.Outlines.Where(f => f.Color == 0x56B9AA).Select(f => f.Bounds).OrderBy(r => r.X).ToArray();
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
        var precise = ui.Canvas.Outlines.Where(f => f.Color == 0x56B9AA).Select(f => f.Bounds).OrderBy(r => r.X).ToArray();
        Check(Math.Abs(precise[0].Right - precise[1].X) < .1, "Precise HS uses decoded length and the next-note boundary");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x56B9AA && l.X1 >= precise[0].X && l.X1 < precise[0].Right
            && l.Y1 > precise[0].Y && l.Y2 < precise[0].Bottom && l.Y2 - l.Y1 > 10), "Quiet waveforms normalize inside narrow precise tags");
        Check(ui.View.CaptureDockLayouts().Hitsounding!.PreciseHitsounds, "Display mode is captured in the Hitsounding layout");
        ui.ClickText(L.Get("hitsounding.noteBased"));
        Check(ReferenceEquals(conversion, ui.View.Conversion) && !ui.View.IsDirty, "Display modes reuse conversion without content history");
        ui.View.Wheel(anchorX, anchorY, -600, false, false, true); ui.Paint();
        var dense = ui.Canvas.Outlines.Where(f => f.Color == 0x56B9AA).Select(f => f.Bounds).OrderBy(r => r.X).ToArray();
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
        ui.ClickText("Drums"); ui.ClickText("Tight");
        ui.View.DropLibraryFiles([source]); WaitImport();
        Check(File.Exists(Path.Combine(nested, "replacement.wav")), "Imports target the browsed subfolder");
        ui.View.NavigateHitsoundingDirectory(Path.GetDirectoryName(libraryRoot)); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Path.Combine("Drums", "Tight")), "Navigation cannot escape the audio library root");
        ui.ClickText("↑"); Check(ui.Canvas.Texts.Any(t => t.Value == "Tight"), "Parent navigation shows child directories");
        ui.ClickText("⌂"); Check(ui.Canvas.Texts.Any(t => t.Value == "Drums"), "Home navigation returns to the library root");
        ui.Resize(980, 620); Check(ui.Canvas.Fills.All(f => float.IsFinite(f.Bounds.Width) && f.Bounds.Width >= 0), "Narrow layout stays valid");
        void WaitImport()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do { ui.Paint(); if (!ui.View.WaveformNeedsRedraw) return; Thread.Sleep(5); } while (clock.Elapsed.TotalSeconds < 10);
            throw new TimeoutException("Library import did not finish");
        }
        Console.WriteLine("PASS Hitsounding snap ruler, tick audio, centered tags, Alt-wheel zoom, independent split docks, library and resource history");
    }
    public static void Performance()
    {
        var map = new MapDocument { DurationMs = 90000, BeatLengthMs = 500 };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 20000, BeatLengthMs = 400 },
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
        ui.View.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 200).ToArray(), 1));
        ui.ClickText(L.Get("hitsounding.precise")); Measure("Precise HS");
        Check(ReferenceEquals(conversion, ui.View.Conversion) && baseline.ContentEquals(ui.View.Document), "Views and pointer motion reuse committed conversion");
        void Measure(string mode)
        {
            var input = new List<double>(); var render = new List<double>(); var memory = new List<long>();
            for (int i = 0; i < 120; i++)
            {
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
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
