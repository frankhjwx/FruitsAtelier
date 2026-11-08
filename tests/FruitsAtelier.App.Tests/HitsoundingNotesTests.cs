using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingNotesTests
{
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        foreach (bool draw in new[] { false, true })
        {
            L.SetLanguage(language);
            var map = new MapDocument { DurationMs = 12000, BeatLengthMs = 500 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
            map.Fruits.Add(new() { TimeMs = 1000, X = 64, OriginalLine = "64,192,1000,1,2,2:3:1:80:" });
            map.Fruits.Add(new() { TimeMs = 1500, X = 448 });
            map.Fruits.Add(new() { TimeMs = 2000, X = 64 });
            map.Fruits.Add(new() { TimeMs = 2500, X = 448 });
            var slider = new CurveTrack { Kind = CurveKind.Linear };
            slider.Nodes.Add(new() { TimeMs = 3000, X = 128 });
            slider.Nodes.Add(new() { TimeMs = 3500, X = 384 });
            map.Tracks.Add(slider);
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
            ui.View.UpdateTransport(2000, 12000, true, false, false, null, null); ui.Paint();
            var baseline = ui.View.Document.DeepClone();
            ui.Key(draw ? 50 : 49);
            Check(ui.View.HitsoundingBrushEnabled == draw, "Empty Select disables brushes while Draw enables them");
            if (!draw)
            {
                ui.Key(68); ui.Key(81);
                Check(ui.View.HitsoundingBrush == 2, "Disabled brush shortcuts leave the brush unchanged");
                var disabledDrum = ui.Canvas.Images.Single(i => i.Path.EndsWith("hitsounding" + Path.DirectorySeparatorChar + "drum.png")).Bounds;
                ui.Click(disabledDrum.X + disabledDrum.Width / 2, disabledDrum.Y + disabledDrum.Height / 2);
                Check(ui.View.HitsoundingBrush == 2, "Disabled brush buttons ignore clicks");
            }
            Click(1750);
            Check(baseline.ContentEquals(ui.View.Document), "Notes editing is off by default");
            Toggle();
            Click(1750);
            Check(ui.View.Document.Fruits.Count == 5 && ui.View.Document.Fruits.Single(f => f.TimeMs == 1750) is { X: 256, Y: 192 }, "Both tools create a centred circle");
            int scale = ui.View.LibrarySettings.UiScalePercent;
            ui.Key(187, ctrl: true);
            Check(ui.View.LibrarySettings.UiScalePercent == scale, "Circle selection does not redirect reverse shortcuts to UI scaling");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Creation is one undo step");
            ui.Key(89, ctrl: true); Check(ui.View.Document.Fruits.Count == 5, "Circle creation supports redo");
            ui.Key(90, ctrl: true);
            ui.View.PointerDown(X(1000), Y(), 0, false, false);
            ui.View.PointerMove(X(1125), Y(), false, false); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Moving Notes previews without mutating content");
            Check(HasCircle(1125) && !HasCircle(1000), "Preview moves the original note graphic instead of leaving a duplicate");
            ui.Key(27); ui.View.PointerUp(X(1125), Y(), 0); ui.Paint();
            Check(HasCircle(1000) && !HasCircle(1125), "Escape restores the original note graphic");
            Drag(1000, 1125);
            Check(ui.View.Document.Fruits.Any(f => f.TimeMs == 1125), "Release commits the preview position");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Note movement is one undo step");
            ui.View.PointerDown(X(1000), Y(), 2, false, false); ui.View.PointerUp(X(1000), Y(), 2); ui.Paint();
            Check(ui.View.Document.Fruits.Count == 3, "Right-click deletes a note in either mode");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Right-click note deletion is undoable");
            float y = Y();
            ui.View.PointerDown(X(850), y, 0, false, false);
            ui.View.PointerMove(X(1600), y, false, false); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Marquee preview leaves content unchanged");
            ui.View.PointerUp(X(1600), y, 0); ui.Paint();
            Check(ui.View.SelectedObjectIds.Count == 2 && ui.View.Document.Tracks.Count == 1, "Dragging empty Notes selects objects");
            Check(ui.View.HitsoundingBrushEnabled, "Selected Notes enable brushes in either mode");
            ui.Key(68); Check(ui.View.HitsoundingBrush == 3, "D selects Drum with Notes selected");
            ui.Key(81); Check(ui.View.HitsoundingBrush == 2, "Q cycles brushes with Notes selected");
            ui.Key(83); Check(ui.View.HitsoundingBrush == 1, "S selects Normal with Notes selected");
            ui.Key(65); Check(ui.View.HitsoundingBrush == 2, "A selects Soft with Notes selected");
            var drumButton = ui.Canvas.Images.Single(i => i.Path.EndsWith("hitsounding" + Path.DirectorySeparatorChar + "drum.png")).Bounds;
            ui.Click(drumButton.X + drumButton.Width / 2, drumButton.Y + drumButton.Height / 2);
            Check(ui.View.HitsoundingBrush == 3, "Selected Notes enable brush button clicks");
            var soundResolver = new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false);
            Check(ui.View.Conversion.Objects.Where(n => ui.View.SelectedObjectIds.Contains(n.SourceId))
                .All(n => soundResolver.Describe(n) is { NormalSet: 3, AdditionSet: 3 }), "Brush buttons apply both banks to all selected Notes in either mode");
            for (int i = 0; i < 5; i++) ui.Key(90, ctrl: true);
            Check(baseline.ContentEquals(ui.View.Document), "Each bank change is one undo step");
            ui.Key(67, ctrl: true);
            ui.View.UpdateTransport(2000, 12000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true);
            Check(ui.View.Document.Fruits.Count == 4 && ui.View.Document.Fruits[2].X == 64, "Matching paste preserves destination geometry");
            var target = ui.View.Conversion.Objects.Single(n => n.SourceId == map.Fruits[2].Id);
            Check(new HitsoundResolver(ui.View.Document, ui.View.Conversion.Objects, inspectResources: false).Describe(target).Additions == 2, "Matching paste transfers sounds");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Matched paste is atomic and undoable");
            ui.View.UpdateTransport(2125, 12000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true);
            Check(baseline.ContentEquals(ui.View.Document) && ui.View.StatusMessage == L.Get("hitsounding.noteRhythmMismatch"), "Partial rhythm overlap rejects the entire paste");
            ui.View.UpdateTransport(6000, 12000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true);
            Check(ui.View.Document.Fruits.Count == 6 && ui.View.Document.Fruits.Any(f => f.TimeMs == 6500), "Empty destination creates copied notes with relative timing");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "New-note paste is one undo step");
            ui.View.UpdateTransport(3000, 12000, true, false, false, null, null); ui.Paint();
            Drag(3500, 3750);
            Check(Math.Abs(CurveMath.EndTimeMs(ui.View.Document.Tracks.Single()) - 3750) < .1, "Slider tail drag adjusts duration");
            ui.Key(90, ctrl: true); Check(baseline.ContentEquals(ui.View.Document), "Endpoint drag is undoable");
            Drag(3000, 2875);
            Check(Math.Abs(ui.View.Document.Tracks.Single().Nodes[0].TimeMs - 2875) < .1 && Math.Abs(CurveMath.EndTimeMs(ui.View.Document.Tracks.Single()) - 3500) < .1, "Slider head drag retains its end");
            ui.Key(90, ctrl: true);
            Click(3250); ui.Key(187, ctrl: true);
            Check(ui.View.Document.Tracks.Single().SpanCount == 2, "Ctrl+plus increases slider spans");
            ui.Key(189, ctrl: true); Check(ui.View.Document.Tracks.Single().SpanCount == 1, "Ctrl+minus decreases slider spans");
            var beforeCancel = ui.View.Document.DeepClone();
            ui.View.PointerDown(X(3500), Y(), 0, false, false); ui.View.PointerMove(X(3750), Y(), false, false); ui.Key(27);
            ui.View.PointerUp(X(3750), Y(), 0); ui.Paint();
            Check(beforeCancel.ContentEquals(ui.View.Document) && !ui.View.LibraryVisible, "Escape cancels the endpoint preview");
            Click(3250); ui.Key(67, ctrl: true);
            ui.View.UpdateTransport(6000, 12000, true, false, false, null, null); ui.Paint(); ui.Key(86, ctrl: true);
            Check(ui.View.Document.Tracks.Count == 2, "Notes clipboard retains complete sliders");
            ui.Key(187, ctrl: true);
            var differentReverse = ui.View.Document.DeepClone(); ui.Key(86, ctrl: true);
            Check(differentReverse.ContentEquals(ui.View.Document) && ui.View.StatusMessage == L.Get("hitsounding.noteRhythmMismatch"), "A reverse-count mismatch rejects paste atomically");
            Toggle(); var disabled = ui.View.Document.DeepClone(); Click(7000);
            Check(disabled.ContentEquals(ui.View.Document), "Disabling Notes editing protects content again");
            void Toggle() { var r = ui.View.HitsoundingNotesToggleBounds; ui.Click(r.X + r.Width / 2, r.Y + 12); }
            float X(double time) => ui.View.HitsoundingNotesLaneBounds.X + (float)((time - (ui.View.PlayheadMs - 1200)) / 4000 * ui.View.HitsoundingNotesLaneBounds.Width);
            float Y() { var r = ui.View.HitsoundingNotesLaneBounds; return r.Y + r.Height / 2; }
            bool HasCircle(double time) => ui.Canvas.Circles.Any(c => c.Radius > 10 && Math.Abs(c.X - X(time)) < 1
                && ui.View.HitsoundingNotesLaneBounds.Contains(c.X, c.Y));
            void Click(double time) => ui.Click(X(time), Y());
            void Drag(double from, double to)
            {
                ui.View.PointerDown(X(from), Y(), 0, false, false); ui.View.PointerMove(X(to), Y(), false, false); ui.Paint();
                ui.View.PointerUp(X(to), Y(), 0); ui.Paint();
            }
        }
        L.SetLanguage("en");
        Console.WriteLine("PASS Hitsounding Notes creation, selection, clipboard, endpoints and reverses");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
