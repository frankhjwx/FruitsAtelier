using FruitsAtelier.Core;
using System.Globalization;

internal static class StableDropletTests
{
    private const string Native = """
        osu file format v14
        [General]
        Mode:2
        [Difficulty]
        SliderMultiplier:1.92
        SliderTickRate:2
        [TimingPoints]
        1582,400,4,2,1,50,1,0
        92782,-31.224639737912863,4,2,1,100,0,0
        [HitObjects]
        339,192,92782,6,0,B|264:58|264:58|242:1|242:1|209:88|209:88|167:236|167:236|134:383|134:383|133:380|133:380|173:232|173:232|216:84|216:84|350:9|350:9|364:1|364:1|481:73|481:73|446:223|446:223|393:367|393:367|363:383|363:383|258:326|258:326|106:304|106:304|34:201|34:201|32:19,1,2152.146517339755,6|2,1:2|0:0,0:0:0:0:
        """;

    public static void Run()
    {
        var native = OsuBeatmapReader.Read(Native);
        var source = native.ImportedSliders.Single();
        var converted = CatchStreamConverter.Convert(native);
        Check(converted.Success, "Overdose Legacy slider failed conversion.");
        var events = converted.Objects;
        // Stable's float distance-to-time conversion truncates the first, second and fourth ticks.
        double[] ticks = [92981, 93181, 93382, 93581, 93782, 93982];
        double[] tiny = [92881, 93081, 93231, 93281, 93331, 93481, 93631, 93681, 93731, 93882, 94063];
        Check(events.Count == 19 && events.Count(o => o.Kind == CatchObjectKind.Fruit) == 2,
            "Overdose must contain two fruits, six droplets and eleven tiny droplets.");
        Check(events.Where(o => o.Kind == CatchObjectKind.Droplet).Select(o => o.TimeMs).SequenceEqual(ticks),
            "Overdose droplet times disagree with stable.");
        Check(events.Where(o => o.Kind == CatchObjectKind.TinyDroplet).Select(o => o.TimeMs).SequenceEqual(tiny),
            "Overdose tiny times disagree with stable, including its two three-tiny intervals.");
        foreach (var item in events.Where(o => o.Kind != CatchObjectKind.Fruit))
            Check(Math.Abs(item.PathX - ImportedSliderConverter.PositionAtTime(native, source, item.TimeMs)) < .0001,
                "A stable event sampled the path at its untruncated timestamp.");
        var nativeOutput = OsuBeatmapWriter.Serialize(native);
        Check(nativeOutput.ObjectSequenceMatches && nativeOutput.MaxConvertedXError == 0
            && nativeOutput.MaxConvertedTimeErrorMs == 0, "Unedited Legacy export changed stable playback.");

        var independent = native.DeepClone();
        independent.TimingPoints.RemoveAll(p => !p.Uninherited);
        Check(SliderImportEditing.Initialize(independent, true).Success
            && independent.ImportedSliders.Single().RandomizationTrack is not null,
            "Compensated import lost the stable sequence when the source SV was independent of timing.");
        var promoted = SliderImportEditing.ConvertFollowingMap(independent, [source.Id]);
        Check(promoted.Tracks.Count == 1 && promoted.Failures.Count == 0,
            "Independent source SV prevented the Overdose slider from becoming an FSlider.");

        foreach (bool hardRock in new[] { false, true })
        {
            var map = ProjectSerializer.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "bunasynergy-overdose-slider.catchproj")));
            map.DerandomizeDropletsForHardRock = hardRock;
            map.Fruits.AddRange([new() { TimeMs = 90000, X = 256 }, new() { TimeMs = 90100, X = 256 }]);
            var track = map.Tracks.Single();
            Check(track.Nodes[0].TimeMs == 92782 && track.Nodes[^1].TimeMs == 94182,
                "The supplied Overdose FSlider fixture lost its time range.");
            var later = new CurveTrack { DropletMode = SliderDropletMode.Derandomize };
            later.Nodes.AddRange([new() { TimeMs = 95000, X = 256 }, new() { TimeMs = 96000, X = 256 }]);
            map.Tracks.Add(later);
            var history = new EditorHistory(map);
            CheckDerandomized(history.Document, hardRock);
            history.Begin("Randomize");
            history.Document.Tracks[0].DropletMode = SliderDropletMode.Randomize;
            history.Commit();
            var randomized = OsuBeatmapWriter.Serialize(history.Document);
            Check(randomized.ObjectSequenceMatches, "Randomize changed stable event identities on export.");
            Check(randomized.PlayableObjects.Where(o => o.SourceId == track.Id && o.Kind == CatchObjectKind.TinyDroplet)
                .Any(o => Math.Abs(o.X - CurveMath.PositionAtTime(track, o.TimeMs)) > 1),
                "Randomize did not restore native tiny offsets.");
            history.Undo(); CheckDerandomized(history.Document, hardRock);
            history.Redo();
            Check(OsuBeatmapWriter.Serialize(history.Document).Text == randomized.Text,
                "Redo changed the stable RNG sequence.");
            history.Undo();
            var reopened = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
            Check(reopened.ContentEquals(history.Document), "Saving changed the slider mode or authoring path.");
            CheckDerandomized(reopened, hardRock);
        }
    }

    private static void CheckDerandomized(MapDocument map, bool hardRock)
    {
        var before = map.DeepClone();
        var cache = new OsuWriteCache();
        var conversion = cache.Convert(map);
        Check(conversion.Success && conversion.Sliders.All(s => s.TinyCompensationSucceeded),
            "Overdose and the following slider must compensate every tiny droplet.");
        var output = OsuBeatmapWriter.Serialize(map, cache: cache);
        var cached = OsuBeatmapWriter.Serialize(map, cache: cache);
        var full = OsuBeatmapWriter.Serialize(map);
        Check(output.ObjectSequenceMatches && output.Text == cached.Text && output.Text == full.Text
            && output.PlayableObjects.SequenceEqual(full.PlayableObjects)
            && output.PlayableHardRockObjects.SequenceEqual(full.PlayableHardRockObjects),
            $"Stable event counts or NM/HR RNG differ between cached and full export (HR={hardRock}, sequence={output.ObjectSequenceMatches}, cached text={output.Text == cached.Text}, full text={output.Text == full.Text}, NM={output.PlayableObjects.SequenceEqual(full.PlayableObjects)}, HR={output.PlayableHardRockObjects.SequenceEqual(full.PlayableHardRockObjects)}).");
        var playable = hardRock ? output.PlayableHardRockObjects : output.PlayableObjects;
        foreach (var track in map.Tracks)
        {
            var actual = playable.Where(o => o.SourceId == track.Id).ToArray();
            var authored = conversion.Objects.Where(o => o.SourceId == track.Id).ToArray();
            if (track.Nodes[0].TimeMs == 92782)
            {
                Check(actual.Count(o => o.Kind == CatchObjectKind.TinyDroplet) == (hardRock ? 9 : 11),
                    $"The Overdose FSlider's stable tiny count changed for its fitted NM/HR SV (HR={hardRock}, count={actual.Count(o => o.Kind == CatchObjectKind.TinyDroplet)}).");
                Check(actual.Where(o => o.Kind == CatchObjectKind.TinyDroplet).Select(o => o.TimeMs)
                    .SequenceEqual(StableTinyTimes(output.Text, 92782)),
                    "Exported Overdose tiny times disagree with an independent stable calculation.");
            }
            Check(actual.Length == authored.Length && actual.Zip(authored).All(p => p.First.Kind == p.Second.Kind
                && Math.Abs(p.First.TimeMs - p.Second.TimeMs) < 1.001),
                "Stable read-back changed the slider's droplet count or event times.");
            foreach (var item in actual.Where(o => o.Kind == CatchObjectKind.TinyDroplet))
                Check(Math.Abs(item.X - CurveMath.PositionAtTime(track, item.TimeMs)) <= 1,
                    $"Derandomized {(hardRock ? "HR" : "NM")} tiny at {item.TimeMs:R} left the authoring path.");
        }
        Check(map.ContentEquals(before), "Conversion or export changed the saved authoring document.");
    }

    private static double[] StableTinyTimes(string osu, int start)
    {
        double multiplier = 0, tickRate = 0, beat = 0, inherited = 100;
        string section = "";
        string[]? slider = null;
        foreach (string raw in osu.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { section = line; continue; }
            if (section == "[Difficulty]" && line.StartsWith("SliderMultiplier:")) multiplier = Number(line.Split(':')[1]);
            if (section == "[Difficulty]" && line.StartsWith("SliderTickRate:")) tickRate = Number(line.Split(':')[1]);
            if (section == "[TimingPoints]" && line.Length > 0 && !line.StartsWith("//"))
            {
                var fields = line.Split(',');
                if (Number(fields[0]) > start) continue;
                if (fields[6] == "1") { beat = Number(fields[1]); inherited = 100; }
                else inherited = Math.Clamp(-Number(fields[1]), 10, 1000);
            }
            if (section == "[HitObjects]" && line.Length > 0 && !line.StartsWith("//"))
            {
                var fields = line.Split(',');
                if (Number(fields[2]) == start) slider = fields;
            }
        }
        Check(slider is not null && slider[6] == "1", "Stable reference expects a single-span slider.");
        double velocity = 100 * multiplier / (beat * ((float)inherited / 100d));
        double distance = Number(slider![7]), tickDistance = velocity * beat / tickRate;
        var boundaries = new List<int> { start };
        for (int tick = 1; tick * tickDistance < distance - velocity * 10; tick++)
            boundaries.Add((int)(start + (float)(tick * tickDistance) / (velocity * 1000) * 1000));
        boundaries.Add(Math.Max(start + (int)(distance / velocity) / 2,
            (int)(start + (float)distance / (velocity * 1000) * 1000) - 36));
        var tiny = new List<double>();
        for (int i = 1; i < boundaries.Count; i++)
        {
            int gap = boundaries[i] - boundaries[i - 1];
            if (gap <= 80) continue;
            float step = gap;
            while (step > 100) step *= .5f;
            for (float time = boundaries[i - 1] + step; time < boundaries[i]; time += step) tiny.Add((int)time);
        }
        return tiny.ToArray();
        static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
