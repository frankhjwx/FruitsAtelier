using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class CrossDifficultyClipboardTests
{
    public static void Run()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(locale);
            var source = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nApproachRate:8\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,2,1:2:3:80:hit.wav\n160,192,1250,2,2,L|260:192,2,100,2|4|8,1:2|2:3|3:4,2:3:4:70:clap.wav\n256,192,2500,12,8,3000,2:3:4:60:banana.wav\n");
            var track = new CurveTrack { Name = "Copied curve", Kind = CurveKind.Linear, CompensateTinyDroplets = false };
            track.Nodes.Add(new() { TimeMs = 1750, X = 300 }); track.Nodes.Add(new() { TimeMs = 2000, X = 340 });
            source.Tracks.Add(track);
            var target = new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 400,
                ApproachRate = 4, CircleSize = 3, SliderMultiplier = 2, SliderMultiplierOverride = 1.6, SliderTickRate = 3 };
            target.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 400 });
            var project = BeatmapProject.FromDocuments([source, target]);
            var ui = new Ui(false); ui.View.LoadProject(project); ui.Paint();
            ui.Key('A', ctrl: true); Check(ui.View.CopySelection(), "Mixed source copy succeeds");
            var beforeSource = ui.View.Document.DeepClone();
            Check(ui.View.SwitchDifficulty(1) && ui.View.CanPasteSelection, "Mapset clipboard survives switching difficulties");
            var beforeTarget = ui.View.Document.DeepClone();
            ui.View.UpdateTransport(4000, 10000, true, false, false, null, "fixture.wav");
            Check(ui.View.PasteSelection(), ui.View.StatusMessage); Wait(ui);
            Check(ui.View.Document.Fruits.Count == 1 && ui.View.Document.Tracks.Count == 1
                && ui.View.Document.ImportedSliders.Count == 1 && ui.View.Document.BananaShowers.Count == 1, "All parent kinds paste across difficulties");
            var pasted = ui.View.Document.ImportedSliders.Single();
            Near(4250, pasted.TimeMs);
            Near(ImportedSliderConverter.DurationMs(beforeSource, beforeSource.ImportedSliders.Single()),
                ImportedSliderConverter.DurationMs(ui.View.Document, pasted));
            Check(pasted.ControlPoints.SequenceEqual(beforeSource.ImportedSliders.Single().ControlPoints), "Legacy path geometry survives different base SV and BPM");
            Near(4750, ui.View.Document.Tracks.Single().Nodes[0].TimeMs);
            Near(6000, ui.View.Document.BananaShowers.Single().EndTimeMs);
            Check(ui.View.Document.SliderTickRate == 3 && ui.View.Document.CircleSize == 3 && ui.View.Document.ApproachRate == 4
                && ui.View.Document.TimingPoints.Count == beforeTarget.TimingPoints.Count, "Paste retains target map parameters and timing");
            var after = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(beforeTarget.ContentEquals(ui.View.Document), "Cross-difficulty paste is one undo step");
            ui.Key('Y', ctrl: true); Check(after.ContentEquals(ui.View.Document), "Redo preserves pasted identities and independent SV");
            ui.View.SwitchDifficulty(0); Check(beforeSource.ContentEquals(ui.View.Document), "Paste never edits its source difficulty");
            ui.View.SwitchDifficulty(1); ui.Key('Z', ctrl: true);
            ui.View.Document.SliderMultiplier = 3.6;
            ui.View.Document.BeatLengthMs = ui.View.Document.TimingPoints[0].BeatLengthMs = 100;
            var unsupported = ui.View.Document.DeepClone();
            Check(!ui.View.PasteSelection() && unsupported.ContentEquals(ui.View.Document), "Unrepresentable SV rejects the whole batch without target edits");
            Check(ui.View.CanPasteSelection, "Rejected paste keeps the mapset clipboard");
            ui.View.Document.SliderMultiplier = 2;
            ui.View.Document.BeatLengthMs = ui.View.Document.TimingPoints[0].BeatLengthMs = 400;
            Check(ui.View.PasteSelection(), "Restoring supported SV allows reuse of the clipboard"); Wait(ui);
            ui.View.LoadProject(project);
            Check(!ui.View.CanPasteSelection, "Reopening a project clears the clipboard");
        }
        ClipboardTests.FruitSnapshotAndHistory(); ClipboardTests.ImportedAndBananaMetadata();
        ClipboardMultiTests.MixedBatchPreservesSnapshotAndOrder(); ClipboardMultiTests.OverflowPasteRollsBackBatch();
        LegacyAlignmentTests.ClipboardAndGrid();
    }

    private static void Wait(Ui ui)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (ui.View.LegacyDropletRefreshBusy && DateTime.UtcNow < until) { Thread.Sleep(5); ui.Paint(); }
        Check(!ui.View.LegacyDropletRefreshBusy, "Paste validation finishes");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}, got {actual}"); }
}
