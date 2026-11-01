using System.Collections.Concurrent;
using FruitsAtelier.Core;

internal static class ExportPathTests
{
    public static void Run()
    {
        foreach (int directoryLength in new[] { 85, 125, 221 }) WithFixture(directoryLength, (session, diff, songs, set) =>
        {
            string title = new string('T', 190);
            SongSetup.Set(diff.Document, "Metadata", "Title", title);
            var plan = WorkspaceExport.Plan(session, diff, songs, false, "Rain", false);
            Check(plan.Target.Length <= 259, "new export fits the legacy full-path limit");
            Observe(set, () => WorkspaceExport.Commit(session, plan), plan.Target);
            Check(OsuBeatmapReader.Setting(OsuBeatmapReader.ReadFile(plan.Target), "Metadata", "Title") == title, "shortening the filename preserves metadata");
            string standalone = Path.Combine(set, new string('f', 259 - set.Length - 1 - 4) + ".osu");
            Observe(set, () => OsuBeatmapWriter.WriteFile(diff.Document, standalone, false), standalone);
            File.Delete(standalone);
            diff.Document.Fruits[0].X++;
            plan = WorkspaceExport.Plan(session, diff, songs, true, "", false);
            Observe(set, () => WorkspaceExport.Commit(session, plan), plan.Target);
            Check(OsuBeatmapReader.ReadFile(plan.Target).Fruits[0].X == diff.Document.Fruits[0].X, "same-name update publishes content");

            SongSetup.Set(diff.Document, "Metadata", "Title", title + " changed");
            var entry = session.Manifest.Difficulties.Single();
            var merge = WorkspaceSynchronization.Merge(entry, diff.Document, WorkspaceSynchronization.ReadStable(plan.Target), session.Directory, true);
            var renamed = WorkspaceSynchronization.PlanLocalChanges(session, entry, merge);
            Check(renamed.Target.Length <= 259, "automatic metadata rename fits the path limit");
            Observe(set, () => WorkspaceExport.Commit(session, renamed), renamed.Target);
            Check(File.Exists(renamed.Target) && entry.Source == renamed.Target, "association follows the safe metadata filename");

            if (directoryLength > 85)
            {
                string first = WorkspaceExport.NewTarget(session, diff.Document, songs, "One");
                string second = WorkspaceExport.NewTarget(session, diff.Document, songs, "Two");
                Check(first != second && first == WorkspaceExport.NewTarget(session, diff.Document, songs, "One"), "shortened names distinguish difficulties and remain deterministic");
                SongSetup.Set(diff.Document, "Metadata", "Artist", string.Concat(Enumerable.Repeat("😀e\u0301", 80)));
                string unicode = Path.GetFileName(WorkspaceExport.NewTarget(session, diff.Document, songs, "Unicode"));
                Check(!unicode.EnumerateRunes().Any(r => r.Value == 0xfffd) && !unicode.Contains("e-"), "shortening preserves Unicode text elements");
            }
        });

        WithFixture(125, (session, diff, songs, set) =>
        {
            SongSetup.Set(diff.Document, "Metadata", "Title", new string('T', 190));
            var initial = WorkspaceExport.Plan(session, diff, songs, false, "Rain", false);
            WorkspaceExport.Commit(session, initial);
            byte[] before = File.ReadAllBytes(initial.Target);
            SongSetup.Set(diff.Document, "Metadata", "Title", new string('U', 190));
            var rename = WorkspaceExport.Plan(session, diff, songs, true, "", false);
            Directory.CreateDirectory(rename.Target);
            Observe(set, () => Reject(() => WorkspaceExport.Commit(session, rename)), initial.Target);
            Check(File.ReadAllBytes(initial.Target).SequenceEqual(before), "failed rename restores original bytes using safe temporary paths");
        });

        WithFixture(222, (session, diff, songs, set) =>
        {
            string original = File.ReadAllText(diff.Document.SourcePath!);
            Reject(() => WorkspaceExport.Plan(session, diff, songs, false, "Rain", false));
            Reject(() => WorkspaceExport.Plan(session, diff, songs, true, "", false));
            var output = OsuBeatmapWriter.Serialize(diff.Document);
            Reject(() => WorkspaceExport.Commit(session, new(diff.Id, diff.Document, Path.Combine(set, "new.osu"), null, output)));
            Reject(() => OsuBeatmapWriter.WriteFile(diff.Document, Path.Combine(set, "new.osu")));
            Check(File.ReadAllText(diff.Document.SourcePath!) == original && Directory.GetFiles(set).Length == 2, "unusable directories are rejected before publishing files");
        });

        WithFixture(125, (session, diff, songs, set) =>
        {
            string unsafeTarget = Path.Combine(set, new string('x', 140) + ".osu");
            Reject(() => OsuBeatmapWriter.WriteFile(diff.Document, unsafeTarget));
            Reject(() => WorkspaceExport.Commit(session, new(diff.Id, diff.Document, unsafeTarget, null, OsuBeatmapWriter.Serialize(diff.Document))));
            Check(!File.Exists(unsafeTarget), "unsafe explicit filenames are rejected before temporary file creation");
            File.Copy(diff.Document.SourcePath!, unsafeTarget);
            var entry = session.Manifest.Difficulties.Single();
            entry.Source = entry.ExportTarget = unsafeTarget;
            entry.SourceHash = entry.ExportHash = WorkspaceProject.Hash(unsafeTarget);
            Reject(() => WorkspaceExport.Plan(session, diff, songs, true, "", false));
            Reject(() => WorkspaceExport.Commit(session, new(diff.Id, diff.Document, unsafeTarget, entry.ExportHash, OsuBeatmapWriter.Serialize(diff.Document))));
            Check(WorkspaceProject.Hash(unsafeTarget) == entry.ExportHash, "an existing overlong linked filename is preserved on rejection");
        });

        WithFixture(85, (session, diff, songs, set) =>
        {
            session.Manifest.SourceDirectory = null;
            session.Manifest.Name = new string('S', 190);
            string target = WorkspaceExport.NewTarget(session, diff.Document, songs, "Rain");
            Check(Path.GetDirectoryName(target)!.Length <= 221 && target.Length <= 259, "generated mapset folders reserve room for temporary files");
            Check(target == WorkspaceExport.NewTarget(session, diff.Document, songs, "Rain"), "generated folders remain deterministic");
        });
        Console.WriteLine("PASS export path limits, watcher events, metadata renames, Unicode, rollback and rejection");
    }

