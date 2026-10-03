using FruitsAtelier.Core;

internal static class TimingSaveFeedbackTests
{
    public static void Run()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n2000,500,4,1,0,100,1,0\n2500,-100,4,2,0,80,0,0\n3500,-100,4,3,0,60,0,1\n3500,-100,4,2,0,40,0,0\n[HitObjects]\n");
        map.DurationMs = 10000;
        map.Tracks.Add(new CurveTrack { Nodes = { new Anchor { TimeMs = 3000, X = 100 }, new Anchor { TimeMs = 4000, X = 300 } } });
        Check(OsuBeatmapWriter.Serialize(map, false).ObjectSequenceMatches, "Original map exports successfully");
        var ui = new Ui(false); ui.LoadDocument(map); ui.Paint();
        ui.View.UpdateTransport(0, 10000, true, false, false, null, null);
        ui.Key('P', ctrl: true); ui.Key(13);
        ui.View.UpdateTransport(2000, 10000, true, false, false, null, null);
        ui.Key('I', ctrl: true);
        var edited = ui.View.Document.DeepClone();
        Check(edited.TimingPoints.Count(p => p.Uninherited) == 1 && edited.TimingPoints.Single(p => p.Uninherited).TimeMs == 0,
            "Inserted red replaces the original red");
        var output = OsuBeatmapWriter.Serialize(edited, false);
        Check(output.ObjectSequenceMatches && edited.ContentEquals(ui.View.Document), "Export preserves authoring and playable events");
        Check(output.ReadBack.TimingPoints.Zip(output.ReadBack.TimingPoints.Skip(1)).All(p => p.First.TimeMs <= p.Second.TimeMs), "Exported timing is chronological");
        Check(output.ReadBack.TimingPoints.Where(p => p.TimeMs == 3500).Select(p => p.Volume).SequenceEqual(new[] { 60, 40 }), "Tied timing retains sample precedence");
        var sorted = edited.DeepClone();
        var points = sorted.TimingPoints.OrderBy(p => p.TimeMs).ToArray(); sorted.TimingPoints.Clear(); sorted.TimingPoints.AddRange(points);
        Check(output.Text == OsuBeatmapWriter.Serialize(sorted, false).Text, "Unsorted authoring exports identically to stable chronological timing");
        ui.Key('Z', ctrl: true); ui.Key('Y', ctrl: true);
        Check(ui.View.Document.ContentEquals(edited), "Red deletion remains undoable");
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/timing-save-feedback", Guid.NewGuid().ToString("N")));
        ui.View.LibrarySettings.Workspace = root;
        ui.View.LibrarySettings.OsuRoot = "";
        Check(ui.View.SaveWorkspace(), "Edited timing project saves");
        var reopened = WorkspaceProject.Open(ui.View.WorkspaceSession!.Directory).Project.Difficulties.Single().Document;
        Check(OsuBeatmapWriter.Serialize(reopened, false).Text == output.Text, "Reopened project exports the same timing and sliders");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
