using FruitsAtelier.Core;

internal static class VersionHistoryTests
{
    public static void Run()
    {
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

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
