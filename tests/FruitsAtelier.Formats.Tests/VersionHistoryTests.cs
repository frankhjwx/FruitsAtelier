using FruitsAtelier.Core;

internal static class VersionHistoryTests
{
    public static void Run()
    {
        StoryboardAudioVersions();
        string workspace = Path.GetFullPath(Path.Combine("artifacts/tests/version-history", Guid.NewGuid().ToString("N")));
        var original = new MapDocument { IsDemo = false };
        original.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        original.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500 });
        SongSetup.Set(original, "Metadata", "Version", "Original");
        var deleted = original.DeepClone();
        SongSetup.Set(deleted, "Metadata", "Version", "Deleted");
        var project = BeatmapProject.FromDocuments([original, deleted]);
        var session = WorkspaceProject.Create(workspace, project, "");
        Check(WorkspaceVersionHistory.List(session).Count == 0, "initial publication has no previous version");
        project.Difficulties[0].Document.Fruits[0].X = 200;
        WorkspaceProject.Save(session, project);
        var first = WorkspaceVersionHistory.List(session).Single();
        Check(File.Exists(first.Path + WorkspaceHistoryFile.Extension) && !File.Exists(first.Path), "new saved versions are compressed binary files");
        Check(first.Operation == "save" && !first.WorkingCopy, "changed saves archive previous authoring");
        Check(WorkspaceVersionHistory.Read(session, first).Difficulties[0].Document.Fruits[0].X == 100, "old note position remains readable");
        byte[] legacy = WorkspaceHistoryFile.Read(first.Path);
        File.WriteAllBytes(first.Path, legacy);
        File.Delete(first.Path + WorkspaceHistoryFile.Extension);
        Check(WorkspaceVersionHistory.Read(session, first).Difficulties[0].Document.Fruits[0].X == 100, "legacy JSON versions remain readable");
        WorkspaceProject.Save(session, project);
        Check(WorkspaceVersionHistory.List(session).Count == 1, "unchanged saves create no extra version");
        project.Difficulties[0].Document.Fruits[0].X = 300;
        WorkspaceVersionHistory.ArchiveCurrent(session, project);
        var captured = WorkspaceVersionHistory.List(session).First(v => v.WorkingCopy);
        Check(WorkspaceVersionHistory.Read(session, captured).Difficulties[0].Document.Fruits[0].X == 300, "pre-restore snapshot preserves unsaved content");
        Guid deletedId = project.Difficulties[1].Id;
        WorkspaceAssociations.DeleteDifficulty(session, project, deletedId);
        session = WorkspaceProject.Open(session.Directory);
        Check(session.Project.Difficulties.Count == 1, "difficulty deleted");
        var deletion = WorkspaceVersionHistory.List(session).First(v => v.Operation == "delete" && v.WorkingCopy);
        Check(WorkspaceVersionHistory.Read(session, deletion).Difficulties.Any(d => d.Id == deletedId), "deleted difficulty remains available by identity");
        var historical = WorkspaceVersionHistory.Read(session, first).Difficulties[0].Document;
        OsuTimeline.AddBreak(historical, 2000, 3000);
        var history = new EditorHistory(session.Project.Difficulties[0].Document);
        var before = history.Document.DeepClone();
        history.RestoreVersion("restore", historical);
        Check(history.Document.ContentEquals(historical) && history.CanUndo, "complete historical authoring is restored in one transaction");
        history.Undo(); Check(history.Document.ContentEquals(before), "restore is undoable");
        history.Redo(); Check(history.Document.ContentEquals(historical), "restore is redoable");
        var unrelated = WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments([original.DeepClone()]), "");
        Check(WorkspaceVersionHistory.List(unrelated).Count == 0, "history is scoped to project identity");
        try { WorkspaceVersionHistory.Read(unrelated, first); throw new Exception("cross-project history accepted"); }
        catch (InvalidDataException) { }
        string invalid = Path.Combine(Path.GetDirectoryName(first.Path)!, "current.catchproj");
        File.WriteAllText(invalid, "damaged");
        Check(WorkspaceVersionHistory.List(session).Any(v => v.Path == invalid), "damaged snapshots do not hide other history");
        try { WorkspaceVersionHistory.Read(session, first with { Path = invalid }); throw new Exception("damaged version accepted"); }
        catch (InvalidDataException) { }
    }

    private static void StoryboardAudioVersions()
    {
        foreach (bool difficultyOnly in new[] { false, true })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/version-history-audio", Guid.NewGuid().ToString("N")));
            string workspace = Path.Combine(root, "workspace"), media = Path.Combine(workspace, "Resources", "media");
            Directory.CreateDirectory(media);
            string source = Path.Combine(root, "source.wav");
            byte[] bytes = [82, 73, 70, 70, 1, 2, 3, 4];
            File.WriteAllBytes(source, bytes);
            var audio = StoryboardAudio.Capture(source);
            var document = new MapDocument { SourcePath = Path.Combine(media, WorkspaceProject.ResourceRootMarker) };
            var events = new OsuSection { Name = "Events" };
            events.Lines.Add($"Sample,1000,0,\"{audio.RelativePath}\",100");
            document.OriginalSections.Add(events); document.StoryboardAudio[audio.RelativePath] = audio;
            var project = BeatmapProject.FromDocuments([document]);
            var session = WorkspaceProject.Create(workspace, project, "");
            string managed = Path.Combine(media, audio.RelativePath);
            Check(File.ReadAllBytes(managed).SequenceEqual(bytes), "initial save materializes storyboard audio");
            File.Delete(source);
            session = WorkspaceProject.Open(session.Directory); project = session.Project;
            var difficulty = project.Difficulties.Single();
            Check(difficulty.Document.StoryboardAudio.Single().Value.Data is null, "reopened resources begin without an encoded cache");
            difficulty.Document.OriginalSections.Single(s => s.Name == "Events").Lines.Clear();
            if (difficultyOnly) session = WorkspaceProject.SaveDifficulty(session, difficulty.Id, difficulty.Document, difficulty.Name);
            else WorkspaceProject.Save(session, project);
            Check(!File.Exists(managed), "history preservation retains final-reference cleanup in the mapset");
            var version = WorkspaceVersionHistory.List(session).Single();
            var historical = WorkspaceVersionHistory.Read(session, version);
            var historicalAudio = historical.Difficulties.Single().Document.StoryboardAudio.Single().Value;
            Check(historicalAudio.Data is null, "opening saved history defers audio loading");
            historicalAudio.Cache(managed);
            Check(historicalAudio.Data!.SequenceEqual(bytes), "saved historical audio survives source deletion and mapset cleanup");
            WorkspaceStorage.Clean(workspace, compactLegacy: true);
            historical = WorkspaceVersionHistory.Read(session, version);
            WorkspaceProject.Save(session, historical);
            Check(File.ReadAllBytes(managed).SequenceEqual(bytes), "restoring a saved version materializes its retained audio after history compression");

            string unsavedSource = Path.Combine(root, "unsaved.wav"); byte[] unsavedBytes = [82, 73, 70, 70, 5, 6, 7, 8];
            File.WriteAllBytes(unsavedSource, unsavedBytes);
            var unsaved = StoryboardAudio.Capture(unsavedSource);
            var current = historical.Difficulties.Single();
            current.Document.StoryboardAudio[unsaved.RelativePath] = unsaved;
            current.Document.OriginalSections.Single(s => s.Name == "Events").Lines.Add($"Sample,2000,0,\"{unsaved.RelativePath}\",100");
            File.Delete(unsavedSource);
            if (difficultyOnly) WorkspaceVersionHistory.ArchiveCurrentDifficulty(session, historical, current.Id);
            else WorkspaceVersionHistory.ArchiveCurrent(session, historical);
            var working = WorkspaceVersionHistory.List(session).First(v => v.WorkingCopy);
            var restoredCurrent = WorkspaceVersionHistory.Read(session, working);
            Check(restoredCurrent.Difficulties.Single().Document.StoryboardAudio[unsaved.RelativePath].Data is null, "opening working-copy history defers audio loading");
            WorkspaceProject.Save(session, restoredCurrent);
            Check(File.ReadAllBytes(Path.Combine(media, unsaved.RelativePath)).SequenceEqual(unsavedBytes), "working-copy restoration materializes previously unsaved audio");
            string oldRound = Path.GetDirectoryName(version.Path)!;
            Check(Directory.EnumerateFiles(Path.Combine(oldRound, "storyboard-audio"), "*", SearchOption.AllDirectories).Any(), "saved round owns its storyboard resources");
            WorkspaceStorage.Clean(workspace, utcNow: DateTime.UtcNow.AddDays(32),
                protectedPaths: [historical.Difficulties.Single().Document.StoryboardAudio[audio.RelativePath].HistoryPath!]);
            Check(Directory.Exists(oldRound), "live lazy audio references protect their history round");
            WorkspaceStorage.Clean(workspace, utcNow: DateTime.UtcNow.AddDays(32));
            Check(!Directory.Exists(oldRound), "retention reclaims archived storyboard bytes with their expired round");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
