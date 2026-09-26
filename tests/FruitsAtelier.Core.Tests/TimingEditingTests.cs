using FruitsAtelier.Core;

internal static class TimingEditingTests
{
    public static void Run()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\nPreviewTime:750\n[Editor]\nBookmarks:500,2500\n[TimingPoints]\n0,500,4,1,1,80,1,8\n2000,400,3,2,2,60,1,0\n[HitObjects]\n128,192,500,1,0,0:0:0:0:\n256,192,2500,1,0,0:0:0:0:\n256,192,1000,8,0,1500\n");
        var track = new CurveTrack { Kind = CurveKind.Bezier };
        track.Nodes.AddRange([new Anchor { TimeMs = 500, X = 100, HandleOut = new(100, 20) }, new Anchor { TimeMs = 1000, X = 200, HandleIn = new(-100, -20) }]);
        map.Tracks.Add(track);
        var original = map.DeepClone();
        var old = TimingEditing.Copy(map.TimingPoints[0]); var changed = TimingEditing.Copy(old);
        changed.TimeMs = 100; changed.BeatLengthMs = 250;
        TimingEditing.Apply(map, [changed, map.TimingPoints[1]], [(old, changed)], new(Scale: true, Bookmarks: true));
        Check(map.Fruits[0].TimeMs == 350 && map.Fruits[1].TimeMs == 2500, "Only the old red section's objects scale");
        Check(track.Nodes[0].TimeMs == 350 && track.Nodes[1].TimeMs == 600 && track.Nodes[0].HandleOut == new MapPoint(50, 20), "FSlider times and handles scale, X stays fixed");
        Check(map.BananaShowers[0].TimeMs == 600 && map.BananaShowers[0].EndTimeMs == 850, "Banana duration scales with its section");
        Check(OsuTimeline.Bookmarks(map).SequenceEqual(new[] { 350, 2500 }) && SongSetup.Get(map, "General", "PreviewTime") == "475", "Bookmarks and preview transform by old section");
        Check(map.TimingPoints[0].Effects == 8, "Untouched effect bits survive timing edits");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).ContentEquals(map), "Project round trip retains timing edits");
        var readback = OsuBeatmapWriter.Serialize(map).ReadBack;
        Check(readback.TimingPoints[0].TimeMs == 100 && readback.TimingPoints[0].BeatLengthMs == 250, "Edited source timing exports actual values");
        var unchanged = original.DeepClone();
        TimingEditing.Apply(unchanged, [changed, unchanged.TimingPoints[1]], [(old, changed)], new());
        Check(unchanged.Fruits[0].TimeMs == 500 && unchanged.Tracks[0].Nodes[1].TimeMs == 1000, "Unchecked options preserve authoring times");
        var sliderMap = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|250:192,2,153\n");
        TimingEditing.ResnapLengths(sliderMap, 4);
        var slider = sliderMap.ImportedSliders.Single();
        Check(Math.Abs(ImportedSliderConverter.EndTimeMs(sliderMap, slider) - 2500) < .001, "Repeated imported slider ends resnap by updating path length");
        Check(Math.Abs(ImportedSliderConverter.EndTimeMs(OsuBeatmapWriter.Serialize(sliderMap).ReadBack, OsuBeatmapWriter.Serialize(sliderMap).ReadBack.ImportedSliders.Single()) - 2500) < .001, "New slider duration survives export");
        var parsed = TimingEditing.Parse(TimingEditing.Serialize(map.TimingPoints));
        Check(parsed.Length == 2 && parsed[0].Effects == 8, "Clipboard preserves control point fields");
        bool invalid = false;
        try { TimingEditing.Parse("not timing"); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "Malformed clipboard fails without partial edits");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
