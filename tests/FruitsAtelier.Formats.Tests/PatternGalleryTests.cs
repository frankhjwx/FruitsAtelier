using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;

internal static class PatternGalleryTests
{
    internal static void Run()
    {
        var map = new MapDocument { Name = "Source map", DurationMs = 12000, ApproachRate = 9.3, SliderTickRate = 2, SourcePath = "source.osu", AudioPath = "song.mp3" };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 3000, BeatLengthMs = 400 }]);
        map.OriginalSections.Add(new() { Name = "Metadata", Lines = { "Title:Song", "Artist:Artist", "Version:Expert", "Creator:Mapper" } });
        map.OriginalSections.Add(new() { Name = "General", Lines = { "AudioFilename:song.mp3", "Mode:2" } });
        map.OriginalSections.Add(new() { Name = "Events", Lines = { "0,0,\"background.jpg\",0,0" } });
        var before = new Fruit { TimeMs = 0, X = 128 }; map.Fruits.Add(before);
        var fruit = new Fruit { TimeMs = 1000, X = 200 }; map.Fruits.Add(fruit);
        var track = new CurveTrack { Name = "Stack", StreamSnapDivisor = 4, SpanCount = 2, Stack = new() { FruitAdjustments = [new(.5, 8)] } };
        track.Nodes.AddRange([new() { TimeMs = 1500, X = 200, HandleOut = new(100, 10) }, new() { TimeMs = 2500, X = 300 }]);
        track.StreamNewComboOffsets.Add(250);
        map.Tracks.Add(track);
        var slider = new ImportedSlider { TimeMs = 4000, X = 128, Y = 192, PathType = 'L', PixelLength = 100,
            SliderVelocityMultiplier = 1.5, ControlPoints = { new(128, 192), new(228, 192) } };
        map.ImportedSliders.Add(slider);
        var shower = new BananaShower { TimeMs = 5000, EndTimeMs = 6000, Pattern = new(1, 4, 1, 4) }; map.BananaShowers.Add(shower);
        var ids = new[] { fruit.Id, track.Id, slider.Id, shower.Id };
        var snapshot = PatternSnapshot.Capture(map, ids, "My pattern");
        snapshot = snapshot.WithTags([new("Stream", 0x58D8C1), new("Jump", 0xFFB45C)]);
        Check(snapshot.ObjectCount == 4 && snapshot.Source == new PatternSource("Artist", "Song", "Expert", "Mapper", 1000), "Captured identity and source metadata");
        var data = snapshot.CreateDocument();
        Check(data.SourcePath is null && data.AudioPath is null && data.OriginalSections.All(s => s.Name != "Events")
            && OsuBeatmapReader.Setting(data, "General", "AudioFilename") is null, "No source resource dependency");
        Check(data.Fruits.Count == 1 && data.SliderTickRate == 2 && data.TimingPoints.Count == 2, "Selection retains source timing and STR");
        fruit.X = 450; track.Stack!.FruitAdjustments[0] = new(.5, 40); slider.ControlPoints[1] = new(400, 192);
        data.Fruits[0].X = 50;
        var isolated = snapshot.CreateDocument();
        Check(isolated.Fruits[0].X == 200 && isolated.Tracks[0].Stack!.FruitAdjustments[0].Offset == 8
            && isolated.ImportedSliders[0].ControlPoints[1].X == 228, "Source and returned copies cannot mutate snapshot");
        Expect<InvalidOperationException>(() => PatternSnapshot.Capture(map, [before.Id, track.Id], "Gap"));
        Expect<InvalidOperationException>(() => PatternSnapshot.Capture(map, [track.Nodes[0].Id], "Partial"));
        Expect<InvalidOperationException>(() => PatternSnapshot.Capture(map, [], "Empty"));
        using var archive = new MemoryStream();
        PatternArchive.Write([snapshot], archive); archive.Position = 0;
        var restored = PatternArchive.Read(archive).Single();
        Check(restored.Tags.SequenceEqual(snapshot.Tags) && snapshot.WithName("Renamed").Tags.SequenceEqual(snapshot.Tags)
            && snapshot.WithDocument(snapshot.CreateDocument()).Tags.SequenceEqual(snapshot.Tags), "Tag names and colours survive sharing and editing");
        Expect<InvalidDataException>(() => snapshot.WithTags([new("tag", 1), new("TAG", 2)]));
        Expect<InvalidDataException>(() => snapshot.WithTags([new("bad", 0x1000000)]));
        Expect<InvalidDataException>(() => snapshot.WithTags(Enumerable.Range(0, 65).Select(i => new PatternTag(i.ToString(), 0))));
        using var legacy = new MemoryStream();
        archive.Position = 0;
        using (var oldZip = new ZipArchive(archive, ZipArchiveMode.Read, true))
        using (var zip = new ZipArchive(legacy, ZipArchiveMode.Create, true))
            foreach (var entry in oldZip.Entries)
            {
                using var output = zip.CreateEntry(entry.FullName).Open();
                using var input = entry.Open();
                if (entry.FullName != "manifest.json") { input.CopyTo(output); continue; }
                var manifest = JsonNode.Parse(input)!; manifest["Version"] = 1;
                foreach (var item in manifest["Patterns"]!.AsArray()) item!.AsObject().Remove("Tags");
                using var writer = new StreamWriter(output); writer.Write(manifest.ToJsonString());
            }
        legacy.Position = 0;
        Check(PatternArchive.Read(legacy).Single().Tags.Count == 0, "Version 1 galleries remain readable without tags");
        Check(restored.CreateDocument().ApproachRate == 9.3 && PatternPreview.Create(restored).ApproachRate == 9.3,
            "Saved AR survives sharing and drives preview independently of the active map");
        Check(restored.Id == snapshot.Id && restored.Name == snapshot.Name && restored.Source == snapshot.Source
            && restored.CreateDocument().ContentEquals(snapshot.CreateDocument()), "Package round trip preserves complete authoring data");
        Check(restored.CreateDocument().Tracks[0].StreamSnapDivisor == 4 && restored.CreateDocument().Tracks[0].Stack!.FruitAdjustments.Count == 1,
            "Stream subdivision and local stack adjustments survive sharing");

        using var malicious = new MemoryStream();
        using (var zip = new ZipArchive(malicious, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false))) writer.Write("{\"Version\":1,\"Patterns\":[]}");
            using var escaped = zip.CreateEntry("../outside.catchproj").Open();
        }
        malicious.Position = 0;
        Expect<InvalidDataException>(() => PatternArchive.Read(malicious));
        using var future = new MemoryStream();
        using (var zip = new ZipArchive(future, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false))) writer.Write("{\"Version\":3,\"Patterns\":[]}");
        future.Position = 0;
        Expect<InvalidDataException>(() => PatternArchive.Read(future));

        string directory = Path.Combine(Path.GetTempPath(), "fa-pattern-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PatternGalleryStore(Path.Combine(directory, "Patterns"));
            var tagStore = new PatternGalleryStore(Path.Combine(directory, "Tags"));
            tagStore.Add(snapshot);
            var taggedSecond = PatternSnapshot.Capture(map, [before.Id], "Tagged second").WithTags([new("Stream", 0x58D8C1)]);
            tagStore.Add(taggedSecond);
            tagStore.EditTag("Stream", new("Flow", 0x65A9FF));
            Check(tagStore.Load().All(p => p.Tags.Any(t => t.Name == "Flow" && t.Colour == 0x65A9FF)), "Tag rename and colour edits update all assignments");
            Expect<InvalidOperationException>(() => tagStore.EditTag("Flow", new("Jump", 1)));
            tagStore.EditTag("Flow", null);
            Check(tagStore.Load().All(p => p.Tags.All(t => t.Name != "Flow")), "Tag deletion removes all assignments");
            Check(store.Load().Count == 0 && store.Add(snapshot) && !store.Add(restored), "Persistent store deduplicates repeated import");
            var second = PatternSnapshot.Capture(map, [before.Id], "Second"); store.Add(second);
            string export = Path.Combine(directory, "selected.fap"); store.Export(export, [snapshot.Id]);
            Check(PatternArchive.ReadFile(export).Select(p => p.Id).SequenceEqual([snapshot.Id]), "Selected export contains only selected patterns");
            string all = Path.Combine(directory, "all.fap"); store.Export(all);
            store.RememberSource(snapshot.Id, Path.Combine(directory, "original.osu"));
            Check(store.SourcePath(snapshot.Id) == Path.Combine(directory, "original.osu"), "Local source hint persists");
            var imported = new PatternGalleryStore(Path.Combine(directory, "Imported"));
            Check(imported.Import(all) == 2 && imported.Import(all) == 0 && imported.Load().Count == 2, "Whole gallery import is persistent and repeatable");
            Check(imported.SourcePath(snapshot.Id) is null, "Sharing never includes local source paths");
            byte[] prior = File.ReadAllBytes(Path.Combine(directory, "Patterns", "gallery.fap"));
            string invalid = Path.Combine(directory, "invalid.fap"); File.WriteAllText(invalid, "invalid zip");
            Expect<InvalidDataException>(() => store.Import(invalid));
            Check(File.ReadAllBytes(Path.Combine(directory, "Patterns", "gallery.fap")).SequenceEqual(prior), "Invalid imports leave the gallery unchanged");
            var edited = snapshot.CreateDocument(); edited.Fruits[0].TimeMs = 1200; edited.Fruits[0].X = 333;
            store.Replace(snapshot.WithDocument(edited));
            var replacement = store.Load().Single(p => p.Id == snapshot.Id);
            Check(replacement.StartTimeMs == 1200 && replacement.Source.StartTimeMs == 1000
                && replacement.CreateDocument().Fruits[0].X == 333, "Editing preserves identity and source provenance while updating geometry");
            store.Export(all);
            Expect<InvalidDataException>(() => imported.Import(all));
            Check(imported.Load().Single(p => p.Id == snapshot.Id).CreateDocument().Fruits[0].X == 200,
                "Conflicting shared identities do not silently overwrite existing patterns");
            store.Replace(replacement.WithName("Renamed"));
            var renamed = new PatternGalleryStore(Path.Combine(directory, "Patterns")).Load().Single(p => p.Id == snapshot.Id);
            Check(renamed.Name == "Renamed" && renamed.Source == replacement.Source
                && renamed.CreateDocument().ContentEquals(replacement.CreateDocument())
                && store.SourcePath(renamed.Id) == Path.Combine(directory, "original.osu"),
                "Rename persists without changing geometry, identity or source navigation");
            store.Delete(snapshot.Id);
            Check(store.Load().Select(p => p.Id).SequenceEqual([second.Id]), "Delete persists and retains other patterns");
            Expect<InvalidOperationException>(() => store.Delete(snapshot.Id));
            store.Delete(second.Id);
            Check(store.Load().Count == 0, "Deleting the final pattern leaves a readable empty gallery");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
