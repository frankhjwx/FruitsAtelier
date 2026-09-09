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
            Require(resolver.Resolve(item).All(s => s.Name != "custom"), "Sample paths cannot escape the map resource index");
            Require(resolver.Resolve(item with { Kind = CatchObjectKind.TinyDroplet }).Count == 0, "Tiny droplets and slider body are silent");
            var banana = resolver.Resolve(item with { Kind = CatchObjectKind.Banana });
            Require(banana.Count == 1 && banana[0].Name == "catch-banana" && banana[0].FilePath == HitsoundDefaults.Find(1, "catch-banana"), "Bananas use their dedicated sound");
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
            foreach (int bank in new[] { 1, 2, 3 })
                foreach (string name in new[] { "hitnormal", "hitwhistle", "hitfinish", "hitclap", "slidertick" })
                    Require(HitsoundDefaults.Find(bank, name) is not null, $"Packaged sample missing: {bank}/{name}");
            var plain = new MapDocument(); plain.Fruits.Add(new Fruit { Id = item.SourceId, TimeMs = 100 });
            var baseSound = new HitsoundResolver(plain, new[] { item }).Resolve(item).Single();
            Require(baseSound.Name == "hitnormal" && baseSound.FilePath == HitsoundDefaults.Find(1, "hitnormal") && baseSound.FilePath is not null,
                "A fruit with no additions plays the actual default normal sample");
            plain.Fruits[0].OriginalLine = "100,192,100,1,8,0:0:0:0:";
            var layered = new HitsoundResolver(plain, new[] { item }).Resolve(item);
            Require(layered.Count == 2 && layered[0].Name == "hitnormal" && layered[1].Name == "hitclap", "Additions retain the default normal layer");
            var preloadView = new EditorView();
            IReadOnlyList<MapDocument>? snapshot = null;
            preloadView.RequestPreloadHitsounds = maps => snapshot = maps;
            preloadView.LoadProject(BeatmapProject.FromDocuments(new[] { plain, plain.DeepClone() }));
            Require(snapshot?.Count == 2 && !ReferenceEquals(snapshot[0], preloadView.Document), "Opening a project preloads all difficulties using isolated snapshots");
            int oldCount = snapshot![0].Fruits.Count;
            preloadView.Document.Fruits.Add(new() { TimeMs = 1000 });
            Require(snapshot[0].Fruits.Count == oldCount, "Loader snapshots do not observe live editor changes");
            Scheduler();
            ScheduledPlayback();
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
    private static void ScheduledPlayback()
    {
        var document = new MapDocument { AudioPath = "music.wav" };
        foreach (double time in new[] { 0.0, 100, 100, 150, 500 }) document.Fruits.Add(new() { TimeMs = time });
        var view = new EditorView(); view.LoadDocument(document);
        var queued = new List<double>(); int cancellations = 0;
        view.RequestScheduleHitsound = (_, time) => queued.Add(time);
        view.RequestStopHitsounds = () => { queued.Clear(); cancellations++; };
        void Poll(double time, bool playing = true) => view.UpdateTransport(time, 5000, true, playing, false, null, "music.wav");
        view.StartHitsounds(0); Poll(0);
        Require(queued.SequenceEqual(new[] { 0.0, 100, 100 }), "Lookahead queues notes with original timestamps before their hit time");
        Poll(16); Require(queued.Count == 3, "Lookahead does not duplicate previously scheduled notes");
        Poll(60); Require(queued.Last() == 150, "Lookahead extends as the device clock advances");
        Poll(60, false); Require(queued.Count == 0, "Pause cancels future notes");
        view.StartHitsounds(60); Poll(60);
        Require(queued.SequenceEqual(new[] { 100.0, 100, 150 }), "Resume reschedules canceled future notes without past notes");
        view.ResetHitsounds(); Poll(400);
        Require(queued.SequenceEqual(new[] { 500.0 }), "Seek schedules only the destination horizon");
        int beforeEdit = cancellations;
        view.Document.Fruits.Add(new() { TimeMs = 450 }); Poll(416);
        Require(cancellations > beforeEdit && queued.SequenceEqual(new[] { 450.0, 500 }), "Edits cancel and rebuild future sounds");
        Poll(900); Require(queued.Count == 0, "Large clock jumps cancel queued sounds");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
