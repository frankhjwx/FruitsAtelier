using FruitsAtelier.Core;

internal static class GeneratedSliderPathTests
{
    public static void Run()
    {
        OptionalSamples();
        foreach (int spans in new[] { 1, 3 })
        foreach (bool compensation in new[] { false, true })
        {
            const string legacy = "100,100,9000,2,0,P|150:50|200:100,2,157,2|4|8,1:2|2:3|3:1,2:3:4:65:edge.wav";
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:0.1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n" + legacy);
            map.DurationMs = 30000;
            Add(1000, 100, 1500, 100);
            Add(3000, 200, 8000, 240);
            Add(23000, 50, 23500, 150);
            var snapshot = map.DeepClone();
            var conversion = CatchStreamConverter.Convert(map);
            var output = OsuBeatmapWriter.Serialize(map);
            Check(output.ObjectSequenceMatches && map.ContentEquals(snapshot), "Export changed authored content or event identities.");
            Check(output.Text.Contains(legacy), "Untouched Legacy Slider changed.");
            foreach (var generated in conversion.Sliders.Where(s => map.Tracks.Any(t => t.Id == s.SourceId)))
            {
                var imported = output.ReadBack.ImportedSliders.Single(s => s.TimeMs == generated.StartTimeMs);
                Check(imported.PathType == 'B' && imported.SpanCount == spans, "Generated slider lost Bezier encoding or repeats.");
                Check(imported.ControlPoints.Count == 2 * generated.Path.Count - 2, "Red-anchor count changed.");
                Check(generated.Path[0].GeometryY == 192 && (generated.Path.All(p => p.GeometryY == 192)
                    || generated.Path.First(p => p.GeometryY != 192).GeometryY < 192),
                    "Generated slider did not start upwards.");
                for (int i = 1; i < generated.Path.Count - 1; i++)
                    Check(imported.ControlPoints[2 * i - 1] == imported.ControlPoints[2 * i], "An internal corner is not a red anchor.");
                Check(generated.Path.All(p => p.GeometryY >= 1 && p.GeometryY <= 383), "Path escaped vertical bounds.");
            }
            if (!compensation)
            {
                var shortPath = conversion.Sliders.Single(s => s.SourceId == map.Tracks[0].Id).Path;
                Check(Math.Abs(shortPath[^1].GeometryY - 92) < 1e-7 && shortPath.Zip(shortPath.Skip(1)).All(p => p.First.GeometryY >= p.Second.GeometryY),
                    "Short segments did not preserve upward travel.");
                var longPath = conversion.Sliders.Single(s => s.SourceId == map.Tracks[1].Id).Path;
                Check(longPath.Any(p => p.GeometryY == 1) && longPath.Any(p => p.GeometryY == 383),
                    "Long segments did not fold at both margins.");
            }

            // The equivalent L path checks duplicate-point segmentation independently of generation.
            var linear = output.ReadBack.DeepClone();
            foreach (var slider in linear.ImportedSliders.Where(s => s.TimeMs != 9000))
            {
                var points = slider.ControlPoints.Where((p, i) => i == 0 || p != slider.ControlPoints[i - 1]).ToArray();
                slider.ControlPoints.Clear(); slider.ControlPoints.AddRange(points);
                slider.PathType = 'L'; slider.OriginalLine = null;
            }
            var equivalent = OsuBeatmapWriter.Serialize(linear);
            Check(output.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(
                equivalent.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X))), "Bezier red anchors changed NM playback.");
            Check(output.PlayableHardRockObjects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(
                equivalent.PlayableHardRockObjects.Select(o => (o.Kind, o.TimeMs, o.X))), "Bezier red anchors changed HR playback.");

            void Add(double start, double from, double end, double to)
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = spans, CompensateTinyDroplets = compensation };
                track.Nodes.AddRange([new() { TimeMs = start, X = from }, new() { TimeMs = end, X = to }]);
                map.Tracks.Add(track);
            }
        }
    }

    private static void OptionalSamples()
    {
        foreach (string suffix in new[] { "", ",2|4", ",2|4,1:2|2:3", ",,,2:3:0:0:", ",,,", ",,", ",,1:2|2:3,2:3:0:0:" })
        {
            string line = "100,192,1000,2,8,L|200:192,1,100" + suffix;
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n" + line);
            var snapshot = map.DeepClone();
            CheckSamples(map, OsuBeatmapWriter.Serialize(map));
            Check(map.ContentEquals(snapshot), "Optional sample repair mutated the imported source.");
            var result = SliderRepresentationEditing.ConvertToTrack(map, map.ImportedSliders.Single().Id, derandomize: false);
            Check(result.Success, string.Join("; ", result.Diagnostics));
            snapshot = map.DeepClone();
            var output = OsuBeatmapWriter.Serialize(map);
            string[] fields = output.ReadBack.ImportedSliders.Single().OriginalLine!.Split(',');
            Check(fields.Length >= 11 && fields.Skip(8).Take(3).All(s => s.Length > 0),
                "Generated slider emitted empty optional sample fields.");
            CheckSamples(map, output);
            Check(map.ContentEquals(snapshot), "Optional sample defaults mutated the authored track.");
            var cache = new OsuWriteCache();
            for (int i = 0; i < 2; i++)
                Check(OsuBeatmapWriter.Serialize(map, cache: cache).Text == output.Text, "Cached export changed optional samples.");
            map = ProjectSerializer.Read(ProjectSerializer.Serialize(map));
            Check(OsuBeatmapWriter.Serialize(map).Text == output.Text, "Project round trip changed optional samples.");
        }

        var authored = new MapDocument();
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
        authored.Tracks.Add(track);
        ObjectFlags.SetNewCombo(authored, track.Id, true);
        CheckSamples(authored, OsuBeatmapWriter.Serialize(authored));

        static void CheckSamples(MapDocument map, OsuWriteResult output)
        {
            var objects = CatchStreamConverter.Convert(map).Objects;
            var source = new HitsoundResolver(map, objects);
            var target = new HitsoundResolver(output.ReadBack, output.PlayableObjects);
            Check(objects.Select(source.Describe).SequenceEqual(output.PlayableObjects.Select(target.Describe)),
                "Filling optional fields changed inherited hitsounds or sample banks.");
            foreach (var slider in output.ReadBack.ImportedSliders)
            {
                string[] fields = slider.OriginalLine!.Split(',');
                if (fields.Length > 8)
                    Check(fields[8].Split('|').All(s => int.TryParse(s, out _)), "Present edge sounds must contain integers.");
                if (fields.Length > 9)
                    Check(fields[9].Split('|').All(s => s.Split(':') is var bank && bank.Length == 2
                        && bank.All(b => int.TryParse(b, out _))), "Present edge sets must contain integer bank pairs.");
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
