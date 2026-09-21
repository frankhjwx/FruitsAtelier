using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class DistanceSnapPresetTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Snapping()
    {
        var reference = new DistanceSnap.Reference(Guid.NewGuid(), new(1000, 256), new(1000, 256), .28, 0);
        double[] presets = [0, 1, 2, 4, 7.25];
        foreach (var (mouse, expected) in new[] { (260d, 256d), (290d, 291d), (330d, 326d), (399d, 396d), (220d, 221d), (115d, 116d), (2d, 2.25d) })
        {
            var point = DistanceSnap.SnapMultiple(new(1125, mouse), reference, presets, out bool outside);
            Check(Math.Abs(point.X - expected) < .001 && !outside, "Nearest DS or implicit zero was not selected.");
        }
        var edge = reference with { Start = new(1000, 500), End = new(1000, 500) };
        Check(DistanceSnap.SnapMultiple(new(1125, 480), edge, presets, out _).X == 465, "Invalid right candidates hid a valid left candidate.");
        Check(DistanceSnap.SnapMultiple(new(5000, 200), reference, presets, out bool invalid).X == 256 && !invalid,
            "Zero DS did not remain available when every nonzero distance exceeded the field.");
        Check(DistanceSnap.SnapMultiple(new(1125, 260), reference, [], out _).X == 256, "Empty list omitted zero DS.");
        Check(DistanceSnap.SnapMultiple(new(1125, 290), reference, [], out _).X == 256, "Empty list included a hidden nonzero spacing.");
        Check(DistanceSnap.SnapMultiple(new(1000, 290), reference, presets, out _).X == 290, "Simultaneous notes were snapped.");
        var map = new MapDocument(); map.DistanceSnapRatios.AddRange(presets);
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(map));
        Check(restored.DistanceSnapRatios.SequenceEqual(presets), "Map DS values did not survive project persistence.");
        var clone = map.DeepClone(); clone.DistanceSnapRatios[0] = .25;
        Check(map.DistanceSnapRatios[0] == 0 && !map.ContentEquals(clone), "Map snapshots share DS values or ignore changes.");
        clone.DistanceSnapRatios[0] = -.1;
        try { ProjectSerializer.Serialize(clone); throw new Exception("Negative DS was accepted."); }
        catch (InvalidDataException) { }
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
            var map = new MapDocument { DurationMs = 10000, IsDemo = false, DistanceSpacing = .4 };
            map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
            ui.LoadDocument(map); ui.SetSnapDivisor(4);
            var baseline = ui.View.Document.DeepClone();
            var button = ui.View.AssistButtonBounds[5];
            ui.View.PointerMove(button.X + 10, button.Y + 10, false, false); ui.Paint();
            ui.ClickText(Strings.Get("ds.configure"));
            Check(ui.View.DistanceSnapDialogVisible && ui.View.DistanceSnapPointerBounds.Count == 0, "Dialog did not open empty.");
            double start = ui.View.PlayheadMs, beatLength = TimingMap.At(map, start).BeatLengthMs;
            double duration = beatLength / 4;
            ConvertedCatchObject Note(double time) => new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, time, 256, 256, 256, 0);
            var range = HyperDashCalculator.Calculate([Note(start), Note(start + duration)], map.CircleSize)[0].Movement!.Value;
            double unit = duration * DistanceSnap.BaseVelocity(map, start);
            foreach (double boundary in new[] { range.StandLimit, range.WalkLimit, range.DashLimit })
                Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("assist.ratio", boundary / unit)), "Reference transition differs from a fresh departure.");
            Check(!ui.View.IsEditingText, "Preview settings entered input mode without a click.");
            var preview = ui.View.DistanceSnapPreviewBounds;
            Check(ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 17, "Quarter snap did not show sixteen intervals and the closing line.");
            ui.Click(preview.X + 50, preview.Bottom - 12);
            ui.Click(preview.X + 140, preview.Bottom - 12 - (preview.Height - 24) / 16);
            Check(ui.View.DistanceSnapPreviewFruits.Count == 2
                && ui.View.DistanceSnapPreviewFruits[0].X == ui.View.DistanceSnapPreviewFruits[1].X,
                "Empty configuration used the hidden legacy DistanceSpacing.");
            ui.ClickText(Strings.Get("ds.reset"));
            ui.Key('F'); ui.Key('Y'); ui.Key(116);
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsTestplaying, "Modal input escaped to editor.");
            var emptyTrack = ui.View.DistanceSnapTrackBounds;
            ui.Click(emptyTrack.X + emptyTrack.Width * .8f, emptyTrack.Y + 8);
            var addedPointer = ui.View.DistanceSnapPointerBounds.Single();
            Check(Math.Abs(addedPointer.X + 8 - (emptyTrack.X + emptyTrack.Width * .8f)) < 3,
                "Clicking the empty pointer row did not add at the clicked position.");
            ui.View.PointerDown(addedPointer.X + 8, addedPointer.Y + 8, 2, false, false); ui.Paint();
            ui.ClickText(Strings.Get("ds.add"));
            var track = ui.View.DistanceSnapTrackBounds;
            var pointer = ui.View.DistanceSnapPointerBounds.Single();
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", 0)), "Add did not create zero DS.");
            Check(Math.Abs(pointer.X + 8 - track.X) < .01, "New pointer was not at zero.");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", 0) && t.Y < pointer.Y), "Arrow value is missing above the pointer.");
            Check(ui.Canvas.Outlines.Any(o => o.Color == 0xC0C0C0 && o.Bounds.Y > track.Y + 100), "Zero value does not have the Stand border.");
            Drag(.625f, false);
            double expected = Math.Round((range.WalkLimit + range.DashLimit) / 2 / unit, 1, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Pointer drag did not snap to tenths.");
            Drag(.625f, true);
            expected = Math.Round((range.WalkLimit + range.DashLimit) / 2 / unit, 2, MidpointRounding.AwayFromZero);
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Shift pointer drag did not snap to hundredths.");
            pointer = ui.View.DistanceSnapPointerBounds.Single();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
            ui.View.PointerMove(track.X + track.Width * .2f, track.Y + 8, false, false);
            ui.Key(27); ui.View.PointerUp(track.X, track.Y + 8, 0); ui.Paint();
            Check(ui.View.DistanceSnapDialogVisible && !ui.View.WantsCapture && ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Escape did not restore the dragged value.");
            pointer = ui.View.DistanceSnapPointerBounds.Single();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
            ui.View.PointerMove(track.X, track.Y + 8, false, false); ui.View.CancelInteraction(); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.ratio", expected)), "Capture cancellation lost the original ratio.");

            float FruitX(double x) => preview.X + 10 + (float)(x / 512) * (preview.Width - 20);
            float FruitY(double beat) => preview.Bottom - 12 - (float)(beat / 4) * (preview.Height - 24);
            ui.Click(FruitX(100), FruitY(0));
            ui.Click(FruitX(100 + expected * unit), FruitY(.25));
            var fruits = ui.View.DistanceSnapPreviewFruits;
            Check(fruits.Count == 2 && Math.Abs(fruits[1].X - fruits[0].X - expected * unit) < .001 && fruits[1].TimeMs == .25, "Preview fruit placement did not snap to the custom DS and beat.");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", expected)), "Adjacent preview fruits do not show actual DS.");
            ui.Click(FruitX(fruits[1].X + 1), FruitY(.5));
            Check(Math.Abs(fruits[2].X - fruits[1].X) < .001, "Preview omitted implicit zero DS.");
            Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 0)), "Zero DS is missing from the preview readout.");
            ui.View.PointerDown(FruitX(fruits[2].X), FruitY(.5), 2, false, false); ui.Paint();
            Check(fruits.Count == 2, "Right click did not remove a preview fruit.");
            ui.View.PointerDown(preview.X + 40, preview.Y + 40, 1, false, false);
            ui.View.PointerMove(preview.X + 80, preview.Y + 80, false, false);
            ui.View.PointerUp(preview.X + 80, preview.Y + 80, 1);
            ui.View.Wheel(preview.X + 50, preview.Y + 50, -1200, false); ui.Paint();
            Check(ui.View.PlayheadMs == start && preview == ui.View.DistanceSnapPreviewBounds && baseline.ContentEquals(ui.View.Document), "Preview navigation or placement modified the map.");

            var subdivision = ui.View.DistanceSnapSubdivisionBounds;
            int[] divisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16];
            for (int i = 0; i < divisors.Length; i++)
            {
                float x = subdivision.X + 7 + (subdivision.Width - 38) * i / (divisors.Length - 1);
                ui.View.PointerDown(x, subdivision.Y + 15, 0, false, false);
                Check(ui.View.WantsCapture, "Snap slider did not capture input.");
                ui.View.PointerMove(x, subdivision.Y + 15, false, false);
                ui.View.PointerUp(x, subdivision.Y + 15, 0); ui.Paint();
                Check(!ui.View.WantsCapture && ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 4 * divisors[i] + 1,
                    "Snap slider did not select a supported divisor or draw the final line.");
            }
            ui.View.PointerDown(subdivision.X + 7, subdivision.Y + 15, 0, false, false);
            ui.Key(27); ui.View.PointerUp(subdivision.X + 7, subdivision.Y + 15, 0); ui.Paint();
            Check(ui.View.DistanceSnapDialogVisible && ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 65, "Escape did not restore the preview Snap.");
            ui.View.PointerDown(subdivision.X + 7, subdivision.Y + 15, 0, false, false);
            ui.View.CancelInteraction(); ui.Paint();
            Check(!ui.View.WantsCapture && ui.Canvas.Lines.Count(l => l.X1 == preview.X && l.X2 == preview.Right && l.Y1 == l.Y2) == 65, "Lost capture did not restore Snap.");
            Check(ui.View.SnapDivisor == 4 && baseline.ContentEquals(ui.View.Document), "Preview Snap changed editor settings or content.");

            for (int i = 1; i < 9; i++) ui.ClickText(Strings.Get("ds.add"));
            Check(ui.View.DistanceSnapPointerBounds.Count == 8, "Preset cap failed.");
            ui.Click(track.X + track.Width * .95f, track.Y + 8);
            Check(ui.View.DistanceSnapPointerBounds.Count == 8, "Empty-row click exceeded the preset cap.");
            var arrowLabels = ui.Canvas.Texts.Where(t => t.Y < track.Y && t.Y > track.Y - 130 && t.Value.EndsWith("x", StringComparison.Ordinal)).ToArray();
            Check(arrowLabels.Length == 8, "Not all eight arrows have value labels.");
            pointer = ui.View.DistanceSnapPointerBounds.Last();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 2, false, false); ui.Paint();
            Check(ui.View.DistanceSnapPointerBounds.Count == 7, "Right click did not delete an arrow.");
            ui.ClickText(Strings.Get("library.apply"));
            Check(!ui.View.DistanceSnapDialogVisible && ui.View.Document.DistanceSnapRatios.Count == 7, "Apply failed.");
            Check(ui.View.Document.DistanceSnapRatios.SequenceEqual(ui.View.Document.DistanceSnapRatios.Order()), "Applied values are not sorted.");
            var configured = ui.View.Document.DeepClone(); configured.DistanceSnapRatios.Clear();
            Check(baseline.ContentEquals(configured), "Configuration moved beatmap objects.");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.DistanceSnapRatios.Count == 0, "Apply did not undo as one step.");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.DistanceSnapRatios.Count == 7, "Apply did not redo.");
            ui.Resize(960, 640); ui.View.OpenDistanceSnapDialog(); ui.Paint();
            Check(ui.View.DistanceSnapPreviewFruits.Count == 0 && ui.Canvas.Lines.Count(l => l.X1 == ui.View.DistanceSnapPreviewBounds.X && l.X2 == ui.View.DistanceSnapPreviewBounds.Right && l.Y1 == l.Y2) == 17, "Reopening did not reset the preview to editor settings.");
            pointer = ui.View.DistanceSnapPointerBounds.Last();
            ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 2, false, false); ui.Paint();
            ui.ClickText(Strings.Get("mac.cancel"));
            Check(ui.View.Document.DistanceSnapRatios.Count == 7, "Cancel persisted a draft deletion.");
            var saved = ui.View.Document.DeepClone();
            ui.View.LoadProject(BeatmapProject.FromDocuments([saved, new MapDocument()])); ui.Paint();
            Check(ui.View.SwitchDifficulty(1), "Could not switch difficulty.");
            ui.View.OpenDistanceSnapDialog(); ui.Paint();
            Check(ui.View.DistanceSnapPointerBounds.Count == 0, "Unconfigured map inherited another map's DS list.");
            ui.Key(27); Check(ui.View.SwitchDifficulty(0), "Could not return to configured difficulty.");
            Check(ui.View.Document.DistanceSnapRatios.SequenceEqual(saved.DistanceSnapRatios), "Map switch lost DS configuration.");
            PreviewReadouts();

            void Drag(float fraction, bool shift)
            {
                var arrow = ui.View.DistanceSnapPointerBounds.Single();
                ui.View.PointerDown(arrow.X + 8, arrow.Y + 8, 0, shift, false);
                Check(ui.View.WantsCapture, "Arrow did not capture input.");
                ui.View.PointerMove(track.X + track.Width * fraction, track.Y + 8, shift, false);
                ui.View.PointerUp(track.X + track.Width * fraction, track.Y + 8, 0); ui.Paint();
                Check(!ui.View.WantsCapture, "Arrow did not release capture.");
            }
        }
        Strings.SetLanguage("en");
    }

    private static void PreviewReadouts()
    {
        var ui = new Ui();
        var map = new MapDocument { SliderMultiplier = 1, IsDemo = false };
        map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        map.DistanceSnapRatios.Add(12);
        ui.LoadDocument(map); ui.SetSnapDivisor(4); ui.View.OpenDistanceSnapDialog(); ui.Paint();
        var r = ui.View.DistanceSnapPreviewBounds;
        float X(double x) => r.X + 10 + (float)(x / 512) * (r.Width - 20);
        float Y(double beat) => r.Bottom - 12 - (float)(beat / 4) * (r.Height - 24);
        ui.Click(X(80), Y(0));
        ui.View.PointerMove(X(380), Y(.25), false, false); ui.Paint();
        Check(ui.Canvas.Circles.Any(c => c.Filled && c.Color == 0xFF5555 && Math.Abs(c.X - X(80)) < .01), "Prospective HDash did not colour its departure fruit red.");
        ui.Click(X(380), Y(.25));
        ui.View.PointerMove(r.X - 20, r.Y, false, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12)), "HDash connection omitted its actual DS.");
        Check(ui.Canvas.Circles.Count(c => c.Filled && c.Color == 0xFF5555 && r.Contains(c.X, c.Y)) == 1, "HDash did not mark only the departure fruit.");
        var pointer = ui.View.DistanceSnapPointerBounds.Single();
        var track = ui.View.DistanceSnapTrackBounds;
        ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
        ui.View.PointerMove(track.X + track.Width * .375f, track.Y + 8, false, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12) && t.Color == 0xFF5555), "Old preview DS was not red after its preset changed.");
        ui.Key(27); ui.View.PointerUp(track.X, track.Y + 8, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewRatio", 12) && t.Color != 0xFF5555), "Restored DS remained red.");
        ui.Click(X(80), Y(.25));
        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ds.previewUndefined")), "Simultaneous fruits displayed a finite DS.");
        ui.View.PointerDown(X(80), Y(.25), 2, false, false);
        ui.View.PointerMove(r.X - 20, r.Y, false, false); ui.Paint();
        ui.View.PointerDown(X(380), Y(.25), 2, false, false);
        ui.View.PointerMove(r.X - 20, r.Y, false, false); ui.Paint();
        Check(!ui.Canvas.Circles.Any(c => c.Filled && c.Color == 0xFF5555 && r.Contains(c.X, c.Y)), "Deleted target left a stale HDash colour.");
        ui.ClickText(Strings.Get("ds.reset"));
        Check(ui.View.DistanceSnapPreviewFruits.Count == 0 && ui.View.DistanceSnapPointerBounds.Count == 1,
            "Reset did not clear only the preview fruits.");
        Check(ui.View.Document.Fruits.Count == 0, "Preview readouts modified the map.");
    }
}
