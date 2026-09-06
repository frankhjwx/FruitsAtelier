using FruitsAtelier.Core;

internal static class SliderSelectionTests
{
    public static void Run()
    {
        foreach (bool imported in new[] { false, true })
        foreach (bool hidden in new[] { false, true })
        foreach (var kind in new[] { CatchObjectKind.Fruit, CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 10000, CircleSize = 0, SliderMultiplier = 1, SliderTickRate = 1 };
            Guid source;
            if (imported)
            {
                var slider = new ImportedSlider { X = 160, Y = 192, TimeMs = 1000, PathType = 'L', PixelLength = 300, SpanCount = 2 };
                slider.ControlPoints.Add(new(160, 192));
                slider.ControlPoints.Add(new(160, 492));
                map.ImportedSliders.Add(slider);
                source = slider.Id;
            }
            else
            {
                var track = new CurveTrack();
                track.Nodes.Add(new() { TimeMs = 1000, X = 160 });
                track.Nodes.Add(new() { TimeMs = 4000, X = 160 });
                map.Tracks.Add(track);
                source = track.Id;
            }
            ui.LoadDocument(map); ui.Paint();
            var item = ui.View.Conversion.Objects.First(o => o.SourceId == source && o.Kind == kind);
            // A nearby large standalone fruit must not steal the generated object's centre hit.
            ui.View.Document.Fruits.Add(new() { TimeMs = item.TimeMs, X = item.X + 50 });
            ui.View.MarkSaved();
            ui.Paint();
            if (hidden) ui.ClickText("隐藏曲线");
            item = ui.View.Conversion.Objects.First(o => o.SourceId == source && o.Kind == kind);
            var p = ui.Plot;
            float x = p.X + (float)(item.X / 512) * p.Width;
            float y = p.Bottom - (float)((item.TimeMs - ui.View.ViewStartMs) * ui.View.PixelsPerMs);
            float radius = kind switch
            {
                CatchObjectKind.Fruit => CatchSize.FruitRadius(0),
                CatchObjectKind.Droplet => CatchSize.DefaultDropletRadius(0),
                _ => 0
            };
            ui.Click(x - radius * p.Width / 512 * 0.65f, y);
            if (ui.View.IsDirty) throw new Exception("Selecting a slider child changed content.");
            if (!ui.Canvas.Circles.Any(c => !c.Filled && c.Color == 0x59D3C3 && Math.Abs(c.X - x) < 0.01f))
                throw new Exception($"No parent selection highlight for {kind}, imported={imported}, hidden={hidden}.");
            ui.Key(46);
            if (ui.View.Document.ImportedSliders.Count + ui.View.Document.Tracks.Count != 0 || ui.View.Document.Fruits.Count != 1)
                throw new Exception("Deleting the selection did not remove exactly the owning slider.");
            ui.Key('Z', ctrl: true);
            if (ui.View.Document.ImportedSliders.Count + ui.View.Document.Tracks.Count != 1 || ui.View.Document.Fruits.Count != 1)
                throw new Exception("Undo did not restore the whole slider.");
        }
    }
}
