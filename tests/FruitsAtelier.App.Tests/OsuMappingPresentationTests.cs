using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class OsuMappingPresentationTests
{
    public static void Run()
    {
        foreach (bool lazer in new[] { false, true })
        {
            var ui = NewUi(lazer);
            Check(GridLines(ui).Any(), "osu Mapping must show the geometric grid with snapping off.");
            Check(!ui.View.EditorGridSettings.Enabled && !ui.View.UsesSpeedDistanceSnap,
                "osu Mapping must retain its grid toggle independently of Catch speed presets.");
            var original = ui.View.Document.DeepClone();
            var grid = ui.View.AssistButtonBounds[4];
            ui.Click(grid.X + 8, grid.Y + 8);
            Check(ui.View.EditorGridSettings.Enabled, "The Grid Snap button must work in osu Mapping with Catch speed presets selected.");
            Check(original.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Grid preferences must not edit content.");
            ui.Key('2'); Click(ui, 103, 107);
            var fruit = ui.View.Document.Fruits.Single();
            Check(fruit.X == 104 && fruit.Y == 108, "Grid Snap must use the selected lattice on both geometric axes.");
            ui.View.SetModifiers(false, true); ui.Paint();
            Check(GridLines(ui).Any(), "Shift must preserve the geometric grid.");
            var point = ui.View.OsuCanvasScreen(143, 147);
            ui.View.PointerDown(point.X, point.Y, 0, true, false);
            ui.View.PointerUp(point.X, point.Y, 0); ui.Paint();
            Check(ui.View.Document.Fruits.Any(f => f.X == 143 && f.Y == 147), "Shift must temporarily disable grid snapping.");
        }
        DraftMatchesCommitted(false);
        DraftMatchesCommitted(true);
        CircleAndBodySize();
        GridBoundsAndSelectionRings();
        CircleComboPreview(false); CircleComboPreview(true);
        TimelineWheelAndToolIcons();
    }

    private static Ui NewUi(bool lazer = false)
    {
        var ui = new Ui(false);
        ui.View.LibrarySettings.LazerEditing = lazer;
        ui.View.LibrarySettings.MappingSpeedDistanceSnap = true;
        ui.View.LibrarySettings.LazerSpeedDistanceSnap = true;
        ui.LoadDocument(new MapDocument { DurationMs = 10000, IsDemo = false });
        ui.Resize(1440, 900);
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null);
        ui.View.ToggleOsuCanvas(); ui.Paint();
        return ui;
    }

    private static void DraftMatchesCommitted(bool newCombo)
    {
        var ui = NewUi();
        var map = new MapDocument { DurationMs = 10000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 500, X = 320, Y = 240 });
        map.Fruits.Add(new() { TimeMs = 3000, X = 400, Y = 240 });
        ui.View.LoadDocument(map); ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        ui.Key('3');
        if (newCombo) ui.Key('Q'); Click(ui, 80, 80); Click(ui, 160, 160);
        var pointer = ui.View.OsuCanvasScreen(240, 80);
        ui.View.PointerMove(pointer.X, pointer.Y, false, false); ui.Paint();
        Check(ui.View.Document.ImportedSliders.Count == 0 && !ui.View.IsDirty, "A draft must not enter the document.");
        var draft = Body(ui);
        Check(draft.Length > 0 && draft.Any(l => l.Color == 0xFFFFFF), "A draft must use the final bordered slider body.");
        var head = ui.View.OsuCanvasScreen(80, 80);
        var tail = ui.View.OsuCanvasScreen(240, 80);
        foreach (var at in new[] { head, tail })
            Check(ui.Canvas.Circles.Any(c => c.Filled && Math.Abs(c.X - at.X) < .05 && Math.Abs(c.Y - at.Y) < .05),
                "The draft must show its head and end circles.");
        ui.Key(13); ui.View.PointerMove(0, 0, false, false);
        ui.View.UpdateTransport(900, 10000, true, false, false, null, null); ui.Paint();
        var committed = Body(ui);
        Check(draft.SequenceEqual(committed), "Draft and committed sliders must retain the same path, colour, border and scale.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Count == 0 && !ui.View.IsDirty, "Finishing the slider must remain one undo step.");
        RecordingCanvas.Segment[] Body(Ui view) => view.Canvas.Lines.Where(l => l.Width > 10).ToArray();
    }

    private static void CircleAndBodySize()
    {
        var ui = NewUi();
        var map = new MapDocument { DurationMs = 10000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 1000, X = 80, Y = 80 });
        var slider = new ImportedSlider { X = 220, Y = 80, TimeMs = 1000, PathType = 'L', PixelLength = 100 };
        slider.ControlPoints.AddRange([new(220, 80), new(320, 80)]);
        map.ImportedSliders.Add(slider);
        ui.View.LoadDocument(map);
        ui.View.UpdateTransport(900, 10000, true, false, false, null, null); ui.Paint();
        var at = ui.View.OsuCanvasScreen(80, 80);
        var circle = ui.Canvas.Circles.First(c => c.Filled && Math.Abs(c.X - at.X) < .05 && Math.Abs(c.Y - at.Y) < .05);
        float bodyWidth = ui.Canvas.Lines.Where(l => l.Width > 10).Max(l => l.Width);
        Check(Math.Abs(circle.Radius * 2 - bodyWidth) < .01, "Circle visible diameter must match slider body diameter.");
        var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("osu.distanceSpacing"));
        Check(ui.View.OsuDistanceSpacingTrackBounds.X > label.X + label.MaxWidth,
            "The Distance Snapping slider must be to the right of its label.");
        foreach (int density in new[] { 1, 2 })
        {
            string folder = Path.GetFullPath(Path.Combine("artifacts/tests/osu-circle-size", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "skin.ini"), "[General]\nName: Circle size fixture\n[Colours]\nCombo1:200,20,60\nSliderTrackOverride:15,14,29\n");
            byte[] header = new byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
            System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(header, 12);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), 128 * density);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), 128 * density);
            string name = density == 1 ? "hitcircle.png" : "hitcircle@2x.png";
            File.WriteAllBytes(Path.Combine(folder, name), header);
            ui.View.LoadSkin(folder); ui.Paint();
            Check(ui.Canvas.Lines.Where(l => l.Width > 10 && l.Color != 0xFFFFFF).All(l => l.Color == 0xC8143C),
                "osu slider bodies must retain combo colour when a skin supplies a track override.");
            var texture = ui.Canvas.Images.First(i => Path.GetFileName(i.Path) == name
                && Math.Abs(i.Bounds.X + i.Bounds.Width / 2 - at.X) < .05 && Math.Abs(i.Bounds.Y + i.Bounds.Height / 2 - at.Y) < .05);
            Check(Math.Abs(texture.Bounds.Width * 118 / 128 - bodyWidth) < .01,
                "Normal and @2x circle textures must use the native 128-pixel reference.");
        }
    }

    private static void GridBoundsAndSelectionRings()
    {
        foreach (int spans in new[] { 1, 2 })
        {
            var ui = NewUi();
            var map = new MapDocument { DurationMs = 10000, IsDemo = false };
            map.Fruits.Add(new() { TimeMs = 1000, X = 80, Y = 64 });
            var slider = new ImportedSlider { X = 220, Y = 100, TimeMs = 1000, PathType = 'L', PixelLength = 140, SpanCount = spans };
            slider.ControlPoints.AddRange([new(220, 100), new(360, 100)]);
            map.ImportedSliders.Add(slider);
            ui.View.LoadDocument(map);
            ui.View.UpdateTransport(900, 10000, true, false, false, null, null); ui.Paint();
            var before = ui.View.Document.DeepClone();
            CheckGridBounds();
            Click(ui, 80, 64); CheckRing(80, 64);
            Click(ui, 220, 100);
            CheckRing(220, 100); CheckRing(360, 100);
            var timeline = ui.View.ObjectTimelineBounds;
            Check(ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0xFFA600 && timeline.Contains(c.X, c.Y)),
                "The selected timeline marker must use the same orange ring.");
            Check(!ui.Canvas.Circles.Any(c => !c.Filled && c.Color is 0x59D3C3 or 0x2866C6),
                "osu selection must not draw the Catch selection ring.");
            var center = ui.View.OsuCanvasScreen(256, 192);
            ui.View.ZoomOsuCanvasAt(center.X, center.Y, 2); ui.Paint();
            CheckGridBounds();
            ui.View.PointerDown(center.X, center.Y, 1, false, false);
            ui.View.PointerMove(center.X + 300, center.Y + 160, false, false);
            ui.View.PointerUp(center.X + 300, center.Y + 160, 1); ui.Paint();
            CheckGridBounds();
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                "Grid clipping and selection presentation must not edit beatmap content.");

            void CheckRing(double x, double y)
            {
                var at = ui.View.OsuCanvasScreen(x, y);
                var rings = ui.Canvas.Circles.Where(c => !c.Filled && c.Color == 0xFFA600
                    && Math.Abs(c.X - at.X) < .05 && Math.Abs(c.Y - at.Y) < .05).ToArray();
                Check(rings.Length == 1, "Each selected geometric endpoint must have one orange ring.");
            }
            void CheckGridBounds()
            {
                var field = ui.View.OsuCanvasBounds;
                var lines = GridLines(ui).ToArray();
                Check(lines.Length > 0, "The playfield grid must remain visible after zoom and pan.");
                foreach (var line in lines)
                    Check(line.X1 >= field.X - .01 && line.X2 <= field.Right + .01
                        && line.Y1 >= field.Y - .01 && line.Y2 <= field.Bottom + .01,
                        "Grid lines must end at the white playfield boundary.");
                foreach (var operation in ui.Canvas.Operations.Where(o => o.Segment is { Width: .5f, Opacity: .15f }))
                    Check(operation.Clip is { } clip && clip.X >= field.X - .01 && clip.Right <= field.Right + .01
                        && clip.Y >= field.Y - .01 && clip.Bottom <= field.Bottom + .01,
                        "Grid strokes must be clipped to the white playfield boundary.");
            }
        }
    }

    private static void CircleComboPreview(bool newCombo)
    {
        var ui = NewUi();
        var map = new MapDocument { DurationMs = 10000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 500, X = 320, Y = 240 });
        map.Fruits.Add(new() { TimeMs = 3000, X = 400, Y = 240 });
        ui.View.LoadDocument(map);
        ui.View.UpdateTransport(1000, 10000, true, false, false, null, null); ui.Paint();
        ui.Key('2');
        if (newCombo) ui.Key('Q');
        var at = ui.View.OsuCanvasScreen(184, 96);
        ui.View.PointerMove(at.X, at.Y, false, false); ui.Paint();
        var preview = Circle();
        Check(preview.Color != 0x59D3C3 && preview.Opacity == 1, "Circle previews must use the resulting combo colour.");
        ui.Click(at.X, at.Y); ui.Key('1');
        ui.View.PointerMove(0, 0, false, false);
        ui.View.UpdateTransport(900, 10000, true, false, false, null, null); ui.Paint();
        Check(Circle().Color == preview.Color, "Circle placement must preserve its preview combo colour.");
        RecordingCanvas.Dot Circle() => ui.Canvas.Circles.First(c => c.Filled
            && Math.Abs(c.X - at.X) < .05 && Math.Abs(c.Y - at.Y) < .05);
    }

    private static void TimelineWheelAndToolIcons()
    {
        var ui = NewUi();
        var before = ui.View.Document.DeepClone();
        var canvas = ui.View.OsuCanvasBounds;
        double scale = ui.View.ObjectTimelinePixelsPerMs;
        var timeline = ui.View.ObjectTimelineBounds;
        ui.View.Wheel(timeline.X + timeline.Width / 2, timeline.Y + timeline.Height / 2, 120, false, false, true); ui.Paint();
        Check(ui.View.ObjectTimelinePixelsPerMs > scale && ui.View.OsuCanvasBounds == canvas,
            "Alt+wheel on the upper timeline must zoom time without zooming the osu canvas.");
        scale = ui.View.ObjectTimelinePixelsPerMs;
        ui.View.Wheel(canvas.X + canvas.Width / 2, canvas.Y + canvas.Height / 2, 120, false, false, true); ui.Paint();
        Check(ui.View.ObjectTimelinePixelsPerMs == scale && ui.View.OsuCanvasBounds.Width > canvas.Width,
            "Alt+wheel on the osu canvas must retain independent geometric zoom.");
        foreach (string name in new[] { "osu-circle.png", "osu-slider.png", "osu-spinner.png" })
            Check(ui.Canvas.Images.Any(i => Path.GetFileName(i.Path) == name && i.Bounds.Width == i.Bounds.Height),
                "osu tools must use their own undistorted Circle, Legacy Slider and Spinner symbols.");
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Viewport zoom and tool icons must not edit content.");
    }

    private static IEnumerable<RecordingCanvas.Segment> GridLines(Ui ui)
        => ui.Canvas.Lines.Where(l => l.Width == .5f && l.Opacity == .15f);
    private static void Click(Ui ui, double x, double y)
    {
        var point = ui.View.OsuCanvasScreen(x, y);
        ui.Click(point.X, point.Y);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
