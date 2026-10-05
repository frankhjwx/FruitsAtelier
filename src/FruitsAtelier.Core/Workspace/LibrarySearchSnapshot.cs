using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace FruitsAtelier.Core;

public sealed record LibrarySetRow(int Index, string Key, LibraryMap Map, int Count, bool? InSongs = null, int MissingCount = 0);

// A disk-backed result index keeps random scrolling independent of library size.
// Callers serialize access on a worker; no query or disposal belongs on the UI thread.
public sealed class LibrarySearchSnapshot : IDisposable
{
    private readonly SqliteConnection db;
    private readonly string catalogPath;
    private readonly bool projectsOnly, favouritesOnly;
    private readonly string songs;
    private sealed record ProjectListing(IReadOnlyList<LibraryMap> Maps, bool? InSongs, LibraryMap Representative);
    private readonly Dictionary<string, ProjectListing> projectDetails = new(WorkspaceSynchronization.Paths);
    private readonly Dictionary<string, Task<WorkspaceSyncScan?>> discoveries = new(WorkspaceSynchronization.Paths);
    private readonly CancellationTokenSource discoveryCancellation = new();
    private readonly SemaphoreSlim discoveryQueue = new(1);
    private long discoveryRevision, cachedDiscoveryRevision;
    public long DiscoveryRevision => Interlocked.Read(ref discoveryRevision);
    public void CancelReferenceDiscovery() => discoveryCancellation.Cancel();
    public int Count { get; }
    internal LibrarySearchSnapshot(SqliteConnection connection, string songs, string query, bool projectsOnly, IReadOnlyCollection<string>? favourites)
    {
        this.projectsOnly = projectsOnly;
        favouritesOnly = favourites is not null;
        this.songs = songs;
        catalogPath = connection.DataSource;
        connection.Dispose();
        db = new SqliteConnection("Data Source=:memory:;Pooling=False");
        try
        {
            db.Open();
            using var attachment = Attach();
            using var command = db.CreateCommand();
            command.CommandText = "PRAGMA temp_store=FILE; PRAGMA cache_size=-8192; PRAGMA temp.cache_size=-8192; "
                + "CREATE TEMP TABLE matches(path TEXT PRIMARY KEY,groupKey TEXT,title TEXT,project TEXT) WITHOUT ROWID;";
            command.ExecuteNonQuery();
            string project = "(SELECT p.project FROM project_sources p WHERE p.source=json_extract(m.data,'$.Directory') ORDER BY p.project LIMIT 1)";
            var favouriteKeys = new HashSet<string>(favourites ?? [], WorkspaceSynchronization.Paths);
            db.CreateFunction<string?, string?, bool>("is_favourite", (directory, projectPath) =>
                directory is not null && favouriteKeys.Contains(directory) || projectPath is not null && favouriteKeys.Contains(projectPath));
            string key = favouritesOnly ? $"coalesce({project},json_extract(m.data,'$.Directory'))" : projectsOnly ? project : "json_extract(m.data,'$.Directory')";
            command.CommandText = $"INSERT INTO matches SELECT m.path,{key},json_extract(m.data,'$.Title'),{project} FROM maps m WHERE (m.root=$r OR m.root IN (SELECT path FROM external_sources))";
            command.Parameters.AddWithValue("$r", songs);
            var words = LibraryDatabase.Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                command.CommandText += $" AND instr(m.search,$q{i})>0";
                command.Parameters.AddWithValue("$q" + i, words[i]);
            }
            if (favouritesOnly) command.CommandText += $" AND is_favourite(json_extract(m.data,'$.Directory'),{project})";
            if (projectsOnly) command.CommandText += $" AND {project} IS NOT NULL";
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = "CREATE INDEX temp.matches_group ON matches(groupKey,path); "
                + "CREATE TEMP TABLE sets(position INTEGER PRIMARY KEY,groupKey TEXT,firstPath TEXT,title TEXT,project TEXT,count INTEGER); "
                + "INSERT INTO sets(groupKey,firstPath,title,project,count) SELECT groupKey,min(path),min(title),min(project),count(*) FROM matches GROUP BY groupKey ORDER BY min(title) COLLATE NOCASE,min(path); "
                + "CREATE UNIQUE INDEX temp.sets_key ON sets(groupKey);";
            command.ExecuteNonQuery();
            if (projectsOnly || favouritesOnly)
            {
                db.CreateFunction<string, bool>("matches_name", name => words.All(word => LibraryDatabase.Normalize(name).Contains(word)));
                command.CommandText = "INSERT INTO sets(groupKey,firstPath,title,project,count) SELECT path,path,name,path,1 FROM projects p WHERE NOT EXISTS(SELECT 1 FROM sets s WHERE s.groupKey=p.path) AND matches_name(name) ORDER BY name COLLATE NOCASE,path";
                if (favouritesOnly) command.CommandText = command.CommandText.Replace("ORDER BY name", "AND is_favourite(NULL,p.path) ORDER BY name");
                command.ExecuteNonQuery();
            }
            command.Parameters.Clear(); command.CommandText = "SELECT count(*) FROM sets";
            Count = Convert.ToInt32(command.ExecuteScalar());
        }
        catch { db.Dispose(); throw; }
    }
    public int FindIndex(string? key)
    {
        if (key is null) return -1;
        using var command = db.CreateCommand();
        command.CommandText = "SELECT position-1 FROM sets WHERE groupKey=$k"; command.Parameters.AddWithValue("$k", key);
        return command.ExecuteScalar() is long index ? (int)index : -1;
    }
    public IReadOnlyList<LibrarySetRow> Page(int start, int count = 64)
    {
        using var attachment = Attach();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT s.position-1,s.groupKey,s.firstPath,s.title,s.project,s.count,m.data FROM sets s LEFT JOIN maps m ON m.path=s.firstPath WHERE s.position BETWEEN $a AND $b ORDER BY s.position";
        command.Parameters.AddWithValue("$a", start + 1); command.Parameters.AddWithValue("$b", start + Math.Clamp(count, 1, 128));
        var rows = new List<LibrarySetRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string? project = reader.IsDBNull(4) ? null : reader.GetString(4);
            var map = reader.IsDBNull(6) ? new LibraryMap(reader.GetString(2), reader.GetString(1), reader.GetString(3), "", "", "", "", "", "", "", "", "", project)
                : JsonSerializer.Deserialize<LibraryMap>(reader.GetString(6))! with { ProjectPath = project };
            int difficultyCount = reader.GetInt32(5);
            WorkspaceManifest? manifest = null;
            if (project is not null)
                lock (WorkspaceProject.Gate) manifest = WorkspaceProject.ReadManifest(project, includeSync: false);
            ProjectListing? details = (projectsOnly || favouritesOnly) && manifest is not null ? ProjectDifficulties(project!, map, manifest) : null;
            if (details is not null) { difficultyCount = details.Maps.Count; map = details.Representative; }
            bool? inSongs = string.IsNullOrWhiteSpace(songs) ? null
                : manifest is not null
                    ? WorkspaceProject.HasExistingSongsFile(manifest, songs)
                    : ExistsInSongs(map.Path);
            rows.Add(new(reader.GetInt32(0), reader.GetString(1), map, difficultyCount, details?.InSongs ?? inSongs, details?.Maps.Count(d => d.ExternalMissing) ?? 0));
        }
        return rows;
    }
    private bool ExistsInSongs(string? path)
        => path is not null && WorkspaceProject.Within(songs, path) && File.Exists(path);
    public IReadOnlyList<LibraryMap> Difficulties(LibrarySetRow set, int start, int count = 64)
    {
        using var attachment = Attach();
        if ((projectsOnly || favouritesOnly) && set.Map.ProjectPath is { } project)
        {
            return ProjectDifficulties(project, set.Map).Maps.Skip(Math.Max(0, start)).Take(Math.Clamp(count, 1, 128)).ToArray();
        }
        using var command = db.CreateCommand();
        command.CommandText = "SELECT m.data FROM matches s JOIN maps m ON m.path=s.path WHERE s.groupKey=$k ORDER BY s.path LIMIT $n OFFSET $s";
        command.Parameters.AddWithValue("$k", set.Key); command.Parameters.AddWithValue("$n", Math.Clamp(count, 1, 128)); command.Parameters.AddWithValue("$s", Math.Max(0, start));
        var result = new List<LibraryMap>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<LibraryMap>(reader.GetString(0))! with { ProjectPath = set.Map.ProjectPath });
        if (result.Count == 0 && start == 0) result.Add(set.Map);
        return result;
    }

    private ProjectListing ProjectDifficulties(string project, LibraryMap fallback, WorkspaceManifest? manifest = null)
    {
        long revision = DiscoveryRevision;
        if (cachedDiscoveryRevision != revision) { projectDetails.Clear(); cachedDiscoveryRevision = revision; }
        if (projectDetails.TryGetValue(project, out var cached)) return cached;
        if (manifest is null)
            lock (WorkspaceProject.Gate) manifest = WorkspaceProject.ReadManifest(project, includeSync: false);
        var targets = manifest.Difficulties.ToDictionary(d => d.Id, WorkspaceSynchronization.Target);
        var folders = targets.Values.OfType<string>().Select(p => Path.GetDirectoryName(p)!).ToHashSet(WorkspaceSynchronization.Paths);
        if (manifest.SongsRoot is { } root && manifest.SourceDirectory is { } relative) folders.Add(Path.Combine(root, relative));
        if (manifest.ExternalSourceDirectory is { } external) folders.Add(external);
        var missing = new HashSet<Guid>();
        bool searching = false;
        if (targets.Values.OfType<string>().Any(p => !File.Exists(p)))
        {
            if (!discoveries.TryGetValue(project, out var task))
            {
                // Reference discovery must never be a prerequisite for showing indexed rows.
                // Retiring a search cancels its queued scans rather than accumulating work.
                var cancellation = discoveryCancellation.Token;
                task = Task.Run(async () =>
                {
                    bool entered = false;
                    try
                    {
                        await discoveryQueue.WaitAsync(cancellation); entered = true;
                        cancellation.ThrowIfCancellationRequested();
                        WorkspaceManifest baseline;
                        lock (WorkspaceProject.Gate) baseline = WorkspaceProject.ReadManifest(project);
                        return WorkspaceSynchronization.Scan(new(project, baseline, new BeatmapProject()), songs, searchMissing: true, cancellation: cancellation);
                    }
                    catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
                    finally { if (entered) discoveryQueue.Release(); }
                });
                discoveries[project] = task;
                _ = task.ContinueWith(_ => Interlocked.Increment(ref discoveryRevision), TaskScheduler.Default);
            }
            searching = !task.IsCompleted;
            var scan = task.IsCompletedSuccessfully ? task.Result : null;
            if (scan is not null) foreach (var status in scan.Difficulties)
            {
                if (status.State is WorkspaceSyncState.Missing or WorkspaceSyncState.Ambiguous
                    || status.State == WorkspaceSyncState.Duplicate && targets[status.DifficultyId] is { } absent && !File.Exists(absent))
                    missing.Add(status.DifficultyId);
                if (status.State != WorkspaceSyncState.Duplicate && status.Candidate is { } candidate)
                {
                    targets[status.DifficultyId] = candidate.Path;
                    folders.Add(Path.GetDirectoryName(candidate.Path)!);
                }
            }
        }
        var live = new Dictionary<string, LibraryMap>(WorkspaceSynchronization.Paths);
        foreach (string folder in folders)
        {
            using var lookup = db.CreateCommand();
            lookup.CommandText = "SELECT data FROM maps WHERE json_extract(data,'$.Directory') COLLATE NOCASE=$d";
            lookup.Parameters.AddWithValue("$d", folder);
            using var reader = lookup.ExecuteReader();
            while (reader.Read())
            {
                var map = JsonSerializer.Deserialize<LibraryMap>(reader.GetString(0))!;
                if (WorkspaceSynchronization.Paths.Equals(map.Directory, folder) && File.Exists(map.Path)) live[map.Path] = map;
            }
        }
        var entries = new List<LibraryMap>();
        var representative = (live.Values.OrderBy(m => m.Path, WorkspaceSynchronization.Paths).FirstOrDefault() ?? fallback) with { ProjectPath = project };
        foreach (var difficulty in manifest.Difficulties)
        {
            if (Path.GetFileName(difficulty.File) != difficulty.File || !difficulty.File.EndsWith(".catchdiff", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(FruitsAtelier.Localization.Strings.Get("project.invalid"));
            var metadata = targets[difficulty.Id] is { } target && live.Remove(target, out var map) ? map : fallback;
            entries.Add(metadata with { Path = Path.Combine(project, difficulty.File), Directory = project,
                Difficulty = difficulty.Name, ProjectPath = project, ExternalMissing = missing.Contains(difficulty.Id),
                ReferenceSearching = searching && targets[difficulty.Id] is { } absent && !File.Exists(absent) });
        }
        entries.AddRange(live.Values.OrderBy(m => m.Path, WorkspaceSynchronization.Paths).Select(m => m with { ProjectPath = project }));
        if (projectDetails.Count >= 128) projectDetails.Remove(projectDetails.Keys.First());
        bool? inSongs = string.IsNullOrWhiteSpace(songs) ? null : targets.Values.OfType<string>()
            .Concat(live.Keys).Any(p => WorkspaceProject.Within(songs, p) && File.Exists(p));
        var listing = new ProjectListing(entries, inSongs, representative);
        projectDetails[project] = listing;
        return listing;
    }
    private IDisposable Attach()
    {
        using var command = db.CreateCommand();
        command.CommandText = "ATTACH DATABASE $p AS catalog";
        command.Parameters.AddWithValue("$p", catalogPath); command.ExecuteNonQuery();
        return new Attachment(db);
    }
    private sealed class Attachment(SqliteConnection db) : IDisposable
    {
        public void Dispose()
        {
            using var command = db.CreateCommand(); command.CommandText = "DETACH DATABASE catalog"; command.ExecuteNonQuery();
        }
    }
    public void Dispose()
    {
        discoveryCancellation.Cancel(); db.Dispose();
        _ = Task.WhenAll(discoveries.Values).ContinueWith(t =>
        { _ = t.Exception; discoveryQueue.Dispose(); discoveryCancellation.Dispose(); }, TaskScheduler.Default);
    }
}
