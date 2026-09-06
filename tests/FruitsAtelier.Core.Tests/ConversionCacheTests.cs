using System.Text.Json;
using FruitsAtelier.Core;

internal static class ConversionCacheTests
{
    public static void EditingMatchesFullConversion()
    {
        var document = new MapDocument { DurationMs = 20000 };
        var first = Track(1000); var second = Track(4000);
        document.Tracks.AddRange([first, second]);
        var banana = new BananaShower { TimeMs = 0, EndTimeMs = 500 };
        document.BananaShowers.Add(banana);
        var imported = new ImportedSlider { TimeMs = 2000, X = 256, Y = 100, PathType = 'L', PixelLength = 200 };
        imported.ControlPoints.Add(new(356, 100)); document.ImportedSliders.Add(imported);
        var cache = new CatchConversionCache(); bool compensation = false;
        Check(); Check();
        var previous = CatchStreamConverter.Convert(document, compensation, cache);
        var fruit = new Fruit { TimeMs = 600, X = 80 }; document.Fruits.Add(fruit); Check();
        var added = CatchStreamConverter.Convert(document, compensation, cache);
        if (!ReferenceEquals(previous.Sliders[0], added.Sliders[0])) throw new Exception("Fruit edits rebuilt an unchanged slider.");
        fruit.X = 300; Check(); fruit.TimeMs = 9000; Check(); document.Fruits.Clear(); Check();
        first.Nodes[0].X = 220; Check();
        first.Kind = CurveKind.Bezier; first.Nodes[0].HandleOut = new(200, 20); first.Nodes[1].HandleIn = new(-200, -20); Check();
        first.SpanCount = 3; Check();
        banana.EndTimeMs = 900; Check();
        imported.PixelLength = 350; imported.SpanCount = 2; Check();
        imported.TimeMs = first.Nodes[0].TimeMs; imported.SourceOrder = -1; Check();
        imported.SourceOrder = 10; Check();
        document.Tracks.Reverse(); Check();
        document.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 400 }); Check();
        document.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = -50, Uninherited = false }); Check();
        document.TimingPoints[1].BeatLengthMs = -80; Check();
        document.SliderTickRate = 2; Check(); document.SliderMultiplier = 1.5; Check();
        compensation = true; Check(); first.CompensateTinyDroplets = false; Check();
        first.Nodes[0].X = -1; Check(); first.Nodes[0].X = 220; Check();
        document.Tracks.Remove(second); Check(); document.Tracks.Add(second); Check();
        document.BananaShowers.Clear(); Check(); document.ImportedSliders.Clear(); Check();
        document.TimingPoints.Clear(); Check();
        document = document.DeepClone(); Check();

        void Check()
        {
            var before = document.DeepClone();
            var actual = CatchStreamConverter.Convert(document, compensation, cache);
            var expected = CatchStreamConverter.Convert(document, compensation);
            if (JsonSerializer.Serialize(actual) != JsonSerializer.Serialize(expected))
                throw new Exception("Cached conversion diverged from full conversion (including paths, RNG, and diagnostics).");
            if (!document.ContentEquals(before)) throw new Exception("Conversion mutated source data.");
        }
        static CurveTrack Track(double time)
        {
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.Add(new Anchor { TimeMs = time, X = 256 });
            track.Nodes.Add(new Anchor { TimeMs = time + 1000, X = 300 });
            return track;
        }
    }
}
