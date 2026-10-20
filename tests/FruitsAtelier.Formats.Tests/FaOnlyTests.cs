using FruitsAtelier.Core;

internal static class FaOnlyTests
{
    public static void Run()
    {
        foreach (string mode in new[] { "keep", "delete", "stale", "locked" })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/fa-only-model", mode, Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), folder = Path.Combine(songs, "set");
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "one.osu"), otherSource = Path.Combine(folder, "two.osu");
            const string text = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Title\nArtist:Artist\nCreator:Mapper\nVersion:One\n[Difficulty]\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n";
            File.WriteAllText(source, text); File.WriteAllText(otherSource, text.Replace("Version:One", "Version:Two"));
            var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), BeatmapProject.FromDocuments(
                [OsuBeatmapReader.ReadFile(source), OsuBeatmapReader.ReadFile(otherSource)]), songs);
            foreach (var entry in session.Manifest.Difficulties) entry.ExportConfirmed = true;
            WorkspaceProject.Save(session, session.Project);
            Guid id = session.Project.Difficulties[0].Id;
            Check(session.Project.Difficulties.All(d => !d.FaOnly) && session.Manifest.Difficulties.All(d => !d.FaOnly), "existing and new defaults enable synchronization");
            Check(!File.ReadAllText(Path.Combine(session.Directory, WorkspaceProject.ManifestName)).Contains("\"FaOnly\""), "default manifests keep their previous representation");
            var stalePlan = WorkspaceExport.Plan(session, session.Project.Difficulties[0], songs, true, "", true);
            string hash = WorkspaceProject.Hash(source);
            if (mode == "stale")
            {
                File.AppendAllText(source, "\n// external edit\n");
                Reject(() => WorkspaceDifficultySettings.SetFaOnly(session, session.Project, id, true, true, hash));
                Check(!WorkspaceProject.Open(session.Directory).Project.Difficulties[0].FaOnly && File.Exists(source), "a newer source cancels deletion and keeps sync enabled");
                continue;
            }
            // Unix permits unlinking an open file; FileShare only prevents deletion on Windows.
            if (mode == "locked" && !OperatingSystem.IsWindows()) continue;
            if (mode == "locked")
            {
                using (var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Reject(() => WorkspaceDifficultySettings.SetFaOnly(session, session.Project, id, true, true, hash));
                Check(!WorkspaceProject.Open(session.Directory).Project.Difficulties[0].FaOnly && !session.Manifest.Difficulties[0].FaOnly
                    && File.Exists(source), "deletion failure restores the saved and live sync policy");
                continue;
            }
            session = WorkspaceDifficultySettings.SetFaOnly(session, session.Project, id, true, mode == "delete", hash);
            Check(WorkspaceProject.Hash(otherSource) == WorkspaceSynchronization.Digest(text.Replace("Version:One", "Version:Two")), "other difficulty is untouched");
            Check(File.Exists(source) == (mode == "keep"), "only explicit deletion removes the linked osu file");
            if (mode == "delete")
            {
                string backup = WorkspaceVersionHistory.List(session).First(v => v.Operation == "fa-settings" && v.WorkingCopy).Path;
                Check(WorkspaceHistoryFile.ReadText(Path.Combine(Path.GetDirectoryName(backup)!, "external.osu")) == text, "deleted osu bytes remain in the operation backup");
                Check(WorkspaceSynchronization.Target(session.Manifest.Difficulties[0]) is null, "deleted external association is detached");
                Check(session.Project.Difficulties[0].Document.SourcePath == source, "detachment preserves the source directory for backgrounds and samples");
                Check(!WorkspaceProject.MissingResources(session.Project).Contains(source), "deleted source is not a required FA-only resource");
            }
            session = WorkspaceProject.Open(session.Directory);
            Check(session.Project.Difficulties[0].FaOnly && session.Manifest.Difficulties[0].FaOnly
                && !session.Project.Difficulties[1].FaOnly, "FA-only state survives restart and is difficulty-local");
            Check(WorkspaceProject.SnapshotManifest(session.Manifest).Difficulties[0].FaOnly
                && ProjectSerializer.ReadProject(ProjectSerializer.Serialize(session.Project)).Difficulties[0].FaOnly, "snapshots and portable projects retain the setting");
            Reject(() => WorkspaceExport.Plan(session, session.Project.Difficulties[0], songs, false, "Export", true));
            Reject(() => WorkspaceExport.Commit(session, stalePlan));
            if (mode == "keep") File.WriteAllText(source, text.Replace("100,192", "200,192"));
            var scan = WorkspaceSynchronization.Scan(session, songs);
            Check(scan.Difficulties.Single(d => d.DifficultyId == id).State == WorkspaceSyncState.Local && scan.Additions.Count == 0,
                "FA-only files are neither synchronized nor reimported as new difficulties");
            session.Project.Difficulties[0].Document.Fruits[0].X = 333;
            session = WorkspaceProject.SaveDifficulty(session, id, session.Project.Difficulties[0].Document, "One");
            Check(WorkspaceProject.Open(session.Directory).Project.Difficulties[0].FaOnly, "individual saves retain the sync policy");
            session = WorkspaceDifficultySettings.SetFaOnly(session, session.Project, id, false);
            Check(!WorkspaceProject.Open(session.Directory).Project.Difficulties[0].FaOnly, "synchronization can be enabled again");
            if (mode == "keep") Check(WorkspaceSynchronization.Scan(session, songs).Difficulties.Single(d => d.DifficultyId == id).State == WorkspaceSyncState.Changed,
                "reenabling sync reviews changes made while detached");
            else Check(WorkspaceExport.Plan(session, session.Project.Difficulties[0], songs, false, "One", true).ExpectedHash is null,
                "reenabling a deleted association can generate a new osu file");
            Console.WriteLine("PASS FA-only persistence, sync exclusion and " + mode + " file policy");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected FA-only export or deletion rejection.");
    }
}
