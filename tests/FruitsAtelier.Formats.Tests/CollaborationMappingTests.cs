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
        foreach (double time in new[] { 500d, 1200, 2000 })
            Near(TimingMap.At(first, time).BeatLengthMs, 600, "Target BPM is retained across sections");
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
        PreserveTargetSounds();
        BoundaryTiming();
        ObjectVelocityAndSectionSamples();
        Console.WriteLine("PASS collaboration objects, samples, timing, graph, schemas, repeated sync and undo");
    }

    private static MapDocument Synchronize(BeatmapProject project, Guid target, IReadOnlyList<CollaborationReference> references)
        => CollaborationMapping.Synchronize(project, target, references).Document;

    private static void BoundaryTiming()
    {
        var source = Difficulty("source", 60000d / 179, 1, 80, 1500.75, 2500.25);
        var target = Difficulty("target", 60000d / 179, 1, 80, 500.5, 3500.5);
        source.Document.TimingPoints[0].TimeMs = target.Document.TimingPoints[0].TimeMs = .75;
        var project = new BeatmapProject { Name = "boundary timing", Difficulties = [source, target] };
        CollaborationReference[] references = [new(Guid.NewGuid(), source.Id, 1006, 2000), new(Guid.NewGuid(), source.Id, 2000, 3000)];
        var original = target.Document.DeepClone();
        var fromZero = Synchronize(project, target.Id, [new(Guid.NewGuid(), source.Id, 0, 3000)]);
        Check(fromZero.TimingPoints.Count == 1 && fromZero.TimingPoints[0].TimeMs == .75,
            "A section before the first fractional red point needs no extra timing point");
        CheckGrid(fromZero);
        var merged = Synchronize(project, target.Id, references);
        Check(merged.TimingPoints.Count == original.TimingPoints.Count, "Matching timing adds no section-boundary points");
        Check(merged.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new[] { 500.5, 1500.75, 2500.25, 3500.5 }),
            "Synchronization preserves fractional note timestamps");
        CheckGrid(merged);
        target.Document = merged;
        Check(Synchronize(project, target.Id, references).ContentEquals(merged), "Matching boundary timing is idempotent");
        var history = new EditorHistory(original); history.RestoreVersion("sync", merged);
        history.Undo(); Check(history.Document.ContentEquals(original), "Boundary timing undo");
        history.Redo(); CheckGrid(history.Document);
        var reopened = ProjectSerializer.Read(ProjectSerializer.Serialize(merged));
        WorkspaceTimingSynchronization.NormalizeDocument(reopened);
        Check(reopened.TimingPoints.Count == 1 && reopened.TimingPoints[0].TimeMs == .75, "Reopening retains only the original red point");
        CheckGrid(reopened);
        CheckGrid(OsuBeatmapWriter.Serialize(merged).ReadBack);

        source.Document.TimingPoints[0].SampleSet = 2;
        source.Document.TimingPoints[0].SampleIndex = 3;
        source.Document.TimingPoints[0].Volume = 65;
        source.Document.TimingPoints[0].Effects = 1;
        merged = Synchronize(project, target.Id, references);
        Check(merged.TimingPoints.Count(p => p.Uninherited) == 1, "Sample-only boundaries do not reset the beat grid");
        Check(merged.TimingPoints.Where(p => !p.Uninherited).Select(p => p.TimeMs).SequenceEqual(new[] { 1006d, 3000d }),
            "Sample changes add only the section sample transitions");
        Sounds(merged, [(500.5,1,80),(1500.75,2,65),(2500.25,2,65),(3500.5,1,80)]);
        CheckGrid(merged);
        reopened = ProjectSerializer.Read(ProjectSerializer.Serialize(merged));
        WorkspaceTimingSynchronization.NormalizeDocument(reopened);
        Check(reopened.TimingPoints.Count(p => p.Uninherited) == 1 && reopened.TimingPoints.Count == 3,
            "Required sample boundaries remain green after reopening");
        CheckGrid(reopened);
        target.Document = reopened;
        Check(Synchronize(project, target.Id, references).ContentEquals(reopened), "Sample boundary timing is idempotent after reopening");

        void CheckGrid(MapDocument document)
        {
            foreach (double time in new[] { 900d, 1006, 1500, 2000, 2500, 3000, 3500 })
                Near(TimingMap.Snap(document, time, 4), TimingMap.Snap(original, time, 4), "Section boundaries preserve the fractional beat grid");
        }
    }

    private static void ObjectVelocityAndSectionSamples()
    {
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(
            "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n" +
            "0,500,4,2,0,70,1,0\n250,-50,4,2,0,70,0,0\n700,300,3,3,0,60,1,1\n1600,-20,3,3,0,60,0,1\n[HitObjects]\n" +
            "100,192,500,2,0,L|380:192,2,280\n180,192,1750,1,0,0:0:0:0:\n") };
        var target = Difficulty("target", 600, 1, 40, 50, 2100);
        target.Document.TimingPoints.Add(new() { TimeMs = 1000.25, BeatLengthMs = 400, Meter = 7, SampleSet = 1, Volume = 40 });
        var original = target.Document.DeepClone();
        var project = new BeatmapProject { Name = "object velocity", Difficulties = [source, target] };
        CollaborationReference[] references = [new(Guid.NewGuid(), source.Id, 100, 2000)];
        var merged = Synchronize(project, target.Id, references);
        Check(merged.TimingPoints.Where(p => p.Uninherited).Select(p => (p.TimeMs, p.BeatLengthMs, p.Meter))
            .SequenceEqual(original.TimingPoints.Select(p => (p.TimeMs, p.BeatLengthMs, p.Meter))),
            "Source BPM and meter changes leave target red timing unchanged");
        Check(!merged.TimingPoints.Any(p => p.TimeMs == 250 || p.TimeMs == 1600), "Source SV-only lines are not copied");
        Check(merged.TimingPoints.Where(p => !p.Uninherited).All(p => p.BeatLengthMs == -100), "Section sample lines carry neutral SV");
        var copied = merged.ImportedSliders.Single();
        double expectedDuration = ImportedSliderConverter.DurationMs(source.Document, source.Document.ImportedSliders.Single());
        double actualDuration = ImportedSliderConverter.DurationMs(merged, copied);
        Check(Math.Abs(actualDuration - expectedDuration) < .001,
            $"Per-object SV retains repeated Legacy slider duration across BPM and base-SV differences: {actualDuration:R} vs {expectedDuration:R}");
        var output = OsuBeatmapWriter.Serialize(merged);
        Check(output.ObjectSequenceMatches, "Retargeted slider exports the same playable sequence");
        var resolver = new HitsoundResolver(output.ReadBack, CatchStreamConverter.Convert(output.ReadBack).Objects);
        foreach (var item in CatchStreamConverter.Convert(output.ReadBack).Objects.Where(o => o.TimeMs >= 100 && o.TimeMs < 2000 && o.Kind != CatchObjectKind.TinyDroplet))
        {
            var expected = item.TimeMs + 5 < 700 ? new HitSampleSettings(2, 2, 0, 70) : new HitSampleSettings(3, 3, 0, 60);
            Check(resolver.Describe(item) == expected, "Section samples follow source timing across the slider and BPM change");
        }
        Sounds(merged, [(50,1,40),(1750,3,60),(2100,1,40)]);
        target.Document = merged;
        Check(Synchronize(project, target.Id, references).ContentEquals(merged), "Section samples and independent object SV remain stable on repeat sync");
        target.Document.TimingPoints[0].BeatLengthMs = 6000;
        var beforeRejection = target.Document.DeepClone();
        Throws(() => Synchronize(project, target.Id, references), "Unrepresentable object SV");
        Check(target.Document.ContentEquals(beforeRejection), "Rejected SV retargeting leaves the complete target unchanged");
    }

    private static void PreserveTargetSounds()
    {
        string root = Path.GetFullPath("artifacts/tests/collaboration/keep-target-sounds-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source"), targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(targetRoot);
        File.WriteAllBytes(Path.Combine(sourceRoot, "source.wav"), [1,2,3]);
        File.WriteAllBytes(Path.Combine(targetRoot, "target.wav"), [4,5,6]);
        string header = "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:2\n[TimingPoints]\n";
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(header +
            "0,500,4,3,0,70,1,0\n[HitObjects]\n400,192,500,1,8,3:3:0:90:source.wav\n180,192,750,1,4,3:3:0:90:source.wav\n" +
            "100,192,1000,2,8,L|240:192,2,140,8|8|8,3:3|3:3|3:3,0:0:0:0:\n", Path.Combine(sourceRoot, "source.osu")) };
        var target = new ProjectDifficulty { Name = "target", Document = OsuBeatmapReader.Read(header +
            "0,500,4,1,0,50,1,0\n[HitObjects]\n100,192,500,1,2,1:1:0:35:\n400,192,500,1,4,2:2:0:65:target.wav\n" +
            "100,192,1000,2,2,L|240:192,2,140,2|4|8,2:2|2:2|2:2,0:0:0:0:\n180,192,2500,1,8,2:2:0:45:\n",
            Path.Combine(targetRoot, "target.osu")) };
        var project = new BeatmapProject { Name = "keep sounds", Difficulties = [source,target] };
        CollaborationReference[] refs = [new(Guid.NewGuid(), source.Id, 0, 2000)];
        var before = target.Document.DeepClone();
        var prepared = CollaborationMapping.Synchronize(project, target.Id, refs, synchronizeObjectHitsounds: false);
        Check(target.Document.ContentEquals(before), "Keeping target sounds does not mutate its input");
        Check(prepared.Document.Fruits.All(f => f.OriginalLine?.Contains("source.wav") != true)
            && prepared.Document.ImportedSliders.All(s => s.OriginalLine?.Split(',')[4] == "0"), "Object-only copies clear source sample fields and edge sounds");
        prepared.Resources.Apply();
        Check(Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories).Count() == 1, "Object-only sync copies no source samples");
        var output = OsuBeatmapWriter.Serialize(prepared.Document); var resolver = new HitsoundResolver(output.ReadBack, CatchStreamConverter.Convert(output.ReadBack).Objects);
        var events = CatchStreamConverter.Convert(output.ReadBack).Objects;
        var kept = resolver.Describe(events.Single(o => o.TimeMs == 500));
        Check(kept is { Additions:4, NormalSet:2, Volume:65, FileName:"target.wav" }, "Same-time target sound wins independently of ID, with nearest X resolving ties");
        var added = resolver.Describe(events.Single(o => o.TimeMs == 750));
        Check(added is { Additions:0, NormalSet:3, Volume:70, FileName:"" }, "Unmatched new object uses merged timing defaults without source additions or file");
        var oldOutput = OsuBeatmapWriter.Serialize(before); var oldResolver = new HitsoundResolver(before, oldOutput.PlayableObjects);
        foreach (var item in output.PlayableObjects.Where(o => o.TimeMs >= 1000 && o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet))
        {
            var old = oldOutput.PlayableObjects.Single(o => o.TimeMs == item.TimeMs && o.Kind == item.Kind);
            var newResolver = new HitsoundResolver(prepared.Document, output.PlayableObjects);
            Check(newResolver.Describe(item) == oldResolver.Describe(old), "Slider edge/tick and outside target sounds are retained");
        }
        target.Document = prepared.Document;
        Check(CollaborationMapping.Synchronize(project, target.Id, refs, synchronizeObjectHitsounds: false).Document.ContentEquals(prepared.Document),
            "Keeping target sounds is idempotent");
        var targetEvent = output.PlayableObjects.Single(o => o.TimeMs == 500);
        int index = target.Document.HitsoundOverrides.FindIndex(o => o.SourceId == targetEvent.SourceId && o.EventIndex == targetEvent.EventIndex);
        target.Document.HitsoundOverrides[index] = target.Document.HitsoundOverrides[index] with { Sample = new(1,1,0,27,2,"target.wav") };
        var updated = CollaborationMapping.Synchronize(project, target.Id, refs, synchronizeObjectHitsounds: false).Document;
        var updatedOutput = OsuBeatmapWriter.Serialize(updated);
        Check(new HitsoundResolver(updated, updatedOutput.PlayableObjects).Describe(updatedOutput.PlayableObjects.Single(o => o.TimeMs == 500)).Volume == 27,
            "Later target hitsound edits survive the next object-only sync");
        target.Document = updated; source.Document.TimingPoints[0].Volume = 80;
        source.Document.Fruits.Add(new() { TimeMs = 850, X = 180 });
        updated = CollaborationMapping.Synchronize(project, target.Id, refs, synchronizeObjectHitsounds: false).Document;
        updatedOutput = OsuBeatmapWriter.Serialize(updated); var changedResolver = new HitsoundResolver(updated, updatedOutput.PlayableObjects);
        Check(changedResolver.Describe(updatedOutput.PlayableObjects.Single(o => o.TimeMs == 750)).Volume == 70
            && changedResolver.Describe(updatedOutput.PlayableObjects.Single(o => o.TimeMs == 850)).Volume == 80,
            "Existing target sample timing is preserved while unmatched additions use the current default");
    }

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
        Near(TimingMap.At(prepared.Document, 800).BeatLengthMs, 600, "Target BPM is retained after exclusions");
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
        Near(TimingMap.At(merged, 1200).BeatLengthMs, 600, "Unassigned section retains target BPM");
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
            var sample = resolver.Describe(events.Single(o => o.TimeMs == Math.Truncate(item.Time)));
            Check(sample.NormalSet == item.Bank && sample.Volume == item.Volume, $"Exported sound at {item.Time}");
        }
    }
    private static void Throws(Action action, string message)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception(message + " should fail"); }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-6, message);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
