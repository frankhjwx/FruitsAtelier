using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace FruitsAtelier.Core;

public sealed record LibrarySetRow(int Index, string Key, LibraryMap Map, int Count);

// A disk-backed result index keeps random scrolling independent of library size.
// Callers serialize access on a worker; no query or disposal belongs on the UI thread.
public sealed class LibrarySearchSnapshot : IDisposable
{
    private readonly SqliteConnection db;
    private readonly string catalogPath;
    public int Count { get; }
    internal LibrarySearchSnapshot(SqliteConnection connection, string songs, string query, bool projectsOnly)
    {
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
            string key = projectsOnly ? project : "json_extract(m.data,'$.Directory')";
            command.CommandText = $"INSERT INTO matches SELECT m.path,{key},json_extract(m.data,'$.Title'),{project} FROM maps m WHERE (m.root=$r OR m.root IN (SELECT path FROM external_sources))";
            command.Parameters.AddWithValue("$r", songs);
            var words = LibraryDatabase.Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                command.CommandText += $" AND instr(m.search,$q{i})>0";
                command.Parameters.AddWithValue("$q" + i, words[i]);
            }
            if (projectsOnly) command.CommandText += $" AND {project} IS NOT NULL";
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = "CREATE INDEX temp.matches_group ON matches(groupKey,path); "
                + "CREATE TEMP TABLE sets(position INTEGER PRIMARY KEY,groupKey TEXT,firstPath TEXT,title TEXT,project TEXT,count INTEGER); "
                + "INSERT INTO sets(groupKey,firstPath,title,project,count) SELECT groupKey,min(path),min(title),min(project),count(*) FROM matches GROUP BY groupKey ORDER BY min(title) COLLATE NOCASE,min(path); "
                + "CREATE UNIQUE INDEX temp.sets_key ON sets(groupKey);";
            command.ExecuteNonQuery();
            if (projectsOnly)
            {
                db.CreateFunction<string, bool>("matches_name", name => words.All(word => LibraryDatabase.Normalize(name).Contains(word)));
                command.CommandText = "INSERT INTO sets(groupKey,firstPath,title,project,count) SELECT path,path,name,path,1 FROM projects p WHERE NOT EXISTS(SELECT 1 FROM sets s WHERE s.groupKey=p.path) AND matches_name(name) ORDER BY name COLLATE NOCASE,path";
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
            rows.Add(new(reader.GetInt32(0), reader.GetString(1), map, reader.GetInt32(5)));
        }
        return rows;
    }
    public IReadOnlyList<LibraryMap> Difficulties(LibrarySetRow set, int start, int count = 64)
    {
        using var attachment = Attach();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT m.data FROM matches s JOIN maps m ON m.path=s.path WHERE s.groupKey=$k ORDER BY s.path LIMIT $n OFFSET $s";
        command.Parameters.AddWithValue("$k", set.Key); command.Parameters.AddWithValue("$n", Math.Clamp(count, 1, 128)); command.Parameters.AddWithValue("$s", Math.Max(0, start));
        var result = new List<LibraryMap>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<LibraryMap>(reader.GetString(0))! with { ProjectPath = set.Map.ProjectPath });
        if (result.Count == 0 && start == 0) result.Add(set.Map);
        return result;
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
    public void Dispose() => db.Dispose();
}
