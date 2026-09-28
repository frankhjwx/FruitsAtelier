using FruitsAtelier.Core;

internal static class SynchronizationTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Sync: older accepted baselines recover reviewable retained objects", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].TimeMs = 800;
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "320,192,1800"));
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true);
            f.Session.Manifest.Difficulties[0].Sync!.RetainedObjectsRecorded = false;
            var review = f.Merge();
            Check(!review.RequiresResolution && review.PreviouslyResolved.Count == 1, "older accepted difference is already resolved and reviewable");
            var replaced = WorkspaceSynchronization.Resolve(review, review.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(replaced.Fruits.Count == 3 && replaced.Fruits.Any(o => o.TimeMs == 1800) && replaced.Fruits.All(o => o.TimeMs != 800), "older decision can be changed without duplicate objects");
        }));
        yield return ("Sync: retained fields can be reviewed again without becoming new conflicts", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "FA title");
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External title"));
            var first = f.Merge(); var decisions = first.Conflicts.ToDictionary(c => c.Key, _ => false);
            var kept = WorkspaceSynchronization.Resolve(first, decisions);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], first.External, kept, true, review: first, choices: decisions);
            f.Diff.Document = kept;
            var review = f.Merge();
            Check(!review.RequiresResolution && review.PreviouslyResolved.Contains("Metadata/Title"), "resolved field remains reviewable");
            Check(OsuBeatmapReader.Setting(WorkspaceSynchronization.Resolve(review, new Dictionary<string, bool>()), "Metadata", "Title") == "FA title", "automatic updates retain the choice");
            Check(OsuBeatmapReader.Setting(WorkspaceSynchronization.Resolve(review, new Dictionary<string, bool> { ["Metadata/Title"] = true }), "Metadata", "Title") == "External title", "field can be resolved again");
        }));
        yield return ("Sync: retained FA objects track repeated external time moves across anchors after restart", () => Run(f =>
        {
            Guid id = f.Diff.Document.Fruits[0].Id;
            f.Diff.Document.Fruits[0].TimeMs = 800;
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "300,192,1800"));
            var first = f.Merge(); var decisions = first.Conflicts.ToDictionary(c => c.Key, _ => false);
            var kept = WorkspaceSynchronization.Resolve(first, decisions);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], first.External, kept, true, review: first, choices: decisions);
            f.Diff.Document = kept; WorkspaceProject.Save(f.Session, f.Session.Project);
            f.Session = WorkspaceProject.Open(f.Session.Directory);
            var history = f.Merge();
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Current && !history.RequiresResolution && history.PreviouslyResolved.Count == 1,
                "resolved difference remains reviewable without requiring another decision");
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "320,192,1900"));
            var second = f.Merge(); var conflict = second.Conflicts.Single(c => c.Key.StartsWith("$objects:"));
            Check(second.WasPreviouslyRetained(conflict.Key) && second.ConflictSources(conflict.Key, false).SetEquals(new[] { id }), "retained identity survives time changes");
            var accepted = WorkspaceSynchronization.Resolve(second, second.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(accepted.Fruits.Count == 3 && accepted.Fruits.All(o => o.Id != id) && accepted.Fruits.Any(o => o.TimeMs == 1900), "external replaces the retained local object instead of duplicating it");
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], second.External, accepted, true, review: second, choices: second.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(f.Session.Manifest.Difficulties[0].Sync!.RetainedObjects.Count == 0, "external choice clears the previous local decision");
        }));
        yield return ("Sync: ignored external additions conflict again without claiming unrelated FA objects", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture() + "\n320,192,2300,1,0,0:0:0:0:\n");
            var first = f.Merge(); var decisions = first.Conflicts.ToDictionary(c => c.Key, _ => false);
            var kept = WorkspaceSynchronization.Resolve(first, decisions);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], first.External, kept, true, review: first, choices: decisions);
            f.Diff.Document = kept;
            File.WriteAllText(f.Source, Fixture() + "\n340,192,2400,1,0,0:0:0:0:\n");
            var second = f.Merge(); var conflict = second.Conflicts.Single(c => c.Key.StartsWith("$objects:"));
            Check(second.WasPreviouslyRetained(conflict.Key) && second.ConflictSources(conflict.Key, false).Count == 0, "previously rejected insertion is explicit");
            var accepted = WorkspaceSynchronization.Resolve(second, second.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(accepted.Fruits.Count == 4 && kept.Fruits.All(o => accepted.Fruits.Any(a => a.Id == o.Id)), "unrelated FA objects survive accepting the new addition");
        }));
        yield return ("Sync: baseline-free deletions expose an absent side and retain unrelated identities", () => Run(f =>
        {
            string text = string.Join('\n', OsuBeatmapWriter.Serialize(f.Diff.Document).Text.Split('\n').Where(line => !line.StartsWith("100,192,1000,")));
            File.WriteAllText(f.Source, text);
            var merge = WorkspaceSynchronization.CompareWithoutBaseline(f.Diff.Document, WorkspaceSynchronization.ReadStable(f.Source), f.Session.Directory, true);
            var conflict = merge.Conflicts.Single(c => c.Key.StartsWith("$objects:"));
            Check(merge.ConflictSources(conflict.Key, false).Contains(f.Diff.Document.Fruits[0].Id) && merge.ConflictSources(conflict.Key, true).Count == 0, "absent counterpart");
            var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Fruits.Count == 2 && result.Fruits.Any(o => o.Id == f.Diff.Document.Fruits[1].Id), "deletion applies independently");
        }));
        yield return ("Sync: no baseline supports independent field and ordered object choices", () => Run(f =>
        {
            string text = OsuBeatmapWriter.Serialize(f.Diff.Document).Text.Replace("Title:Title", "Title:External")
                .Replace("100,192,1000", "300,192,1000").Replace("150,192,1500", "350,192,1500");
            File.WriteAllText(f.Source, text);
            var merge = WorkspaceSynchronization.CompareWithoutBaseline(f.Diff.Document, WorkspaceSynchronization.ReadStable(f.Source), f.Session.Directory, true);
            Check(merge.Conflicts.Count == 3, "one field and two ordered objects");
            Reject(() => WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>()));
            var choices = merge.Conflicts.ToDictionary(c => c.Key, c => c.Key == "Metadata/Title" || c.Key == "$objects:1");
            var result = WorkspaceSynchronization.Resolve(merge, choices);
            Check(OsuBeatmapReader.Setting(result, "Metadata", "Title") == "External", "external field chosen");
            Check(result.Fruits.Any(o => o.Id == f.Diff.Document.Fruits[0].Id && o.X == 100)
                && result.Fruits.Any(o => o.TimeMs == 1500 && o.X == 350), "mixed object choices");
        }));
        yield return ("Sync: deleting a stale association requires resolving an external rename first", () => Run(f =>
        {
            string renamed = Path.Combine(f.Set, "renamed.osu"); File.Move(f.Source, renamed);
            Reject(() => WorkspaceAssociations.DeleteDifficulty(f.Session, f.Session.Project, f.Diff.Id));
            Check(File.Exists(renamed) && WorkspaceProject.Open(f.Session.Directory).Project.Difficulties.Count == 1, "neither side removed from stale association");
        }));
        yield return ("Sync: choosing the complete FA version retains unchanged local fields against future external edits", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External"));
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true, retainLocalFields: true);
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External again"));
            Check(f.Merge().Conflicts.Any(c => c.Key == "Metadata/Title"), "explicit full FA choice stays pending");
        }));
        yield return ("Sync: source changes between reading and baseline capture cannot become the baseline", () => Run(f =>
        {
            var imported = OsuBeatmapReader.Read(File.ReadAllText(f.Source), f.Source);
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:Changed during import"));
            bool rejected = false;
            try { WorkspaceSynchronization.Capture(f.Source, imported, f.Session.Directory); }
            catch (IOException) { rejected = true; }
            Check(rejected, "concurrent source edit rejected");
        }));
        yield return ("Sync: tied-time external insertion preserves output order", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000,1,0,0:0:0:0:", "300,192,1000,1,0,0:0:0:0:\n100,192,1000,1,0,0:0:0:0:"));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            var output = OsuBeatmapWriter.Serialize(result);
            Check(WorkspaceSynchronization.ObjectLines(output.Text).Take(2).Select(l => l.Split(',')[0]).SequenceEqual(new[] { "300", "100" }), "same-time ordering");
        }));
        yield return ("Sync: independent local additions survive external object deletion", () => Run(f =>
        {
            var added = new Fruit { TimeMs = 3000, X = 321 }; f.Diff.Document.Fruits.Add(added);
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000,1,0,0:0:0:0:\n", ""));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Fruits.Count == 3 && result.Fruits.Any(o => o.Id == added.Id), "local addition retained");
        }));
        yield return ("Sync: interrupted deletion rolls back the external removal", () => Run(f =>
        {
            string backup = WorkspaceSynchronization.Archive(f.Session, "delete-test");
            File.Copy(f.Source, Path.Combine(backup, "external.osu"));
            string journal = System.Text.Json.JsonSerializer.Serialize(new { DifficultyId = f.Diff.Id, Path = f.Source, Backup = backup, Hash = WorkspaceProject.Hash(f.Source) });
            File.WriteAllText(Path.Combine(f.Session.Directory, "delete.json"), journal); File.Delete(f.Source);
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            Check(File.Exists(f.Source) && reopened.Project.Difficulties[0].Id == f.Diff.Id, "both sides restored");
        }));
        yield return ("Sync: two legacy difficulties in one project require one retained owner", () => Run(f =>
        {
            var copy = new ProjectDifficulty { Name = f.Diff.Name, Document = f.Diff.Document.DeepClone() };
            f.Session.Project.Difficulties.Add(copy);
            var entry = f.Session.Manifest.Difficulties[0];
            f.Session.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = copy.Id, Name = copy.Name, Source = f.Source, SourceHash = entry.SourceHash, Sync = entry.Sync });
            WorkspaceProject.Save(f.Session, f.Session.Project);
            Check(f.Scan().Difficulties.All(s => s.State == WorkspaceSyncState.Duplicate), "all duplicate owners blocked");
            var keep = WorkspaceAssociations.Claims(f.Workspace).Single(c => c.DifficultyId == copy.Id);
            var result = WorkspaceAssociations.KeepOnly(f.Session, f.Session.Project, f.Source, keep);
            Check(result.Project.Difficulties.Single().Id == copy.Id && File.Exists(f.Source), "selected owner only");
        }));
        yield return ("Sync: metadata rename and audio replacement can happen together", () => Run(f =>
        {
            string folder = Path.Combine(f.Songs, "new set"); Directory.Move(f.Set, folder);
            string path = Path.Combine(folder, "new.osu"); File.Move(Path.Combine(folder, "map.osu"), path);
            File.Delete(Path.Combine(folder, "audio.mp3")); File.WriteAllText(Path.Combine(folder, "new.mp3"), "different recording");
            File.WriteAllText(path, Fixture().Replace("audio.mp3", "new.mp3").Replace("Title:Title", "Title:New").Replace("Artist:Artist", "Artist:New").Replace("Creator:Mapper", "Creator:New"));
            var status = f.Scan().Difficulties.Single(); var result = f.Resolve(status.Candidate!);
            Check(result.AudioPath == Path.Combine(folder, "new.mp3") && result.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, "joint rename and replacement");
        }));
        yield return ("Sync: timestamp and size equality cannot hide a metadata change", () => Run(f =>
        {
            var stamp = File.GetLastWriteTimeUtc(f.Source);
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:Other")); File.SetLastWriteTimeUtc(f.Source, stamp);
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Changed, "content check");
        }));
        yield return ("Sync: a truncated live source is unavailable rather than deleted", () => Run(f =>
        {
            File.WriteAllText(f.Source, "osu file format v14\n");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Unavailable, "incomplete save must not relink or delete");
        }));
        yield return ("Sync: missing referenced audio requires explicit repair", () => Run(f =>
        {
            File.Delete(Path.Combine(f.Set, "audio.mp3"));
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.AudioMissing, "audio missing");
        }));
        yield return ("Sync: matching metadata changes on both sides need no choice", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "Same"); File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:Same"));
            Check(f.Merge().Conflicts.Count == 0, "same value");
        }));
        yield return ("Sync: accepted local metadata stays pending through later external edits", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "Local"); File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External"));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => false));
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, result, true); f.Diff.Document = result;
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External again"));
            Check(f.Merge().Conflicts.Any(c => c.Key == "Metadata/Title"), "local choice cannot be silently overwritten later");
        }));
        yield return ("Sync: external timing and difficulty settings update while retaining object IDs", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("0,500,4", "0,400,3").Replace("CircleSize:5", "CircleSize:6"));
            var result = f.Resolve(WorkspaceSynchronization.ReadStable(f.Source));
            Check(result.TimingPoints[0].BeatLengthMs == 400 && result.TimingPoints[0].Meter == 3 && result.CircleSize == 6, "settings applied");
            Check(result.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, "object retained");
        }));
        yield return ("Sync: exported curves keep anchors and handles after metadata updates", () => Run(f =>
        {
            var track = new CurveTrack { Kind = CurveKind.Bezier };
            track.Nodes.AddRange([new() { TimeMs = 3000, X = 100, HandleOut = new(100, 20) }, new() { TimeMs = 4000, X = 250, HandleIn = new(-100, -20) }]);
            f.Diff.Document.Tracks.Add(track);
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true); WorkspaceExport.Commit(f.Session, plan);
            File.WriteAllText(f.Source, File.ReadAllText(f.Source).Replace("Creator:Mapper", "Creator:Uploaded mapper"));
            var result = f.Resolve(WorkspaceSynchronization.ReadStable(f.Source));
            Check(result.Tracks.Single().Id == track.Id && result.Tracks[0].Nodes[0].Id == track.Nodes[0].Id
                && result.Tracks[0].Nodes[0].HandleOut == track.Nodes[0].HandleOut, "curve authoring retained");
        }));
        yield return ("Sync: changing one stream output resolves its complete authoring group", () => Run(f =>
        {
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4 };
            track.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 4000, X = 250 }]); f.Diff.Document.Tracks.Add(track);
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true); WorkspaceExport.Commit(f.Session, plan);
            string[] lines = WorkspaceSynchronization.ObjectLines(plan.Output.Text); int index = plan.Output.ObjectSources.ToList().FindIndex(id => id == track.Id);
            string changed = "350," + lines[index][(lines[index].IndexOf(',') + 1)..];
            File.WriteAllText(f.Source, plan.Output.Text.Replace(lines[index], changed));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Tracks.Count == 0 && result.Fruits.Count == lines.Length, "all stream members retained when accepting external structure");
        }));
        yield return ("Sync: interrupted export restores pending authoring without overwriting newer osu content", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 430;
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true);
            WorkspaceExportRecovery.Prepare(f.Session, f.Session.Project, plan, f.Diff.Id);
            File.WriteAllText(f.Source, Fixture().Replace("100,192", "321,192"));
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            Check(reopened.Project.Difficulties[0].Document.Fruits[0].X == 430, "pending authoring recovered");
            Check(OsuBeatmapReader.ReadFile(f.Source).Fruits[0].X == 321, "newer external state not overwritten");
            Check(WorkspaceSynchronization.Scan(reopened, f.Songs).Difficulties.Single().State == WorkspaceSyncState.Changed, "external difference still detected");
        }));
        yield return ("Sync: legacy missing baseline requires explicit version choice", () => Run(f =>
        {
            f.Session.Manifest.Difficulties[0].Sync = null;
            File.WriteAllText(f.Source, Fixture().Replace("100,192", "321,192"));
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.NeedsBaseline, "unknown history must not auto merge");
        }));
        yield return ("Sync: numeric spelling and newline changes are not object conflicts", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "100.0,192.0,1000.0").Replace("\n", "\r\n"));
            Check(f.Merge().Conflicts.Count == 0, "semantic normalization");
        }));
        yield return ("Sync: duplicate project resolution preserves unique difficulties", () => Run(f =>
        {
            f.CopyProject(); var copy = WorkspaceProject.Open(Path.Combine(f.Workspace, "copied-project"));
            string unique = Path.Combine(f.Set, "unique.osu"); File.WriteAllText(unique, Fixture().Replace("100,192", "333,192"));
            var diff = BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(unique)]).Difficulties.Single(); copy.Project.Difficulties.Add(diff);
            WorkspaceProject.Save(copy, copy.Project);
            var keep = WorkspaceAssociations.Claims(f.Workspace).First(c => c.Project == f.Session.Directory);
            var winner = WorkspaceAssociations.KeepOnly(f.Session, f.Session.Project, f.Source, keep);
            Check(winner.Project.Difficulties.Count == 2 && winner.Project.Difficulties.Any(d => d.Id == diff.Id), "unique difficulty moved");
            Check(File.Exists(unique) && File.Exists(f.Source) && WorkspaceAssociations.Claims(f.Workspace).Select(c => c.Project).Distinct().Count() == 1, "one project, both external files");
        }));
        yield return ("Sync: selecting FA audio can recover overwritten bytes", () => Run(f =>
        {
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "external replacement");
            string path = WorkspaceSynchronization.LocalAudioVersion(f.Session.Manifest.Difficulties[0], f.Diff.Document, f.Session.Directory)!;
            Check(File.ReadAllText(path) == "original audio" && Path.GetExtension(path) == ".mp3", "original playable audio restored");
        }));
        yield return ("Sync: retained FA audio remains its own version after restart and another replacement", () => Run(f =>
        {
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "external replacement");
            var entry = f.Session.Manifest.Difficulties[0];
            f.Diff.Document.AudioPath = WorkspaceSynchronization.LocalAudioVersion(entry, f.Diff.Document, f.Session.Directory);
            WorkspaceSynchronization.Accept(f.Session, entry, WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true);
            WorkspaceProject.Save(f.Session, f.Session.Project);
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            var local = reopened.Project.Difficulties[0].Document;
            File.WriteAllText(local.AudioPath!, "overwritten retained audio");
            string recovered = WorkspaceSynchronization.LocalAudioVersion(reopened.Manifest.Difficulties[0], local, reopened.Directory)!;
            Check(File.ReadAllText(recovered) == "original audio", "authoring hash is independent of external baseline");
        }));
        yield return ("Sync: read-only modes appear in the library without entering Catch import", () => Run(f =>
        {
            string other = Path.Combine(f.Set, "standard.osu"); File.WriteAllText(other, Fixture().Replace("Mode:2", "Mode:0"));
            var db = new LibraryDatabase(f.Workspace, f.Songs); db.Scan();
            Check(db.Search("").Any(m => m.Path == other && m.Mode == 0), "read-only indexed");
            Check(f.Scan().Additions.Count == 0, "not imported as Catch");
        }));
        yield return ("Sync: metadata, file and folder rename retain authoring identities", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 401;
            string moved = Path.Combine(f.Songs, "renamed"); Directory.Move(f.Set, moved);
            string path = Path.Combine(moved, "new name.osu"); File.Move(Path.Combine(moved, "map.osu"), path);
            File.WriteAllText(path, Fixture().Replace("Title:Title", "Title:New title").Replace("Artist:Artist", "Artist:New artist").Replace("Creator:Mapper", "Creator:New mapper"));
            var status = f.Scan().Difficulties.Single(); Check(status.State == WorkspaceSyncState.Changed && status.Candidate!.Path == path, "rename association");
            var merged = f.Resolve(status.Candidate!);
            Check(merged.Fruits[0].Id == f.Diff.Document.Fruits[0].Id && merged.Fruits[0].X == 401, "pending FA objects retained");
            Check(OsuBeatmapReader.Setting(merged, "Metadata", "Title") == "New title", "metadata copied");
            Check(merged.AudioPath == Path.Combine(moved, "audio.mp3"), "audio relocated");
            Check(WorkspaceAssociations.FindProject(f.Workspace, path) == f.Session.Directory, "reopening rename finds old project");
        }));
        yield return ("Sync: uploaded IDs update without replacing objects", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("BeatmapID:0", "BeatmapID:12345").Replace("BeatmapSetID:-1", "BeatmapSetID:789"));
            var resolved = f.Resolve(WorkspaceSynchronization.ReadStable(f.Source));
            Check(resolved.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, "identity");
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "BeatmapID") == "12345", "ID");
        }));
        yield return ("Sync: disjoint metadata edits merge and same-field edits require choice", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "FA title"); Set(f.Diff.Document, "Tags", "local tags");
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:osu title").Replace("Creator:Mapper", "Creator:External mapper"));
            var merge = f.Merge(); Check(merge.Conflicts.Single().Key == "Metadata/Title", "precise conflict");
            Reject(() => WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>()));
            var resolved = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool> { ["Metadata/Title"] = false });
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "Title") == "FA title", "local choice");
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "Creator") == "External mapper", "external disjoint");
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "Tags") == "local tags", "local disjoint");
        }));
        yield return ("Sync: external deletion retains authoring and new files are visible", () => Run(f =>
        {
            File.Delete(f.Source);
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Missing, "missing state");
            Check(WorkspaceProject.Open(f.Session.Directory).Project.Difficulties.Count == 1, "authoring retained");
            File.WriteAllText(Path.Combine(f.Set, "new.osu"), Fixture().Replace("100,192", "220,192"));
            Check(f.Scan().Additions.Count == 1, "new difficulty");
        }));
        yield return ("Sync: unavailable Songs is not a deletion", () => Run(f =>
        {
            Directory.Move(f.Songs, f.Songs + "-offline");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Unavailable, "offline state");
        }));
        yield return ("Sync: identical relocation candidates require explicit association", () => Run(f =>
        {
            File.Delete(f.Source);
            File.WriteAllText(Path.Combine(f.Set, "copy1.osu"), Fixture()); File.WriteAllText(Path.Combine(f.Set, "copy2.osu"), Fixture());
            var scan = f.Scan(); Check(scan.Difficulties.Single().State == WorkspaceSyncState.Ambiguous && scan.Additions.Count == 0, "ambiguity not additions");
        }));
        yield return ("Sync: a live association is not stolen by an identical copy", () => Run(f =>
        {
            File.WriteAllText(Path.Combine(f.Set, "copy.osu"), Fixture());
            var scan = f.Scan(); Check(scan.Difficulties.Single().Candidate!.Path == f.Source && scan.Additions.Count == 1, "copy stays separate");
        }));
        yield return ("Sync: renamed and replaced audio synchronizes with metadata", () => Run(f =>
        {
            File.Delete(Path.Combine(f.Set, "audio.mp3")); File.WriteAllText(Path.Combine(f.Set, "new.mp3"), "replacement");
            File.WriteAllText(f.Source, Fixture().Replace("audio.mp3", "new.mp3"));
            var merge = f.Merge(); Check(merge.Conflicts.Count == 0, "missing old audio is not a local edit");
            var result = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>());
            Check(result.AudioPath == Path.Combine(f.Set, "new.mp3"), "new audio");
            Check(result.Fruits[0].TimeMs == 1000, "no implicit retiming");
        }));
        yield return ("Sync: same-name audio replacement is detected and prior bytes retained", () => Run(f =>
        {
            string hash = f.Session.Manifest.Difficulties[0].Sync!.AudioHash!;
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "replaced audio");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Changed, "audio changed");
            Check(File.ReadAllText(Path.Combine(f.Workspace, ".sync-history", "resources", hash)) == "original audio", "old audio saved");
        }));
        yield return ("Sync: independently replaced audio requires resource choice", () => Run(f =>
        {
            string local = Path.Combine(f.Set, "local.mp3"); File.WriteAllText(local, "local audio"); f.Diff.Document.AudioPath = local;
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "external audio");
            var merge = f.Merge(); Check(merge.Conflicts.Any(c => c.Key == "$audio"), "audio conflict");
            var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => false));
            Check(result.AudioPath == local, "local audio retained");
        }));
        yield return ("Sync: changes to separate objects can be resolved independently", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 300;
            File.WriteAllText(f.Source, Fixture().Replace("100,192", "250,192").Replace("200,192", "400,192"));
            var merge = f.Merge(); Check(merge.Conflicts.Count >= 1, "object conflict");
            var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Fruits.Any(o => o.X == 250) && result.Fruits.Any(o => o.X == 400), "external objects selected");
        }));
        yield return ("Sync: plain local save does not advance synchronization baseline", () => Run(f =>
        {
            string text = f.Session.Manifest.Difficulties[0].Sync!.Text;
            f.Diff.Document.Fruits[0].X = 400; WorkspaceProject.Save(f.Session, f.Session.Project);
            Check(f.Session.Manifest.Difficulties[0].Sync!.Text == text, "baseline unchanged");
        }));
        yield return ("Sync: export records actual emitted objects and authoring mapping", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 350;
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true); WorkspaceExport.Commit(f.Session, plan);
            var sync = f.Session.Manifest.Difficulties[0].Sync!;
            Check(sync.Text == File.ReadAllText(f.Source) && sync.ObjectSources[0] == f.Diff.Document.Fruits[0].Id, "export baseline");
        }));
        yield return ("Sync: late external edits reject stale resolution", () => Run(f =>
        {
            var candidate = WorkspaceSynchronization.ReadStable(f.Source);
            File.AppendAllText(f.Source, "\n// later change");
            Reject(() => WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], candidate, f.Diff.Document, true));
        }));
        yield return ("Sync: difficulty deletion removes both files and retains recovery data", () => Run(f =>
        {
            string diff = Path.Combine(f.Session.Directory, f.Session.Manifest.Difficulties[0].File);
            WorkspaceAssociations.DeleteDifficulty(f.Session, f.Session.Project, f.Diff.Id);
            Check(!File.Exists(f.Source) && !File.Exists(diff) && !File.Exists(Path.Combine(f.Session.Directory, WorkspaceProject.ManifestName)), "both removed");
            Check(Directory.EnumerateFiles(Path.Combine(f.Workspace, ".sync-history"), "external.osu", SearchOption.AllDirectories).Any(), "external recovery");
        }));
        yield return ("Sync: duplicate ownership blocks export and ordinary deletion", () => Run(f =>
        {
            f.CopyProject();
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Duplicate, "duplicate state");
            Reject(() => WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true));
            Reject(() => WorkspaceAssociations.DeleteDifficulty(f.Session, f.Session.Project, f.Diff.Id));
            Check(File.Exists(f.Source), "shared external untouched");
        }));
        yield return ("Sync: resolving duplicate projects leaves one owner and preserves osu", () => Run(f =>
        {
            f.CopyProject(); var keep = WorkspaceAssociations.Claims(f.Workspace).First(c => c.Project == f.Session.Directory);
            WorkspaceAssociations.KeepOnly(f.Session, f.Session.Project, f.Source, keep);
            Check(WorkspaceAssociations.Claims(f.Workspace).Count == 1 && File.Exists(f.Source), "one owner, external retained");
        }));
        yield return ("Sync: baseline survives project save and restart", () => Run(f =>
        {
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            Check(reopened.Manifest.Difficulties[0].Sync!.ObjectSources.SequenceEqual(f.Session.Manifest.Difficulties[0].Sync!.ObjectSources), "mapping persists");
            Check(WorkspaceSynchronization.Scan(reopened, f.Songs).Difficulties.Single().State == WorkspaceSyncState.Current, "restart current");
        }));
    }
    private sealed class FixtureContext
    {
        public string Root = Path.GetFullPath(Path.Combine("artifacts/tests/synchronization", Guid.NewGuid().ToString("N")));
        public string Workspace => Path.Combine(Root, "Workspace");
        public string Songs => Path.Combine(Root, "Songs");
        public string Set => Path.Combine(Songs, "original");
        public string Source => Path.Combine(Set, "map.osu");
        public WorkspaceSession Session;
        public ProjectDifficulty Diff => Session.Project.Difficulties[0];
        public FixtureContext()
        {
            Directory.CreateDirectory(Set); File.WriteAllText(Source, Fixture()); File.WriteAllText(Path.Combine(Set, "audio.mp3"), "original audio");
            Session = WorkspaceProject.Create(Workspace, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(Source)]), Songs);
        }
        public WorkspaceSyncScan Scan() => WorkspaceSynchronization.Scan(Session, Songs);
        public WorkspaceMerge Merge() => WorkspaceSynchronization.Merge(Session.Manifest.Difficulties[0], Diff.Document, WorkspaceSynchronization.ReadStable(Source), Session.Directory, true);
        public MapDocument Resolve(WorkspaceSyncCandidate candidate) => WorkspaceSynchronization.Resolve(WorkspaceSynchronization.Merge(Session.Manifest.Difficulties[0], Diff.Document, candidate, Session.Directory, true), new Dictionary<string, bool>());
        public void CopyProject()
        {
            string copy = Path.Combine(Workspace, "copied-project"); Directory.CreateDirectory(copy);
            foreach (string file in Directory.EnumerateFiles(Session.Directory)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }
    }
    private static void Run(Action<FixtureContext> action) => action(new FixtureContext());
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    private static void Set(MapDocument document, string key, string value)
    {
        var section = document.OriginalSections.Single(s => s.Name == "Metadata"); section.Lines.RemoveAll(l => l.StartsWith(key + ":")); section.Lines.Add(key + ":" + value);
    }
    private static string Fixture() => """
osu file format v14
[General]
AudioFilename:audio.mp3
Mode:2
[Metadata]
Title:Title
Artist:Artist
Creator:Mapper
Version:Rain
BeatmapID:0
BeatmapSetID:-1
[Difficulty]
CircleSize:5
ApproachRate:5
SliderMultiplier:1.4
SliderTickRate:1
[TimingPoints]
0,500,4,1,0,100,1,0
[HitObjects]
100,192,1000,1,0,0:0:0:0:
150,192,1500,1,0,0:0:0:0:
200,192,2000,1,0,0:0:0:0:
""";
}
