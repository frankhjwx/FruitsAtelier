using FruitsAtelier.Core;

internal static class TimingRetimingTests
{
    public static void Run()
    {
        foreach (int version in new[] { 7, 14 })
        {
            var map = OsuBeatmapReader.Read($"""
                osu file format v{version}
                [General]
                Mode:2
                [Difficulty]
                SliderMultiplier:1.4
                SliderTickRate:2
                [TimingPoints]
                0,500,4,1,0,100,1,0
                500,-50,4,1,0,80,0,0
                11000,400,3,1,0,100,1,0
                [HitObjects]
                100,192,1000,2,0,B|180:100|250:192,3,153
                """);
            var history = new EditorHistory(map);
            history.Begin("Retiming");
            var old = TimingEditing.Copy(history.Document.TimingPoints[0]);
            var next = TimingEditing.Copy(old); next.TimeMs = 10000;
            TimingEditing.Apply(history.Document,
                history.Document.TimingPoints.Select((p, i) => i == 0 ? next : p).ToArray(),
                [(old, next)], new(Scale: true, Snap: true, SliderLengths: true));
            history.Commit();
            var edited = history.Document.DeepClone();
            var slider = edited.ImportedSliders.Single();
            double expectedTail = ImportedSliderConverter.EndTimeMs(edited, slider);
            var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(edited));
            var output = OsuBeatmapWriter.Serialize(saved);
            var exported = output.ReadBack.ImportedSliders.Single();
            Check(output.ObjectSequenceMatches && output.MaxConvertedTimeErrorMs < 1,
                "Retimed repeated slider changed exported Catch events");
            Check(Math.Abs(ImportedSliderConverter.EndTimeMs(output.ReadBack, exported) - expectedTail) < .001,
                "Retimed imported tail changed during export read-back");
            Check(exported.SpanCount == 3 && saved.DurationMs >= expectedTail,
                "Retimed repeats or complete duration were lost");
            Check(saved.ContentEquals(edited), "Retiming project round trip changed authoring content");
            history.Undo(); Check(history.Document.ContentEquals(map), "Retiming undo changed the original map");
            history.Redo(); Check(history.Document.ContentEquals(edited), "Retiming redo changed exported source data");
            Check(OsuBeatmapWriter.Serialize(history.Document).Text == output.Text,
                "Retiming redo changed the exported beatmap");
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
