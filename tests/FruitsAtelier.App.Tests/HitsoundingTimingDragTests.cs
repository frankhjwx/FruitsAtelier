using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingTimingDragTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        map.TimingPoints.AddRange([
            new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 1, SampleIndex = 1, Volume = 60, SourceOrder = 0 },
            new() { TimeMs = 1000, BeatLengthMs = -50, Uninherited = false, SampleSet = 2, SampleIndex = 7, Volume = 75, Effects = 1, SourceOrder = 1 },
            new() { TimeMs = 2000, BeatLengthMs = 500, SampleSet = 3, SampleIndex = 8, Volume = 65, SourceOrder = 2 },
            new() { TimeMs = 2500, BeatLengthMs = -80, Uninherited = false, SampleSet = 2, SampleIndex = 9, Volume = 40, SourceOrder = 3 },
            new() { TimeMs = 3500, BeatLengthMs = -100, Uninherited = false, SampleSet = 1, SampleIndex = 10, Volume = 80, SourceOrder = 4 }]);
        foreach (int time in new[] { 500, 1200, 1800, 2400, 3200 }) map.Fruits.Add(new() { TimeMs = time, X = 256 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
        ui.View.UpdateTransport(1200, 10000, true, false, false, null, null); ui.Paint();
        var original = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        float scale = (Handle(2000).X - Handle(1000).X) / 1000;
        var start = Handle(1000); float x = start.X + 4, y = start.Y + 10;
        ui.View.PointerMove(x, y, false, false);
        ui.View.PointerDown(x, y, 0, false, false);
        ui.View.PointerMove(x + scale * 220, y, false, false); ui.Paint();
        Check(original.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Timing drag previews without editing or rebuilding committed conversion");
        Check(Math.Abs(Handle(1000).X - start.X - scale * 250) < .1, "Green-line preview snaps to the nearest beat subdivision");
        var previewLine = ui.Canvas.Lines.Single(l => l.Width == 5 && Math.Abs(l.X1 - (start.X + 4 + scale * 250)) < .1);
        Check(ui.Canvas.Outlines.Any(o => Math.Abs(o.Bounds.X - previewLine.X1) < .1 && Math.Abs(o.Bounds.Right - Handle(2000).X - 4) < .1), "Controlled interval updates during dragging");
        ui.View.PointerUp(x + scale * 220, y, 0); ui.Paint();
        var expectedMoved = original.DeepClone(); expectedMoved.TimingPoints.Single(p => p.TimeMs == 1000).TimeMs = 1250;
        Check(expectedMoved.ContentEquals(ui.View.Document), "Release changes only the green point's time");
        ui.Key(90, ctrl: true); Check(original.ContentEquals(ui.View.Document), "Single undo restores a drag");
        ui.Key(89, ctrl: true); Check(ui.View.Document.TimingPoints.Any(p => p.TimeMs == 1250), "Redo restores a drag");
        ui.Key(90, ctrl: true);

        start = Handle(2000); x = start.X + 4; y = start.Y + 10;
        ui.View.PointerMove(x, y, false, false);
        ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(x + scale * 250, y, false, false); ui.View.PointerUp(x + scale * 250, y, 0); ui.Paint();
        Check(original.ContentEquals(ui.View.Document), "Dragging a red line cannot edit its position");

        float from = Handle(1000).X + 4 - scale * 200, to = Handle(2500).X + 4 + scale * 100;
        ui.View.PointerDown(from, y, 0, false, false); ui.View.PointerMove(to, y, false, false); ui.View.PointerUp(to, y, 0); ui.Paint();
        Check(ui.Canvas.Lines.Count(l => l.Width == 5 && l.Y1 >= start.Y) == 2, "Marquee selects green lines and excludes red lines");
        start = Handle(1000); x = start.X + 4;
        conversion = ui.View.Conversion;
        ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(x + scale * 1030, y, false, false); ui.Paint();
        Check(Math.Abs(Handle(1000).X - start.X - scale * 1000) < .1, "Group drag snaps its anchor while crossing other timing points");
        Check(ReferenceEquals(conversion, ui.View.Conversion) && original.ContentEquals(ui.View.Document), "Group preview leaves map content and conversion unchanged");
        ui.View.PointerUp(x + scale * 1030, y, 0); ui.Paint();
        Check(ui.View.Document.TimingPoints.Any(p => !p.Uninherited && p.TimeMs == 2000 && p.SampleIndex == 7)
            && ui.View.Document.TimingPoints.Any(p => p.TimeMs == 3500 && p.SampleIndex == 9)
            && ui.View.Document.TimingPoints.Any(p => p.Uninherited && p.TimeMs == 2000), "Group drag preserves spacing and leaves red lines unchanged");
        ui.Key(90, ctrl: true); Check(original.ContentEquals(ui.View.Document), "One undo restores the entire group");
        start = Handle(1000); x = start.X + 4;
        ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerMove(x + scale * 250, y, false, false); ui.Key(27); ui.View.PointerUp(x, y, 0); ui.Paint();
        Check(original.ContentEquals(ui.View.Document) && !ui.View.WantsCapture && ui.View.HitsoundingPageVisible, "Escape cancels the timing preview");

        foreach (double target in new[] { 1650d, 2180d })
        {
            double snapped = new TimingMap.Lookup(ui.View.Document).Snap(target, 4);
            var expected = TimingEditing.Create(ui.View.Document, snapped, true);
            x = Handle(2000).X + 4 + (float)(target - 2000) * scale;
            ui.View.PointerDown(x, y, 2, false, false); ui.View.PointerUp(x, y, 2); ui.Paint();
            ui.ClickText(L.Get("hitsounding.newInheritedPoint"));
            var added = ui.View.Document.TimingPoints.Single(p => p.TimeMs == expected.TimeMs);
            Check(!added.Uninherited && added.SampleSet == expected.SampleSet && added.SampleIndex == expected.SampleIndex
                && added.Volume == expected.Volume && added.Effects == expected.Effects && added.Meter == expected.Meter && added.BeatLengthMs == expected.BeatLengthMs,
                "New inherited point matches Timing Panel creation after either a green or red predecessor");
            ui.Key(90, ctrl: true); Check(original.ContentEquals(ui.View.Document), "New inherited point is undoable");
        }
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                foreach (string action in new[] { "keyboard", "block", "handle", "unselected" })
                {
                    var first = Handle(1000); var last = Handle(2500);
                    float selectY = first.Y + 10;
                    ui.View.PointerDown(first.X - 15, selectY, 0, false, false);
                    ui.View.PointerMove(last.X + 20, selectY, false, false);
                    ui.View.PointerUp(last.X + 20, selectY, 0); ui.Paint();
                    Check(ui.Canvas.Lines.Count(l => l.Width == 5 && l.Y1 >= first.Y) == 2, "Deletion fixture selects two green points");
                    if (action == "keyboard") ui.Key(46);
                    else
                    {
                        Context(action == "block", action == "unselected" ? 3500 : 1000);
                        ui.ClickText(L.Get("editor.command.delete"));
                    }
                    var expected = original.DeepClone();
                    expected.TimingPoints.RemoveAll(p => action == "unselected" ? p.TimeMs == 3500 : p.TimeMs is 1000 or 2500);
                    Check(expected.ContentEquals(ui.View.Document), "Delete removes the selected greens while preserving red points, other greens and notes: " + action);
                    ui.Key(90, ctrl: true); Check(original.ContentEquals(ui.View.Document), "One undo restores the whole deletion: " + action);
                    ui.Key(89, ctrl: true); Check(expected.ContentEquals(ui.View.Document), "One redo reapplies the whole deletion: " + action);
                    ui.Key(90, ctrl: true);
                }
                foreach (bool block in new[] { true, false })
                {
                    L.SetLanguage(locale);
                    var layout = ui.View.CaptureDockLayouts(); layout.Hitsounding!.Right!.Panels = []; layout.Hitsounding.Right.Collapsed = true;
                    ui.View.LibrarySettings.DockLayouts = layout; ui.View.RestoreDockLayouts(); ui.Paint();
                    Context(block, 1000);
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("editor.command.delete"))
                        && !ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.newInheritedPoint")), "Green block and handle expose edit and delete actions");
                    ui.ClickText(L.Get("hitsounding.editTimingHelper"));
                    var reopened = ui.View.CaptureDockLayouts().Hitsounding!.Right!;
                    Check(!reopened.Collapsed && reopened.Active == "HitsoundingInspector" && reopened.Panels!.Contains("HitsoundingInspector")
                        && ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.selectedTiming")), "Edit opens the missing Timing Helper and selects the point");
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("samples.auto") && t.X > 1100), "Timing Helper exposes only concrete sample banks");
                    Context(block, 1000); ui.ClickText(L.Get("editor.command.delete"));
                    Check(ui.View.Document.TimingPoints.Count == original.TimingPoints.Count - 1 && !ui.View.Document.TimingPoints.Any(p => p.TimeMs == 1000), "Delete removes the clicked green point");
                    ui.Key(90, ctrl: true); Check(original.ContentEquals(ui.View.Document), "Context deletion is undoable");
                    Context(block, 2000);
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("editor.command.delete")), "Red timing points are protected from this delete action"); ui.Key(27);
                }
            }
        }
        finally { L.SetLanguage(language); }
        void Context(bool block, double time)
        {
            var handle = Handle(time); float cx = handle.X + (block ? 20 : 4), cy = handle.Y + (block ? -20 : 10);
            ui.View.PointerDown(cx, cy, 2, false, false); ui.View.PointerUp(cx, cy, 2); ui.Paint();
        }
        FruitsAtelier.App.Rendering.Rect Handle(double time) => ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints.Single(p => p.TimeMs == time));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
