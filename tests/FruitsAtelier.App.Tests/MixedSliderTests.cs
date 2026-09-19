using FruitsAtelier.Core;

internal static class MixedSliderTests
{
    public static void Author()
    {
        var ui = new Ui();
        ui.LoadDocument(new MapDocument { DurationMs = 12000 }); ui.Paint();
        var kinds = new[] { CurveKind.Linear, CurveKind.Bezier, CurveKind.Bezier, CurveKind.Linear, CurveKind.Linear, CurveKind.Bezier };
        ui.Key('B'); ui.ClickMap(1000, 100);
        for (int i = 0; i < kinds.Length; i++)
        {
            double time = 1500 + i * 500, x = 120 + i * 20;
            if (i is 1 or 5)
            {
                ui.DownMap(time, x); ui.MoveMap(time + 125, x + 30); ui.UpMap(time + 125, x + 30);
            }
            else ui.ClickMap(time, x, ctrl: kinds[i] == CurveKind.Linear);
        }
        ui.Key(13);
        var track = ui.View.Document.Tracks.Single();
        Check(track.Nodes.Count == 7, "Mixed drawing ended the in-progress track.");
        Check(kinds.SequenceEqual(Enumerable.Range(0, kinds.Length).Select(i => CurveMath.SegmentKind(track, i))), "Mixed segment types were lost.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Tracks.Count == 0, "Mixed authoring did not undo in one transaction.");
        ui.Key('Y', ctrl: true);
        track = ui.View.Document.Tracks.Single();
        ui.SelectTrack(track.Id); ui.Key('B'); ui.ClickMap(1500, ui.View.Document.Tracks.Single(t => t.Id == track.Id).Nodes[1].X);
        ui.Key('B'); ui.ClickMap(1500, track.Nodes[1].X);
        ui.Key('L', ctrl: true);
        ui.Key('L', ctrl: true);
        Check(CurveMath.SegmentKind(track, 1) == CurveKind.Bezier, "The selected outgoing segment was not changed.");

        var samples = Enumerable.Range(0, 21).Select(i => CurveMath.PositionAtTime(track, 1500 + i * 25)).ToArray();
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.edit"));
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.splitMenu"));
        Check(track.Nodes.Count == 8, "A node was not inserted into the selected segment.");
        Check(samples.Zip(Enumerable.Range(0, 21).Select(i => CurveMath.PositionAtTime(track, 1500 + i * 25)))
            .All(p => Math.Abs(p.First - p.Second) < 0.00001), "Splitting changed the curve geometry.");
        Check(CurveMath.Validate(ui.View.Document).Count == 0, "Mixed authoring produced an invalid map.");
        Check(ui.View.Document.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document))), "Project reload lost mixed types or handles.");
    }

    public static void Import()
    {
        var ui = new Ui();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode: 2\n[Difficulty]\nSliderMultiplier: 1\nSliderTickRate: 1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n160,192,1000,2,0,B|220:250|300:192,1,200\n");
        Guid id = map.ImportedSliders.Single().Id;
        ui.LoadDocument(map); ui.Paint();
        ui.Key('1'); ui.ClickMap(1000, 160);
        ui.HoldMap(1000, 160); ui.ClickText(FruitsAtelier.Localization.Strings.Get("preview.convertSlider"));
        var track = ui.View.Document.Tracks.Single();
        Check(track.Id == id && track.SpanCount == 1 && track.CompensateTinyDroplets == true
            && ui.View.Document.ImportedSliders.Count == 0, "Conversion lost the original parent identity or FSlider alignment policy.");
        ui.ClickMap(track.Nodes[0].TimeMs, track.Nodes[0].X);
        ui.DownMap(track.Nodes[0].TimeMs, track.Nodes[0].X); ui.MoveMap(track.Nodes[0].TimeMs, 155); ui.UpMap(track.Nodes[0].TimeMs, 155);
        Check(Math.Abs(track.Nodes[0].X - 155) < 0.001, "An imported slider node remained read-only.");
        bool wasCurved = CurvePointEditing.IsCurved(track, track.Nodes[0].Id);
        ui.Key('L', ctrl: true);
        Check(CurvePointEditing.IsCurved(track, track.Nodes[0].Id) != wasCurved, "Fitted control point cannot be edited.");
        var output = OsuBeatmapWriter.Serialize(ui.View.Document);
        Check(output.ReadBack.ImportedSliders.Single().SpanCount == 1 && output.ObjectSequenceMatches, "Edited FSlider did not survive osu export.");
        Check(ui.View.Document.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document))), "Edited imported slider cannot be saved and reopened.");
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ImportedSliders.Single().Id == id && ui.View.Document.Tracks.Count == 0, "Undo did not recover the imported source.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
