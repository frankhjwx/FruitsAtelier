using FruitsAtelier.Core;

internal static class EmptyCanvasTests
{
    public static void Run()
    {
        foreach (bool playing in new[] { false, true })
        {
            var ui = new Ui(overview: false);
            var map = new MapDocument { DurationMs = 10000 };
            map.Fruits.Add(new() { TimeMs = 3000, X = 256 });
            ui.View.LoadDocument(map);
            ui.View.UpdateTransport(3000, 10000, true, playing, false, null, "fixture.wav");
            ui.Paint();
            ui.Key(49);
            ui.Key(65, ctrl: true);
            int seeks = 0;
            ui.View.RequestSeek = _ => seeks++;
            ui.Click(ui.Plot.X + 30, ui.Plot.Y + 35);
            if (seeks != 0 || ui.View.PlayheadMs != 3000 || ui.View.SelectedObjectIds.Count != 0)
                throw new Exception("Empty canvas click must clear selection without seeking, paused or playing.");
            if (!map.ContentEquals(ui.View.Document) || ui.View.IsDirty)
                throw new Exception("Empty canvas selection edited map content.");
        }
    }
}
