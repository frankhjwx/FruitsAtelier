using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed class WorkspaceSyncBaseline
{
    public string Path { get; set; } = "";
    public string Text { get; set; } = "";
    public string Authoring { get; set; } = "";
    public List<Guid> ObjectSources { get; set; } = [];
    public string? AudioHash { get; set; }
    public string? AuthoringAudioHash { get; set; }
    public List<string> PreviousPaths { get; set; } = [];
    public List<string> LocalOverrides { get; set; } = [];
    public List<WorkspaceRetainedObjects> RetainedObjects { get; set; } = [];
    public bool RetainedObjectsRecorded { get; set; }
}

public sealed record WorkspaceRetainedObjects(List<Guid> Sources, List<string> ExternalLines);

public enum WorkspaceSyncState { Local, Current, Changed, Missing, Unavailable, Ambiguous, Duplicate, NeedsBaseline, AudioMissing }
public sealed record WorkspaceSyncCandidate(string Path, string Hash, string Text, MapDocument Document);
public sealed record WorkspaceSyncStatus(Guid DifficultyId, WorkspaceSyncState State, WorkspaceSyncCandidate? Candidate,
    IReadOnlyList<string> Candidates, string? Detail = null);
public sealed record WorkspaceSyncScan(IReadOnlyList<WorkspaceSyncStatus> Difficulties, IReadOnlyList<WorkspaceSyncCandidate> Additions);
public sealed record WorkspaceSyncConflict(string Key, string Local, string External);

public sealed class WorkspaceMerge
{
    public required MapDocument Local { get; init; }
    public required WorkspaceSyncCandidate External { get; init; }
    public required MapDocument Baseline { get; init; }
    public List<WorkspaceSyncConflict> Conflicts { get; } = [];
    internal Dictionary<string, string?> Fields { get; } = [];
    internal List<(string Key, Guid[] Sources, string[] Lines)> Objects { get; } = [];
    internal Dictionary<Guid, int> ExternalOrders { get; } = [];
    public IReadOnlyList<WorkspaceRetainedObjects> PreviouslyRetained { get; internal set; } = [];
    public HashSet<string> PreviouslyResolved { get; } = [];
    public bool RequiresResolution => Conflicts.Any(c => !PreviouslyResolved.Contains(c.Key));
    internal Dictionary<string, string[]> ChangedBeforeLines { get; } = [];
    public bool WasPreviouslyRetained(string key) => ChangedBeforeLines.TryGetValue(key, out var before)
        && PreviouslyRetained.Any(p => p.ExternalLines.Intersect(before).Any());
    public IReadOnlySet<Guid> ConflictSources(string key, bool external)
    {
        var group = Objects.FirstOrDefault(g => g.Key == key);
        if (group.Sources is null) return new HashSet<Guid>();
        if (!external) return group.Sources.ToHashSet();
        var lines = group.Lines.ToHashSet();
        var orders = WorkspaceSynchronization.ObjectLines(External.Text).Select((line, order) => (line, order))
            .Where(p => lines.Contains(p.line)).Select(p => p.order).ToHashSet();
        return WorkspaceSynchronization.SourceIds(External.Document).Where(p => orders.Contains(p.Order)).Select(p => p.Id).ToHashSet();
    }
}

public static class WorkspaceSynchronization
{
    public static bool IsMetadataField(string key) => key is "Metadata/Title" or "Metadata/TitleUnicode" or "Metadata/Artist"
        or "Metadata/ArtistUnicode" or "Metadata/Creator" or "Metadata/Version" or "Metadata/Source" or "Metadata/Tags"
        or "Metadata/BeatmapID" or "Metadata/BeatmapSetID";
    public static bool HasMetadataDifferences(MapDocument local, MapDocument external)
    {
        return local.OriginalSections.Concat(external.OriginalSections).Where(s => s.Name == "Metadata")
            .SelectMany(s => s.Lines).Select(line => line.Split(':', 2)[0].Trim()).Distinct()
            .Any(key => IsMetadataField("Metadata/" + key)
                && OsuBeatmapReader.Setting(local, "Metadata", key) != OsuBeatmapReader.Setting(external, "Metadata", key));
    }
    public static StringComparer Paths => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string? Target(WorkspaceDifficulty entry) => entry.ExportTarget ?? entry.Source;
    public static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string SnapshotPath(string directory) => System.IO.Path.Combine(directory, "sync.catchdiff");

    public static WorkspaceSyncBaseline Capture(string path, MapDocument authoring, string directory,
        string? text = null, IReadOnlyList<Guid>? sources = null, WorkspaceSyncBaseline? previous = null)
    {
        if (text is null)
        {
            text = ReadStable(path).Text;
            if (authoring.ImportedContentHash is { } imported && Digest(text) != imported)
                throw new IOException(L.Get("library.exportConflict", path));
        }
        string? authoringAudioHash = StoreAudio(authoring.AudioPath, directory);
        var baseline = new WorkspaceSyncBaseline
        {
            Path = System.IO.Path.GetFullPath(path), Text = text,
            Authoring = ProjectSerializer.Serialize(authoring, SnapshotPath(directory)),
            ObjectSources = sources?.ToList() ?? SourceIds(authoring).OrderBy(p => p.Order).Select(p => p.Id).ToList(),
            AudioHash = StoreAudio(OsuBeatmapReader.Read(text, path).AudioPath, directory),
            AuthoringAudioHash = authoringAudioHash,
            RetainedObjectsRecorded = true,
            PreviousPaths = previous?.PreviousPaths.ToList() ?? []
        };
        if (previous is not null && !Paths.Equals(previous.Path, path) && !baseline.PreviousPaths.Contains(previous.Path, Paths))
            baseline.PreviousPaths.Add(previous.Path);
        return baseline;
    }

