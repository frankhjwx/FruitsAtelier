using FruitsAtelier.Core;

internal static class GroupDragBoundaryTests
{
    public static void Run()
    {
        foreach (bool rightward in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 8000 };
            // These coordinates round outward at the respective float-based drag limits.
            var left = new Fruit { TimeMs = 1000, X = 17.141816377522872 };
            var right = new Fruit { TimeMs = 2000, X = 161.53972185880468 };
            map.Fruits.AddRange([left, right]);
            var ui = new Ui(); ui.LoadDocument(map); ui.Key('1');
            ui.ClickFruit(left.Id); ui.ClickMap(right.TimeMs, right.X, ctrl: true);
            ui.DownMap(right.TimeMs, right.X);
            double destination = rightward ? 700 : -100;
            ui.MoveMap(right.TimeMs, destination);
            Check();
            ui.UpMap(right.TimeMs, destination);
            Check();
            var moved = ui.View.Document.DeepClone();
            if (!OsuBeatmapWriter.Serialize(moved).ObjectSequenceMatches)
                throw new Exception("Boundary drag failed export validation.");
            ui.Key('Z', ctrl: true);
            if (!map.ContentEquals(ui.View.Document)) throw new Exception("Boundary drag failed undo.");
            ui.Key('Y', ctrl: true);
            if (!moved.ContentEquals(ui.View.Document)) throw new Exception("Boundary drag failed redo.");
            ui.Key('Z', ctrl: true);
            ui.ClickFruit(left.Id); ui.ClickMap(right.TimeMs, right.X, ctrl: true);
            ui.DownMap(right.TimeMs, right.X); ui.MoveMap(right.TimeMs, destination);
            ui.View.CancelInteraction(); ui.Paint();
            if (!map.ContentEquals(ui.View.Document)) throw new Exception("Boundary drag failed cancellation.");
            ui.View.NewProject();

            void Check()
            {
                var conversion = CatchStreamConverter.Convert(ui.View.Document);
                if (!conversion.Success || conversion.Objects.Count != 2 || ui.View.Conversion.Objects.Count != 2)
                    throw new Exception($"Boundary drag lost a fruit: rightward={rightward}, X={string.Join(", ", ui.View.Document.Fruits.Select(f => f.X.ToString("R")))}.");
                double shift = ui.Fruit(right.Id).X - right.X;
                if (Math.Abs(ui.Fruit(left.Id).X - left.X - shift) > 1e-12)
                    throw new Exception("Boundary drag changed relative fruit spacing.");
            }
        }
    }
}
