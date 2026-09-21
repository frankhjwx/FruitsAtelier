using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class DistanceSnapPresetTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Snapping()
    {
        var reference = new DistanceSnap.Reference(Guid.NewGuid(), new(1000, 256), new(1000, 256), .28, 0);
        double[] presets = [1, 2, 4, 7.25];
        foreach (var (mouse, expected) in new[] { (260d, 256d), (290d, 291d), (330d, 326d), (399d, 396d), (220d, 221d), (115d, 116d), (2d, 2.25d) })
        {
            var point = DistanceSnap.SnapMultiple(new(1125, mouse), reference, presets, 1, out bool outside);
            Check(Math.Abs(point.X - expected) < .001 && !outside, "Nearest DS or implicit zero was not selected.");
        }
        var edge = reference with { Start = new(1000, 500), End = new(1000, 500) };
        Check(DistanceSnap.SnapMultiple(new(1125, 480), edge, presets, 1, out _).X == 465, "Invalid right candidates hid a valid left candidate.");
        Check(DistanceSnap.SnapMultiple(new(5000, 200), reference, presets, 1, out bool invalid).X == 256 && !invalid,
            "Zero DS did not remain available when every nonzero distance exceeded the field.");
        Check(DistanceSnap.SnapMultiple(new(1125, 260), reference, [], 1, out _).X == 256, "Empty list omitted zero DS.");
        Check(DistanceSnap.SnapMultiple(new(1125, 290), reference, [], 1, out _).X == 291, "Empty list omitted legacy spacing.");
        Check(DistanceSnap.SnapMultiple(new(1000, 290), reference, presets, 1, out _).X == 290, "Simultaneous notes were snapped.");
        var map = new MapDocument(); map.DistanceSnapRatios.AddRange(presets);
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(map));
        Check(restored.DistanceSnapRatios.SequenceEqual(presets), "Map DS values did not survive project persistence.");
        var clone = map.DeepClone(); clone.DistanceSnapRatios[0] = .25;
        Check(map.DistanceSnapRatios[0] == 1 && !map.ContentEquals(clone), "Map snapshots share DS values or ignore changes.");
        Check(ProjectSerializer.Read("{\"SchemaVersion\":1,\"Document\":{}}").DistanceSnapRatios.Count == 0, "Old maps did not default to an empty list.");
        map.DistanceSnapRatios.AddRange([1, 1, 1, 1, 1]);
        try { ProjectSerializer.Serialize(map); throw new Exception("More than eight DS values were saved."); }
        catch (InvalidDataException) { }

    }

    public static void Dialog()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            Strings.SetLanguage(language);
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 10000, IsDemo = false };
            map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = -25, Uninherited = false });
            map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
            ui.LoadDocument(map);
            var baseline = ui.View.Document.DeepClone();
            var button = ui.View.AssistButtonBounds[5];
            ui.View.PointerMove(button.X + 10, button.Y + 10, false, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.configure")), "Hover did not reveal DS configuration.");
            ui.ClickText(Strings.Get("ds.configure"));
            Check(ui.View.DistanceSnapDialogVisible && ui.View.DistanceSnapSliderBounds.Count == 0, "Dialog did not open empty.");
            double start = ui.View.PlayheadMs, duration = TimingMap.At(map, start).BeatLengthMs / ui.View.SnapDivisor;
            ConvertedCatchObject Note(double time) => new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, time, 256, 256, 256, 0);
            var range = HyperDashCalculator.Calculate([Note(start), Note(start + duration)], map.CircleSize)[0].Movement!.Value;
            double unit = duration * DistanceSnap.BaseVelocity(map, start);
            foreach (double boundary in new[] { range.StandLimit, range.WalkLimit, range.DashLimit })
                Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("assist.ratio", boundary / unit)), "Reference transition differs from a fresh catcher departure.");
            ui.Key('F'); ui.Key('Y'); ui.Key(116); ui.ClickMap(1250, 300);
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.DistanceSnapEnabled && !ui.View.IsTestplaying, "Modal input escaped to editor.");
            ui.ClickText(Strings.Get("ds.add"));
            var slider = ui.View.DistanceSnapSliderBounds.Single();
            float sliderX = slider.X + slider.Width * .375f;
            ui.View.PointerDown(sliderX, slider.Y + 16, 0, false, false); ui.Paint();
            Check(ui.View.WantsCapture, "DS slider did not capture dragging.");
            ui.View.PointerMove(slider.X + slider.Width * .625f, slider.Y + 16, false, false);
            ui.View.PointerUp(slider.X + slider.Width * .625f, slider.Y + 16, 0); ui.Paint();
            double expected = Math.Round((range.WalkLimit + range.DashLimit) / 2 / unit, 1, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", expected)), "Slider and numeric field disagree.");
            Check(!ui.View.WantsCapture, "DS slider did not release capture.");
            ui.View.PointerDown(slider.X + slider.Width * .625f, slider.Y + 16, 0, true, false); ui.Paint();
            expected = Math.Round((range.WalkLimit + range.DashLimit) / 2 / unit, 2, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", expected)), "Shift drag did not snap to hundredths.");
            ui.View.SetModifiers(false, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", Math.Round(expected, 1))), "Releasing Shift did not restore tenths.");
            ui.View.PointerMove(slider.X + slider.Width * .625f, slider.Y + 16, true, false);
            ui.View.PointerUp(slider.X + slider.Width * .625f, slider.Y + 16, 0); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", expected)), "Mouse release changed Shift precision.");
            var handle = ui.Canvas.Lines.Single(l => l.X1 == l.X2 && l.Y1 == slider.Y + 5 && l.Y2 == slider.Y + 27 && l.Width == 2);
            float barLeft = (ui.Width - 640) / 2 + 20, pointerY = (ui.Height - 570) / 2 + 41;
            var pointer = ui.Canvas.Lines.Single(l => l.X1 == l.X2 && l.Y1 == pointerY && l.Y2 == pointerY + 10);
            Check(Math.Abs((pointer.X1 - barLeft) / 600 - (handle.X1 - slider.X) / slider.Width) < .001,
                "Reference pointer and slider handle disagree.");
            ui.View.PointerDown(pointer.X1, pointerY + 5, 0, false, false);
            Check(ui.View.WantsCapture, "Reference pointer did not capture dragging.");
            ui.View.PointerMove(barLeft + 600 * .375f, pointerY + 5, false, false); ui.Paint();
            double pointerRatio = Math.Round((range.StandLimit + range.WalkLimit) / 2 / unit, 1, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", pointerRatio)),
                "Reference drag did not update its DS row in tenths.");
            ui.View.PointerMove(barLeft + 600 * .375f, pointerY + 5, true, false); ui.Paint();
            pointerRatio = Math.Round((range.StandLimit + range.WalkLimit) / 2 / unit, 2, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", pointerRatio)),
                "Reference drag ignored Shift precision.");
            ui.Key(27);
            Check(!ui.View.WantsCapture && ui.View.DistanceSnapDialogVisible, "Escape did not cancel just the reference drag.");
            ui.View.PointerUp(barLeft + 600 * .375f, pointerY + 5, 0); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", expected)),
                "Cancelled reference drag changed the DS value.");
            pointer = ui.Canvas.Lines.Single(l => l.X1 == l.X2 && l.Y1 == pointerY && l.Y2 == pointerY + 10);
            ui.View.PointerDown(pointer.X1, pointerY + 5, 0, true, false);
            ui.View.PointerMove(barLeft + 600 * .375f, pointerY + 5, true, false);
            ui.View.PointerUp(barLeft + 600 * .375f, pointerY + 5, 0); ui.Paint();
            expected = pointerRatio;
            Check(!ui.View.WantsCapture, "Reference drag did not release capture.");
            ui.View.PointerDown(sliderX, slider.Y + 16, 0, false, false);
            ui.View.CancelInteraction(); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", expected)), "Cancelled drag did not restore value.");
            var value = ui.Canvas.Texts.Single(t => t.Y == slider.Y + 9 && t.Value == Strings.Get("ds.ratio", expected));
            ui.Click(value.X + 3, value.Y + 3); ui.Type("0"); ui.Key(13);
            Check(ui.View.IsEditingText && ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.invalid")), "Invalid ratio was accepted.");
            ui.Key('A', ctrl: true); ui.Type("1.259"); ui.Key(13);
            for (int i = 1; i < 8; i++) ui.ClickText(Strings.Get("ds.add"));
            ui.ClickText(Strings.Get("ds.add"));
            ui.ClickText(Strings.Get("library.apply"));
            Check(!ui.View.DistanceSnapDialogVisible && ui.View.Document.DistanceSnapRatios.Count == 8, "Apply or preset cap failed.");
            Check(ui.View.Document.DistanceSnapRatios[0] == 1.25, "Field edits were lost.");
            var configured = ui.View.Document.DeepClone(); configured.DistanceSnapRatios.Clear();
            Check(baseline.ContentEquals(configured), "Configuration moved beatmap objects.");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.DistanceSnapRatios.Count == 0, "DS Apply did not undo as one step.");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.DistanceSnapRatios.Count == 8, "DS Apply did not redo.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            var remove = ui.Canvas.Texts.First(t => t.Value == Strings.Get("ds.remove")); ui.Click(remove.X + 2, remove.Y + 2);
            ui.ClickText(Strings.Get("mac.cancel"));
            Check(ui.View.Document.DistanceSnapRatios.Count == 8, "Cancel persisted a draft deletion.");
            ui.Resize(960, 640); ui.View.OpenDistanceSnapDialog(); ui.Paint();
            ui.Key(9); ui.Type("2.5"); ui.Key(9); ui.Type("3.5");
            ui.Key(9, shift: true); ui.Type("4.5");
            ui.Key(9, shift: true); ui.Type("8.5"); ui.Key(9);
            ui.ClickText(Strings.Get("library.apply"));
            Check(ui.View.Document.DistanceSnapRatios[0] == 4.5 && ui.View.Document.DistanceSnapRatios[1] == 3.5
                && ui.View.Document.DistanceSnapRatios[7] == 8.5, "Tab navigation did not commit and wrap between DS rows.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            double time = ui.View.PlayheadMs;
            ui.View.Wheel(480, 300, -1200, false); ui.Paint();
            Check(ui.View.PlayheadMs == time, "Modal scrolling moved the playhead.");
            ui.Key(27); Check(!ui.View.DistanceSnapDialogVisible, "Esc did not close configuration.");
            var saved = ui.View.Document.DeepClone();
            ui.View.LoadProject(BeatmapProject.FromDocuments([saved, new MapDocument()])); ui.Paint();
            Check(ui.View.SwitchDifficulty(1), "Could not switch to unconfigured difficulty.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            Check(ui.View.DistanceSnapSliderBounds.Count == 0, "Unconfigured map inherited another map's DS list.");
            ui.Key(27); Check(ui.View.SwitchDifficulty(0), "Could not return to configured difficulty.");
            Check(ui.View.Document.DistanceSnapRatios.SequenceEqual(saved.DistanceSnapRatios), "Map switch lost DS configuration.");
        }
        Strings.SetLanguage("en");
    }
}
