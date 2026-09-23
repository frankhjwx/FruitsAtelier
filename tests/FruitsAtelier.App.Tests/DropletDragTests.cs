using FruitsAtelier.Core;

internal static class DropletDragTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void SelectAndMove()
    {
        foreach (bool imported in new[] { false, true })
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 5000, SliderTickRate = 1, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 120 }, new Anchor { TimeMs = 3000, X = 320 }]);
            var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 200 };
            slider.ControlPoints.AddRange([new(120, 192), new(320, 192)]);
            if (imported) map.ImportedSliders.Add(slider); else map.Tracks.Add(track);
            Guid source = imported ? slider.Id : track.Id;
            var target = CatchStreamConverter.Convert(map).Objects.First(o => o.SourceId == source && o.Kind == kind);
            var ui = new Ui(); ui.LoadDocument(map);
            var original = ui.View.Document.DeepClone();
            ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.SelectedObjectIds.Single() == source && ui.View.StatusMessage.Contains("Slider"),
                $"First click on {kind} did not select the parent slider.");
            ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.StatusMessage.Contains("drag horizontally"), $"Second click did not select {kind} for movement.");
            Check(ui.View.XCoordinateFieldBounds is not null, $"Selected {kind} has no X coordinate field.");
            var position = ui.ScreenAt(target.TimeMs, target.X);
            Check(ui.Canvas.Circles.Any(c => !c.Filled && Math.Abs(c.X - position.X) < 1 && Math.Abs(c.Y - position.Y) < 1
                && c.Color == 0xE7EBF2), $"Selected {kind} has no visible outer ring.");
            ui.DownMap(target.TimeMs, target.X);
            ui.MoveMap(target.TimeMs, target.X + 20);
            ui.UpMap(target.TimeMs, target.X + 20);
            var converted = CatchStreamConverter.Convert(ui.View.Document);
            var moved = converted.Objects.Single(o => o.SourceId == source && o.EventIndex == target.EventIndex);
            Check(Math.Abs(moved.X - (target.X + 20)) < .5 && Math.Abs(moved.TimeMs - target.TimeMs) < .001,
                $"Dragging {kind} did not move only its X at the same event time: {target.X} -> {moved.X}, status: {ui.View.StatusMessage}");
            ui.Key('Z', ctrl: true);
            Check(original.ContentEquals(ui.View.Document), $"One undo did not restore the {kind} drag.");
            ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
            ui.DownMap(target.TimeMs, target.X); ui.MoveMap(target.TimeMs, target.X + 15);
            ui.Key(27); ui.UpMap(target.TimeMs, target.X + 15);
            Check(original.ContentEquals(ui.View.Document), $"Escape did not cancel the {kind} drag.");
        }
    }
}
