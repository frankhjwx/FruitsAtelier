using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

static class HitsoundTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "fruits-hitsounds-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            foreach (string name in new[] { "soft-hitnormal2.wav", "drum-hitclap2.ogg", "normal-slidertick2.wav", "Custom.wav" })
                File.WriteAllBytes(Path.Combine(root, name), HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
            var document = new MapDocument { SourcePath = Path.Combine(root, "map.osu"), IsDemo = false };
            document.TimingPoints.Add(new() { TimeMs = 0, SampleSet = 2, SampleIndex = 2, Volume = 40 });
            var fruit = new Fruit { TimeMs = 100, X = 100, OriginalLine = "100,192,100,1,8,0:3:0:0:" };
            document.Fruits.Add(fruit);
            var item = new ConvertedCatchObject(fruit.Id, 0, CatchObjectKind.Fruit, 100, 100, 100, 100, 0, true);
            var resolver = new HitsoundResolver(document, new[] { item });
            var sounds = resolver.Resolve(item);
            Require(sounds.Count == 2 && sounds[0].FilePath == Path.Combine(root, "soft-hitnormal2.wav") && sounds[1].FilePath == Path.Combine(root, "drum-hitclap2.ogg"), "Timing bank/index and addition bank select map samples");
            Require(sounds.All(s => Math.Abs(s.Volume - .4) < .001), "Timing volume is inherited");
            fruit.OriginalLine = "100,192,100,1,14,1:3:0:75:custom.wav";
            resolver = new(document, new[] { item }); sounds = resolver.Resolve(item);
            Require(sounds.Count == 4 && sounds[0].FilePath == Path.Combine(root, "Custom.wav") && sounds.All(s => s.Volume == .75f), "Case-insensitive custom sample replaces normal while retaining additions and explicit volume");
            fruit.OriginalLine = "100,192,100,1,14,1:3:0:75:../outside.wav";
            resolver = new(document, new[] { item });
            Require(resolver.Resolve(item).All(s => s.FilePath is null || Path.GetDirectoryName(s.FilePath) == root), "Sample paths cannot escape the map resource index");
            Require(resolver.Resolve(item with { Kind = CatchObjectKind.TinyDroplet }).Count == 0, "Tiny droplets and slider body are silent");
            var banana = resolver.Resolve(item with { Kind = CatchObjectKind.Banana });
            Require(banana.Count == 1 && banana[0].Name == "catch-banana" && banana[0].FilePath is null, "Bananas use their dedicated sound");
            var slider = new ImportedSlider { TimeMs = 0, OriginalLine = "100,192,0,2,2,L|300:192,2,200,0|8|4,1:0|2:3|3:1,0:0:0:0:" };
            document.ImportedSliders.Add(slider);
            var events = new[] {
                item with { SourceId = slider.Id, IsStandalone = false, EventIndex = 0, TimeMs = 0 },
                item with { SourceId = slider.Id, IsStandalone = false, EventIndex = 1, TimeMs = 200, Kind = CatchObjectKind.Droplet },
                item with { SourceId = slider.Id, IsStandalone = false, EventIndex = 2, TimeMs = 400 },
                item with { SourceId = slider.Id, IsStandalone = false, EventIndex = 3, TimeMs = 800 }
            };
            resolver = new(document, events);
            Require(resolver.Resolve(events[0]).Count == 1, "Explicit zero edge flags suppress parent additions");
            Require(resolver.Resolve(events[1]).Single().Name == "slidertick", "Slider ticks use slidertick");
            Require(resolver.Resolve(events[2])[1].Name == "hitclap" && resolver.Resolve(events[2])[1].SampleSet == 3, "Repeat edge uses its own flags and addition bank");
            Require(resolver.Resolve(events[3])[1].Name == "hitfinish", "Tail uses final edge samples");
            document.TimingPoints.Add(new() { TimeMs = 500, Volume = 0 });
            resolver = new(document, events);
            Require(resolver.Resolve(events[3]).Count == 0, "Zero timing volume mutes samples");
            Scheduler();
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Scheduler()
    {
        var document = new MapDocument { AudioPath = "music.wav" };
        document.Fruits.Add(new() { TimeMs = 0 });
        document.Fruits.Add(new() { TimeMs = 100 });
        document.Fruits.Add(new() { TimeMs = 100 });
        document.Fruits.Add(new() { TimeMs = 150 });
        document.Fruits.Add(new() { TimeMs = 500 });
        var view = new EditorView(); view.LoadDocument(document);
        var sounds = new List<Hitsound>(); view.RequestHitsound = sounds.Add;
        void Poll(double time, bool playing = true) => view.UpdateTransport(time, 5000, true, playing, false, null, "music.wav");
        Poll(0); Poll(100); Poll(100);
        Require(sounds.Count == 3, "Time zero and simultaneous objects play exactly once");
        Poll(100, false); Poll(100); // Resuming must not duplicate the paused boundary.
        Require(sounds.Count == 3, "Pause/resume does not duplicate hits at the boundary");
        view.ResetHitsounds(); Poll(400); Poll(500);
        Require(sounds.Count == 4, "Seeking skips objects between the old and new position");
        Poll(0); Poll(100);
        Require(sounds.Count == 6, "Backward seek rearms future objects without a backlog");
        Poll(1000); Require(sounds.Count == 6, "Long discontinuities discard stale events");
        Poll(0, false); view.StartHitsounds(0); Poll(16); Require(sounds.Count == 7, "First host poll after replay still includes the time-zero object");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
