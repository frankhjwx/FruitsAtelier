using FruitsAtelier.Core;

internal static class DropletDragTests
{
    public static void DefaultModeDrag()
    {
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 60000.0 / 180, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 120 }, new Anchor { TimeMs = 3000, X = 320 }]);
            map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.SetSliderEditingMode(FruitsAtelier.App.Editor.SliderEditingMode.OsuLegacy);
            ui.Paint();
            var original = ui.View.Document.DeepClone();
            var target = OsuBeatmapWriter.Serialize(map).PlayableObjects.First(o => o.Kind == kind);
            ui.ClickMap(target.TimeMs, target.X);
            ui.DownMap(target.TimeMs, target.X);
            ui.MoveMap(target.TimeMs, target.X + 10);
            ui.MoveMap(target.TimeMs, target.X + 20);
            ui.UpMap(target.TimeMs, target.X + 20);
            var moved = OsuBeatmapWriter.Serialize(ui.View.Document).PlayableObjects.Single(o => o.EventIndex == target.EventIndex);
            Check(Math.Abs(moved.X - target.X - 20) < 1,
                $"Default-mode {kind} drag stopped at {moved.X}, started at {target.X}: {ui.View.StatusMessage}");
            ui.Key('Z', ctrl: true);
            Check(original.ContentEquals(ui.View.Document), "Default-mode drag did not undo in one step.");
        }
    }

    public static void DragAfterLegacyConversion()
    {
        var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 60000.0 / 183, IsDemo = false };
        var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'B', PixelLength = 420 };
        slider.ControlPoints.AddRange([new(120, 192), new(400, 100), new(200, 300), new(320, 192)]);
        map.ImportedSliders.Add(slider);
        ImportedSliderEditing.ConvertToTrack(map, slider.Id);
        var baseline = CatchStreamConverter.Convert(map);
        Check(baseline.Success, "Converted legacy fixture is invalid.");
        var displayed = OsuBeatmapWriter.Serialize(map).PlayableObjects;
        foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>())
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        foreach (var target in displayed.Where(o => o.Kind == kind))
        {
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.SetSliderEditingMode(mode);
            ui.Paint();
            ui.ClickMap(target.TimeMs, target.X);
            ui.DownMap(target.TimeMs, target.X);
            ui.MoveMap(target.TimeMs, target.X + 5);
            ui.MoveMap(target.TimeMs, target.X + 10);
            ui.UpMap(target.TimeMs, target.X + 10);
            var after = CatchStreamConverter.Convert(ui.View.Document);
            Check(after.Success, "Converted slider drag became invalid.");
            foreach (var old in baseline.Objects)
            {
                var current = after.Objects.Single(o => o.EventIndex == old.EventIndex);
                double expected = old.EventIndex == target.EventIndex ? target.X + 10 : old.X;
                Check(Math.Abs(current.X - expected) < .01 && Math.Abs(current.TimeMs - old.TimeMs) < .001,
                    $"Converted {kind} #{target.EventIndex}: event {old.EventIndex} expected {expected}, got {current.X}; {ui.View.StatusMessage}");
            }
            var moved = OsuBeatmapWriter.Serialize(ui.View.Document).PlayableObjects.Single(o => o.EventIndex == target.EventIndex);
            ui.DownMap(moved.TimeMs, moved.X);
            ui.MoveMap(moved.TimeMs, moved.X - 5);
            Check(ui.View.SelectedAnchorIds.Count == 0, "Dragging the same droplet again selected its new anchor.");
            ui.Key(27);
            ui.UpMap(moved.TimeMs, moved.X - 5);
            Check(CatchStreamConverter.Convert(ui.View.Document).Objects.SequenceEqual(after.Objects),
                "Cancelling a second drag changed the accepted droplet position.");
            ui.Key('Z', ctrl: true);
            Check(map.ContentEquals(ui.View.Document), "Converted slider drag did not undo.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void SelectAndMove()
    {
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 5000, SliderTickRate = 1, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 120 }, new Anchor { TimeMs = 3000, X = 320 }]);
            map.Tracks.Add(track);
            Guid source = track.Id;
            var originalEvents = CatchStreamConverter.Convert(map).Objects.Where(o => o.SourceId == source).ToArray();
            var target = originalEvents.First(o => o.Kind == kind);
            var ui = new Ui(); ui.LoadDocument(map);
            var original = ui.View.Document.DeepClone();
            ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.SelectedObjectIds.Single() == source && ui.View.StatusMessage.Contains("Slider"),
                $"First click on {kind} did not select the parent slider.");
            ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.StatusMessage.Contains("drag horizontally"), $"Second click did not select {kind} for movement.");
            Check(ui.View.XCoordinateFieldBounds is not null, $"Selected {kind} has no X coordinate field.");
            var editableBaseline = ui.View.Document.DeepClone();
            var baselineEvents = CatchStreamConverter.Convert(ui.View.Document).Objects.Where(o => o.SourceId == source).ToArray();
            var selected = baselineEvents.Single(o => o.EventIndex == target.EventIndex);
            var position = ui.ScreenAt(selected.TimeMs, selected.X);
            Check(ui.Canvas.Circles.Any(c => !c.Filled && Math.Abs(c.X - position.X) < 1 && Math.Abs(c.Y - position.Y) < 1
                && c.Color == 0xE7EBF2), $"Selected {kind} has no visible outer ring.");
            ui.DownMap(selected.TimeMs, selected.X);
            ui.MoveMap(selected.TimeMs, selected.X + 20);
            ui.UpMap(selected.TimeMs, selected.X + 20);
            var converted = CatchStreamConverter.Convert(ui.View.Document);
            var moved = converted.Objects.Single(o => o.SourceId == source && o.EventIndex == target.EventIndex);
            Check(Math.Abs(moved.X - (selected.X + 20)) < .5 && Math.Abs(moved.TimeMs - selected.TimeMs) < .001,
                $"Dragging {kind} did not move only its X at the same event time: {selected.X} -> {moved.X}, status: {ui.View.StatusMessage}");
            foreach (var old in baselineEvents.Where(o => o.EventIndex != target.EventIndex))
            {
                var current = converted.Objects.Single(o => o.SourceId == source && o.EventIndex == old.EventIndex);
                Check(Math.Abs(current.X - old.X) < .001 && Math.Abs(current.TimeMs - old.TimeMs) < .001,
                    $"Dragging {kind} displaced another slider event {old.EventIndex}: {old.X} -> {current.X}.");
            }
            ui.Key('Z', ctrl: true);
            Check(editableBaseline.ContentEquals(ui.View.Document), $"One undo did not restore the {kind} drag.");
            ui.ClickMap(selected.TimeMs, selected.X); ui.ClickMap(selected.TimeMs, selected.X);
            ui.DownMap(selected.TimeMs, selected.X); ui.MoveMap(selected.TimeMs, selected.X + 15);
            ui.Key(27); ui.UpMap(selected.TimeMs, selected.X + 15);
            Check(editableBaseline.ContentEquals(ui.View.Document), $"Escape did not cancel the {kind} drag.");
            Check(original.ContentEquals(ui.View.Document), "Cancelling the drag changed the original FSlider.");

            if (kind == CatchObjectKind.Droplet)
            {
                ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
                ui.DownMap(target.TimeMs, target.X);
                ui.MoveMap(target.TimeMs, 512); ui.UpMap(target.TimeMs, 512);
                converted = CatchStreamConverter.Convert(ui.View.Document);
                moved = converted.Objects.Single(o => o.SourceId == source && o.EventIndex == target.EventIndex);
                Check(moved.X > target.X && moved.X < 512, "Unreachable drag was not clamped at the last valid position.");
                foreach (var old in originalEvents.Where(o => o.EventIndex != target.EventIndex))
                    Check(Math.Abs(converted.Objects.Single(o => o.SourceId == source && o.EventIndex == old.EventIndex).X - old.X) < .001,
                        "Clamping a drag displaced another slider event.");
            }
        }
    }

    public static void CurvedNeighbors()
    {
        var map = new MapDocument { DurationMs = 5000, SliderTickRate = 1, IsDemo = false };
        var track = new CurveTrack { Kind = CurveKind.Bezier };
        track.Nodes.AddRange([
            new Anchor { TimeMs = 1000, X = 120, HandleOut = new(500, 70) },
            new Anchor { TimeMs = 3000, X = 320, HandleIn = new(-500, -70) }
        ]);
        map.Tracks.Add(track);
        var before = CatchStreamConverter.Convert(map);
        Check(before.Success, "Curved slider fixture did not convert.");
        var events = before.Objects.Where(o => o.SourceId == track.Id).ToArray();
        var target = events.First(o => o.Kind == CatchObjectKind.Droplet);
        DistanceSpacingEditing.ApplyIsolatedX(map, target, target.X + 1, true);
        var after = CatchStreamConverter.Convert(map);
        Check(after.Success, "Isolated curved droplet edit did not convert.");
        foreach (var old in events)
        {
            var current = after.Objects.Single(o => o.SourceId == track.Id && o.EventIndex == old.EventIndex);
            double expected = old.X + (old.EventIndex == target.EventIndex ? 1 : 0);
            Check(Math.Abs(current.X - expected) < .001,
                $"Curved droplet edit displaced event {old.EventIndex}: expected {expected}, got {current.X}.");
        }
    }

    public static void LegacyRequiresConversion()
    {
        var map = new MapDocument { DurationMs = 5000, SliderTickRate = 1, IsDemo = false };
        var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 200 };
        slider.ControlPoints.AddRange([new(120, 192), new(320, 192)]);
        map.ImportedSliders.Add(slider);
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var ui = new Ui(); ui.LoadDocument(map);
            var original = ui.View.Document.DeepClone();
            var child = CatchStreamConverter.Convert(map).Objects.First(o => o.SourceId == slider.Id && o.Kind == kind);
            ui.ClickMap(child.TimeMs, child.X); ui.ClickMap(child.TimeMs, child.X);
            Check(ui.View.SelectedObjectIds.Single() == slider.Id && ui.View.XCoordinateFieldBounds is null
                && original.ContentEquals(ui.View.Document),
                $"Legacy {kind} was selected or auto-converted before its parent became an FSlider.");
        }
        ImportedSliderEditing.ConvertToTrack(map, slider.Id);
        var converted = CatchStreamConverter.Convert(map);
        var target = converted.Objects.First(o => o.SourceId == slider.Id && o.Kind == CatchObjectKind.Droplet);
        var editor = new Ui(); editor.LoadDocument(map);
        editor.ClickMap(target.TimeMs, target.X); editor.ClickMap(target.TimeMs, target.X);
        Check(editor.View.XCoordinateFieldBounds is not null, "Converted FSlider droplet could not be selected.");
    }

    public static void ConvertedAndDenseCurves()
    {
        foreach (bool compensated in new[] { true, false })
        foreach (bool dense in new[] { false, true })
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 60000.0 / 183, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = compensated };
            track.Nodes.Add(new Anchor { TimeMs = 1000, X = 120 });
            if (dense)
                for (int i = 1; i < 100; i++)
                    track.Nodes.Add(new Anchor { TimeMs = 1000 + i * 20, X = 120 + i * 2 });
            track.Nodes.Add(new Anchor { TimeMs = 3000, X = 320 });
            map.Tracks.Add(track);
            var before = CatchStreamConverter.Convert(map);
            Check(before.Success, "Drag fixture did not convert.");
            var target = before.Objects.First(o => o.Kind == kind);
            try { DistanceSpacingEditing.ApplyIsolatedX(map, target, target.X + 5, true); }
            catch (Exception error) { throw new Exception($"Cannot drag {kind}, compensated={compensated}, dense={dense}", error); }
            var after = CatchStreamConverter.Convert(map);
            Check(after.Success, "Dragged fixture did not convert.");
            foreach (var old in before.Objects)
            {
                var current = after.Objects.Single(o => o.EventIndex == old.EventIndex);
                Check(Math.Abs(current.X - old.X - (old.EventIndex == target.EventIndex ? 5 : 0)) < .001,
                    $"Drag displaced event {old.EventIndex}, compensated={compensated}, dense={dense}.");
            }
        }
    }
}
