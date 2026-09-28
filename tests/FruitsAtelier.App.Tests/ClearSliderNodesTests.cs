using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class ClearSliderNodesTests
{
    public static void Run()
    {
        foreach (string language in Strings.AvailableLanguages)
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            Strings.SetLanguage(language);
            var map = new MapDocument { DurationMs = 10000 };
            var pen = new CurveTrack { SpanCount = 3, CompensateTinyDroplets = false, Name = "Pen" };
            pen.Nodes.AddRange([
                new() { TimeMs = 1000, X = 100, HandleOut = new(100, 40) },
                new() { TimeMs = 1500, X = 220, HandleIn = new(-100, -20), HandleOut = new(100, 20) },
                new() { TimeMs = 2000, X = 300, HandleIn = new(-100, -40) }]);
            var exact = new CurveTrack { Name = "Exact", StreamSnapDivisor = 2 };
            var head = new Anchor { TimeMs = 5000, X = 100, OutgoingCurve = new() { Kind = ControlCurveKind.Bezier } };
            head.OutgoingCurve.Controls.Add(new() { Offset = new(250, 100) });
            exact.Nodes.AddRange([head, new() { TimeMs = 5500, X = 200 }]);
            map.Tracks.AddRange([pen, exact]);
            map.Fruits.Add(new() { TimeMs = 7000, X = 400 });
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Paint();
            var before = ui.View.Document.DeepClone();
            ui.ClickMap(1000, 100);
            ui.Key('L'); Clear(ui);
            Check(before.ContentEquals(ui.View.Document), "Locked notes were cleared");
            ui.Key(27); ui.Key('L');
            ui.ClickMap(5000, 100, ctrl: true);
            Clear(ui);
            foreach (var old in before.Tracks)
            {
                var track = ui.View.Document.Tracks.Single(t => t.Id == old.Id);
                Check(track.Nodes.Count == 2 && track.SpanCount == old.SpanCount && track.Name == old.Name
                    && track.StreamSnapDivisor == old.StreamSnapDivisor, "Clearing lost parent metadata");
                Check(track.Nodes[0].Id == old.Nodes[0].Id && track.Nodes[^1].Id == old.Nodes[^1].Id,
                    "Clearing replaced endpoint identities");
                foreach (double u in new[] { 0, .25, .5, .75, 1 })
                    Check(CurveMath.Evaluate(track, 0, u) == MapPoint.Lerp(new(old.Nodes[0].TimeMs, old.Nodes[0].X),
                        new(old.Nodes[^1].TimeMs, old.Nodes[^1].X), u), "Cleared path is not the endpoint line");
            }
            var after = ui.View.Document.DeepClone();
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(after)).ContentEquals(after), "Cleared path did not persist");
            Clear(ui); ui.Key(27);
            ui.Key('Z', ctrl: true);
            Check(before.ContentEquals(ui.View.Document), "Batch undo or no-op history is incorrect");
            ui.Key('Y', ctrl: true);
            Check(after.ContentEquals(ui.View.Document), "Batch redo did not restore cleared paths");
            ui.Key('Z', ctrl: true);
            ui.EditTrack(pen.Id);
            Clear(ui);
            Check(ui.View.Document.Tracks[0].Nodes.Count == 2 && ui.View.Document.Tracks[1].Nodes[0].OutgoingCurve is not null,
                "Anchor-edit selection cleared an unselected slider");
            ui.Key('Z', ctrl: true);
            Check(before.ContentEquals(ui.View.Document), "Anchor-edit clear did not undo");
        }
    }

    private static void Clear(Ui ui)
    {
        ui.ClickText(Strings.Get("ui.edit"));
        ui.ClickText(Strings.Get("slider.clearInternalNodes"));
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