    private static void Observe(string directory, Action action, string target)
    {
        var paths = new ConcurrentQueue<string>();
        using var published = new ManualResetEventSlim();
        using var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, e) => { paths.Enqueue(e.FullPath); if (e.FullPath == target) published.Set(); };
        watcher.Renamed += (_, e) => { paths.Enqueue(e.FullPath); if (e.FullPath == target) published.Set(); };
        watcher.EnableRaisingEvents = true;
        action();
        Check(published.Wait(TimeSpan.FromSeconds(5)), "watcher observed publication");
        Check(paths.Any(p => p.EndsWith(".tmp")), "watcher observed actual temporary files");
        Check(paths.All(p => p.Length <= 259 && Path.GetDirectoryName(p)!.Length < 248), "every created and renamed path is safe for the legacy watcher");
        Check(!Directory.GetFiles(directory, "*.tmp").Any(), "temporary files are cleaned up");
    }

    private static void WithFixture(int directoryLength, Action<WorkspaceSession, ProjectDifficulty, string, string> action)
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetFullPath("artifacts"), "ep-" + Guid.NewGuid().ToString("N")[..8]);
        string songs = Path.Combine(root, "Songs");
        string set = Path.Combine(songs, new string('s', Math.Max(1, directoryLength - songs.Length - 1)));
        Directory.CreateDirectory(set);
        try
        {
            string source = Path.Combine(set, "map.osu");
            File.WriteAllText(Path.Combine(set, "audio.mp3"), "fixture-audio");
            File.WriteAllText(source, "osu file format v14\n[General]\nAudioFilename:audio.mp3\nMode:2\n[Metadata]\nTitle:Short\nArtist:Artist\nCreator:Mapper\nVersion:Rain\n[Difficulty]\nCircleSize:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
            var project = BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]);
            var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), project, songs);
            session.Manifest.Difficulties.Single().ExportConfirmed = true;
            action(session, project.Difficulties[0], songs, set);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new Exception("Expected export rejection");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