    public static WorkspaceSyncCandidate ReadStable(string path)
    {
        WorkspaceProject.RejectLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.fileLimit"));
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (bytes.Length + count > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.fileLimit"));
            bytes.Write(buffer, 0, count);
        }
        string hash = Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8, true);
        string text = reader.ReadToEnd();
        if (WorkspaceProject.Hash(path) != hash) throw new IOException(L.Get("library.exportConflict", path));
        return new(System.IO.Path.GetFullPath(path), hash, text, OsuBeatmapReader.Read(text, path));
    }

    public static WorkspaceSyncScan Scan(WorkspaceSession session, string songs, bool searchMissing = true, CancellationToken cancellation = default)
    {
        var entries = session.Manifest.Difficulties;
        var files = new Dictionary<string, WorkspaceSyncCandidate>(Paths);
        var errors = new List<string>();
        var roots = new HashSet<string>(Paths);
        var trackedPaths = entries.Select(Target).OfType<string>().ToHashSet(Paths);
        var associatedFolders = trackedPaths.Select(p => System.IO.Path.GetDirectoryName(p)!).ToHashSet(Paths);
        var signatures = entries.Where(e => e.Sync is not null).Select(e => ObjectSignature(e.Sync!.Text)).ToHashSet();
        var onlineIds = entries.Where(e => e.Sync is not null).Select(e => OnlineIdentity(e.Sync!.Text)).OfType<string>().ToHashSet();
        bool missingAssociation = entries.Select(Target).OfType<string>().Any(path => !File.Exists(path));
        if (!string.IsNullOrWhiteSpace(songs) && missingAssociation && searchMissing) roots.Add(System.IO.Path.GetFullPath(songs));
        foreach (var path in entries.Select(Target).OfType<string>())
            if (string.IsNullOrWhiteSpace(songs) || !missingAssociation || !searchMissing || !WorkspaceProject.Within(songs, path)) roots.Add(System.IO.Path.GetDirectoryName(path)!);
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) { errors.Add(root); continue; }
                foreach (string file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Where(p => System.IO.Path.GetExtension(p).Equals(".osu", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        if (new FileInfo(file).Length > OsuBeatmapReader.MaximumFileBytes)
                        {
                            if (trackedPaths.Contains(file)) errors.Add(file);
                            continue;
                        }
                        if (LibraryDatabase.ReadMetadata(file) is not null)
                        {
                            // A missing association may require searching a very large Songs root. Retain full
                            // documents only for this set or identity candidates, not for the entire library.
                            if (!associatedFolders.Contains(System.IO.Path.GetDirectoryName(file)!))
                            {
                                string text = File.ReadAllText(file);
                                if (!signatures.Contains(ObjectSignature(text)) && (OnlineIdentity(text) is not { } identity || !onlineIds.Contains(identity))) continue;
                            }
                            files[file] = ReadStable(file);
                        }
                        else if (trackedPaths.Contains(file)) errors.Add(file);
                    }
                    catch (InvalidDataException) { if (trackedPaths.Contains(file)) errors.Add(file); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add(file); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add(root); }
        }
        var results = new List<WorkspaceSyncStatus>();
        Dictionary<string, List<WorkspaceSyncCandidate>>? byObjects = null;
        var duplicates = WorkspaceAssociations.Claims(System.IO.Path.GetDirectoryName(session.Directory)!)
            .Where(c => !Paths.Equals(c.Project, session.Directory)).ToArray();
        // A missing copy must not steal a surviving difficulty's exact association,
        // even when their objects or copied online IDs are identical.
        var occupied = trackedPaths.Concat(duplicates.Select(c => c.Target)).Where(File.Exists).ToHashSet(Paths);
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string? path = Target(entry);
            if (path is null) { results.Add(new(entry.Id, WorkspaceSyncState.Local, null, [])); continue; }
            files.TryGetValue(path, out var exact);
            if (exact is null && File.Exists(path))
            {
                results.Add(new(entry.Id, WorkspaceSyncState.Unavailable, null, [], path)); continue;
            }
            var candidates = new List<WorkspaceSyncCandidate>();
            if (exact is null && entry.Sync is { } baseline)
            {
                string objects = ObjectSignature(baseline.Text);
                byObjects ??= files.Values.GroupBy(f => ObjectSignature(f.Text)).ToDictionary(g => g.Key, g => g.ToList());
                if (byObjects.TryGetValue(objects, out var matches)) candidates.AddRange(matches.Where(f => !occupied.Contains(f.Path)));
                if (candidates.Count == 0)
                {
                    var original = OsuBeatmapReader.Read(baseline.Text, baseline.Path);
                    string? id = OsuBeatmapReader.Setting(original, "Metadata", "BeatmapID"), set = OsuBeatmapReader.Setting(original, "Metadata", "BeatmapSetID");
                    if (long.TryParse(id, out long beatmapId) && beatmapId > 0 && long.TryParse(set, out long setId) && setId > 0)
                        candidates.AddRange(files.Values.Where(f => !occupied.Contains(f.Path) && OsuBeatmapReader.Setting(f.Document, "Metadata", "BeatmapID") == id
                            && OsuBeatmapReader.Setting(f.Document, "Metadata", "BeatmapSetID") == set));
                }
            }
            if (exact is not null)
            {
                // A known live path remains the primary association; alternate copies never steal it.
                candidates.Clear(); candidates.Add(exact);
            }
            if (candidates.Count == 0)
            {
                results.Add(new(entry.Id, errors.Count > 0 ? WorkspaceSyncState.Unavailable : WorkspaceSyncState.Missing, null, [], string.Join("\n", errors)));
                continue;
            }
            if (candidates.Count > 1)
            {
                results.Add(new(entry.Id, WorkspaceSyncState.Ambiguous, null, candidates.Select(c => c.Path).ToArray())); continue;
            }
            var candidate = candidates[0];
            bool duplicate = duplicates.Any(c => Paths.Equals(c.Target, candidate.Path) || Paths.Equals(c.Target, path)
                || Paths.Equals(System.IO.Path.GetDirectoryName(c.Target), System.IO.Path.GetDirectoryName(candidate.Path)));
            var state = duplicate ? WorkspaceSyncState.Duplicate : entry.Sync is null
                ? WorkspaceSyncState.NeedsBaseline
                : !Paths.Equals(path, candidate.Path) || candidate.Hash != (entry.ExportHash ?? entry.SourceHash)
                    || AudioHash(candidate.Document.AudioPath) != entry.Sync.AudioHash ? WorkspaceSyncState.Changed : WorkspaceSyncState.Current;
            if (!duplicate && candidate.Document.AudioPath is { } audio && !File.Exists(audio)
                && (session.Project.Difficulties.FirstOrDefault(d => d.Id == entry.Id)?.Document.AudioPath is not { } localAudio || !File.Exists(localAudio)))
                state = WorkspaceSyncState.AudioMissing;
            results.Add(new(entry.Id, state, candidate, []));
        }
        foreach (var group in results.Where(r => r.Candidate is not null).GroupBy(r => r.Candidate!.Path, Paths).Where(g => g.Count() > 1).ToArray())
        {
            bool exactClaims = group.All(item => Paths.Equals(Target(entries.Single(e => e.Id == item.DifficultyId)), group.Key));
            foreach (var item in group.ToArray()) results[results.IndexOf(item)] = exactClaims
                ? item with { State = WorkspaceSyncState.Duplicate }
                : item with { State = WorkspaceSyncState.Ambiguous, Candidate = null, Candidates = [group.Key] };
        }
        var known = results.SelectMany(r => r.Candidate is { } c ? new[] { c.Path } : r.Candidates).ToHashSet(Paths);
        var folders = entries.Select(Target).OfType<string>().Select(p => System.IO.Path.GetDirectoryName(p)!)
            .Concat(results.Where(r => r.Candidate is not null).Select(r => System.IO.Path.GetDirectoryName(r.Candidate!.Path)!)).ToHashSet(Paths);
        return new(results, files.Values.Where(f => folders.Contains(System.IO.Path.GetDirectoryName(f.Path)!) && !known.Contains(f.Path)).ToArray());
    }

    public static WorkspaceMerge CompareWithoutBaseline(MapDocument local, WorkspaceSyncCandidate external, string directory, bool compensate)
    {
        var output = OsuBeatmapWriter.Serialize(local, compensate);
        // This snapshot aligns the two current versions; it is not evidence of a shared ancestor.
        var comparison = new WorkspaceDifficulty { Sync = new WorkspaceSyncBaseline
        {
            Path = local.SourcePath ?? external.Path, Text = output.Text,
            Authoring = ProjectSerializer.Serialize(local, SnapshotPath(directory)),
            ObjectSources = output.ObjectSources.ToList(), AudioHash = AudioHash(local.AudioPath), RetainedObjectsRecorded = true
        } };
        var merge = Merge(comparison, local, external, directory, compensate);
        var ours = Fields(output.ReadBack); var theirs = Fields(external.Document);
        var objects = merge.Conflicts.Where(c => c.Key.StartsWith("$objects:")).ToArray();
        merge.Conflicts.Clear();
        foreach (string key in ours.Keys.Union(theirs.Keys))
        {
            merge.Fields[key] = ours.GetValueOrDefault(key);
            if (ours.GetValueOrDefault(key) != theirs.GetValueOrDefault(key))
                merge.Conflicts.Add(new(key, ours.GetValueOrDefault(key) ?? "", theirs.GetValueOrDefault(key) ?? ""));
        }
        if (AudioHash(local.AudioPath) != AudioHash(external.Document.AudioPath))
            merge.Conflicts.Add(new("$audio", local.AudioPath ?? "", external.Document.AudioPath ?? ""));
        merge.Conflicts.AddRange(objects);
        return merge;
    }

    public static WorkspaceMerge Merge(WorkspaceDifficulty entry, MapDocument local, WorkspaceSyncCandidate external, string directory, bool compensate)
    {
        if (entry.Sync is not { } baseline) throw new InvalidOperationException(L.Get("sync.baseline"));
        var original = ProjectSerializer.Read(baseline.Authoring, SnapshotPath(directory));
        if (!baseline.RetainedObjectsRecorded)
        {
            // Older baselines already record the accepted pair of versions, even though
            // they did not store the unresolved export differences as explicit groups.
            var historical = new WorkspaceSyncCandidate(baseline.Path, Digest(baseline.Text), baseline.Text, OsuBeatmapReader.Read(baseline.Text, baseline.Path));
            var comparison = CompareWithoutBaseline(original, historical, directory, compensate);
            baseline.RetainedObjects = comparison.Objects.Select(g => new WorkspaceRetainedObjects(g.Sources.ToList(), g.Lines.ToList())).ToList();
            baseline.RetainedObjectsRecorded = true;
        }
        var merge = new WorkspaceMerge { Local = local.DeepClone(), External = external, Baseline = original, PreviouslyRetained = baseline.RetainedObjects };
        var baseFields = Fields(OsuBeatmapReader.Read(baseline.Text, baseline.Path));
        var authorFields = Fields(original); var localFields = Fields(local); var externalFields = Fields(external.Document);
        foreach (string key in baseFields.Keys.Concat(authorFields.Keys).Concat(localFields.Keys).Concat(externalFields.Keys).Distinct())
        {
            baseFields.TryGetValue(key, out var before); authorFields.TryGetValue(key, out var authorBefore);
            localFields.TryGetValue(key, out var ours); externalFields.TryGetValue(key, out var theirs);
            bool outsideChanged = before != theirs, insideChanged = authorBefore != ours || baseline.LocalOverrides.Contains(key);
            merge.Fields[key] = outsideChanged && !insideChanged ? theirs : ours;
            if (IsMetadataField(key) && ours != theirs)
            {
                merge.Fields[key] = ours;
                merge.Conflicts.Add(new(key, ours ?? "", theirs ?? ""));
                if (!outsideChanged && ours == authorBefore && baseline.LocalOverrides.Contains(key)) merge.PreviouslyResolved.Add(key);
            }
            else if (outsideChanged && insideChanged && ours != theirs)
                merge.Conflicts.Add(new(key, ours ?? "", theirs ?? ""));
            else if (!outsideChanged && baseline.LocalOverrides.Contains(key) && ours != theirs)
            {
                merge.Conflicts.Add(new(key, ours ?? "", theirs ?? ""));
                merge.PreviouslyResolved.Add(key);
            }
        }
        string? localAudio = AudioHash(local.AudioPath), externalAudio = AudioHash(external.Document.AudioPath);
        if (localAudio is null && Paths.Equals(local.AudioPath, original.AudioPath)) localAudio = baseline.AudioHash;
        if (localAudio != baseline.AudioHash && externalAudio != baseline.AudioHash && localAudio != externalAudio)
            merge.Conflicts.Add(new("$audio", local.AudioPath ?? "", external.Document.AudioPath ?? ""));

        string[] beforeLines = ObjectLines(baseline.Text), afterLines = ObjectLines(external.Text);
        var retainedSources = baseline.RetainedObjects.SelectMany(r => r.ExternalLines.Select(line => (Line: line, r.Sources)))
            .GroupBy(p => p.Line).ToDictionary(g => g.Key, g => g.SelectMany(p => p.Sources).Distinct().ToArray());
        Guid[] SourcesAt(int index)
        {
            return retainedSources.TryGetValue(beforeLines[index], out var retained) ? retained
                : [index < baseline.ObjectSources.Count ? baseline.ObjectSources[index] : Guid.Empty];
        }
        if (!beforeLines.SequenceEqual(afterLines))
        {
            // Unique exact lines anchor ordered runs. Unmatched runs are explicit groups, never guessed identities.
            var counts = beforeLines.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var positions = afterLines.Select((line, index) => (line, index)).GroupBy(x => x.line)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().index);
            var anchors = new List<(int Before, int After)> { (-1, -1) };
            for (int i = 0; i < beforeLines.Length; i++)
                if (counts[beforeLines[i]] == 1 && positions.TryGetValue(beforeLines[i], out int j) && j > anchors[^1].After) anchors.Add((i, j));
            anchors.Add((beforeLines.Length, afterLines.Length));
            foreach (var anchor in anchors.Where(a => a.Before >= 0 && a.Before < baseline.ObjectSources.Count))
            {
                Guid id = baseline.ObjectSources[anchor.Before];
                if (id != Guid.Empty) merge.ExternalOrders[id] = Math.Min(merge.ExternalOrders.GetValueOrDefault(id, int.MaxValue), anchor.After);
            }
            var changed = new List<(HashSet<Guid> Sources, List<string> Lines, HashSet<string> Before)>();
            for (int k = 1; k < anchors.Count; k++)
            {
                var a = anchors[k - 1]; var b = anchors[k];
                if (a.Before + 1 == b.Before && a.After + 1 == b.After) continue;
                int beforeCount = b.Before - a.Before - 1, afterCount = b.After - a.After - 1;
                if (beforeCount == afterCount && beforeCount > 1
                    && Enumerable.Range(1, beforeCount).All(offset => ObjectKey(beforeLines[a.Before + offset]) == ObjectKey(afterLines[a.After + offset])))
                {
                    for (int offset = 1; offset <= beforeCount; offset++)
                    {
                        var sources = SourcesAt(a.Before + offset);
                        if (sources.Contains(Guid.Empty)) break;
                        changed.Add((sources.ToHashSet(), [afterLines[a.After + offset]], [beforeLines[a.Before + offset]]));
                    }
                    if (Enumerable.Range(a.Before + 1, beforeCount).All(i => !SourcesAt(i).Contains(Guid.Empty))) continue;
                }
                var ids = Enumerable.Range(a.Before + 1, b.Before - a.Before - 1)
                    .SelectMany(SourcesAt).ToHashSet();
                if (ids.Contains(Guid.Empty)) { ids = SourceIds(original).Select(p => p.Id).ToHashSet(); changed.Clear(); changed.Add((ids, afterLines.ToList(), beforeLines.ToHashSet())); break; }
                var lines = afterLines.Skip(a.After + 1).Take(b.After - a.After - 1).ToList();
                changed.Add((ids, lines, beforeLines.Skip(a.Before + 1).Take(beforeCount).ToHashSet()));
            }
            // A moved object can cross an unchanged anchor. Keep the unmatched removal and
            // insertion together rather than presenting an invented absence on each side.
            var removals = changed.Where(c => c.Sources.Count > 0 && c.Lines.Count == 0).ToArray();
            var insertions = changed.Where(c => c.Sources.Count == 0 && c.Before.Count == 0 && c.Lines.Count > 0).ToArray();
            if (removals.Length > 0 && insertions.Length > 0)
            {
                var related = removals.Concat(insertions).ToArray();
                int index = related.Min(c => changed.IndexOf(c));
                var joined = (related.SelectMany(c => c.Sources).ToHashSet(), related.SelectMany(c => c.Lines).ToList(), related.SelectMany(c => c.Before).ToHashSet());
                foreach (var item in related) changed.Remove(item);
                changed.Insert(index, joined);
            }
            // One authoring curve may emit many lines; accepting part would destroy the rest of that curve.
            foreach (var change in changed.ToArray())
            {
                if (!changed.Contains(change)) continue;
                foreach (var other in changed.ToArray())
                    if (!ReferenceEquals(change.Sources, other.Sources) && change.Sources.Overlaps(other.Sources))
                    { change.Sources.UnionWith(other.Sources); change.Lines.AddRange(other.Lines); change.Before.UnionWith(other.Before); changed.Remove(other); }
            }
            int n = 0;
            var localOutput = OsuBeatmapWriter.Serialize(local, compensate);
            foreach (var change in changed)
            {
                // Include unchanged output members of a changed multi-output source.
                for (int i = 0; i < beforeLines.Length; i++)
                    if (SourcesAt(i).Any(change.Sources.Contains) && anchors.Any(a => a.Before == i)) { change.Lines.Add(beforeLines[i]); change.Before.Add(beforeLines[i]); }
                string key = "$objects:" + n++;
                merge.Objects.Add((key, change.Sources.ToArray(), change.Lines.ToArray()));
                merge.ChangedBeforeLines[key] = change.Before.ToArray();
                merge.Conflicts.Add(new(key, string.Join("\n", ObjectLines(localOutput.Text).Where((_, i) => i < localOutput.ObjectSources.Count && change.Sources.Contains(localOutput.ObjectSources[i]))), string.Join("\n", change.Lines)));
            }
        }
        if (baseline.RetainedObjects.Count > 0)
        {
            var output = OsuBeatmapWriter.Serialize(local, compensate);
            var localLines = ObjectLines(output.Text);
            foreach (var retained in baseline.RetainedObjects)
            {
                if (merge.Objects.Any(g => g.Sources.Intersect(retained.Sources).Any()
                    || merge.ChangedBeforeLines[g.Key].Intersect(retained.ExternalLines).Any())) continue;
                var ours = localLines.Where((_, i) => retained.Sources.Contains(output.ObjectSources[i])).ToArray();
                if (ours.SequenceEqual(retained.ExternalLines)) continue;
                string key = "$objects:resolved:" + merge.PreviouslyResolved.Count;
                merge.Objects.Add((key, retained.Sources.ToArray(), retained.ExternalLines.ToArray()));
                merge.ChangedBeforeLines[key] = retained.ExternalLines.ToArray();
                merge.PreviouslyResolved.Add(key);
                merge.Conflicts.Add(new(key, string.Join('\n', ours), string.Join('\n', retained.ExternalLines)));
            }
        }
        var ordered = merge.Conflicts.OrderBy(c => merge.PreviouslyResolved.Contains(c.Key)).ToArray();
        merge.Conflicts.Clear(); merge.Conflicts.AddRange(ordered);
        return merge;
    }

    public static MapDocument Resolve(WorkspaceMerge merge, IReadOnlyDictionary<string, bool> externalChoices)
    {
        foreach (var conflict in merge.Conflicts)
            if (!externalChoices.ContainsKey(conflict.Key) && !merge.PreviouslyResolved.Contains(conflict.Key)) throw new InvalidOperationException(L.Get("sync.unresolved"));
        var result = merge.Local.DeepClone();
        var fields = new Dictionary<string, string?>(merge.Fields);
        var outside = Fields(merge.External.Document);
        foreach (var conflict in merge.Conflicts.Where(c => !c.Key.StartsWith('$')))
            if (externalChoices.GetValueOrDefault(conflict.Key)) fields[conflict.Key] = outside.GetValueOrDefault(conflict.Key);
        ApplyFields(result, fields, merge.External.Path);
        foreach (var group in merge.Objects)
        {
            if (!externalChoices.GetValueOrDefault(group.Key)) continue;
            var ids = group.Sources.ToHashSet();
            result.Fruits.RemoveAll(f => ids.Contains(f.Id)); result.Tracks.RemoveAll(f => ids.Contains(f.Id));
            result.ImportedSliders.RemoveAll(f => ids.Contains(f.Id)); result.BananaShowers.RemoveAll(f => ids.Contains(f.Id));
            var positions = ObjectLines(merge.External.Text).Select((line, i) => (line, i)).GroupBy(p => p.line)
                .ToDictionary(g => g.Key, g => new Queue<int>(g.Select(p => p.i)));
            foreach (string line in group.Lines) OsuBeatmapReader.ParseObject(result, line, positions[line].Dequeue());
        }
        if (merge.Objects.Any(g => externalChoices.GetValueOrDefault(g.Key)))
        {
            foreach (var f in result.Fruits) if (merge.ExternalOrders.TryGetValue(f.Id, out int order)) f.SourceOrder = order;
            foreach (var f in result.Tracks) if (merge.ExternalOrders.TryGetValue(f.Id, out int order)) f.SourceOrder = order;
            foreach (var f in result.ImportedSliders) if (merge.ExternalOrders.TryGetValue(f.Id, out int order)) f.SourceOrder = order;
            foreach (var f in result.BananaShowers) if (merge.ExternalOrders.TryGetValue(f.Id, out int order)) f.SourceOrder = order;
        }
        if (externalChoices.TryGetValue("$audio", out bool audio)) result.AudioPath = audio ? merge.External.Document.AudioPath : merge.Local.AudioPath;
        else if (merge.Fields.GetValueOrDefault("General/AudioFilename") == OsuBeatmapReader.Setting(merge.Local, "General", "AudioFilename")
            && merge.Local.AudioPath is not null && !Paths.Equals(merge.Local.AudioPath, merge.Baseline.AudioPath)) result.AudioPath = merge.Local.AudioPath;
        result.SourcePath = merge.External.Path;
        OsuBeatmapReader.Validate(result);
        return result;
    }

    public static void Accept(WorkspaceSession session, WorkspaceDifficulty entry, WorkspaceSyncCandidate external, MapDocument resolved, bool compensate, bool retainLocalFields = false,
        WorkspaceMerge? review = null, IReadOnlyDictionary<string, bool>? choices = null)
    {
        if (WorkspaceProject.Hash(external.Path) != external.Hash) throw new IOException(L.Get("library.exportConflict", external.Path));
        WorkspaceAssociations.EnsureOwner(session, entry.Id, external.Path);
        var output = OsuBeatmapWriter.Serialize(resolved, compensate);
        // The external baseline stays external even when the chosen local resolution has not been exported.
        var sources = ObjectLines(output.Text).SequenceEqual(ObjectLines(external.Text)) ? output.ObjectSources : MapExternalSources(resolved, external, output, entry.Sync);
        var pending = new List<string>();
        var resolvedFields = Fields(resolved); var externalFields = Fields(external.Document);
        if (entry.Sync is { } previous && !retainLocalFields)
        {
            var oldFields = Fields(ProjectSerializer.Read(previous.Authoring, SnapshotPath(session.Directory)));
            pending.AddRange(resolvedFields.Keys.Union(externalFields.Keys).Where(k => (previous.LocalOverrides.Contains(k) || resolvedFields.GetValueOrDefault(k) != oldFields.GetValueOrDefault(k))
                && resolvedFields.GetValueOrDefault(k) != externalFields.GetValueOrDefault(k)));
        }
        else pending.AddRange(resolvedFields.Keys.Union(externalFields.Keys).Where(k => resolvedFields.GetValueOrDefault(k) != externalFields.GetValueOrDefault(k)));
        if (review is not null && choices is not null)
            foreach (var conflict in review.Conflicts.Where(c => IsMetadataField(c.Key)))
                if (choices.TryGetValue(conflict.Key, out bool takeExternal) && !takeExternal
                    && resolvedFields.GetValueOrDefault(conflict.Key) != externalFields.GetValueOrDefault(conflict.Key)
                    && !pending.Contains(conflict.Key)) pending.Add(conflict.Key);
        var retained = entry.Sync?.RetainedObjects.ToList() ?? [];
        if (review is not null && choices is not null)
            foreach (var group in review.Objects)
            {
                retained.RemoveAll(r => r.Sources.Intersect(group.Sources).Any() || r.ExternalLines.Intersect(review.ChangedBeforeLines.GetValueOrDefault(group.Key) ?? []).Any());
                if (choices.TryGetValue(group.Key, out bool takeExternal) && !takeExternal)
                    retained.Add(new(group.Sources.ToList(), group.Lines.ToList()));
            }
        if (ObjectLines(output.Text).SequenceEqual(ObjectLines(external.Text))) retained.Clear();
        else
        {
            var liveSources = SourceIds(resolved).Select(p => p.Id).ToHashSet();
            retained = retained.Select(r => new WorkspaceRetainedObjects(r.Sources.Where(liveSources.Contains).ToList(), r.ExternalLines.ToList())).ToList();
        }
        entry.Sync = Capture(external.Path, resolved, session.Directory, external.Text, sources, entry.Sync);
        entry.Sync.RetainedObjects = retained;
        entry.Sync.LocalOverrides = pending;
        entry.Source = external.Path; entry.SourceHash = external.Hash;
        if (entry.ExportTarget is not null) { entry.ExportTarget = external.Path; entry.ExportHash = external.Hash; }
        if (session.Manifest.SongsRoot is { } root && WorkspaceProject.Within(root, external.Path))
            session.Manifest.SourceDirectory = System.IO.Path.GetRelativePath(root, System.IO.Path.GetDirectoryName(external.Path)!);
        else session.Manifest.ExternalSourceDirectory = System.IO.Path.GetDirectoryName(external.Path);
    }

    private static IReadOnlyList<Guid> MapExternalSources(MapDocument document, WorkspaceSyncCandidate external, OsuWriteResult output, WorkspaceSyncBaseline? previous)
    {
        var exact = ObjectLines(output.Text).Select((line, i) => (line, Id: output.ObjectSources[i])).GroupBy(p => p.line)
            .Where(g => g.Select(p => p.Id).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().Id);
        var live = SourceIds(document).Select(p => p.Id).ToHashSet();
        var historical = previous is null ? new Dictionary<string, Guid>() : ObjectLines(previous.Text)
            .Select((line, i) => (Key: ObjectKey(line), Id: i < previous.ObjectSources.Count ? previous.ObjectSources[i] : Guid.Empty))
            .GroupBy(p => p.Key).Where(g => g.Count() == 1 && live.Contains(g.First().Id)).ToDictionary(g => g.Key, g => g.First().Id);
        return ObjectLines(external.Text).Select(line => exact.TryGetValue(line, out var id) ? id : historical.GetValueOrDefault(ObjectKey(line))).ToArray();
    }

    public static string Archive(WorkspaceSession session, string operation)
    {
        string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(session.Directory)!, ".sync-history", session.Manifest.Id.ToString("N"), DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + operation);
        Directory.CreateDirectory(target);
        foreach (string path in Directory.EnumerateFiles(session.Directory)) File.Copy(path, System.IO.Path.Combine(target, System.IO.Path.GetFileName(path)));
        var saved = new BeatmapProject { Name = session.Manifest.Name };
        var manifest = WorkspaceProject.ReadManifest(session.Directory);
        foreach (var entry in manifest.Difficulties)
        {
            if (System.IO.Path.GetFileName(entry.File) != entry.File) throw new InvalidDataException(L.Get("project.invalid"));
            saved.Difficulties.Add(new ProjectDifficulty { Id = entry.Id, Name = entry.Name,
                Document = ProjectSerializer.ReadFile(System.IO.Path.Combine(session.Directory, entry.File)) });
        }
        ProjectSerializer.WriteFile(saved, System.IO.Path.Combine(target, "saved.catchproj"));
        return target;
    }

    private static string? StoreAudio(string? path, string directory)
    {
        lock (WorkspaceProject.Gate)
        {
            string? hash = AudioHash(path);
            if (hash is null) return null;
            string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory)!, ".sync-history", "resources", hash);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(path!, target);
            if (WorkspaceProject.Hash(target) != hash) throw new IOException(L.Get("sync.resourceChanged"));
            // A newly captured baseline may not be published yet when maintenance runs.
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
            return hash;
        }
    }
    public static string? AudioHash(string? path) => path is not null && File.Exists(path) ? WorkspaceProject.Hash(path) : null;
    public static string? LocalAudioVersion(WorkspaceDifficulty entry, MapDocument local, string directory)
    {
        lock (WorkspaceProject.Gate)
        {
            if (entry.Sync is not { } baseline || (baseline.AuthoringAudioHash ?? baseline.AudioHash) is not { } hash) return local.AudioPath;
            var original = ProjectSerializer.Read(baseline.Authoring, SnapshotPath(directory));
            if (!Paths.Equals(local.AudioPath, original.AudioPath) || AudioHash(local.AudioPath) == hash) return local.AudioPath;
            string resources = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory)!, ".sync-history", "resources");
            string stored = System.IO.Path.Combine(resources, hash);
            if (!File.Exists(stored) || WorkspaceProject.Hash(stored) != hash) throw new IOException(L.Get("sync.resourceChanged"));
            string playback = System.IO.Path.Combine(resources, "playback", hash + System.IO.Path.GetExtension(original.AudioPath ?? ".mp3"));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(playback)!);
            if (!File.Exists(playback)) File.Copy(stored, playback);
            else if (WorkspaceProject.Hash(playback) != hash)
            {
                playback = System.IO.Path.Combine(resources, "playback", Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(playback));
                File.Copy(stored, playback);
            }
            File.SetLastWriteTimeUtc(playback, DateTime.UtcNow);
            return playback;
        }
    }
    public static IEnumerable<(Guid Id, int Order)> SourceIds(MapDocument document) => document.Fruits.Select(f => (f.Id, f.SourceOrder))
        .Concat(document.Tracks.Select(f => (f.Id, f.SourceOrder))).Concat(document.ImportedSliders.Select(f => (f.Id, f.SourceOrder)))
        .Concat(document.BananaShowers.Select(f => (f.Id, f.SourceOrder)));

    public static string[] ObjectLines(string text)
    {
        bool objects = false; var lines = new List<string>();
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { objects = line == "[HitObjects]"; continue; }
            if (objects && OsuBeatmapReader.IsDataLine(line)) lines.Add(NormalizeObject(line));
        }
        return lines.ToArray();
    }
    public static string ObjectSignature(string text) => Digest(string.Join("\n", ObjectLines(text)));
    public static string? OnlineIdentity(string text)
    {
        bool metadata = false; string? id = null, set = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { metadata = line == "[Metadata]"; continue; }
            if (!metadata) continue;
            var parts = line.Split(':', 2); if (parts.Length != 2) continue;
            if (parts[0].Trim() == "BeatmapID") id = parts[1].Trim();
            if (parts[0].Trim() == "BeatmapSetID") set = parts[1].Trim();
        }
        return long.TryParse(id, out long mapId) && mapId > 0 && long.TryParse(set, out long setId) && setId > 0 ? setId + "/" + mapId : null;
    }
    public static void RebaseContext(MapDocument snapshot, MapDocument before, MapDocument after)
    {
        var fields = Fields(snapshot); var oldFields = Fields(before); var newFields = Fields(after);
        foreach (string key in oldFields.Keys.Concat(newFields.Keys).Distinct())
            if (oldFields.GetValueOrDefault(key) != newFields.GetValueOrDefault(key)) fields[key] = newFields.GetValueOrDefault(key);
        string? audio = snapshot.AudioPath;
        ApplyFields(snapshot, fields, after.SourcePath ?? before.SourcePath ?? System.IO.Path.Combine(Environment.CurrentDirectory, "map.osu"));
        snapshot.SourcePath = after.SourcePath;
        snapshot.AudioPath = Paths.Equals(before.AudioPath, after.AudioPath) ? audio : after.AudioPath;
    }
    private static string NormalizeObject(string line)
    {
        string[] parts = line.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i < 5 || i is 6 or 7)
            {
                if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) parts[i] = n.ToString("R", CultureInfo.InvariantCulture);
            }
        }
        return string.Join(',', parts);
    }
    private static string ObjectKey(string line)
    {
        var parts = line.Split(',');
        return parts.Length > 3 ? parts[2] + "/" + (int.Parse(parts[3], CultureInfo.InvariantCulture) & 11) : line;
    }

    private static readonly HashSet<string> SettingsSections = ["General", "Editor", "Metadata", "Difficulty", "Colours"];
    internal static Dictionary<string, string?> Fields(MapDocument document)
    {
        var fields = new Dictionary<string, string?>();
        foreach (var section in document.OriginalSections.Where(s => s.Name is not ("HitObjects" or "TimingPoints" or "")))
        {
            if (SettingsSections.Contains(section.Name))
            {
                foreach (string line in section.Lines.Where(l => OsuBeatmapReader.IsDataLine(l.Trim())))
                {
                    string[] split = line.Split(':', 2);
                    if (split.Length == 2) fields[section.Name + "/" + split[0].Trim()] = split[1].Trim();
                }
            }
            else fields[section.Name + "/"] = string.Join("\n", section.Lines);
        }
        fields["Difficulty/ApproachRate"] = document.ApproachRate.ToString("R", CultureInfo.InvariantCulture);
        fields["Difficulty/CircleSize"] = document.CircleSize.ToString("R", CultureInfo.InvariantCulture);
        fields["Difficulty/SliderMultiplier"] = document.SliderMultiplier.ToString("R", CultureInfo.InvariantCulture);
        fields["Difficulty/SliderTickRate"] = document.SliderTickRate.ToString("R", CultureInfo.InvariantCulture);
        fields["Editor/DistanceSpacing"] = document.DistanceSpacing.ToString("R", CultureInfo.InvariantCulture);
        fields["TimingPoints/"] = string.Join("\n", document.TimingPoints.Select(t => string.Join(',',
            t.TimeMs.ToString("R", CultureInfo.InvariantCulture), t.BeatLengthMs.ToString("R", CultureInfo.InvariantCulture), t.Meter, t.SampleSet, t.SampleIndex, t.Volume, t.Uninherited ? 1 : 0, t.Effects)));
        return fields;
    }
    private static void ApplyFields(MapDocument target, Dictionary<string, string?> fields, string path)
    {
        var text = new StringBuilder("osu file format v14\n");
        foreach (var group in fields.Where(p => p.Value is not null).GroupBy(p => p.Key.Split('/')[0]))
        {
            text.Append('[').Append(group.Key).Append("]\n");
            foreach (var pair in group) text.Append(pair.Key[(pair.Key.IndexOf('/') + 1)..] is { Length: > 0 } key ? key + ":" + pair.Value : pair.Value).Append('\n');
        }
        var parsed = OsuBeatmapReader.Read(text.ToString(), path);
        target.OriginalSections.Clear(); target.OriginalSections.AddRange(parsed.OriginalSections);
        target.Name = parsed.Name; target.ApproachRate = parsed.ApproachRate; target.CircleSize = parsed.CircleSize;
        target.SliderMultiplier = parsed.SliderMultiplier; target.SliderTickRate = parsed.SliderTickRate; target.DistanceSpacing = parsed.DistanceSpacing;
        target.TimingPoints.Clear(); target.TimingPoints.AddRange(parsed.TimingPoints);
        target.BeatLengthMs = parsed.BeatLengthMs; target.TimingOffsetMs = parsed.TimingOffsetMs;
        target.AudioPath = parsed.AudioPath;
    }
}
