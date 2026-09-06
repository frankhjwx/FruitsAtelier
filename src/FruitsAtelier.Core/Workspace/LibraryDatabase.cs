using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed class LibrarySettings
{
    public string Workspace { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FruitsAtelier Workspace");
    public string Songs { get; set; } = "";
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FruitsAtelier", "library.json");
    public static LibrarySettings Load(string? path = null) => File.Exists(path ?? SettingsPath) ? JsonSerializer.Deserialize<LibrarySettings>(File.ReadAllText(path ?? SettingsPath)) ?? new() : new();
    public void Save(string? path = null)
    {
        Workspace = Path.GetFullPath(Workspace); Songs = string.IsNullOrWhiteSpace(Songs) ? "" : Path.GetFullPath(Songs);
        WorkspaceProject.ValidateRoots(Workspace, Songs);
        Directory.CreateDirectory(Workspace);
        path = Path.GetFullPath(path ?? SettingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, JsonSerializer.Serialize(this));
    }
}

public sealed record LibraryMap(string Path, string Directory, string Title, string TitleUnicode, string Artist, string ArtistUnicode,
    string Creator, string Difficulty, string Tags, string Source, string Audio, string Background, string? ProjectPath = null);
public sealed record LibraryScan(int Count, IReadOnlyList<string> Errors);

public sealed class LibraryDatabase
{
    private readonly string workspace, songs;
    public LibraryDatabase(string workspace, string songs)
    {
        WorkspaceProject.ValidateRoots(workspace, songs, false);
        this.workspace = Path.GetFullPath(workspace); this.songs = string.IsNullOrWhiteSpace(songs) ? "" : Path.GetFullPath(songs);
        Directory.CreateDirectory(workspace);
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS maps(path TEXT PRIMARY KEY, root TEXT NOT NULL, stamp INTEGER NOT NULL, size INTEGER NOT NULL, data TEXT NOT NULL, search TEXT NOT NULL); CREATE TABLE IF NOT EXISTS projects(path TEXT PRIMARY KEY, source TEXT, name TEXT NOT NULL); PRAGMA user_version=1;";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        string path = Path.Combine(workspace, "library.db"); WorkspaceProject.RejectLinks(path);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); db.Open(); return db;
    }
    public static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    public LibraryScan Scan(CancellationToken cancellation = default)
    {
        if (songs.Length == 0) { ReindexProjects(); return new(0, []); }
        if (!Directory.Exists(songs)) throw new DirectoryNotFoundException(songs);
        using var db = Open();
        using var transaction = db.BeginTransaction();
        var errors = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        var cached = new Dictionary<string, (long Stamp, long Size)>();
        using (var command = db.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "SELECT path,stamp,size FROM maps WHERE root=$root"; command.Parameters.AddWithValue("$root", songs);
            using var reader = command.ExecuteReader(); while (reader.Read()) cached[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
        }
        var directories = new Stack<string>(); directories.Push(songs);
        while (directories.TryPop(out string? directory))
        {
            cancellation.ThrowIfCancellationRequested();
            string[] files;
            try
            {
                WorkspaceProject.RejectLinks(directory);
                foreach (var child in System.IO.Directory.EnumerateDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) directories.Push(child);
                files = System.IO.Directory.GetFiles(directory).Where(p => Path.GetExtension(p).Equals(".osu", StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { errors.Add(directory + ": " + e.Message); continue; }
            foreach (var file in files)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    WorkspaceProject.RejectLinks(file);
                    var info = new FileInfo(file); var stamp = info.LastWriteTimeUtc.Ticks;
                    if (cached.TryGetValue(file, out var old) && old == (stamp, info.Length)) { seen.Add(file); continue; }
                    var map = ReadMetadata(file);
                    if (map is null) continue;
                    seen.Add(file);
                    using var command = db.CreateCommand(); command.Transaction = transaction;
                    command.CommandText = "INSERT OR REPLACE INTO maps VALUES($p,$r,$t,$s,$d,$q)";
                    command.Parameters.AddWithValue("$p", file); command.Parameters.AddWithValue("$r", songs);
                    command.Parameters.AddWithValue("$t", stamp); command.Parameters.AddWithValue("$s", info.Length);
                    command.Parameters.AddWithValue("$d", JsonSerializer.Serialize(map));
                    command.Parameters.AddWithValue("$q", Normalize(string.Join(" ", map.Title, map.TitleUnicode, map.Artist, map.ArtistUnicode, map.Creator, map.Difficulty, map.Tags, map.Source)));
                    command.ExecuteNonQuery();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { errors.Add(file + ": " + e.Message); }
            }
        }
        // Only prune on a complete scan; inaccessible subtrees must not erase cached maps.
        if (errors.Count == 0)
            foreach (var path in cached.Keys.Where(p => !seen.Contains(p)))
            {
                using var command = db.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "DELETE FROM maps WHERE path=$p"; command.Parameters.AddWithValue("$p", path); command.ExecuteNonQuery();
            }
        transaction.Commit(); ReindexProjects();
        return new(seen.Count, errors);
    }

    public void ReindexProjects() { lock (WorkspaceProject.Gate) ReindexProjectsLocked(); }
    private void ReindexProjectsLocked()
    {
        foreach (var previous in Directory.EnumerateDirectories(workspace, "*.previous")) WorkspaceProject.Recover(previous[..^9]);
        using var db = Open(); using var transaction = db.BeginTransaction();
        using (var clear = db.CreateCommand()) { clear.Transaction = transaction; clear.CommandText = "DELETE FROM projects"; clear.ExecuteNonQuery(); }
        foreach (string folder in Directory.EnumerateDirectories(workspace))
        {
            if (!File.Exists(Path.Combine(folder, WorkspaceProject.ManifestName)) || folder.EndsWith(".saving") || folder.EndsWith(".previous")) continue;
            try
            {
                var manifest = WorkspaceProject.ReadManifest(folder);
                using var command = db.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "INSERT INTO projects VALUES($p,$s,$n)";
                command.Parameters.AddWithValue("$p", folder);
                command.Parameters.AddWithValue("$s", manifest.SongsRoot is not null && manifest.SourceDirectory is not null ? Path.GetFullPath(Path.Combine(manifest.SongsRoot, manifest.SourceDirectory)) : DBNull.Value);
                command.Parameters.AddWithValue("$n", manifest.Name); command.ExecuteNonQuery();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* A damaged project stays on disk; explicit open reports the error. */ }
        }
        transaction.Commit();
    }

    public IReadOnlyList<LibraryMap> Search(string query, bool projectsOnly = false)
    {
        using var db = Open(); using var command = db.CreateCommand();
        var words = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        command.CommandText = "SELECT m.data,(SELECT p.path FROM projects p WHERE p.source=json_extract(m.data,'$.Directory') LIMIT 1) FROM maps m WHERE m.root=$r";
        command.Parameters.AddWithValue("$r", songs);
        for (int i = 0; i < words.Length; i++) { command.CommandText += $" AND instr(m.search,$q{i})>0"; command.Parameters.AddWithValue("$q" + i, words[i]); }
        command.CommandText += " ORDER BY json_extract(m.data,'$.Title') COLLATE NOCASE,path";
        var maps = new List<LibraryMap>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var map = JsonSerializer.Deserialize<LibraryMap>(reader.GetString(0))!;
                if (!reader.IsDBNull(1)) map = map with { ProjectPath = reader.GetString(1) };
                if (!projectsOnly || map.ProjectPath is not null) maps.Add(map);
            }
        if (projectsOnly)
        {
            using var projects = db.CreateCommand(); projects.CommandText = "SELECT path,name FROM projects";
            using var reader = projects.ExecuteReader();
            while (reader.Read())
            {
                string path = reader.GetString(0), name = reader.GetString(1);
                if (!maps.Any(m => m.ProjectPath == path) && words.All(w => Normalize(name).Contains(w)))
                    maps.Add(new(path, path, name, "", "", "", "", "", "", "", "", "", path));
            }
        }
        return maps;
    }

    public static LibraryMap? ReadMetadata(string path)
    {
        if (new FileInfo(path).Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.reader.fileLimit"));
        var values = new Dictionary<string, string>(); string section = "", background = "";
        foreach (string line in File.ReadLines(path))
        {
            string s = line.Trim();
            if (s.StartsWith('[')) { section = s.Trim('[', ']'); if (section == "HitObjects") break; continue; }
            if (section is "General" or "Metadata") { var parts = s.Split(':', 2); if (parts.Length == 2) values[parts[0]] = parts[1].Trim(); }
            if (section == "Events" && s.StartsWith("0,")) { var parts = WorkspaceProject.Csv(s); if (parts.Length > 2) background = parts[2]; }
        }
        string Get(string key) => values.GetValueOrDefault(key, "");
        if (Get("Mode") != "2") return null;
        string Resolve(string resource) => resource.Length == 0 ? "" : OsuBeatmapReader.ResolveResource(path, resource.Replace('\\', '/'));
        return new(path, Path.GetDirectoryName(path)!, Get("Title"), Get("TitleUnicode"), Get("Artist"), Get("ArtistUnicode"), Get("Creator"), Get("Version"), Get("Tags"), Get("Source"), Resolve(Get("AudioFilename")), Resolve(background));
    }
}
