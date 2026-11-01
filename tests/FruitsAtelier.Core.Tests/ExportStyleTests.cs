using FruitsAtelier.Core;

internal static class ExportStyleTests
{
    public static void Run(string? path = null)
    {
        if (path is not null) { Verify(OsuBeatmapReader.ReadFile(path)); return; }
        var map = OsuBeatmapReader.Read("""
            osu file format v14
            [General]
            Mode: 2
            [Metadata]
            Version: Platter
            [Difficulty]
            CircleSize: 4
            ApproachRate: 8
            SliderMultiplier: 1.4
            SliderTickRate: 1
            [TimingPoints]
            0,500,4,2,1,60,1,0
            [HitObjects]
            100,192,1000,5,0,0:0:0:0:
            120,192,1100,1,0,0:0:0:0:
            180,192,1200,1,0,0:0:0:0:
            250,192,1300,1,0,0:0:0:0:
            300,192,1400,5,0,0:0:0:0:
            320,192,1500,1,0,0:0:0:0:
            100,160,2000,6,2,B|150:90|200:160,2,150,2|4|8,1:2|2:1|0:0,1:2:3:60:
            300,383,4000,2,0,L|300:1,1,382
            256,192,6000,8,0,7000
            250,192,7500,5,0,0:0:0:0:
            """);
        Verify(map);
        var output = OsuBeatmapWriter.Serialize(map, experimentalExportStyle: true);
        var fruits = output.ReadBack.Fruits;
        double step = CatchScrollTiming.PixelsPerMs(8, 512) * .6 * 100;
        for (int i = 1; i < 4; i++)
            Check(Math.Abs(fruits[i - 1].Y - fruits[i].Y - step) <= 1, "Group must preserve linear time projection");
        Check(fruits[4].Y > fruits[3].Y, "NC starts a new local projection");
        Check(output.ReadBack.ImportedSliders[1].Y == 383, "Full-height slider cannot move");
        foreach (var track in new[] { CurveKind.Linear, CurveKind.Bezier })
        {
            var generated = new MapDocument { ApproachRate = 8, DurationMs = 5000 };
            var curve = new CurveTrack { Kind = track };
            curve.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 120 }, new Anchor { TimeMs = 1500, X = 280 }]);
            generated.Tracks.Add(curve); Verify(generated);
            curve.StreamSnapDivisor = 4; Verify(generated);
        }
        var high = new MapDocument { ApproachRate = 8, DurationMs = 5000 };
        high.OriginalSections.Add(new() { Name = "Metadata", Lines = { "Version:Overdose" } });
        high.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1100, X = 300 }, new() { TimeMs = 1200, X = 100 }]);
        var chain = OsuBeatmapWriter.Serialize(high, experimentalExportStyle: true).ReadBack.Fruits;
        Check(chain[0].Y > chain[1].Y && chain[1].Y > chain[2].Y, "Uniform HD chains must not split at every hyperdash");
        high.Fruits[1].X = 110; high.Fruits[2].X = 400; high.Fruits.Add(new() { TimeMs = 1300, X = 410 });
        var separated = OsuBeatmapWriter.Serialize(high, experimentalExportStyle: true).ReadBack.Fruits;
        Check(separated[2].Y > separated[1].Y, "HD between compact runs should start a new local projection");
        Console.WriteLine("PASS export layout: projection, NC, slider translation, NM/HR, repeatability and source isolation");
    }

    private static void Verify(MapDocument map)
    {
        var snapshot = map.DeepClone();
        var cache = new OsuWriteCache();
        foreach (bool compensate in new[] { false, true })
        {
            var plain = OsuBeatmapWriter.Serialize(map, compensate, cache);
            var styled = OsuBeatmapWriter.Serialize(map, compensate, cache, true);
            Check(map.ContentEquals(snapshot), "Layout changed authoring data");
            Check(plain.Text == OsuBeatmapWriter.Serialize(map, compensate, cache).Text, "Off must retain ordinary export");
            Check(styled.Text == OsuBeatmapWriter.Serialize(map, compensate, cache, true).Text, "Layout must be deterministic");
            Check(ReferenceEquals(ExperimentalExportLayout.Apply(plain), ExperimentalExportLayout.Apply(plain)), "Unchanged preview must reuse layout");
            var a = plain.ReadBack; var b = styled.ReadBack;
            Check(a.Fruits.Zip(b.Fruits).All(p => p.First.X == p.Second.X && p.First.TimeMs == p.Second.TimeMs), "Fruit X/time changed");
            foreach (var (oldSlider, slider) in a.ImportedSliders.Zip(b.ImportedSliders))
            {
                double delta = slider.Y - oldSlider.Y;
                Check(slider.X == oldSlider.X && slider.TimeMs == oldSlider.TimeMs && slider.PathType == oldSlider.PathType
                    && slider.PixelLength == oldSlider.PixelLength && slider.SpanCount == oldSlider.SpanCount, "Slider shape metadata changed");
                Check(slider.ControlPoints.Zip(oldSlider.ControlPoints).All(p => p.First.X == p.Second.X
                    && p.First.GeometryY - p.Second.GeometryY == delta), "Slider must translate every control by the same Y offset");
                var oldFields = oldSlider.OriginalLine!.Split(','); var fields = slider.OriginalLine!.Split(',');
                Check(oldFields.Where((_, i) => i is not (1 or 5)).SequenceEqual(fields.Where((_, i) => i is not (1 or 5))), "Slider samples or other fields changed");
            }
            var ca = CatchStreamConverter.Convert(a); var cb = CatchStreamConverter.Convert(b);
            Same(ca.Objects, cb.Objects); Same(CatchPreviewMods.HardRock(a, ca), CatchPreviewMods.HardRock(b, cb));
        }
    }
    private static void Same(IReadOnlyList<ConvertedCatchObject> a, IReadOnlyList<ConvertedCatchObject> b)
        => Check(a.Count == b.Count && a.Zip(b).All(p => p.First.Kind == p.Second.Kind && p.First.TimeMs == p.Second.TimeMs
            && p.First.X == p.Second.X), "Export changed playable NM/HR events");
    private static void Check(bool success, string message) { if (!success) throw new Exception(message); }
}
