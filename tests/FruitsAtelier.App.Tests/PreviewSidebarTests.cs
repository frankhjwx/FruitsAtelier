using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class PreviewSidebarTests
{
    public static void DisplayModesAndOverscan()
    {
        var ui = new Ui(overview: false);
        var map = new MapDocument { ApproachRate = 5, CircleSize = 5, DurationMs = 10000 };
        for (int i = 0; i < 100; i++) map.Fruits.Add(new Fruit { TimeMs = i * 100, X = 256 });
        ui.LoadDocument(map); ui.Resize(1440, 700); ui.OpenPreview();
        Near(4d / 3, ui.View.PreviewViewport.Width / ui.View.PreviewViewport.Height);
        CheckStandardViewport();
        ui.ClickText("16:9");
        Near(16d / 9, ui.View.PreviewViewport.Width / ui.View.PreviewViewport.Height);
        CheckStandardViewport();
        ui.ClickText(L.Get("preview.fit"));
        var shortViewport = ui.View.PreviewViewport;
        RecordingCanvas.Dot[] Fruits() => ui.Canvas.Circles.Where(c => c.Filled && c.Color == 0xFFFFFF && c.X > ui.Plot.Right).ToArray();
        var shortFruits = Fruits();
        ui.Resize(1440, 1000);
        Check(ui.View.PreviewViewport.Height > shortViewport.Height + 290, "Fit uses additional vertical space");
        Near(shortViewport.Width, ui.View.PreviewViewport.Width);
        Check(Fruits().Length > shortFruits.Length, "taller Fit viewport shows more future notes");
        Near(shortFruits[0].Radius, Fruits()[0].Radius);
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "display controls do not create edits");
        var plate = ui.Canvas.Fills.Single(f => f.Color == 0xB5C9D0).Bounds;
        var reference = ui.Canvas.Outlines.Single(o => o.Color == 0x2B3442).Bounds;
        Near(4d / 3, reference.Width / reference.Height);
        Near(ui.View.PreviewViewport.Bottom - 6, reference.Bottom);
        Near(reference.Y + reference.Height * (0.15 + 340 * 1.6 / 768), plate.Y);
        Check(plate.Y > reference.Y && plate.Bottom < reference.Bottom, "Fit reference includes the catcher area");
        double speed = CatchScrollTiming.PixelsPerMs(5, reference.Width * .8f);
        float radius = Fruits()[0].Radius;
        double edgeTime = ui.View.PlayheadMs + (plate.Y - ui.View.PreviewViewport.Y + radius / 2) / speed;
        ui.View.Document.Fruits.Clear();
        ui.View.Document.Fruits.Add(new Fruit { TimeMs = edgeTime, X = 256 }); ui.Paint();
        var edge = Fruits().Single();
        Check(edge.Y < ui.View.PreviewViewport.Y && edge.Y + edge.Radius > ui.View.PreviewViewport.Y, "partially entering fruit is drawn before its centre enters");
        void CheckStandardViewport()
        {
            var viewport = ui.View.PreviewViewport;
            var mode = ui.Canvas.Texts.Single(t => t.Value == L.Get("preview.mode"));
            var resolution = ui.Canvas.Texts.Single(t => t.Value == L.Get("preview.resolution"));
            Check(mode.Y < resolution.Y && resolution.Y < viewport.Y, "option labels stay above centred preview");
            Near(ui.Canvas.Clips.Last().X + ui.Canvas.Clips.Last().Width / 2, viewport.X + viewport.Width / 2);
            float availableTop = resolution.Y - 81 + 107;
            float availableBottom = ui.View.PreviewResizeBounds.Bottom - 20;
            Near((availableTop + availableBottom) / 2, viewport.Y + viewport.Height / 2);
            Check(!ui.Canvas.Outlines.Any(o => o.Bounds == viewport || o.Color == 0x2B3442), "standard preview has no viewport or internal field outline");
            Check(!ui.Canvas.Lines.Any(l => l.Color == 0x677085), "preview has no catch guide line");
            var catcherPlate = ui.Canvas.Fills.Single(f => f.Color == 0xB5C9D0).Bounds;
            Near(viewport.Y + viewport.Height * (0.15 + 340 * 1.6 / 768), catcherPlate.Y);
            Check(catcherPlate.Y > viewport.Y && catcherPlate.Bottom < viewport.Bottom, "catcher plate is inside full legacy viewport");
        }
    }
    public static void AutomaticCatcher()
    {
        var map = new MapDocument { CircleSize = 5 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 2000, X = 456 }, new Fruit { TimeMs = 3000, X = 100 }]);
        var autoplay = new CatchAutoPreview(CatchStreamConverter.Convert(map).Objects, 5);
        Near(256, autoplay.At(1500).X);
        Check(autoplay.At(1800).X > 256 && autoplay.At(1800).X < 456, "catcher moves continuously toward the next fruit");
        foreach (var fruit in map.Fruits) Near(fruit.X, autoplay.At(fruit.TimeMs).X);
        var beforeSeek = autoplay.At(1800); autoplay.At(2300);
        Check(beforeSeek == autoplay.At(1800), "backward seeking restores catcher position");
        Near(256, new CatchAutoPreview([], 5).At(1000).X);
    }
    public static void PlateEffects()
    {
        ConvertedCatchObject Item(int index, CatchObjectKind kind, double time) => new(Guid.NewGuid(), index, kind, time, 256, 256, 256, 0);
        var objects = new[] { Item(0, CatchObjectKind.Fruit, 0), Item(1, CatchObjectKind.Fruit, 100),
            Item(2, CatchObjectKind.Droplet, 150), Item(3, CatchObjectKind.TinyDroplet, 160),
            Item(4, CatchObjectKind.Banana, 170), Item(5, CatchObjectKind.Fruit, 200), Item(6, CatchObjectKind.Fruit, 400) };
        var auto = new CatchAutoPreview(objects, 5);
        var plate = new CatchPlatePreview(objects, auto, 5, [(objects[5].SourceId, objects[5].EventIndex)]);
        var stacked = plate.At(120, 256).ToArray();
        Check(stacked.Length == 2 && stacked[0].Y != stacked[1].Y, "fruits form a stack");
        var shifted = plate.At(120, 300).ToArray(); Near(44, shifted[0].X - stacked[0].X);
        var beforeBurst = plate.At(180, 256).ToArray();
        Check(beforeBurst.Any(s => s.Object.Kind == CatchObjectKind.Banana && s.Opacity == 1), "bananas remain on plate");
        Check(beforeBurst.Any(s => s.Object.Kind == CatchObjectKind.Droplet && s.Opacity < 1), "droplets eject immediately");
        Check(beforeBurst.All(s => s.Object.Kind != CatchObjectKind.TinyDroplet), "tiny droplets do not stack");
        var burst = plate.At(300, 256).ToArray();
        Check(burst.All(s => s.Opacity < 1), "completed combo explodes and fades");
        Check(plate.At(1000, 256).Single().Object == objects[6], "old explosion expires while new combo stays");
        Check(stacked.SequenceEqual(plate.At(120, 256)), "stack restores exactly after rewind");
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.AddRange([new Fruit { TimeMs = 100, X = 256 }, new Fruit { TimeMs = 200, X = 256 },
            new Fruit { TimeMs = 400, X = 256, OriginalLine = "256,192,400,5,0" }, new Fruit { TimeMs = 600, X = 256 }]);
        ui.LoadDocument(map); ui.OpenPreview();
        void Seek(double time) { ui.View.UpdateTransport(time, 5000, true, false, false, null, null); ui.Paint(); }
        Seek(250);
        Check(ui.Canvas.Circles.Any(c => c.X > ui.Plot.Right && c.Color == 0xFFFFFF && c.Opacity < 1), "New Combo metadata triggers prior group explosion");
        var drawn = ui.Canvas.Circles.ToArray();
        Seek(2000); Seek(250);
        Check(drawn.SequenceEqual(ui.Canvas.Circles), "plate effects restore after seek");
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "plate effects preserve content");
    }
    public static void DashEffects()
    {
        var map = new MapDocument { CircleSize = 5, DurationMs = 5000 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 1250, X = 456 },
            new Fruit { TimeMs = 2000, X = 100 }, new Fruit { TimeMs = 2100, X = 500 }]);
        var auto = new CatchAutoPreview(CatchStreamConverter.Convert(map).Objects, 5);
        Check(auto.At(1040).Dashing && !auto.HyperDashingAt(1040), "ordinary dash has its own state");
        Check(!auto.HyperDashingAt(1999) && auto.HyperDashingAt(2000) && !auto.HyperDashingAt(2100), "hyper begins on departure fruit and ends at target");
        Check(auto.HyperTintAt(2050) > 0 && auto.HyperTintAt(2200) > 0 && auto.HyperTintAt(2280) == 0, "hyper body colour transitions in and out");
        Check(auto.HyperStarts(1900, 2200).SequenceEqual(new double[] { 2000 }), "one afterimage per hyper start");
        var ui = new Ui(); ui.LoadDocument(map); ui.OpenPreview();
        void Seek(double time) { ui.View.UpdateTransport(time, 5000, true, false, false, null, null); ui.Paint(); }
        RecordingCanvas.Dot[] Effects() => ui.Canvas.Circles.Where(c => c.X > ui.Plot.Right && c.Filled && c.Color is 0xB5C9D0 or 0xFF0000).ToArray();
        Seek(1100);
        Check(Effects().Any(c => c.Color == 0xB5C9D0 && c.Opacity > 0 && c.Opacity <= .4f), "dash draws fading catcher ghosts");
        Seek(2050);
        Check(Effects().Any(c => c.Color == 0xFF0000 && c.Opacity > .4f), "hyper draws tinted body and start afterimage");
        var snapshot = Effects(); ui.Paint();
        Check(snapshot.SequenceEqual(Effects()), "paused frame is stable");
        Seek(4000); Check(!Effects().Any(c => c.Color == 0xFF0000 || c.Opacity < 1), "expired trails disappear");
        Seek(2050); Check(snapshot.SequenceEqual(Effects()), "seeking reconstructs identical effects");
        Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "effects preserve beatmap");
    }
    public static void Sidebar()
    {
        var ui = new Ui(overview: false);
        var map = new MapDocument { ApproachRate = 8, CircleSize = 5, DurationMs = 10000 };
        map.Fruits.Add(new Fruit { TimeMs = 200, X = 200 });
        ui.LoadDocument(map); var baseline = ui.View.Document.DeepClone();
        Check(!ui.View.CatchPreviewVisible && !ui.Canvas.Texts.Any(text => text.Value == L.Get("ui.preview")), "preview starts collapsed");
        ui.ClickMap(200, 200);
        Check(!ui.Canvas.Texts.Any(text => text.Value == L.Get("ui.timeField") || text.Value == L.Get("ui.deleteFruit")), "details contain no object inspector");
        float closedWidth = ui.View.CanvasPlotBounds.Width;
        ui.OpenPreview();
        Check(ui.View.CatchPreviewVisible && ui.View.CanvasPlotBounds.Width < closedWidth, "drawer occupies canvas space");
        ui.ClickText(L.Get("preview.easy"));
        Near(4, ui.View.PreviewApproachRate); Near(2.5, ui.View.PreviewCircleSize);
        Check(ui.Canvas.Texts.Any(text => text.Value == L.Get("ui.previewStats", "4", "2.5", "EZ")), "Easy stats");
        ui.ClickText(L.Get("preview.hardRock"));
        Near(10, ui.View.PreviewApproachRate); Near(6.5, ui.View.PreviewCircleSize);
        Check(ui.Canvas.Texts.Any(text => text.Value == L.Get("ui.previewStats", "10", "6.5", "HR")), "Hard Rock stats use the gameplay AR cap");
        ui.ClickText(L.Get("preview.normal"));
        Near(8, ui.View.PreviewApproachRate); Near(5, ui.View.PreviewCircleSize);
        float before = ui.View.CanvasPlotBounds.Width;
        var resize = ui.View.PreviewResizeBounds;
        ui.View.PointerDown(resize.X + 4, resize.Y + 20, 0, false, false);
        Check(ui.View.WantsCapture, "splitter captures pointer");
        ui.View.PointerMove(resize.X - 60, resize.Y + 20, false, false); ui.Paint();
        ui.View.PointerUp(resize.X - 60, resize.Y + 20, 0); ui.Paint();
        Check(ui.View.CanvasPlotBounds.Width < before && !ui.View.WantsCapture, "splitter resizes and releases capture");
        var toggle = ui.View.PreviewToggleBounds; ui.Click(toggle.X + 10, toggle.Y + 10);
        Near(closedWidth, ui.View.CanvasPlotBounds.Width);
        Check(ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty, "preview controls preserve content and history");
        string language = L.Get("ui.languageButton", System.Globalization.CultureInfo.GetCultureInfo(L.Language).NativeName) + " ▾";
        Check(ui.Canvas.Texts.Single(text => text.Value == L.Get("library.back")).X > ui.Canvas.Texts.Single(text => text.Value == language).X, "Library is right of Language");
        ui.ClickText(L.Get("ui.file"));
        var shortcuts = ui.Canvas.Texts.Where(text => text.Value.StartsWith("Ctrl +", StringComparison.Ordinal)).ToArray();
        Check(shortcuts.Length >= 4, "menu shortcuts are separate labels");
        var edges = shortcuts.Select(text => text.X + ((ICanvas)ui.Canvas).MeasureText(text.Value, 12)).ToArray();
        Check(edges.Max() - edges.Min() < .01, "shortcut right edges align");
    }
    public static void LegacyConversion()
    {
        var ui = new Ui();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n160,192,1000,2,0,L|300:192,1,140\n");
        ui.LoadDocument(map);
        Check(ui.View.LegacyConversionBounds.Width == 0, "unselected slider has no conversion button");
        ui.ClickMap(1000, 160);
        var button = ui.View.LegacyConversionBounds;
        Check(button.Width > 0, "selected hovered legacy slider offers conversion");
        ui.View.PointerMove(button.Right - 10, button.Y + 15, false, false); ui.Paint();
        Check(ui.View.LegacyConversionBounds == button, "button stays reachable while moving into it");
        ui.Click(button.Right - 10, button.Y + 15);
        Check(ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Count == 1, "button converts slider");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(map), "conversion undo restores source");
    }
    public static void HardRock()
    {
        var map = new MapDocument();
        map.Fruits.AddRange([new Fruit { TimeMs = 0, X = 100 }, new Fruit { TimeMs = 300, X = 120 }, new Fruit { TimeMs = 1600, X = 500 }, new Fruit { TimeMs = 1800, X = 510 }]);
        var source = CatchStreamConverter.Convert(map); var baseline = map.DeepClone();
        var hr = CatchPreviewMods.HardRock(map, source);
        Check(hr.Select(item => item.X).SequenceEqual(new double[] { 100, 140, 500, 510 }), "HR spacing, reset and edge constraints");
        map.Fruits.Add(new Fruit { TimeMs = 1900, X = 510 });
        source = CatchStreamConverter.Convert(map);
        hr = CatchPreviewMods.HardRock(map, source);
        Check(hr[^1].X is >= 490 and <= 512 && hr.SequenceEqual(CatchPreviewMods.HardRock(map, source)), "HR random offsets are bounded and deterministic");
        Check(source.Objects[^1].X == 510 && baseline.Fruits[1].X == map.Fruits[1].X, "mod calculation preserves source objects");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .01, $"Expected {expected}, got {actual}");
}
