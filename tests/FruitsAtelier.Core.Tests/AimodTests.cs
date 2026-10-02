using FruitsAtelier.Core;

internal static class AimodTests
{
    public static void Run()
    {
        var map = new MapDocument();
        map.Fruits.AddRange([new() { TimeMs = 1020, X = 0 }, new() { TimeMs = 1000, X = 512 },
            new() { TimeMs = 1010, X = 0 }, new() { TimeMs = 1009.999, X = 0 }]);
        var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 2000, X = 100 }, new() { TimeMs = 3000, X = 200 }]);
        map.Tracks.Add(track);
        var imported = new ImportedSlider { TimeMs = 2005, X = 200 };
        map.ImportedSliders.Add(imported);
        var banana = new BananaShower { TimeMs = 2005, EndTimeMs = 3000 };
        map.BananaShowers.Add(banana);
        map.Fruits.Add(new() { TimeMs = 3000, X = 200 });
        var before = map.DeepClone();
        var errors = Aimod.FindOverlaps(map);
        if (errors.Count != 4 || errors[0].SecondTimeMs - errors[0].FirstTimeMs >= 10
            || errors[2].FirstId != track.Id || errors[2].SecondId != imported.Id
            || errors[3].SecondId != banana.Id || errors.Any(e => e.SecondTimeMs == 3000)
            || !before.ContentEquals(map)) throw new Exception("AiMod scope, strict threshold, chronological order or read-only behavior changed.");
        var dense = new MapDocument();
        for (int i = 0; i < 10000; i++) dense.Fruits.Add(new() { TimeMs = 1000 });
        if (Aimod.FindOverlaps(dense).Count != 9999) throw new Exception("Dense stacks must identify adjacent pairs with bounded output.");
    }
}
