using FruitsAtelier.Core;
using System.Text.Json;

internal static class WorkspaceStorageTests
{
    public static void Run()
    {
        DateTime now = new(2030, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/storage", Guid.NewGuid().ToString("N")));
        string workspace = Path.Combine(root, "workspace"), history = Path.Combine(workspace, ".sync-history");
        string active = new('A', 64), historical = new('B', 64), orphan = new('C', 64), fresh = new('D', 64);
        string Write(string relative, string text, int age = 60)
        {
            string path = Path.Combine(workspace, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text); File.SetLastWriteTimeUtc(path, now.AddDays(-age)); return path;
        }
        string Snapshot(Guid id, int age, string content = "{}")
        {
            string relative = Path.Combine(".sync-history", id.ToString("N"), now.AddDays(-age).ToString("yyyyMMddTHHmmssfffffff") + "-resolution", "current.catchproj");
            return Path.GetDirectoryName(Write(relative, content, age))!;
        }
        string objectLine = "240,352,197071,6,0,B|" + string.Join("|", Enumerable.Repeat("238:352", 6000)) + ",0.5";
        Write("project/source-lines.catchdiff", JsonSerializer.Serialize(new { OriginalSourceLine = objectLine, Name = "{not JSON", Tags = "a/b.c" }));
        Write("project/embedded.catchsync", JsonSerializer.Serialize(new { Authoring = JsonSerializer.Serialize(new { OriginalSections = new[] { new { Lines = new[] { objectLine } } } }) }));
        Write("project/project.catchsync", JsonSerializer.Serialize(new { AuthoringAudioHash = active }));
        string activeAudio = Write(".sync-history/resources/" + active, "active audio");
        string oldAudio = Write(".sync-history/resources/" + historical, "retained history audio");
        string unusedAudio = Write(".sync-history/resources/" + orphan, "unused audio");
        string freshAudio = Write(".sync-history/resources/" + fresh, "unpublished baseline", 0);
        string unusedPlayback = Write(".sync-history/resources/playback/" + active + ".mp3", "unused playback copy");
        string usedPlayback = Write(".sync-history/resources/playback/" + historical + ".mp3", "referenced playback copy");
        Write("project/playback.catchdiff", JsonSerializer.Serialize(new { AudioPath = "../.sync-history/resources/playback/" + historical + ".mp3" }));
        Guid id = Guid.NewGuid();
        var versions = Enumerable.Range(2, 15).Select(age => Snapshot(id, age)).ToArray();
        string lastCopy = Snapshot(Guid.NewGuid(), 60, JsonSerializer.Serialize(new { AudioHash = historical }));
        string old = Snapshot(id, 80, JsonSerializer.Serialize(new { AudioPath = "audio.mp3" })), pinned = Snapshot(id, 90);
        Write("project/author.catchdiff", JsonSerializer.Serialize(new { AudioPath = Path.Combine(pinned, "audio.mp3") }));
        string asset = Write("Resources/song/audio.mp3", "user resource");
        string skin = Write("Skins/user/skin.ini", "user skin");
        var before = WorkspaceStorage.Inspect(workspace);
        Check(before.TotalBytes == before.Categories.Sum(c => c.Bytes) && before.TotalBytes == before.Folders.Sum(f => f.Bytes), "storage totals and folder shares agree");
        var report = WorkspaceStorage.Clean(workspace, utcNow: now);
        Check(report.ReclaimedBytes > 0 && before.TotalBytes - report.TotalBytes == report.ReclaimedBytes, "reclaimed bytes match actual file sizes");
        Check(versions.Take(10).All(Directory.Exists) && versions.Skip(10).All(p => !Directory.Exists(p)), "ten newest historical versions retained");
        Check(!Directory.Exists(old) && Directory.Exists(lastCopy) && Directory.Exists(pinned), "latest copy and referenced snapshots survive age expiry");
        Check(File.Exists(activeAudio) && File.Exists(oldAudio) && File.Exists(freshAudio) && !File.Exists(unusedAudio), "active, retained and unpublished audio protected; orphan reclaimed");
        Check(!File.Exists(unusedPlayback) && File.Exists(usedPlayback), "unused playback copies are reclaimed independently of retained canonical audio");
        Check(File.Exists(asset) && File.Exists(skin), "imported music and skins are not caches");
        Check(WorkspaceStorage.Clean(workspace, utcNow: now).ReclaimedBytes == 0, "cleanup is idempotent");
        string review = Write(".sync-history/reviews/review.txt", "temporary comparison", 0);
        string stale = Snapshot(id, 100);
        WorkspaceStorage.Clean(workspace, clearCache: true, utcNow: now);
        Check(!File.Exists(review) && Directory.Exists(stale), "cache clearing leaves version history intact");
        string receipt = Write(".sync-history/pending/export.json", "{}");
        Check(WorkspaceStorage.Clean(workspace, utcNow: now).RecoveryPending && Directory.Exists(stale), "pending export pauses cleanup");
        File.Delete(receipt);
        string deletion = Write("project/delete.json", "{}");
        Check(WorkspaceStorage.Clean(workspace, utcNow: now).RecoveryPending && Directory.Exists(stale), "pending deletion pauses cleanup");
        File.Delete(deletion);
        string broken = Write("project/broken.catchdiff", "{invalid");
        try { WorkspaceStorage.Clean(workspace, utcNow: now); throw new Exception("corrupt references accepted"); }
        catch (JsonException) { Check(Directory.Exists(stale), "corrupt references prevent any deletion"); }
        File.Delete(broken);
        string invalidPath = Write("project/invalid-path.catchdiff", JsonSerializer.Serialize(new { AudioPath = "\0" }));
        try { WorkspaceStorage.Clean(workspace, utcNow: now); throw new Exception("invalid resource path accepted"); }
        catch (InvalidDataException) { Check(Directory.Exists(stale), "invalid real path fields abort before deletion"); }
        File.Delete(invalidPath);
        string liveAudio = Write(".sync-history/resources/playback/" + new string('F', 64) + ".mp3", "unsaved open audio");
        WorkspaceStorage.Clean(workspace, utcNow: now, protectedPaths: [liveAudio]);
        Check(File.Exists(liveAudio), "open document audio is protected before its reference is saved");

        string external = Path.Combine(root, "external"); Directory.CreateDirectory(external);
        string map = Path.Combine(external, "map.osu");
        File.WriteAllText(map, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Storage test\nArtist:A\nCreator:C\nVersion:V\n[Difficulty]\nCircleSize:5\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
        string catalogRoot = Path.Combine(root, "catalog");
        var database = new LibraryDatabase(catalogRoot, ""); database.RegisterSource(external); database.Scan();
        Check(database.Search("").Count == 1, "external source indexed");
        database.ClearDerivedCache(); Check(database.Search("").Count == 0, "derived index cleared");
        database.Scan(); Check(database.Search("").Count == 1 && File.Exists(map), "source registration survives cache rebuilding");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
