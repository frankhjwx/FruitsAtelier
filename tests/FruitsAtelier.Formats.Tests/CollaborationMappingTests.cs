using FruitsAtelier.Core;

internal static class CollaborationMappingTests
{
    public static void Run()
    {
        var a = Difficulty("A", 500, 2, 80, 100, 997, 1000);
        var b = Difficulty("B", 400, 3, 60, 900, 1000, 1999, 2000);
        var c = Difficulty("C", 600, 1, 40, 99, 100, 1500, 2000, 2500);
        var d = Difficulty("D", 600, 1, 40, 3000);
        var project = new BeatmapProject { Name = "Collaboration", Difficulties = [a, b, c, d] };
        CollaborationReference[] references = [new(Guid.NewGuid(), a.Id, 100, 1000), new(Guid.NewGuid(), b.Id, 1000, 2000)];
        var original = c.Document.DeepClone();
        var first = Synchronize(project, c.Id, references);
        Check(first.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new double[] {99,100,997,1000,1999,2000,2500}), "Half-open ownership and untouched outside objects");
        Check(c.Document.ContentEquals(original), "Synchronization never mutates its input");
        Near(TimingMap.At(first, 500).BeatLengthMs, 500, "A BPM");
        Near(TimingMap.At(first, 1200).BeatLengthMs, 400, "B BPM");
        Near(TimingMap.At(first, 2000).BeatLengthMs, 600, "Restored outside BPM");
        Sounds(first, [(99,1,40),(100,2,80),(997,2,80),(1000,3,60),(1999,3,60),(2000,1,40),(2500,1,40)]);
        c.Document = first;
        Check(Synchronize(project, c.Id, references).ContentEquals(first), "Repeated synchronization is idempotent");
        var history = new EditorHistory(original); history.RestoreVersion("sync", first);
        history.Undo(); Check(history.Document.ContentEquals(original), "Single-step undo");
        history.Redo(); Check(history.Document.ContentEquals(first), "Redo includes references and samples");
        string saved = ProjectSerializer.Serialize(project);
        Check(saved.Contains("\"SchemaVersion\": 34"), "Multi-difficulty schema protects references");
        var reopened = ProjectSerializer.ReadProject(saved);
        Check(reopened.Difficulties[2].Document.ContentEquals(first), "Project roundtrip");
        var single = ProjectSerializer.Serialize(first);
        Check(single.Contains("\"SchemaVersion\": 33") && ProjectSerializer.Read(single).ContentEquals(first), "Difficulty roundtrip");
        a.Document.Fruits.RemoveAll(f => f.TimeMs == 100);
        a.Document.Fruits.Single(f => f.TimeMs == 997).X = 250;
        a.Document.TimingPoints[0].Volume = 73;
        var updated = Synchronize(project, c.Id, references);
        Check(!updated.Fruits.Any(f => f.TimeMs == 100) && updated.Fruits.Single(f => f.TimeMs == 997).X == 250, "Source deletions and changes");
        Sounds(updated, [(99,1,40),(997,2,73),(1000,3,60),(1999,3,60),(2000,1,40),(2500,1,40)]);
        d.Document = Synchronize(project, d.Id, [new(Guid.NewGuid(), a.Id, 100, 1000)]);
        Check(d.Document.CollaborationReferences!.Single().DifficultyId == a.Id && c.Document.CollaborationReferences!.Count == 2,
            "Multiple targets independently reference the same source");
        Throws(() => Synchronize(project, c.Id, [references[0], references[1] with { StartMs = 999 }]), "Overlap");
        Throws(() => Synchronize(project, c.Id, [references[0] with { StartMs = double.NaN }]), "Nonfinite range");
        Throws(() => Synchronize(project, c.Id, [references[0] with { StartMs = 1.5 }]), "Fractional milliseconds");
        Throws(() => Synchronize(project, c.Id, [references[0] with { DifficultyId = c.Id }]), "Self reference");
        Throws(() => Synchronize(project, c.Id, [references[0] with { DifficultyId = Guid.NewGuid() }]), "Missing source");
        a.Document.CollaborationReferences = [new(Guid.NewGuid(), c.Id, 100, 1000)];
        Check(Synchronize(project, c.Id, references).CollaborationReferences!.Count == 2, "Manual synchronization reads direct snapshots even with reverse references");
        a.Document.CollaborationReferences = null;
        Sliders();
        CustomSamples();
        TickRateAndResources();
        MultipleRanges();
        BookmarkSections();
        CompleteSections();
        Console.WriteLine("PASS collaboration objects, samples, timing, graph, schemas, repeated sync and undo");
    }

    private static MapDocument Synchronize(BeatmapProject project, Guid target, IReadOnlyList<CollaborationReference> references)
        => CollaborationMapping.Synchronize(project, target, references).Document;

    private static void CompleteSections()
    {
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(
            "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\n[TimingPoints]\n0,500,4,2,0,70,1,0\n[HitObjects]\n" +
            "100,192,0,2,0,L|240:192,2,140\n100,192,600,2,0,L|240:192,1,140\n" +
            "100,192,1000,2,0,L|240:192,2,140\n100,192,1000,2,0,L|240:192,1,140\n" +
            "180,192,800,1,0,0:0:0:0:\n") };
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2 };
        track.Nodes.AddRange([new() { TimeMs = 600, X = 160 }, new() { TimeMs = 1100, X = 260 }]);
        source.Document.Tracks.Add(track);
        source.Document.BananaShowers.AddRange([new() { TimeMs = 300, EndTimeMs = 900 },
            new() { TimeMs = 1200, EndTimeMs = 1600 }, new() { TimeMs = 1100, EndTimeMs = 1500 }]);
        var target = Difficulty("target", 600, 1, 40, 750, 2500);
        var project = new BeatmapProject { Name = "containment", Difficulties = [source, target] };
        CollaborationReference[] refs = [new(Guid.NewGuid(), source.Id, 500, 1500)];
        var prepared = CollaborationMapping.Synchronize(project, target.Id, refs);
        Check(prepared.Document.ImportedSliders.Count == 2 && prepared.Document.Tracks.Count == 0,
            "Sliders crossing either boundary and repeated FSlider tails are excluded");
        Check(prepared.Document.BananaShowers.Single().EndTimeMs == 1500, "Spinner ending exactly at the section boundary is included");
        Check(prepared.Document.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new[] {800d,2500d}), "Other objects sync normally");
        Check(prepared.ExcludedObjects.Count == 5 && prepared.ExcludedObjects.Count(o => o.Spinner) == 2,
            "Partial source intersections are returned as report entries instead of errors");
        Check(prepared.ExcludedObjects.All(o => o.DifficultyId == source.Id && o.SectionStartMs == 500 && o.SectionEndMs == 1500),
            "Exclusions retain source identity and section bounds");
        Near(TimingMap.At(prepared.Document, 800).BeatLengthMs, 500, "Timing still synchronizes after exclusions");
        target.Document = prepared.Document;
        Check(Synchronize(project, target.Id, refs).ContentEquals(prepared.Document), "Containment filtering is idempotent");
    }

    private static void BookmarkSections()
    {
        var source = Difficulty("source", 400, 2, 80, 100, 1100, 2100);
        var target = Difficulty("target", 600, 1, 40, 200, 1200, 2200);
        target.Document.DurationMs = 3000;
        SongSetup.Set(target.Document, "Editor", "Bookmarks", "2000,0,1000,1000,3000,4000");
        var sections = CollaborationMapping.SectionsFromBookmarks(target.Document);
        Check(sections.Select(r => (r.StartMs, r.EndMs)).SequenceEqual(new[] { (0d,1000d), (1000d,2000d), (2000d,3000d) }),
            "Bookmark sections cover the timeline without duplicate or zero-length ranges");
        Check(sections.All(r => r.DifficultyId == Guid.Empty) && sections.Select(r => r.Id).Distinct().Count() == 3,
            "Generated sections have independent identities and empty sources");
        target.Document.CollaborationReferences = sections;
        var project = new BeatmapProject { Name = "bookmarks", Difficulties = [source, target] };
        Check(Synchronize(project, target.Id, sections).ContentEquals(target.Document), "All-empty synchronization is content neutral");
        string single = ProjectSerializer.Serialize(target.Document), multi = ProjectSerializer.Serialize(project);
        Check(single.Contains("\"SchemaVersion\": 35") && ProjectSerializer.Read(single).ContentEquals(target.Document), "Empty source single-difficulty persistence");
        Check(multi.Contains("\"SchemaVersion\": 36") && ProjectSerializer.ReadProject(multi).Difficulties[1].Document.ContentEquals(target.Document),
            "Empty source multi-difficulty persistence");
        Check(ProjectSerializer.ReadProject(single).Difficulties[0].Document.ContentEquals(target.Document), "Single document opens as project");
        Throws(() => Synchronize(project, target.Id, [sections[0], sections[1] with { StartMs = 999 }]), "Two empty sources still cannot overlap");
        sections[0] = sections[0] with { DifficultyId = source.Id };
        sections[2] = sections[2] with { DifficultyId = source.Id };
        Throws(() => Synchronize(project, target.Id, [sections[0], sections[1] with { StartMs = 999 }]), "Assigned and empty sources cannot overlap");
        var merged = Synchronize(project, target.Id, sections);
        Check(merged.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new[] { 100d,1200d,2100d }), "Only assigned sections replace objects");
        Check(merged.Fruits.Single(f => f.TimeMs == 1200).Id == target.Document.Fruits.Single(f => f.TimeMs == 1200).Id, "Empty section retains parent identity");
        Near(TimingMap.At(merged, 1200).BeatLengthMs, 600, "Timing restores at an adjacent unassigned section");
        Sounds(merged, [(100,2,80),(1200,1,40),(2100,2,80)]);
        target.Document = merged;
        Check(Synchronize(project, target.Id, sections).ContentEquals(merged), "Repeated synchronization with an empty middle section");
        sections[0] = sections[0] with { DifficultyId = Guid.Empty };
        source.Document.Fruits.Clear();
        var cleared = Synchronize(project, target.Id, sections);
        Check(cleared.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new[] { 100d,1200d }), "Clearing a source preserves previously merged content");
        var blank = new MapDocument { DurationMs = 1000.5 };
        Check(CollaborationMapping.SectionsFromBookmarks(blank).Single().EndMs == 1001, "No bookmarks creates one whole-millisecond section");
        Check(CollaborationMapping.SectionsFromBookmarks(blank, 5000).Single().EndMs == 5000, "Loaded audio can extend the final section");
        SongSetup.Set(blank, "Editor", "Bookmarks", string.Join(',', Enumerable.Range(1, 256)));
        Throws(() => CollaborationMapping.SectionsFromBookmarks(blank), "Section count limit");
    }

    private static void MultipleRanges()
    {
        var source = Difficulty("source", 500, 2, 80, 100, 600, 1100, 1600, 2100);
        var target = Difficulty("target", 600, 1, 40, 700, 1700, 3000);
        var project = new BeatmapProject { Name = "ranges", Difficulties = [source, target] };
        var references = new[] { new CollaborationReference(Guid.NewGuid(), source.Id, 0, 500),
            new(Guid.NewGuid(), source.Id, 1000, 1500), new(Guid.NewGuid(), source.Id, 2000, 2500) };
        target.Document = Synchronize(project, target.Id, references);
        Check(target.Document.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new double[] {100,700,1100,1700,2100,3000}), "Multiple disjoint ranges from one source preserve the gaps");
        Near(TimingMap.At(target.Document, 700).BeatLengthMs, 600, "Timing gap retains target state");
        Check(Synchronize(project, target.Id, references).ContentEquals(target.Document), "Repeated multi-range synchronization");
        source.Document.Fruits.RemoveAll(f => f.TimeMs == 100);
        source.Document.Fruits.Single(f => f.TimeMs == 1100).X = 300;
        target.Document = Synchronize(project, target.Id, references);
        Check(!target.Document.Fruits.Any(f => f.TimeMs == 100) && target.Document.Fruits.Single(f => f.TimeMs == 1100).X == 300,
            "All ranges synchronize source deletions and edits together");
        var reopened = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project));
        Check(reopened.Difficulties[1].Document.CollaborationReferences!.Count == 3, "Multiple ranges persist independently");
    }

    private static void Sliders()
    {
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,2,70,1,1\n700,-100,4,3,3,60,0,0\n[HitObjects]\n100,192,500,2,2,L|240:192,2,140,2|4|8,2:3|3:2|2:2,0:0:0:0:\n") };
        var target = Difficulty("target", 500, 1, 40);
        source.Document.DurationMs = 5000;
        target.Document.SliderMultiplier = source.Document.SliderMultiplier;
        var project = new BeatmapProject { Name = "sliders", Difficulties = [source, target] };
        var refs = new[] { new CollaborationReference(Guid.NewGuid(), source.Id, 0, 2000) };
        var merged = Synchronize(project, target.Id, refs);
        Check(merged.ImportedSliders.Count == 1, "Native repeated slider retained");
        target.Document = merged;
        Check(Synchronize(project, target.Id, refs).ContentEquals(merged), "Repeated slider synchronization");
        var stream = new CurveTrack { StreamSnapDivisor = 4, Kind = CurveKind.Linear };
        stream.Nodes.AddRange([new() { TimeMs = 2100, X = 140 }, new() { TimeMs = 2600, X = 280 }]);
        source.Document.Tracks.Add(stream);
        refs = [refs[0] with { EndMs = 3000 }];
        merged = Synchronize(project, target.Id, refs);
        Check(merged.Tracks.Count == 1 && merged.Tracks[0].Id != stream.Id && merged.Tracks[0].Nodes[0].Id != stream.Nodes[0].Id, "Stream authoring and detached identities");
        var pen = new CurveTrack { Kind = CurveKind.Linear };
        pen.Nodes.AddRange([new() { TimeMs = 3100, X = 160 }, new() { TimeMs = 3600, X = 300 }]);
        source.Document.Tracks.Add(pen);
        source.Document.BananaShowers.Add(new() { TimeMs = 3800, EndTimeMs = 4200 });
        target.Document.BananaShowers.Add(new() { TimeMs = 4500, EndTimeMs = 4700 });
        refs = [refs[0] with { EndMs = 4000 }];
        merged = Synchronize(project, target.Id, refs);
        Check(merged.Tracks.Count == 2 && merged.BananaShowers.Count == 1, "FSliders and outside bananas retained; boundary-crossing bananas excluded");
        target.Document = merged;
        Check(Synchronize(project, target.Id, refs).ContentEquals(merged), "Mixed-object idempotence");
        var single = ProjectSerializer.Read(ProjectSerializer.Serialize(merged));
        Check(single.ContentEquals(merged), "Mixed authoring roundtrip");
    }

    private static void CustomSamples()
    {
        string root = Path.GetFullPath("artifacts/tests/collaboration/resources"); Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "tap.wav"), new byte[44]);
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(
            "osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500,4,2,2,70,1,0\n[HitObjects]\n180,192,500,1,8,2:3:2:65:tap.wav\n", Path.Combine(root, "source.osu")) };
        var target = Difficulty("target", 500, 1, 100); target.Document.SourcePath = Path.Combine(root, "target.osu");
        var project = new BeatmapProject { Name = "sounds", Difficulties = [source, target] };
        var merged = Synchronize(project, target.Id, [new(Guid.NewGuid(), source.Id, 0, 1000)]);
        var output = OsuBeatmapWriter.Serialize(merged);
        var resolver = new HitsoundResolver(merged, output.PlayableObjects);
        var item = output.PlayableObjects.Single();
        Check(resolver.Describe(item) is { FileName: "tap.wav", Volume: 65, Additions: 8 }, "Custom file, volume and clap retained");
        Check(resolver.Resolve(item).Any(s => s.FilePath == Path.Combine(root, "tap.wav")), "Custom resource resolves in target");
    }

    private static void TickRateAndResources()
    {
        string root = Path.GetFullPath("artifacts/tests/collaboration/resource-transfer");
        string sourceRoot = Path.Combine(root, "source"), targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(targetRoot);
        File.WriteAllBytes(Path.Combine(sourceRoot, "tap.wav"), [1,2,3]);
        File.WriteAllBytes(Path.Combine(targetRoot, "tap.wav"), [4,5,6]);
        File.WriteAllBytes(Path.Combine(sourceRoot, "soft-hitnormal2.wav"), [7,8,9]);
        File.WriteAllBytes(Path.Combine(targetRoot, "soft-hitnormal2.wav"), [10,11,12]);
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(
            "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,2,70,1,0\n[HitObjects]\n180,192,500,1,8,2:3:2:65:tap.wav\n180,192,700,1,0,2:2:2:65:\n100,192,1000,2,4,L|240:192,2,140,4|0|0,2:2|2:2|2:2,0:0:0:0:\n", Path.Combine(sourceRoot, "source.osu")) };
        var target = Difficulty("target", 500, 1, 100); target.Document.SourcePath = Path.Combine(targetRoot, "target.osu");
        target.Document.SliderTickRate = 2;
        source.Document.DurationMs = 4000;
        var stream = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4 };
        stream.Nodes.AddRange([new() { TimeMs = 2500, X = 140 }, new() { TimeMs = 3000, X = 260 }]);
        source.Document.Tracks.Add(stream);
        foreach (var item in OsuBeatmapWriter.Serialize(source.Document).PlayableObjects.Where(o => o.SourceId == stream.Id))
            source.Document.HitsoundOverrides.Add(new(item.SourceId, item.EventIndex, new(2, 2, 2, 55), InheritedFields: InheritedSampleFields.None));
        var project = new BeatmapProject { Name = "resources", Difficulties = [source, target] };
        var references = new[] { new CollaborationReference(Guid.NewGuid(), source.Id, 0, 3000) };
        var before = source.Document.DeepClone();
        var prepared = CollaborationMapping.Synchronize(project, target.Id, references);
        Check(prepared.Document.SliderTickRate == 2 && source.Document.ContentEquals(before), "Target STR wins without source mutation");
        prepared.Resources.Apply();
        var output = OsuBeatmapWriter.Serialize(prepared.Document);
        var resolver = new HitsoundResolver(prepared.Document, output.PlayableObjects);
        var custom = output.PlayableObjects.Single(o => o.TimeMs == 500);
        var customPath = resolver.Resolve(custom).First().FilePath!;
        Check(customPath != Path.Combine(targetRoot, "tap.wav") && File.ReadAllBytes(customPath).SequenceEqual(new byte[] {1,2,3}), "Conflicting custom samples are copied independently");
        var numbered = output.PlayableObjects.Single(o => o.TimeMs == 700);
        var numberedPath = resolver.Resolve(numbered).First().FilePath!;
        Check(numberedPath != Path.Combine(targetRoot, "soft-hitnormal2.wav") && File.ReadAllBytes(numberedPath).SequenceEqual(new byte[] {7,8,9}), "Conflicting numbered sample groups are remapped");
        Check(output.PlayableObjects.Count(o => o.Kind == CatchObjectKind.Droplet) > OsuBeatmapWriter.Serialize(source.Document).PlayableObjects.Count(o => o.Kind == CatchObjectKind.Droplet), "Merged STR generates the target tick density");
        target.Document = prepared.Document;
        var repeated = CollaborationMapping.Synchronize(project, target.Id, references);
        Check(repeated.Document.ContentEquals(prepared.Document), "Resource and STR synchronization is idempotent");
        prepared.Resources.Restore(); prepared.Resources.Apply();
    }

    private static ProjectDifficulty Difficulty(string name, double beat, int bank, int volume, params double[] times)
    {
        var document = new MapDocument { Name = name, IsDemo = false, DurationMs = 5000 };
        document.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = beat, SampleSet = bank, Volume = volume });
        foreach (double time in times) document.Fruits.Add(new() { TimeMs = time, X = 180 });
        return new() { Name = name, Document = document };
    }
    private static void Sounds(MapDocument document, (double Time, int Bank, int Volume)[] expected)
    {
        var readback = OsuBeatmapWriter.Serialize(document).ReadBack;
        var events = CatchStreamConverter.Convert(readback).Objects;
        var resolver = new HitsoundResolver(readback, events);
        foreach (var item in expected)
        {
            var sample = resolver.Describe(events.Single(o => o.TimeMs == item.Time));
            Check(sample.NormalSet == item.Bank && sample.Volume == item.Volume, $"Exported sound at {item.Time}");
        }
    }
    private static void Throws(Action action, string message)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception(message + " should fail"); }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-6, message);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
